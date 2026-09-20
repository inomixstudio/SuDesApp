using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IArsipSuratRepository
    {
        Task<List<SuratKeluarMasukData>> GetAllAsync();
        Task<SuratKeluarMasukData> AddAsync(SuratKeluarMasukData item);
        Task UpdateAsync(SuratKeluarMasukData item);
        Task DeleteAsync(int idBarisExcel);

        /// <summary>Path penyimpanan berkas lampiran baru (PDF/gambar) untuk satu arsip.</summary>
        (string FileName, string FullPath) ResolveLampiranStorage(int id, string jenisSurat, string nomorSurat, string sourceExtension);

        /// <summary>Path lengkap berkas lampiran tersimpan, atau null bila tidak ada.</summary>
        string? ResolveLampiranFullPath(string? fileName);

        /// <summary>
        /// Impor data dari file Excel ArsipSurat.xlsx (arsip lama) ke database SQLite.
        /// Baris dengan Id yang sudah ada dilewati (aman dipanggil ulang).
        /// Mengembalikan jumlah baris yang berhasil diimpor.
        /// </summary>
        Task<int> ImportFromExcelAsync(string? excelPath = null);
    }

    /// <summary>
    /// Akses data buku agenda Surat Masuk/Keluar yang tersimpan di database
    /// SQLite (tabel ArsipSurat pada desa.db) — pengganti file Excel
    /// ArsipSurat.xlsx agar lebih tangguh (transaksional, tanpa file terkunci,
    /// ID auto-increment tahan celah, tanpa parsing tanggal rapuh).
    /// Padanan logika Excel pada SuratKeluarMasuk.cs dan InputKeluarMasuk.cs (WinForms).
    ///
    /// Kompatibilitas: saat peluncuran pertama (tabel masih kosong dan belum ada
    /// penanda impor), seluruh isi ArsipSurat.xlsx lama diimpor otomatis,
    /// termasuk Id baris aslinya. Berkas Excel lama tidak diubah, sehingga bisa
    /// dipakai lagi sebagai sumber impor lewat ImportFromExcelAsync.
    /// Berkas lampiran (PDF/gambar) tersimpan di folder ArsipSuratFiles.
    /// </summary>
    public class ArsipSuratRepository : IArsipSuratRepository
    {
        private readonly SqliteConnection _connection;
        private readonly string _legacyExcelPath;
        private readonly string _lampiranFolder;
        private readonly ILogger<ArsipSuratRepository> _logger;
        private readonly ActivityLogService? _activityLog;

        /// <summary>Inisialisasi tabel + impor legacy hanya sekali per proses.</summary>
        private static bool _initialized;
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        static ArsipSuratRepository() => ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

        public ArsipSuratRepository(
            SqliteConnection connection,
            AppConfig appConfig,
            FileService fileService,
            ILogger<ArsipSuratRepository> logger,
            ActivityLogService? activityLog = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _legacyExcelPath = fileService.SanitizePath(Path.Combine(appConfig.TemplateFolder, "ArsipSurat.xlsx"));
            _lampiranFolder = fileService.SanitizePath(Path.Combine(appConfig.TemplateFolder, "ArsipSuratFiles"));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityLog = activityLog;

            if (!Directory.Exists(_lampiranFolder)) Directory.CreateDirectory(_lampiranFolder);
        }

        // =====================================================================
        // Inisialisasi skema + impor legacy sekali jalan
        // =====================================================================

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized) return;

                await EnsureOpenAsync().ConfigureAwait(false);
                await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ArsipSurat (
                    Id                     INTEGER PRIMARY KEY,
                    JenisSurat             TEXT NOT NULL,
                    NomorSurat             TEXT NOT NULL,
                    TanggalSurat           TEXT NOT NULL,
                    TanggalDiterimaDikirim TEXT,
                    AsalTujuan             TEXT,
                    Perihal                TEXT,
                    IsiRingkas             TEXT,
                    Keterangan             TEXT,
                    FileLampiran           TEXT,
                    DibuatAt               TEXT NOT NULL DEFAULT (datetime('now','localtime')));
                    CREATE INDEX IF NOT EXISTS idx_arsipsurat_jenis   ON ArsipSurat(JenisSurat);
                    CREATE INDEX IF NOT EXISTS idx_arsipsurat_tanggal ON ArsipSurat(TanggalSurat);
                    CREATE TABLE IF NOT EXISTS ArsipSuratMeta (
                    Kunci  TEXT PRIMARY KEY,
                    Nilai  TEXT);").ConfigureAwait(false);

                await EnsureColumnLampiranAsync().ConfigureAwait(false);

                // Impor legacy sekali saja (ditandai di tabel meta agar penghapusan
                // data oleh pengguna tidak memicu impor ulang).
                long sudah = await _connection.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM ArsipSuratMeta WHERE Kunci = 'ImportedFromExcel'")
                    .ConfigureAwait(false);
                if (sudah == 0)
                {
                    int n = await ImportFromExcelInternalAsync(_legacyExcelPath).ConfigureAwait(false);
                    await _connection.ExecuteAsync(
                        "INSERT OR REPLACE INTO ArsipSuratMeta (Kunci, Nilai) VALUES ('ImportedFromExcel', '1')")
                        .ConfigureAwait(false);
                    if (n > 0)
                    {
                        _logger.LogInformation("Impor legacy ArsipSurat.xlsx selesai: {Count} baris dimasukkan ke SQLite.", n);
                    }
                }

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Migrasi aman untuk tabel yang sudah dibuat versi lama (tanpa kolom
        /// FileLampiran): tambahkan kolomnya bila belum ada.
        /// </summary>
        private async Task EnsureColumnLampiranAsync()
        {
            long ada = await _connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_table_info('ArsipSurat') WHERE name = 'FileLampiran'")
                .ConfigureAwait(false);
            if (ada == 0)
            {
                await _connection.ExecuteAsync("ALTER TABLE ArsipSurat ADD COLUMN FileLampiran TEXT")
                    .ConfigureAwait(false);
                _logger.LogInformation("Kolom ArsipSurat.FileLampiran ditambahkan (migrasi tabel lama).");
            }
        }

        private async Task EnsureOpenAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>Eksekusi aksi tulis dengan retry bila database sempat terkunci.</summary>
        private async Task<T> WriteWithRetryAsync<T>(Func<Task<T>> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await EnsureOpenAsync().ConfigureAwait(false);
                    return await action().ConfigureAwait(false);
                }
                catch (SqliteException ex) when (attempt < 3 && (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6))
                {
                    _logger.LogWarning(ex, "Database terkunci (percobaan {Attempt}/3), retry...", attempt);
                    await Task.Delay(200 * attempt).ConfigureAwait(false);
                }
            }
        }

        // =====================================================================
        // Operasi data
        // =====================================================================

        public async Task<List<SuratKeluarMasukData>> GetAllAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            const string sql = @"SELECT Id AS IdBarisExcel, JenisSurat, NomorSurat, TanggalSurat,
                                        TanggalDiterimaDikirim, AsalTujuan, Perihal, IsiRingkas, Keterangan,
                                        FileLampiran
                                 FROM ArsipSurat
                                 ORDER BY Id";
            var rows = await _connection.QueryAsync<Row>(sql).ConfigureAwait(false);
            return rows.Select(r => new SuratKeluarMasukData
            {
                IdBarisExcel = r.IdBarisExcel,
                JenisSurat = (r.JenisSurat ?? "").Trim(),
                NomorSurat = (r.NomorSurat ?? "").Trim(),
                TanggalSurat = ParseTanggal(r.TanggalSurat),
                TanggalDiterimaDikirim = ParseTanggalOrNull(r.TanggalDiterimaDikirim),
                AsalTujuan = (r.AsalTujuan ?? "").Trim(),
                Perihal = (r.Perihal ?? "").Trim(),
                IsiRingkas = (r.IsiRingkas ?? "").Trim(),
                Keterangan = (r.Keterangan ?? "").Trim(),
                FileLampiran = string.IsNullOrWhiteSpace(r.FileLampiran) ? null : r.FileLampiran!.Trim()
            }).ToList();
        }

        public async Task<SuratKeluarMasukData> AddAsync(SuratKeluarMasukData item)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await WriteWithRetryAsync(async () =>
            {
                const string sql = @"INSERT INTO ArsipSurat (JenisSurat, NomorSurat, TanggalSurat, TanggalDiterimaDikirim, AsalTujuan, Perihal, IsiRingkas, Keterangan, FileLampiran)
                                     VALUES (@JenisSurat, @NomorSurat, @TanggalSurat, @TanggalDiterimaDikirim, @AsalTujuan, @Perihal, @IsiRingkas, @Keterangan, @FileLampiran);
                                     SELECT last_insert_rowid();";
                long id = await _connection.ExecuteScalarAsync<long>(sql, new
                {
                    item.JenisSurat,
                    item.NomorSurat,
                    TanggalSurat = ToIso(item.TanggalSurat),
                    TanggalDiterimaDikirim = ToIsoOrNull(item.TanggalDiterimaDikirim),
                    item.AsalTujuan,
                    item.Perihal,
                    item.IsiRingkas,
                    item.Keterangan,
                    FileLampiran = string.IsNullOrWhiteSpace(item.FileLampiran) ? null : item.FileLampiran!.Trim()
                }).ConfigureAwait(false);
                item.IdBarisExcel = (int)id;
                return id;
            }).ConfigureAwait(false);

            _logger.LogInformation("Arsip surat ditambahkan: Id={Id} Nomor={Nomor}", item.IdBarisExcel, item.NomorSurat);

            // Riwayat aktivitas: catat pembuat arsip surat masuk/keluar.
            _activityLog?.Log($"Agenda {item.JenisSurat}", item.NomorSurat, "Buat", $"Perihal: {item.Perihal}");
            return item;
        }

        public async Task UpdateAsync(SuratKeluarMasukData item)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            int affected = await WriteWithRetryAsync(async () =>
                await _connection.ExecuteAsync(@"UPDATE ArsipSurat
                         SET JenisSurat = @JenisSurat, NomorSurat = @NomorSurat,
                             TanggalSurat = @TanggalSurat, TanggalDiterimaDikirim = @TanggalDiterimaDikirim,
                             AsalTujuan = @AsalTujuan, Perihal = @Perihal, IsiRingkas = @IsiRingkas, Keterangan = @Keterangan,
                             FileLampiran = @FileLampiran
                         WHERE Id = @Id", new
                {
                    item.JenisSurat,
                    item.NomorSurat,
                    TanggalSurat = ToIso(item.TanggalSurat),
                    TanggalDiterimaDikirim = ToIsoOrNull(item.TanggalDiterimaDikirim),
                    item.AsalTujuan,
                    item.Perihal,
                    item.IsiRingkas,
                    item.Keterangan,
                    FileLampiran = string.IsNullOrWhiteSpace(item.FileLampiran) ? null : item.FileLampiran!.Trim(),
                    Id = item.IdBarisExcel
                }).ConfigureAwait(false)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException($"Data id={item.IdBarisExcel} tidak ditemukan di database.");
            }
            _logger.LogInformation("Arsip surat diperbarui: Id={Id}", item.IdBarisExcel);

            // Riwayat aktivitas: catat pengedit arsip surat masuk/keluar.
            _activityLog?.Log($"Agenda {item.JenisSurat}", item.NomorSurat, "Edit", $"Perihal: {item.Perihal}");
        }

        public async Task DeleteAsync(int idBarisExcel)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await WriteWithRetryAsync(async () =>
            {
                var fileLampiran = await _connection.ExecuteScalarAsync<string?>(
                    "SELECT FileLampiran FROM ArsipSurat WHERE Id = @Id", new { Id = idBarisExcel })
                    .ConfigureAwait(false);

                int affected = await _connection.ExecuteAsync(
                    "DELETE FROM ArsipSurat WHERE Id = @Id", new { Id = idBarisExcel })
                    .ConfigureAwait(false);
                if (affected > 0)
                {
                    _logger.LogInformation("Arsip surat dihapus: Id={Id}", idBarisExcel);

                    // Riwayat aktivitas: catat penghapus arsip surat.
                    _activityLog?.Log("Agenda Surat", $"ID {idBarisExcel}", "Hapus");

                    // Hapus berkas lampiran terkait agar tidak menumpuk berkas yatim.
                    // Kegagalan hapus berkas tidak menggagalkan penghapusan data.
                    if (!string.IsNullOrWhiteSpace(fileLampiran))
                    {
                        try
                        {
                            var path = ResolveLampiranFullPath(fileLampiran);
                            if (path != null)
                            {
                                File.Delete(path);
                                _logger.LogInformation("Berkas lampiran terkait dihapus: {File}", fileLampiran);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Gagal menghapus berkas lampiran terkait: {File}", fileLampiran);
                        }
                    }
                }
                return affected;
            }).ConfigureAwait(false);
        }

        // =====================================================================
        // Impor Excel legacy
        // =====================================================================

        /// <summary>
        /// Impor dari Excel arsip lama (bisa dipanggil ulang; baris dengan Id yang
        /// sudah ada dilewati). excelPath kosong berarti path legacy bawaan.
        /// </summary>
        public async Task<int> ImportFromExcelAsync(string? excelPath = null)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            int n = await WriteWithRetryAsync(() =>
                ImportFromExcelInternalAsync(string.IsNullOrWhiteSpace(excelPath) ? _legacyExcelPath : excelPath!))
                .ConfigureAwait(false);
            _logger.LogInformation("Impor manual dari {Path}: {Count} baris baru dimasukkan.", excelPath ?? _legacyExcelPath, n);
            return n;
        }

        private async Task<int> ImportFromExcelInternalAsync(string excelPath)
        {
            var rows = ReadLegacyExcel(excelPath);
            if (rows.Count == 0) return 0;

            int imported = 0;
            foreach (var r in rows)
            {
                imported += await _connection.ExecuteAsync(@"INSERT OR IGNORE INTO ArsipSurat
                        (Id, JenisSurat, NomorSurat, TanggalSurat, TanggalDiterimaDikirim, AsalTujuan, Perihal, IsiRingkas, Keterangan)
                        VALUES (@Id, @JenisSurat, @NomorSurat, @TanggalSurat, @TanggalDiterimaDikirim, @AsalTujuan, @Perihal, @IsiRingkas, @Keterangan)", r)
                    .ConfigureAwait(false);
            }
            return imported;
        }

        private List<object> ReadLegacyExcel(string excelPath)
        {
            var result = new List<object>();
            try
            {
                var fileInfo = new FileInfo(excelPath);
                if (!fileInfo.Exists) return result;

                using var package = new ExcelPackage(fileInfo);
                var ws = package.Workbook.Worksheets["Arsip Surat"];
                if (ws?.Dimension == null) return result;

                for (int row = 2; row <= ws.Dimension.End.Row; row++)
                {
                    if (!int.TryParse((ws.Cells[row, 1].Text ?? "").Trim(), out int id) || id <= 0) continue;

                    result.Add(new
                    {
                        Id = id,
                        JenisSurat = (ws.Cells[row, 2].Text ?? "").Trim(),
                        NomorSurat = (ws.Cells[row, 3].Text ?? "").Trim(),
                        // TanggalSurat wajib; parsing toleran seperti kolom lain
                        // (format lama dd-MM-yyyy, sekarang juga dd/MM/yyyy, ISO, serial Excel).
                        TanggalSurat = ToIso(ParseTanggalCell(ws.Cells[row, 4])),
                        TanggalDiterimaDikirim = ToIsoOrNull(ParseTanggalCellOrNull(ws.Cells[row, 5])),
                        AsalTujuan = (ws.Cells[row, 6].Text ?? "").Trim(),
                        Perihal = (ws.Cells[row, 7].Text ?? "").Trim(),
                        IsiRingkas = (ws.Cells[row, 8].Text ?? "").Trim(),
                        Keterangan = (ws.Cells[row, 9].Text ?? "").Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca Excel legacy dari {Path} (impor dilewati)", excelPath);
            }
            return result;
        }

        // =====================================================================
        // Utilitas
        // =====================================================================

        private class Row
        {
            public int IdBarisExcel { get; set; }
            public string JenisSurat { get; set; } = "";
            public string NomorSurat { get; set; } = "";
            public string TanggalSurat { get; set; } = "";
            public string? TanggalDiterimaDikirim { get; set; }
            public string AsalTujuan { get; set; } = "";
            public string Perihal { get; set; } = "";
            public string IsiRingkas { get; set; } = "";
            public string Keterangan { get; set; } = "";
            public string? FileLampiran { get; set; }
        }

        private static string ToIso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string? ToIsoOrNull(DateTime? d) =>
            d.HasValue ? ToIso(d.Value) : null;

        private static DateTime ParseTanggal(string iso)
        {
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return DateTime.Today;
        }

        private static DateTime? ParseTanggalOrNull(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return null;
        }

        /// <summary>Parsing tanggal Excel toleran: dd-MM-yyyy, dd/MM/yyyy, ISO, nama bulan Indonesia, serial Excel.</summary>
        private static DateTime? ParseTanggalCellOrNull(ExcelRange cell)
        {
            var text = (cell.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                string[] formats = { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy" };
                if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt;
                if (DateTime.TryParse(text, new CultureInfo("id-ID"), DateTimeStyles.None, out dt))
                    return dt;
            }
            try
            {
                if (cell.Value is double d && d > 0) return DateTime.FromOADate(d);
            }
            catch { /* bukan tanggal serial */ }
            return null;
        }

        private static DateTime ParseTanggalCell(ExcelRange cell) =>
            ParseTanggalCellOrNull(cell) ?? DateTime.Today;

        // =====================================================================
        // Berkas lampiran (PDF / gambar)
        // =====================================================================

        /// <summary>
        /// Nama + path penyimpanan berkas lampiran baru untuk satu arsip.
        /// Hanya ekstensi PDF/gambar yang diterima (lihat
        /// <see cref="LampiranArsipSurat"/>); berkas lain ditolak agar folder
        /// arsip tidak terisi format yang tidak bisa dibuka.</summary>
        public (string FileName, string FullPath) ResolveLampiranStorage(int id, string jenisSurat, string nomorSurat, string sourceExtension)
        {
            string ext = LampiranArsipSurat.EkstensiTerdukung(sourceExtension);
            if (ext.Length == 0)
            {
                throw new InvalidOperationException(LampiranArsipSurat.PesanTidakDidukung(sourceExtension));
            }

            if (!Directory.Exists(_lampiranFolder)) Directory.CreateDirectory(_lampiranFolder);

            var jenis = SanitizePart(jenisSurat ?? "SURAT");
            var safeNomor = SanitizeFileName(string.IsNullOrWhiteSpace(nomorSurat) ? id.ToString() : nomorSurat.Trim());
            var fileName = $"{jenis}_{id}_{safeNomor}{ext}";
            return (fileName, Path.Combine(_lampiranFolder, fileName));
        }

        /// <summary>Path lengkap berkas lampiran tersimpan, atau null bila tidak ada.</summary>
        public string? ResolveLampiranFullPath(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            // Jaga agar tidak lolos direktori.
            if (Path.IsPathRooted(fileName) || fileName.Contains("..")) return null;
            var full = Path.Combine(_lampiranFolder, fileName);
            return File.Exists(full) ? full : null;
        }

        /// <summary>Folder penyimpanan berkas lampiran agenda surat.</summary>
        public string LampiranFolder => _lampiranFolder;

        private static string SanitizeFileName(string name) =>
            string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        private static string SanitizePart(string part) => SanitizeFileName(part.Replace("/", "_").Replace("\\", "_"));
    }
}
