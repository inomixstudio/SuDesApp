using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SuDesApp.Configuration
{
    public interface IDatabaseInitializer
    {
        Task InitializeAsync();
        Task<DesaData?> GetInfoDesaAsync();
        Task<bool> ValidateSchemaAsync();
        Task<Dictionary<string, object>> GetDatabaseStatsAsync();
        Task CreateBackupAsync(string backupPath);
        Task RestoreBackupAsync(string backupPath);
    }

    public class DatabaseInitializer : IDatabaseInitializer
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseInitializer> _logger;
        private readonly IDesaRepository _desaRepository;
        private readonly IJenisSuratRepository _jenisSuratRepository;
        private readonly IWargaRepository _wargaRepository;
        private readonly ISuratRepository _suratRepository;

        // ? SCHEMA VERSION TRACKING
        private const string CURRENT_SCHEMA_VERSION = "2.0.0";
        private const string SCHEMA_VERSION_TABLE = "SchemaVersion";

        public DatabaseInitializer(
            AppConfig config,
            ILogger<DatabaseInitializer> logger,
            IDesaRepository desaRepository,
            IJenisSuratRepository jenisSuratRepository,
            IWargaRepository wargaRepository,
            ISuratRepository suratRepository)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Memulai inisialisasi database v{Version}...", CURRENT_SCHEMA_VERSION);

            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();

                // ? 1. Buat tabel versi schema jika belum ada
                await CreateSchemaVersionTableAsync(connection);

                // ? 2. Get current version before any schema operations
                var currentVersion = await GetCurrentSchemaVersionAsync(connection);
                _logger.LogInformation("Current schema version: {CurrentVersion}, Target: {TargetVersion}",
                    currentVersion, CURRENT_SCHEMA_VERSION);

                // ? 3. ALWAYS execute base schema first to ensure tables exist
                await ExecuteDatabaseSchemaAsync(connection);

                // ? 4. Check if this is a fresh install (no previous version records or default version)
                var isFirstRun = await IsFirstRunAsync(connection, currentVersion);

                // ? 5. Perform migration only if needed and not a fresh install
                if (!isFirstRun && await ShouldMigrateSchema(currentVersion))
                {
                    await MigrateSchemaAsync(connection, currentVersion);
                }
                else if (isFirstRun)
                {
                    _logger.LogInformation("Fresh installation detected, skipping migrations");
                }

                // ? 6. Verify required columns exist (non-blocking)
                await EnsureRequiredColumnsAsync(connection);

                // ? 7. Inisialisasi tabel-tabel utama
                await _desaRepository.InitializeAsync();
                await _jenisSuratRepository.InitializeJenisSuratDataAsync();
                await _wargaRepository.InitializeWargaTableAsync();
                await _suratRepository.InitializeSuratIndexesAsync();

                // ? 8. Inisialisasi komponen baru
                await InitializeNewFeaturesAsync(connection);

                // ? 9. Validasi schema final - dengan retry jika gagal
                bool isValid = await ValidateSchemaWithRetryAsync(connection);
                if (!isValid)
                {
                    throw new InvalidOperationException("Schema validation failed after initialization and retry attempts");
                }

                // ? 10. Update versi schema hanya jika berhasil
                await UpdateSchemaVersionAsync(connection, CURRENT_SCHEMA_VERSION);

                _logger.LogInformation("Inisialisasi database berhasil v{Version}", CURRENT_SCHEMA_VERSION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal inisialisasi database");
                throw;
            }
        }

        // ? ENSURE REQUIRED COLUMNS EXIST - SIMPLIFIED
        private async Task EnsureRequiredColumnsAsync(SqliteConnection connection)
        {
            _logger.LogInformation("Verifying required columns exist...");

            try
            {
                // Check if tables exist first
                if (!await TableExistsAsync(connection, "Surat"))
                {
                    _logger.LogWarning("Surat table does not exist, skipping column checks");
                    return;
                }

                if (!await TableExistsAsync(connection, "Warga"))
                {
                    _logger.LogWarning("Warga table does not exist, skipping column checks");
                    return;
                }

                // Just verify critical columns exist, don't try to add them again
                // The schema creation or migration should have handled this
                var suratRequiredColumns = new[] { "Status", "CreatedAt", "UpdatedAt" };
                foreach (var column in suratRequiredColumns)
                {
                    if (!await CheckColumnExistsAsync(connection, "Surat", column))
                    {
                        _logger.LogWarning("Required column {Column} missing from Surat table", column);
                    }
                }

                var wargaRequiredColumns = new[] { "CreatedAt", "UpdatedAt" };
                foreach (var column in wargaRequiredColumns)
                {
                    if (!await CheckColumnExistsAsync(connection, "Warga", column))
                    {
                        _logger.LogWarning("Required column {Column} missing from Warga table", column);
                    }
                }

                _logger.LogInformation("Required columns verification completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify required columns");
                // Don't throw here, continue with initialization
            }
        }

        // ? NEW: Check if table exists
        private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
        {
            try
            {
                var count = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@TableName",
                    new { TableName = tableName });
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check if table {TableName} exists", tableName);
                return false;
            }
        }

        private async Task EnsureColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            try
            {
                // Check if column exists using a direct SQL approach
                var checkColumnSql = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = @columnName COLLATE NOCASE";

                var columnExists = await connection.ExecuteScalarAsync<int>(
                    checkColumnSql,
                    new { columnName }) > 0;

                if (!columnExists)
                {
                    _logger.LogInformation("Adding missing column {ColumnName} to table {TableName}", columnName, tableName);

                    var alterSql = $"ALTER TABLE [{tableName}] ADD COLUMN [{columnName}] {columnDefinition}";
                    await connection.ExecuteAsync(alterSql);

                    _logger.LogInformation("Successfully added column {ColumnName} to table {TableName}", columnName, tableName);
                }
                else
                {
                    _logger.LogDebug("Column {ColumnName} already exists in table {TableName}", columnName, tableName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure column {ColumnName} exists in table {TableName}", columnName, tableName);
                throw;
            }
        }

        // ? VALIDATE SCHEMA WITH RETRY
        private async Task<bool> ValidateSchemaWithRetryAsync(SqliteConnection connection, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogInformation("Schema validation attempt {Attempt}/{MaxRetries}", attempt, maxRetries);

                try
                {
                    bool isValid = await ValidateSchemaInternalAsync(connection);
                    if (isValid)
                    {
                        _logger.LogInformation("Schema validation passed on attempt {Attempt}", attempt);
                        return true;
                    }

                    if (attempt < maxRetries)
                    {
                        _logger.LogWarning("Schema validation failed on attempt {Attempt}, retrying...", attempt);
                        await Task.Delay(1000 * attempt); // Progressive delay

                        // Try to fix missing columns before next attempt
                        await EnsureRequiredColumnsAsync(connection);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Schema validation attempt {Attempt} failed with exception", attempt);

                    if (attempt == maxRetries)
                    {
                        throw;
                    }
                }
            }

            return false;
        }

        // ? CREATE SCHEMA VERSION TABLE
        private async Task CreateSchemaVersionTableAsync(SqliteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    ID INTEGER PRIMARY KEY,
                    Version TEXT NOT NULL,
                    AppliedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    Description TEXT
                );";

            await connection.ExecuteAsync(sql);
        }

        // ? GET CURRENT SCHEMA VERSION
        private async Task<string> GetCurrentSchemaVersionAsync(SqliteConnection connection)
        {
            try
            {
                var version = await connection.ExecuteScalarAsync<string>(
                    "SELECT Version FROM SchemaVersion ORDER BY AppliedAt DESC LIMIT 1");
                return version ?? "1.0.0";
            }
            catch
            {
                return "1.0.0"; // Default version
            }
        }

        // ? CHECK IF MIGRATION IS NEEDED - IMPROVED
        private async Task<bool> ShouldMigrateSchema(string currentVersion)
        {
            var current = Version.Parse(currentVersion);
            var target = Version.Parse(CURRENT_SCHEMA_VERSION);

            var shouldMigrate = current < target;

            if (shouldMigrate)
            {
                _logger.LogInformation("Schema migration needed: {Current} -> {Target}", currentVersion, CURRENT_SCHEMA_VERSION);
                return true;
            }
            else if (current == target)
            {
                _logger.LogInformation("Schema version is current: {Version}", currentVersion);
                return false;
            }
            else
            {
                _logger.LogWarning("Schema version is newer than expected: {Current} > {Target}", currentVersion, CURRENT_SCHEMA_VERSION);
                return false;
            }
        }

        // ? MIGRATE SCHEMA - IMPROVED SAFETY
        private async Task MigrateSchemaAsync(SqliteConnection connection, string fromVersion)
        {
            _logger.LogInformation("Migrating schema from {FromVersion} to {ToVersion}", fromVersion, CURRENT_SCHEMA_VERSION);

            var from = Version.Parse(fromVersion);
            var to = Version.Parse(CURRENT_SCHEMA_VERSION);

            // Only perform migration if we're actually upgrading from an older version
            // Skip migration if base schema already has the required columns
            if (from.Major == 1 && to.Major == 2)
            {
                // Check if the main tables already have the required columns
                // If they do, skip the critical migrations since base schema is up to date
                bool suratHasStatus = await CheckColumnExistsAsync(connection, "Surat", "Status");
                bool suratHasCreatedAt = await CheckColumnExistsAsync(connection, "Surat", "CreatedAt");
                bool wargaHasCreatedAt = await CheckColumnExistsAsync(connection, "Warga", "CreatedAt");

                if (suratHasStatus && suratHasCreatedAt && wargaHasCreatedAt)
                {
                    _logger.LogInformation("Base schema already contains required columns, skipping critical migrations");
                }
                else
                {
                    _logger.LogInformation("Applying critical migrations for missing columns");
                    await MigrateFrom1To2Async(connection);
                }

                // Always try optional migrations (non-critical)
                await ApplyOptionalMigrationsAsync(connection);
            }

            _logger.LogInformation("Schema migration completed");
        }

        // ? CHECK IF THIS IS FIRST RUN
        private async Task<bool> IsFirstRunAsync(SqliteConnection connection, string currentVersion)
        {
            try
            {
                // Check if there are any version records beyond the default
                var versionCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM SchemaVersion WHERE Version != '1.0.0'");

                // Check if any actual data exists (indicating existing installation)
                var hasData = false;
                try
                {
                    var suratCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
                    var wargaCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");
                    hasData = suratCount > 0 || wargaCount > 0;
                }
                catch
                {
                    // Tables might not exist yet, which is fine for first run
                    hasData = false;
                }

                // It's a first run if:
                // 1. We're on default version (1.0.0) AND
                // 2. No version history beyond default AND
                // 3. No actual application data exists
                bool isFirstRun = currentVersion == "1.0.0" && versionCount == 0 && !hasData;

                _logger.LogInformation("First run detection: Version={Version}, VersionHistory={VersionCount}, HasData={HasData}, IsFirstRun={IsFirstRun}",
                    currentVersion, versionCount, hasData, isFirstRun);

                return isFirstRun;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect first run, assuming upgrade scenario");
                return false; // Assume it's an upgrade to be safe
            }
        }
        private async Task<bool> CheckColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
        {
            try
            {
                if (!await TableExistsAsync(connection, tableName))
                    return false;

                var checkColumnSql = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = @columnName COLLATE NOCASE";
                var columnExists = await connection.ExecuteScalarAsync<int>(checkColumnSql, new { columnName }) > 0;
                return columnExists;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check if column {ColumnName} exists in table {TableName}", columnName, tableName);
                return false;
            }
        }

        // ? APPLY OPTIONAL MIGRATIONS
        private async Task ApplyOptionalMigrationsAsync(SqliteConnection connection)
        {
            _logger.LogInformation("Applying optional migrations...");

            // Optional migrations (non-critical) - only execute if tables exist
            var optionalMigrations = new[]
            {
                ("Kematian", "TempatKematian", "TEXT"),
                ("Instansi", "PimpinanInstansi", "TEXT"),
                ("Instansi", "TeleponInstansi", "TEXT"),
                ("IZIN", "TanggalKeberangkatan", "DATE"),
                ("IZIN", "TanggalKembali", "DATE"),
                ("SKU", "LokasiUsaha", "TEXT"),
                ("SKU", "ModalUsaha", "REAL CHECK (ModalUsaha >= 0)"),
                ("SKTM", "PenghasilanPerBulan", "REAL CHECK (PenghasilanPerBulan >= 0)"),
                ("SKTM", "JumlahTanggungan", "INTEGER CHECK (JumlahTanggungan >= 0)"),
                ("BedaNama", "AlasanPerbedaan", "TEXT"),
                ("KenalLahir", "TempatLahirAnak", "TEXT"),
                ("KenalLahir", "JenisKelaminAnak", "TEXT CHECK (JenisKelaminAnak IN ('L', 'P', 'Laki-laki', 'Perempuan'))"),
                ("KenalLahir", "AlamatLengkapAnak", "TEXT"),
                ("KenalLahir", "LahirDi", "TEXT"),
                ("KenalLahir", "BeratBadan", "REAL CHECK (BeratBadan > 0)"),
                ("KenalLahir", "PanjangBadan", "REAL CHECK (PanjangBadan > 0)"),
                ("AhliWaris", "BagianWaris", "TEXT"),
                ("IjinTinggal", "DusunTujuan", "TEXT"),
                ("IjinTinggal", "DesaTujuan", "TEXT"),
                ("IjinTinggal", "KecamatanTujuan", "TEXT"),
                ("IjinTinggal", "KabupatenTujuan", "TEXT"),
                ("IjinTinggal", "NikPenanggungJawab", "TEXT"),
                ("IjinTinggal", "NamaPenanggungJawab", "TEXT"),
                ("IjinTinggal", "TglLahirPenanggungJawab", "TEXT"),
                ("IjinTinggal", "PekerjaanPenanggungJawab", "TEXT")
            };

            foreach (var (tableName, columnName, columnDef) in optionalMigrations)
            {
                if (await TableExistsAsync(connection, tableName))
                {
                    try
                    {
                        await EnsureColumnExistsAsync(connection, tableName, columnName, columnDef);
                        _logger.LogDebug("Optional migration executed: {Table}.{Column}", tableName, columnName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Optional migration failed (non-critical): {Table}.{Column}", tableName, columnName);
                    }
                }
                else
                {
                    _logger.LogDebug("Table {Table} does not exist, skipping optional migration for {Column}", tableName, columnName);
                }
            }
        }
        // ? MIGRATION FROM v1 TO v2 - CRITICAL ONLY
        private async Task MigrateFrom1To2Async(SqliteConnection connection)
        {
            _logger.LogInformation("Applying critical migration from v1.x to v2.x...");

            // Critical columns that must exist - but only if tables exist
            // CATATAN: SQLite menolak ALTER ADD COLUMN dengan default non-konstan
            // (mis. DEFAULT CURRENT_TIMESTAMP), sehingga definisi di sini dibuat
            // sekonstan yang diizinkan; kolom CreatedAt/UpdatedAt/Status juga dijamin
            // oleh SuratRepository/WargaRepository saat inisialisasi.
            if (await TableExistsAsync(connection, "Surat"))
            {
                var criticalSuratColumns = new[]
                {
                    ("Status", "TEXT DEFAULT 'Draft'"),
                    ("CreatedAt", "TEXT"),
                    ("UpdatedAt", "TEXT")
                };

                foreach (var (columnName, columnDef) in criticalSuratColumns)
                {
                    try
                    {
                        await EnsureColumnExistsAsync(connection, "Surat", columnName, columnDef);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical migration failed for Surat.{Column}", columnName);
                        // Don't throw here, continue with other columns
                    }
                }
            }

            if (await TableExistsAsync(connection, "Warga"))
            {
                var criticalWargaColumns = new[]
                {
                    ("CreatedAt", "TEXT"),
                    ("UpdatedAt", "TEXT")
                };

                foreach (var (columnName, columnDef) in criticalWargaColumns)
                {
                    try
                    {
                        await EnsureColumnExistsAsync(connection, "Warga", columnName, columnDef);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical migration failed for Warga.{Column}", columnName);
                        // Don't throw here, continue with other columns
                    }
                }
            }

            _logger.LogInformation("Critical migration completed");
        }

        // ? EXECUTE COMPLETE DATABASE SCHEMA
        private async Task ExecuteDatabaseSchemaAsync(SqliteConnection connection)
        {
            var dbFile = Path.Combine(AppContext.BaseDirectory, "desa.db.sql");
            if (!File.Exists(dbFile))
            {
                _logger.LogWarning("File schema SQL tidak ditemukan: {DbFile}", dbFile);
                return;
            }

            _logger.LogInformation("Executing database schema from: {DbFile}", dbFile);

            var schemaSql = await File.ReadAllTextAsync(dbFile);
            var cleanedSql = schemaSql
                .Replace("BEGIN TRANSACTION;", "")
                .Replace("COMMIT;", "");

            // Split SQL commands while preserving complex structures
            var commands = SplitSqlCommands(cleanedSql);

            foreach (var cmd in commands)
            {
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                try
                {
                    await connection.ExecuteAsync(cmd);
                    _logger.LogDebug("Schema command executed successfully");
                }
                catch (SqliteException ex) when (ex.Message.Contains("already exists"))
                {
                    // Object already exists, which is fine
                    _logger.LogDebug("Database object already exists, skipping");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal mengeksekusi perintah SQL: {Command}", cmd.Substring(0, Math.Min(100, cmd.Length)));
                    // Don't throw here, continue with other commands
                }
            }
        }

        // ? INITIALIZE NEW FEATURES
        private async Task InitializeNewFeaturesAsync(SqliteConnection connection)
        {
            _logger.LogInformation("Initializing new features...");

            // ? 1. Create audit log table jika belum ada
            await CreateAuditLogTableAsync(connection);

            // ? 1a. Create tabel permintaan surat online (WhatsApp)
            await CreatePermintaanWaTableAsync(connection);

            // ? 1b. Migrasi kolom mode Google Sheet (Sumber, SheetToken, SheetRowId)
            await MigratePermintaanWaSheetColumnsAsync(connection);

            // ? 2. Create views jika belum ada
            await CreateViewsAsync(connection);

            // ? 3. Create triggers jika belum ada
            await CreateTriggersAsync(connection);

            // ? 4. Create additional indexes
            await CreateAdditionalIndexesAsync(connection);

            // ? 5. Insert default data for new features
            await InsertDefaultDataAsync(connection);

            _logger.LogInformation("New features initialized successfully");
        }

        // ? CREATE AUDIT LOG TABLE
        private async Task CreateAuditLogTableAsync(SqliteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS AuditLog (
                    ID_Log INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    RecordID INTEGER NOT NULL,
                    Action TEXT NOT NULL CHECK (Action IN ('INSERT', 'UPDATE', 'DELETE')),
                    OldValues TEXT,
                    NewValues TEXT,
                    UserID TEXT,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            await connection.ExecuteAsync(sql);
            _logger.LogInformation("Tabel AuditLog dimigrasikan.");
        }

        /// <summary>
        /// Migrasi kolom mode Google Sheet pada tabel PermintaanWa (Sumber,
        /// SheetToken, SheetRowId) untuk database lama yang tabelnya sudah ada.
        /// </summary>
        private async Task MigratePermintaanWaSheetColumnsAsync(SqliteConnection connection)
        {
            try
            {
                var kolomBaru = new[]
                {
                    "Sumber TEXT NOT NULL DEFAULT 'WA'",
                    "SheetToken TEXT",
                    "SheetRowId INTEGER"
                };

                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rows = await connection.QueryAsync<(int cid, string name)>(
                    "SELECT cid, name FROM pragma_table_info('PermintaanWa');");
                foreach (var row in rows) existing.Add(row.name);

                foreach (var def in kolomBaru)
                {
                    var nama = def.Split(' ')[0];
                    if (existing.Contains(nama)) continue;
                    try
                    {
                        await connection.ExecuteAsync($"ALTER TABLE PermintaanWa ADD COLUMN {def};");
                        _logger.LogInformation("Kolom PermintaanWa ditambahkan: {Kolom}", nama);
                    }
                    catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
                    {
                        // Kolom sudah ada — aman diabaikan.
                    }
                }
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Migrasi kolom Sheet PermintaanWa: kolom sudah ada.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal migrasi kolom Sheet PermintaanWa");
            }
        }

        // ? CREATE PERMINTAAN WA TABLE
        private async Task CreatePermintaanWaTableAsync(SqliteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS PermintaanWa (
                    ID_Permintaan    INTEGER PRIMARY KEY AUTOINCREMENT,
                    KodePermintaan   TEXT NOT NULL,
                    NomorWA          TEXT NOT NULL,
                    NamaWarga        TEXT,
                    NIK              TEXT,
                    NamaJenis        TEXT NOT NULL,
                    PesanMentah      TEXT,
                    DataJson         TEXT,
                    Status           TEXT NOT NULL DEFAULT 'BARU',
                    IdSurat          INTEGER,
                    IsRead           INTEGER NOT NULL DEFAULT 0,
                    TanggalPermintaan TEXT NOT NULL,
                    TanggalDiproses  TEXT,
                    Catatan          TEXT,
                    PesanBalasan     TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_status ON PermintaanWa(Status);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_wa ON PermintaanWa(NomorWA);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_isread ON PermintaanWa(IsRead);";

            await connection.ExecuteAsync(sql);
            _logger.LogInformation("Tabel PermintaanWa dimigrasikan.");
        }

        // ? CREATE VIEWS
        private async Task CreateViewsAsync(SqliteConnection connection)
        {
            var views = new[]
            {
                @"CREATE VIEW IF NOT EXISTS v_SuratLengkap AS
                SELECT 
                    s.ID_Surat,
                    s.NomorSurat,
                    s.TanggalSurat,
                    js.NamaJenis,
                    js.KodeJenis,
                    w.Nama as NamaWarga,
                    w.NIK,
                    s.Keterangan,
                    s.Keperluan,
                    s.Status,
                    s.CreatedAt
                FROM Surat s
                LEFT JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga;",

                @"CREATE VIEW IF NOT EXISTS v_WargaAktif AS
                SELECT 
                    w.*,
                    COUNT(s.ID_Surat) as JumlahSurat
                FROM Warga w
                LEFT JOIN Surat s ON w.ID_Warga = s.ID_Warga
                GROUP BY w.ID_Warga;"
            };

            foreach (var viewSql in views)
            {
                try
                {
                    await connection.ExecuteAsync(viewSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create view: {ViewSql}", viewSql.Substring(0, Math.Min(50, viewSql.Length)));
                }
            }
        }

        // ? CREATE TRIGGERS
        private async Task CreateTriggersAsync(SqliteConnection connection)
        {
            var triggers = new[]
            {
                @"CREATE TRIGGER IF NOT EXISTS tr_warga_updated 
                    AFTER UPDATE ON Warga
                    FOR EACH ROW
                BEGIN
                    UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga;
                END;",

                @"CREATE TRIGGER IF NOT EXISTS tr_surat_updated 
                    AFTER UPDATE ON Surat
                    FOR EACH ROW
                BEGIN
                    UPDATE Surat SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Surat = NEW.ID_Surat;
                END;"
            };

            foreach (var triggerSql in triggers)
            {
                try
                {
                    await connection.ExecuteAsync(triggerSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create trigger: {TriggerSql}", triggerSql.Substring(0, Math.Min(50, triggerSql.Length)));
                }
            }
        }

        // ? CREATE ADDITIONAL INDEXES
        private async Task CreateAdditionalIndexesAsync(SqliteConnection connection)
        {
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK);",
                "CREATE INDEX IF NOT EXISTS idx_warga_nama ON Warga(Nama);",
                "CREATE INDEX IF NOT EXISTS idx_surat_status ON Surat(Status);",
                "CREATE INDEX IF NOT EXISTS idx_surat_createdat ON Surat(CreatedAt);",
                "CREATE INDEX IF NOT EXISTS idx_auditlog_table_record ON AuditLog(TableName, RecordID);",
                "CREATE INDEX IF NOT EXISTS idx_auditlog_timestamp ON AuditLog(Timestamp);"
            };

            foreach (var indexSql in indexes)
            {
                try
                {
                    await connection.ExecuteAsync(indexSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create index: {IndexSql}", indexSql);
                }
            }
        }

        // ? INSERT DEFAULT DATA
        private async Task InsertDefaultDataAsync(SqliteConnection connection)
        {
            // Insert IJIN_TINGGAL jenis surat jika belum ada
            var ijinTinggalSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (13, 'IJIN_TINGGAL', 'IJT', 'Surat Ijin Tinggal');";

            try
            {
                await connection.ExecuteAsync(ijinTinggalSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default IJIN_TINGGAL data");
            }

            // Insert NTCR jenis surat (N1-N4) jika belum ada
            var ntcrSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (14, 'NTCR_N1', 'N1T', 'Surat Pengantar Nikah (NTCR N1)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (15, 'NTCR_N2', 'N2T', 'Surat Keterangan Untuk Nikah (NTCR N2)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (16, 'NTCR_N3', 'N3T', 'Surat Persetujuan Calon Mempelai (NTCR N3)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (17, 'NTCR_N4', 'N4T', 'Surat Keterangan Orang Tua (NTCR N4)');";

            try
            {
                await connection.ExecuteAsync(ntcrSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default NTCR data");
            }

            // Update existing JenisSurat dengan deskripsi jika belum ada
            var updateDescriptions = @"
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Umum' WHERE ID_Jenis = 1 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Warga' WHERE ID_Jenis = 2 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Instansi' WHERE ID_Jenis = 3 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Usaha' WHERE ID_Jenis = 4 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Pengantar SKCK' WHERE ID_Jenis = 5 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Izin Orang Tua' WHERE ID_Jenis = 6 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Garapan Sawah' WHERE ID_Jenis = 7 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Kematian' WHERE ID_Jenis = 8 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Tidak Mampu' WHERE ID_Jenis = 9 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Beda Nama' WHERE ID_Jenis = 10 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Kenal Lahir' WHERE ID_Jenis = 11 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Ahli Waris' WHERE ID_Jenis = 12 AND (Deskripsi IS NULL OR Deskripsi = '');";

            try
            {
                await connection.ExecuteAsync(updateDescriptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update JenisSurat descriptions");
            }

            // Update/muat deskripsi NTCR (N1-N4) jika ID_Jenis sudah terisi
            try
            {
                await connection.ExecuteAsync(@"
                    UPDATE JenisSurat SET Deskripsi = 'Surat Pengantar Nikah (NTCR N1)' WHERE NamaJenis = 'NTCR_N1' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Untuk Nikah (NTCR N2)' WHERE NamaJenis = 'NTCR_N2' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Persetujuan Calon Mempelai (NTCR N3)' WHERE NamaJenis = 'NTCR_N3' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Orang Tua (NTCR N4)' WHERE NamaJenis = 'NTCR_N4' AND (Deskripsi IS NULL OR Deskripsi = '');");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update NTCR descriptions");
            }
        }

        // ? UPDATE SCHEMA VERSION
        private async Task UpdateSchemaVersionAsync(SqliteConnection connection, string version)
        {
            var sql = @"
                INSERT INTO SchemaVersion (Version, Description) 
                VALUES (@Version, @Description);";

            await connection.ExecuteAsync(sql, new
            {
                Version = version,
                Description = $"Schema updated to version {version} with enhanced features"
            });
        }

        // ? VALIDATE SCHEMA - IMPROVED
        public async Task<bool> ValidateSchemaAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();
                return await ValidateSchemaInternalAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schema validation failed");
                return false;
            }
        }

        private async Task<bool> ValidateSchemaInternalAsync(SqliteConnection connection)
        {
            // Check essential tables exist
            var essentialTables = new[]
            {
                "Surat", "Warga", "JenisSurat", "InfoDesa", "Kematian",
                "SKU", "SKTM", "IZIN", "Instansi", "Garapan", "AhliWaris",
                "BedaNama", "KenalLahir", "IjinTinggal", "AuditLog", "SchemaVersion",
                "NTCR"
            };

            foreach (var table in essentialTables)
            {
                var count = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@TableName",
                    new { TableName = table });

                if (count == 0)
                {
                    _logger.LogError("Required table missing: {TableName}", table);
                    return false;
                }
            }

            // Check essential views exist
            var essentialViews = new[] { "v_SuratLengkap", "v_WargaAktif" };
            foreach (var view in essentialViews)
            {
                var count = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='view' AND name=@ViewName",
                    new { ViewName = view });

                if (count == 0)
                {
                    _logger.LogWarning("View missing (non-critical): {ViewName}", view);
                }
            }

            // Check essential columns exist in Surat table (only if table exists)
            if (await TableExistsAsync(connection, "Surat"))
            {
                var requiredColumns = new[] { "Status", "CreatedAt", "UpdatedAt" };
                foreach (var column in requiredColumns)
                {
                    var columnExists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM pragma_table_info('Surat') WHERE name = @columnName COLLATE NOCASE",
                        new { columnName = column }) > 0;

                    if (!columnExists)
                    {
                        _logger.LogError("Required column missing in Surat table: {ColumnName}", column);
                        return false;
                    }
                }
            }

            // Check essential columns exist in Warga table (only if table exists)
            if (await TableExistsAsync(connection, "Warga"))
            {
                var requiredWargaColumns = new[] { "CreatedAt", "UpdatedAt" };
                foreach (var column in requiredWargaColumns)
                {
                    var columnExists = await connection.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(*) FROM pragma_table_info('Warga') WHERE name = @columnName COLLATE NOCASE",
                        new { columnName = column }) > 0;

                    if (!columnExists)
                    {
                        _logger.LogError("Required column missing in Warga table: {ColumnName}", column);
                        return false;
                    }
                }
            }

            _logger.LogInformation("Schema validation passed");
            return true;
        }

        // ? GET DATABASE STATISTICS
        public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
        {
            var stats = new Dictionary<string, object>();

            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();

                // Table counts
                stats["TotalSurat"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
                stats["TotalWarga"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");
                stats["TotalJenisSurat"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM JenisSurat");

                // Surat by status
                var statusCounts = await connection.QueryAsync<(string Status, int Count)>(
                    "SELECT COALESCE(Status, 'Unknown') as Status, COUNT(*) as Count FROM Surat GROUP BY Status");
                stats["SuratByStatus"] = statusCounts.ToDictionary(x => x.Status, x => x.Count);

                // Recent activity
                stats["RecentSurat"] = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Surat WHERE CreatedAt >= date('now', '-7 days')");

                // Database size
                var dbPath = connection.DataSource;
                if (File.Exists(dbPath))
                {
                    stats["DatabaseSizeBytes"] = new FileInfo(dbPath).Length;
                }

                // Schema version
                stats["SchemaVersion"] = await GetCurrentSchemaVersionAsync(connection);

                _logger.LogInformation("Database statistics retrieved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database statistics");
                stats["Error"] = ex.Message;
            }

            return stats;
        }

        // ? CREATE BACKUP
        public async Task CreateBackupAsync(string backupPath)
        {
            try
            {
                var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;

                if (!File.Exists(dbPath))
                {
                    throw new FileNotFoundException($"Database file not found: {dbPath}");
                }

                // Create backup directory if it doesn't exist
                var backupDir = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                // Copy database file
                File.Copy(dbPath, backupPath, overwrite: true);

                _logger.LogInformation("Database backup created: {BackupPath}", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create database backup");
                throw;
            }
        }

        // ? RESTORE BACKUP
        public async Task RestoreBackupAsync(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Backup file not found: {backupPath}");
                }

                var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;

                // Close any existing connections
                SqliteConnection.ClearAllPools();

                // Copy backup to database location
                File.Copy(backupPath, dbPath, overwrite: true);

                // Re-initialize to ensure schema is current
                await InitializeAsync();

                _logger.LogInformation("Database restored from backup: {BackupPath}", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore database backup");
                throw;
            }
        }

        // ? IMPROVED SQL COMMAND SPLITTING
        private IEnumerable<string> SplitSqlCommands(string sql)
        {
            var commands = new List<string>();
            var currentCommand = new StringBuilder();
            bool insideTrigger = false;
            bool insideView = false;
            bool insideQuotes = false;
            char quoteChar = '"';

            var lines = sql.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("--"))
                    continue;

                // Track quotes
                for (int i = 0; i < trimmedLine.Length; i++)
                {
                    if ((trimmedLine[i] == '"' || trimmedLine[i] == '\'') && !insideQuotes)
                    {
                        insideQuotes = true;
                        quoteChar = trimmedLine[i];
                    }
                    else if (trimmedLine[i] == quoteChar && insideQuotes)
                    {
                        insideQuotes = false;
                    }
                }

                if (!insideQuotes)
                {
                    if (trimmedLine.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
                    {
                        insideTrigger = true;
                        currentCommand.Clear();
                    }
                    else if (trimmedLine.StartsWith("CREATE VIEW", StringComparison.OrdinalIgnoreCase))
                    {
                        insideView = true;
                        currentCommand.Clear();
                    }
                }

                currentCommand.AppendLine(trimmedLine);

                if (!insideQuotes)
                {
                    if ((insideTrigger || insideView) && trimmedLine.Equals("END;", StringComparison.OrdinalIgnoreCase))
                    {
                        insideTrigger = false;
                        insideView = false;
                        commands.Add(currentCommand.ToString());
                        currentCommand.Clear();
                    }
                    else if (!insideTrigger && !insideView && trimmedLine.EndsWith(";"))
                    {
                        commands.Add(currentCommand.ToString());
                        currentCommand.Clear();
                    }
                }
            }

            // Add any remaining command
            if (currentCommand.Length > 0)
            {
                commands.Add(currentCommand.ToString());
            }

            return commands.Where(cmd => !string.IsNullOrWhiteSpace(cmd));
        }

        public async Task<DesaData?> GetInfoDesaAsync()
        {
            try
            {
                return await _desaRepository.GetInfoDesaAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil InfoDesa");
                throw;
            }
        }
    }
}
