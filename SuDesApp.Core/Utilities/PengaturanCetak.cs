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

        /// <summary>
        /// Kode kabupaten/kota Kemendagri yang lambangnya sedang dipakai, disimpan
        /// terpisah dari jalur berkasnya supaya pilihan tetap dikenali walau berkas
        /// sementara tidak ada (mis. folder belum diisi). Nilai kosong = tidak sedang
        /// memakai lambang kabupaten.
        /// </summary>
        public const string KeyKodeLogoKabupaten = "kodeLogoKabupaten";

        /// <summary>Sumber logo kop yang dipakai pengguna.</summary>
        public enum SumberLogo
        {
            /// <summary>Logo aplikasi/kabupaten mengikuti pengaturan lama (bawaan).</summary>
            Bawaan = 0,

            /// <summary>Logo yang diunggah pengguna sendiri (paling umum: logo desa).</summary>
            Unggahan = 1,

            /// <summary>Lambang kabupaten/kota terpilih dari folder LogosKabupaten.</summary>
            LambangKabupaten = 2,
        }

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

        /// <summary>
        /// Sumber logo kop yang sedang aktif. Bawaan bila belum pernah disetel —
        /// sehingga pengguna lama yang hanya mengunggah logo (tanpa menyentuh pilihan
        /// sumber) tetap melihat logonya seperti biasa, tanpa perubahan diam-diam.
        /// </summary>
        public static SumberLogo GetSumberLogo()
        {
            string nilai = AppPreferenceStore.GetString(KeyJalurLogo, string.Empty) ?? string.Empty;
            // Kunci terpisah untuk sumber; nilai lama tanpa kunci tetap Bawaan.
            string sumber = AppPreferenceStore.GetString("sumberLogoSurat", string.Empty) ?? string.Empty;
            return sumber switch
            {
                "kabupaten" => SumberLogo.LambangKabupaten,
                "unggahan" => SumberLogo.Unggahan,
                _ => SumberLogo.Bawaan,
            };
        }

        public static void SetSumberLogo(SumberLogo sumber)
            => AppPreferenceStore.SetString("sumberLogoSurat", sumber switch
            {
                SumberLogo.LambangKabupaten => "kabupaten",
                SumberLogo.Unggahan => "unggahan",
                _ => string.Empty,
            });

        /// <summary>Kode kabupaten Kemendagri yang lambangnya sedang dipakai (kosong bila tidak).</summary>
        public static string GetKodeLogoKabupaten()
            => AppPreferenceStore.GetString(KeyKodeLogoKabupaten, string.Empty) ?? string.Empty;

        public static void SetKodeLogoKabupaten(string? kode)
            => AppPreferenceStore.SetString(KeyKodeLogoKabupaten, kode ?? string.Empty);

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
        /// Jalur logo yang benar-benar dipakai kop surat, mengikuti sumber yang
        /// dipilih pengguna (lihat <see cref="SumberLogo"/>):
        ///   • LambangKabupaten — lambang kabupaten/kota terpilih; bila berkasnya
        ///     tidak ada, jatuh ke pilihan pengguna lalu bawaan (tidak pernah
        ///     menghasilkan kop tanpa logo hanya karena satu berkas hilang).
        ///   • Unggahan — logo yang diunggah pengguna; bila tidak ada, bawaan.
        ///   • Bawaan — pilihan pengguna bila ada, jika tidak logo bawaan aplikasi.
        /// Null = tidak ada gambar sama sekali (kop tetap dicetak tanpa logo).
        /// </summary>
        public static string? JalurLogoEfektif(string? jalurDariSetelan = null)
        {
            if (GetSumberLogo() == SumberLogo.LambangKabupaten)
            {
                string? lambang = LogoKabupaten.Cari(GetKodeLogoKabupaten());
                if (lambang != null)
                {
                    return lambang;
                }
                // Berkas lambang hilang (folder belum diisi / kode salah): jangan
                // kosongkan kop — turun ke pilihan pengguna seperti biasa.
            }

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
