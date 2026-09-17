using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System.Data;
using System.IO;
using System.Threading.Tasks;

namespace SuDesApp.db
{
    // Kelas untuk inisialisasi database
    public class DatabaseInitializer
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseInitializer(AppConfig config, ILogger<DatabaseService> logger)
        {
            _config = config;
            _logger = logger;
        }

        // Pastikan file database dan direktori ada
        private void EnsureDatabaseFileExists()
        {
            var dbPath = _config.DatabaseConnectionString.Replace("Data Source=", "").Trim();
            if (!Path.IsPathRooted(dbPath))
                dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                _logger.LogInformation($"Direktori database dibuat: {dir}");
            }

            if (!File.Exists(dbPath))
            {
                // Buat file database kosong
                File.Create(dbPath).Dispose();
                _logger.LogInformation($"File database dibuat: {dbPath}");
            }
        }

        // Inisialisasi database dan seed data
        public async Task InitializeAsync()
        {
            _logger.LogInformation("Memulai inisialisasi database...");

            // Pastikan database file dan direktori ada
            EnsureDatabaseFileExists();

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // Cek apakah tabel Warga ada
                var wargaTableCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Warga'", transaction);

                if (wargaTableCount == 0)
                {
                    await connection.ExecuteAsync(@"
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
                            Alamat TEXT,
                            Pendidikan TEXT,
                            Kewarganegaraan TEXT
                        )", transaction);
                    _logger.LogInformation("Tabel Warga dibuat.");
                }

                // Cek apakah tabel InfoDesa ada
                var infoDesaCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='InfoDesa'", transaction);

                if (infoDesaCount > 0)
                {
                    var existingData = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM InfoDesa", transaction);
                    if (existingData > 0)
                    {
                        _logger.LogInformation("Database sudah terinisialisasi dengan data InfoDesa");
                    }
                }

                await ExecuteSchemaScriptsAsync(connection, transaction);
                await SeedInitialDataAsync(connection, transaction);
                await transaction.CommitAsync();
                _logger.LogInformation("Database berhasil diinisialisasi");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Inisialisasi database gagal");
                throw;
            }
        }

        // Eksekusi skrip schema dari file desa.db.sql
        private async Task ExecuteSchemaScriptsAsync(SqliteConnection connection, IDbTransaction transaction)
        {
            var dbFile = Path.Combine(AppContext.BaseDirectory, "desa.db.sql");
            if (!File.Exists(dbFile))
            {
                _logger.LogWarning("File schema SQL tidak ditemukan: {DbFile}", dbFile);
                return;
            }

            var schemaSql = await File.ReadAllTextAsync(dbFile);
            var cleanedSql = schemaSql.Replace("BEGIN TRANSACTION;", "")
                                     .Replace("COMMIT;", "")
                                     .Replace("BEGIN;", "")
                                     .Replace("END;", "");

            var commands = cleanedSql.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(cmd => cmd.Trim())
                                    .Where(cmd => !string.IsNullOrWhiteSpace(cmd))
                                    .ToList();

            foreach (var cmd in commands)
            {
                try
                {
                    if (cmd.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) ||
                        cmd.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Executing schema command: {Command}", cmd);
                        await connection.ExecuteAsync(cmd, transaction: transaction);
                    }
                    else if (cmd.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
                    {
                        var upsertCmd = cmd.Replace("INSERT INTO", "INSERT OR REPLACE INTO");
                        _logger.LogDebug("Executing UPSERT: {Command}", upsertCmd);
                        await connection.ExecuteAsync(upsertCmd, transaction: transaction);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal mengeksekusi perintah SQL: {Command}", cmd);
                    throw; // Lempar pengecualian untuk memastikan rollback
                }
            }
        }

        // Seed data awal untuk JenisSurat
        private async Task SeedInitialDataAsync(SqliteConnection connection, IDbTransaction transaction)
        {
            // Seed JenisSurat
            var countJenisSurat = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM JenisSurat", transaction);

            if (countJenisSurat == 0)
            {
                await connection.ExecuteAsync(
                    @"INSERT OR REPLACE INTO JenisSurat (NamaJenis, KodeJenis)
                      VALUES 
                      ('SKD_UMUM', 'SKD'),
                      ('DOMISILI_WARGA', 'DOM_WRG'),
                      ('INSTANSI', 'DOM_INS'),
                      ('SKU', 'SKU'),
                      ('PENGANTAR_SKCK', 'SKCK'),
                      ('IZIN_ORTU', 'IZIN'),
                      ('GARAPAN_SAWAH', 'GRP_SAW'),
                      ('KEMATIAN', 'KEM'),
                      ('SKTM', 'SKTM'),
                      ('BEDANAMA', 'BEDANAMA')",
                    transaction);
                _logger.LogInformation("Data awal JenisSurat berhasil ditambahkan");
            }

            //// Seed InfoDesa
            //var countInfoDesa = await connection.ExecuteScalarAsync<int>(
            //    "SELECT COUNT(*) FROM InfoDesa", transaction);

            //if (countInfoDesa == 0)
            //{
            //    await connection.ExecuteAsync(@"
            //        INSERT OR REPLACE INTO InfoDesa (NamaDesa, Kecamatan, Kabupaten, Alamat, Kodepos, KepalaDesa, SekretarisDesa, NamaCamat, NipCamat, GolCamat)
            //        VALUES ('Desa Default', 'Kecamatan Default', 'Kabupaten Default', 'Alamat Default', '00000', 'Kepala Desa Default', 'Sekretaris Desa Default', 'Camat Default', 'N/A', 'N/A')",
            //        transaction);
            //    _logger.LogInformation("Data awal InfoDesa berhasil ditambahkan");
            //}
        }

        public async Task<DesaData?> GetInfoDesaAsync()
        {
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            try
            {
                var desa = await connection.QueryFirstOrDefaultAsync<DesaData>(
                    "SELECT * FROM InfoDesa LIMIT 1");
                _logger.LogDebug("Data InfoDesa diambil: NamaDesa={NamaDesa}", desa?.NamaDesa ?? "null");
                return desa;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil InfoDesa");
                throw new DataRetrievalException("Gagal mengambil InfoDesa", ex);
            }
        }
    }
}
