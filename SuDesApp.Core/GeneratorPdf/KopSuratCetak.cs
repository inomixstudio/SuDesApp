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
        /// Tinggi halaman longgar (point) tempat kop diukur. Halaman sengaja dibuat
        /// jauh lebih tinggi dari kop terpanjang supaya kop tidak pernah terbelah ke
        /// halaman kedua; <see cref="SuDesApp.Wpf.Services.KopSuratPratinjauService"/>
        /// kemudian memangkas rasternya ke batas tinta, sehingga pratinjau tidak
        /// menyisakan pita kertas kosong di bawah garis ganda.
        /// </summary>
        private const float TinggiHalaman = 400f;

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
