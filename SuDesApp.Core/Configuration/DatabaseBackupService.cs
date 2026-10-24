using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Snapshot dan pemulihan database desa.
    ///
    /// Dipisahkan dari <see cref="DatabaseInitializer"/> karena keduanya punya
    /// alasan berubah yang berbeda: initializer menjawab "skema sudah benar?",
    /// kelas ini menjawab "datanya selamat?". Kelas yang menjawab dua pertanyaan
    /// sekaligus akan kehilangan keduanya — dan memang, back up database WAL
    /// dengan File.Copy tersembunyi di tengah 1.500 baris DDL sampai ketahuan
    /// lewat penelusuran, bukan lewat desain.
    ///
    /// Yang dikunci di sini bukan cuma pemindahan kode, melainkan properti yang
    /// sebelumnya tak teruji: backup harus konsisten dengan WAL.
    /// </summary>
    public interface IDatabaseBackupService
    {
        Task<Dictionary<string, object>> GetStatsAsync();
        Task BuatBackupAsync(string backupPath);
        Task<string?> PulihkanBackupAsync(string backupPath);

        /// <summary>Buang cadangan pra-restore setelah pemakai memastikan salinan bisa dibuka.</summary>
        void BuangCadanganPraRestore(string cadanganPath);
    }

    public class DatabaseBackupService : IDatabaseBackupService
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseBackupService> _logger;

        // SENGAJA tidak bergantung pada IDatabaseInitializer. Kebutuhan "setelah
        // restore, jalankan InitializeAsync lagi" akan membuat siklus DI
        // (Initializer -> BackupService -> Initializer) yang mustahil diselesaikan
        // sekali pakai. Sebagai gantinya pemanggil — yang memang sudah memegang
        // initializer — yang menjalankan re-init, lalu memanggil
        // BuangCadanganPraRestore bila berhasil.
        public DatabaseBackupService(
            AppConfig config,
            ILogger<DatabaseBackupService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ? DATABASE STATISTICS
        public async Task<Dictionary<string, object>> GetStatsAsync()
        {
            var stats = new Dictionary<string, object>();

            try
            {
                await using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();

                stats["TotalSurat"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
                stats["TotalWarga"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");
                stats["TotalJenisSurat"] = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM JenisSurat");

                var statusCounts = await connection.QueryAsync<(string Status, int Count)>(
                    "SELECT COALESCE(Status, 'Unknown') as Status, COUNT(*) as Count FROM Surat GROUP BY Status");
                stats["SuratByStatus"] = statusCounts.ToDictionary(x => x.Status, x => x.Count);

                stats["RecentSurat"] = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Surat WHERE CreatedAt >= date('now', '-7 days')");

                var dbPath = connection.DataSource;
                if (File.Exists(dbPath))
                {
                    stats["DatabaseSizeBytes"] = new FileInfo(dbPath).Length;
                }

                _logger.LogInformation("Statistik database berhasil diambil");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil statistik database");
                // Sengaja tetap mengembalikan kamus berisi pesan galat: halaman
                // Pengaturan menampilkannya apa adanya. Alternatifnya melempar,
                // dan satu tabel hilang akan mengosongkan halaman tanpa
                // menjelaskan apa pun.
                stats["Error"] = ex.Message;
            }

            return stats;
        }

        // ? CREATE BACKUP
        public async Task BuatBackupAsync(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                throw new ArgumentException("Jalur backup tidak boleh kosong.", nameof(backupPath));

            var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;

            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException($"Berkas database tidak ditemukan: {dbPath}");
            }

            var backupDir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            // Snapshot konsisten lewat VACUUM INTO, BUKAN File.Copy.
            //
            // Database berjalan pada journal_mode=WAL, jadi transaksi yang masih
            // ada di desa.db-wal belum ikut terekam di desa.db. File.Copy hanya
            // menyalin berkas utama: backup diam-diam kehilangan surat terakhir,
            // dan hasil restore-nya korup.
            //
            // Backup ini juga dipakai sebagai jaring pengaman sebelum penghapusan
            // data N7 yang destruktif, jadi kekurangannya paling berbahaya justru
            // saat paling dibutuhkan.
            //
            // Catatan penting: VACUUM INTO TIDAK hiclip ke berkas yang sudah ada.
            // Kalau file tujuan sudah ada (misal jalur tambalan yang dipakai
            // ulang), perintah gagal — jadi sengaja dibuang lebih dulu.
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            await using (var connection = new SqliteConnection(_config.DatabaseConnectionString))
            {
                await connection.OpenAsync();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "VACUUM INTO @tujuan";
                cmd.Parameters.AddWithValue("@tujuan", backupPath);
                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Backup database dibuat: {BackupPath}", backupPath);
        }

        // ? RESTORE BACKUP
        /// <returns>Jalur cadangan pra-restore, atau null bila tidak ada database
        /// aktif untuk dicadangkan. Pemanggil wajib meneruskannya ke
        /// <see cref="BuangCadanganPraRestore"/> setelah memastikan database
        /// hasil restore bisa dibuka.</returns>
        public async Task<string?> PulihkanBackupAsync(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                throw new ArgumentException("Jalur backup tidak boleh kosong.", nameof(backupPath));

            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Berkas backup tidak ditemukan: {backupPath}");
            }

            var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;

            // Salin database yang SEDANG DIPAKAI ke tempat aman DULU.
            //
            // Sebelumnya File.Copy(backupPath, dbPath, overwrite: true) langsung
            // menghancurkan database aktif: kalau salinan ternyata rusak atau
            // berasal dari mesin dengan kunci enkripsi berbeda (DPAPI terikat
            // per-user), pengguna kehilangan semuanya dalam satu langkah tanpa
            // jalan pulih.
            //
            // Koneksi terbuka TIDAK ditutup di sini. ClearAllPools() hanya
            // membuang pooling; koneksi yang masih hidup di scope DI milik
            // siapapun dan menutupnya berarti merusak kode lain diam-diam.
            // Kalau isinya terkunci, File.Copy akan gagal — dan itu memang
            // hasil yang benar: gagal terang lebih baik daripada menimpa
            // sebagian jadi sebagian tidak.
            var cadangan = string.Concat(dbPath, ".sebelum-restore-", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (File.Exists(dbPath))
            {
                File.Copy(dbPath, cadangan, overwrite: false);
                _logger.LogWarning(
                    "Database aktif disalin ke {Cadangan} sebelum restore. Hapus bila restore berhasil.",
                    cadangan);
            }

            File.Copy(backupPath, dbPath, overwrite: true);

            _logger.LogInformation(
                "Database dipulihkan dari backup: {BackupPath}. Cadangan pra-restore: {Cadangan}",
                backupPath, cadangan);

            return cadangan;
        }

        /// <summary>
        /// Buang cadangan pra-restore yang dibuat oleh
        /// <see cref="PulihkanBackupAsync"/>. Dipisahkan supaya pemanggil yang
        /// tahu database hasil restore bisa dibuka (karena ia yang menjalankan
        /// InitializeAsync) yang memutuskan hapus atau tidak — kelas ini tidak
        /// punya cara memastikannya sendiri.
        ///
        /// Sengaja tidak melempar bila gagal hapus: berkas yang tertinggal hanya
        /// menempati disk, sedangkan restore sudah berhasil.
        /// </summary>
        public void BuangCadanganPraRestore(string cadanganPath)
        {
            if (string.IsNullOrWhiteSpace(cadanganPath) || !File.Exists(cadanganPath)) return;

            try
            {
                File.Delete(cadanganPath);
                _logger.LogInformation("Cadangan pra-restore dibuang: {Cadangan}", cadanganPath);
            }
            catch (Exception ex)
            {
                // Meninggalkan berkas bukan kegagalan operasi restore; yang gagal
                // hanyalah "bersih-bersih".
                _logger.LogWarning(ex, "Cadangan pra-restore GAGAL dibuang; dibiarkan di {Cadangan}", cadanganPath);
            }
        }
    }
}