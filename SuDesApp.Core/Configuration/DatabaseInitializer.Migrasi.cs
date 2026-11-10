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

        // APPLY OPTIONAL MIGRATIONS
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
        // MIGRATION FROM v1 TO v2 - CRITICAL ONLY
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

        // EXECUTE COMPLETE DATABASE SCHEMA
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

    }
}
