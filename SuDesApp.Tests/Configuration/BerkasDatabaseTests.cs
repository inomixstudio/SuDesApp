using Microsoft.Data.Sqlite;
using SuDesApp.Configuration;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace SuDesApp.Tests.Configuration
{
    /// <summary>
    /// Uji di kelas ini mengubah sumber kunci database proses-wide, jadi kelas uji
    /// lain yang juga mengubahnya harus berada dalam koleksi yang sama agar tidak
    /// berjalan bersamaan.
    /// </summary>
    [CollectionDefinition("BerkasDatabase", DisableParallelization = true)]
    public sealed class BerkasDatabaseCollection
    {
    }

    /// <summary>
    /// Pemeriksaan berkas database yang dijalankan saat aplikasi dibuka sebelum
    /// koneksi pertama. Keputusan di sini menentukan apakah aplikasi bisa sama
    /// sekali, jadi pesan galatnya harus jelas dan berkas yang valid tidak boleh
    /// pernah ditolak — baik database SQLite biasa maupun database terenkripsi
    /// dari versi lama yang harus tetap bisa dibuka setelah pembaruan.
    /// </summary>
    [Collection("BerkasDatabase")]
    public class BerkasDatabaseTests : IDisposable
    {
        private const string KunciUji = "kunci-database-uji";

        private readonly string _folder;
        private string? _kunci;

        public BerkasDatabaseTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "sudes-uji-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);

            // Kunci disuntikkan supaya uji tidak bergantung pada berkas kunci DPAPI
            // akun Windows yang menjalankan tes — dan tidak menyentuh berkas asli
            // milik pengguna.
            BerkasDatabase.MuatKunci = () => _kunci;
        }

        public void Dispose()
        {
            BerkasDatabase.MuatKunci = KunciDatabase.Muat;
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
            catch (IOException) { /* berkas mungkin masih dipegang proses lain — uji tidak peduli */ }
        }

        private string Berkas(string nama) => Path.Combine(_folder, nama);

        [Fact]
        public void Berkas_yang_dipakai_eksklusif_dilaporkan_terkunci_bukan_terenkripsi()
        {
            var jalur = BuatDatabaseValid("terkunci.db");
            SqliteConnection.ClearAllPools();

            using var kunci = new FileStream(
                jalur, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Assert.Equal(StatusBerkasDatabase.Terkunci, BerkasDatabase.Periksa(jalur));

            var galat = Assert.Throws<IOException>(() =>
                BerkasDatabase.PastikanTerbaca($"Data Source={jalur}"));
            Assert.Contains("sedang dipakai", galat.Message);
        }

        /// <summary>Buat database SQLite yang benar-benar valid dan bisa dibuka.</summary>
        private string BuatDatabaseValid(string nama)
        {
            var jalur = Berkas(nama);
            using (var conn = new SqliteConnection($"Data Source={jalur}"))
            {
                conn.Open();
                using var cmd = new SqliteCommand("CREATE TABLE Warga (ID INTEGER PRIMARY KEY);", conn);
                cmd.ExecuteNonQuery();
            }
            return jalur;
        }

        /// <summary>Buat database terenkripsi SQLCipher — meniru database versi lama.</summary>
        private string BuatDatabaseTerenkripsi(string nama, string kunci)
        {
            var jalur = Berkas(nama);
            using (var conn = new SqliteConnection(Koneksi(jalur, kunci)))
            {
                conn.Open();
                using var cmd = new SqliteCommand(
                    "CREATE TABLE Warga (ID INTEGER PRIMARY KEY, Nama TEXT);" +
                    "INSERT INTO Warga (Nama) VALUES ('isi-terenkripsi');", conn);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            return jalur;
        }

        /// <summary>Berkas yang bukan SQLite — meniru database rusak atau bentuk lain.</summary>
        private string BuatBerkasNonSqlite(string nama)
        {
            var jalur = Berkas(nama);
            // Cukup panjang supaya pemeriksaan header membaca 16 byte penuh,
            // dan sengaja bukan magic "SQLite format 3".
            File.WriteAllBytes(jalur, Encoding.ASCII.GetBytes("SQLite format 4\0-bukan-sqlite"));
            return jalur;
        }

        /// <summary>
        /// Connection string uji. Tanpa kunci, Password TIDAK ditulis sama sekali —
        /// "Password=" kosong adalah kunci kosong bagi SQLCipher, bukan plaintext.
        /// </summary>
        private static string Koneksi(string jalur, string? kunci = null)
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = jalur };
            if (!string.IsNullOrWhiteSpace(kunci)) builder.Password = kunci;
            return builder.ToString();
        }

        private static string BacaNama(string jalur, string? kunci = null)
        {
            using var conn = new SqliteConnection(Koneksi(jalur, kunci));
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Nama FROM Warga LIMIT 1;", conn);
            return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        }

        private static bool HeaderPolos(string jalur)
        {
            var header = Encoding.ASCII.GetBytes("SQLite format 3\0");
            // Koneksi yang masih terbuka/pool adalah keadaan normal saat header
            // diperiksa; mode berbagi membuat pemeriksaan tidak gagal karena itu.
            using var fs = new FileStream(
                jalur, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var isi = new byte[fs.Length];
            fs.ReadExactly(isi);
            return isi.Length >= header.Length && isi.AsSpan(0, header.Length).SequenceEqual(header);
        }

        // =====================================================================
        // Berkas belum ada / SQLite biasa
        // =====================================================================

        [Fact]
        public void Periksa_PadaBerkasBelumAda_MengembalikanBelumAda()
        {
            Assert.Equal(StatusBerkasDatabase.BelumAda, BerkasDatabase.Periksa(Berkas("tidak-ada.db")));
        }

        [Fact]
        public void Periksa_PadaDatabaseSqliteValid_MengembalikanSiap()
        {
            var jalur = BuatDatabaseValid("valid.db");

            Assert.Equal(StatusBerkasDatabase.Siap, BerkasDatabase.Periksa(jalur));
        }

        [Fact]
        public void Periksa_PadaBerkasKosong_MengembalikanSiapTanpaMengubahBerkas()
        {
            var jalur = Berkas("kosong.db");
            File.WriteAllBytes(jalur, Array.Empty<byte>());

            Assert.Equal(StatusBerkasDatabase.Siap, BerkasDatabase.Periksa(jalur));
            Assert.Empty(File.ReadAllBytes(jalur));
            File.Delete(jalur); // Pemeriksaan tidak boleh menahan handle pada Windows.
            Assert.False(File.Exists(jalur));
        }

        [Fact]
        public void Periksa_PadaBerkasBukanSqlite_MengembalikanBerkasLamaTerkunci()
        {
            var jalur = BuatBerkasNonSqlite("lama-terkunci.db");

            Assert.Equal(StatusBerkasDatabase.BerkasLamaTerkunci, BerkasDatabase.Periksa(jalur));
        }

        [Fact]
        public void DeteksiKoneksi_PadaDatabaseValid_MengembalikanKoneksiTanpaKunci()
        {
            var jalur = BuatDatabaseValid("valid.db");

            var koneksi = BerkasDatabase.DeteksiKoneksi(jalur);

            Assert.NotNull(koneksi);
            Assert.DoesNotContain("Password", koneksi, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeteksiKoneksi_PadaBerkasBukanSqlite_MengembalikanNull()
        {
            var jalur = BuatBerkasNonSqlite("lama-terkunci.db");

            Assert.Null(BerkasDatabase.DeteksiKoneksi(jalur));
        }

        [Fact]
        public void PastikanTerbaca_PadaDatabaseValid_MengembalikanKoneksiYangSama()
        {
            var jalur = BuatDatabaseValid("valid.db");

            var hasil = BerkasDatabase.PastikanTerbaca($"Data Source={jalur}");

            Assert.Contains(Path.GetFullPath(jalur), hasil, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", hasil, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PastikanTerbaca_PadaBerkasBelumAda_DilewatiTanpaGalat()
        {
            // Berkas baru belum ada: yang membuatnya adalah inisialisasi database,
            // bukan pemeriksaan ini — jadi tidak boleh ditolak di sini, dan database
            // baru tetap dibuat sebagai SQLite biasa tanpa kunci.
            var hasil = BerkasDatabase.PastikanTerbaca($"Data Source={Berkas("baru.db")}");

            Assert.Contains("baru.db", hasil, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", hasil, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PastikanTerbaca_PadaBerkasKosong_MengembalikanKoneksiTanpaMengubahBerkas()
        {
            var jalur = Berkas("kosong.db");
            File.WriteAllBytes(jalur, Array.Empty<byte>());

            var hasil = BerkasDatabase.PastikanTerbaca($"Data Source={jalur}");

            Assert.Contains(Path.GetFullPath(jalur), hasil, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(File.ReadAllBytes(jalur));
            File.Delete(jalur); // Pemeriksaan tidak boleh menahan handle pada Windows.
            Assert.False(File.Exists(jalur));
        }

        [Fact]
        public void PastikanTerbaca_PadaBerkasBukanSqlite_MelemparGalatYangDapatDitindaklanjuti()
        {
            var jalur = BuatBerkasNonSqlite("lama-terkunci.db");

            var galat = Assert.Throws<InvalidOperationException>(
                () => BerkasDatabase.PastikanTerbaca($"Data Source={jalur}"));

            // Pengguna harus tahu ini database versi lama, bukan sekadar "rusak".
            Assert.Contains("versi SuDesApp lama", galat.Message, StringComparison.Ordinal);
            Assert.Contains(jalur, galat.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DesaKunciDb.bin", galat.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PastikanTerbaca_PadaDatabaseRusak_MelemparGalat()
        {
            // Header SQLite benar tetapi isinya bukan database — kasus berkas korup.
            var jalur = Berkas("rusak.db");
            var header = Encoding.ASCII.GetBytes("SQLite format 3\0");
            File.WriteAllBytes(jalur, header);

            var galat = Assert.Throws<InvalidOperationException>(
                () => BerkasDatabase.PastikanTerbaca($"Data Source={jalur}"));

            Assert.Contains("rusak", galat.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Database terenkripsi versi lama (SQLCipher)
        // =====================================================================

        [Fact]
        public void Periksa_PadaDatabaseTerenkripsiDenganKunci_MengembalikanTerenkripsi()
        {
            _kunci = KunciUji;
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);

            Assert.False(HeaderPolos(jalur)); // benar-benar terenkripsi, bukan plaintext
            Assert.Equal(StatusBerkasDatabase.Terenkripsi, BerkasDatabase.Periksa(jalur));
        }

        [Fact]
        public void Periksa_PadaDatabaseTerenkripsiTanpaKunci_MengembalikanBerkasLamaTerkunci()
        {
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);
            _kunci = null;

            Assert.Equal(StatusBerkasDatabase.BerkasLamaTerkunci, BerkasDatabase.Periksa(jalur));
        }

        [Fact]
        public void DeteksiKoneksi_PadaDatabaseTerenkripsi_MengembalikanKoneksiBerKunci()
        {
            _kunci = KunciUji;
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);

            var koneksi = BerkasDatabase.DeteksiKoneksi(jalur);

            Assert.NotNull(koneksi);
            Assert.True(BerkasDatabase.Terkunci(koneksi!));
            Assert.Equal("isi-terenkripsi", BacaNama(jalur, KunciUji));
        }

        [Fact]
        public void PastikanTerbaca_PadaDatabaseTerenkripsi_MembukaDenganKunci()
        {
            _kunci = KunciUji;
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);

            var hasil = BerkasDatabase.PastikanTerbaca($"Data Source={jalur}");

            Assert.True(BerkasDatabase.Terkunci(hasil));

            using var conn = new SqliteConnection(hasil);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Nama FROM Warga;", conn);
            Assert.Equal("isi-terenkripsi", Convert.ToString(cmd.ExecuteScalar()));

            // Pembukaan TIDAK boleh mengubah database menjadi plaintext.
            SqliteConnection.ClearAllPools();
            Assert.False(HeaderPolos(jalur));
            Assert.Equal(StatusBerkasDatabase.Terenkripsi, BerkasDatabase.Periksa(jalur));
        }

        [Fact]
        public void PastikanTerbaca_PadaDatabaseTerenkripsiTanpaKunci_MelemparGalatBerkasKunci()
        {
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);
            _kunci = null;

            var galat = Assert.Throws<InvalidOperationException>(
                () => BerkasDatabase.PastikanTerbaca($"Data Source={jalur}"));

            Assert.Contains("terenkripsi", galat.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DesaKunciDb.bin", galat.Message, StringComparison.Ordinal);
            Assert.Contains(jalur, galat.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PastikanTerbaca_PadaDatabaseTerenkripsiKunciBeda_MelemparGalatKunciTidakCocok()
        {
            var jalur = BuatDatabaseTerenkripsi("terenkripsi.db", KunciUji);
            _kunci = "kunci-dari-komputer-lain";

            var galat = Assert.Throws<InvalidOperationException>(
                () => BerkasDatabase.PastikanTerbaca($"Data Source={jalur}"));

            Assert.Contains("tidak cocok", galat.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Salin isi lintas bentuk (dipakai pemulihan cadangan & impor)
        // =====================================================================

        [Fact]
        public void SalinIsi_DariTerenkripsiKePlaintext_MendekripsiIsinya()
        {
            var sumber = BuatDatabaseTerenkripsi("sumber.db", KunciUji);
            var tujuan = Berkas("hasil.db");

            BerkasDatabase.SalinIsi(Koneksi(sumber, KunciUji), tujuan, kunciTujuan: null);

            Assert.True(HeaderPolos(tujuan));
            Assert.Equal("isi-terenkripsi", BacaNama(tujuan));
        }

        [Fact]
        public void SalinIsi_DariPlaintextKeTerenkripsi_MengenkripsiIsinya()
        {
            var sumber = Berkas("sumber.db");
            using (var conn = new SqliteConnection(Koneksi(sumber)))
            {
                conn.Open();
                using var cmd = new SqliteCommand(
                    "CREATE TABLE Warga (ID INTEGER PRIMARY KEY, Nama TEXT);" +
                    "INSERT INTO Warga (Nama) VALUES ('isi-polosan');", conn);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var tujuan = Berkas("hasil.db");
            BerkasDatabase.SalinIsi(Koneksi(sumber), tujuan, KunciUji);

            Assert.False(HeaderPolos(tujuan));
            Assert.Equal("isi-polosan", BacaNama(tujuan, KunciUji));
        }

        [Fact]
        public void SalinIsi_DariPlaintextKePlaintext_MenyalinIsinya()
        {
            var sumber = Berkas("sumber.db");
            using (var conn = new SqliteConnection(Koneksi(sumber)))
            {
                conn.Open();
                using var cmd = new SqliteCommand(
                    "CREATE TABLE Warga (ID INTEGER PRIMARY KEY, Nama TEXT);" +
                    "INSERT INTO Warga (Nama) VALUES ('isi-polosan');", conn);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var tujuan = Berkas("hasil.db");
            BerkasDatabase.SalinIsi(Koneksi(sumber), tujuan, kunciTujuan: null);

            Assert.True(HeaderPolos(tujuan));
            Assert.Equal("isi-polosan", BacaNama(tujuan));
        }
    }
}
