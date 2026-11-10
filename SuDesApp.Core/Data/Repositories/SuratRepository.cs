using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SuDesApp.Configuration;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Utilities;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    /// <summary>
    /// Ringkasan register untuk halaman Beranda: total, hari ini, bulan ini,
    /// jumlah per jenis, dan surat terbaru — seluruh angka dihitung di database
    /// (COUNT/GROUP BY + LIMIT), bukan dari buku agenda surat masuk/keluar.
    /// NTCR dikecualikan karena memakai register sendiri, persis seperti
    /// halaman Register Surat, sehingga angka Beranda cocok dengan yang dilihat
    /// operator di halaman itu.
    /// </summary>
    public sealed class RingkasanRegisterSurat
    {
        public int Total { get; init; }
        public int HariIni { get; init; }
        public int BulanIni { get; init; }

        /// <summary>Jumlah surat per jenis (kunci = NamaJenis uppercase, mis. SKD_UMUM).</summary>
        public Dictionary<string, int> PerJenis { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Beberapa surat terbaru (urut Id menurun) untuk daftar aktivitas.</summary>
        public List<BarisAktivitasRegister> Terbaru { get; init; } = new();
    }

    /// <summary>Satu baris "Aktivitas terbaru" pada halaman Beranda.</summary>
    public sealed class BarisAktivitasRegister
    {
        public DateTime TanggalSurat { get; init; }

        /// <summary>Nama jenis surat apa adanya dari register (konstanta, mis. SKD_UMUM).</summary>
        public string JenisSurat { get; init; } = string.Empty;

        public string NomorSurat { get; init; } = string.Empty;

        /// <summary>Keperluan surat; jatuh ke Keterangan bila keperluan kosong.</summary>
        public string Perihal { get; init; } = string.Empty;
    }

    public interface ISuratRepository
    {
        Task<int> InsertAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> UpdateStatusAsync(int id, string status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<SuratData> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<SuratData>> GetFilteredAsync(FilterConditions filters, string sortBy, bool ascending, int skip, int take, CancellationToken cancellationToken = default);
        Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Jumlah surat per status (Draft/Active/Cancelled) untuk filter yang sama —
        /// satu query GROUP BY, bukan satu query per status.
        /// </summary>
        Task<Dictionary<string, int>> CountByStatusAsync(FilterConditions filters, CancellationToken cancellationToken = default);
        Task InitializeSuratIndexesAsync();
        Task<List<string>> GetJenisSuratKeteranganDesaAsync();
        Task RefreshJenisSuratConfigurationAsync();
        Task<IEnumerable<SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
        Task<int> AddSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default);

        /// <summary>
        /// Nomor urut terbesar pada tahun berjalan, diambil dari segmen kedua
        /// nomor surat. Dipakai sinkron Android supaya nomor draf di perangkat
        /// tidak mulai dari 1 bila register desktop sudah lebih jauh.
        /// </summary>
        Task<int> AmbilUrutTerakhirAsync(int tahun, CancellationToken cancellationToken = default);
        Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
        Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default);
        Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<Dictionary<string, object>> GetDatabaseStatsAsync();
        Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default);
        Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default);
        Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default);
        Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default);

        // =================================================================
        // Alur persetujuan, scan, cetakan, dan kode verifikasi keaslian.
        // Kolom tersimpan langsung di tabel Surat (dijamin EnsureSuratSchemaAsync).
        // =================================================================

        /// <summary>Simpan seluruh keadaan alur persetujuan satu surat.</summary>
        Task<bool> SimpanPersetujuanAsync(SuratData surat, CancellationToken cancellationToken = default);

        /// <summary>Simpan/lepas nama berkas scan surat (null = lepas).</summary>
        Task<bool> SimpanScanSuratAsync(int idSurat, string? namaBerkas, CancellationToken cancellationToken = default);

        /// <summary>Catat satu cetakan; kembalikan jumlah cetakan sesudahnya.</summary>
        Task<int> CatatCetakAsync(int idSurat, CancellationToken cancellationToken = default);

        /// <summary>Simpan kode verifikasi + cap dokumen (kode wajib tidak kosong).</summary>
        Task<bool> SimpanVerifikasiAsync(int idSurat, string kode, string hash, CancellationToken cancellationToken = default);

        /// <summary>Cari surat berdasarkan kode verifikasi (huruf besar/kecil diabaikan).</summary>
        Task<SuratData?> GetByKodeVerifikasiAsync(string kode, CancellationToken cancellationToken = default);

        /// <summary>Jumlah surat per status persetujuan (surat tanpa alur = TANPA_ALUR).</summary>
        Task<Dictionary<string, int>> CountPerStatusPersetujuanAsync(CancellationToken cancellationToken = default);

        /// <summary>Rekap jumlah surat per bulan + status persetujuan untuk satu tahun.</summary>
        Task<List<BarisRekapPersetujuan>> GetRekapPersetujuanPerBulanAsync(int tahun, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ringkasan register untuk halaman Beranda (lihat <see cref="RingkasanRegisterSurat"/>):
        /// total, hari ini, bulan ini, jumlah per jenis, dan surat terbaru.
        /// </summary>
        Task<RingkasanRegisterSurat> GetRingkasanRegisterAsync(int jumlahTerbaru = 5, CancellationToken cancellationToken = default);
    }

    // ISuratInsertion: kemampuan sisip mentah internal — satu-satunya pemakai
    // yang disengaja adalah SuratSaveService (pemilik alur simpan); test diberi
    // akses lewat InternalsVisibleTo untuk mengunci jalur ini.
    public partial class SuratRepository : ISuratRepository, ISuratInsertion
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<SuratRepository> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;
        private readonly IEnumerable<ISuratDataHandler> _suratDataHandlers;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepository;
        private readonly IJenisSuratRepository _jenisSuratRepository;
        private readonly ConcurrentDictionary<int, SuratData> _cachedSuratData;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private readonly ActivityLogService? _activityLog;

        public SuratRepository(
            SqliteConnection connection,
            ILogger<SuratRepository> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor,
            IEnumerable<ISuratDataHandler> suratDataHandlers,
            IWargaRepository wargaRepository,
            IJenisSuratRepository jenisSuratRepository,
            IDesaRepository desaRepository,
            ActivityLogService? activityLog = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
            _suratDataHandlers = suratDataHandlers ?? throw new ArgumentNullException(nameof(suratDataHandlers));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _activityLog = activityLog;
            _cachedSuratData = new ConcurrentDictionary<int, SuratData>();
        }

        private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
        {
            if (_connection.State != ConnectionState.Open)
            {
                try
                {
                    await _connection.OpenAsync(cancellationToken);
                    _logger.LogDebug("Database connection opened in SuratRepository");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open database connection in SuratRepository");
                    throw;
                }
            }
        }

        /// <summary>
        /// Ringkasan register untuk halaman Beranda (lihat <see cref="RingkasanRegisterSurat"/>):
        /// seluruh angka dihitung di database; NTCR dikecualikan agar cocok
        /// dengan halaman Register Surat.
        /// </summary>
        public async Task<RingkasanRegisterSurat> GetRingkasanRegisterAsync(int jumlahTerbaru = 5, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);

            var hariIni = DateTime.Today;
            var awalBulan = new DateTime(hariIni.Year, hariIni.Month, 1);

            var kecualiNtcr = SuratConstants.NtcrSemua.Select(n => n.ToUpperInvariant()).ToArray();
            string dasar = "FROM Surat s INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis "
                         + "WHERE UPPER(js.NamaJenis) NOT IN @Kecuali";

            int total = await _connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) {dasar}", new { Kecuali = kecualiNtcr });

            int hariIniCount = await _connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) {dasar} AND s.TanggalSurat >= @Awal AND s.TanggalSurat < @Akhir",
                new { Kecuali = kecualiNtcr, Awal = hariIni.ToString("yyyy-MM-dd"), Akhir = hariIni.AddDays(1).ToString("yyyy-MM-dd") });

            int bulanIniCount = await _connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) {dasar} AND s.TanggalSurat >= @Awal AND s.TanggalSurat < @Akhir",
                new { Kecuali = kecualiNtcr, Awal = awalBulan.ToString("yyyy-MM-dd"), Akhir = awalBulan.AddMonths(1).ToString("yyyy-MM-dd") });

            var perJenis = (await _connection.QueryAsync<(string Jenis, int Jumlah)>(
                $"SELECT UPPER(js.NamaJenis) AS Jenis, COUNT(*) AS Jumlah {dasar} GROUP BY UPPER(js.NamaJenis)",
                new { Kecuali = kecualiNtcr }))
                .ToDictionary(r => r.Jenis, r => r.Jumlah, StringComparer.OrdinalIgnoreCase);

            var terbaru = new List<BarisAktivitasRegister>();
            if (jumlahTerbaru > 0)
            {
                var baris = await _connection.QueryAsync<BarisRegisterRow>(
                    $@"SELECT s.TanggalSurat, UPPER(js.NamaJenis) AS NamaJenis, s.NomorSurat,
                              COALESCE(NULLIF(s.Keperluan, ''), s.Keterangan) AS Perihal
                       {dasar}
                       ORDER BY s.ID_Surat DESC LIMIT @Take",
                    new { Kecuali = kecualiNtcr, Take = jumlahTerbaru });

                terbaru.AddRange(baris.Select(r => new BarisAktivitasRegister
                {
                    TanggalSurat = SafeGetDateTime(r.TanggalSurat!),
                    JenisSurat = r.NamaJenis ?? string.Empty,
                    NomorSurat = r.NomorSurat ?? string.Empty,
                    Perihal = r.Perihal ?? string.Empty
                }));
            }

            return new RingkasanRegisterSurat
            {
                Total = total,
                HariIni = hariIniCount,
                BulanIni = bulanIniCount,
                PerJenis = perJenis,
                Terbaru = terbaru
            };
        }

        private sealed class BarisRegisterRow
        {
            public string? TanggalSurat { get; set; }
            public string? NamaJenis { get; set; }
            public string? NomorSurat { get; set; }
            public string? Perihal { get; set; }
        }

        public async Task InitializeSuratIndexesAsync()
        {
            _logger.LogInformation("Initializing Surat indexes...");
            await EnsureConnectionOpenAsync();
            await EnsureSuratSchemaAsync();

            // Menggunakan indeks yang sudah didefinisikan di database schema
            var indexCommands = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK);",
                "CREATE INDEX IF NOT EXISTS idx_warga_nama ON Warga(Nama);",
                "CREATE INDEX IF NOT EXISTS idx_surat_nomor ON Surat(NomorSurat);",
                "CREATE INDEX IF NOT EXISTS idx_surat_tanggal ON Surat(TanggalSurat);",
                "CREATE INDEX IF NOT EXISTS idx_surat_jenis ON Surat(ID_Jenis);",
                "CREATE INDEX IF NOT EXISTS idx_surat_warga ON Surat(ID_Warga);",
                "CREATE INDEX IF NOT EXISTS idx_surat_status ON Surat(Status);",

                // Komposit (Status, TanggalSurat) untuk tombol cepat status di register
                // (Draft/Aktif/Dibatalkan). Diuji pada database 100.000 surat: tab Draft
                // turun dari 30 ms → 13 ms (COUNT) dan 31 ms → 0 ms (daftar 50 baris),
                // karena rencananya menjadi "Status = ? AND TanggalSurat > ? AND < ?"
                // dalam satu indeks — tanpa memilah ulang seluruh baris berstatus itu.
                // CATATAN hasil uji: kandidat lain — (ID_Jenis, TanggalSurat) dan
                // (ID_Jenis, Status, TanggalSurat) — TIDAK memberi perbaikan terukur
                // (perencana tetap memilih idx_surat_tanggal), jadi sengaja tidak dipasang
                // supaya tidak menambah biaya tulis.
                "CREATE INDEX IF NOT EXISTS idx_surat_status_tanggal ON Surat(Status, TanggalSurat);",

                // Sisa uji coba indeks yang ternyata tidak dipakai perencana query
                // (lihat catatan di atas) — dibuang lagi agar tidak membebani penulisan.
                "DROP INDEX IF EXISTS idx_surat_jenis_tanggal;",
                "DROP INDEX IF EXISTS idx_surat_jenis_status_tanggal;"
            };

            foreach (var cmd in indexCommands)
            {
                await _connection.ExecuteAsync(cmd);
            }
            _logger.LogInformation("Surat indexes initialized.");
        }

        // Migrasi skema: pastikan kolom yang dibutuhkan model baru ada di tabel Surat
        private async Task EnsureTableColumnAsync(string table, string column, string ddl, CancellationToken cancellationToken = default)
        {
            var tableExists = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;",
                new { name = table });
            if (tableExists == 0) return;

            var existingColumns = new List<string>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            if (existingColumns.Any(c => c.Equals(column, StringComparison.OrdinalIgnoreCase)))
                return;

            await _connection.ExecuteAsync(ddl, transaction: null);
            _logger.LogInformation("Migrasi {Table}: kolom {Column} ditambahkan.", table, column);
        }

        private async Task EnsureSuratSchemaAsync(CancellationToken cancellationToken = default)
        {
            var migrations = new (string Table, string Column, string Ddl)[]
            {
                ("Surat", "Status", "ALTER TABLE Surat ADD COLUMN Status TEXT NOT NULL DEFAULT 'Draft';"),
                ("Surat", "KodeJenis", "ALTER TABLE Surat ADD COLUMN KodeJenis TEXT NULL;"),
                ("Surat", "AdditionalData", "ALTER TABLE Surat ADD COLUMN AdditionalData TEXT NULL;"),
                ("Surat", "CreatedAt", "ALTER TABLE Surat ADD COLUMN CreatedAt TEXT NULL;"),
                ("Surat", "IdAntreanSinkron", "ALTER TABLE Surat ADD COLUMN IdAntreanSinkron TEXT NULL;"),
                ("Surat", "UpdatedAt", "ALTER TABLE Surat ADD COLUMN UpdatedAt TEXT NULL;"),
                // Alur persetujuan + keaslian surat (opsional per surat).
                ("Surat", "StatusPersetujuan", "ALTER TABLE Surat ADD COLUMN StatusPersetujuan TEXT NULL;"),
                ("Surat", "VerifikasiOleh", "ALTER TABLE Surat ADD COLUMN VerifikasiOleh TEXT NULL;"),
                ("Surat", "VerifikasiPada", "ALTER TABLE Surat ADD COLUMN VerifikasiPada TEXT NULL;"),
                ("Surat", "DitandatanganiOleh", "ALTER TABLE Surat ADD COLUMN DitandatanganiOleh TEXT NULL;"),
                ("Surat", "DitandatanganiPada", "ALTER TABLE Surat ADD COLUMN DitandatanganiPada TEXT NULL;"),
                ("Surat", "CatatanPersetujuan", "ALTER TABLE Surat ADD COLUMN CatatanPersetujuan TEXT NULL;"),
                ("Surat", "FileScanSurat", "ALTER TABLE Surat ADD COLUMN FileScanSurat TEXT NULL;"),
                ("Surat", "JumlahCetak", "ALTER TABLE Surat ADD COLUMN JumlahCetak INTEGER NOT NULL DEFAULT 0;"),
                ("Surat", "KodeVerifikasi", "ALTER TABLE Surat ADD COLUMN KodeVerifikasi TEXT NULL;"),
                ("Surat", "HashVerifikasi", "ALTER TABLE Surat ADD COLUMN HashVerifikasi TEXT NULL;"),
                ("Instansi", "PimpinanInstansi", "ALTER TABLE Instansi ADD COLUMN PimpinanInstansi TEXT NULL;"),
                ("Instansi", "NomorSkIzinUsaha", "ALTER TABLE Instansi ADD COLUMN NomorSkIzinUsaha TEXT NULL;"),
                ("Instansi", "NikPimpinan", "ALTER TABLE Instansi ADD COLUMN NikPimpinan TEXT NULL;"),
                ("JenisSurat", "Deskripsi", "ALTER TABLE JenisSurat ADD COLUMN Deskripsi TEXT NULL;"),
                ("SKU", "LokasiUsaha", "ALTER TABLE SKU ADD COLUMN LokasiUsaha TEXT NULL;"),
                ("Kematian", "TempatKematian", "ALTER TABLE Kematian ADD COLUMN TempatKematian TEXT NULL;")
            };

            foreach (var migration in migrations)
            {
                await EnsureTableColumnAsync(migration.Table, migration.Column, migration.Ddl, cancellationToken);
            }
        }

        private async Task ValidateSuratDataAsync(SuratData suratData, CancellationToken cancellationToken = default)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));
            var errors = await suratData.ValidateAsync();
            if (errors.Any())
                throw new ValidationException($"Validation failed: {string.Join("; ", errors)}");

            var jenisSurat = await _jenisSuratRepository.GetJenisSuratByNamaAsync(suratData.NamaJenis?.ToUpperInvariant() ?? string.Empty);
            if (jenisSurat == null)
                throw new ArgumentException($"Invalid surat type '{suratData.NamaJenis}'.");

            suratData.ID_Jenis = jenisSurat.ID_Jenis;
            suratData.KodeJenis = jenisSurat.KodeJenis;
            suratData.NamaJenis = jenisSurat.NamaJenis!;
            suratData.SetJenisFromNamaJenis(suratData.NamaJenis!);
        }

        /// <summary>Jalur sisip mentah internal (lihat <see cref="ISuratInsertion"/>) — alurnya sama dengan InsertAsync.</summary>
        Task<int> ISuratInsertion.InsertSuratAsync(SuratData entity, IDbTransaction? transaction, CancellationToken cancellationToken)
            => InsertAsync(entity, transaction, cancellationToken);

        public async Task<int> InsertAsync(SuratData suratData, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            bool ownTransaction = transaction == null;
            if (ownTransaction)
            {
                await EnsureConnectionOpenAsync(cancellationToken);
                transaction = _connection.BeginTransaction();
            }

            try
            {
                // Resolve jenis + nomor + default SEBELUM validasi agar data baru
                // yang belum punya nomor tidak langsung gagal validasi.
                var jenisSurat = await _jenisSuratRepository.GetJenisSuratByNamaAsync(suratData.NamaJenis?.ToUpperInvariant() ?? string.Empty);
                if (jenisSurat == null)
                    throw new ArgumentException($"Invalid surat type '{suratData.NamaJenis}'.");

                suratData.ID_Jenis = jenisSurat.ID_Jenis;
                suratData.KodeJenis = jenisSurat.KodeJenis;
                suratData.NamaJenis = jenisSurat.NamaJenis!;
                suratData.SetJenisFromNamaJenis(suratData.NamaJenis!);

                if (string.IsNullOrWhiteSpace(suratData.Status))
                    suratData.Status = "Draft";

                if (string.IsNullOrWhiteSpace(suratData.NomorSurat))
                {
                    suratData.NomorSurat = await _jenisSuratRepository.GenerateNomorSuratAsync(suratData.KodeJenis!);
                }
                else if (await CheckNomorSuratExistsAsync(suratData.NomorSurat, null, cancellationToken))
                {
                    // Nomor pada form diisi otomatis saat form dibuka, bukan dipesan:
                    // bisa tertabrak operator lain yang menyimpan lebih dulu. Bangkitkan
                    // nomor segar alih-alih menolak, supaya penulisan surat tidak mentok.
                    _logger.LogWarning(
                        "Nomor surat {NomorSurat} sudah dipakai; membangkitkan nomor segar untuk {NamaJenis}.",
                        suratData.NomorSurat, suratData.NamaJenis);

                    suratData.NomorSurat = await _jenisSuratRepository.GenerateNomorSuratAsync(suratData.KodeJenis!);
                }

                await ValidateSuratDataAsync(suratData, cancellationToken);

                int idWarga = await GetOrCreateWargaAsync(suratData, transaction!, cancellationToken);

                var query = _queryProvider.GetQuery("InsertSurat");
                var suratId = await _connection.ExecuteScalarAsync<int>(query, new
                {
                    suratData.ID_Jenis,
                    suratData.NomorSurat,
                    TanggalSurat = suratData.TanggalSurat.ToString("yyyy-MM-dd"),
                    suratData.Keterangan,
                    suratData.Keperluan,
                    ID_Warga = idWarga,
                    suratData.AdditionalData,
                    Status = suratData.Status
                }, transaction);

                if (suratId <= 0)
                    throw new DataAccessException("Failed to insert surat, invalid ID returned.");

                suratData.ID_Surat = suratId;
                await InsertRelatedDataSafeAsync(suratId, suratData, transaction!, cancellationToken);

                // Surat yang langsung terbit (Active) wajib punya kode verifikasi
                // + cap dokumen sejak tersimpan — surat draft mendapatkannya saat
                // diubah menjadi Active lewat UpdateAsync.
                if (string.Equals(suratData.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    await PastikanKodeVerifikasiAsync(suratData, transaction!, cancellationToken);
                }

                if (ownTransaction)
                    transaction?.Commit();

                // Riwayat aktivitas: catat pembuat surat (email Google / admin)
                // SETELAH commit agar operasi yang gagal/rollback tidak tercatat.
                _activityLog?.Log(suratData.NamaJenis ?? "Surat", $"{suratData.NomorSurat}", "Buat",
                    string.IsNullOrWhiteSpace(suratData.Warga?.Nama) ? null : $"Pemohon: {suratData.Warga?.Nama}");

                _cachedSuratData[suratId] = suratData;
                await _cacheService.SetAsync($"Surat_{suratId}", suratData, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });
                await ClearListCacheAsync();

                return suratId;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction?.Rollback();
                _logger.LogError(ex, "Failed to insert surat");
                throw;
            }
        }

        public async Task<bool> UpdateAsync(SuratData suratData, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            if (suratData.ID_Surat <= 0)
                throw new ArgumentException("ID_Surat must be provided.");

            bool ownTransaction = transaction == null;
            if (ownTransaction)
            {
                await EnsureConnectionOpenAsync(cancellationToken);
                transaction = _connection.BeginTransaction();
            }

            try
            {
                // Validasi duplikat nomor lebih dulu (murni SQLite): jalur edit dari
                // draft harus bisa menyimpan baris yang belum lengkap tanpa diblokir
                // validasi penuh, sementara nomor dobel tetap tertolak di kondisi mana pun.
                var existing = await GetByIdAsync(suratData.ID_Surat, transaction, cancellationToken);
                if (existing == null)
                    throw new DataRetrievalException($"Surat with ID {suratData.ID_Surat} not found.");

                if (!string.IsNullOrWhiteSpace(suratData.NomorSurat) && suratData.NomorSurat != existing.NomorSurat)
                    if (await CheckNomorSuratExistsAsync(suratData.NomorSurat, suratData.ID_Surat, cancellationToken))
                        throw new ValidationException($"Nomor surat '{suratData.NomorSurat}' already used.");

                // Draft tetap dapat disimpan walau belum lengkap; surat Aktif wajib lolos
                // validasi penuh. Nomor duplikat sudah ditolak di atas.
                bool isDraft = string.Equals(suratData.Status ?? existing.Status, "Draft", StringComparison.OrdinalIgnoreCase);
                if (!isDraft)
                {
                    await ValidateSuratDataAsync(suratData, cancellationToken);
                }

                int idWarga = await GetOrCreateWargaAsync(suratData, transaction!, cancellationToken);

                var query = _queryProvider.GetQuery("UpdateSurat");
                var rowsAffected = await _connection.ExecuteAsync(query, new
                {
                    ID_Surat = suratData.ID_Surat,
                    suratData.ID_Jenis,
                    suratData.NomorSurat,
                    TanggalSurat = suratData.TanggalSurat.ToString("yyyy-MM-dd"),
                    suratData.Keterangan,
                    suratData.Keperluan,
                    ID_Warga = idWarga,
                    suratData.AdditionalData,
                    Status = suratData.Status ?? existing.Status
                }, transaction);

                if (rowsAffected > 0)
                {
                    await DeleteRelatedDataAsync(suratData.ID_Surat, suratData.NamaJenis, transaction!, cancellationToken);
                    await InsertRelatedDataSafeAsync(suratData.ID_Surat, suratData, transaction!, cancellationToken);

                    // Draft yang baru saja dijadikan Active: kode verifikasi dibuat
                    // SEKALI di sini dan tidak pernah diganti pada edit berikutnya.
                    if (string.Equals(suratData.Status ?? existing.Status, "Active", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(existing.KodeVerifikasi))
                    {
                        await PastikanKodeVerifikasiAsync(suratData, transaction!, cancellationToken);
                    }

                    if (ownTransaction)
                        transaction?.Commit();

                    // Riwayat aktivitas: catat pengedit surat SETELAH commit.
                    _activityLog?.Log(suratData.NamaJenis ?? "Surat", $"{suratData.NomorSurat}", "Edit",
                        $"Status: {suratData.Status ?? existing?.Status}");

                    _cachedSuratData[suratData.ID_Surat] = suratData;
                    await _cacheService.SetAsync($"Surat_{suratData.ID_Surat}", suratData, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(10),
                        AbsoluteExpirationRelativeToNow = _cacheExpiration
                    });
                    await ClearListCacheAsync();
                    return true;
                }

                if (ownTransaction)
                    transaction?.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction?.Rollback();
                _logger.LogError(ex, "Failed to update surat ID: {ID}", suratData.ID_Surat);
                throw;
            }
        }

        public async Task<bool> UpdateStatusAsync(int id, string status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            bool ownTransaction = transaction == null;
            if (ownTransaction)
            {
                await EnsureConnectionOpenAsync(cancellationToken);
                transaction = _connection.BeginTransaction();
            }

            try
            {
                var query = _queryProvider.GetQuery("UpdateSuratStatus");
                var rowsAffected = await _connection.ExecuteAsync(query, new { ID_Surat = id, Status = status }, transaction);

                if (rowsAffected > 0)
                {
                    if (ownTransaction)
                        transaction?.Commit();

                    // Riwayat aktivitas: perubahan status dari register/panel WA.
                    string? nomor = _cachedSuratData.TryGetValue(id, out var cached) ? cached.NomorSurat : null;
                    _activityLog?.Log("Surat", nomor ?? $"ID {id}", "Ubah Status", $"Status baru: {status}");

                    // Update cache
                    if (_cachedSuratData.TryGetValue(id, out var cachedSurat))
                    {
                        cachedSurat.Status = status;
                        await _cacheService.SetAsync($"Surat_{id}", cachedSurat, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                    await ClearListCacheAsync();
                    return true;
                }

                if (ownTransaction)
                    transaction?.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction?.Rollback();
                _logger.LogError(ex, "Failed to update surat status ID: {ID}", id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            bool ownTransaction = transaction == null;
            if (ownTransaction)
            {
                await EnsureConnectionOpenAsync(cancellationToken);
                transaction = _connection.BeginTransaction();
            }

            try
            {
                var surat = await GetByIdAsync(id, transaction, cancellationToken);
                if (surat == null)
                    return false;

                await DeleteRelatedDataAsync(id, surat.NamaJenis, transaction!, cancellationToken);

                var query = _queryProvider.GetQuery("DeleteSurat");
                var rowsAffected = await _connection.ExecuteAsync(query, new { ID_Surat = id }, transaction);

                if (rowsAffected > 0)
                {
                    if (ownTransaction)
                        transaction?.Commit();

                    // Riwayat aktivitas: catat penghapus surat.
                    _activityLog?.Log(surat.NamaJenis ?? "Surat", $"{surat.NomorSurat}", "Hapus",
                        $"Status terakhir: {surat.Status}");

                    _cachedSuratData.TryRemove(id, out _);
                    await _cacheService.RemoveAsync<SuratData>($"Surat_{id}", cancellationToken);
                    await ClearListCacheAsync();
                    return true;
                }

                if (ownTransaction)
                    transaction?.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction?.Rollback();
                _logger.LogError(ex, "Failed to delete surat ID: {ID}", id);
                throw;
            }
        }

        public async Task<SuratData> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            string cacheKey = $"Surat_{id}";
            var cached = await _cacheService.GetAsync<SuratData>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("GetSuratById");

            var result = await _connection.QueryAsync<SuratData, WargaData, Instansi, SuratData>(
                query,
                (s, w, i) =>
                {
                    if (w != null && w.ID_Warga > 0)
                    {
                        s.Warga = new WargaData()
                        {
                            ID_Warga = w.ID_Warga,
                            NIK = w.NIK,
                            Nama = w.Nama,
                            TempatLahir = w.TempatLahir,
                            TanggalLahir = w.TanggalLahir,
                            JenisKelamin = w.JenisKelamin,
                            Agama = w.Agama,
                            StatusPerkawinan = w.StatusPerkawinan,
                            Pekerjaan = w.Pekerjaan,
                            Dusun = w.Dusun,
                            RT = w.RT,
                            RW = w.RW,
                            Desa = w.Desa,
                            Kecamatan = w.Kecamatan,
                            Kabupaten = w.Kabupaten,
                            Pendidikan = w.Pendidikan,
                            Kewarganegaraan = w.Kewarganegaraan,
                            AlamatLengkap = BuildAlamatLengkap(w.Dusun!, w.Desa!, w.Kecamatan!, w.Kabupaten!, w.RT, w.RW)
                        };
                    }

                    if (i != null && !string.IsNullOrEmpty(i.NamaInstansi))
                    {
                        s.Instansi = i;
                    }

                    return s;
                },
                new { ID_Surat = id },
                transaction,
                splitOn: "NIK,NamaInstansi"
            );

            var surat = result.FirstOrDefault();
            if (surat != null)
            {
                surat.Desa = await _desaRepository.GetInfoDesaAsync(cancellationToken);
                await LoadRelatedDataAsync(surat, transaction!, cancellationToken);
                await _cacheService.SetAsync(cacheKey, surat, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                }, cancellationToken);
                _cachedSuratData[id] = surat;
                return surat;
            }
            return null!;
        }

    }
}
