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

    /// <summary>
    /// Gambar pindaian yang tampil kecil harus diturunkan ke resolusi cetak:
    /// rasio tetap, tidak diperbesar, dan tidak menggelapkan tepi transparan
    /// (rata-rata dilakukan dalam bentuk pramultiplied).
    /// </summary>
    [Fact]
    public void GambarBesarDiperkecilTanpaMenggelapkanTepiTransparan()
    {
        var asli = PdfWriterImage.FromFile(CariBerkas(Path.Combine("Resources", "logo.png")));

        // Kotak 60 pt pada 300 dpi = 250 px; aslinya ribuan piksel.
        var kecil = asli.UntukUkuran(60f, 60f * asli.Height / asli.Width);
        Assert.Equal(250, kecil.Width);
        Assert.True(kecil.Height < asli.Height / 4);
        Assert.Equal(
            (double)asli.Width / asli.Height,
            (double)kecil.Width / kecil.Height,
            precision: 2);

        double terangAsli = RataRataTerlihat(asli);
        double terangKecil = RataRataTerlihat(kecil);
        Assert.InRange(terangKecil, terangAsli - 6, terangAsli + 6);

        // Gambar yang sudah cukup kecil tidak diperbesar maupun disalin ulang.
        Assert.Same(asli, asli.UntukUkuran(2000f, 2000f));
    }

    /// <summary>JPEG tetap ditanam apa adanya: isinya belum diurai, jadi tidak bisa diperkecil tanpa kodek.</summary>
    [Fact]
    public void GambarJpegTidakDiperkecil()
    {
        byte[] jpeg = JpegKecil(CariBerkas(Path.Combine("Resources", "logo.png")));
        var gambar = PdfWriterImage.FromBytes(jpeg);

        Assert.True(gambar.Jpeg);
        Assert.Same(gambar, gambar.UntukUkuran(60f, 82f));
    }

    /// <summary>
    /// Pratinjau kop di Pengaturan Surat dicetak mesin PDF yang sama dengan surat:
    /// baris kop, surel, logo, dan lebar kertasnya harus ikut, dalam satu halaman mini.
    /// </summary>
    [Fact]
    public void PratinjauKopMencetakBarisDanLogoDalamSatuHalaman()
    {
        var desa = new Data.Models.DesaData
        {
            NamaDesa = "Sukamaju",
            Kecamatan = "Cikarang",
            Kabupaten = "Bekasi",
            JenisKabupaten = "Kabupaten",
            Alamat = "Jl. Raya No. 1",
            Kodepos = "17530",
            Email = "desa@sukamaju.id",
        };

        byte[] pdf = KopSuratCetak.BuatPdf(
            desa, CariBerkas(Path.Combine("Resources", "logo.png")), SuDesApp.Utilities.UkuranKertasSurat.A4);

        var dokumen = PdfDocument.Load(pdf);
        Assert.Equal(1, dokumen.PageCount);
        Assert.Equal(595.28, dokumen.Pages[0].Width, precision: 1);

        string teks = PdfRenderer.ExtractText(dokumen.Pages[0]);
        foreach (var baris in KopSurat.BarisKop(desa).Where(baris => baris.Teks.Length > 0))
        {
            Assert.Contains(baris.Teks, teks, StringComparison.Ordinal);
        }

        Assert.Contains(KopSurat.SurelKop(desa), teks, StringComparison.Ordinal);
        Assert.True(HitungPikselBerwarna(pdf) > 100, "Logo kop tidak tergambar pada pratinjau.");
    }

    /// <summary>
    /// Gambar yang sama dipakai di dua halaman harus menghasilkan halaman yang
    /// identik. Cache gambar sekarang milik dokumen (bukan per halaman), jadi
    /// salah kunci akan menampilkan gambar halaman lain.
    /// </summary>
    [Fact]
    public void GambarSamaDiDuaHalamanMenghasilkanHalamanIdentik()
    {
        byte[] logo = File.ReadAllBytes(CariBerkas(Path.Combine("Resources", "logo.png")));
        var dokumen = Document.Create(container =>
        {
            for (int i = 0; i < 2; i++)
            {
                container.Page(halaman =>
                {
                    halaman.Size(PageSizes.A4);
                    halaman.Margin(36);
                    halaman.Content().Column(kolom =>
                    {
                        kolom.Item().Width(60).Height(82).Image(logo).FitArea();
                    });
                });
            }
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);

        var mesin = PdfDocument.Load(aliran.ToArray());
        var pertama = PdfRenderer.RenderPage(mesin, 0, 1.0);
        var kedua = PdfRenderer.RenderPage(mesin, 1, 1.0);

        Assert.Equal(pertama.Width, kedua.Width);
        Assert.Equal(pertama.Height, kedua.Height);
        Assert.Equal(pertama.Pixels, kedua.Pixels);
    }

    /// <summary>Rata-rata kecerahan piksel yang benar-benar terlihat (alfa ≥ 128).</summary>
    private static double RataRataTerlihat(PdfWriterImage gambar)
    {
        int komponen = gambar.ColorSpace == "/DeviceGray" ? 1 : 3;
        double jumlah = 0;
        int dihitung = 0;
        for (int i = 0; i < gambar.Width * gambar.Height; i++)
        {
            if (gambar.Alpha is { } alfa && alfa[i] < 128) continue;

            int o = i * komponen;
            jumlah += komponen == 1
                ? gambar.Data[o]
                : (gambar.Data[o] + gambar.Data[o + 1] + gambar.Data[o + 2]) / 3.0;
            dihitung++;
        }

        Assert.True(dihitung > 0, "Gambar tidak punya piksel terlihat.");
        return jumlah / dihitung;
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
