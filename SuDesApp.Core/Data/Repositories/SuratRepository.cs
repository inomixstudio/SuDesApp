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
    public interface ISuratRepository
    {
        Task<int> InsertAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> UpdateStatusAsync(int id, string status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<SuratData> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<SuratData>> GetFilteredAsync(FilterConditions filters, string sortBy, bool ascending, int skip, int take, CancellationToken cancellationToken = default);
        Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default);
        Task InitializeSuratIndexesAsync();
        Task<List<string>> GetJenisSuratKeteranganDesaAsync();
        Task RefreshJenisSuratConfigurationAsync();
        Task<IEnumerable<SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
        Task<int> AddSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
        Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default);
        Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
        Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default);
        Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<Dictionary<string, object>> GetDatabaseStatsAsync();
        Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default);
        Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default);
        Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default);
        Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default);
    }

    public class SuratRepository : ISuratRepository
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
        /// Helper method untuk mendapatkan data dari cache atau repository
        /// </summary>
        private async Task<T> GetFromCacheOrRepositoryAsync<T>(
            string cacheKey,
            Func<Task<T>> repositoryOperation,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var cached = await _cacheService.GetAsync<T>(cacheKey, cancellationToken);
                if (cached != null)
                {
                    _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
                    return cached;
                }

                _logger.LogDebug("Cache miss for key: {CacheKey}, fetching from repository", cacheKey);
                var result = await repositoryOperation();
                
                if (result != null)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = expiration ?? _cacheExpiration,
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                    };
                    await _cacheService.SetAsync(cacheKey, result, cacheOptions, cancellationToken);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cache operation for key: {CacheKey}", cacheKey);
                // Fallback to repository operation if cache fails
                return await repositoryOperation();
            }
        }

        /// <summary>
        /// Helper method untuk invalidate cache related to surat
        /// </summary>
        private async Task InvalidateSuratCacheAsync(int suratId, CancellationToken cancellationToken = default)
        {
            var cacheKeys = new[]
            {
                $"SuDesApp:SuratData:Surat_{suratId}",
                $"SuDesApp:SuratData:SuratData_{suratId}",
                "SuDesApp:SuratData:All",
                "SuDesApp:SuratStatistics"
            };

            foreach (var key in cacheKeys)
            {
                try
                {
                    await _cacheService.RemoveAsync<string>(key, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invalidate cache key: {CacheKey}", key);
                }
            }
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
                "CREATE INDEX IF NOT EXISTS idx_surat_status ON Surat(Status);"
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
                ("Surat", "UpdatedAt", "ALTER TABLE Surat ADD COLUMN UpdatedAt TEXT NULL;"),
                ("Instansi", "PimpinanInstansi", "ALTER TABLE Instansi ADD COLUMN PimpinanInstansi TEXT NULL;"),
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
            suratData.NamaJenis = jenisSurat.NamaJenis;
            suratData.SetJenisFromNamaJenis(suratData.NamaJenis);
        }

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
                suratData.NamaJenis = jenisSurat.NamaJenis;
                suratData.SetJenisFromNamaJenis(suratData.NamaJenis);

                if (string.IsNullOrWhiteSpace(suratData.Status))
                    suratData.Status = "Draft";

                if (string.IsNullOrWhiteSpace(suratData.NomorSurat))
                    suratData.NomorSurat = await _jenisSuratRepository.GenerateNomorSuratAsync(suratData.KodeJenis);
                else if (await CheckNomorSuratExistsAsync(suratData.NomorSurat, null, cancellationToken))
                    throw new ValidationException($"Nomor surat '{suratData.NomorSurat}' already used.");

                await ValidateSuratDataAsync(suratData, cancellationToken);

                int idWarga = await GetOrCreateWargaAsync(suratData, transaction, cancellationToken);

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
                await InsertRelatedDataSafeAsync(suratId, suratData, transaction, cancellationToken);

                if (ownTransaction)
                    transaction.Commit();

                // Riwayat aktivitas: catat pembuat surat (email Google / admin)
                // SETELAH commit agar operasi yang gagal/rollback tidak tercatat.
                _activityLog?.Log(suratData.NamaJenis ?? "Surat", $"{suratData.NomorSurat}", "Buat",
                    string.IsNullOrWhiteSpace(suratData.Warga?.Nama) ? null : $"Pemohon: {suratData.Warga.Nama}");

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
                    transaction.Rollback();
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
                await ValidateSuratDataAsync(suratData, cancellationToken);

                var existing = await GetByIdAsync(suratData.ID_Surat, transaction, cancellationToken);
                if (existing == null)
                    throw new DataRetrievalException($"Surat with ID {suratData.ID_Surat} not found.");

                if (!string.IsNullOrWhiteSpace(suratData.NomorSurat) && suratData.NomorSurat != existing.NomorSurat)
                    if (await CheckNomorSuratExistsAsync(suratData.NomorSurat, suratData.ID_Surat, cancellationToken))
                        throw new ValidationException($"Nomor surat '{suratData.NomorSurat}' already used.");

                int idWarga = await GetOrCreateWargaAsync(suratData, transaction, cancellationToken);

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
                    await DeleteRelatedDataAsync(suratData.ID_Surat, suratData.NamaJenis, transaction, cancellationToken);
                    await InsertRelatedDataSafeAsync(suratData.ID_Surat, suratData, transaction, cancellationToken);

                    if (ownTransaction)
                        transaction.Commit();

                    // Riwayat aktivitas: catat pengedit surat SETELAH commit.
                    _activityLog?.Log(suratData.NamaJenis ?? "Surat", $"{suratData.NomorSurat}", "Edit",
                        $"Status: {suratData.Status ?? existing.Status}");

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
                    transaction.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction.Rollback();
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
                        transaction.Commit();

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
                    transaction.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction.Rollback();
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

                await DeleteRelatedDataAsync(id, surat.NamaJenis, transaction, cancellationToken);

                var query = _queryProvider.GetQuery("DeleteSurat");
                var rowsAffected = await _connection.ExecuteAsync(query, new { ID_Surat = id }, transaction);

                if (rowsAffected > 0)
                {
                    if (ownTransaction)
                        transaction.Commit();

                    // Riwayat aktivitas: catat penghapus surat.
                    _activityLog?.Log(surat.NamaJenis ?? "Surat", $"{surat.NomorSurat}", "Hapus",
                        $"Status terakhir: {surat.Status}");

                    _cachedSuratData.TryRemove(id, out _);
                    await _cacheService.RemoveAsync<SuratData>($"Surat_{id}", cancellationToken);
                    await ClearListCacheAsync();
                    return true;
                }

                if (ownTransaction)
                    transaction.Rollback();
                return false;
            }
            catch (Exception ex)
            {
                if (ownTransaction)
                    transaction.Rollback();
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
                            Desa = w.Desa,
                            Kecamatan = w.Kecamatan,
                            Kabupaten = w.Kabupaten,
                            Pendidikan = w.Pendidikan,
                            Kewarganegaraan = w.Kewarganegaraan,
                            AlamatLengkap = BuildAlamatLengkap(w.Dusun, w.Desa, w.Kecamatan, w.Kabupaten)
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
                await LoadRelatedDataAsync(surat, transaction, cancellationToken);
                await _cacheService.SetAsync(cacheKey, surat, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                }, cancellationToken);
                _cachedSuratData[id] = surat;
                return surat;
            }
            return null;
        }

        private async Task LoadRelatedDataAsync(SuratData surat, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(surat.NamaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.LoadRelatedDataAsync(surat, _connection);
        }

        public async Task<IEnumerable<SuratData>> GetFilteredAsync(
    FilterConditions filters,
    string sortBy,
    bool ascending,
    int skip,
    int take,
    CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var (whereClause, parameters) = await BuildWhereClauseAsync(filters);
            var orderBy = GetValidSortColumn(sortBy);
            var direction = ascending ? "ASC" : "DESC";
            var baseQuery = _queryProvider.GetQuery("GetFilteredSuratData");

            var query = baseQuery
                .Replace("{WhereClause}", whereClause)
                .Replace("{OrderBy}", orderBy)
                .Replace("{Direction}", direction);

            parameters.Add("Take", take);
            parameters.Add("Skip", skip);

            try
            {
                // ? GUNAKAN PEMETAAN MANUAL DENGAN KONVERSI YANG AMAN
                var dynamicResults = await _connection.QueryAsync(query, parameters);
                var results = new List<SuratData>();

                foreach (var row in dynamicResults)
                {
                    try
                    {
                        var surat = new SuratData
                        {
                            ID_Surat = SafeGetInt(row.ID_Surat),
                            ID_Jenis = SafeGetInt(row.ID_Jenis),
                            NomorSurat = SafeGetString(row.NomorSurat),
                            TanggalSurat = SafeGetDateTime(row.TanggalSurat),
                            Keterangan = SafeGetString(row.Keterangan),
                            Keperluan = SafeGetString(row.Keperluan),
                            AdditionalData = SafeGetString(row.AdditionalData),
                            Status = SafeGetString(row.Status, "Draft"),
                            CreatedAt = SafeGetDateTime(row.CreatedAt, DateTime.Now),
                            KodeJenis = SafeGetString(row.KodeJenis),
                            NamaJenis = SafeGetString(row.NamaJenis)
                        };

                        // ? MAP WARGA DATA DENGAN KONVERSI AMAN
                        if (HasProperty(row, "ID_Warga") && SafeGetInt(row.ID_Warga) > 0)
                        {
                            surat.Warga = new WargaData
                            {
                                ID_Warga = SafeGetInt(row.ID_Warga),
                                NIK = SafeGetString(row.NIK),
                                Nama = SafeGetString(row.Nama),
                                TempatLahir = SafeGetString(row.TempatLahir),
                                TanggalLahir = SafeGetString(row.TanggalLahir),
                                JenisKelamin = SafeGetString(row.JenisKelamin),
                                Agama = SafeGetString(row.Agama),
                                StatusPerkawinan = SafeGetString(row.StatusPerkawinan),
                                Pekerjaan = SafeGetString(row.Pekerjaan),
                                Dusun = SafeGetString(row.Dusun),
                                Desa = SafeGetString(row.Desa),
                                Kecamatan = SafeGetString(row.Kecamatan),
                                Kabupaten = SafeGetString(row.Kabupaten),
                                Pendidikan = SafeGetString(row.Pendidikan),
                                Kewarganegaraan = SafeGetString(row.Kewarganegaraan),
                                CreatedAt = SafeGetDateTime(row.CreatedAt, DateTime.Now),
                                UpdatedAt = SafeGetDateTime(row.UpdatedAt, DateTime.Now)
                            };
                        }

                        // ? MAP INSTANSI JIKA ADA
                        if (HasProperty(row, "NamaInstansi") && !string.IsNullOrEmpty(SafeGetString(row.NamaInstansi)))
                        {
                            surat.Instansi = new Instansi
                            {
                                NamaInstansi = SafeGetString(row.NamaInstansi),
                                AlamatInstansi = SafeGetString(row.AlamatInstansi),
                                PimpinanInstansi = SafeGetString(row.PimpinanInstansi)
                            };
                        }

                        // ? MAP NTCR DATA (N1-N4) bila jenis surat termasuk NTCR
                        if (IsNtcrJenis(surat.NamaJenis))
                        {
                            await AttachNtcrDataAsync(surat, cancellationToken);
                        }

                        results.Add(surat);
                    }
                    catch (Exception ex)
                    {
                        //_logger.LogError(ex, "Error mapping row for surat ID: {ID}", SafeGetInt(row.ID_Surat));
                        // Continue with next row instead of failing completely
                    }
                }

                return results;
            }
            catch (SqliteException ex)
            {
                _logger.LogError(ex, "SQLite error in GetFilteredAsync");
                throw;
            }
        }

        // ? HELPER METHODS UNTUK KONVERSI AMAN
        private static int SafeGetInt(object value, int defaultValue = 0)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return (int)longValue;

            if (int.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        private static string SafeGetString(object value, string defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            return value.ToString();
        }

        private static string BuildAlamatLengkap(string dusun, string desa, string kecamatan, string kabupaten)
        {
            var parts = new[]
            {
                (dusun ?? string.Empty).Trim(),
                (desa ?? string.Empty).Trim(),
                (kecamatan ?? string.Empty).Trim(),
                (kabupaten ?? string.Empty).Trim()
            };

            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static DateTime SafeGetDateTime(object value, DateTime? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue ?? DateTime.MinValue;

            if (value is DateTime dateTimeValue)
                return dateTimeValue;

            if (DateTime.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue ?? DateTime.MinValue;
        }

        private static bool HasProperty(dynamic obj, string propertyName)
        {
            try
            {
                var dict = obj as IDictionary<string, object>;
                return dict?.ContainsKey(propertyName) ?? false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(string, DynamicParameters)> BuildWhereClauseAsync(FilterConditions filters)
        {
            var conditions = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrEmpty(filters.JenisSurat))
            {
                if (filters.JenisSurat.Equals("GROUP_KETERANGAN_DESA", StringComparison.OrdinalIgnoreCase))
                {
                    var jenisGroup = await _jenisSuratRepository.GetJenisSuratKeteranganDesa();
                    _logger.LogDebug("JenisGroup for GROUP_KETERANGAN_DESA: {@JenisGroup}", jenisGroup);
                    if (jenisGroup?.Any() == true)
                    {
                        conditions.Add($"UPPER(js.NamaJenis) IN @JenisGroup");
                        parameters.Add("JenisGroup", jenisGroup.Select(j => j.ToUpperInvariant()));
                    }
                    else
                    {
                        _logger.LogWarning("No jenis surat found for GROUP_KETERANGAN_DESA, skipping filter.");
                    }
                }
                else if (filters.JenisSurat.Equals("GROUP_NTCR", StringComparison.OrdinalIgnoreCase))
                {
                    var ntcrGroup = new[] { SuratConstants.NTCR_N1, SuratConstants.NTCR_N2, SuratConstants.NTCR_N3, SuratConstants.NTCR_N4 };
                    conditions.Add("UPPER(js.NamaJenis) IN @JenisGroup");
                    parameters.Add("JenisGroup", ntcrGroup.Select(j => j.ToUpperInvariant()));
                }
                else if (filters.JenisSurat != "ALL_TYPES")
                {
                    conditions.Add("UPPER(js.NamaJenis) = @JenisSurat");
                    parameters.Add("JenisSurat", filters.JenisSurat.ToUpperInvariant());
                }
            }

            if (filters.ExcludeJenisNames?.Any() == true)
            {
                conditions.Add("UPPER(js.NamaJenis) NOT IN @ExcludeJenis");
                parameters.Add("ExcludeJenis", filters.ExcludeJenisNames.Select(n => n.ToUpperInvariant()));
            }

            if (!string.IsNullOrEmpty(filters.Tahun))
            {
                conditions.Add("strftime('%Y', s.TanggalSurat) = @Tahun");
                parameters.Add("Tahun", filters.Tahun);
            }

            if (!string.IsNullOrEmpty(filters.Status))
            {
                conditions.Add("s.Status = @Status");
                parameters.Add("Status", filters.Status);
            }

            if (!string.IsNullOrEmpty(filters.SearchText))
            {
                var searchText = $"%{filters.SearchText}%";
                conditions.Add("(s.NomorSurat LIKE @SearchText OR s.Keperluan LIKE @SearchText OR w.Nama LIKE @SearchText OR i.NamaInstansi LIKE @SearchText OR w.NIK LIKE @SearchText)");
                parameters.Add("SearchText", searchText);
            }

            var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : " ";
            _logger.LogDebug("Generated WHERE clause: {WhereClause}", whereClause);
            return (whereClause, parameters);
        }

        /// <summary>Apakah NamaJenis termasuk kelompok NTCR (N1-N4).</summary>
        private static bool IsNtcrJenis(string? namaJenis) =>
            namaJenis?.ToUpperInvariant() is "NTCR_N1" or "NTCR_N2" or "NTCR_N3" or "NTCR_N4";

        /// <summary>
        /// Melengkapi data pasangan NTCR untuk baris register (Ntcr tidak ikut query dasar).
        /// Database lama yang belum punya kolom baru otomatis jatuh ke seleksi kolom dasar.
        /// </summary>
        private async Task AttachNtcrDataAsync(SuratData surat, CancellationToken cancellationToken)
        {
            try
            {
                var row = await _connection.QueryFirstOrDefaultAsync<NtcrRegisterRow>(
                    @"SELECT ID_CalonIstri, NikIstri, NamaIstri, TempatLahirIstri, TanggalLahirIstri,
                             AgamaIstri, PekerjaanIstri, AlamatIstri,
                             NamaAyahCalonSuami, NamaIbuCalonSuami,
                             NamaAyahCalonIstri, NamaIbuCalonIstri,
                             StatusPerkawinanIstri, KeteranganTemuan, TujuanSurat
                      FROM NTCR WHERE ID_Surat = @ID_Surat",
                    new { surat.ID_Surat });
                if (row == null) return;

                surat.Ntcr = new NtcrData
                {
                    ID_CalonIstri = row.ID_CalonIstri,
                    NikIstri = row.NikIstri,
                    NamaIstri = row.NamaIstri,
                    TempatLahirIstri = row.TempatLahirIstri,
                    TanggalLahirIstri = row.TanggalLahirIstri,
                    AgamaIstri = row.AgamaIstri,
                    PekerjaanIstri = row.PekerjaanIstri,
                    AlamatIstri = row.AlamatIstri,
                    NamaAyahCalonSuami = row.NamaAyahCalonSuami,
                    NamaIbuCalonSuami = row.NamaIbuCalonSuami,
                    NamaAyahCalonIstri = row.NamaAyahCalonIstri,
                    NamaIbuCalonIstri = row.NamaIbuCalonIstri,
                    StatusPerkawinanIstri = row.StatusPerkawinanIstri,
                    KeteranganTemuan = row.KeteranganTemuan,
                    TujuanSurat = row.TujuanSurat
                };
            }
            catch (SqliteException)
            {
                // Database lama sebelum kolom StatusPerkawinanIstri/KeteranganTemuan/TujuanSurat.
                try
                {
                    var row = await _connection.QueryFirstOrDefaultAsync<NtcrRegisterRow>(
                        @"SELECT ID_CalonIstri, NikIstri, NamaIstri, TempatLahirIstri, TanggalLahirIstri,
                                 AgamaIstri, PekerjaanIstri, AlamatIstri,
                                 NamaAyahCalonSuami, NamaIbuCalonSuami,
                                 NamaAyahCalonIstri, NamaIbuCalonIstri
                          FROM NTCR WHERE ID_Surat = @ID_Surat",
                        new { surat.ID_Surat });
                    if (row == null) return;

                    surat.Ntcr = new NtcrData
                    {
                        ID_CalonIstri = row.ID_CalonIstri,
                        NikIstri = row.NikIstri,
                        NamaIstri = row.NamaIstri,
                        TempatLahirIstri = row.TempatLahirIstri,
                        TanggalLahirIstri = row.TanggalLahirIstri,
                        AgamaIstri = row.AgamaIstri,
                        PekerjaanIstri = row.PekerjaanIstri,
                        AlamatIstri = row.AlamatIstri,
                        NamaAyahCalonSuami = row.NamaAyahCalonSuami,
                        NamaIbuCalonSuami = row.NamaIbuCalonSuami,
                        NamaAyahCalonIstri = row.NamaAyahCalonIstri,
                        NamaIbuCalonIstri = row.NamaIbuCalonIstri
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal memuat data NTCR (kolom dasar) untuk ID_Surat={ID}", surat.ID_Surat);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat data NTCR untuk ID_Surat={ID}", surat.ID_Surat);
            }
        }

        private class NtcrRegisterRow
        {
            public int ID_CalonIstri { get; set; }
            public string? NikIstri { get; set; }
            public string? NamaIstri { get; set; }
            public string? TempatLahirIstri { get; set; }
            public string? TanggalLahirIstri { get; set; }
            public string? AgamaIstri { get; set; }
            public string? PekerjaanIstri { get; set; }
            public string? AlamatIstri { get; set; }
            public string? NamaAyahCalonSuami { get; set; }
            public string? NamaIbuCalonSuami { get; set; }
            public string? NamaAyahCalonIstri { get; set; }
            public string? NamaIbuCalonIstri { get; set; }
            public string? StatusPerkawinanIstri { get; set; }
            public string? KeteranganTemuan { get; set; }
            public string? TujuanSurat { get; set; }
        }

        public async Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default)
        {
            string cacheKey = $"Surat_Count_{filters.JenisSurat ?? "null"}_{filters.Tahun ?? "null"}_{filters.Status ?? "null"}_{filters.SearchText ?? "null"}_Ex_{string.Join(",", filters.ExcludeJenisNames ?? new List<string>())}";
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken);
            if (cached.HasValue)
                return cached.Value;

            await EnsureConnectionOpenAsync(cancellationToken);
            var (whereClause, parameters) = await BuildWhereClauseAsync(filters);
            var query = _queryProvider.GetQuery("GetFilteredSuratCount").Replace("{WhereClause}", whereClause);
            var count = await _connection.ExecuteScalarAsync<int>(query, parameters);

            await _cacheService.SetAsync(cacheKey, count, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return count;
        }

        public async Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));
            if (string.IsNullOrWhiteSpace(year))
                throw new ArgumentException("Tahun tidak boleh kosong.", nameof(year));

            string cacheKey = $"Surat_Count_{namaJenis.ToUpperInvariant()}_{year}";
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken);
            if (cached.HasValue)
                return cached.Value;

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CountSuratByJenisAndYear");
            var parameters = new { NamaJenis = namaJenis.ToUpperInvariant(), Year = year };
            var count = await _connection.ExecuteScalarAsync<int>(query, parameters);

            await _cacheService.SetAsync(cacheKey, count, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return count;
        }

        public async Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("GetSuratByDateRange");

            var results = await _connection.QueryAsync<SuratData, string, string, SuratData>(
                query,
                (s, jenisNama, wargaNama) =>
                {
                    s.NamaJenis = jenisNama;
                    if (s.Warga == null) s.Warga = new WargaData();
                    s.Warga.Nama = wargaNama;
                    return s;
                },
                new { TanggalMulai = startDate.ToString("yyyy-MM-dd"), TanggalSelesai = endDate.ToString("yyyy-MM-dd") },
                splitOn: "NamaJenis,Nama"
            );

            return results;
        }

        public async Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default)
        {
            string cacheKey = "Surat_Statistics_Status";
            var cached = await _cacheService.GetAsync<Dictionary<string, int>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CountSuratByStatus");

            var results = await _connection.QueryAsync<(string Status, int Jumlah)>(query);
            var statistics = results.ToDictionary(r => r.Status ?? "Unknown", r => r.Jumlah);

            await _cacheService.SetAsync(cacheKey, statistics, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return statistics;
        }

        public async Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CheckNomorSuratExists");
            var count = await _connection.ExecuteScalarAsync<int>(query, new { NomorSurat = nomorSurat, ID_Surat = excludeId });
            return count > 0;
        }

        private async Task<int> GetOrCreateWargaAsync(SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (suratData.Warga == null)
                return await _wargaRepository.GetOrCreateDummyWargaAsync(_connection, transaction);

            var warga = await _wargaRepository.GetWargaByNikAsync(suratData.Warga.NIK ?? string.Empty);
            var wargaId = warga?.ID_Warga ?? await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(suratData.Warga);
            suratData.Warga.ID_Warga = wargaId;
            return wargaId;
        }

        private async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(suratData.NamaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.InsertRelatedDataAsync(idSurat, suratData, _connection, transaction);
        }

        /// <summary>
        /// Insert data terkait dengan toleransi DRAFT: surat berstatus Draft boleh
        /// belum punya data rincian (mis. garapan baru terisi NIK/Nama), sehingga
        /// kegagalan handler karena data belum lengkap tidak menggagalkan simpan.
        /// </summary>
        private async Task InsertRelatedDataSafeAsync(int idSurat, SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var isDraft = string.Equals(suratData.Status, "Draft", StringComparison.OrdinalIgnoreCase);
            try
            {
                await InsertRelatedDataAsync(idSurat, suratData, transaction, cancellationToken);
            }
            catch (Exception ex) when (isDraft && ex is ArgumentException or ValidationException)
            {
                _logger.LogWarning(ex,
                    "DRAFT ID_Surat={ID_Surat}: rincian belum lengkap ({Message}); surat tetap disimpan.",
                    idSurat, ex.Message);
            }
        }

        private async Task DeleteRelatedDataAsync(int idSurat, string namaJenis, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(namaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.DeleteRelatedDataAsync(idSurat, _connection, transaction);
        }

        private static string GetValidSortColumn(string sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return "s.ID_Surat";

            return sortBy.ToLowerInvariant() switch
            {
                "id_surat" => "s.ID_Surat",
                "nomorsurat" => "s.NomorSurat",
                "nama" => "COALESCE(w.Nama, i.NamaInstansi)",
                "jenissurat" => "js.NamaJenis",
                "tanggal" => "s.TanggalSurat",
                "status" => "s.Status",
                "createdat" => "s.CreatedAt",
                _ => "s.ID_Surat"
            };
        }

        private async Task ClearListCacheAsync()
        {
            await _cacheService.RemoveByPrefixAsync("Surat_Filtered_", default);
            await _cacheService.RemoveByPrefixAsync("Surat_Count_", default);
            await _cacheService.RemoveByPrefixAsync("Surat_Statistics_", default);
        }

        public async Task<List<string>> GetJenisSuratKeteranganDesaAsync()
        {
            return await _jenisSuratRepository.GetJenisSuratKeteranganDesa() ?? new List<string>();
        }

        public async Task RefreshJenisSuratConfigurationAsync()
        {
            await _jenisSuratRepository.RefreshConfigurationAsync();
            await ClearListCacheAsync();
            _logger.LogInformation("JenisSurat configuration refreshed.");
        }

        public Task<int> AddSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return InsertAsync(entity, transaction, cancellationToken);
        }

        public async Task<IEnumerable<SuratData>> GetAllSuratDataAsync(
    string sortBy = "ID_Surat",
    bool ascending = false,
    int skip = 0,
    int take = 100,
    CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GetAllSuratDataAsync: Loading {Take} records, skip {Skip}, sortBy {SortBy}", take, skip, sortBy);

            try
            {
                await EnsureConnectionOpenAsync(cancellationToken);

                take = Math.Max(1, Math.Min(take, 1000));
                skip = Math.Max(0, skip);

                var orderBy = GetValidSortColumn(sortBy);
                var direction = ascending ? "ASC" : "DESC";

                // ? GUNAKAN QUERY YANG LEBIH SEDERHANA DENGAN PEMETAAN MANUAL
                var query = $@"
            SELECT 
                s.ID_Surat, s.ID_Jenis, s.NomorSurat, s.TanggalSurat, 
                s.Keterangan, s.Keperluan, s.AdditionalData, s.Status, 
                s.CreatedAt, s.UpdatedAt,
                w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, 
                w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan, 
                w.Dusun, w.Desa, w.Kecamatan, w.Kabupaten, w.Pendidikan, 
                w.Kewarganegaraan, w.CreatedAt as WargaCreatedAt, w.UpdatedAt as WargaUpdatedAt,
                i.NamaInstansi, i.AlamatInstansi, i.PimpinanInstansi,
                js.NamaJenis, js.KodeJenis, js.Deskripsi
            FROM Surat s
            LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
            INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
            LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
            ORDER BY {orderBy} {direction}
            LIMIT @Take OFFSET @Skip";

                _logger.LogDebug("Executing query: {Query}", query);

                var dynamicResults = await _connection.QueryAsync(query, new { Take = take, Skip = skip });
                var results = new List<SuratData>();

                foreach (var row in dynamicResults)
                {
                    try
                    {
                        var surat = new SuratData
                        {
                            ID_Surat = SafeGetInt(row.ID_Surat),
                            ID_Jenis = SafeGetInt(row.ID_Jenis),
                            NomorSurat = SafeGetString(row.NomorSurat),
                            TanggalSurat = SafeGetDateTime(row.TanggalSurat),
                            Keterangan = SafeGetString(row.Keterangan),
                            Keperluan = SafeGetString(row.Keperluan),
                            AdditionalData = SafeGetString(row.AdditionalData),
                            Status = SafeGetString(row.Status, "Draft"),
                            CreatedAt = SafeGetDateTime(row.CreatedAt, DateTime.Now),
                            UpdatedAt = SafeGetDateTime(row.UpdatedAt, DateTime.Now),
                            KodeJenis = SafeGetString(row.KodeJenis),
                            NamaJenis = SafeGetString(row.NamaJenis)
                        };

                        // Map Warga data jika ada
                        if (SafeGetInt(row.ID_Warga) > 0)
                        {
                            surat.Warga = new WargaData
                            {
                                ID_Warga = SafeGetInt(row.ID_Warga),
                                NIK = SafeGetString(row.NIK),
                                Nama = SafeGetString(row.Nama),
                                TempatLahir = SafeGetString(row.TempatLahir),
                                TanggalLahir = SafeGetString(row.TanggalLahir),
                                JenisKelamin = SafeGetString(row.JenisKelamin),
                                Agama = SafeGetString(row.Agama),
                                StatusPerkawinan = SafeGetString(row.StatusPerkawinan),
                                Pekerjaan = SafeGetString(row.Pekerjaan),
                                Dusun = SafeGetString(row.Dusun),
                                Desa = SafeGetString(row.Desa),
                                Kecamatan = SafeGetString(row.Kecamatan),
                                Kabupaten = SafeGetString(row.Kabupaten),
                                Pendidikan = SafeGetString(row.Pendidikan),
                                Kewarganegaraan = SafeGetString(row.Kewarganegaraan),
                                CreatedAt = SafeGetDateTime(row.WargaCreatedAt, DateTime.Now),
                                UpdatedAt = SafeGetDateTime(row.WargaUpdatedAt, DateTime.Now)
                            };
                        }

                        // Map Instansi jika ada
                        if (!string.IsNullOrEmpty(SafeGetString(row.NamaInstansi)))
                        {
                            surat.Instansi = new Instansi
                            {
                                NamaInstansi = SafeGetString(row.NamaInstansi),
                                AlamatInstansi = SafeGetString(row.AlamatInstansi),
                                PimpinanInstansi = SafeGetString(row.PimpinanInstansi)
                            };
                        }

                        results.Add(surat);
                    }
                    catch (Exception ex)
                    {
                        //_logger.LogError(ex, "Error mapping row for surat ID: {ID}", SafeGetInt(row.ID_Surat));
                    }
                }

                _logger.LogInformation("SUCCESS: Retrieved {Count} surat records", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FAILED to retrieve surat data");
                throw new DataRetrievalException("Gagal mengambil data surat.", ex);
            }
        }

        // ? MAIN METHOD IMPLEMENTATION
        public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
        {
            try
            {
                _logger.LogInformation("Getting database statistics...");
                await EnsureConnectionOpenAsync();
                var stats = new Dictionary<string, object>();

                // ? 1. TOTAL SURAT
                var totalSurat = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
                stats["Total Surat"] = totalSurat;
                _logger.LogDebug("Total Surat: {Count}", totalSurat);

                // ? 2. TOTAL WARGA
                var totalWarga = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");
                stats["Total Warga"] = totalWarga;
                _logger.LogDebug("Total Warga: {Count}", totalWarga);

                // ? 3. TOTAL JENIS SURAT
                // NOTE: tanpa filter IsActive agar backward-compatible
                // dengan database lama yang belum punya kolom tersebut.
                var totalJenis = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM JenisSurat");
                stats["Total Jenis Surat"] = totalJenis;

                // ? 4. SURAT BY STATUS
                try
                {
                    var statusQuery = "SELECT Status, COUNT(*) as Count FROM Surat GROUP BY Status";
                    var statusResults = await _connection.QueryAsync(statusQuery);

                    foreach (var row in statusResults)
                    {
                        var status = SafeGetString(row.Status, "Unknown");
                        var count = SafeGetInt(row.Count);
                        stats[$"Surat {status}"] = count;
                        //_logger.LogDebug("Status {Status}: {Count}", status, count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get status statistics");
                    stats["Surat Status"] = "Error getting status data";
                }

                // ? 5. SURAT BY JENIS (TOP 10)
                try
                {
                    var jenisQuery = @"
                SELECT js.NamaJenis, COUNT(*) as Count 
                FROM Surat s 
                INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis 
                GROUP BY js.NamaJenis 
                ORDER BY Count DESC 
                LIMIT 10";

                    var jenisResults = await _connection.QueryAsync(jenisQuery);

                    foreach (var row in jenisResults)
                    {
                        var namaJenis = SafeGetString(row.NamaJenis, "Unknown");
                        var count = SafeGetInt(row.Count);
                        stats[$"Jenis: {namaJenis}"] = count;
                        //_logger.LogDebug("Jenis {NamaJenis}: {Count}", namaJenis, count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get jenis statistics");
                    stats["Surat by Jenis"] = "Error getting jenis data";
                }

                // ? 6. SURAT TAHUN INI
                try
                {
                    var currentYear = DateTime.Now.Year.ToString();
                    var yearQuery = "SELECT COUNT(*) FROM Surat WHERE strftime('%Y', TanggalSurat) = @Year";
                    var suratThisYear = await _connection.ExecuteScalarAsync<int>(yearQuery, new { Year = currentYear });
                    stats[$"Surat Tahun {currentYear}"] = suratThisYear;
                    _logger.LogDebug("Surat tahun {Year}: {Count}", currentYear, suratThisYear);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get yearly statistics");
                    stats["Surat Tahun Ini"] = "Error getting yearly data";
                }

                // ? 7. SURAT BULAN INI
                try
                {
                    var currentMonth = DateTime.Now.ToString("yyyy-MM");
                    var monthQuery = "SELECT COUNT(*) FROM Surat WHERE strftime('%Y-%m', TanggalSurat) = @Month";
                    var suratThisMonth = await _connection.ExecuteScalarAsync<int>(monthQuery, new { Month = currentMonth });
                    stats[$"Surat Bulan Ini"] = suratThisMonth;
                    _logger.LogDebug("Surat bulan {Month}: {Count}", currentMonth, suratThisMonth);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get monthly statistics");
                    stats["Surat Bulan Ini"] = "Error getting monthly data";
                }

                // ? 8. DATABASE FILE SIZE (jika perlu)
                try
                {
                    var dbSizeQuery = "SELECT page_count * page_size as size FROM pragma_page_count(), pragma_page_size()";
                    var dbSize = await _connection.ExecuteScalarAsync<long>(dbSizeQuery);
                    var dbSizeMB = Math.Round(dbSize / (1024.0 * 1024.0), 2);
                    stats["Database Size (MB)"] = dbSizeMB;
                    _logger.LogDebug("Database size: {Size} MB", dbSizeMB);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get database size");
                    stats["Database Size"] = "Unknown";
                }

                // ? 9. LAST SURAT CREATED
                try
                {
                    var lastSuratQuery = @"
                SELECT s.NomorSurat, s.TanggalSurat, js.NamaJenis 
                FROM Surat s 
                INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis 
                ORDER BY s.CreatedAt DESC 
                LIMIT 1";

                    var lastSurat = await _connection.QueryFirstOrDefaultAsync(lastSuratQuery);
                    if (lastSurat != null)
                    {
                        var nomorSurat = SafeGetString(lastSurat.NomorSurat);
                        var jenisNama = SafeGetString(lastSurat.NamaJenis);
                        stats["Surat Terakhir"] = $"{nomorSurat} ({jenisNama})";
                    }
                    else
                    {
                        stats["Surat Terakhir"] = "Tidak ada data";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get last surat info");
                    stats["Surat Terakhir"] = "Error getting last surat";
                }

                _logger.LogInformation("Database statistics retrieved successfully. Total stats: {Count}", stats.Count);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database statistics");
                return new Dictionary<string, object>
        {
            { "Error", "Gagal mengambil statistik database" },
            { "Exception", ex.Message },
            { "Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
        };
            }
        }

        public async Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.GetWargaByIdAsync(idWarga);
        }

        public async Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.GetWargaByNikAsync(nik);
        }

        public async Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(wargaData);
        }

        public async Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default)
        {
            var filters = new FilterConditions { JenisSurat = templateName };
            var surats = await GetFilteredAsync(filters, "ID_Surat", false, 0, 1, cancellationToken);
            var lastSurat = surats.FirstOrDefault();
            return lastSurat?.ID_Surat;
        }
    }
}
