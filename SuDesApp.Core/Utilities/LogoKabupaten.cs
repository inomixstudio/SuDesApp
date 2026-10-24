using System;
using System.Collections.Generic;
using System.IO;
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
    /// SUMBER GAMBAR: folder lokal (bukan unduhan internet — aplikasi ini sengaja
    /// bisa jalan offline penuh). Berkas diberi nama = kode Kemendagri tingkat
    /// kabupaten/kota (mis. <c>32.15.png</c> untuk Kabupaten Karawang, <c>32.73.png</c>
    /// untuk Kota Bandung), ditaruh di salah satu folder di <see cref="FolderKandidat"/>.
    /// Selama sebuah kabupaten tidak punya berkas, pilihan otomatisnya tidak muncul
    /// dan kop tetap memakai logo desa — tidak ada perubahan diam-diam.
    ///
    /// Ekstensi yang dikenali: .png, .jpg, .jpeg, .bmp (sama seperti dialog ganti logo).
    /// </summary>
    public static class LogoKabupaten
    {
        /// <summary>Ekstensi gambar yang diterima, huruf kecil diawali titik.</summary>
        private static readonly string[] EkstensiGambar = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

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

            return null;
        }

        /// <summary>
        /// Benar bila ada lambang untuk kabupaten ini — dipakai UI untuk menampilkan
        /// pilihan otomatis hanya bila gambarnya benar-benar tersedia.
        /// </summary>
        public static bool Ada(string? kodeKabupaten) => Cari(kodeKabupaten) != null;

        /// <summary>
        /// Semua lambang yang tersedia di seluruh folder kandidat, dipetakan dari kode
        /// kabupaten ke jalur berkasnya. Bila sebuah kode punya berkas di beberapa
        /// folder, yang dipakai adalah yang paling dihormati (folder pengguna menang).
        /// Dipakai UI untuk menampilkan daftar pilihan lambang yang tersedia.
        /// </summary>
        public static IReadOnlyDictionary<string, string> SemuaTersedia()
        {
            var hasil = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Telusuri dari folder PALING AKHIR ke paling awal supaya folder yang
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

            return hasil;
        }
    }
}
