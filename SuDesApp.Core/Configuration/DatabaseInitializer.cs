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

        // Backup/statistik dipindahkan ke DatabaseBackupService. Tiga method di
        // bawah ada hanya sebagai penerus supaya pemakil lama tidak perlu
        // diubah; logikanya tinggal di kelas itu.
        private readonly IDatabaseBackupService _backupService;

        // ? SCHEMA VERSION TRACKING
        private const string CURRENT_SCHEMA_VERSION = "2.0.0";

        public DatabaseInitializer(
            AppConfig config,
            ILogger<DatabaseInitializer> logger,
            IDesaRepository desaRepository,
            IJenisSuratRepository jenisSuratRepository,
            IWargaRepository wargaRepository,
            ISuratRepository suratRepository,
            IDatabaseBackupService backupService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
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

                // JenisSurat.Deskripsi: kolom tambahan yang dipakai saat menyemai jenis surat
                // (termasuk blanko NTCR N1–N6). Database yang dibuat dari desa.db.sql versi
                // lama belum punya kolom ini, dan penyemaian yang menyebutnya akan gagal
                // seluruhnya — daftar jenis surat pun tidak pernah terisi.
                if (await TableExistsAsync(connection, "JenisSurat"))
                {
                    await EnsureColumnExistsAsync(connection, "JenisSurat", "Deskripsi", "TEXT");
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
            var commands = SplitSqlCommands(cleanedSql)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            // PRAGMA (mis. journal_mode=WAL) tidak boleh berada di dalam transaksi,
            // jadi dijalankan lebih dulu — sendirian di luar transaksi.
            foreach (var pragma in commands.Where(IsPragma))
            {
                await EksekusiAmanAsync(connection, pragma);
            }

            // Sisanya dibungkus SATU transaksi: SQLite hanya melakukan commit (fsync)
            // sekali di akhir, bukan satu kali per perintah. Skema desa.db.sql berisi
            // puluhan perintah CREATE, jadi tanpa transaksi ini bagian startup yang
            // paling mahal di setiap kali aplikasi dibuka.
            try
            {
                await connection.ExecuteAsync("BEGIN;");

                foreach (var cmd in commands.Where(c => !IsPragma(c)))
                {
                    await EksekusiAmanAsync(connection, cmd);
                }

                await connection.ExecuteAsync("COMMIT;");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaksi skema database gagal — dikembalikan seperti semula.");
                try { await connection.ExecuteAsync("ROLLBACK;"); }
                catch { /* koneksi mungkin sudah tertutup/bubar; biarkan */ }
            }
        }

        private static bool IsPragma(string sql)
            => sql.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);

        private async Task EksekusiAmanAsync(SqliteConnection connection, string cmd)
        {
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

        // ? INITIALIZE NEW FEATURES
        private async Task InitializeNewFeaturesAsync(SqliteConnection connection)
        {
            _logger.LogInformation("Initializing new features...");

            // ? 1. Create audit log table jika belum ada
            await CreateAuditLogTableAsync(connection);

            // ? 1a. Tabel Perangkat Desa (dibuat + migrasi nama jabatan lama)
            await EnsurePerangkatDesaTableAsync(connection);

            // ? 1b. Tabel status tutup buku tahunan (fitur baru; tidak ada di desa.db.sql versi awal)
            await EnsureTutupBukuTahunTableAsync(connection);

            // ? 1c. Tabel bukti langganan (kode aktivasi terhubung ke database desa)
            await EnsureLisensiAktivasiTableAsync(connection);

            // ? 2. Create views jika belum ada
            await CreateViewsAsync(connection);

            // ? 3. Create triggers jika belum ada
            await CreateTriggersAsync(connection);

            // ? 4. Create additional indexes
            await CreateAdditionalIndexesAsync(connection);

            // ? 5. Insert default data for new features
            await InsertDefaultDataAsync(connection);

            // ? 6. Buang sisa data Model N7 (sudah tidak dipakai aplikasi)
            await HapusSisaDataN7Async(connection);

            // ? 7. Kosongkan Pejabat Kecamatan yang masih berisi contoh bawaan
            await BersihkanPejabatKecamatanContohAsync(connection);

            // 🧹 8. Buang contoh warga bawaan (desa.db.sql tak lagi menyuntiknya)
            await BersihkanWargaContohAsync(connection);

            _logger.LogInformation("New features initialized successfully");
        }

        // ? BERSIHKAN PEJABAT KECAMATAN CONTOH
        /// <summary>
        /// Kolom Pejabat Kecamatan (Nama Camat, NIP Camat, Golongan) tidak lagi diisi
        /// contoh bawaan karena tidak semua surat memakainya. Database lama yang masih
        /// menyimpan nilai contoh dibersihkan di sini — hanya bila nilainya PERSIS
        /// contoh bawaan, jadi data camat yang benar-benar diisi pengguna tidak tersentuh.
        ///
        /// Aman dijalankan berulang: setelah bersih, tidak ada baris yang cocok lagi.
        /// </summary>
        private async Task BersihkanPejabatKecamatanContohAsync(SqliteConnection connection)
        {
            try
            {
                if (!await TableExistsAsync(connection, "InfoDesa"))
                {
                    return;
                }

                int terhapus = await connection.ExecuteAsync(
                    @"UPDATE InfoDesa
                         SET NamaCamat = CASE WHEN TRIM(COALESCE(NamaCamat, '')) IN ('Nama Camat', 'Default Camat') THEN '' ELSE NamaCamat END,
                             NipCamat  = CASE WHEN TRIM(COALESCE(NipCamat, '')) IN ('000000000000000000', '0') THEN '' ELSE NipCamat END,
                             GolCamat  = CASE WHEN TRIM(COALESCE(GolCamat, '')) IN ('IV/a', 'IV/a ') THEN '' ELSE GolCamat END
                       WHERE TRIM(COALESCE(NamaCamat, '')) IN ('Nama Camat', 'Default Camat')
                          OR TRIM(COALESCE(NipCamat, '')) IN ('000000000000000000', '0')
                          OR TRIM(COALESCE(GolCamat, '')) IN ('IV/a', 'IV/a ')");

                if (terhapus > 0)
                {
                    _logger.LogInformation(
                        "Pejabat Kecamatan contoh dikosongkan pada {Jumlah} baris InfoDesa.", terhapus);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan data opsional: jangan gagalkan startup karenanya.
                _logger.LogWarning(ex, "Gagal membersihkan Pejabat Kecamatan contoh (tidak fatal).");
            }
        }

        // 🧹 BERSIHKAN CONTOH WARGA
        /// <summary>
        /// desa.db.sql versi lama menyuntik satu warga contoh ("NAMA ORANG TUA",
        /// NIK 1234567890123456) setiap database dibuat — instalasi baru jadi sudah
        /// terisi contoh. Penyemaian itu dihapus supaya data warga mulai kosong;
        /// database lama dibersihkan di sini — hanya bila NIK DAN namanya persis
        /// contoh bawaan, jadi data warga buatan pengguna tidak tersentuh.
        ///
        /// Aman dijalankan berulang: setelah bersih, tidak ada baris yang cocok
        /// lagi. Bila baris itu sudah dirujuk surat (FK RESTRICT), pembersihan
        /// dilewati tanpa menggagalkan startup.
        /// </summary>
        private async Task BersihkanWargaContohAsync(SqliteConnection connection)
        {
            try
            {
                if (!await TableExistsAsync(connection, "Warga"))
                {
                    return;
                }

                int terhapus = await connection.ExecuteAsync(
                    @"DELETE FROM Warga
                       WHERE NIK = '1234567890123456'
                         AND Nama = 'NAMA ORANG TUA'");

                if (terhapus > 0)
                {
                    _logger.LogInformation(
                        "Warga contoh bawaan dihapus dari tabel Warga: {Jumlah} baris.", terhapus);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan data opsional: jangan gagalkan startup karenanya.
                _logger.LogWarning(ex, "Gagal membersihkan warga contoh (tidak fatal).");
            }
        }

        // ? BUANG SISA DATA MODEL N7
        /// <summary>
        /// Model N7 (penolakan kehendak nikah/rujuk) sudah tidak ada di aplikasi:
        /// blanko itu diterbitkan KUA, bukan kantor desa. Migrasi ini membuang sisa
        /// datanya pada database lama — surat beserta detail NTCR-nya dan baris jenis
        /// suratnya — supaya register, penomoran, dan pemilihan jenis surat bersih.
        ///
        /// Aman untuk dijalankan berulang: bila tidak ada data N7, tidak terjadi apa-apa.
        /// Sebelum menghapus, berkas database dicadangkan ke folder "Backup" di samping
        /// database, sehingga surat lama masih bisa dikembalikan bila diperlukan.
        /// </summary>
        private async Task HapusSisaDataN7Async(SqliteConnection connection)
        {
            const string jenisN7 = "NTCR_N7";

            try
            {
                // Tabel Surat tidak menyimpan NamaJenis; jenis surat dibaca lewat
                // relasi ke JenisSurat (s.ID_Jenis = j.ID_Jenis).
                const string hitungSuratN7 = @"
                    SELECT COUNT(*) FROM Surat s
                    INNER JOIN JenisSurat j ON j.ID_Jenis = s.ID_Jenis
                    WHERE j.NamaJenis = @Jenis COLLATE NOCASE";

                int suratN7 = await TableExistsAsync(connection, "Surat") && await TableExistsAsync(connection, "JenisSurat")
                    ? await connection.ExecuteScalarAsync<int>(hitungSuratN7, new { Jenis = jenisN7 })
                    : 0;

                int jenisN7Tersisa = await TableExistsAsync(connection, "JenisSurat")
                    ? await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE", new { Jenis = jenisN7 })
                    : 0;

                if (suratN7 == 0 && jenisN7Tersisa == 0)
                {
                    return;
                }

                _logger.LogWarning(
                    "Migrasi N7: ditemukan {Surat} surat, {Jenis} jenis surat. " +
                    "Model N7 sudah tidak dipakai — data lama akan dibuang setelah dicadangkan.",
                    suratN7, jenisN7Tersisa);

                await CadangkanSebelumHapusN7Async();

                if (suratN7 > 0)
                {
                    if (await TableExistsAsync(connection, "NTCR"))
                    {
                        await connection.ExecuteAsync(@"
                            DELETE FROM NTCR WHERE ID_Surat IN (
                                SELECT s.ID_Surat FROM Surat s
                                INNER JOIN JenisSurat j ON j.ID_Jenis = s.ID_Jenis
                                WHERE j.NamaJenis = @Jenis COLLATE NOCASE)",
                            new { Jenis = jenisN7 });
                    }

                    // Hapus suratnya selagi baris jenis suratnya masih ada (sumber nama jenis).
                    await connection.ExecuteAsync(@"
                        DELETE FROM Surat WHERE ID_Jenis IN (
                            SELECT ID_Jenis FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE)",
                        new { Jenis = jenisN7 });
                }

                if (jenisN7Tersisa > 0)
                {
                    await connection.ExecuteAsync(
                        "DELETE FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE", new { Jenis = jenisN7 });
                }

                _logger.LogInformation("Migrasi N7 selesai: sisa data Model N7 dihapus.");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan startup hanya karena pembersihan data opsional ini.
                _logger.LogWarning(ex, "Gagal membersihkan sisa data Model N7 (tidak fatal).");
            }
        }

        /// <summary>
        /// Cadangkan berkas database sebelum data N7 dibuang: "&lt;database&gt;.sebelum-hapus-N7-
        /// &lt;stempel waktu&gt;.bak" di folder "Backup" sebelah berkas database.
        /// Bila pencadangan gagal, penghapusan dibatalkan demi keamanan data.
        /// </summary>
        private async Task CadangkanSebelumHapusN7Async()
        {
            var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;
            var dbDir = Path.GetDirectoryName(dbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var nama = Path.GetFileNameWithoutExtension(dbPath);

            var backupPath = Path.Combine(dbDir, "Backup",
                $"{nama}.sebelum-hapus-N7-{DateTime.Now:yyyyMMdd-HHmmss}.bak");

            try
            {
                await CreateBackupAsync(backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pencadangan sebelum hapus data N7 gagal.");
                throw new InvalidOperationException(
                    "Data N7 tidak dihapus karena pencadangan database gagal.", ex);
            }
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
        /// Pastikan tabel <c>PerangkatDesa</c> ada, kolom <c>BerkasSK</c> sudah
        /// ada, indeksnya terbentuk, dan nama jabatan lama sudah dipetakan ke nama
        /// resmi terbaru. Satu metode di repository yang menangani semuanya supaya
        /// langkah-langkah ini tidak tercecer di banyak tempat.
        /// </summary>
        private async Task EnsurePerangkatDesaTableAsync(SqliteConnection connection)
        {
            try
            {
                await PerangkatDesaRepository.EnsurePerangkatDesaAsync(connection);
                _logger.LogInformation("Tabel PerangkatDesa siap dipakai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan tabel PerangkatDesa");
                throw;
            }
        }

        /// <summary>
        /// Tabel status tutup buku tahunan. Skemanya hidup di repository sebagai
        /// satu-satunya sumber; tanpa langkah ini database lama tidak pernah
        /// memiliki tabelnya (desa.db.sql versi awal tidak memuatnya) sehingga
        /// halaman Tutup Buku Tahunan gagal dengan "no such table".
        /// </summary>
        private async Task EnsureTutupBukuTahunTableAsync(SqliteConnection connection)
        {
            try
            {
                await TutupBukuTahunRepository.EnsureAsync(connection);
                _logger.LogInformation("Tabel TutupBukuTahun siap dipakai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan tabel TutupBukuTahun");
                throw;
            }
        }

        /// <summary>
        /// Tabel bukti langganan. Menyimpan token bertanda tangan hasil
        /// pengecekan server ke dalam database desa, supaya kode aktivasi
        /// terhubung ke data yang sama dan ikut terbawa saat aplikasi
        /// mencadangkan datanya. Tanpa langkah ini, penyimpanan bukti
        /// langganan gagal dengan "no such table" dan aplikasi kembali
        /// ke cache berkas saja.
        /// </summary>
        private async Task EnsureLisensiAktivasiTableAsync(SqliteConnection connection)
        {
            try
            {
                await LisensiAktivasiRepository.EnsureAsync(connection);
                _logger.LogInformation("Tabel LisensiAktivasi siap dipakai.");
            }
            catch (Exception ex)
            {
                // Kegagalan menyiapkan tabel lisensi tidak boleh menggagalkan
                // start aplikasi: cache berkas lisensi-cache.json masih
                // tersedia sebagai sumber offline, jadi desa yang sudah
                // langganan tidak terkunci hanya karena tabel ini belum ada.
                _logger.LogError(ex, "Gagal menyiapkan tabel LisensiAktivasi");
            }
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

            // Insert jenis surat REKENING_KORAN (permohonan print out rekening koran
            // ke bank) jika belum ada. Surat ini surat keluar desa dan ikut penomoran
            // bersama seperti SKD, jadi barisnya wajib ada agar nomornya bisa
            // dihasilkan dan surat tercatat di register.
            var rekeningKoranSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (21, 'REKENING_KORAN', 'REKKOR', 'Permohonan Print Out Rekening Koran');";

            try
            {
                await connection.ExecuteAsync(rekeningKoranSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default REKENING_KORAN data");
            }

            // Insert jenis surat NTCR yang dibuat kantor desa (blanko N1-N6 + surat
            // numpang nikah N8) jika belum ada. Model N7 (penolakan kehendak
            // nikah/rujuk) diterbitkan KUA, jadi tidak ikut disemai; sisa data N7 pada
            // database lama dibersihkan oleh HapusSisaDataN7Async.
            var ntcrSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (14, 'NTCR_N1', 'N1T', 'Surat Pengantar Nikah (NTCR N1)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (15, 'NTCR_N2', 'N2T', 'Permohonan Kehendak Nikah (NTCR N2)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (16, 'NTCR_N3', 'N3T', 'Permohonan Pencatatan Isbat (NTCR N3)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (17, 'NTCR_N4', 'N4T', 'Persetujuan Calon Pengantin (NTCR N4)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (18, 'NTCR_N5', 'N5T', 'Surat Izin Orang Tua (NTCR N5)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (19, 'NTCR_N6', 'N6T', 'Keterangan Kematian Suami/Istri (NTCR N6)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (20, 'NTCR_N8', 'N8T', 'Surat Keterangan Numpang Nikah (NTCR N8)');";

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

            // Update/muat deskripsi NTCR (N1-N6, N8) jika belum terisi. Deskripsi lama
            // yang masih memakai penamaan keliru (N2-N4 sebelum disesuaikan dengan
            // blanko resmi) ikut dimigrasi agar menu tidak menampilkan nama formulir
            // yang salah.
            try
            {
                await connection.ExecuteAsync(@"
                    UPDATE JenisSurat SET Deskripsi = 'Surat Pengantar Nikah (NTCR N1)' WHERE NamaJenis = 'NTCR_N1' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Kehendak Nikah (NTCR N2)' WHERE NamaJenis = 'NTCR_N2' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Keterangan Untuk Nikah (NTCR N2)');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Pencatatan Isbat (NTCR N3)' WHERE NamaJenis = 'NTCR_N3' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Persetujuan Calon Mempelai (NTCR N3)');
                    UPDATE JenisSurat SET Deskripsi = 'Persetujuan Calon Pengantin (NTCR N4)' WHERE NamaJenis = 'NTCR_N4' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Keterangan Orang Tua (NTCR N4)');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Izin Orang Tua (NTCR N5)' WHERE NamaJenis = 'NTCR_N5' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Keterangan Kematian Suami/Istri (NTCR N6)' WHERE NamaJenis = 'NTCR_N6' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Numpang Nikah (NTCR N8)' WHERE NamaJenis = 'NTCR_N8' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Print Out Rekening Koran' WHERE NamaJenis = 'REKENING_KORAN' AND (Deskripsi IS NULL OR Deskripsi = '');");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update NTCR descriptions");
            }
        }

        // ? UPDATE SCHEMA VERSION
        private async Task UpdateSchemaVersionAsync(SqliteConnection connection, string version)
        {
            // Idempoten. Sebelumnya setiap kali aplikasi dibuka satu baris baru
            // ditambahkan tanpa syarat, jadi tabel membengkak setiap hari tanpa
            // informasi apa pun. Lebih buruk: GetCurrentSchemaVersionAsync
            // membaca "baris terbaru" dari kolom DATETIME berpresisi detik,
            // jadi dua aplikasi yang mulai di detik sama menghasilkan versi
            // yang tidak menentu.
            var sql = @"
                INSERT INTO SchemaVersion (Version, Description)
                SELECT @Version, @Description
                WHERE NOT EXISTS (
                    SELECT 1 FROM SchemaVersion WHERE Version = @Version
                );";

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
                "NTCR",
                // Validasi berjalan setelah InitializeNewFeaturesAsync (langkah 8),
                // jadi tabel ini selalu sudah dibuat lebih dulu. Memasukkannya di
                // sini membuat instalasi yang benar-benar rusak (tabel hilang
                // walau migrasi sempat jalan) terdeteksi, bukan baru terasa saat
                // pengguna membuka menu Perangkat Desa.
                "PerangkatDesa"
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

        // ? DATABASE STATISTICS / BACKUP / RESTORE
        //
        // Ketiganya kini tinggal di DatabaseBackupService. Yang tersisa di sini
        // hanyalah penerus supaya pemakil IDatabaseInitializer tidak perlu
        // diubah pada refactor yang sama. Kalau pemakil mau, ia bisa langsung
        // memakai IDatabaseBackupService dan ketiga method ini boleh dihapus.

        public Task<Dictionary<string, object>> GetDatabaseStatsAsync()
            => _backupService.GetStatsAsync();

        public Task CreateBackupAsync(string backupPath)
            => _backupService.BuatBackupAsync(backupPath);

        public async Task RestoreBackupAsync(string backupPath)
        {
            // Cadangan pra-restore baru dibuang SETELAH skema terbukti bisa
            // dibuka lewat InitializeAsync. Bila tidak, berkas biarkan di disk
            // dan laporkan — pengguna masih bisa memulihkan manual.
            var cadangan = await _backupService.PulihkanBackupAsync(backupPath);

            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Restore selesai menyalin, TAPI InitializeAsync gagal. Database mungkin tidak bisa dibuka - " +
                    "cadangan pra-restore {Cadangan} DIBIARKAN dan harus dipulihkan manual.", cadangan);
                throw;
            }

            if (!string.IsNullOrWhiteSpace(cadangan))
            {
                _backupService.BuangCadanganPraRestore(cadangan);
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
