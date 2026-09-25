using System;
using System.IO;

namespace SuDesApp.Utilities
{
    /// <summary>Ukuran kertas untuk cetak PDF surat.</summary>
    public enum UkuranKertasSurat
    {
        /// <summary>A4 — 210 x 297 mm. Ukuran bawaan aplikasi.</summary>
        A4,

        /// <summary>F4/Folio — 8,5 x 13 inci (215 x 330 mm), ukuran surat kantor desa.</summary>
        F4
    }

    /// <summary>
    /// Pengaturan cetak surat yang berlaku untuk seluruh dokumen: ukuran kertas PDF
    /// dan gambar logo kop. Disimpan di AppPreferenceStore (di luar database) supaya
    /// bisa dibaca sebelum login maupun saat generate PDF, dan langsung berlaku
    /// tanpa perlu memuat ulang aplikasi.
    /// </summary>
    public static class PengaturanCetak
    {
        public const string KeyUkuranKertas = "ukuranKertasSurat";
        public const string KeyJalurLogo = "jalurLogoSurat";

        // Dimensi halaman dalam point (72 point = 1 inci).
        private const float A4Lebar = 595.28f;   // 210 mm
        private const float A4Tinggi = 841.89f;  // 297 mm
        private const float F4Lebar = 607.5f;    // 8,5 inci
        private const float F4Tinggi = 935.4f;   // 13 inci

        /// <summary>Ukuran kertas terpilih; bawaan A4.</summary>
        public static UkuranKertasSurat GetUkuranKertas()
            => string.Equals(AppPreferenceStore.GetString(KeyUkuranKertas, "A4"), "F4", StringComparison.OrdinalIgnoreCase)
                ? UkuranKertasSurat.F4
                : UkuranKertasSurat.A4;

        public static void SetUkuranKertas(UkuranKertasSurat ukuran)
            => AppPreferenceStore.SetString(KeyUkuranKertas, ukuran == UkuranKertasSurat.F4 ? "F4" : "A4");

        /// <summary>Dimensi halaman (point) untuk ukuran kertas terpilih.</summary>
        public static (float Lebar, float Tinggi) Dimensi(bool landscape = false)
            => Dimensi(GetUkuranKertas(), landscape);

        /// <summary>
        /// Dimensi halaman (point) untuk ukuran kertas tertentu. Dipakai juga oleh
        /// pratinjau di layar, supaya perbandingan ukuran huruf terhadap lebar kertas
        /// dihitung dari angka yang sama dengan pencetakan PDF.
        /// </summary>
        public static (float Lebar, float Tinggi) Dimensi(UkuranKertasSurat ukuran, bool landscape = false)
        {
            var (lebar, tinggi) = ukuran == UkuranKertasSurat.F4
                ? (F4Lebar, F4Tinggi)
                : (A4Lebar, A4Tinggi);

            return landscape ? (tinggi, lebar) : (lebar, tinggi);
        }

        /// <summary>Keterangan ukuran untuk ditampilkan di halaman pengaturan.</summary>
        public static string LabelUkuranKertas(UkuranKertasSurat ukuran)
            => ukuran == UkuranKertasSurat.F4 ? "F4 / Folio — 8,5 x 13 inci" : "A4 — 210 x 297 mm";

        /// <summary>Jalur gambar logo pilihan pengguna; kosong bila belum pernah dipilih.</summary>
        public static string GetJalurLogo()
            => AppPreferenceStore.GetString(KeyJalurLogo, string.Empty) ?? string.Empty;

        public static void SetJalurLogo(string? path)
            => AppPreferenceStore.SetString(KeyJalurLogo, path ?? string.Empty);

        /// <summary>Logo bawaan aplikasi (dipakai bila pengguna belum memilih sendiri).</summary>
        public static string JalurLogoBawaan()
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png");

        /// <summary>
        /// Folder penyimpanan logo pilihan pengguna. Dipakai folder data aplikasi
        /// (bukan folder program) supaya tetap bisa menulis walau aplikasi dipasang
        /// di folder yang dilindungi.
        /// </summary>
        public static string FolderLogoPengguna()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp");

        /// <summary>
        /// Jalur logo yang benar-benar dipakai kop surat: pilihan pengguna bila
        /// berkasnya ada, jika tidak logo bawaan aplikasi. Null = tidak ada gambar
        /// (kop tetap dicetak tanpa logo).
        /// </summary>
        public static string? JalurLogoEfektif(string? jalurDariSetelan = null)
        {
            string pilihan = GetJalurLogo();
            if (!string.IsNullOrWhiteSpace(pilihan) && File.Exists(pilihan))
            {
                return pilihan;
            }

            if (!string.IsNullOrWhiteSpace(jalurDariSetelan) && File.Exists(jalurDariSetelan))
            {
                return jalurDariSetelan;
            }

            string bawaan = JalurLogoBawaan();
            return File.Exists(bawaan) ? bawaan : null;
        }

        /// <summary>
        /// Salin gambar logo pilihan pengguna ke folder data aplikasi lalu simpan
        /// jalurnya. Mengembalikan jalur tersimpan, atau null bila gagal.
        /// </summary>
        public static string? SimpanLogoPengguna(string sumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sumber) || !File.Exists(sumber))
                {
                    return null;
                }

                string folder = FolderLogoPengguna();
                Directory.CreateDirectory(folder);

                string tujuan = Path.Combine(folder, "logo-surat" + Path.GetExtension(sumber).ToLowerInvariant());
                File.Copy(sumber, tujuan, overwrite: true);
                SetJalurLogo(tujuan);
                return tujuan;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Kembalikan ke logo bawaan aplikasi (hapus pilihan pengguna).</summary>
        public static void HapusLogoPengguna()
        {
            try
            {
                string pilihan = GetJalurLogo();
                if (!string.IsNullOrWhiteSpace(pilihan) && File.Exists(pilihan))
                {
                    File.Delete(pilihan);
                }
            }
            catch
            {
                // Berkas logo non-kritis — kegagalan menghapus tidak menghalangi.
            }

            SetJalurLogo(string.Empty);
        }
    }
}
