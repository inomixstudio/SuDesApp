using Microsoft.Data.Sqlite;
using SuDesApp.Configuration;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace SuDesApp.Tests.Configuration
{
    /// <summary>
    /// Pemeriksaan berkas database yang dijalankan saat aplikasi dibuka sebelum
    /// koneksi pertama. Keputusan di sini menentukan apakah aplikasi bisa sama
    /// sekali, jadi pesan galatnya harus jelas dan berkas yang valid tidak boleh
    /// pernah ditolak — terutama sejak database disimpan sebagai SQLite biasa
    /// tanpa kunci.
    /// </summary>
    public class BerkasDatabaseTests : IDisposable
    {
        private readonly string _folder;

        public BerkasDatabaseTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "sudes-uji-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
            catch (IOException) { /* berkas mungkin masih dipegang proses lain — uji tidak peduli */ }
        }

        private string Berkas(string nama) => Path.Combine(_folder, nama);

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

        /// <summary>Berkas yang bukan SQLite — meniru database versi lama terenkripsi.</summary>
        private string BuatBerkasNonSqlite(string nama)
        {
            var jalur = Berkas(nama);
            // Cukup panjang supaya pemeriksaan header membaca 16 byte penuh,
            // dan sengaja bukan magic "SQLite format 3".
            File.WriteAllBytes(jalur, Encoding.ASCII.GetBytes("SQLite format 4\0-bukan-sqlite"));
            return jalur;
        }

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
            // bukan pemeriksaan ini — jadi tidak boleh ditolak di sini.
            var hasil = BerkasDatabase.PastikanTerbaca($"Data Source={Berkas("baru.db")}");

            Assert.Contains("baru.db", hasil, StringComparison.OrdinalIgnoreCase);
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
    }
}
