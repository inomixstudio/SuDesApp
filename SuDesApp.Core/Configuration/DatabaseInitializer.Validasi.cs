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

        // VALIDATE SCHEMA - IMPROVED
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

        // DATABASE STATISTICS / BACKUP / RESTORE
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

        // IMPROVED SQL COMMAND SPLITTING
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
