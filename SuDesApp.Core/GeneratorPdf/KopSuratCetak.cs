using SudesPdf.Core;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Kop surat sebagai dokumen PDF sungguhan — dipakai pratinjau di Pengaturan
    /// Surat supaya yang terlihat di layar adalah hasil mesin cetak yang sama,
    /// bukan gambar ulang dengan kode tata letak tersendiri.
    /// </summary>
    public static class KopSuratCetak
    {
        /// <summary>
        /// Tinggi halaman mini (point): cukup untuk kop beserta margin atas/bawah,
        /// jauh lebih pendek dari kertas penuh agar raster pratinjau murah.
        /// </summary>
        private const float TinggiHalaman = 230f;

        /// <summary>
        /// Bangun PDF satu halaman selebar kertas surat berisi kop saja — logo,
        /// baris kop, dan garis ganda pemisah, persis seperti awal setiap surat.
        /// </summary>
        public static byte[] BuatPdf(DesaData? desa, string? logoPath, UkuranKertasSurat ukuranKertas)
        {
            var (lebar, _) = PengaturanCetak.Dimensi(ukuranKertas);

            using var penampung = new MemoryStream();
            Document.Create(container =>
            {
                container.Page(halaman =>
                {
                    TataLetakHalamanSurat.Siapkan(halaman, lebar, TinggiHalaman);

                    halaman.Content().Column(kolom =>
                    {
                        kolom.Item().Element(kop => KopSurat.Gambar(kop, desa, logoPath));
                    });
                });
            }).GeneratePdf(penampung);

            return penampung.ToArray();
        }
    }
}
