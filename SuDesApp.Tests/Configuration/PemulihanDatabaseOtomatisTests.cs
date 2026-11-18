using Microsoft.Data.Sqlite;
using SuDesApp.Configuration;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace SuDesApp.Tests.Configuration
{
    [Collection("BerkasDatabase")]
    public class PemulihanDatabaseOtomatisTests : IDisposable
    {
        private readonly string _folder;

        public PemulihanDatabaseOtomatisTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "sudes-pulih-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
            catch (IOException) { /* berkas masih dipegang proses lain; uji tidak peduli */ }
        }

        private string Berkas(string nama) => Path.Combine(_folder, nama);

        [Fact]
        public void Database_rusak_dipulihkan_dari_database_lama_yang_sehat()
        {
            var lama = BuatDatabase("lama.db", "Desa Sumberjaya");
            var aktif = Berkas("desa.db");
            File.WriteAllBytes(aktif, Encoding.ASCII.GetBytes("bukan-database-sqlite"));

            var hasil = PemulihanDatabaseOtomatis.Pulihkan(
                aktif, $"Data Source={aktif}", new[] { lama }, sertakanKandidatMesin: false);

            Assert.True(hasil.Berhasil);
            Assert.False(hasil.DatabaseBaru);
            Assert.Equal(lama, hasil.Sumber);
            Assert.NotNull(hasil.Cadangan);
            Assert.True(File.Exists(hasil.Cadangan));
            Assert.Contains("Desa Sumberjaya", BacaNama(aktif));
        }

        [Fact]
        public void Tanpa_kandidat_sehat_database_lama_dipindahkan_dan_aplikasi_bisa_membuat_baru()
        {
            var aktif = Berkas("desa.db");
            var isiLama = Encoding.ASCII.GetBytes("database-lama-rusak");
            File.WriteAllBytes(aktif, isiLama);

            var hasil = PemulihanDatabaseOtomatis.Pulihkan(
                aktif, $"Data Source={aktif}", sertakanKandidatMesin: false);

            Assert.True(hasil.Berhasil);
            Assert.True(hasil.DatabaseBaru);
            Assert.False(File.Exists(aktif));
            Assert.NotNull(hasil.Cadangan);
            Assert.Equal(isiLama, File.ReadAllBytes(hasil.Cadangan!));
        }

        [Fact]
        public void Database_yang_terkunci_tidak_dipindahkan()
        {
            var aktif = BuatDatabase("desa.db", "Data Terkunci");
            SqliteConnection.ClearAllPools();

            using var kunci = new FileStream(
                aktif, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var hasil = PemulihanDatabaseOtomatis.Pulihkan(
                aktif, $"Data Source={aktif}");

            Assert.False(hasil.Berhasil);
            Assert.True(File.Exists(aktif));
            Assert.Contains("dipakai proses lain", hasil.Pesan);
        }

        [Fact]
        public void Produksi_tidak_menonaktifkan_kandidat_dari_folder_instalasi()
        {
            // Justru database di folder instalasi lama yang harus ditemukan lagi
            // saat database aktif rusak; kalau produksi ikut menutup kandidat itu,
            // perbaikan 'instalasi baru kehilangan data lama' diam-diam batal.
            var jalur = Path.Combine(CariAkarRepo(), "SuDesApp.Wpf", "App.Startup.cs");
            var sumber = File.ReadAllText(jalur);
            var i = sumber.IndexOf("PemulihanDatabaseOtomatis.Pulihkan(", StringComparison.Ordinal);
            Assert.True(i >= 0, "panggilan pemulihan tidak ditemukan di App.Startup.cs");
            Assert.DoesNotContain("sertakanKandidatMesin: false",
                sumber.Substring(i, Math.Min(600, sumber.Length - i)));
        }

        private static string CariAkarRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Akar repo tidak ditemukan.");
        }

        private string BuatDatabase(string nama, string isi)
        {
            var jalur = Berkas(nama);
            using (var conn = new SqliteConnection($"Data Source={jalur}"))
            {
                conn.Open();
                using var cmd = new SqliteCommand(
                    "CREATE TABLE Warga (ID INTEGER PRIMARY KEY, Nama TEXT);" +
                    "INSERT INTO Warga (Nama) VALUES ($nama);", conn);
                cmd.Parameters.AddWithValue("$nama", isi);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            return jalur;
        }

        private static string BacaNama(string jalur)
        {
            using var conn = new SqliteConnection($"Data Source={jalur}");
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Nama FROM Warga LIMIT 1;", conn);
            return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        }
    }
}
