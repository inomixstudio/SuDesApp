using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.Data;

namespace SuDesApp.Data.Repositories
{
    public interface IJenisSuratRepository
    {
        Task InitializeJenisSuratDataAsync();
        Task<JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis);
        Task<JenisSuratKelas> GetByNamaAsync(string namaJenis);
        Task<int> GetIdJenisSuratByNamaAsync(string namaJenis);
        Task<string> GenerateNomorSuratAsync(string kodeJenis);
        Task<bool> IsNomorSuratExistsAsync(string nomorSurat);
        Task<List<JenisSuratKelas>> GetAllJenisSuratAsync();
        Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync();
        Task<List<string>> GetJenisSuratKeteranganDesa();
        Task<int?> GetLastSuratIdByTypeAsync(string templateName);
        Task<List<string>> GetAvailableYearsAsync();
        Task<bool> HasExistingNomorSuratAsync(string namaJenis);

        Task<HashSet<string>> GetSharedNumberingGroupAsync();
        Task RefreshConfigurationAsync();
    }

    public class JenisSuratRepository : BaseRepository<JenisSuratKelas>, IJenisSuratRepository
    {
        private readonly ICacheService _cacheService;
        private readonly IJenisSuratConfigLoader _configLoader;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(2);

        // PERBAIKAN: Hapus hard-coded grup, gunakan konfigurasi dinamis
        private HashSet<string>? _cachedSharedNumberingGroup;
        private readonly SemaphoreSlim _configSemaphore = new(1, 1);

        public JenisSuratRepository(
            IUnitOfWork uow,
            ILogger<JenisSuratRepository> logger,
            ICacheService cacheService,
            IJenisSuratConfigLoader configLoader)
            : base(uow, logger)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        }

        protected override string TableName => "JenisSurat";
        protected override string IdColumnName => "ID_Jenis";

        public async Task InitializeJenisSuratDataAsync()
        {
            _logger.LogInformation("Memulai inisialisasi data JenisSurat...");

            try
            {
                await SqlMapper.ExecuteAsync(
                    _uow.Connection,
                    @"CREATE TABLE IF NOT EXISTS JenisSurat (
                        ID_Jenis INTEGER PRIMARY KEY AUTOINCREMENT,
                        NamaJenis TEXT NOT NULL UNIQUE,
                        KodeJenis TEXT NOT NULL UNIQUE
                    )",
                    null, // param
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                var jenisSuratConfigs = await _configLoader.LoadConfigAsync();

                // Rekonsiliasi idempoten: tambahkan jenis yang belum ada dari konfigurasi
                // (menutup celah DB lama yang hanya di-seed sebagian, mis. AHLI_WARIS).
                var insertedCount = await SqlMapper.ExecuteAsync(
                    _uow.Connection,
                    @"INSERT INTO JenisSurat (NamaJenis, KodeJenis) VALUES (@NamaJenis, @KodeJenis)
                      ON CONFLICT(NamaJenis) DO NOTHING",
                    jenisSuratConfigs,
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                if (insertedCount > 0)
                {
                    _logger.LogInformation("Berhasil menambahkan {Count} jenis surat yang belum ada", insertedCount);
                }

                var countJenisSurat = await SqlMapper.ExecuteScalarAsync<int>(
                    _uow.Connection,
                    "SELECT COUNT(*) FROM JenisSurat",
                    null, // param
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                _logger.LogInformation("Total jenis surat saat ini: {Count}", countJenisSurat);

                // Clear all related caches
                await ClearAllConfigCacheAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menginisialisasi data JenisSurat");
                throw new DataAccessException("Gagal menginisialisasi data JenisSurat", ex);
            }
        }

        public async Task<JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));

            string cacheKey = $"JenisSurat_{namaJenis.ToUpperInvariant()}";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var result = await SqlMapper.QueryFirstOrDefaultAsync<JenisSuratKelas>(
                    _uow.Connection,
                    "SELECT ID_Jenis, NamaJenis, KodeJenis FROM JenisSurat WHERE UPPER(NamaJenis) = @NamaJenis",
                    new { NamaJenis = namaJenis.ToUpperInvariant() },
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                return result ?? throw new DataRetrievalException($"Jenis surat '{namaJenis}' tidak ditemukan.");
            });
        }

        public async Task<JenisSuratKelas> GetByNamaAsync(string namaJenis)
        {
            return await GetJenisSuratByNamaAsync(namaJenis);
        }

        public async Task<int> GetIdJenisSuratByNamaAsync(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));

            string cacheKey = $"JenisSurat_Id_{namaJenis.ToUpperInvariant()}";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                return await SqlMapper.ExecuteScalarAsync<int>(
                    _uow.Connection,
                    "SELECT ID_Jenis FROM JenisSurat WHERE UPPER(NamaJenis) = @NamaJenis",
                    new { NamaJenis = namaJenis.ToUpperInvariant() },
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );
            });
        }

        public async Task<string> GenerateNomorSuratAsync(string kodeJenis)
        {
            if (string.IsNullOrWhiteSpace(kodeJenis))
                throw new ArgumentNullException(nameof(kodeJenis), "Kode jenis surat tidak boleh kosong.");

            _logger.LogInformation("Generate nomor surat untuk KodeJenis: {KodeJenis}", kodeJenis);

            try
            {
                var config = await _configLoader.GetConfigByKodeJenisAsync(kodeJenis);
                if (config == null)
                    throw new ArgumentException($"Kode jenis '{kodeJenis}' tidak valid", nameof(kodeJenis));

                // PERBAIKAN: Gunakan konfigurasi dinamis untuk menentukan shared numbering
                bool isInGroup = config.IsSharedNumbering;
                string currentYear = DateTime.Now.Year.ToString();

                _logger.LogInformation("Config: {NamaJenis}, Shared: {IsInGroup}, Tahun: {CurrentYear}",
                    config.NamaJenis, isInGroup, currentYear);

                int nextNumber = await GetNextDocumentNumberAsync(config, isInGroup, currentYear);
                _logger.LogInformation("Next number calculated: {NextNumber}", nextNumber);

                string newNomor = await GenerateUniqueDocumentNumberAsync(config, nextNumber, currentYear);

                _logger.LogInformation("Nomor surat berhasil digenerate: {NomorSurat}", newNomor);
                return newNomor;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal generate nomor surat untuk KodeJenis: {KodeJenis}", kodeJenis);
                throw new DataRetrievalException("Gagal generate nomor surat", ex);
            }
        }

        public async Task<bool> IsNomorSuratExistsAsync(string nomorSurat)
        {
            if (string.IsNullOrWhiteSpace(nomorSurat))
                throw new ArgumentException("Nomor surat tidak boleh kosong.", nameof(nomorSurat));

            string cacheKey = $"NomorSurat_Exists_{nomorSurat}";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var count = await SqlMapper.ExecuteScalarAsync<int>(
                    _uow.Connection,
                    "SELECT COUNT(*) FROM Surat WHERE NomorSurat = @NomorSurat",
                    new { NomorSurat = nomorSurat },
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );
                return count > 0;
            }, TimeSpan.FromMinutes(10));
        }

        public async Task<List<JenisSuratKelas>> GetAllJenisSuratAsync()
        {
            const string cacheKey = "JenisSurat_All";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var result = await SqlMapper.QueryAsync<JenisSuratKelas>(
                    _uow.Connection,
                    "SELECT ID_Jenis, NamaJenis, KodeJenis FROM JenisSurat ORDER BY NamaJenis",
                    null, // param
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                return result?.ToList() ?? new List<JenisSuratKelas>();
            });
        }

        public async Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync()
        {
            const string cacheKey = "JenisSurat_DisplayNames";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var configs = await _configLoader.LoadConfigAsync();
                return configs.ToDictionary(
                    c => c.NamaJenis,
                    c => c.DisplayName,
                    StringComparer.OrdinalIgnoreCase);
            });
        }

        // PERBAIKAN: Gunakan konfigurasi dinamis untuk keterangan desa
        public async Task<List<string>> GetJenisSuratKeteranganDesa()
        {
            const string cacheKey = "JenisSurat_KeteranganDesa";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var configs = await _configLoader.LoadConfigAsync();
                return configs
                    .Where(c => c.IsKeteranganDesa)
                    .Select(c => c.NamaJenis)
                    .ToList();
            });
        }

        // PERBAIKAN: Method baru untuk mendapatkan grup shared numbering dari konfigurasi
        public async Task<HashSet<string>> GetSharedNumberingGroupAsync()
        {
            if (_cachedSharedNumberingGroup != null)
                return _cachedSharedNumberingGroup;

            await _configSemaphore.WaitAsync();
            try
            {
                if (_cachedSharedNumberingGroup != null)
                    return _cachedSharedNumberingGroup;

                const string cacheKey = "JenisSurat_SharedNumberingGroup";
                _cachedSharedNumberingGroup = await GetWithCacheAsync(cacheKey, async () =>
                {
                    var configs = await _configLoader.LoadConfigAsync();
                    return configs
                        .Where(c => c.IsSharedNumbering)
                        .Select(c => c.NamaJenis)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                });

                return _cachedSharedNumberingGroup;
            }
            finally
            {
                _configSemaphore.Release();
            }
        }

        public async Task<int?> GetLastSuratIdByTypeAsync(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                throw new ArgumentException("Nama template surat tidak boleh kosong.", nameof(templateName));

            try
            {
                return await SqlMapper.QueryFirstOrDefaultAsync<int?>(
                    _uow.Connection,
                    @"SELECT MAX(s.ID_Surat) 
                      FROM Surat s
                      JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                      WHERE js.NamaJenis = @TemplateName",
                    new { TemplateName = templateName },
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil ID surat terakhir untuk template: {TemplateName}", templateName);
                throw new DataRetrievalException($"Gagal mengambil ID surat terakhir untuk template {templateName}", ex);
            }
        }

        public async Task<List<string>> GetAvailableYearsAsync()
        {
            const string cacheKey = "AvailableYears";
            return await GetWithCacheAsync(cacheKey, async () =>
            {
                var years = await SqlMapper.QueryAsync<string>(
                    _uow.Connection,
                    @"SELECT DISTINCT strftime('%Y', TanggalSurat) AS Year 
                      FROM Surat 
                      WHERE TanggalSurat IS NOT NULL
                      ORDER BY Year DESC",
                    null, // param
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                var yearList = years.ToList();
                var currentYear = DateTime.Now.Year.ToString();

                if (!yearList.Contains(currentYear))
                    yearList.Insert(0, currentYear);

                return yearList;
            }, TimeSpan.FromHours(1), new List<string> { DateTime.Now.Year.ToString() });
        }

        // PERBAIKAN: Method untuk refresh konfigurasi
        public async Task RefreshConfigurationAsync()
        {
            await _configLoader.RefreshConfigAsync();
            await ClearAllConfigCacheAsync();

            // Reset cached shared numbering group
            await _configSemaphore.WaitAsync();
            try
            {
                _cachedSharedNumberingGroup = null;
            }
            finally
            {
                _configSemaphore.Release();
            }

            _logger.LogInformation("Configuration refreshed successfully");
        }

        #region Private Helper Methods

        private async Task<T> GetWithCacheAsync<T>(
            string cacheKey,
            Func<Task<T>> factory,
            TimeSpan? customExpiration = null,
            T? fallbackValue = default)
        {
            try
            {
                var cached = await _cacheService.GetAsync<T>(cacheKey);
                if (cached != null) return cached;

                var result = await factory();

                await _cacheService.SetAsync(cacheKey, result, new MemoryCacheEntryOptions
                {
                    Priority = CacheItemPriority.High,
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = customExpiration ?? _cacheExpiration
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetWithCacheAsync for key: {CacheKey}", cacheKey);

                // Try fallback cache
                var fallbackCache = await _cacheService.GetAsync<T>(cacheKey);
                if (fallbackCache != null) return fallbackCache;

                // Use fallback value if provided
                if (fallbackValue != null) return fallbackValue;

                throw;
            }
        }

        // PERBAIKAN: Gunakan konfigurasi dinamis untuk menentukan grup shared numbering
        private async Task<int> GetNextDocumentNumberAsync(JenisSuratConfig config, bool isInGroup, string currentYear)
        {
            string sql;
            object parameters;

            if (isInGroup)
            {
                // PERBAIKAN: Dapatkan grup dari konfigurasi, bukan hard-coded
                var sharedGroup = await GetSharedNumberingGroupAsync();
                var filterList = sharedGroup.ToList();
                var inClause = string.Join(", ", filterList.Select((_, i) => $"@Filter{i}"));

                sql = $@"
                    SELECT 
                        COALESCE(MAX(
                            CAST(
                                SUBSTR(
                                    s.NomorSurat,
                                    INSTR(s.NomorSurat, '/') + 1,
                                    INSTR(SUBSTR(s.NomorSurat, INSTR(s.NomorSurat, '/') + 1), '/') - 1
                                ) AS INTEGER
                            )
                        ), 0) + 1
                    FROM Surat s
                    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                    WHERE js.NamaJenis IN ({inClause})
                      AND s.NomorSurat LIKE '%/Ds/' || @Tahun
                      AND LENGTH(TRIM(s.NomorSurat)) > 0";

                var dynamicParams = new DynamicParameters();
                dynamicParams.Add("@Tahun", currentYear);

                for (int i = 0; i < filterList.Count; i++)
                {
                    dynamicParams.Add($"@Filter{i}", filterList[i]);
                }

                parameters = dynamicParams;
            }
            else
            {
                sql = $@"
                    SELECT 
                        COALESCE(MAX(
                            CAST(
                                SUBSTR(
                                    s.NomorSurat,
                                    INSTR(s.NomorSurat, '/') + 1,
                                    INSTR(SUBSTR(s.NomorSurat, INSTR(s.NomorSurat, '/') + 1), '/') - 1
                                ) AS INTEGER
                            )
                        ), 0) + 1
                    FROM Surat s
                    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                    WHERE js.KodeJenis = @Filter
                      AND s.NomorSurat LIKE '%/Ds/' || @Tahun
                      AND LENGTH(TRIM(s.NomorSurat)) > 0";

                parameters = new
                {
                    Filter = config.KodeJenis,
                    Tahun = currentYear
                };
            }

            try
            {
                _logger.LogDebug("Executing next number query: {sql}", sql);
                var result = await SqlMapper.QueryFirstOrDefaultAsync<int>(
                    _uow.Connection,
                    sql,
                    parameters,
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                _logger.LogInformation("Result from next number query: {result}", result);

                return result > 0 ? result : 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get next document number");
                return 1;
            }
        }

        private async Task<string> GenerateUniqueDocumentNumberAsync(JenisSuratConfig config, int currentNumber, string currentYear)
        {
            const int maxRetries = 10;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    string candidateNumber = string.Format(config.NomorFormat, currentNumber, config.KodeJenis, currentYear);

                    bool exists = await IsNomorSuratExistsAsync(candidateNumber);

                    if (!exists)
                    {
                        return candidateNumber;
                    }

                    _logger.LogWarning("Nomor surat {NomorSurat} sudah ada, mencoba nomor selanjutnya", candidateNumber);
                    currentNumber++;
                    retryCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GenerateUniqueDocumentNumberAsync");
                    retryCount++;
                }
            }

            throw new InvalidOperationException($"Gagal menghasilkan nomor surat unik setelah {maxRetries} percobaan");
        }

        private async Task ClearAllConfigCacheAsync()
        {
            await _cacheService.RemoveAsync<object>("JenisSurat_All");
            await _cacheService.RemoveAsync<object>("JenisSurat_DisplayNames");
            await _cacheService.RemoveAsync<object>("JenisSurat_KeteranganDesa");
            await _cacheService.RemoveAsync<object>("JenisSurat_SharedNumberingGroup");
            await _cacheService.RemoveByPrefixAsync("JenisSurat_");
        }

        #endregion

        #region BaseRepository Implementation

        public override async Task<IEnumerable<JenisSuratKelas>> GetPagedAsync(int pageNumber, int pageSize, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return await SqlMapper.QueryAsync<JenisSuratKelas>(
                _uow.Connection,
                $"SELECT * FROM {TableName} ORDER BY {IdColumnName} LIMIT @PageSize OFFSET @Offset",
                new { PageSize = pageSize, Offset = (pageNumber - 1) * pageSize },
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
        }

        public override async Task<int> InsertAsync(JenisSuratKelas entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return await SqlMapper.ExecuteScalarAsync<int>(
                _uow.Connection,
                $@"INSERT INTO {TableName} (NamaJenis, KodeJenis) VALUES (@NamaJenis, @KodeJenis);
                   SELECT last_insert_rowid();",
                entity,
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
        }

        public override async Task<int> InsertBatchAsync(IEnumerable<JenisSuratKelas> entities, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return await SqlMapper.ExecuteAsync(
                _uow.Connection,
                $@"INSERT INTO {TableName} (NamaJenis, KodeJenis) VALUES (@NamaJenis, @KodeJenis)",
                entities,
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
        }

        public override async Task<bool> UpdateAsync(JenisSuratKelas entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            var affectedRows = await SqlMapper.ExecuteAsync(
                _uow.Connection,
                $@"UPDATE {TableName} SET NamaJenis = @NamaJenis, KodeJenis = @KodeJenis WHERE {IdColumnName} = @ID_Jenis",
                entity,
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
            return affectedRows > 0;
        }

        public override async Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            var affectedRows = await SqlMapper.ExecuteAsync(
                _uow.Connection,
                $"DELETE FROM {TableName} WHERE {IdColumnName} = @Id",
                new { Id = id },
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
            return affectedRows > 0;
        }

        public override async Task<int> CountAsync(IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return await SqlMapper.ExecuteScalarAsync<int>(
                _uow.Connection,
                $"SELECT COUNT(*) FROM {TableName}",
                null, // param
                transaction: transaction ?? _uow.CurrentTransaction,
                null, // commandTimeout
                null  // commandType
            );
        }

        public async Task<bool> HasExistingNomorSuratAsync(string kodeJenis)
        {
            if (string.IsNullOrWhiteSpace(kodeJenis))
                throw new ArgumentException("Kode jenis surat tidak boleh kosong.", nameof(kodeJenis));

            try
            {
                var count = await SqlMapper.ExecuteScalarAsync<int>(
                    _uow.Connection,
                    @"SELECT COUNT(*) 
                    FROM Surat s
                    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                    WHERE js.KodeJenis = @KodeJenis",
                    new { KodeJenis = kodeJenis },
                    _uow.CurrentTransaction,
                    null, // commandTimeout
                    null  // commandType
                );

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa keberadaan nomor surat untuk jenis: {KodeJenis}", kodeJenis);
                throw new DataRetrievalException($"Gagal memeriksa keberadaan nomor surat untuk jenis {kodeJenis}", ex);
            }
        }

        #endregion
    }
}
