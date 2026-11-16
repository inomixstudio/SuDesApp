using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Lambang kabupaten/kota yang bisa dipakai sebagai alternatif logo kop surat.
    ///
    /// MODE: pilihan, bukan penggantian (lihat keputusan desain). Logo desa yang
    /// diunggah pengguna TETAP yang utama; lambang kabupaten hanya dipakai bila
    /// pengguna secara tegas memilihnya — tidak pernah mengambil alih kop desa
    /// yang sudah punya logo sendiri.
    ///
    /// SUMBER GAMBAR, berurutan dari yang paling dihormati:
    ///   1. berkas bebas di folder pengguna (%LocalAppData%\SuDesApp\LogosKabupaten,
    ///      bernama kode Kemendagri — mis. 32.15.png); berkas di sini milik pengguna
    ///      dan TIDAK PERNAH ditimpa pembaruan (daftar dilindungi PatchUpdateService);
    ///   2. arsip terbuntel Resources/LambangKabupaten.zip — satu berkas ±20 MB
    ///      berisi seluruh 514 lambang (≤512 px, ≤256 warna) yang dibungkus saat
    ///      build dari folder repo Resources/LogosKabupaten. Bila aplikasi dijalankan
    ///      dari source dan folder LogosKabupaten mentah ada di sampingnya, folder
    ///      itu tetap ikut diperiksa. Isi arsip diekstrak sekali per kode ke folder
    ///      cache pengguna supaya penelepon tetap menerima JALUR BERKAS (pola lama),
    ///      bukan stream — kop, pratinjau, dan UI tidak perlu diubah.
    ///
    /// Selama sebuah kabupaten tidak punya lambang di semua sumber, pilihan
    /// otomatisnya tidak muncul dan kop tetap memakai logo desa — tidak ada
    /// perubahan diam-diam.
    ///
    /// Ekstensi yang dikenali: .png, .jpg, .jpeg, .bmp (sama seperti dialog ganti logo).
    /// </summary>
    public static class LogoKabupaten
    {
        /// <summary>Ekstensi gambar yang diterima, huruf kecil diawali titik.</summary>
        private static readonly string[] EkstensiGambar = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        /// <summary>Nama arsip terbuntel berisi seluruh lambang (bawaan build).</summary>
        public const string NamaArsip = "LambangKabupaten.zip";

        /// <summary>Jalur arsip terbuntel: folder Resources di samping aplikasi.</summary>
        public static string JalurArsip() => JalurArsipOverride
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", NamaArsip);

        /// <summary>Penunjuk arsip untuk pengujian; null = lokasi bawaan.</summary>
        internal static string? JalurArsipOverride { get; set; }

        /// <summary>
        /// Folder cache hasil ekstraksi arsip (di samping data pengguna, TIDAK di
        /// folder LogosKabupaten yang dilindungi — cache boleh dibuang/ditimpa
        /// kapan saja; sumber kebenarannya arsip + folder pengguna).
        /// </summary>
        public static string FolderCacheArsip() => FolderCacheOverride
            ?? Path.Combine(PengaturanCetak.FolderLogoPengguna(), "cache-lambang");

        /// <summary>Penunjuk folder cache untuk pengujian; null = lokasi bawaan.</summary>
        internal static string? FolderCacheOverride { get; set; }

        /// <summary>
        /// Nama berkas dasar = kode kabupaten tanpa titik, supaya aman sebagai nama
        /// berkas di semua sistem berkas (mis. kode "32.15" menjadi berkas "32.15.png";
        /// titik di tengah nama aman, hanya garis-miring yang tidak).
        /// </summary>
        private static string NamaBerkas(string kodeKabupaten) => kodeKabupaten.Trim();

        /// <summary>
        /// Folder tempat pengguna menaruh lambang kabupaten, berurutan dari yang
        /// paling dihormati. Folder pengguna (LocalAppData) didahulukan supaya tambahan
        /// pengguna menang atas berkas bawaan yang ikut terpasang — dan tetap bisa
        /// ditulis walau aplikasi terpasang di folder program yang dilindungi.
        /// </summary>
        public static IEnumerable<string> FolderKandidat()
        {
            if (FolderOverride != null)
            {
                yield return FolderOverride;
                yield break;
            }

            yield return Path.Combine(PengaturanCetak.FolderLogoPengguna(), "LogosKabupaten");
            // Folder repo (source): berkas mentah sengaja tidak lagi diterbangkan
            // ke instalasi, tetapi jalur ini tetap dihormati bila ada.
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LogosKabupaten");
        }

        /// <summary>
        /// Penunjuk folder lambang kabupaten untuk pengujian (dan skenario khusus).
        /// Null = lokasi bawaan (folder pengguna + folder program). Pola sama dengan
        /// <c>AppPreferenceStore.LokasiOverride</c>.
        /// </summary>
        internal static string? FolderOverride { get; set; }

        /// <summary>
        /// Cari berkas lambang untuk sebuah kabupaten/kota berdasarkan kode Kemendagri.
        /// Mengembalikan jalur lengkap yang ada, atau null bila tidak ada berkas —
        /// null berarti "kabupaten ini tidak punya lambang", bukan kesalahan.
        /// Folder pengguna diperiksa lebih dulu; bila kosong, arsip terbuntel
        /// <see cref="NamaArsip"/> dibaca dan entrinya diekstrak ke folder cache.
        /// </summary>
        /// <param name="kodeKabupaten">Kode Kemendagri tingkat kabupaten/kota (mis. "32.15").</param>
        public static string? Cari(string? kodeKabupaten)
        {
            string nama = NamaBerkas(kodeKabupaten ?? string.Empty);
            if (nama.Length == 0)
            {
                return null;
            }

            foreach (string folder in FolderKandidat())
            {
                // Cocokkan lewat enumeration, bukan File.Exists, supaya jalur yang
                // dikembalikan selalu nama berkas SEBENARNYA di disk. File.Exists
                // di Windows menerima huruf besar-kecil sembarangan, sehingga
                // Cari("ab") bisa mengembalikan jalur "ab.png" yang tidak ada
                // padahal berkasnya "AB.png" — jalur itu benar di Windows tapi
                // rusak di sistem berkas yang peka huruf besar-kecil.
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                string? berkas = Directory.EnumerateFiles(folder)
                    .FirstOrDefault(f =>
                        EkstensiGambar.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) &&
                        string.Equals(
                            Path.GetFileName(f), nama + Path.GetExtension(f),
                            StringComparison.OrdinalIgnoreCase));

                if (berkas != null)
                {
                    return berkas;
                }
            }

            return CariDariArsip(nama);
        }

        /// <summary>
        /// Cari lambang di arsip terbuntel: bila entrinya ada, kembalikan jalur
        /// cache-nya — diekstrak sekali ke folder cache supaya permintaan berulang
        /// (tiap render kop) tidak membuka arsip lagi. Dengan <paramref name="ekstrak"/>
        /// = false, hanya jalur yang dihitung tanpa menyentuh disk (dipakai
        /// <see cref="SemuaTersedia"/> untuk DAFTAR; jalur nyata tetap diekstrak oleh
        /// <see cref="Cari"/> saat benar-benar dipakai).
        /// </summary>
        private static string? CariDariArsip(string kode, bool ekstrak = true)
        {
            string jalurArsip = JalurArsip();
            if (!File.Exists(jalurArsip))
            {
                return null;
            }

            string cache = Path.Combine(FolderCacheArsip(), kode + ".png");
            if (File.Exists(cache))
            {
                return cache;
            }

            if (!ekstrak)
            {
                return cache;
            }

            try
            {
                using var arsip = ZipFile.OpenRead(jalurArsip);
                var masuk = arsip.GetEntry(kode + ".png");
                if (masuk == null)
                {
                    return null;
                }

                Directory.CreateDirectory(FolderCacheArsip());
                using var isi = masuk.Open();
                using var keluaran = File.Create(cache);
                isi.CopyTo(keluaran);
                return cache;
            }
            catch (Exception)
            {
                // Arsip rusak/sebagian tertulis: perlakukan sama seperti lambang
                // yang tidak ada — kop jatuh ke logo pengguna/bawaan, bukan galat.
                // Pembersihan cache juga boleh gagal: berkas sampah di cache tidak
                // mengganggu pencarian berikutnya karena arsipnya yang disalin ulang.
                try { if (File.Exists(cache)) File.Delete(cache); } catch (Exception) { /* berkas sampah di cache — abaikan */ }
                return null;
            }
        }

        /// <summary>Benar bila ada lambang untuk kabupaten ini — dipakai UI untuk menampilkan</summary>
        /// <summary>pilihan otomatis hanya bila gambarnya benar-benar tersedia.</summary>
        public static bool Ada(string? kodeKabupaten) => Cari(kodeKabupaten) != null;

        /// <summary>
        /// Semua lambang yang tersedia dari seluruh sumber, dipetakan dari kode
        /// kabupaten ke jalur berkasnya. Urutan kehormatan: folder pengguna >
        /// folder repo > arsip terbuntel — bila satu kode ada di beberapa sumber,
        /// yang lebih dihormati menang. Dipakai UI untuk menampilkan daftar pilihan
        /// lambang yang tersedia.
        /// </summary>
        public static IReadOnlyDictionary<string, string> SemuaTersedia()
        {
            var hasil = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Telusuri folder dari PALING AKHIR ke paling awal supaya folder yang
            // lebih dihormati (pengguna) menimpa isi folder bawaan untuk kode sama.
            foreach (string folder in FolderKandidat().Reverse())
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string berkas in Directory.EnumerateFiles(folder))
                {
                    string ekstensi = Path.GetExtension(berkas).ToLowerInvariant();
                    if (!EkstensiGambar.Contains(ekstensi))
                    {
                        continue;
                    }

                    string kode = Path.GetFileNameWithoutExtension(berkas).Trim();
                    if (kode.Length == 0)
                    {
                        continue;
                    }

                    hasil[kode] = berkas;
                }
            }

            // Arsip terbuntel mengisi kode yang belum punya berkas bebas; entri arsip
            // tidak menimpa hasil folder (folder selalu lebih dihormati).
            string jalurArsip = JalurArsip();
            if (File.Exists(jalurArsip))
            {
                try
                {
                    using var arsip = ZipFile.OpenRead(jalurArsip);
                    foreach (var masuk in arsip.Entries)
                    {
                        string kode = Path.GetFileNameWithoutExtension(masuk.FullName).Trim();
                        if (kode.Length == 0 || !EkstensiGambar.Contains(Path.GetExtension(masuk.FullName)))
                        {
                            continue;
                        }

                        if (!hasil.ContainsKey(kode))
                        {
                            // Tanpa ekstraksi: daftar ini hanya untuk UI memilih kode;
                            // berkas nyata diekstrak Cari() saat kode itu dipakai.
                            hasil[kode] = CariDariArsip(kode, ekstrak: false) ?? string.Empty;
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    // Arsip rusak — daftar tetap berisi isi folder yang valid.
                }
            }

            return hasil;
        }
    }
}
