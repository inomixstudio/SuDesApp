using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IWargaRepository
    {
        Task InitializeWargaTableAsync();
        Task<int> AddOrUpdateWargaAsync(WargaData wargaData, SqliteConnection? existingConnection = null, IDbTransaction existingTransaction = null);
        Task<WargaData> GetWargaByIdAsync(int idWarga);
        Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction transaction = null);
        Task<IEnumerable<WargaData>> GetAllWargaAsync();
        Task<IEnumerable<WargaData>> SearchWargaAsync(string searchTerm);
        Task<bool> DeleteWargaAsync(int id);
        Task<string> GetAlamatByNikAsync(string nik);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData);
        Task<int> GetOrCreateDummyWargaAsync(SqliteConnection connection, IDbTransaction transaction);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection connection, IDbTransaction transaction);
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
                            Desa TEXT,
                            Kecamatan TEXT,
                            Kabupaten TEXT,
                            Pendidikan TEXT,
                            Kewarganegaraan TEXT,
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
                ("CreatedAt", "ALTER TABLE Warga ADD COLUMN CreatedAt TEXT NULL;"),
                ("UpdatedAt", "ALTER TABLE Warga ADD COLUMN UpdatedAt TEXT NULL;")
            };

            foreach (var migration in migrations)
            {
                if (existingColumns.Any(c => c.Equals(migration.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                await _connection.ExecuteAsync(migration.Ddl, transaction: null);
                _logger.LogInformation("Migrasi Warga: kolom {Column} ditambahkan.", migration.Name);
            }

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
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
            };

            var query = @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                   Agama, StatusPerkawinan, Pekerjaan, Dusun, Desa, Kecamatan, Kabupaten, 
                   Pendidikan, Kewarganegaraan)
                VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama, 
                        @StatusPerkawinan, @Pekerjaan, @Dusun, @Desa, @Kecamatan, @Kabupaten, 
                        @Pendidikan, @Kewarganegaraan);
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
                    Pekerjaan = @Pekerjaan, Dusun = @Dusun, Desa = @Desa, 
                    Kecamatan = @Kecamatan, Kabupaten = @Kabupaten, 
                    Pendidikan = @Pendidikan, Kewarganegaraan = @Kewarganegaraan
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
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
            };

            var rowsAffected = await connection.ExecuteAsync(query, updateData, transaction);

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

        public async Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction transaction = null)
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

    }
}
