using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Hasil satu percobaan pemulihan database saat startup. <see cref="Cadangan"/>
    /// adalah berkas database lama yang dipindahkan ke samping; berkas itu tidak
    /// pernah dihapus, sehingga data desa masih bisa diambil kembali secara manual.
    /// </summary>
    public sealed record HasilPemulihanDatabase(
        bool Berhasil,
        bool DatabaseBaru,
        string? Sumber,
        string? Cadangan,
        string? ConnectionString,
        string? Pesan);

    /// <summary>
    /// Pemulihan database otomatis untuk pemasangan/upgrade pertama.
    ///
    /// Urutannya sengaja tidak pernah menimpa berkas aktif secara langsung:
    /// kandidat disalin ke berkas sementara, diperiksa, lalu berkas aktif lama
    /// dipindahkan ke samping sebelum berkas baru menggantikannya. Bila tidak ada
    /// kandidat yang bisa dibaca, berkas lama tetap dipindahkan (bukan dihapus)
    /// dan aplikasi diizinkan membuat database baru — supaya aplikasi bisa
    /// dibuka, tanpa mengorbankan kemungkinan memulihkan data lama nanti.
    /// </summary>
    public static class PemulihanDatabaseOtomatis
    {
        /// <summary>
        /// Coba pulihkan database aktif. Tidak melempar untuk kegagalan yang wajar
        /// (berkas rusak/terkunci); pemanggil memakai <see cref="HasilPemulihanDatabase.Berhasil"/>
        /// untuk memutuskan langkah berikutnya.
        /// </summary>
        /// <param name="jalurAktif">Berkas database yang gagal dibuka.</param>
        /// <param name="connectionStringAktif">Connection string yang sedang dipakai; Password-nya dipertahankan.</param>
        /// <param name="kandidatTambahan">Kandidat tambahan (mis. dari pengaturan/pengujian).</param>
        public static HasilPemulihanDatabase Pulihkan(
            string jalurAktif,
            string connectionStringAktif,
            IEnumerable<string>? kandidatTambahan = null,
            ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;

            if (string.IsNullOrWhiteSpace(jalurAktif))
                return Gagal("Lokasi database tidak diketahui.");

            var jalur = Path.GetFullPath(jalurAktif);
            var kunciTujuan = BacaKunci(connectionStringAktif);

            if (!File.Exists(jalur))
            {
                return new HasilPemulihanDatabase(
                    Berhasil: true,
                    DatabaseBaru: true,
                    Sumber: null,
                    Cadangan: null,
                    ConnectionString: SusunKoneksi(jalur, kunciTujuan),
                    Pesan: "Berkas database belum ada; database baru akan dibuat.");
            }

            var statusAktif = BerkasDatabase.Periksa(jalur);
            if (statusAktif is StatusBerkasDatabase.Siap or StatusBerkasDatabase.Terenkripsi)
            {
                return new HasilPemulihanDatabase(
                    Berhasil: true,
                    DatabaseBaru: false,
                    Sumber: jalur,
                    Cadangan: null,
                    ConnectionString: SusunKoneksi(jalur, kunciTujuan),
                    Pesan: "Database aktif masih bisa dibaca.");
            }

            // Berkas terkunci bukan alasan untuk memindahkannya: proses lain
            // mungkin sedang menulis. Biarkan pemanggil menampilkan pilihan manual.
            if (statusAktif == StatusBerkasDatabase.Terkunci)
            {
                return Gagal(
                    "Berkas database sedang dipakai proses lain dan belum bisa dipindahkan. " +
                    "Tutup aplikasi lain yang memakai berkas itu, lalu buka kembali SuDesApp.");
            }

            foreach (var kandidat in Kandidat(jalur, kandidatTambahan))
            {
                if (string.Equals(kandidat, jalur, StringComparison.OrdinalIgnoreCase))
                    continue;

                var statusKandidat = BerkasDatabase.Periksa(kandidat);
                if (statusKandidat is not (StatusBerkasDatabase.Siap or StatusBerkasDatabase.Terenkripsi))
                    continue;

                var koneksiKandidat = BerkasDatabase.DeteksiKoneksi(kandidat);
                if (string.IsNullOrWhiteSpace(koneksiKandidat))
                    continue;

                var hasil = PulihkanDariKandidat(
                    jalur, koneksiKandidat, kandidat, kunciTujuan, logger);
                if (hasil.Berhasil) return hasil;

                logger.LogWarning(
                    "Kandidat database {Kandidat} gagal dipulihkan; mencoba kandidat berikutnya.",
                    kandidat);
            }

            // Tidak ada kandidat sehat. Pindahkan berkas aktif (beserta sidecar)
            // agar DatabaseInitializer dapat membuat database baru. Berkas lama
            // tetap ada di samping database untuk pemulihan manual.
            try
            {
                var cadangan = PindahkanKeSamping(jalur);
                logger.LogWarning(
                    "Database aktif tidak bisa dipulihkan; berkas lama dipindahkan ke {Cadangan}. " +
                    "Aplikasi akan membuat database baru.",
                    cadangan);

                return new HasilPemulihanDatabase(
                    Berhasil: true,
                    DatabaseBaru: true,
                    Sumber: null,
                    Cadangan: cadangan,
                    ConnectionString: SusunKoneksi(jalur, kunciTujuan),
                    Pesan:
                        "Database lama tidak bisa dibaca dan telah dipindahkan ke:\n" +
                        cadangan +
                        "\n\nAplikasi dimulai dengan database baru. Data lama tidak dihapus; " +
                        "pulihkan berkas itu secara manual bila datanya masih dibutuhkan.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gagal memindahkan database lama yang tidak terbaca.");
                return Gagal(
                    "Database lama tidak bisa dibaca dan juga tidak bisa dipindahkan: " +
                    ex.Message +
                    "\n\nTutup aplikasi lain yang memakai berkas itu, lalu coba lagi.");
            }
        }

        private static HasilPemulihanDatabase PulihkanDariKandidat(
            string jalurAktif,
            string koneksiKandidat,
            string kandidat,
            string? kunciTujuan,
            ILogger logger)
        {
            var sementara = jalurAktif + ".pemulihan-" + Guid.NewGuid().ToString("N") + ".db";
            string? cadangan = null;

            try
            {
                // Salin ke berkas sementara lebih dahulu: database aktif belum
                // disentuh sampai hasil salinan terbukti bisa dibuka.
                BerkasDatabase.SalinIsi(koneksiKandidat, sementara, kunciTujuan);

                var koneksiSementara = SusunKoneksi(sementara, kunciTujuan);
                BerkasDatabase.PastikanTerbaca(koneksiSementara);

                cadangan = PindahkanKeSamping(jalurAktif);

                try
                {
                    File.Move(sementara, jalurAktif);
                }
                catch
                {
                    // Kembalikan berkas lama bila penggantian gagal, supaya
                    // kegagalan pemulihan tidak menghilangkan database aktif.
                    if (File.Exists(cadangan))
                        File.Move(cadangan, jalurAktif);
                    throw;
                }

                var koneksiBaru = SusunKoneksi(jalurAktif, kunciTujuan);
                BerkasDatabase.PastikanTerbaca(koneksiBaru);

                logger.LogInformation(
                    "Database dipulihkan dari {Kandidat}; database lama disimpan di {Cadangan}.",
                    kandidat, cadangan);

                return new HasilPemulihanDatabase(
                    Berhasil: true,
                    DatabaseBaru: false,
                    Sumber: kandidat,
                    Cadangan: cadangan,
                    ConnectionString: koneksiBaru,
                    Pesan:
                        "Database desa dipulihkan dari:\n" + kandidat +
                        (string.IsNullOrWhiteSpace(cadangan)
                            ? string.Empty
                            : "\n\nBerkas database lama disimpan di:\n" + cadangan));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Pemulihan dari kandidat {Kandidat} gagal.", kandidat);
                return Gagal("Pemulihan dari " + kandidat + " gagal: " + ex.Message);
            }
            finally
            {
                HapusSidecar(sementara);
                if (File.Exists(sementara))
                {
                    try { File.Delete(sementara); }
                    catch (IOException) { /* berkas sementara masih dipegang proses lain; biarkan */ }
                    catch (UnauthorizedAccessException) { /* tanpa izin hapus; biarkan untuk pemeriksaan manual */ }
                }
            }
        }

        private static IEnumerable<string> Kandidat(
            string jalurAktif, IEnumerable<string>? kandidatTambahan)
        {
            var kandidat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Tambah(string? jalur)
            {
                if (string.IsNullOrWhiteSpace(jalur)) return;
                try
                {
                    if (File.Exists(jalur)) kandidat.Add(Path.GetFullPath(jalur));
                }
                catch (Exception)
                {
                    // Jalur dari konfigurasi/preferensi bisa tidak sah; kandidat
                    // lain masih layak dicoba.
                }
            }

            Tambah(jalurAktif);
            Tambah(Path.Combine(AppContext.BaseDirectory, "desa.db"));
            Tambah(Path.Combine(AppContext.BaseDirectory, "Database", "desa.db"));

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Tambah(Path.Combine(local, "Programs", "SuDesApp", "desa.db"));
            Tambah(Path.Combine(local, "SuDesApp", "desa.db"));

            try
            {
                var folder = Path.GetDirectoryName(jalurAktif);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    // Cadangan yang dibuat sebelum impor/pemulihan adalah kandidat
                    // yang paling mungkin masih sehat setelah database aktif rusak.
                    foreach (var berkas in Directory.EnumerateFiles(folder, "desa*.db")
                                 .OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        Tambah(berkas);
                    }
                }
            }
            catch (Exception)
            {
                // Folder tidak terbaca: kandidat lain tetap dipakai.
            }

            if (kandidatTambahan != null)
            {
                foreach (var jalur in kandidatTambahan) Tambah(jalur);
            }

            return kandidat;
        }

        private static string PindahkanKeSamping(string jalur)
        {
            var cap = jalur + ".tidak-terbaca-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            var nomor = 1;
            while (File.Exists(cap) || File.Exists(cap + "-wal") || File.Exists(cap + "-shm"))
            {
                cap = jalur + ".tidak-terbaca-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + nomor++;
            }

            File.Move(jalur, cap);

            foreach (var akhiran in new[] { "-wal", "-shm", "-journal" })
            {
                if (!File.Exists(jalur + akhiran)) continue;
                try { File.Move(jalur + akhiran, cap + akhiran); }
                catch (IOException) { /* sidecar mungkin sudah hilang/terkunci; database utama tetap aman */ }
                catch (UnauthorizedAccessException) { /* sidecar tanpa izin dipindahkan; database utama tetap aman */ }
            }

            SqliteConnection.ClearAllPools();
            return cap;
        }

        private static void HapusSidecar(string jalur)
        {
            foreach (var akhiran in new[] { "-wal", "-shm", "-journal" })
            {
                var berkas = jalur + akhiran;
                if (!File.Exists(berkas)) continue;
                try { File.Delete(berkas); }
                catch (IOException) { /* berkas sementara masih dipakai; biarkan */ }
                catch (UnauthorizedAccessException) { /* tanpa izin hapus; biarkan */ }
            }
        }

        private static string? BacaKunci(string connectionString)
        {
            try
            {
                var kunci = new SqliteConnectionStringBuilder(connectionString).Password;
                return string.IsNullOrWhiteSpace(kunci) ? null : kunci;
            }
            catch (Exception)
            {
                // Connection string yang rusak tidak boleh membuat pemulihan
                // berhenti; database tujuan akan dibuat sebagai SQLite biasa.
                return null;
            }
        }

        private static string SusunKoneksi(string jalur, string? kunci)
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = jalur };
            if (!string.IsNullOrWhiteSpace(kunci)) builder.Password = kunci;
            return builder.ToString();
        }

        private static HasilPemulihanDatabase Gagal(string pesan) =>
            new(false, false, null, null, null, pesan);
    }
}
