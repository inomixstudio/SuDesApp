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
    public partial class DatabaseInitializer : IDatabaseInitializer
    {

        // ENSURE REQUIRED COLUMNS EXIST - SIMPLIFIED
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

        // NEW: Check if table exists
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

        // VALIDATE SCHEMA WITH RETRY
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

        // CREATE SCHEMA VERSION TABLE
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

        // GET CURRENT SCHEMA VERSION
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

        // CHECK IF MIGRATION IS NEEDED - IMPROVED
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

        // MIGRATE SCHEMA - IMPROVED SAFETY
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

        // CHECK IF THIS IS FIRST RUN
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

    }
}
