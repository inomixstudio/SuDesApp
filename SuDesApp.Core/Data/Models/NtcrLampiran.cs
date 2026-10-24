using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Daftar "Bersama ini kami sampaikan surat-surat yang diperlukan untuk diperiksa
    /// sebagai berikut" pada blanko permohonan N2 &amp; N3 — persis seperti template
    /// Kepdirjen Bimas Islam No. 473 Tahun 2020.
    ///
    /// Butir di luar daftar baku sengaja dibiarkan berupa titik-titik (tidak diisi
    /// aplikasi) karena diisi tangan petugas KUA setelah blanko dicetak.
    /// </summary>
    public static class NtcrLampiran
    {
        /// <summary>Baris titik-titik untuk butir lampiran yang dibiarkan kosong.</summary>
        public const string ButirKosong = "..............................";

        /// <summary>
        /// Lampiran blanko N2 (permohonan kehendak nikah): 6 butir baku, lalu butir
        /// 7 &amp; 8 titik-titik.
        /// </summary>
        public static List<string> UntukN2() => new()
        {
            "Surat pengantar nikah dari Desa/Kelurahan",
            "Persetujuan calon mempelai",
            "Fotokopi KTP",
            "Fotokopi akte kelahiran",
            "Fotokopi kartu keluarga",
            "Paspoto 2x3 = 3 lembar berlatar belakang biru",
            ButirKosong,
            ButirKosong
        };

        /// <summary>
        /// Lampiran blanko N3 (permohonan pencatatan isbat): 4 butir baku, lalu 2 butir
        /// titik-titik.
        /// </summary>
        public static List<string> UntukN3() => new()
        {
            "Putusan Isbat",
            "Fotokopi KTP",
            "Fotokopi kartu keluarga",
            "Pasfoto 2x3 = 3 lembar berlatar belakang biru",
            ButirKosong,
            ButirKosong
        };
    }
}
