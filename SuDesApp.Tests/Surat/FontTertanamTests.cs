using System.Text;
using SudesPdf.Core;
using SudesPdf.Core.Layout;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Berkas surat harus MEMUAT Times New Roman, bukan sekadar menyebut nama
/// base-14 Times yang setiap pembaca PDF ganti dengan font serif lain (bentuk
/// hurufnya bisa lebih tipis dan tampak tidak jernih). Uji ini menjaga subset
/// TrueType benar-benar tersemat, teksnya tetap terbaca, dan hasilnya tidak
/// membengkakkan berkas.
/// </summary>
public sealed class FontTertanamTests
{
    [Fact]
    public void Pdf_surat_memuat_subset_times_new_roman()
    {
        byte[] pdf = BuatPdf("Surat Keterangan Desa Sukamaju — H. Ahmad Suryana");
        string isi = Encoding.Latin1.GetString(pdf);

        Assert.Contains("/Subtype /TrueType", isi, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", isi, StringComparison.Ordinal);
        Assert.Contains("TimesNewRomanPSMT", isi, StringComparison.Ordinal);

        // Bukan lagi font base-14 yang disubstitusi pembaca.
        Assert.DoesNotContain("/BaseFont /Times-Roman", isi, StringComparison.Ordinal);

        // Subset, bukan seluruh berkas font (1,2 MB per muka).
        Assert.True(pdf.Length < 400_000, $"PDF terlalu besar: {pdf.Length} byte");
    }

    [Fact]
    public void Teks_tetap_terbaca_dari_pdf_bertimes_tersemat()
    {
        byte[] pdf = BuatPdf("Surat Keterangan Tidak Mampu");

        var dokumen = PdfDocument.Load(pdf);
        string terbaca = PdfRenderer.ExtractText(dokumen.Pages[0]);

        Assert.Contains("Surat Keterangan", terbaca, StringComparison.Ordinal);
    }

    [Fact]
    public void Halaman_bertimes_tersemat_masih_tergambar()
    {
        byte[] pdf = BuatPdf("BUDI SANTOSO");
        var dokumen = PdfDocument.Load(pdf);
        var raster = PdfRenderer.RenderPage(dokumen, 0, 2.0);

        int gelap = 0;
        for (int i = 0; i + 3 < raster.Pixels.Length; i += 4)
        {
            // Piksel BGRA: merah < 100 berarti tinta huruf.
            if (raster.Pixels[i + 2] < 100) gelap++;
        }

        Assert.True(gelap > 200, "Teks Times yang tersemat tidak tergambar di halaman.");
    }

    [Fact]
    public void Muka_tebal_dan_miring_disematkan_terpisah()
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(PageSizes.A4);
                halaman.Margin(40);
                halaman.Content().Column(kolom =>
                {
                    kolom.Item().Text(t =>
                    {
                        // Blok teks campuran: ukuran ditulis pada tiap potongan
                        // karena Text(Action<...>) sendiri tidak mengembalikan
                        // deskriptor yang bisa diatur.
                        t.Span("Tebal ").Bold().FontSize(12);
                        t.Span("miring ").Italic().FontSize(12);
                        t.Span("biasa").FontSize(12);
                    });
                });
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);
        string isi = Encoding.Latin1.GetString(aliran.ToArray());

        Assert.Contains("TimesNewRomanPS-BoldMT", isi, StringComparison.Ordinal);
        Assert.Contains("TimesNewRomanPS-ItalicMT", isi, StringComparison.Ordinal);

        // Roman ikut karena satu baris memuat teks biasa.
        Assert.Contains("TimesNewRomanPSMT", isi, StringComparison.Ordinal);
    }

    private static byte[] BuatPdf(string teks)
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(PageSizes.A4);
                halaman.Margin(40);
                halaman.Content().Column(kolom =>
                {
                    kolom.Item().Text(teks).FontSize(12);
                });
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);
        return aliran.ToArray();
    }
}
