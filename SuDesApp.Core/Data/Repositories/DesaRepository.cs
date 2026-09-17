using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using System.Data;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IDesaRepository
    {
        Task InitializeAsync();
        Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default);
        Task UpdateInfoDesaAsync(DesaData desaData);
        Task SaveInfoDesaAsync(DesaData desaData); // Added missing method
        Task<DesaData> GetInfoDesaFromCacheAsync();
        Task InvalidateCacheAsync();
    }

    public class DesaRepository : IDesaRepository
    {
        private readonly string _connectionString;
        private readonly ICacheService _cacheService;
        private readonly ILogger<DesaRepository> _logger;
        private readonly AppConfig _config;
        private readonly IJenisSuratConfigLoader _configLoader;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);

        public DesaRepository(
            AppConfig config,
            ICacheService cacheService,
            ILogger<DesaRepository> logger,
            IJenisSuratConfigLoader configLoader)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
            _connectionString = _config.DatabaseConnectionString ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "Database", "desa.db")}";
            _logger.LogInformation("Using database at: {ConnectionString}", _connectionString);
        }

        private async Task<SqliteConnection> GetOpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Checking database initialization...");

            try
            {
                using var connection = await GetOpenConnectionAsync();

                // Use explicit Dapper method call to avoid ambiguity
                const string checkTableSql = @"
            SELECT COUNT(*) 
            FROM sqlite_master 
            WHERE type='table' AND name=@TableName";

                var tableExists = await SqlMapper.ExecuteScalarAsync<int>(
                    connection,
                    checkTableSql,
                    new { TableName = "InfoDesa" },
                    null, // transaction
                    null, // commandTimeout 
                    null  // commandType
                );

                if (tableExists == 0)
                {
                    _logger.LogInformation("Initializing default database data...");
                    await InitializeDefaultData();
                }
                else
                {
                    // Check if table exists but is empty
                    var rowCount = await SqlMapper.ExecuteScalarAsync<int>(
                        connection,
                        "SELECT COUNT(*) FROM InfoDesa",
                        null, null, null, null
                    );

                    if (rowCount == 0)
                    {
                        _logger.LogInformation("InfoDesa table exists but is empty, inserting default data...");
                        await InitializeDefaultData();
                    }
                }

                // Pastikan struktur tabel tambahan mutakhir untuk database lama
                // (mis. kolom alamat tujuan pada IjinTinggal yang tidak ada di skema lama).
                await EnsureRequiredTableColumnsAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize database");
                throw;
            }
        }

        private async Task EnsureRequiredTableColumnsAsync(SqliteConnection connection)
        {
            // Kolom yang ditambahkan ke desa.db.sql setelah database lama dibuat.
            // CREATE TABLE IF NOT EXISTS tidak mengubah tabel yang sudah ada,
            // jadi tambahkan kolom secara eksplisit jika belum tersedia.
            var pendingColumns = new (string Table, string Column, string Definition)[]
            {
                ("IjinTinggal", "DusunTujuan", "TEXT"),
                ("IjinTinggal", "DesaTujuan", "TEXT"),
                ("IjinTinggal", "KecamatanTujuan", "TEXT"),
                ("IjinTinggal", "KabupatenTujuan", "TEXT"),
                ("IjinTinggal", "NikPenanggungJawab", "TEXT"),
                ("IjinTinggal", "NamaPenanggungJawab", "TEXT"),
                ("IjinTinggal", "TglLahirPenanggungJawab", "TEXT"),
                ("IjinTinggal", "PekerjaanPenanggungJawab", "TEXT")
            };

            foreach (var (table, column, definition) in pendingColumns)
            {
                try
                {
                    var exists = await SqlMapper.ExecuteScalarAsync<int>(
                        connection,
                        $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @ColumnName COLLATE NOCASE",
                        new { ColumnName = column }) > 0;

                    if (!exists)
                    {
                        _logger.LogInformation("Adding missing column {Column} to table {Table}", column, table);
                        await SqlMapper.ExecuteAsync(
                            connection,
                            $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition}");
                        _logger.LogInformation("Successfully added column {Column} to table {Table}", column, table);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to ensure column {Column} in table {Table}", column, table);
                }
            }
        }

        private async Task InitializeDefaultData()
        {
            try
            {
                using var connection = await GetOpenConnectionAsync();
                await ExecuteSchemaScriptsAsync(connection);

                // Jangan menyisipkan data default ke InfoDesa agar status
                // "first run" tetap terdeteksi (tabel kosong) dan user wajib
                // melengkapi pengaturan desa melalui SetelanForm.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize default data");
                throw;
            }
        }

        private async Task ExecuteSchemaScriptsAsync(SqliteConnection connection)
        {
            var dbFile = Path.Combine(AppContext.BaseDirectory, "desa.db.sql");
            _logger.LogInformation("Looking for schema file at: {Path}", dbFile);

            if (!File.Exists(dbFile))
            {
                _logger.LogWarning("SQL schema file not found: {DbFile}", dbFile);
                await CreateInfoDesaTableAsync(connection);
                return;
            }

            var schemaSql = await File.ReadAllTextAsync(dbFile);
            _logger.LogDebug("Schema SQL content: {SchemaSql}", schemaSql);

            var commands = SplitSqlCommands(schemaSql);

            foreach (var cmd in commands)
            {
                try
                {
                    _logger.LogInformation("Executing SQL command: {Command}", cmd);
                    if (cmd.ToUpperInvariant().StartsWith("CREATE TABLE"))
                    {
                        var tableNameMatch = System.Text.RegularExpressions.Regex.Match(cmd, @"CREATE TABLE\s+(\w+)\s*\(");
                        if (tableNameMatch.Success)
                        {
                            var tableName = tableNameMatch.Groups[1].Value;
                            var tableExists = await SqlMapper.ExecuteScalarAsync<int>(
                                connection,
                                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@TableName",
                                new { TableName = tableName },
                                null, // transaction
                                null, // commandTimeout
                                null  // commandType
                            );
                            if (tableExists > 0)
                            {
                                _logger.LogInformation("Table {TableName} already exists, skipping creation", tableName);
                                continue;
                            }
                        }
                    }
                    await SqlMapper.ExecuteAsync(
                        connection,
                        cmd,
                        null, // param
                        null, // transaction
                        null, // commandTimeout
                        null  // commandType
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to execute SQL command: {Command}", cmd);
                    throw new DataAccessException($"Failed to execute SQL command: {cmd}", ex);
                }
            }
        }

        private async Task CreateInfoDesaTableAsync(SqliteConnection connection)
        {
            try
            {
                await SqlMapper.ExecuteAsync(
                    connection,
                    @"CREATE TABLE IF NOT EXISTS InfoDesa (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        NamaDesa TEXT,
                        Kecamatan TEXT,
                        Kabupaten TEXT,
                        Alamat TEXT,
                        Kodepos TEXT,
                        KepalaDesa TEXT,
                        SekretarisDesa TEXT,
                        NamaCamat TEXT,
                        NipCamat TEXT,
                        GolCamat TEXT
                    )",
                    null, // param
                    null, // transaction
                    null, // commandTimeout
                    null  // commandType
                );
                _logger.LogInformation("InfoDesa table created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create InfoDesa table");
                throw;
            }
        }

        private IEnumerable<string> SplitSqlCommands(string sql)
        {
            var commands = new List<string>();
            var currentCommand = new System.Text.StringBuilder();
            bool insideTrigger = false;
            bool insideView = false; // Tambahkan flag untuk CREATE VIEW

            var lines = sql.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                // Jika sedang dalam trigger
                if (insideTrigger)
                {
                    currentCommand.AppendLine(trimmedLine);
                    if (trimmedLine.Equals("END;", StringComparison.OrdinalIgnoreCase))
                    {
                        insideTrigger = false;
                        commands.Add(currentCommand.ToString());
                        currentCommand.Clear();
                    }
                    continue;
                }

                // Jika sedang dalam view
                if (insideView)
                {
                    currentCommand.AppendLine(trimmedLine);
                    if (trimmedLine.EndsWith(";"))
                    {
                        insideView = false;
                        commands.Add(currentCommand.ToString());
                        currentCommand.Clear();
                    }
                    continue;
                }

                // Memulai trigger
                if (trimmedLine.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
                {
                    insideTrigger = true;
                    currentCommand.Clear();
                    currentCommand.AppendLine(trimmedLine);
                    continue;
                }

                // Memulai view
                if (trimmedLine.StartsWith("CREATE VIEW", StringComparison.OrdinalIgnoreCase))
                {
                    insideView = true;
                    currentCommand.Clear();
                    currentCommand.AppendLine(trimmedLine);
                    continue;
                }

                // Perintah biasa
                currentCommand.AppendLine(trimmedLine);

                if (trimmedLine.EndsWith(";"))
                {
                    commands.Add(currentCommand.ToString());
                    currentCommand.Clear();
                }
            }

            // Tambahkan sisa perintah jika ada
            if (currentCommand.Length > 0)
            {
                commands.Add(currentCommand.ToString());
            }

            return commands.Where(cmd => !string.IsNullOrWhiteSpace(cmd));
        }

        public async Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default)
        {
            return await _cacheService.GetOrCreateAsync(
                CacheKeys.DesaInfo,
                async () =>
                {
                    using var connection = await GetOpenConnectionAsync();
                    var result = await SqlMapper.QueryFirstOrDefaultAsync<DesaData>(
                        connection,
                        "SELECT * FROM InfoDesa LIMIT 1",
                        null, // param
                        null, // transaction
                        null, // commandTimeout
                        null  // commandType
                    );
                    
                    // If no data in database, return default desa data instead of empty object
                    if (result == null || !IsDesaDataValid(result))
                    {
                        _logger.LogWarning("InfoDesa data not found or invalid, returning default data");
                        return CreateDefaultDesaData();
                    }
                    
                    return result;
                },
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                },
                cancellationToken);
        }

        private bool IsDesaDataValid(DesaData desa)
        {
            return desa != null &&
                   !string.IsNullOrWhiteSpace(desa.NamaDesa) &&
                   !string.IsNullOrWhiteSpace(desa.Kecamatan) &&
                   !string.IsNullOrWhiteSpace(desa.Kabupaten) &&
                   !string.IsNullOrWhiteSpace(desa.KepalaDesa) &&
                   !string.IsNullOrWhiteSpace(desa.Alamat) &&
                   !desa.NamaDesa.StartsWith("Default", StringComparison.OrdinalIgnoreCase) &&
                   !desa.NamaDesa.StartsWith("Nama ", StringComparison.OrdinalIgnoreCase);
        }

        private DesaData CreateDefaultDesaData()
        {
            return new DesaData
            {
                NamaDesa = "Nama Desa",
                Kecamatan = "Nama Kecamatan",
                Kabupaten = "Nama Kabupaten",
                Alamat = "Alamat Desa",
                Kodepos = "00000",
                KepalaDesa = "Nama Kepala Desa",
                SekretarisDesa = "Nama Sekretaris Desa",
                NamaCamat = "Nama Camat",
                NipCamat = "000000000000000000",
                GolCamat = "IV/a"
            };
        }

        public async Task UpdateInfoDesaAsync(DesaData desaData)
        {
            if (desaData == null) throw new ArgumentNullException(nameof(desaData));

            try
            {
                using var connection = await GetOpenConnectionAsync();

                // Check if record exists
                var existingCount = await SqlMapper.ExecuteScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM InfoDesa",
                    null, // param
                    null, // transaction
                    null, // commandTimeout
                    null  // commandType
                );

                if (existingCount == 0)
                {
                    // Insert new record
                    await SqlMapper.ExecuteAsync(
                        connection,
                        @"INSERT INTO InfoDesa 
                            (NamaDesa, Kecamatan, Kabupaten, Alamat, Kodepos, 
                             KepalaDesa, SekretarisDesa, NamaCamat, NipCamat, GolCamat)
                          VALUES 
                            (@NamaDesa, @Kecamatan, @Kabupaten, @Alamat, @Kodepos, 
                             @KepalaDesa, @SekretarisDesa, @NamaCamat, @NipCamat, @GolCamat)",
                        desaData,
                        null, // transaction
                        null, // commandTimeout
                        null  // commandType
                    );
                }
                else
                {
                    // Update existing record
                    await SqlMapper.ExecuteAsync(
                        connection,
                        @"UPDATE InfoDesa SET 
                            NamaDesa = @NamaDesa, 
                            Kecamatan = @Kecamatan, 
                            Kabupaten = @Kabupaten,
                            Alamat = @Alamat, 
                            Kodepos = @Kodepos, 
                            KepalaDesa = @KepalaDesa, 
                            SekretarisDesa = @SekretarisDesa,
                            NamaCamat = @NamaCamat, 
                            NipCamat = @NipCamat, 
                            GolCamat = @GolCamat",
                        desaData,
                        null, // transaction
                        null, // commandTimeout
                        null  // commandType
                    );
                }

                await _cacheService.SetAsync(CacheKeys.DesaInfo, desaData, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                _logger.LogInformation("Desa info updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update village info");
                throw new DataAccessException("Failed to update village info", ex);
            }
        }

        public async Task SaveInfoDesaAsync(DesaData desaData)
        {
            // Alias for UpdateInfoDesaAsync for backward compatibility
            await UpdateInfoDesaAsync(desaData);
        }

        public Task<DesaData> GetInfoDesaFromCacheAsync()
        {
            var cachedDataTask = _cacheService.GetAsync<DesaData>(CacheKeys.DesaInfo);
            if (cachedDataTask == null)
            {
                return Task.FromResult(new DesaData());
            }
            return cachedDataTask!;
        }

        public async Task InvalidateCacheAsync()
        {
            await _cacheService.RemoveAsync<DesaData>(CacheKeys.DesaInfo);
        }
    }
}
