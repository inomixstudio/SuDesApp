using SudesPdf.Core.Layout;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Geometri halaman surat: ukuran kertas, margin, dan gaya teks baku.
    ///
    /// Satu-satunya tempat aturan ini ditulis. Generator surat, template surat, dan
    /// pratinjau kop di Pengaturan Surat memakai angka yang sama — tanpa itu kop bisa
    /// bergeser antara yang terlihat di layar dan yang tercetak di kertas.
    /// </summary>
    public static class TataLetakHalamanSurat
    {
        /// <summary>Margin kiri/kanan halaman (point).</summary>
        public const float MarginSisi = 45f;

        /// <summary>
        /// Siapkan halaman surat: ukuran, margin, dan huruf baku Times New Roman.
        /// Margin atas/bawah mengikuti kerapatan yang sedang aktif, supaya surat yang
        /// isinya panjang tetap muat satu halaman.
        /// </summary>
        public static void Siapkan(
            PageDescriptor page,
            float lebarHalaman,
            float tinggiHalaman,
            float ukuranTeks = SuratRenderer.UkuranTeks)
        {
            var kerapatan = KerapatanSurat.Aktif;

            page.Size(new PageSize(lebarHalaman, tinggiHalaman, Unit.Point));
            page.MarginTop(kerapatan.MarginAtas, Unit.Point);
            page.MarginRight(MarginSisi, Unit.Point);
            // Sisi bawah diberi ruang supaya tanda tangan tidak menyentuh tepi kertas
            // (area cetak printer umumnya butuh ±20pt).
            page.MarginBottom(kerapatan.MarginBawah, Unit.Point);
            page.MarginLeft(MarginSisi, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(ukuranTeks));
        }
    }
}
