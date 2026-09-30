using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Lapisan C (keamanan): enkripsi database desa.db dengan SQLCipher.
    ///
    /// Alur startup (idempoten, dipanggil sekali sebelum DatabaseInitializer):
    /// 1. Kunci DPAPI 256 bit diambil dari <see cref="KunciDatabase"/> (dibuat
    ///    bila belum pernah ada).
    /// 2. Berkas belum ada → connection string langsung diberi Password= sehingga
    ///    database baru terenkripsi sejak byte pertama.
    /// 3. Berkas ada → dibaca TANPA kunci dulu:
    ///    - terbaca (plaintext, database lama) → salin cadangan desa-plaintext-*.bak,
    ///      konversi isi ke database terenkripsi baru lewat ATTACH ... KEY +
    ///      sqlcipher_export(), ganti berkas lama, verifikasi dengan kunci.
    ///    - "file is not a database" (sudah terenkripsi) → verifikasi dengan kunci;
    ///      kunci berbeda → galat jelas (jangan menebak-nebak).
    /// 4. Koneksi pengujian (:memory:) dilewatkan tanpa kunci — enkripsi hanya
    ///    untuk berkas database sungguhan.
    ///
    /// CATATAN PENTING: PRAGMA rekey TIDAK bisa mengenkripsi database plaintext —
    /// SQLCipher hanya mengizinkannya untuk mengganti kunci database yang SUDAH
    /// terenkripsi (dokumen Zetetic: "PRAGMA rekey can not be used to encrypt a
    /// standard SQLite database"). Karena itu migrasi memakai sqlcipher_export():
    /// buka plaintext tanpa kunci, ATTACH database tujuan ber-kunci, salin seluruh
    /// isi, lalu ganti berkas lamanya.
    ///
    /// Kenapa berkas juga diperiksa byte-per-byte setelah konversi: bila provider
    /// native yang termuat bukan e_sqlcipher, Password= tidak berfungsi dan ATTACH
    /// ... KEY diam-diam menghasilkan database plaintext — tanpa pemeriksaan
    /// header, aplikasi akan "merasa" terenkripsi padahal berkas masih terbuka.
    /// </summary>
    /// <summary>
    /// Keadaan sebuah berkas database — dipakai halaman Pengaturan Aplikasi
    /// sebelum pengguna memilih berkas database desa, supaya akibatnya sudah
    /// jelas lebih dahulu (dibuat baru, dienkripsi, atau tidak bisa dibuka).
    /// </summary>
    public enum StatusBerkasDatabase
    {
        /// <summary>Berkas belum ada di lokasi itu — akan dibuat baru (terenkripsi).</summary>
        BelumAda,

        /// <summary>Sudah terenkripsi dan cocok dengan kunci di komputer ini.</summary>
        TerenkripsiTerkunciIni,

        /// <summary>Masih database polos (SQLite biasa) — akan dienkripsi otomatis saat dipakai.</summary>
        PlaintextSiapDienkripsi,

        /// <summary>
        /// Tidak bisa dibaca: rusak, atau terenkripsi dengan kunci komputer lain
        /// sehingga aplikasi tidak akan bisa memakainya.
        /// </summary>
        TidakTerbaca
    }

    public static class EnkripsiDatabase
    {
        private static readonly byte[] HeaderPlaintext =
            System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");

        /// <summary>Sudah diperiksa bahwa provider SQLCipher benar-benar aktif.</summary>
        private static volatile bool _sqlcipherDiperiksa;

        /// <summary>
        /// Jamin connection string merujuk database terenkripsi; kembalikan
        /// connection string final (dengan Password=). Idempoten.
        /// </summary>
        /// <param name="connectionString">Connection string asal (boleh tanpa kunci).</param>
        /// <param name="logger">Logger opsional untuk jalur konversi.</param>
        /// <param name="cadangkanPlaintext">
        /// Salin berkas plaintext ke desa-plaintext-*.bak SEBELUM konversi.
        /// Dimatikan untuk jalur impor database — di sana cadangan sudah dibuat
        /// prosedur impor sendiri dan berkas sumber bukan database aktif.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// SQLCipher tidak aktif, atau database terenkripsi dengan kunci yang
        /// tidak tersedia di komputer ini.
        /// </exception>
        public static string JaminTerkunci(string connectionString, ILogger? logger = null, bool cadangkanPlaintext = true)
        {
            logger ??= NullLogger.Instance;
            PastikanSqlcipherAktif();

            var builder = new SqliteConnectionStringBuilder(connectionString);
            var path = NormalisasiPath(builder.DataSource);
            if (path == null) return connectionString; // :memory: / file: / kosong — tanpa kunci

            builder.DataSource = path;
            builder.Password = string.Empty;

            if (!File.Exists(path))
            {
                builder.Password = KunciDatabase.MuatAtauBuat();
                logger.LogInformation("Database baru akan dibuat terenkripsi (SQLCipher): {Path}", path);
                return builder.ToString();
            }

            var kunci = KunciDatabase.MuatAtauBuat();

            if (!BacaTanpaKunci(builder.ToString(), path, logger))
            {
                // Sudah terenkripsi (atau rusak): satu-satunya jalan adalah kunci kita.
                builder.Password = kunci;
                if (!Terbaca(builder.ToString()))
                    throw new InvalidOperationException(
                        "Database terenkripsi tetapi kunci di komputer ini tidak cocok.\n" +
                        $"Database: {path}\n" +
                        $"Berkas kunci: {KunciDatabase.LokasiTampil}\n" +
                        "Pulihkan berkas kunci itu dari cadangan (atau salin dari instalasi " +
                        "asal database) — tanpa kunci aslinya data tidak bisa dibuka.");
                logger.LogInformation("Database sudah terenkripsi, dilewati: {Path}", path);
                SqliteConnection.ClearAllPools(); // jangan tahan handle hasil pemeriksaan
                return builder.ToString();
            }

            // ----- plaintext: backup lalu konversi ke database terenkripsi -----
            string? jalurCadangan = null;
            if (cadangkanPlaintext)
            {
                jalurCadangan = Path.Combine(
                    Path.GetDirectoryName(path) ?? ".",
                    $"desa-plaintext-{DateTime.Now:yyyyMMddHHmmss}.bak");
                File.Copy(path, jalurCadangan, true);
                logger.LogInformation("Cadangan database plaintext disimpan di: {Cadangan}", jalurCadangan);
            }

            var jalurBaru = path + ".enc-new";
            try
            {
                KonversiTerkripsi(builder.ToString(), jalurBaru, kunci);

                // Berkas HASIL wajib terbaca dengan kunci (dan bukan plaintext)
                // sebelum berkas lama diganti.
                var csBaru = new SqliteConnectionStringBuilder(builder.ToString())
                {
                    DataSource = jalurBaru,
                    Password = kunci
                }.ToString();
                if (HeaderMasihPlaintext(jalurBaru) || !Terbaca(csBaru))
                    throw KonversiGagal(jalurCadangan);

                // Ganti berkas lama. Cadangan sudah ada, jadi berkas plaintext lama
                // boleh dilepas; sidecar -wal/-shm sudah bersih setelah penutupan
                // normal, namun dihapus secara defensif karena tidak ikut dipindah.
                SqliteConnection.ClearAllPools();
                CobaHapus(path + "-wal");
                CobaHapus(path + "-shm");
                CobaHapus(jalurBaru + "-wal");
                CobaHapus(jalurBaru + "-shm");
                File.Move(jalurBaru, path, overwrite: true);

                builder.Password = kunci;
                if (HeaderMasihPlaintext(path) || !Terbaca(builder.ToString()))
                    throw KonversiGagal(jalurCadangan);
                SqliteConnection.ClearAllPools(); // jangan tahan handle hasil pemeriksaan
            }
            finally
            {
                CobaHapus(jalurBaru); // kalau gagal di tengah, berkas lama tidak tersentuh
            }

            logger.LogInformation(
                "Database plaintext dikonversi ke terenkripsi (sqlcipher_export): {Path}",
                path);
            return builder.ToString();
        }

        private static InvalidOperationException KonversiGagal(string? jalurCadangan) =>
            new(
                "Enkripsi database gagal diverifikasi setelah konversi sqlcipher_export.\n" +
                (jalurCadangan != null
                    ? $"Database asli tidak diubah; cadangan plaintext ada di: {jalurCadangan}"
                    : "Database asli tidak diubah."));

        /// <summary>
        /// Deteksi connection string TERBAIK untuk membaca berkas database tanpa
        /// memodifikasinya: tanpa kunci bila plaintext, dengan kunci tersimpan bila
        /// terenkripsi, null bila tidak bisa dibuka sama sekali (termasuk kunci
        /// dari mesin lain). Dipakai impor/ekspor database yang berkasnya bisa
        /// dalam dua bentuk.
        /// </summary>
        public static string? DeteksiKoneksi(string path)
        {
            var absolut = NormalisasiPath(path);
            if (absolut == null || !File.Exists(absolut)) return null;

            var builder = new SqliteConnectionStringBuilder { DataSource = absolut };
            if (Terbaca(builder.ToString())) return builder.ToString();

            var kunci = KunciDatabase.Muat(); // sengaja Muat(): jangan membuat kunci baru demi sekadar membaca
            if (kunci == null) return null;

            builder.Password = kunci;
            return Terbaca(builder.ToString()) ? builder.ToString() : null;
        }

        /// <summary>
        /// Salin seluruh isi database plaintext (terbuka tanpa kunci) ke berkas
        /// baru yang terkunci. ATTACH ... KEY membuat database tujuan terenkripsi
        /// dengan <paramref name="kunci"/>, lalu sqlcipher_export menyalin skema,
        /// data, trigger, dan seluruh objek dari main ke tujuan.
        /// </summary>
        private static void KonversiTerkripsi(string connectionStringPlaintext, string jalurBaru, string kunci)
        {
            SqliteConnection.ClearAllPools();
            CobaHapus(jalurBaru);
            CobaHapus(jalurBaru + "-wal");
            CobaHapus(jalurBaru + "-shm");

            using (var conn = new SqliteConnection(connectionStringPlaintext))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"ATTACH DATABASE {Kutip(jalurBaru)} AS enc KEY {Kutip(kunci)};";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT sqlcipher_export('enc');";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DETACH DATABASE enc;";
                    cmd.ExecuteNonQuery();
                }
            }
            SqliteConnection.ClearAllPools(); // tutup normal → wal tujuan ter-checkpoint & dilepas
        }

        /// <summary>Literal string SQL aman (hanya kutip ganda yang perlu digandakan).</summary>
        private static string Kutip(string nilai) => "'" + nilai.Replace("'", "''") + "'";

        /// <summary>
        /// Pastikan provider native yang termuat benar-benar SQLCipher. Provider
        /// e_sqlite3 biasa mengabaikan PRAGMA terkait cipher tanpa error, sehingga
        /// tanpa pemeriksaan ini migrasi bisa "sukses" palsu.
        /// </summary>
        public static void PastikanSqlcipherAktif()
        {
            if (_sqlcipherDiperiksa) return;

            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using var cmd = new SqliteCommand("PRAGMA cipher_version;", conn);
            var versi = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(versi))
                throw new InvalidOperationException(
                    "SQLCipher tidak aktif (PRAGMA cipher_version kosong): provider native " +
                    "e_sqlcipher tidak termuat, sehingga enkripsi database tidak berfungsi.");

            _sqlcipherDiperiksa = true;
        }

        /// <summary>
        /// Periksa keadaan berkas database TANPA mengubahnya sedikit pun — untuk
        /// memberi tahu pengguna lebih dahulu di halaman pengaturan.
        /// </summary>
        /// <param name="jalur">Jalur berkas (boleh relatif terhadap folder aplikasi).</param>
        public static StatusBerkasDatabase PeriksaBerkas(string? jalur)
        {
            var absolut = NormalisasiPath(jalur ?? string.Empty);
            if (absolut == null || !File.Exists(absolut)) return StatusBerkasDatabase.BelumAda;

            // Polos = berkasnya masih diawali magic SQLite. Dipastikan juga bisa dibaca,
            // supaya berkas rusak tidak dilaporkan sebagai "siap dienkripsi".
            if (HeaderMasihPlaintext(absolut))
            {
                var polos = new SqliteConnectionStringBuilder { DataSource = absolut };
                return Terbaca(polos.ToString())
                    ? StatusBerkasDatabase.PlaintextSiapDienkripsi
                    : StatusBerkasDatabase.TidakTerbaca;
            }

            // Bukan polos: coba kunci yang tersimpan di komputer ini. Muat() sengaja
            // dipakai (bukan MuatAtauBuat) agar sekadar memeriksa tidak membuat kunci baru.
            var kunci = KunciDatabase.Muat();
            if (kunci == null) return StatusBerkasDatabase.TidakTerbaca;

            var terkunci = new SqliteConnectionStringBuilder { DataSource = absolut, Password = kunci };
            return Terbaca(terkunci.ToString())
                ? StatusBerkasDatabase.TerenkripsiTerkunciIni
                : StatusBerkasDatabase.TidakTerbaca;
        }

        /// <summary>
        /// Baca database TANPA kunci. True = plaintext (dan WAL sudah di-checkpoint),
        /// False = tidak terbaca tanpa kunci (diasumsikan terenkripsi).
        /// </summary>
        private static bool BacaTanpaKunci(string connectionString, string path, ILogger logger)
        {
            bool plaintext;
            SqliteConnection.ClearAllPools();
            try
            {
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                try
                {
                    using var cmd = new SqliteCommand("SELECT count(*) FROM sqlite_master;", conn);
                    cmd.ExecuteScalar();
                    plaintext = true;
                }
                catch (SqliteException)
                {
                    plaintext = false; // "file is not a database" → terenkripsi
                }

                if (plaintext)
                {
                    try
                    {
                        // Pastikan seluruh isi WAL masuk berkas utama sebelum berkas
                        // disalin (cadangan) dan dikonversi — WAL sisa berisi data
                        // plaintext akan merusak database terenkripsi.
                        using var wal = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", conn);
                        wal.ExecuteScalar();
                    }
                    catch (SqliteException ex)
                    {
                        logger.LogWarning(ex, "Checkpoint WAL sebelum enkripsi gagal: {Path}", path);
                    }
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools(); // lepas handle pool supaya berkas bebas disalin
            }
            return plaintext;
        }

        /// <summary>True bila 16 byte pertama berkas masih magic "SQLite format 3" (plaintext).</summary>
        private static bool HeaderMasihPlaintext(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var header = new byte[HeaderPlaintext.Length];
                if (fs.Read(header, 0, header.Length) < header.Length) return false; // berkas kecil → bukan magic plaintext
                for (int i = 0; i < header.Length; i++)
                    if (header[i] != HeaderPlaintext[i]) return false;
                return true;
            }
            catch (IOException)
            {
                return false; // biar verifikasi SQL berikutnya yang menilai
            }
        }

        /// <summary>Bisa dibuka dan sqlite_master terbaca? (dipakai untuk deteksi & verifikasi.)</summary>
        private static bool Terbaca(string connectionString)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                using var cmd = new SqliteCommand("SELECT count(*) FROM sqlite_master;", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch (Exception)
            {
                // SqliteException (kunci salah/file bukan database), provider tanpa
                // sqlite3_key, dsb. → anggap tidak terbaca; pesan galat akhir
                // disusun oleh pemanggil.
                return false;
            }
        }

        private static void CobaHapus(string jalur)
        {
            try { if (File.Exists(jalur)) File.Delete(jalur); }
            catch (IOException) { /* file masih terkunci — biar penangan galat berikutnya yang bicara */ }
        }

        /// <summary>
        /// Path absolut untuk DataSource berkas; null untuk sumber non-berkas
        /// (:memory:, file:, kosong) yang tidak boleh disentuh enkripsi.
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
