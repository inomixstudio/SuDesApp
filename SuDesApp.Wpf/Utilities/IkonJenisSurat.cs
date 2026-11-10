using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Pemetaan jenis surat → glyph ikon (IkonMenu). Satu sumber untuk sidebar
    /// (FeatureNavigationViewModel) dan pintasan Beranda supaya ikon kedua
    /// tempat tidak bisa saling berbeda; jenis di luar daftar jatuh ke ikon
    /// blanko generik.
    /// </summary>
    public static class IkonJenisSurat
    {
        public static string UntukJenis(string jenis) => jenis switch
        {
            SuratConstants.SKD_UMUM => IkonMenu.Dokumen,
            SuratConstants.DOMISILI_WARGA or SuratConstants.INSTANSI or SuratConstants.IJIN_TINGGAL => IkonMenu.Beranda,
            SuratConstants.SKU or SuratConstants.GARAPAN_SAWAH or SuratConstants.IZIN_KERAMAIAN => IkonMenu.Perusahaan,
            SuratConstants.SKTM => IkonMenu.Laporan,
            SuratConstants.KEMATIAN => IkonMenu.Kalender,
            SuratConstants.KENAL_LAHIR => IkonMenu.Posyandu,
            SuratConstants.AHLI_WARIS => IkonMenu.Komentar,
            SuratConstants.SK_PINDAH => IkonMenu.SuratKeluar,
            _ => IkonMenu.Blanko
        };
    }
}
