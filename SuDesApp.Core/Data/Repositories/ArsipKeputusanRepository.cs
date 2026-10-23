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
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IArsipKeputusanRepository
    {
        Task<List<DataKeputusan>> GetAllAsync();
        Task<DataKeputusan> AddAsync(DataKeputusan item);
        Task UpdateAsync(DataKeputusan item);
        Task DeleteAsync(int idBarisExcel);

        /// <summary>Nama file & path lengkap untuk menyimpan file Word/PDF yang diimport.</summary>
        (string FileName, string FullPath) ResolveWordStorage(int id, string jenisKeputusan, string nomor, string sourceExtension);
        /// <summary>Path lengkap file Word/PDF tersimpan (atau null bila belum ada).</summary>
        string? ResolveWordFullPath(string? fileName);

        /// <summary>
        /// Impor data dari file Excel ArsipKeputusan.xlsx (arsip lama) ke database SQLite.
        /// Baris dengan Id yang sudah ada dilewati (aman dipanggil ulang).
        /// Mengembalikan jumlah baris yang berhasil diimpor.
        /// </summary>
        Task<int> ImportFromExcelAsync(string? excelPath = null);
    }

    /// <summary>
    /// Akses data buku SK / Peraturan (SK, Perdes, Perkades) yang tersimpan di
    /// database SQLite (tabel ArsipKeputusan pada desa.db) — pengganti file
    /// Excel ArsipKeputusan.xlsx agar lebih tangguh (transaksional, tanpa file
    /// terkunci, ID auto-increment tahan celah, tanpa parsing tanggal rapuh).
    ///
    /// Kompatibilitas: saat peluncuran pertama (tabel masih kosong dan belum ada
    /// penanda impor), seluruh isi ArsipKeputusan.xlsx lama diimpor otomatis,
    /// termasuk Id baris aslinya. Berkas Excel lama tidak diubah, sehingga bisa
    /// dipakai lagi sebagai sumber impor lewat ImportFromExcelAsync.
    /// Berkas lampiran Word/PDF tetap tersimpan di folder ArsipKeputusanFiles.
    /// </summary>
    public class ArsipKeputusanRepository : IArsipKeputusanRepository
    {
        private readonly SqliteConnection _connection;
        private readonly string _wordFilesFolder;
        private readonly string _legacyExcelPath;
        private readonly ILogger<ArsipKeputusanRepository> _logger;
        private readonly ActivityLogService? _activityLog;

        /// <summary>Inisialisasi tabel + impor legacy hanya sekali per proses.</summary>
        private static bool _initialized;
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        public ArsipKeputusanRepository(
            SqliteConnection connection,
            AppConfig appConfig,
            FileService fileService,
            ILogger<ArsipKeputusanRepository> logger,
            ActivityLogService? activityLog = null)
        {
            _activityLog = activityLog;
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _wordFilesFolder = fileService.SanitizePath(Path.Combine(appConfig.TemplateFolder, "ArsipKeputusanFiles"));
            _legacyExcelPath = fileService.SanitizePath(Path.Combine(appConfig.TemplateFolder, "ArsipKeputusan.xlsx"));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!Directory.Exists(_wordFilesFolder)) Directory.CreateDirectory(_wordFilesFolder);
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
                await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ArsipKeputusan (
                    Id             INTEGER PRIMARY KEY,
                    JenisKeputusan TEXT NOT NULL,
                    Nomor          TEXT NOT NULL,
                    Tanggal        TEXT NOT NULL,
                    Tentang        TEXT,
                    Keterangan     TEXT,
                    FileLampiran   TEXT,
                    DibuatAt       TEXT NOT NULL DEFAULT (datetime('now','localtime')));
                    CREATE INDEX IF NOT EXISTS idx_arsipkeputusan_jenis   ON ArsipKeputusan(JenisKeputusan);
                    CREATE INDEX IF NOT EXISTS idx_arsipkeputusan_tanggal ON ArsipKeputusan(Tanggal);
                    CREATE TABLE IF NOT EXISTS ArsipKeputusanMeta (
                    Kunci  TEXT PRIMARY KEY,
                    Nilai  TEXT);").ConfigureAwait(false);

                // Impor legacy sekali saja (ditandai di tabel meta agar penghapusan
                // data oleh pengguna tidak memicu impor ulang).
                long sudah = await _connection.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM ArsipKeputusanMeta WHERE Kunci = 'ImportedFromExcel'")
                    .ConfigureAwait(false);
                if (sudah == 0)
                {
                    int n = await ImportFromExcelInternalAsync(_legacyExcelPath).ConfigureAwait(false);
                    await _connection.ExecuteAsync(
                        "INSERT OR REPLACE INTO ArsipKeputusanMeta (Kunci, Nilai) VALUES ('ImportedFromExcel', '1')")
                        .ConfigureAwait(false);
                    if (n > 0)
                    {
                        _logger.LogInformation("Impor legacy ArsipKeputusan.xlsx selesai: {Count} baris dimasukkan ke SQLite.", n);
                    }
                }

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
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

        public async Task<List<DataKeputusan>> GetAllAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            const string sql = @"SELECT Id AS IdBarisExcel, JenisKeputusan, Nomor, Tanggal, Tentang, Keterangan,
                                        FileLampiran AS FileWord
                                 FROM ArsipKeputusan";
            var rows = await _connection.QueryAsync<Row>(sql).ConfigureAwait(false);
            return rows.Select(r => new DataKeputusan
            {
                IdBarisExcel = r.IdBarisExcel,
                JenisKeputusan = (r.JenisKeputusan ?? "").Trim(),
                Nomor = (r.Nomor ?? "").Trim(),
                Tanggal = ParseTanggal(r.Tanggal),
                Tentang = (r.Tentang ?? "").Trim(),
                Keterangan = (r.Keterangan ?? "").Trim(),
                FileWord = string.IsNullOrWhiteSpace(r.FileWord) ? null : r.FileWord.Trim()
            }).ToList();
        }

        public async Task<DataKeputusan> AddAsync(DataKeputusan item)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await WriteWithRetryAsync(async () =>
            {
                const string sql = @"INSERT INTO ArsipKeputusan (JenisKeputusan, Nomor, Tanggal, Tentang, Keterangan, FileLampiran)
                                     VALUES (@Jenis, @Nomor, @Tanggal, @Tentang, @Keterangan, @FileWord);
                                     SELECT last_insert_rowid();";
                long id = await _connection.ExecuteScalarAsync<long>(sql, new
                {
                    Jenis = item.JenisKeputusan,
                    item.Nomor,
                    Tanggal = ToIso(item.Tanggal),
                    item.Tentang,
                    item.Keterangan,
                    FileWord = item.FileWord
                }).ConfigureAwait(false);
                item.IdBarisExcel = (int)id;
                return id;
            }).ConfigureAwait(false);

            _logger.LogInformation("Keputusan ditambahkan: Id={Id} Nomor={Nomor} Jenis={Jenis}",
                item.IdBarisExcel, item.Nomor, item.JenisKeputusan);

            // Riwayat aktivitas: catat pembuat arsip keputusan.
            _activityLog?.Log(item.JenisKeputusan, item.Nomor, "Buat", $"Tentang: {item.Tentang}");
            return item;
        }

        public async Task UpdateAsync(DataKeputusan item)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            int affected = await WriteWithRetryAsync(async () =>
                await _connection.ExecuteAsync(@"UPDATE ArsipKeputusan
                         SET JenisKeputusan = @Jenis, Nomor = @Nomor, Tanggal = @Tanggal,
                             Tentang = @Tentang, Keterangan = @Keterangan, FileLampiran = @FileWord
                         WHERE Id = @Id", new
                {
                    Jenis = item.JenisKeputusan,
                    item.Nomor,
                    Tanggal = ToIso(item.Tanggal),
                    item.Tentang,
                    item.Keterangan,
                    FileWord = item.FileWord,
                    Id = item.IdBarisExcel
                }).ConfigureAwait(false)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException($"Data id={item.IdBarisExcel} tidak ditemukan di database.");
            }
            _logger.LogInformation("Keputusan diperbarui: Id={Id}", item.IdBarisExcel);

            // Riwayat aktivitas: catat pengedit arsip keputusan.
            _activityLog?.Log(item.JenisKeputusan, item.Nomor, "Edit", $"Tentang: {item.Tentang}");
        }

        public async Task DeleteAsync(int idBarisExcel)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await WriteWithRetryAsync(async () =>
            {
                var fileLampiran = await _connection.ExecuteScalarAsync<string?>(
                    "SELECT FileLampiran FROM ArsipKeputusan WHERE Id = @Id", new { Id = idBarisExcel })
                    .ConfigureAwait(false);

                int affected = await _connection.ExecuteAsync(
                    "DELETE FROM ArsipKeputusan WHERE Id = @Id", new { Id = idBarisExcel })
                    .ConfigureAwait(false);
                if (affected == 0) return affected;

                _logger.LogInformation("Keputusan dihapus: Id={Id}", idBarisExcel);

                // Riwayat aktivitas: catat penghapus arsip keputusan.
                _activityLog?.Log("Keputusan", $"ID {idBarisExcel}", "Hapus");

                // Hapus berkas lampiran terkait agar tidak menumpuk file yatim.
                // Kegagalan hapus berkas tidak menggagalkan penghapusan data.
                if (!string.IsNullOrWhiteSpace(fileLampiran))
                {
                    try
                    {
                        var path = ResolveWordFullPath(fileLampiran);
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
                return affected;
            }).ConfigureAwait(false);
        }

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
                imported += await _connection.ExecuteAsync(@"INSERT OR IGNORE INTO ArsipKeputusan
                        (Id, JenisKeputusan, Nomor, Tanggal, Tentang, Keterangan, FileLampiran)
                        VALUES (@Id, @Jenis, @Nomor, @Tanggal, @Tentang, @Keterangan, @FileWord)", r)
                    .ConfigureAwait(false);
            }
            return imported;
        }

        // =====================================================================
        // Pembacaan Excel legacy (sumber impor)
        // =====================================================================

        static ArsipKeputusanRepository() => ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

        private List<object> ReadLegacyExcel(string excelPath)
        {
            var result = new List<object>();
            try
            {
                var fileInfo = new FileInfo(excelPath);
                if (!fileInfo.Exists) return result;

                using var package = new ExcelPackage(fileInfo);
                var ws = package.Workbook.Worksheets["Arsip Keputusan"];
                if (ws?.Dimension == null) return result;

                for (int row = 2; row <= ws.Dimension.End.Row; row++)
                {
                    if (!int.TryParse((ws.Cells[row, 1].Text ?? "").Trim(), out int id) || id <= 0) continue;

                    result.Add(new
                    {
                        Id = id,
                        Jenis = (ws.Cells[row, 2].Text ?? "SK").Trim().ToUpperInvariant(),
                        Nomor = (ws.Cells[row, 3].Text ?? "").Trim(),
                        Tanggal = ToIso(ParseTanggalCell(ws.Cells[row, 4])),
                        Tentang = (ws.Cells[row, 5].Text ?? "").Trim(),
                        Keterangan = (ws.Cells[row, 6].Text ?? "").Trim(),
                        FileWord = string.IsNullOrWhiteSpace(ws.Cells[row, 7].Text) ? null : ws.Cells[row, 7].Text.Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca Excel legacy dari {Path} (impor dilewati)", excelPath);
            }
            return result;
        }

        /// <summary>Parsing tanggal Excel toleran: dd-MM-yyyy, dd/MM/yyyy, ISO, dsb.</summary>
        private static DateTime ParseTanggalCell(ExcelRange cell)
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
            return DateTime.Today;
        }

        // =====================================================================
        // Utilitas
        // =====================================================================

        private class Row
        {
            public int IdBarisExcel { get; set; }
            public string JenisKeputusan { get; set; } = "";
            public string Nomor { get; set; } = "";
            public string Tanggal { get; set; } = "";
            public string Tentang { get; set; } = "";
            public string Keterangan { get; set; } = "";
            public string? FileWord { get; set; }
        }

        private static string ToIso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime ParseTanggal(string iso)
        {
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return DateTime.Today;
        }

        public (string FileName, string FullPath) ResolveWordStorage(int id, string jenisKeputusan, string nomor, string sourceExtension)
        {
            if (!Directory.Exists(_wordFilesFolder)) Directory.CreateDirectory(_wordFilesFolder);

            var safeNomor = SanitizeFileName(string.IsNullOrWhiteSpace(nomor) ? id.ToString() : nomor);
            var jenis = SanitizePart(jenisKeputusan ?? "SK");
            var ext = string.Equals(Path.GetExtension(sourceExtension), ".docx", StringComparison.OrdinalIgnoreCase) ? ".docx" :
                      string.Equals(Path.GetExtension(sourceExtension), ".doc", StringComparison.OrdinalIgnoreCase) ? ".doc" :
                      string.Equals(Path.GetExtension(sourceExtension), ".pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".docx";
            var fileName = $"{jenis}_{id}_{safeNomor}{ext}";
            return (fileName, Path.Combine(_wordFilesFolder, fileName));
        }

        public string? ResolveWordFullPath(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            // Jaga agar tidak lolos direktori.
            if (Path.IsPathRooted(fileName) || fileName.Contains("..")) return null;
            var full = Path.Combine(_wordFilesFolder, fileName);
            return File.Exists(full) ? full : null;
        }

        private static string SanitizeFileName(string name) =>
            string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        private static string SanitizePart(string part) => SanitizeFileName(part.Replace("/", "_").Replace("\\", "_"));
    }
}
