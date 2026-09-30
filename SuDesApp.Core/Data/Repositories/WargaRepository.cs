using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IWargaRepository
    {
        Task InitializeWargaTableAsync();
        Task<int> AddOrUpdateWargaAsync(WargaData wargaData, SqliteConnection? existingConnection = null, IDbTransaction? existingTransaction = null);
        Task<WargaData> GetWargaByIdAsync(int idWarga);
        Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction? transaction = null);

        /// <summary>
        /// Ambil banyak warga sekaligus lewat daftar NIK dengan satu query
        /// (bukan satu query per NIK), dipakai penyusunan lampiran SK yang
        /// bisa memuat puluhan nama. NIK yang tidak ada dilewati diam-diam.
        /// </summary>
        Task<List<WargaData>> AmbilDaftarByNikAsync(IEnumerable<string> daftarNik, CancellationToken ct = default);

        Task<IEnumerable<WargaData>> GetAllWargaAsync();

        /// <summary>
        /// Jumlah warga terdata — dihitung di database (COUNT) tanpa memuat seluruh
        /// baris, dipakai oleh ringkasan Beranda.
        /// </summary>
        Task<int> CountWargaAsync();
        Task<IEnumerable<WargaData>> SearchWargaAsync(string searchTerm);
        Task<bool> DeleteWargaAsync(int id);
        Task<string> GetAlamatByNikAsync(string nik);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData);
        Task<int> GetOrCreateDummyWargaAsync(SqliteConnection connection, IDbTransaction transaction);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection connection, IDbTransaction transaction);

        /// <summary>
        /// Ringkasan angka kependudukan dalam satu perhitungan (status, jenis
        /// kelamin, KK, per RT). Dipakai API /statistik dan laporan penduduk.
        /// </summary>
        Task<WargaStatistikRingkasan> GetStatistikWargaAsync(CancellationToken cancellationToken = default);

        /// <summary>Daftar warga satu halaman; baris dummy (Instansi/Kematian) otomatis tersaring.</summary>
        Task<WargaPagedResult> GetWargaPageAsync(WargaFilter filter, CancellationToken cancellationToken = default);

        /// <summary>Seluruh warga yang cocok filter, tanpa paginasi — untuk ekspor.</summary>
        Task<List<WargaData>> GetWargaForExportAsync(WargaFilter filter, CancellationToken cancellationToken = default);

        /// <summary>Ubah status warga beserta tanggal &amp; keterangan mutasinya. Status tidak dikenal ditolak.</summary>
        Task<bool> UbahStatusWargaAsync(int idWarga, string status, string? tanggalStatus, string? keterangan, CancellationToken cancellationToken = default);

        /// <summary>Simpan (insert/update) kepala keluarga &amp; alamat satu Kartu Keluarga.</summary>
        Task SaveKartuKeluargaAsync(KartuKeluargaData kartuKeluarga, CancellationToken cancellationToken = default);

        /// <summary>Baca satu Kartu Keluarga (JumlahAnggota dihitung dari tabel Warga); null bila belum ada.</summary>
        Task<KartuKeluargaData?> GetKartuKeluargaAsync(string noKk, CancellationToken cancellationToken = default);

        /// <summary>Anggota satu Kartu Keluarga, urut nama; baris dummy tersaring.</summary>
        Task<List<WargaData>> GetAnggotaKeluargaAsync(string noKk, CancellationToken cancellationToken = default);

        /// <summary>Daftar nilai unik RT/RW/dusun untuk dropdown filter wilayah halaman Data Warga.</summary>
        Task<IEnumerable<string>> GetDaftarNilaiWilayahAsync(CancellationToken cancellationToken = default);

        /// <summary>Jumlah surat per warga (ID_Warga → jumlah) — kolom "Jumlah Surat" pada ekspor.</summary>
        Task<Dictionary<int, int>> GetJumlahSuratSemuaWargaAsync(CancellationToken cancellationToken = default);
    }

    public class WargaRepository : IWargaRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ICacheService _cacheService;
        private readonly ILogger<WargaRepository> _logger;
        private readonly AppConfig _config;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private const string DummyNikInstansi = "9999999999999999";
        private const string DummyNikKematian = "0000000000000000";

        public WargaRepository(
            SqliteConnection connection,
            ICacheService cacheService,
            AppConfig config,
            ILogger<WargaRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeWargaTableAsync()
        {
            _logger.LogInformation("Memulai inisialisasi tabel Warga...");
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                await EnsureWargaSchemaAsync();

                var wargaTableCount = await _connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Warga'");

                if (wargaTableCount == 0)
                {
                    await _connection.ExecuteAsync(@"
                        CREATE TABLE Warga (
                            ID_Warga INTEGER PRIMARY KEY AUTOINCREMENT,
                            NIK TEXT UNIQUE,
                            Nama TEXT NOT NULL,
                            TempatLahir TEXT,
                            TanggalLahir TEXT,
                            JenisKelamin TEXT,
                            Agama TEXT,
                            StatusPerkawinan TEXT,
                            Pekerjaan TEXT,
                            Dusun TEXT,
                            RT TEXT,
                            RW TEXT,
                            Desa TEXT,
                            Kecamatan TEXT,
                            Kabupaten TEXT,
                            Pendidikan TEXT,
                            Kewarganegaraan TEXT,
                            GolonganDarah TEXT,
                            NomorHP TEXT,
                            StatusWarga TEXT,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        )");
                    _logger.LogInformation("Tabel Warga dibuat.");

                    await _connection.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK)");
                    _logger.LogInformation("Indeks idx_warga_nik dibuat.");

                    await _connection.ExecuteAsync(
                        "CREATE TRIGGER IF NOT EXISTS tr_warga_updated AFTER UPDATE ON Warga " +
                        "FOR EACH ROW BEGIN " +
                        "UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga; " +
                        "END;");
                    _logger.LogInformation("Trigger update timestamp dibuat.");
                }

                await EnsureKartuKeluargaTableAsync();

                _logger.LogInformation("Inisialisasi tabel Warga selesai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menginisialisasi tabel Warga");
                throw new DataAccessException("Gagal menginisialisasi tabel Warga", ex);
            }
        }

        // Migrasi skema: pastikan kolom yang dibutuhkan model baru ada di tabel Warga legacy
        private async Task EnsureWargaSchemaAsync()
        {
            var tableExists = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Warga';");
            if (tableExists == 0) return;

            var existingColumns = new List<string>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Warga);";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            var migrations = new (string Name, string Ddl)[]
            {
                ("Dusun", "ALTER TABLE Warga ADD COLUMN Dusun TEXT NULL;"),
                ("Desa", "ALTER TABLE Warga ADD COLUMN Desa TEXT NULL;"),
                ("Kecamatan", "ALTER TABLE Warga ADD COLUMN Kecamatan TEXT NULL;"),
                ("Kabupaten", "ALTER TABLE Warga ADD COLUMN Kabupaten TEXT NULL;"),
                ("RT", "ALTER TABLE Warga ADD COLUMN RT TEXT NULL;"),
                ("RW", "ALTER TABLE Warga ADD COLUMN RW TEXT NULL;"),
                ("StatusWarga", "ALTER TABLE Warga ADD COLUMN StatusWarga TEXT NULL;"),
                ("CreatedAt", "ALTER TABLE Warga ADD COLUMN CreatedAt TEXT NULL;"),
                ("UpdatedAt", "ALTER TABLE Warga ADD COLUMN UpdatedAt TEXT NULL;"),
                ("GolonganDarah", "ALTER TABLE Warga ADD COLUMN GolonganDarah TEXT NULL;"),
                ("NomorHP", "ALTER TABLE Warga ADD COLUMN NomorHP TEXT NULL;"),
                ("NamaAyah", "ALTER TABLE Warga ADD COLUMN NamaAyah TEXT NULL;"),
                ("NamaIbu", "ALTER TABLE Warga ADD COLUMN NamaIbu TEXT NULL;"),
                ("NoKK", "ALTER TABLE Warga ADD COLUMN NoKK TEXT NULL;"),
                ("AlamatDetail", "ALTER TABLE Warga ADD COLUMN AlamatDetail TEXT NULL;"),
                ("TanggalStatus", "ALTER TABLE Warga ADD COLUMN TanggalStatus TEXT NULL;"),
                ("KeteranganWarga", "ALTER TABLE Warga ADD COLUMN KeteranganWarga TEXT NULL;"),
                ("StatusKeluarga", "ALTER TABLE Warga ADD COLUMN StatusKeluarga TEXT NULL;")
            };

            foreach (var migration in migrations)
            {
                if (existingColumns.Any(c => c.Equals(migration.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                await _connection.ExecuteAsync(migration.Ddl, transaction: null);
                _logger.LogInformation("Migrasi Warga: kolom {Column} ditambahkan.", migration.Name);
            }

            // Baris lama tidak punya nilai status bertanggal; isi kosong agar
            // pembaca model baru tidak pernah mendapat null pada data lama.
            // Status warga yang belum pernah diisi berarti warga tetap tinggal.
            await _connection.ExecuteAsync("UPDATE Warga SET TanggalStatus = '' WHERE TanggalStatus IS NULL");
            await _connection.ExecuteAsync("UPDATE Warga SET KeteranganWarga = '' WHERE KeteranganWarga IS NULL");
            await _connection.ExecuteAsync(
                "UPDATE Warga SET StatusWarga = 'AKTIF' WHERE StatusWarga IS NULL OR StatusWarga = ''");

            await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK)");
            await _connection.ExecuteAsync(
                "CREATE TRIGGER IF NOT EXISTS tr_warga_updated AFTER UPDATE ON Warga " +
                "FOR EACH ROW BEGIN " +
                "UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga; " +
                "END;");
        }

        private void ValidateWargaData(WargaData wargaData)
        {
            if (wargaData == null)
                throw new ArgumentNullException(nameof(wargaData));

            if (!wargaData.IsValid(out var wargaErrors))
            {
                _logger.LogWarning("Validasi WargaData gagal untuk NIK: {NIK}. Errors: {Errors}",
                    wargaData.NIK, string.Join("; ", wargaErrors));
                throw new ValidationException($"Data warga tidak valid: {string.Join("; ", wargaErrors)}");
            }

            // Validasi NIK khusus untuk kasus instansi atau kematian
            if (wargaData.IsForInstansi)
            {
                if (wargaData.NIK != DummyNikInstansi)
                {
                    throw new ArgumentException($"NIK untuk instansi harus {DummyNikInstansi}.");
                }
            }
            else if (wargaData.IsForKematian)
            {
                if (wargaData.NIK != DummyNikKematian)
                {
                    throw new ArgumentException($"NIK untuk kematian harus {DummyNikKematian}.");
                }

                if (string.IsNullOrWhiteSpace(wargaData.TempatLahir))
                    wargaData.TempatLahir = "Tidak diketahui";

                if (string.IsNullOrWhiteSpace(wargaData.TanggalLahir))
                    wargaData.TanggalLahir = "01-01-1900";
            }
            else
            {
                // Validasi NIK untuk kasus non-dummy
                if (string.IsNullOrWhiteSpace(wargaData.NIK) ||
                    wargaData.NIK == DummyNikInstansi ||
                    wargaData.NIK == DummyNikKematian)
                {
                    throw new ArgumentException("NIK tidak valid atau menggunakan NIK dummy untuk kasus non-instansi/kematian.");
                }

                // Validasi format NIK (16 digit angka)
                if (wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit))
                {
                    throw new ValidationException("NIK harus terdiri dari 16 digit angka.");
                }
            }
        }

        public async Task<int> AddOrUpdateWargaAsync(
            WargaData wargaData,
            SqliteConnection? existingConnection = null,
            IDbTransaction? existingTransaction = null)
        {
            if (wargaData == null)
                throw new ArgumentNullException(nameof(wargaData));

            ValidateWargaData(wargaData);

            // Normalisasi SEBELUM objek dicache: tanpa ini entri cache membawa
            // null dan pembacaan berikutnya melaporkan status warga kosong.
            wargaData.StatusWarga = StatusWargaTipe.Normalisasi(wargaData.StatusWarga);

            var connection = existingConnection ?? _connection;
            var transaction = existingTransaction;

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var existingWarga = !string.IsNullOrWhiteSpace(wargaData.NIK)
                    ? await connection.QueryFirstOrDefaultAsync<WargaData>(
                        "SELECT * FROM Warga WHERE NIK = @NIK",
                        new { wargaData.NIK },
                        transaction)
                    : null;

                int idWarga;
                if (existingWarga != null)
                {
                    idWarga = existingWarga.ID_Warga;
                    await UpdateWargaInternalAsync(idWarga, wargaData, connection, transaction!);
                }
                else
                {
                    idWarga = await InsertWargaInternalAsync(wargaData, connection, transaction!);
                }

                if (!IsDummyNik(wargaData.NIK!))
                {
                    await CacheWargaAsync(wargaData.NIK!, idWarga, wargaData);
                }

                return idWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AddOrUpdateWargaAsync");
                throw new DataAccessException("Gagal menambah/memperbarui data warga", ex);
            }
        }

        private async Task<int> InsertWargaInternalAsync(WargaData wargaData, IDbConnection connection, IDbTransaction transaction)
        {
            var nikToInsert = wargaData.NIK ?? (wargaData.IsForInstansi ? DummyNikInstansi : wargaData.IsForKematian ? DummyNikKematian : throw new ArgumentException("NIK wajib diisi."));

            var insertData = new
            {
                NIK = nikToInsert,
                Nama = wargaData.Nama ?? string.Empty,
                TempatLahir = wargaData.TempatLahir ?? string.Empty,
                TanggalLahir = wargaData.TanggalLahir ?? string.Empty,
                JenisKelamin = wargaData.JenisKelamin ?? string.Empty,
                Agama = wargaData.Agama ?? string.Empty,
                StatusPerkawinan = wargaData.StatusPerkawinan ?? string.Empty,
                StatusKeluarga = wargaData.StatusKeluarga ?? string.Empty,
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                RT = wargaData.RT ?? string.Empty,
                RW = wargaData.RW ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI",
                GolonganDarah = wargaData.GolonganDarah ?? string.Empty,
                NomorHP = wargaData.NomorHP ?? string.Empty,
                StatusWarga = wargaData.StatusWarga ?? StatusWargaTipe.Aktif,
                NamaAyah = wargaData.NamaAyah ?? string.Empty,
                NamaIbu = wargaData.NamaIbu ?? string.Empty,
                NoKK = wargaData.NoKK ?? string.Empty,
                AlamatDetail = wargaData.AlamatDetail ?? string.Empty,
                TanggalStatus = wargaData.TanggalStatus ?? string.Empty,
                KeteranganWarga = wargaData.KeteranganWarga ?? string.Empty
            };

            var query = @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin,
                   Agama, StatusPerkawinan, StatusKeluarga, Pekerjaan, Dusun, RT, RW, Desa, Kecamatan, Kabupaten,
                   Pendidikan, Kewarganegaraan, GolonganDarah, NomorHP, StatusWarga,
                   NamaAyah, NamaIbu, NoKK, AlamatDetail, TanggalStatus, KeteranganWarga)
                VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama,
                        @StatusPerkawinan, @StatusKeluarga, @Pekerjaan, @Dusun, @RT, @RW, @Desa, @Kecamatan, @Kabupaten,
                        @Pendidikan, @Kewarganegaraan, @GolonganDarah, @NomorHP, @StatusWarga,
                        @NamaAyah, @NamaIbu, @NoKK, @AlamatDetail, @TanggalStatus, @KeteranganWarga);
                SELECT last_insert_rowid();";

            return await connection.ExecuteScalarAsync<int>(query, insertData, transaction);
        }

        private async Task<int> UpdateWargaInternalAsync(int idWarga, WargaData wargaData, SqliteConnection connection, IDbTransaction transaction)
        {
            if (!string.IsNullOrWhiteSpace(wargaData.NIK) && !IsDummyNik(wargaData.NIK))
            {
                var existingNikWarga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT ID_Warga, Nama FROM Warga WHERE NIK = @NIK AND ID_Warga != @ID_Warga_Param",
                    new { wargaData.NIK, ID_Warga_Param = idWarga },
                    transaction);

                if (existingNikWarga != null)
                {
                    throw new ValidationException($"NIK '{wargaData.NIK}' sudah digunakan oleh warga lain: {existingNikWarga.Nama}.");
                }
            }

            var query = @"UPDATE Warga
                SET NIK = @NIK, Nama = @Nama, TempatLahir = @TempatLahir,
                    TanggalLahir = @TanggalLahir, JenisKelamin = @JenisKelamin,
                    Agama = @Agama, StatusPerkawinan = @StatusPerkawinan,
                    StatusKeluarga = @StatusKeluarga,
                    Pekerjaan = @Pekerjaan, Dusun = @Dusun, RT = @RT, RW = @RW, Desa = @Desa,
                    Kecamatan = @Kecamatan, Kabupaten = @Kabupaten,
                    Pendidikan = @Pendidikan, Kewarganegaraan = @Kewarganegaraan,
                    GolonganDarah = @GolonganDarah, NomorHP = @NomorHP, StatusWarga = @StatusWarga,
                    NamaAyah = @NamaAyah, NamaIbu = @NamaIbu, NoKK = @NoKK,
                    AlamatDetail = @AlamatDetail, TanggalStatus = @TanggalStatus,
                    KeteranganWarga = @KeteranganWarga
                WHERE ID_Warga = @ID_Warga_Param";

            var updateData = new
            {
                ID_Warga_Param = idWarga,
                NIK = wargaData.NIK ?? (wargaData.IsForInstansi ? DummyNikInstansi : wargaData.IsForKematian ? DummyNikKematian : throw new ArgumentException("NIK wajib diisi.")),
                Nama = wargaData.Nama ?? string.Empty,
                TempatLahir = wargaData.TempatLahir ?? string.Empty,
                TanggalLahir = wargaData.TanggalLahir ?? string.Empty,
                JenisKelamin = wargaData.JenisKelamin ?? string.Empty,
                Agama = wargaData.Agama ?? string.Empty,
                StatusPerkawinan = wargaData.StatusPerkawinan ?? string.Empty,
                StatusKeluarga = wargaData.StatusKeluarga ?? string.Empty,
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                RT = wargaData.RT ?? string.Empty,
                RW = wargaData.RW ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI",
                GolonganDarah = wargaData.GolonganDarah ?? string.Empty,
                NomorHP = wargaData.NomorHP ?? string.Empty,
                StatusWarga = wargaData.StatusWarga ?? StatusWargaTipe.Aktif,
                NamaAyah = wargaData.NamaAyah ?? string.Empty,
                NamaIbu = wargaData.NamaIbu ?? string.Empty,
                NoKK = wargaData.NoKK ?? string.Empty,
                AlamatDetail = wargaData.AlamatDetail ?? string.Empty,
                TanggalStatus = wargaData.TanggalStatus ?? string.Empty,
                KeteranganWarga = wargaData.KeteranganWarga ?? string.Empty
            };

            var rowsAffected = await connection.ExecuteAsync(query, updateData, transaction);

            if (rowsAffected > 0)
            {
                // Jumlah warga (ringkasan Beranda) ikut berubah setiap ada penulisan
                // baris warga — termasuk baris dummy yang dipakai surat tanpa data warga.
                await _cacheService.RemoveAsync<int?>("Warga_Count");
            }

            if (rowsAffected > 0 && !IsDummyNik(wargaData.NIK!))
            {
                wargaData.ID_Warga = idWarga;
                wargaData.AlamatLengkap = BuildAlamatLengkap(wargaData);

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                };

                await _cacheService.SetAsync($"Warga_{wargaData.NIK}", wargaData, cacheOptions);
                await _cacheService.SetAsync($"Warga_Id_{idWarga}", wargaData, cacheOptions);
                await _cacheService.RemoveAsync<WargaData>($"Warga_Id_{idWarga}");
                await _cacheService.RemoveByPrefixAsync("Warga_Search_");
            }

            return rowsAffected;
        }

        private async Task CacheWargaAsync(string nik, int idWarga, WargaData wargaData)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            };

            await _cacheService.SetAsync($"Warga_{nik}", wargaData, cacheOptions);
            await _cacheService.SetAsync($"Warga_Id_{idWarga}", wargaData, cacheOptions);
        }

        public async Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction? transaction = null)
        {
            if (string.IsNullOrWhiteSpace(nik))
            {
                _logger.LogWarning("GetWargaByNikAsync called with null or empty NIK.");
                return null!;
            }

            string cacheKey = $"Warga_{nik}";
            var cachedWarga = await _cacheService.GetAsync<WargaData>(cacheKey);
            if (cachedWarga != null)
            {
                _logger.LogInformation("Warga data retrieved from cache for NIK: {NIK}", nik);
                return cachedWarga;
            }

            var conn = connection ?? _connection;
            try
            {
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<WargaData>(
                    @"SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, 
                             StatusPerkawinan, Pekerjaan, Dusun, Desa, Kecamatan, Kabupaten, 
                             Pendidikan, Kewarganegaraan 
                      FROM Warga WHERE NIK = @NIK",
                    new { NIK = nik },
                    transaction);

                if (result != null)
                {
                    result.AlamatLengkap = BuildAlamatLengkap(result);

                    if (!IsDummyNik(nik))
                    {
                        await _cacheService.SetAsync(cacheKey, result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                    _logger.LogInformation("Warga data retrieved from database and cached for NIK: {NIK}, Nama: {Nama}", nik, result.Nama);
                }
                else
                {
                    _logger.LogInformation("No Warga data found for NIK: {NIK}", nik);
                }

                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Warga data for NIK: {NIK}", nik);
                throw new DataRetrievalException($"Failed to retrieve Warga data for NIK: {nik}", ex);
            }
        }

        public async Task<List<WargaData>> AmbilDaftarByNikAsync(IEnumerable<string> daftarNik, CancellationToken ct = default)
        {
            if (daftarNik == null) return new List<WargaData>();

            var nik = daftarNik
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (nik.Count == 0) return new List<WargaData>();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                // Dapper memperluas daftar ke placeholders berparameter,
                // jadi nilai NIK tidak pernah dirangkai ke dalam SQL.
                var hasil = new List<WargaData>();
                const int ukuranBatch = 500;
                for (int i = 0; i < nik.Count; i += ukuranBatch)
                {
                    ct.ThrowIfCancellationRequested();

                    var potongan = nik.Skip(i).Take(ukuranBatch).ToList();
                    var baris = (await _connection.QueryAsync<WargaData>(
                        @"SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                                 StatusPerkawinan, Pekerjaan, Dusun, RT, RW, Desa, Kecamatan, Kabupaten,
                                 Pendidikan, Kewarganegaraan, GolonganDarah, NomorHP, StatusWarga
                          FROM Warga
                         WHERE NIK IN @Nik",
                        new { Nik = potongan })).ToList();

                    foreach (var w in baris)
                    {
                        w.AlamatLengkap = BuildAlamatLengkap(w);
                        hasil.Add(w);
                    }
                }

                return hasil;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca {Jumlah} warga berdasarkan daftar NIK", nik.Count);
                throw new DataRetrievalException("Gagal membaca data warga berdasarkan daftar NIK", ex);
            }
        }

        /// <summary>
        /// Satu query agregat untuk seluruh angka ringkasan kependudukan:
        /// total/status/jenis kelamin/KK/per RT. Angka "penduduk" hanya
        /// menghitung yang masih tinggal di desa (AKTIF + BARU); seluruh warga
        /// terdata tetap dilaporkan terpisah supaya laporan tidak menyesatkan.
        /// </summary>
        public async Task<WargaStatistikRingkasan> GetStatistikWargaAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "Warga_Statistik";
            var cached = await _cacheService.GetAsync<WargaStatistikRingkasan>(cacheKey, cancellationToken);
            if (cached != null) return cached;

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var hasil = new WargaStatistikRingkasan();

                var baris = await _connection.QueryAsync<(
                    string? Status, string? Jk, string? Rt, string? NoKk,
                    string? TanggalLahir, string? Pendidikan, string? Agama, string? StatusPerkawin)>(
                    @"SELECT COALESCE(NULLIF(StatusWarga,''), 'AKTIF'), JenisKelamin, RT, NoKK,
                             TanggalLahir, Pendidikan, Agama, StatusPerkawinan
                      FROM Warga WHERE " + SyaratBukanDummy,
                    commandTimeout: 30);

                var perRt = new Dictionary<string, (int Jumlah, int L, int P)>(StringComparer.OrdinalIgnoreCase);
                var perPendidikan = new Dictionary<string, int>(StringComparer.Ordinal);
                var perAgama = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var perPerkawinan = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var perKelompokUsia = new Dictionary<string, int>(StringComparer.Ordinal);
                var kkUnik = new HashSet<string>(StringComparer.Ordinal);
                int tanpaKk = 0;

                // Kantong usia 5 tahunan gaya BPS; kantong "(tidak diketahui)"
                // SELALU ada supaya pembaca laporan tidak perlu null-check.
                string KantongUsia(string? tanggalLahir)
                {
                    if (!CobaParseTanggalLahir(tanggalLahir, out var lahir))
                    {
                        hasil.UsiaTidakDiketahui++;
                        return "(tidak diketahui)";
                    }

                    int usia = DateTime.Today.Year - lahir.Year;
                    if (lahir.Date > DateTime.Today.AddYears(-usia)) usia--;
                    if (usia < 0) usia = 0;
                    return usia >= 65 ? "65+" : $"{usia / 5 * 5}-{usia / 5 * 5 + 4}";
                }

                foreach (var (status, jk, rt, noKk, tanggalLahir, pendidikan, agama, perkawinan) in baris)
                {
                    var statusRapi = StatusWargaTipe.Normalisasi(status);
                    // Jenis kelamin menerima bentuk lengkap maupun singkat (L/P)
                    // karena data lama dan impor menyimpan keduanya.
                    bool laki = string.Equals(jk?.Trim(), "Laki-laki", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(jk?.Trim(), "L", StringComparison.OrdinalIgnoreCase);
                    bool perempuan = string.Equals(jk?.Trim(), "Perempuan", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(jk?.Trim(), "P", StringComparison.OrdinalIgnoreCase);

                    hasil.TotalSeluruh++;
                    if (laki) hasil.LakiLaki++;
                    if (perempuan) hasil.Perempuan++;

                    switch (statusRapi)
                    {
                        case StatusWargaTipe.Aktif:
                            hasil.TotalAktif++;
                            break;
                        case StatusWargaTipe.Baru:
                            hasil.TotalBaru++;
                            break;
                        case StatusWargaTipe.Pindah:
                            hasil.TotalPindah++;
                            break;
                        case StatusWargaTipe.Meninggal:
                            hasil.TotalMeninggal++;
                            break;
                    }

                    // Penduduk = yang masih tinggal di desa (aktif/baru).
                    if (StatusWargaTipe.Menghitung.Contains(statusRapi, StringComparer.Ordinal))
                    {
                        if (laki) hasil.LakiLakiPenduduk++;
                        else if (perempuan) hasil.PerempuanPenduduk++;
                        else hasil.PendudukJenisKelaminTidakDiketahui++;
                    }

                    if (!string.IsNullOrWhiteSpace(rt))
                    {
                        var kunci = rt!.Trim();
                        var (jml, l, p) = perRt.TryGetValue(kunci, out var v) ? v : (0, 0, 0);
                        perRt[kunci] = (jml + 1, l + (laki ? 1 : 0), p + (perempuan ? 1 : 0));
                    }

                    // KK dihitung unik dari kolom NoKK warga; baris tanpa NoKK
                    // dihitung sebagai "tanpa KK".
                    if (string.IsNullOrWhiteSpace(noKk))
                        tanpaKk++;
                    else
                        kkUnik.Add(noKk.Trim());

                    // Kelompok laporan: pendidikan ke jenjang BPS, agama & status
                    // perkawinan apa adanya, usia ke kantong 5 tahunan.
                    var jenjang = JenjangPendidikanBps.Tentukan(pendidikan);
                    perPendidikan[jenjang] = perPendidikan.GetValueOrDefault(jenjang) + 1;

                    if (!string.IsNullOrWhiteSpace(agama))
                    {
                        var kunciAgama = agama!.Trim();
                        perAgama[kunciAgama] = perAgama.GetValueOrDefault(kunciAgama) + 1;
                    }

                    if (!string.IsNullOrWhiteSpace(perkawinan))
                    {
                        var kunciKawin = perkawinan!.Trim();
                        perPerkawinan[kunciKawin] = perPerkawinan.GetValueOrDefault(kunciKawin) + 1;
                    }

                    var kantong = KantongUsia(tanggalLahir);
                    perKelompokUsia[kantong] = perKelompokUsia.GetValueOrDefault(kantong) + 1;
                }

                // Kantong "(tidak diketahui)" SELALU ada — walau kosong — supaya
                // pembaca laporan tidak perlu berurusan dengan baris hilang.
                perKelompokUsia.TryAdd("(tidak diketahui)", 0);

                foreach (var (rt, v) in perRt)
                {
                    hasil.PerRt = hasil.PerRt.Append(new WargaStatistikBaris
                    {
                        Kunci = rt,
                        Jumlah = v.Jumlah,
                        LakiLaki = v.L,
                        Perempuan = v.P
                    }).ToList();
                }

                hasil.PerPendidikan = UrutkanJenjang(perPendidikan);
                hasil.PerAgama = perAgama
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                hasil.PerStatusPerkawinan = perPerkawinan
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Kantong usia diurutkan numerik; kantong tak diketahui selalu
                // diletakkan paling akhir agar tabel laporan terbaca berurut.
                hasil.PerKelompokUsia = perKelompokUsia
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci == "(tidak diketahui)" ? 1 : 0)
                    .ThenBy(b => int.TryParse((b.Kunci ?? "0").Split('-')[0], out var bawah) ? bawah : int.MaxValue)
                    .ToList();

                hasil.JumlahKartuKeluarga = kkUnik.Count;
                hasil.TanpaKartuKeluarga = tanpaKk;

                await _cacheService.SetAsync(cacheKey, hasil, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                }, cancellationToken);

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun statistik warga");
                throw new DataRetrievalException("Gagal menyusun statistik warga", ex);
            }
        }

        private static IReadOnlyList<WargaStatistikBaris> UrutkanJenjang(Dictionary<string, int> perJenjang)
        {
            var daftar = new List<WargaStatistikBaris>();
            foreach (var jenjang in JenjangPendidikanBps.Lista)
            {
                if (perJenjang.TryGetValue(jenjang, out var jumlah))
                {
                    daftar.Add(new WargaStatistikBaris { Kunci = jenjang, Jumlah = jumlah });
                }
            }

            // Jenjang di luar daftar baku tetap dilaporkan di akhir.
            foreach (var (jenjang, jumlah) in perJenjang)
            {
                if (!JenjangPendidikanBps.Lista.Contains(jenjang))
                {
                    daftar.Add(new WargaStatistikBaris { Kunci = jenjang, Jumlah = jumlah });
                }
            }

            return daftar;
        }

        /// <summary>Baca tanggal lahir dalam format umum (ISO, dd-MM-yyyy, titik, garis miring).</summary>
        private static bool CobaParseTanggalLahir(string? teks, out DateTime tanggal)
        {
            tanggal = default;
            if (string.IsNullOrWhiteSpace(teks)) return false;

            string[] format = { "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss" };
            return DateTime.TryParseExact(
                teks!.Trim(), format, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out tanggal);
        }

        public async Task<WargaData> GetWargaByIdAsync(int idWarga)
        {
            if (idWarga <= 0)
            {
                _logger.LogWarning("GetWargaByIdAsync called with invalid ID: {ID}", idWarga);
                return null!;
            }

            string cacheKey = $"Warga_Id_{idWarga}";
            var cachedWarga = await _cacheService.GetAsync<WargaData>(cacheKey);
            if (cachedWarga != null)
            {
                _logger.LogInformation("Warga data retrieved from cache for ID: {ID_Warga}", idWarga);
                return cachedWarga;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var result = await _connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE ID_Warga = @ID_Warga",
                    new { ID_Warga = idWarga });

                if (result != null)
                {
                    result.AlamatLengkap = BuildAlamatLengkap(result);

                    if (!IsDummyNik(result.NIK!))
                    {
                        await _cacheService.SetAsync(cacheKey, result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                        await _cacheService.SetAsync($"Warga_{result.NIK}", result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Warga by ID: {ID_Warga}", idWarga);
                throw new DataRetrievalException($"Failed to retrieve Warga by ID: {idWarga}", ex);
            }
        }

        public async Task<int> CountWargaAsync()
        {
            const string cacheKey = "Warga_Count";
            var cached = await _cacheService.GetAsync<int?>(cacheKey);
            if (cached.HasValue)
            {
                return cached.Value;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                int jumlah = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");

                await _cacheService.SetAsync(cacheKey, jumlah, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return jumlah;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghitung jumlah warga");
                return 0;
            }
        }

        public async Task<IEnumerable<WargaData>> GetAllWargaAsync()
        {
            const string cacheKey = "Warga_All";
            var cachedWargaList = await _cacheService.GetAsync<List<WargaData>>(cacheKey);
            if (cachedWargaList != null)
            {
                _logger.LogInformation("All Warga data retrieved from cache.");
                return cachedWargaList;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                const string query = @"
                    SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                           Agama, StatusPerkawinan, Pekerjaan, Dusun, Desa, Kecamatan, Kabupaten,
                           Pendidikan, Kewarganegaraan
                    FROM Warga
                    ORDER BY Nama";

                var wargaList = (await _connection.QueryAsync<WargaData>(query)).ToList();

                foreach (var warga in wargaList)
                {
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.SetAsync($"Warga_{warga.NIK}", warga, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                await _cacheService.SetAsync(cacheKey, wargaList, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return wargaList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil semua data warga.");
                throw new DataRetrievalException("Gagal mengambil semua data warga.", ex);
            }
        }

        public async Task<IEnumerable<WargaData>> SearchWargaAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogWarning("SearchWargaAsync called with null or empty search term.");
                return new List<WargaData>();
            }

            string cacheKey = $"Warga_Search_{searchTerm.ToLowerInvariant()}";
            var cachedResults = await _cacheService.GetAsync<List<WargaData>>(cacheKey);
            if (cachedResults != null)
            {
                _logger.LogInformation("Search Warga results retrieved from cache for term: {SearchTerm}", searchTerm);
                return cachedResults;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var query = @"
                    SELECT * FROM Warga
                    WHERE NIK LIKE @SearchTerm OR Nama LIKE @SearchTerm
                    ORDER BY Nama";

                var results = (await _connection.QueryAsync<WargaData>(
                    query,
                    new { SearchTerm = $"%{searchTerm}%" })).ToList();

                foreach (var warga in results)
                {
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.SetAsync($"Warga_{warga.NIK}", warga, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                await _cacheService.SetAsync(cacheKey, results, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencari warga dengan term: {SearchTerm}", searchTerm);
                throw new DataRetrievalException($"Gagal mencari warga dengan term: {searchTerm}", ex);
            }
        }

        public async Task<bool> DeleteWargaAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("ID harus lebih besar dari 0.", nameof(id));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var warga = await GetWargaByIdAsync(id);
                if (warga == null)
                {
                    _logger.LogWarning("Tidak ada warga ditemukan dengan ID: {ID}", id);
                    return false;
                }

                var rowsAffected = await _connection.ExecuteAsync(
                    "DELETE FROM Warga WHERE ID_Warga = @ID",
                    new { ID = id });

                if (rowsAffected > 0)
                {
                    await _cacheService.RemoveAsync<int?>("Warga_Count");
                    await _cacheService.RemoveAsync<WargaData>($"Warga_Id_{id}");
                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.RemoveAsync<WargaData>($"Warga_{warga.NIK}");
                        await _cacheService.RemoveAsync<WargaData>($"Warga_{warga.NIK}");
                        await _cacheService.RemoveByPrefixAsync("Warga_Search_");
                    }
                    _logger.LogInformation("Warga dengan ID: {ID} berhasil dihapus", id);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus warga dengan ID: {ID}, Message: {Message}", id, ex.Message);
                throw new DataAccessException($"Gagal menghapus warga dengan ID: {id}", ex);
            }
        }

        public async Task<string> GetAlamatByNikAsync(string nik)
        {
            var warga = await GetWargaByNikAsync(nik);
            return warga != null ? BuildAlamatLengkap(warga) : null!;
        }

        private bool IsDummyNik(string nik) => nik == DummyNikKematian || nik == DummyNikInstansi;

        private string BuildAlamatLengkap(WargaData warga)
        {
            if (warga == null) return string.Empty;

            var alamatParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(warga.Dusun))
                alamatParts.Add(warga.Dusun);
            if (!string.IsNullOrWhiteSpace(warga.Desa))
                alamatParts.Add(warga.Desa);
            if (!string.IsNullOrWhiteSpace(warga.Kecamatan))
                alamatParts.Add(warga.Kecamatan);
            if (!string.IsNullOrWhiteSpace(warga.Kabupaten))
                alamatParts.Add(warga.Kabupaten);

            return string.Join(", ", alamatParts);
        }
        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData)
        {
            return await AddOrUpdateWargaAsync(wargaData);
        }

        public async Task<int> GetOrCreateDummyWargaAsync(SqliteConnection connection, IDbTransaction transaction)
        {
            const string dummyNik = "9999999999999999";
            const string dummyName = "INSTANSI DUMMY";

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var existing = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE NIK = @NIK",
                    new { NIK = dummyNik },
                    transaction);

                if (existing != null)
                {
                    return existing.ID_Warga;
                }

                var dummyWarga = new WargaData
                {
                    NIK = dummyNik,
                    Nama = dummyName,
                    TempatLahir = "N/A",
                    TanggalLahir = "1900-01-01",
                    JenisKelamin = "N/A",
                    Dusun = "N/A",
                    Desa = "N/A",
                    Kecamatan = "N/A",
                    Kabupaten = "N/A",
                    Agama = "N/A",
                    StatusPerkawinan = "N/A",
                    Pekerjaan = "N/A",
                    Pendidikan = "N/A",
                    Kewarganegaraan = "N/A",
                    IsForInstansi = true
                };

                var idWarga = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                       Dusun, Desa, Kecamatan, Kabupaten, Agama, StatusPerkawinan, Pekerjaan, Pendidikan, Kewarganegaraan)
                     VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, 
                             @Dusun, @Desa, @Kecamatan, @Kabupaten, @Agama, @StatusPerkawinan, 
                             @Pekerjaan, @Pendidikan, @Kewarganegaraan);
                     SELECT last_insert_rowid();",
                    dummyWarga, transaction);

                if (idWarga <= 0)
                    throw new DataAccessException("Gagal membuat dummy warga (Instansi).");

                return idWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create dummy warga (Instansi)");
                throw new DataAccessException("Failed to get or create dummy warga (Instansi)", ex);
            }
        }

        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection connection, IDbTransaction transaction)
        {
            return await AddOrUpdateWargaAsync(wargaData, connection, transaction);
        }

        // =====================================================================
        // Daftar halaman, ekspor, mutasi status, dan Kartu Keluarga
        // =====================================================================

        /// <summary>Syarat SQL yang selalu menyaring baris dummy Instansi/Kematian.</summary>
        private const string SyaratBukanDummy =
            "(NIK IS NULL OR NIK NOT IN ('9999999999999999','0000000000000000'))";

        /// <summary>Susun syarat WHERE + parameter dari filter halaman/ekspor. Semua opsional.</summary>
        private static void BangunFilterWhere(WargaFilter filter, List<string> syarat, Dictionary<string, object> param)
        {
            if (!string.IsNullOrWhiteSpace(filter.Cari))
            {
                // Pencarian bebas mencakup NIK, nama, No KK, alamat, nomor HP,
                // dan RT — operator mengetik \"003\" untuk menemukan alamat.
                syarat.Add("(NIK LIKE @Cari OR Nama LIKE @Cari OR NoKK LIKE @Cari OR AlamatDetail LIKE @Cari OR NomorHP LIKE @Cari OR RT LIKE @Cari)");
                param["Cari"] = $"%{filter.Cari.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                syarat.Add("UPPER(COALESCE(StatusWarga,'')) = @Status");
                param["Status"] = filter.Status.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(filter.RT))
            {
                syarat.Add("RT = @RT");
                param["RT"] = filter.RT.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filter.RW))
            {
                syarat.Add("RW = @RW");
                param["RW"] = filter.RW.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filter.Dusun))
            {
                // Dropdown wilayah menyimpan dusun huruf besar sebagai kunci.
                syarat.Add("UPPER(COALESCE(Dusun,'')) = @Dusun");
                param["Dusun"] = filter.Dusun.Trim().ToUpperInvariant();
            }
        }

        public async Task<WargaPagedResult> GetWargaPageAsync(WargaFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new WargaFilter();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var syarat = new List<string> { SyaratBukanDummy };
                var param = new Dictionary<string, object>();
                BangunFilterWhere(filter, syarat, param);
                string where = "WHERE " + string.Join(" AND ", syarat);

                int total = await _connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM Warga {where}", param);

                int ukuran = filter.UkuranHalamanEfektif;
                int totalHalaman = Math.Max(1, (int)Math.Ceiling(total / (double)ukuran));
                // Halaman melebihi batas dikembalikan ke halaman terakhir supaya
                // operator tidak pernah melihat tabel kosong karena salah klik.
                int halaman = Math.Min(Math.Max(1, filter.HalamanEfektif), totalHalaman);

                var items = (await _connection.QueryAsync<WargaData>(
                    $"SELECT * FROM Warga {where} " +
                    "ORDER BY COALESCE(Nama,'') COLLATE NOCASE, ID_Warga " +
                    "LIMIT @Ambil OFFSET @Lewati",
                    new Dictionary<string, object>(param)
                    {
                        ["Ambil"] = ukuran,
                        ["Lewati"] = (halaman - 1) * ukuran
                    })).ToList();

                foreach (var warga in items)
                {
                    warga.StatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);
                }

                return new WargaPagedResult
                {
                    Items = items,
                    Total = total,
                    Halaman = halaman,
                    UkuranHalaman = ukuran
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca halaman daftar warga");
                throw new DataRetrievalException("Gagal membaca halaman daftar warga", ex);
            }
        }

        public async Task<List<WargaData>> GetWargaForExportAsync(WargaFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new WargaFilter();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var syarat = new List<string> { SyaratBukanDummy };
                var param = new Dictionary<string, object>();
                BangunFilterWhere(filter, syarat, param);

                var hasil = (await _connection.QueryAsync<WargaData>(
                    "SELECT * FROM Warga WHERE " + string.Join(" AND ", syarat) +
                    " ORDER BY COALESCE(Nama,'') COLLATE NOCASE",
                    param)).ToList();

                foreach (var warga in hasil)
                {
                    warga.StatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);
                }

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan ekspor daftar warga");
                throw new DataRetrievalException("Gagal menyiapkan ekspor daftar warga", ex);
            }
        }

        public async Task<bool> UbahStatusWargaAsync(
            int idWarga, string status, string? tanggalStatus, string? keterangan,
            CancellationToken cancellationToken = default)
        {
            if (idWarga <= 0)
                throw new ArgumentException("ID warga tidak sah.", nameof(idWarga));

            // Status tidak dikenal ditolak: salah ketik tidak boleh merusak
            // data status warga yang membedakan penduduk dari arsip.
            if (!StatusWargaTipe.Valid(status))
                throw new ArgumentException($"Status '{status}' tidak dikenal.", nameof(status));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                int baris = await _connection.ExecuteAsync(
                    @"UPDATE Warga
                      SET StatusWarga = @Status,
                          TanggalStatus = @Tanggal,
                          KeteranganWarga = @Keterangan
                      WHERE ID_Warga = @ID",
                    new
                    {
                        Status = StatusWargaTipe.Normalisasi(status),
                        Tanggal = tanggalStatus ?? string.Empty,
                        Keterangan = keterangan ?? string.Empty,
                        ID = idWarga
                    });

                if (baris > 0)
                {
                    await _cacheService.RemoveAsync<int?>("Warga_Count");
                    await _cacheService.RemoveAsync<WargaData>($"Warga_Id_{idWarga}");
                    await _cacheService.RemoveByPrefixAsync("Warga_Search_");
                    await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
                }

                return baris > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status warga ID {ID}", idWarga);
                throw new DataAccessException($"Gagal mengubah status warga ID {idWarga}", ex);
            }
        }

        /// <summary>Tabel KartuKeluarga dibuat bila belum ada (database lama).</summary>
        private async Task EnsureKartuKeluargaTableAsync()
        {
            var ada = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KartuKeluarga'");
            if (ada > 0) return;

            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS KartuKeluarga (
                    NoKK TEXT PRIMARY KEY,
                    NamaKepalaKeluarga TEXT,
                    Alamat TEXT,
                    RT TEXT,
                    RW TEXT,
                    Dusun TEXT,
                    Desa TEXT,
                    Kecamatan TEXT,
                    Kabupaten TEXT,
                    NomorHP TEXT,
                    Catatan TEXT,
                    JumlahAnggota INTEGER NOT NULL DEFAULT 0,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )");
        }

        public async Task SaveKartuKeluargaAsync(KartuKeluargaData kartuKeluarga, CancellationToken cancellationToken = default)
        {
            if (kartuKeluarga == null)
                throw new ArgumentNullException(nameof(kartuKeluarga));
            if (string.IsNullOrWhiteSpace(kartuKeluarga.NoKK))
                throw new ArgumentException("Nomor Kartu Keluarga wajib diisi.", nameof(kartuKeluarga));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                await EnsureKartuKeluargaTableAsync();

                await _connection.ExecuteAsync(@"
                    INSERT INTO KartuKeluarga
                        (NoKK, NamaKepalaKeluarga, Alamat, RT, RW, Dusun, Desa, Kecamatan, Kabupaten, NomorHP, Catatan, UpdatedAt)
                    VALUES
                        (@NoKK, @NamaKepalaKeluarga, @Alamat, @RT, @RW, @Dusun, @Desa, @Kecamatan, @Kabupaten, @NomorHP, @Catatan, CURRENT_TIMESTAMP)
                    ON CONFLICT(NoKK) DO UPDATE SET
                        NamaKepalaKeluarga = excluded.NamaKepalaKeluarga,
                        Alamat = excluded.Alamat,
                        RT = excluded.RT,
                        RW = excluded.RW,
                        Dusun = excluded.Dusun,
                        Desa = excluded.Desa,
                        Kecamatan = excluded.Kecamatan,
                        Kabupaten = excluded.Kabupaten,
                        NomorHP = excluded.NomorHP,
                        Catatan = excluded.Catatan,
                        UpdatedAt = CURRENT_TIMESTAMP",
                    kartuKeluarga);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan Kartu Keluarga {NoKK}", kartuKeluarga.NoKK);
                throw new DataAccessException("Gagal menyimpan Kartu Keluarga", ex);
            }
        }

        public async Task<KartuKeluargaData?> GetKartuKeluargaAsync(string noKk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(noKk)) return null;

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                await EnsureKartuKeluargaTableAsync();

                var kk = await _connection.QueryFirstOrDefaultAsync<KartuKeluargaData>(
                    "SELECT * FROM KartuKeluarga WHERE NoKK = @NoKK",
                    new { NoKK = noKk.Trim() });
                if (kk == null) return null;

                // Jumlah anggota selalu dihitung dari tabel Warga (sumber sebenarnya).
                kk.JumlahAnggota = await _connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Warga WHERE NoKK = @NoKK AND " + SyaratBukanDummy,
                    new { kk.NoKK });

                return kk;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca Kartu Keluarga {NoKK}", noKk);
                throw new DataRetrievalException("Gagal membaca Kartu Keluarga", ex);
            }
        }

        public async Task<List<WargaData>> GetAnggotaKeluargaAsync(string noKk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(noKk)) return new List<WargaData>();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var hasil = (await _connection.QueryAsync<WargaData>(
                    "SELECT * FROM Warga WHERE NoKK = @NoKK AND " + SyaratBukanDummy +
                    " ORDER BY COALESCE(Nama,'') COLLATE NOCASE",
                    new { NoKK = noKk.Trim() })).ToList();

                foreach (var warga in hasil)
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca anggota Kartu Keluarga {NoKK}", noKk);
                throw new DataRetrievalException("Gagal membaca anggota Kartu Keluarga", ex);
            }
        }

        public async Task<IEnumerable<string>> GetDaftarNilaiWilayahAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                async Task<List<string>> AmbilAsync(string kolom, string? awalan)
                {
                    var rows = await _connection.QueryAsync<string>(new CommandDefinition(
                        $"SELECT DISTINCT TRIM({kolom}) FROM Warga " +
                        $"WHERE {kolom} IS NOT NULL AND TRIM({kolom}) <> '' " +
                        $"ORDER BY TRIM({kolom})",
                        cancellationToken: cancellationToken));

                    var hasilKolom = new List<string>();
                    foreach (var v in rows)
                    {
                        if (string.IsNullOrWhiteSpace(v)) continue;
                        var teks = v.Trim();
                        // Awalan "RT"/"RW" dipakai halaman Data Warga untuk memisahkan
                        // nilai RT dan RW yang keduanya sering hanya berupa angka.
                        hasilKolom.Add(awalan == null ? teks : awalan + " " + teks);
                    }
                    return hasilKolom;
                }

                var hasil = new List<string>();
                hasil.AddRange(await AmbilAsync("RT", "RT"));
                hasil.AddRange(await AmbilAsync("RW", "RW"));
                hasil.AddRange(await AmbilAsync("Dusun", null));

                return hasil
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil daftar nilai wilayah warga");
                return Array.Empty<string>();
            }
        }

        public async Task<Dictionary<int, int>> GetJumlahSuratSemuaWargaAsync(CancellationToken cancellationToken = default)
        {
            var hasil = new Dictionary<int, int>();
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var baris = await _connection.QueryAsync(new CommandDefinition(
                    @"SELECT ID_Warga, COUNT(*) AS Jumlah
                      FROM Surat
                      WHERE ID_Warga IS NOT NULL AND ID_Warga > 0
                      GROUP BY ID_Warga",
                    cancellationToken: cancellationToken));

                foreach (var b in baris)
                {
                    int id = Convert.ToInt32(b.ID_Warga);
                    if (id > 0) hasil[id] = Convert.ToInt32(b.Jumlah);
                }
            }
            catch (Exception ex)
            {
                // Kolom "Jumlah Surat" hanyalah pelengkap ekspor: kegagalan hitung
                // tidak boleh menggagalkan seluruh berkas, jadi cukup dicatat.
                _logger.LogError(ex, "Gagal menghitung jumlah surat per warga");
            }

            return hasil;
        }

    }
}
