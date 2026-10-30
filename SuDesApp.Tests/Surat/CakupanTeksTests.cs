using SudesPdf.Core;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Cakupan huruf dihitung sebagai luas bentuk yang jatuh di tiap piksel
/// (<see cref="Rasterizer.FillTepat"/>), bukan dari beberapa sampel per baris.
/// Uji ini menjaga angka cakupannya tetap benar: pecahan piksel, jumlah tinta
/// yang sesuai luas sebenarnya, dan lubang huruf (mis. "o") tidak ikut terisi.
/// </summary>
public sealed class CakupanTeksTests
{
    [Fact]
    public void Pecahan_piksel_dihitung_sebagai_luas()
    {
        var gambar = new RasterImage(3, 1);
        var jalur = new PdfPathBuilder();
        jalur.AddRectangle(0.25, 0, 0.5, 1);

        var raster = new Rasterizer(gambar);
        raster.FillTepat(jalur.Build(), PdfFillRule.NonZero, 0, 0, 0, 1, PdfBlendMode.Normal, null);

        // Setengah piksel tertutup → abu-abu pertengahan, bukan hitam/putih.
        Assert.InRange(Nilai(gambar, 0, 0), 126, 129);
        Assert.Equal(255, Nilai(gambar, 1, 0));
        Assert.Equal(255, Nilai(gambar, 2, 0));
    }

    [Fact]
    public void Jumlah_tinta_sesuai_luas_segitiga()
    {
        const int sisi = 8;
        var gambar = new RasterImage(sisi, sisi);
        var jalur = new PdfPathBuilder();
        jalur.MoveTo(0, 0);
        jalur.LineTo(sisi, 0);
        jalur.LineTo(0, sisi);
        jalur.Close();

        var raster = new Rasterizer(gambar);
        raster.FillTepat(jalur.Build(), PdfFillRule.NonZero, 0, 0, 0, 1, PdfBlendMode.Normal, null);

        // Luas segitiga = ½ × 8 × 8 = 32 piksel tinta.
        double tinta = 0;
        for (int y = 0; y < sisi; y++)
        {
            for (int x = 0; x < sisi; x++) tinta += (255 - Nilai(gambar, x, y)) / 255.0;
        }

        Assert.InRange(tinta, 31.5, 32.5);
    }

    [Fact]
    public void Lubang_kontur_berlawanan_arah_tetap_kosong()
    {
        var gambar = new RasterImage(6, 6);
        var jalur = new PdfPathBuilder();

        // Kontur luar searah jarum jam.
        jalur.MoveTo(1, 1);
        jalur.LineTo(5, 1);
        jalur.LineTo(5, 5);
        jalur.LineTo(1, 5);
        jalur.Close();

        // Kontur dalam berlawanan arah: harus menjadi lubang.
        jalur.MoveTo(2, 2);
        jalur.LineTo(2, 4);
        jalur.LineTo(4, 4);
        jalur.LineTo(4, 2);
        jalur.Close();

        var raster = new Rasterizer(gambar);
        raster.FillTepat(jalur.Build(), PdfFillRule.NonZero, 0, 0, 0, 1, PdfBlendMode.Normal, null);

        Assert.Equal(255, Nilai(gambar, 3, 3)); // tengah lubang: kertas putih
        Assert.Equal(0, Nilai(gambar, 1, 3));   // dinding luar: tinta penuh
        Assert.Equal(255, Nilai(gambar, 0, 0)); // di luar bentuk: kertas putih
    }

    /// <summary>Nilai kanal merah (0 = hitam, 255 = putih) pada piksel tertentu.</summary>
    private static byte Nilai(RasterImage gambar, int x, int y) =>
        gambar.Pixels[(y * gambar.Width + x) * 4 + 2];
}
