using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SudesPdf.Core;
using SudesPdf.Core.Layout;
using SuDesApp.GeneratorPdf;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Gambar kop (logo desa) dan lambang Garuda pada SK harus benar-benar tertanam
/// di PDF dan terlihat saat dirender. Keduanya PNG RGBA 8-bit; kesalahan membaca
/// kanal alfa pernah membuat seluruh gambar hilang tanpa pesan galat.
/// </summary>
public sealed class GambarPdfTests
{
    [Fact]
    public void LogoKopTertanamDanTerlihat()
    {
        string jalur = CariBerkas(Path.Combine("Resources", "logo.png"));
        var gambar = PdfWriterImage.FromFile(jalur);

        Assert.True(gambar.Width > 0 && gambar.Height > 0);
        Assert.NotNull(gambar.Alpha);
        Assert.Equal(gambar.Width * gambar.Height, gambar.Alpha!.Length);

        byte[] pdf = BuatPdf(File.ReadAllBytes(jalur));
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.True(HitungPikselBerwarna(pdf) > 100, "Logo tidak tergambar pada halaman PDF.");
    }

    [Fact]
    public void LambangGarudaSkTertanamDanTerlihat()
    {
        byte[]? garuda = GambarBawaan.Garuda();
        Assert.NotNull(garuda);

        byte[] pdf = BuatPdf(garuda!);
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.True(HitungPikselBerwarna(pdf) > 100, "Lambang Garuda tidak tergambar pada halaman PDF.");
    }

    /// <summary>
    /// Logo berformat .jpg disimpan sebagai gambar DCTDecode. Mesin inti tidak
    /// memuat dekoder JPEG sendiri, jadi tanpa kodek WPF gambar ini tidak digambar
    /// sama sekali — kop surat dan SK kehilangan lambangnya tanpa pesan galat.
    /// </summary>
    [Fact]
    public void LogoJpegTertanamDanTerlihat()
    {
        byte[] jpeg = JpegKecil(CariBerkas(Path.Combine("Resources", "logo.png")));
        byte[] pdf = BuatPdf(jpeg);
        Assert.Contains("/DCTDecode", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);

        // Lapisan SudesPdf memasang kodek ini saat Document dipakai; di uji mesin
        // dipasang langsung supaya jalur DCTDecode ikut diuji.
        PdfImages.ExternalCodec ??= new SudesPdf.WicPdfImageCodec();

        Assert.True(HitungPikselBerwarna(pdf) > 100, "Logo JPEG tidak tergambar pada halaman PDF.");
    }

    /// <summary>Logo diperkecil lalu disimpan sebagai JPEG RGB — seperti logo .jpg milik desa.</summary>
    private static byte[] JpegKecil(string jalur)
    {
        using var aliran = File.OpenRead(jalur);
        var asli = BitmapFrame.Create(aliran, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var kecil = new TransformedBitmap(asli, new ScaleTransform(220.0 / asli.PixelWidth, 220.0 / asli.PixelWidth));

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(kecil));
        using var keluaran = new MemoryStream();
        encoder.Save(keluaran);
        return keluaran.ToArray();
    }

    private static byte[] BuatPdf(byte[] gambar)
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(PageSizes.A4);
                halaman.Margin(36);
                halaman.Content().Column(kolom =>
                {
                    kolom.Item().Width(60).Height(82).Image(gambar).FitArea();
                });
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);
        return aliran.ToArray();
    }

    /// <summary>Jumlah piksel berwarna (bukan hitam/putih/abu) pada halaman pertama.</summary>
    private static int HitungPikselBerwarna(byte[] pdf)
    {
        var dokumen = PdfDocument.Load(pdf);
        var raster = PdfRenderer.RenderPage(dokumen, 0, 1.0);
        int jumlah = 0;
        for (int i = 0; i + 3 < raster.Pixels.Length; i += 4)
        {
            byte biru = raster.Pixels[i];
            byte hijau = raster.Pixels[i + 1];
            byte merah = raster.Pixels[i + 2];
            if (Math.Abs(merah - hijau) > 16 || Math.Abs(hijau - biru) > 16) jumlah++;
        }

        return jumlah;
    }

    /// <summary>Berkas dari folder repo (dicari lewat SuDesApp.slnx).</summary>
    private static string CariBerkas(string relatif)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
            {
                string jalur = Path.Combine(folder.FullName, relatif);
                Assert.True(File.Exists(jalur), $"Berkas uji tidak ditemukan: {jalur}");
                return jalur;
            }

            folder = folder.Parent;
        }

        throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }
}
