using Microsoft.Data.Sqlite;
using SuDesApp.Configuration;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Lapisan C — SQLCipher: kunci DPAPI (KunciDatabase) dan migrasi enkripsi
    /// otomatis (EnkripsiDatabase: deteksi plaintext → cadangan → sqlcipher_export).
    /// Semua tes memakai berkas sementara di folder
    /// uji sendiri; LokasiOverride kunci WAJIB dipasang lebih dulu supaya kunci
    /// asli milik user (%LOCALAPPDATA%\SuDesApp) tidak pernah tersentuh —
    /// kunci itu satu-satunya pembuka database nyata dan tidak boleh ditimpa
    /// atau dihapus oleh tes.
    /// </summary>
    public sealed class EnkripsiDatabaseTests
    {
        /// <summary>
        /// Pasang LokasiOverride kunci ke folder sementara milik tes, hapus
        /// foldernya, dan kembalikan override ke null saat dispose — pola yang
        /// sama dengan ApiKunci.LokasiOverride pada KeamananLapisanDataTests.
        /// </summary>
        private sealed class KunciSementara : IDisposable
        {
            public string Folder { get; } =
                Path.Combine(Path.GetTempPath(), "uji-enkripsi-" + Guid.NewGuid().ToString("N"));

            public KunciSementara()
            {
                Directory.CreateDirectory(Folder);
                KunciDatabase.LokasiOverride = () => Path.Combine(Folder, "DesaKunciDb.bin");
            }

            public void Dispose()
            {
                KunciDatabase.LokasiOverride = null;
                try { Directory.Delete(Folder, recursive: true); }
                catch { /* sementara — biarkan OS membersihkan */ }
            }
        }

        /// <summary>Berkas DB uji di folder tes; dihapus bersama KunciSementara.</summary>
        private static string BuatJalurDb(string folder) =>
            Path.Combine(folder, "desa-uji.db");

        private static void BuatDbPlaintext(string path, string isiData)
        {
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE Uji (Isi TEXT);" +
                    "INSERT INTO Uji (Isi) VALUES ($isi);";
                cmd.Parameters.AddWithValue("$isi", isiData);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
        }

        private static string BacaIsi(string connectionString)
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Isi FROM Uji;";
            return (string?)cmd.ExecuteScalar() ?? string.Empty;
        }

        [Fact]
        public void KunciDatabase_MuatAtauBuat_MembuatSekaliLaluMemuatUlang()
        {
            using var sementara = new KunciSementara();

            Assert.Null(KunciDatabase.Muat());
            var kunci = KunciDatabase.MuatAtauBuat();
            Assert.False(string.IsNullOrWhiteSpace(kunci));
            Assert.True(KunciDatabase.Ada);
            Assert.Equal(kunci, KunciDatabase.Muat());
            Assert.Equal(kunci, KunciDatabase.MuatAtauBuat()); // idempoten, tidak mengganti kunci
        }

        [Fact]
        public void KunciDatabase_BerkasTidakTerbaca_MenolakBuatKunciBaru()
        {
            using var sementara = new KunciSementara();

            // Berkas ada tapi bukan blob DPAPI yang valid (mis. korup / hasil
            // salinan dari mesin lain): membuat kunci baru diam-diam akan menyegel
            // database lama selamanya, jadi harus galat jelas.
            File.WriteAllBytes(
                Path.Combine(sementara.Folder, "DesaKunciDb.bin"),
                new byte[] { 1, 2, 3, 4, 5 });

            Assert.Null(KunciDatabase.Muat());
            Assert.Throws<InvalidOperationException>(() => KunciDatabase.MuatAtauBuat());
        }

        [Fact]
        public void JaminTerkunci_KoneksiMemori_TanpaKunci()
        {
            using var sementara = new KunciSementara();

            var hasil = EnkripsiDatabase.JaminTerkunci("Data Source=:memory:");

            Assert.True(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(hasil).Password));
        }

        [Fact]
        public void JaminTerkunci_DBPlaintext_DirekeyTerbacaDanDicadangkan()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            BuatDbPlaintext(jalur, "data lama");

            var hasil = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");

            // Kunci terpasang dan berkas benar-benar bukan plaintext lagi.
            Assert.False(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(hasil).Password));
            var header = new byte[16];
            using (var fs = File.OpenRead(jalur))
                Assert.Equal(16, fs.Read(header, 0, 16));
            Assert.NotEqual("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));

            // Cadangan plaintext dibuat sebelum rekey.
            var cadangan = Directory.GetFiles(sementara.Folder, "desa-plaintext-*.bak");
            Assert.Single(cadangan);
            using (var conn = new SqliteConnection($"Data Source={cadangan[0]}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Isi FROM Uji;";
                Assert.Equal("data lama", cmd.ExecuteScalar());
            }

            // Data lama terbaca lewat koneksi baru ber-kunci.
            Assert.Equal("data lama", BacaIsi(hasil));
        }

        [Fact]
        public void JaminTerkunci_DiuLang_TanpaCadanganBaruDanTetapTerbaca()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            BuatDbPlaintext(jalur, "isi kedua");

            var pertama = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");
            var kedua = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");

            // Idempoten: run kedua mendeteksi sudah terenkripsi → tanpa konversi,
            // tidak membuat cadangan plaintext tambahan.
            Assert.Single(Directory.GetFiles(sementara.Folder, "desa-plaintext-*.bak"));
            Assert.Equal(pertama, kedua);
            Assert.Equal("isi kedua", BacaIsi(kedua));
        }

        [Fact]
        public void JaminTerkunci_DBBaru_DibuatTerenkripsiSejakAwal()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            Assert.False(File.Exists(jalur));

            var hasil = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");
            Assert.False(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(hasil).Password));

            using (var conn = new SqliteConnection(hasil))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Uji (Isi TEXT); INSERT INTO Uji VALUES ('baru');";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Berkas baru benar-benar terenkripsi sejak awal (bukan magic plaintext).
            var header = new byte[16];
            using (var fs = File.OpenRead(jalur))
                Assert.Equal(16, fs.Read(header, 0, 16));
            Assert.NotEqual("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));

            // Jalankan lagi (db kini ada & terenkripsi) → tetap terbaca.
            var ulang = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");
            Assert.Equal("baru", BacaIsi(ulang));
            Assert.Empty(Directory.GetFiles(sementara.Folder, "desa-plaintext-*.bak"));
        }

        [Fact]
        public void DeteksiKoneksi_DBTerenkripsi_MemakaiKunciDanPlaintextTanpaKunci()
        {
            using var sementara = new KunciSementara();
            var jalurPlaintext = Path.Combine(sementara.Folder, "plaintext.db");
            var jalurTerenkripsi = Path.Combine(sementara.Folder, "terenkripsi.db");
            BuatDbPlaintext(jalurPlaintext, "tanpa kunci");
            BuatDbPlaintext(jalurTerenkripsi, "dengan kunci");
            EnkripsiDatabase.JaminTerkunci($"Data Source={jalurTerenkripsi}");

            var csPlaintext = EnkripsiDatabase.DeteksiKoneksi(jalurPlaintext);
            var csTerenkripsi = EnkripsiDatabase.DeteksiKoneksi(jalurTerenkripsi);

            Assert.NotNull(csPlaintext);
            Assert.True(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(csPlaintext!).Password));
            Assert.Equal("tanpa kunci", BacaIsi(csPlaintext!));

            Assert.NotNull(csTerenkripsi);
            Assert.False(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(csTerenkripsi!).Password));
            Assert.Equal("dengan kunci", BacaIsi(csTerenkripsi!));

            // Berkas bukan database → null (tidak ada kunci yang dipaksakan).
            var bukanDb = Path.Combine(sementara.Folder, "bukan.db");
            File.WriteAllText(bukanDb, "bukan sqlite sama sekali");
            Assert.Null(EnkripsiDatabase.DeteksiKoneksi(bukanDb));
        }

        [Fact]
        public void Ekspor_VacuumInto_HasilnyaTetapTerbacaLayananImpor()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            BuatDbPlaintext(jalur, "diekspor");
            var cs = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");

            var target = Path.Combine(sementara.Folder, "hasil-ekspor.db");
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{target.Replace("'", "''")}'";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Invarian yang dituntut alur impor: berkas ekspor (apapun bentuk
            // enkripsinya) selalu bisa dibuka kembali oleh DeteksiKoneksi.
            var csHasil = EnkripsiDatabase.DeteksiKoneksi(target);
            Assert.NotNull(csHasil);
            Assert.Equal("diekspor", BacaIsi(csHasil!));

            // Hasil VACUUM INTO dari database terenkripsi ikut terenkripsi
            // (SQLCipher menerapkan kunci pada database tujuan) — salinan lokal
            // dan unggahan cadangan cloud tetap aman di atap, sementara alur
            // impor tetap bisa membacanya lewat DeteksiKoneksi (kunci yang sama).
            SqliteConnection.ClearAllPools();
            var header = new byte[16];
            using (var fs = File.OpenRead(target))
                Assert.Equal(16, fs.Read(header, 0, 16));
            Assert.NotEqual("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));
            Assert.False(string.IsNullOrEmpty(new SqliteConnectionStringBuilder(csHasil!).Password));
        }

        [Fact]
        public void PeriksaBerkas_BelumAda_UntukBerkasYangTidakAda()
        {
            using var sementara = new KunciSementara();

            Assert.Equal(
                StatusBerkasDatabase.BelumAda,
                EnkripsiDatabase.PeriksaBerkas(BuatJalurDb(sementara.Folder)));
            Assert.Equal(
                StatusBerkasDatabase.BelumAda,
                EnkripsiDatabase.PeriksaBerkas(null));
        }

        [Fact]
        public void PeriksaBerkas_DBPlaintext_SiapDienkripsiTanpaMengubahBerkas()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            BuatDbPlaintext(jalur, "polos");
            var sebelum = File.GetLastWriteTimeUtc(jalur);

            Assert.Equal(
                StatusBerkasDatabase.PlaintextSiapDienkripsi,
                EnkripsiDatabase.PeriksaBerkas(jalur));

            // Pemeriksaan tidak mengubah berkas: isinya masih terbaca tanpa kunci.
            Assert.Equal(sebelum, File.GetLastWriteTimeUtc(jalur));
            Assert.Equal("polos", BacaIsi($"Data Source={jalur}"));
        }

        [Fact]
        public void PeriksaBerkas_DBPilihanSendiri_TerenkripsiTerkunciIni()
        {
            using var sementara = new KunciSementara();
            var jalur = BuatJalurDb(sementara.Folder);
            BuatDbPlaintext(jalur, "terkunci");
            var cs = EnkripsiDatabase.JaminTerkunci($"Data Source={jalur}");
            Assert.Equal("terkunci", BacaIsi(cs));

            Assert.Equal(
                StatusBerkasDatabase.TerenkripsiTerkunciIni,
                EnkripsiDatabase.PeriksaBerkas(jalur));
        }

        [Fact]
        public void PeriksaBerkas_TerenkripsiKunciMesinLain_TidakTerbaca()
        {
            using var sementara = new KunciSementara();
            var jalur = Path.Combine(sementara.Folder, "desa-orang-lain.db");

            // Database dibuat dengan kunci LAIN (seolah dari komputer lain):
            // kunci milik komputer ini tidak boleh dianggap cocok.
            using (var conn = new SqliteConnection(
                       new SqliteConnectionStringBuilder
                       {
                           DataSource = jalur,
                           Password = "kunci-komputer-lain"
                       }.ToString()))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Uji (Isi TEXT); INSERT INTO Uji VALUES ('milik lain');";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Kunci komputer ini dipastikan ada, supaya hasilnya benar-benar
            // menguji "kunci tidak cocok" — bukan "kunci belum pernah dibuat".
            KunciDatabase.MuatAtauBuat();

            Assert.Equal(
                StatusBerkasDatabase.TidakTerbaca,
                EnkripsiDatabase.PeriksaBerkas(jalur));
        }

        [Fact]
        public void PeriksaBerkas_BerkasRusak_TidakTerbaca()
        {
            using var sementara = new KunciSementara();
            var jalur = Path.Combine(sementara.Folder, "rusak.db");
            File.WriteAllText(jalur, "ini bukan database sama sekali");
            KunciDatabase.MuatAtauBuat();

            Assert.Equal(
                StatusBerkasDatabase.TidakTerbaca,
                EnkripsiDatabase.PeriksaBerkas(jalur));
        }
    }
}
