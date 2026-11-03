using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Text;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Keadaan sebuah berkas database desa — dipakai halaman Pengaturan Aplikasi
    /// sebelum pengguna memilih berkas database, supaya akibatnya sudah jelas
    /// lebih dahulu (dibuat baru, siap dipakai, terenkripsi dan bisa dibuka, atau
    /// tidak bisa dibuka).
    /// </summary>
    public enum StatusBerkasDatabase
    {
        /// <summary>Berkas belum ada di lokasi itu — akan dibuat baru saat aplikasi dibuka.</summary>
        BelumAda,

        /// <summary>SQLite biasa dan bisa dibuka — siap dipakai apa adanya.</summary>
        Siap,

        /// <summary>
        /// Database terenkripsi (SQLCipher, buatan versi SuDesApp lama) dan kunci
        /// di komputer ini cocok — akan dibuka memakai kunci itu.
        /// </summary>
        Terenkripsi,

        /// <summary>
        /// Terenkripsi, tetapi kunci yang tersedia di komputer ini tidak ada atau
        /// tidak cocok. Berkasnya bukan database rusak: ia bisa dibuka asalkan
        /// berkas kunci dari instalasi asal tersedia.
        /// </summary>
        BerkasLamaTerkunci,

        /// <summary>Header SQLite benar tetapi isinya tidak bisa dibaca (rusak/tidak lengkap).</summary>
        TidakTerbaca
    }

    /// <summary>
    /// Pemeriksaan berkas dan penyusunan connection string database desa.
    ///
    /// Aplikasi versi ini menyimpan database baru sebagai SQLite biasa (tanpa
    /// enkripsi), tetapi instalasi yang diperbarui dari versi lama masih memakai
    /// database terenkripsi SQLCipher beserta berkas kuncinya
    /// (DesaKunciDb.bin). Keduanya harus bisa dibuka tanpa pengguna memilih apa
    /// pun, jadi bentuk database TIDAK PERNAH ditebak dari versi aplikasi atau
    /// dari appsettings: setiap kali dibuka, berkasnya sendiri yang diperiksa
    /// (header SQLite, lalu kunci tersimpan) dan connection string yang sesuai
    /// dipakai seluruh aplikasi.
    ///
    /// Dua aturan yang dijaga di sini:
    /// - database plaintext tidak pernah dienkripsi ulang saat startup, dan
    /// - database terenkripsi tidak pernah didekripsi diam-diam; ia tetap
    ///   terenkripsi dan dibuka memakai kunci yang sudah ada.
    /// </summary>
    public static class BerkasDatabase
    {
        private static readonly byte[] _headerSqlite =
            Encoding.ASCII.GetBytes("SQLite format 3\0");

        /// <summary>
        /// Sumber kunci database versi lama. Dipisahkan sebagai delegate supaya
        /// uji tidak bergantung pada DPAPI akun Windows yang menjalankan tes.
        /// </summary>
        internal static Func<string?> MuatKunci { get; set; } = KunciDatabase.Muat;

        /// <summary>Lokasi berkas kunci versi lama (untuk pesan & halaman pengaturan).</summary>
        public static string LokasiBerkasKunci => KunciDatabase.LokasiTampil;

        /// <summary>
        /// Periksa keadaan berkas TANPA mengubahnya sedikit pun — untuk
        /// memberi tahu pengguna lebih dahulu di halaman pengaturan.
        /// </summary>
        /// <param name="jalur">Jalur berkas (boleh relatif terhadap folder aplikasi).</param>
        public static StatusBerkasDatabase Periksa(string? jalur)
        {
            var absolut = NormalisasiPath(jalur ?? string.Empty);
            if (absolut == null || !File.Exists(absolut)) return StatusBerkasDatabase.BelumAda;

            var polos = KoneksiAbsolut(absolut);

            // Masih plaintext: cukup pastikan bisa dibaca. Berkas kosong dianggap
            // siap karena inisialisasi database yang akan mengisi skemanya.
            if (HeaderMasihSqlite(absolut))
                return Terbaca(polos) ? StatusBerkasDatabase.Siap : StatusBerkasDatabase.TidakTerbaca;

            // Bukan header SQLite → database terenkripsi versi lama (atau rusak).
            // Kunci yang tersimpan di komputer ini yang menentukan; sengaja tidak
            // membuat kunci baru hanya untuk memeriksa.
            var kunci = MuatKunci();
            if (string.IsNullOrWhiteSpace(kunci)) return StatusBerkasDatabase.BerkasLamaTerkunci;

            return Terbaca(DenganKunci(polos, kunci))
                ? StatusBerkasDatabase.Terenkripsi
                : StatusBerkasDatabase.BerkasLamaTerkunci;
        }

        /// <summary>
        /// Connection string untuk membaca berkas tanpa memodifikasinya: tanpa
        /// kunci bila plaintext, dengan kunci tersimpan bila terenkripsi, null bila
        /// tidak bisa dibuka sama sekali (rusak, atau kunci tidak tersedia di
        /// komputer ini). Dipakai impor/ekspor database yang berkasnya bisa datang
        /// dalam dua bentuk.
        /// </summary>
        public static string? DeteksiKoneksi(string jalur)
        {
            var absolut = NormalisasiPath(jalur);
            if (absolut == null || !File.Exists(absolut)) return null;

            var polos = KoneksiAbsolut(absolut);
            if (Terbaca(polos)) return polos;

            var kunci = MuatKunci(); // sengaja Muat(): jangan membuat kunci baru demi membaca
            if (string.IsNullOrWhiteSpace(kunci)) return null;

            var terkunci = DenganKunci(polos, kunci);
            return Terbaca(terkunci) ? terkunci : null;
        }

        /// <summary>
        /// Jamin connection string merujuk database yang bisa dibuka; kembalikan
        /// connection string final — ber-Password bila berkasnya terenkripsi versi
        /// lama. Berkas belum ada → dilewati (akan dibuat baru sebagai SQLite
        /// biasa). Berkas terenkripsi tanpa kunci yang cocok, atau rusak, →
        /// galat jelas, karena diteruskan hanya menghasilkan "file is not a
        /// database" yang membingungkan di setiap halaman.
        /// </summary>
        /// <exception cref="InvalidOperationException">Berkas tidak bisa dibuka.</exception>
        public static string PastikanTerbaca(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var absolut = NormalisasiPath(builder.DataSource);

            // :memory:, file:, atau kosong — bukan berkas, tidak perlu diperiksa.
            if (absolut == null) return connectionString;

            // Bentuk plaintext disusun ulang HANYA dengan Data Source. Menulis
            // "Password=" kosong ke connection string bukan berarti "tanpa kunci"
            // bagi SQLCipher, melainkan kunci kosong — database SQLite biasa lalu
            // ditolak sebagai "file is not a database". Connection string aplikasi
            // memang hanya membawa Data Source (appsettings/preferensi).
            var polos = KoneksiAbsolut(absolut);
            var kunciPemanggil = builder.Password;

            if (!File.Exists(absolut)) return polos; // database baru — dibuat saat inisialisasi

            if (HeaderMasihSqlite(absolut))
            {
                if (Terbaca(polos)) return polos;

                throw new InvalidOperationException(
                    "Database tidak bisa dibaca — berkasnya rusak atau tidak lengkap.\n" +
                    $"Database: {absolut}\n\n" +
                    "Pulihkan dari cadangan, atau pilih berkas database lain lewat " +
                    "Pengaturan Aplikasi → Database Desa.");
            }

            // Bukan header SQLite: kemungkinan besar database terenkripsi SQLCipher
            // dari versi SuDesApp lama. Bentuk ini hanya bisa dibuka bila kuncinya
            // tersedia; provider SQLCipher diperiksa lebih dahulu supaya kegagalan
            // provider tidak dilaporkan sebagai "kunci salah".
            PastikanSqlcipherAktif();

            var kunci = string.IsNullOrWhiteSpace(kunciPemanggil) ? MuatKunci() : kunciPemanggil;
            if (string.IsNullOrWhiteSpace(kunci))
                throw new InvalidOperationException(PesanKunciTidakTersedia(absolut));

            var terkunci = DenganKunci(polos, kunci);
            if (Terbaca(terkunci)) return terkunci;

            throw new InvalidOperationException(
                "Database terenkripsi tetapi kunci di komputer ini tidak cocok.\n" +
                $"Database: {absolut}\n" +
                $"Berkas kunci: {KunciDatabase.LokasiTampil}\n\n" +
                "Pulihkan berkas kunci itu dari instalasi asal database (atau dari cadangan " +
                "yang memuatnya), atau pulihkan cadangan database yang tidak terenkripsi. " +
                "Tanpa kunci aslinya data tidak bisa dibuka.");
        }

        /// <summary>True bila connection string membuka database terenkripsi (memuat Password).</summary>
        public static bool Terkunci(string connectionString) =>
            !string.IsNullOrWhiteSpace(new SqliteConnectionStringBuilder(connectionString).Password);

        /// <summary>
        /// Salin seluruh isi database <paramref name="sumberConnectionString"/> ke
        /// <paramref name="berkasTujuan"/> dalam bentuk yang diminta:
        /// <paramref name="kunciTujuan"/> null/kosong = SQLite biasa, selain itu
        /// terenkripsi dengan kunci tersebut.
        ///
        /// Dipakai pemulihan cadangan dan impor database saat bentuk berkas sumber
        /// berbeda dari database yang sedang dipakai. API backup SQLite menolak
        /// database terenkripsi ("backup is not supported with encrypted
        /// databases"), jadi isinya disalin lewat sqlcipher_export — cara yang sama
        /// yang dipakai versi lama saat mengonversi database.
        /// </summary>
        /// <exception cref="InvalidOperationException">Hasil salinan tidak sesuai bentuk yang diminta.</exception>
        public static void SalinIsi(string sumberConnectionString, string berkasTujuan, string? kunciTujuan)
        {
            if (string.IsNullOrWhiteSpace(berkasTujuan))
                throw new ArgumentException("Berkas tujuan tidak boleh kosong.", nameof(berkasTujuan));

            var tujuan = NormalisasiPath(berkasTujuan)
                ?? throw new ArgumentException(
                    "Tujuan penyalinan harus berupa berkas database.", nameof(berkasTujuan));

            if (!string.IsNullOrWhiteSpace(kunciTujuan) || Terkunci(sumberConnectionString))
                PastikanSqlcipherAktif();

            // sqlcipher_export hanya bisa menulis ke database tujuan yang masih
            // kosong: hapus berkas lama beserta sidecar-nya lebih dahulu.
            CobaHapus(tujuan + "-wal");
            CobaHapus(tujuan + "-shm");
            CobaHapus(tujuan + "-journal");
            CobaHapus(tujuan);

            var kunci = kunciTujuan ?? string.Empty;
            using (var conn = new SqliteConnection(sumberConnectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    // KEY '' = tujuan tanpa enkripsi. Tanpa klausa KEY sama sekali,
                    // SQLCipher menyambung tujuan sebagai database terenkripsi
                    // sehingga hasil "plaintext" tetap tidak bisa dibuka SQLite biasa.
                    cmd.CommandText = $"ATTACH DATABASE {Kutip(tujuan)} AS salinan KEY {Kutip(kunci)};";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT sqlcipher_export('salinan');";
                    cmd.ExecuteScalar();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DETACH DATABASE salinan;";
                    cmd.ExecuteNonQuery();
                }
            }

            // Lepas handle pool supaya berkas hasil bisa dipindah/dibaca proses lain.
            SqliteConnection.ClearAllPools();

            // Hasil wajib benar-benar berbentuk seperti yang diminta. Provider tanpa
            // SQLCipher mengabaikan KEY, sehingga tanpa pemeriksaan ini salinan bisa
            // "merasa" terenkripsi padahal berkasnya terbuka (atau sebaliknya).
            bool hasilPolos = HeaderMasihSqlite(tujuan);
            bool dimintaPolos = string.IsNullOrWhiteSpace(kunci);

            if (hasilPolos != dimintaPolos)
                throw new InvalidOperationException(
                    $"Penyalinan database tidak menghasilkan berkas {(dimintaPolos ? "tanpa enkripsi" : "terenkripsi")} " +
                    $"seperti yang diminta: {tujuan}\n\n" +
                    "SQLCipher tidak aktif atau kunci tujuan tidak berlaku. Berkas tujuan dibuang; " +
                    "database sumber tidak diubah.");

            var koneksiTujuan = dimintaPolos ? KoneksiAbsolut(tujuan) : DenganKunci(KoneksiAbsolut(tujuan), kunci);
            if (!Terbaca(koneksiTujuan))
                throw new InvalidOperationException(
                    $"Hasil penyalinan database tidak bisa dibuka: {tujuan}\n\n" +
                    "Berkas tujuan dibuang; database sumber tidak diubah.");

            SqliteConnection.ClearAllPools();
        }

        /// <summary>Connection string tanpa kunci untuk satu berkas absolut.</summary>
        private static string KoneksiAbsolut(string absolut) =>
            new SqliteConnectionStringBuilder { DataSource = absolut }.ToString();

        /// <summary>Connection string yang sama dengan kunci database.</summary>
        private static string DenganKunci(string connectionString, string kunci)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString) { Password = kunci };
            return builder.ToString();
        }

        /// <summary>
        /// Pastikan provider native yang termuat benar-benar SQLCipher. Provider
        /// e_sqlite3 biasa mengabaikan PRAGMA terkait cipher tanpa galat, sehingga
        /// tanpa pemeriksaan ini database terenkripsi akan dilaporkan sebagai
        /// "kunci salah" — diagnosa yang menyesatkan.
        /// </summary>
        private static void PastikanSqlcipherAktif()
        {
            if (_sqlcipherDiperiksa) return;

            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using var cmd = new SqliteCommand("PRAGMA cipher_version;", conn);
            var versi = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(versi))
                throw new InvalidOperationException(
                    "SQLCipher tidak aktif (PRAGMA cipher_version kosong): provider native " +
                    "e_sqlcipher tidak termuat, sehingga database terenkripsi versi lama tidak bisa dibuka.");

            _sqlcipherDiperiksa = true;
        }

        private static volatile bool _sqlcipherDiperiksa;

        /// <summary>Pesan untuk database terenkripsi yang kuncinya tidak ada di komputer ini.</summary>
        private static string PesanKunciTidakTersedia(string absolut)
        {
            bool kunciAdaTetapiTidakTerbaca = KunciDatabase.Ada;

            return (kunciAdaTetapiTidakTerbaca
                       ? "Berkas kunci database ada tetapi tidak bisa dibaca — kemungkinan rusak atau " +
                         "dibuat oleh akun Windows lain.\n"
                       : "Berkas database ini terenkripsi (dibuat versi SuDesApp lama) dan berkas " +
                         "kuncinya tidak ditemukan di komputer ini.\n") +
                   $"Database: {absolut}\n" +
                   $"Berkas kunci yang dicari: {KunciDatabase.LokasiTampil}\n\n" +
                   "Aplikasi versi ini tetap bisa membuka database terenkripsi asalkan berkas kunci " +
                   "dari instalasi asal ikut tersedia. Salin DesaKunciDb.bin dari komputer yang " +
                   "memakai database ini (folder %LOCALAPPDATA%\\SuDesApp) ke lokasi di atas, atau " +
                   "pulihkan cadangan database yang tidak terenkripsi, atau pilih berkas database " +
                   "lain lewat Pengaturan Aplikasi → Database Desa.";
        }

        /// <summary>Bisa dibuka dan sqlite_master terbaca?</summary>
        private static bool Terbaca(string connectionString)
        {
            try
            {
                // This is a short-lived inspection connection. Do not clear every
                // SQLite pool in the process, and do not return its file handle
                // to a pool after the check completes.
                var builder = new SqliteConnectionStringBuilder(connectionString)
                {
                    Pooling = false
                };
                using var conn = new SqliteConnection(builder.ToString());
                conn.Open();
                using var cmd = new SqliteCommand("SELECT count(*) FROM sqlite_master;", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch (Exception)
            {
                // Bukan SQLite / kunci salah / rusak → anggap tidak terbaca;
                // pesan galat akhir disusun oleh pemanggil.
                return false;
            }
        }

        /// <summary>True bila berkas kosong atau header awalnya adalah magic SQLite.</summary>
        private static bool HeaderMasihSqlite(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                // SQLite treats an existing zero-byte file as a valid empty
                // database; DatabaseInitializer can create its schema in it.
                if (fs.Length == 0) return true;

                var header = new byte[_headerSqlite.Length];
                if (fs.Read(header, 0, header.Length) < header.Length) return false;
                for (int i = 0; i < header.Length; i++)
                    if (header[i] != _headerSqlite[i]) return false;
                return true;
            }
            catch (IOException)
            {
                return false; // biar pemeriksaan SQL berikutnya yang menilai
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void CobaHapus(string jalur)
        {
            try { if (File.Exists(jalur)) File.Delete(jalur); }
            catch (IOException) { /* masih dipakai proses lain — biar penangan galat berikutnya yang bicara */ }
            catch (UnauthorizedAccessException) { /* tanpa izin hapus — sama seperti di atas */ }
        }

        /// <summary>Literal string SQL aman (hanya kutip tunggal yang perlu digandakan).</summary>
        private static string Kutip(string nilai) => "'" + nilai.Replace("'", "''") + "'";

        /// <summary>
        /// Path absolut untuk DataSource berkas; null untuk sumber non-berkas
        /// (:memory:, file:, kosong) yang tidak boleh diperlakukan sebagai berkas.
        /// </summary>
        private static string? NormalisasiPath(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource)) return null;
            var p = dataSource.Trim();
            if (p == ":memory:" || p.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!Path.IsPathRooted(p))
                p = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, p));
            return p;
        }
    }
}
