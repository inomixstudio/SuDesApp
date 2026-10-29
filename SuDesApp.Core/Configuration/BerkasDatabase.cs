using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Keadaan sebuah berkas database desa — dipakai halaman Pengaturan Aplikasi
    /// sebelum pengguna memilih berkas database, supaya akibatnya sudah jelas
    /// lebih dahulu (dibuat baru, bisa dipakai, atau tidak bisa dibuka).
    /// </summary>
    public enum StatusBerkasDatabase
    {
        /// <summary>Berkas belum ada di lokasi itu — akan dibuat baru saat aplikasi dibuka.</summary>
        BelumAda,

        /// <summary>SQLite biasa dan bisa dibuka — siap dipakai apa adanya.</summary>
        Siap,

        /// <summary>
        /// Berkas bukan database SQLite yang bisa dibuka. Kemungkinan terbesar:
        /// database dari versi SuDesApp lama yang masih terenkripsi SQLCipher,
        /// yang tidak lagi didukung aplikasi versi ini.
        /// </summary>
        BerkasLamaTerkunci,

        /// <summary>Header SQLite benar tetapi isinya tidak bisa dibaca (rusak/tidak lengkap).</summary>
        TidakTerbaca
    }

    /// <summary>
    /// Pemeriksaan berkas database desa. Aplikasi menyimpan database sebagai
    /// SQLite biasa (tanpa enkripsi, tanpa berkas kunci), jadi satu-satunya
    /// pertanyaan yang perlu dijawab di sini adalah: "apakah berkas ini
    /// database SQLite yang bisa dibuka?".
    /// </summary>
    public static class BerkasDatabase
    {
        private static readonly byte[] _headerSqlite =
            System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");

        /// <summary>
        /// Periksa keadaan berkas TANPA mengubahnya sedikit pun — untuk
        /// memberi tahu pengguna lebih dahulu di halaman pengaturan.
        /// </summary>
        /// <param name="jalur">Jalur berkas (boleh relatif terhadap folder aplikasi).</param>
        public static StatusBerkasDatabase Periksa(string? jalur)
        {
            var absolut = NormalisasiPath(jalur ?? string.Empty);
            if (absolut == null || !File.Exists(absolut)) return StatusBerkasDatabase.BelumAda;

            if (!HeaderMasihSqlite(absolut)) return StatusBerkasDatabase.BerkasLamaTerkunci;

            return Terbaca(KoneksiAbsolut(absolut))
                ? StatusBerkasDatabase.Siap
                : StatusBerkasDatabase.TidakTerbaca;
        }

        /// <summary>
        /// Connection string untuk membaca berkas tanpa memodifikasinya;
        /// null bila tidak bisa dibuka sama sekali (rusak, atau database versi
        /// lama yang terenkripsi). Dipakai impor/ekspor database yang berkasnya
        /// bisa datang dari dua bentuk itu.
        /// </summary>
        public static string? DeteksiKoneksi(string jalur)
        {
            var absolut = NormalisasiPath(jalur);
            if (absolut == null || !File.Exists(absolut)) return null;

            var connectionString = KoneksiAbsolut(absolut);
            return Terbaca(connectionString) ? connectionString : null;
        }

        /// <summary>
        /// Jamin connection string merujuk database SQLite yang bisa dibuka;
        /// kembalikan connection string final. Berkas belum ada → dilewati
        /// (akan dibuat baru). Berkas ada tetapi bukan SQLite biasa → galat
        /// jelas, karena diteruskan hanya menghasilkan galat "file is not a
        /// database" yang membingungkan di setiap halaman.
        /// </summary>
        /// <exception cref="InvalidOperationException">Berkas bukan SQLite yang bisa dibuka.</exception>
        public static string PastikanTerbaca(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var absolut = NormalisasiPath(builder.DataSource);

            // :memory:, file:, atau kosong — bukan berkas, tidak perlu diperiksa.
            if (absolut == null) return connectionString;

            builder.DataSource = absolut;
            var final = builder.ToString();

            if (!File.Exists(absolut)) return final; // database baru — dibuat saat inisialisasi

            if (!HeaderMasihSqlite(absolut))
                throw new InvalidOperationException(
                    "Berkas database bukan database SQLite yang bisa dibuka.\n" +
                    $"Database: {absolut}\n\n" +
                    "Berkas ini kemungkinan berasal dari versi SuDesApp lama yang masih " +
                    "terenkripsi; aplikasi versi ini menyimpan database tanpa enkripsi " +
                    "sehingga tidak bisa membukanya.\n\n" +
                    "Pulihkan dari cadangan database (.db) yang lebih baru, atau pilih " +
                    "berkas database lain lewat Pengaturan Aplikasi → Database Desa.");

            if (!Terbaca(final))
                throw new InvalidOperationException(
                    "Database tidak bisa dibaca — berkasnya rusak atau tidak lengkap.\n" +
                    $"Database: {absolut}\n\n" +
                    "Pulihkan dari cadangan, atau pilih berkas database lain lewat " +
                    "Pengaturan Aplikasi → Database Desa.");

            return final;
        }

        /// <summary>Connection string tanpa kunci untuk satu berkas absolut.</summary>
        private static string KoneksiAbsolut(string absolut) =>
            new SqliteConnectionStringBuilder { DataSource = absolut }.ToString();

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
