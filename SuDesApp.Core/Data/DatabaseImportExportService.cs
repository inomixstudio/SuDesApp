using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SuDesApp.Configuration
{
public class DatabaseImportExportService
{
    private readonly AppConfig _config;
    private readonly ILogger<DatabaseImportExportService> _logger;
    private readonly ISuratRepository _suratRepository;
    private readonly IWargaRepository _wargaRepository;
    private readonly IDesaRepository _desaRepository;
    private readonly IJenisSuratRepository _jenisSuratRepository;
    private readonly IIzinOrtuRepository _izinOrtuRepository;
    private const int CurrentSchemaVersion = 1;

    public DatabaseImportExportService(
        AppConfig config,
        ILogger<DatabaseImportExportService> logger,
        ISuratRepository suratRepository,
        IWargaRepository wargaRepository,
        IDesaRepository desaRepository,
        IJenisSuratRepository jenisSuratRepository,
        IIzinOrtuRepository izinOrtuRepository)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
        _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
        _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
        _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
        _izinOrtuRepository = izinOrtuRepository ?? throw new ArgumentNullException(nameof(izinOrtuRepository));
    }

    public async Task InitializeAsync()
        {
            // Initialize databases using repositories
            await _suratRepository.InitializeSuratIndexesAsync();
            await _wargaRepository.InitializeWargaTableAsync();
            await _jenisSuratRepository.InitializeJenisSuratDataAsync();
            await _desaRepository.InitializeAsync();
            await _izinOrtuRepository.InitializeAsync();
        }

        public async Task ImportDatabaseAsync(string sourceDbPath)
    {
        try
        {
            _logger.LogInformation("Memulai proses impor database dari: {SourcePath}", sourceDbPath);

            if (!File.Exists(sourceDbPath))
                throw new FileNotFoundException($"File database tidak ditemukan di: {sourceDbPath}");

            if (!await IsValidSQLiteDatabaseAsync(sourceDbPath))
                throw new InvalidOperationException("File database tidak valid atau rusak (integrity check gagal).");

            string currentDbPath = GetDatabasePath();
            string? dbDir = Path.GetDirectoryName(currentDbPath);

            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir!);

            // Migrasi dilakukan pada file sementara terlebih dahulu agar database aktif
            // tidak ditimpa saat masih dibuka oleh koneksi lain (mencegah file korup).
            string tempPath = Path.Combine(dbDir!, $"desa_import_{DateTime.Now:yyyyMMddHHmmss}.db");
            try
            {
                File.Copy(sourceDbPath, tempPath, true);
                _logger.LogInformation("Sumber disalin ke file sementara: {TempPath}", tempPath);

                // Berkas impor bisa plaintext (ekspor lama) maupun sudah terenkripsi;
                // koneksi yang tepat ditentukan dari bentuk berkasnya.
                var tempConnectionString = EnkripsiDatabase.DeteksiKoneksi(tempPath)
                    ?? throw new InvalidOperationException(
                        "Berkas impor tidak bisa dibuka: rusak atau terenkripsi dengan kunci mesin lain.");

                using (var conn = new SqliteConnection(tempConnectionString))
                {
                    await conn.OpenAsync();
                    await MigrateDatabaseAsync(conn);
                }

                if (!await IsValidSQLiteDatabaseAsync(tempPath))
                    throw new InvalidOperationException("Database hasil migrasi tidak valid (integrity check gagal).");

                if (File.Exists(currentDbPath))
                {
                    // Pastikan seluruh data di berkas WAL sudah masuk berkas utama
                    // sebelum database dicadangkan & diganti, lalu tutup semua
                    // koneksi pool — handle lama yang masih memetakan berkas saat
                    // diganti adalah penyebab klasik korupsi indeks SQLite.
                    SqliteConnection.ClearAllPools();
                    using (var conn = new SqliteConnection(_config.DatabaseConnectionString))
                    {
                        await conn.OpenAsync();
                        using var cmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", conn);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    SqliteConnection.ClearAllPools();

                    string backupPath = Path.Combine(dbDir!, $"desa_backup_before_import_{DateTime.Now:yyyyMMddHHmmss}.db");
                    File.Copy(currentDbPath, backupPath, true);
                    _logger.LogInformation("Database saat ini dicadangkan ke: {BackupPath}", backupPath);
                }

                DeleteSidecarFiles(currentDbPath);
                File.Copy(tempPath, currentDbPath, true);
                SqliteConnection.ClearAllPools(); // buang handle lama yang menunjuk berkas bekas
                _logger.LogInformation("Database berhasil disalin ke: {Path}", currentDbPath);
            }
            finally
            {
                DeleteSidecarFiles(tempPath);
                TryDeleteFile(tempPath);
            }

            // Berkas hasil impor bisa berbentuk plaintext, sedangkan koneksi
            // aplikasi yang sudah ber-kunci tidak akan bisa membukanya. Jadikan
            // bentuk finalnya terenkripsi dengan kunci mesin ini sebelum dipakai;
            // cadangan plaintext tidak dibuat lagi karena impor sudah mencadangkan
            // database lama ke desa_backup_before_import_*.db.
            _config.DatabaseConnectionString = EnkripsiDatabase.JaminTerkunci(
                _config.DatabaseConnectionString, _logger, cadangkanPlaintext: false);

            // Panggil InitializeAsync setelah transaksi migrasi selesai
            await InitializeAsync();
            _logger.LogInformation("Impor dan migrasi database selesai dengan sukses.");
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terjadi kesalahan saat mengimpor database.");
            throw new Exception($"Terjadi kesalahan saat mengimpor database: {ex.Message}", ex);
        }
    }

    public async Task ExportDatabaseAsync(string targetDbPath)
    {
        try
        {
            _logger.LogInformation("Memulai proses ekspor database ke: {TargetPath}", targetDbPath);

            string currentDbPath = GetDatabasePath();
            if (!File.Exists(currentDbPath))
                throw new FileNotFoundException($"Database saat ini tidak ditemukan di: {currentDbPath}");

            string? targetDir = Path.GetDirectoryName(targetDbPath);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir!);

            // Gunakan VACUUM INTO untuk menghasilkan snapshot yang konsisten
            // (termasuk data yang masih berada di WAL) alih-alih menyalin file mentah.
            if (File.Exists(targetDbPath))
                File.Delete(targetDbPath);

            // Koneksi database aktif (bisa ber-kunci SQLCipher) — bukan jalur mentah,
            // supaya VACUUM INTO tetap bisa membaca seluruh isi database.
            using (var conn = new SqliteConnection(_config.DatabaseConnectionString))
            {
                await conn.OpenAsync();
                using var cmd = new SqliteCommand($"VACUUM INTO '{EscapeSqlString(targetDbPath)}'", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            _logger.LogInformation("Database berhasil diekspor ke: {TargetPath}", targetDbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengekspor database ke: {TargetPath}", targetDbPath);
            throw new Exception($"Gagal mengekspor database: {ex.Message}", ex);
        }
    }

    private static string EscapeSqlString(string value)
    {
        return value.Replace("'", "''");
    }

    private void DeleteSidecarFiles(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            return;

        TryDeleteFile(dbPath + "-wal");
        TryDeleteFile(dbPath + "-shm");
        TryDeleteFile(dbPath + "-journal");
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Gagal menghapus file: {Path}", filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Tidak dapat menghapus file: {Path}", filePath);
        }
    }
    private async Task<int> GetRowCountAsync(SqliteConnection conn, string tableName, SqliteTransaction? transaction = null)
    {
        using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {tableName}", conn, transaction);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
    private async Task MigrateDatabaseAsync(SqliteConnection conn)
    {
        using (var transaction = conn.BeginTransaction())
        {
            try
            {
                int currentVersion = await GetSchemaVersionAsync(conn, transaction);
                _logger.LogInformation("Versi skema saat ini: {Version}", currentVersion);

                if (currentVersion < CurrentSchemaVersion)
                {
                    // Sebelum migrasi
                    var countBefore = await GetRowCountAsync(conn, "Warga", transaction);
                    _logger.LogInformation($"Jumlah data Warga sebelum migrasi: {countBefore}");

                    _logger.LogInformation("Memulai migrasi skema dari versi {CurrentVersion} ke {TargetVersion}", currentVersion, CurrentSchemaVersion);
                    await ApplyVersion1MigrationAsync(conn, transaction);
                    await UpdateSchemaVersionAsync(conn, CurrentSchemaVersion, transaction);

                    // Setelah migrasi
                    var countAfter = await GetRowCountAsync(conn, "Warga", transaction);
                    _logger.LogInformation($"Jumlah data Warga setelah migrasi: {countAfter}");

                    if (countBefore != countAfter)
                    {
                        _logger.LogWarning("Perbedaan jumlah data Warga sebelum dan setelah migrasi!");
                    }
                    _logger.LogInformation("Migrasi skema selesai ke versi {Version}", CurrentSchemaVersion);
                }
                else
                {
                    _logger.LogInformation("Tidak diperlukan migrasi skema. Versi sudah terbaru: {Version}", currentVersion);
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Gagal melakukan migrasi skema.");
                throw new InvalidOperationException("Gagal melakukan migrasi skema.", ex);
            }
        }
    }

    private async Task ApplyVersion1MigrationAsync(SqliteConnection conn, SqliteTransaction transaction)
    {
        _logger.LogInformation("Menerapkan migrasi untuk versi 1...");

        try
        {
            var infoDesaColumns = await GetTableColumnsAsync(conn, "InfoDesa", transaction);
            if (!infoDesaColumns.Contains("SekretarisDesa"))
            {
                await ExecuteNonQueryAsync(conn, "ALTER TABLE InfoDesa ADD COLUMN SekretarisDesa TEXT;", transaction);
                _logger.LogInformation("Kolom 'SekretarisDesa' ditambahkan ke tabel InfoDesa.");
            }

            var wargaColumns = await GetTableColumnsAsync(conn, "Warga", transaction);
            if (!await HasUniqueConstraintAsync(conn, "Warga", "NIK", transaction))
            {
                // Pertama, backup data Warga ke tabel sementara
                await ExecuteNonQueryAsync(conn, @"
                CREATE TEMPORARY TABLE Warga_Backup AS 
                SELECT * FROM Warga;
            ", transaction);

                // Buat tabel baru dengan struktur yang diinginkan.
                //
                // Kolom di sini harus memuat SELURUH kolom yang dipakai aplikasi,
                // bukan hanya kolom KTP dasar: tabel lama dibuang lalu diganti tabel
                // ini, jadi kolom yang tidak dicantumkan hilang permanen bersama
                // datanya. Kolom yang tidak dicantumkan hilang permanen: itu
                // kehilangan data senyap, bukan sekadar kolom yang belum diisi.
                await ExecuteNonQueryAsync(conn, @"
                CREATE TABLE Warga_New (
                    ID_Warga INTEGER PRIMARY KEY AUTOINCREMENT,
                    NIK TEXT NOT NULL UNIQUE,
                    Nama TEXT NOT NULL,
                    TempatLahir TEXT,
                    TanggalLahir TEXT,
                    JenisKelamin TEXT,
                    Agama TEXT,
                    StatusPerkawinan TEXT,
                    Pekerjaan TEXT,
                    Alamat TEXT,
                    Pendidikan TEXT,
                    Kewarganegaraan TEXT,
                    Dusun TEXT,
                    RT TEXT,
                    RW TEXT,
                    Desa TEXT,
                    Kecamatan TEXT,
                    Kabupaten TEXT,
                    GolonganDarah TEXT,
                    NomorHP TEXT,
                    StatusWarga TEXT,
                    CreatedAt TEXT,
                    UpdatedAt TEXT
                );
            ", transaction);

                // Daftar kolom yang dipindahkan. Hanya kolom yang benar-benar ada
                // di tabel sumber yang ikut: berkas cadangan dari versi lama belum
                // punya kolom wilayah/lampiran, dan menyebutkannya di SELECT akan
                // membuat pemulihannya gagal.
                var kolomPindah = new[]
                {
                    "ID_Warga", "NIK", "Nama", "TempatLahir", "TanggalLahir",
                    "JenisKelamin", "Agama", "StatusPerkawinan", "Pekerjaan",
                    "Alamat", "Pendidikan", "Kewarganegaraan",
                    "Dusun", "RT", "RW", "Desa", "Kecamatan", "Kabupaten",
                    "GolonganDarah", "NomorHP", "StatusWarga", "CreatedAt", "UpdatedAt"
                }.Where(wargaColumns.Contains).ToList();

                // Pindahkan data. NIK dinormalkan lebih dulu (kosong -> NIK_<ROWID>)
                // karena kolom itu kini NOT NULL UNIQUE. Kolom lain disalin apa
                // adanya; NULL tetap sah dan tidak perlu dipaksa menjadi teks kosong.
                var sqlPindah = new System.Text.StringBuilder();
                sqlPindah.AppendLine("INSERT INTO Warga_New (" + string.Join(", ", kolomPindah) + ")");
                sqlPindah.AppendLine("SELECT");
                for (int i = 0; i < kolomPindah.Count; i++)
                {
                    if (i > 0) sqlPindah.Append(", ");
                    sqlPindah.Append(kolomPindah[i] == "NIK"
                        ? "CASE WHEN NIK IS NULL OR TRIM(NIK) = '' THEN 'NIK_' || ROWID ELSE NIK END"
                        : kolomPindah[i]);
                }
                sqlPindah.AppendLine();
                sqlPindah.AppendLine("FROM Warga_Backup;");

                await ExecuteNonQueryAsync(conn, sqlPindah.ToString(), transaction);

                // Hapus tabel lama dan ganti dengan yang baru
                await ExecuteNonQueryAsync(conn, @"
                DROP TABLE Warga;
                ALTER TABLE Warga_New RENAME TO Warga;
                DROP TABLE Warga_Backup;
            ", transaction);

                _logger.LogInformation(
                    "Tabel Warga dimigrasi dengan constraint NOT NULL dan UNIQUE pada NIK ({Jumlah} kolom dipindahkan).",
                    kolomPindah.Count);
            }

            var izinColumns = await GetTableColumnsAsync(conn, "IZIN", transaction);
            if (izinColumns.Contains("NIK"))
            {
                if (!izinColumns.Contains("ID_Warga_Anak"))
                {
                    await ExecuteNonQueryAsync(conn, "ALTER TABLE IZIN ADD COLUMN ID_Warga_Anak INTEGER;", transaction);
                    _logger.LogInformation("Kolom 'ID_Warga_Anak' ditambahkan ke tabel IZIN.");
                }
                await ExecuteNonQueryAsync(conn, @"
                    -- Perbaiki relasi yang hilang
                    UPDATE Surat 
                    SET ID_Warga = (
                        SELECT ID_Warga FROM Warga 
                        WHERE Warga.NIK = Surat.NomorSurat 
                        LIMIT 1
                    )
                    WHERE ID_Warga IS NULL AND NomorSurat IS NOT NULL;
                ", transaction);

                await ExecuteNonQueryAsync(conn, @"
                    INSERT OR IGNORE INTO Warga (
                        NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                        StatusPerkawinan, Pekerjaan, Alamat, Pendidikan, Kewarganegaraan
                    )
                    SELECT NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                           StatusPerkawinan, Pekerjaan, Alamat, NULL, NULL
                    FROM IZIN
                    WHERE NIK IS NOT NULL AND Nama IS NOT NULL;
                ", transaction);

                await ExecuteNonQueryAsync(conn, @"
                    UPDATE IZIN
                    SET ID_Warga_Anak = (
                        SELECT ID_Warga FROM Warga WHERE Warga.NIK = IZIN.NIK LIMIT 1
                    )
                    WHERE NIK IS NOT NULL;
                ", transaction);

                await ExecuteNonQueryAsync(conn, @"
                    UPDATE IZIN SET NegaraTujuan = 'Unknown' WHERE NegaraTujuan IS NULL;
                ", transaction);

                await ExecuteNonQueryAsync(conn, @"
                    CREATE TABLE IZIN_New (
                        ID_Surat INTEGER PRIMARY KEY,
                        ID_Warga_Anak INTEGER NOT NULL,
                        NegaraTujuan TEXT NOT NULL,
                        NamaPT TEXT,
                        FOREIGN KEY(ID_Surat) REFERENCES Surat(ID_Surat) ON DELETE CASCADE,
                        FOREIGN KEY(ID_Warga_Anak) REFERENCES Warga(ID_Warga) ON DELETE RESTRICT
                    );
                    INSERT INTO IZIN_New (ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT)
                    SELECT ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT
                    FROM IZIN
                    WHERE ID_Warga_Anak IS NOT NULL;
                    DROP TABLE IZIN;
                    ALTER TABLE IZIN_New RENAME TO IZIN;
                ", transaction);
                _logger.LogInformation("Tabel IZIN dimigrasi ke struktur baru dengan ID_Warga_Anak.");
            }

            await ExecuteNonQueryAsync(conn, @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis)
                VALUES (10, 'BEDANAMA', 'BEDANAMA');
            ", transaction);
            _logger.LogInformation("Jenis surat 'BEDANAMA' ditambahkan.");

            await ExecuteNonQueryAsync(conn, "PRAGMA journal_mode = WAL;", transaction);
            _logger.LogInformation("Mode WAL diaktifkan.");

            _logger.LogInformation("Migrasi versi 1 selesai.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menerapkan migrasi versi 1.");
            throw new InvalidOperationException("Gagal menerapkan migrasi versi 1.", ex);
        }
    }

    private async Task<int> GetSchemaVersionAsync(SqliteConnection conn, SqliteTransaction? transaction = null)
    {
        try
        {
            await ExecuteNonQueryAsync(conn, @"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    Version INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO SchemaVersion (Version) VALUES (0);", transaction);
            using var cmd = new SqliteCommand("SELECT Version FROM SchemaVersion LIMIT 1", conn, transaction);
            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mendapatkan versi skema.");
            return 0;
        }
    }

    private async Task UpdateSchemaVersionAsync(SqliteConnection conn, int version, SqliteTransaction? transaction = null)
    {
        using var cmd = new SqliteCommand("UPDATE SchemaVersion SET Version = @version", conn, transaction);
        cmd.Parameters.AddWithValue("@version", version);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Versi skema diperbarui ke: {Version}", version);
    }

    private async Task<HashSet<string>> GetTableColumnsAsync(SqliteConnection conn, string tableName, SqliteTransaction? transaction = null)
    {
        var columns = new HashSet<string>();
        using var cmd = new SqliteCommand($"PRAGMA table_info({tableName});", conn, transaction);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private async Task<bool> HasUniqueConstraintAsync(SqliteConnection conn, string tableName, string columnName, SqliteTransaction? transaction = null)
    {
        using var cmd = new SqliteCommand($"PRAGMA index_list({tableName});", conn, transaction);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(1);
            if (reader.GetInt32(2) == 1)
            {
                using var cmdIndex = new SqliteCommand($"PRAGMA index_info({indexName});", conn, transaction);
                using var indexReader = await cmdIndex.ExecuteReaderAsync();
                while (await indexReader.ReadAsync())
                {
                    var colName = await GetColumnNameByIndexAsync(conn, tableName, indexReader.GetInt32(1), transaction!);
                    if (colName == columnName)
                        return true;
                }
            }
        }
        return false;
    }

    private async Task<string> GetColumnNameByIndexAsync(SqliteConnection conn, string tableName, int columnIndex, SqliteTransaction? transaction = null)
    {
        using var cmd = new SqliteCommand($"PRAGMA table_info({tableName});", conn, transaction);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt32(0) == columnIndex)
                return reader.GetString(1);
        }
        return string.Empty;
    }

    private async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql, SqliteTransaction? transaction = null)
    {
        using var cmd = new SqliteCommand(sql, conn, transaction);
        await cmd.ExecuteNonQueryAsync();
    }

    public string GetDatabasePath()
    {
        // Connection string bisa memuat parameter lain (mis. Password= SQLCipher),
        // jadi jalurnya dibaca lewat builder — bukan dengan membuang teks
        // "Data Source=" yang akan menyisakan "...;Password=..." sebagai "jalur".
        var path = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString)
            .DataSource.Trim();
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }
        return Path.GetFullPath(path);
    }

    private async Task<bool> IsValidSQLiteDatabaseAsync(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            _logger.LogError("File database tidak ditemukan untuk validasi: {Path}", dbPath);
            return false;
        }

        try
        {
            // Berkas kandidat boleh plaintext (ekspor lama) atau sudah terenkripsi
            // (cadangan dari mesin ini); DeteksiKoneksi memilih bentuk yang terbaca.
            var connectionString = EnkripsiDatabase.DeteksiKoneksi(dbPath)
                ?? throw new InvalidOperationException(
                    "Database tidak bisa dibuka: berkasnya rusak atau terenkripsi dengan kunci mesin lain.");

            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='Warga'", conn);
            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result.ToString() != "Warga")
            {
                _logger.LogError("Database tidak mengandung tabel 'Warga' yang diperlukan: {Path}", dbPath);
                return false;
            }
            using var cmdIntegrity = new SqliteCommand("PRAGMA integrity_check", conn);
            var integrityResult = await cmdIntegrity.ExecuteScalarAsync();
            if (integrityResult?.ToString() != "ok")
            {
                _logger.LogError("Pemeriksaan integritas database gagal: {Path}. Hasil: {Result}", dbPath, integrityResult);
                return false;
            }
            _logger.LogInformation("Database SQLite valid dan mengandung tabel 'Warga': {Path}", dbPath);
            return true;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Terjadi kesalahan SqliteException saat memvalidasi database: {Path}", dbPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terjadi kesalahan umum saat memvalidasi database: {Path}", dbPath);
            return false;
        }
    }

    private async Task<bool> IsOldIzinStructureAsync(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("PRAGMA table_info(IZIN);", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns.Contains("NIK") && columns.Contains("Nama");
    }
}
}
