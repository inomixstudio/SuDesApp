using SudesPdf.Core;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Titik aktif penyusun jalur harus berpindah setiap kali garis digambar.
/// Glif huruf memakai pola garis-seri disusul kurva; kalau titik aktifnya basi,
/// kurva itu dihitung dari titik yang salah, melengkung melewati batang huruf,
/// dan bentuk hurufnya jadi bolong/bergerigi di pratinjau.
/// </summary>
public sealed class PembentukJalurTests
{
    [Fact]
    public void Kurva_setelah_garis_mulai_dari_ujung_garis()
    {
        var jalur = new PdfPathBuilder();

        jalur.MoveTo(0, 0);
        jalur.LineTo(0, 100);

        // Kendali berimpit dengan titik awal kurva → kurva ini harus lurus
        // mendatar pada y = 100. Bila titik aktif masih (0, 0), kurvanya
        // melengkung naik dari y = 0 dan uji ini gagal.
        jalur.QuadTo(0, 100, 100, 100);

        var kontur = Assert.Single(jalur.Build().Contours);
        Assert.True(kontur.Count >= 3);
        for (int i = 1; i < kontur.Count; i++)
        {
            Assert.Equal(100, kontur.Y(i), 6);
        }
    }

    [Fact]
    public void Garis_setelah_kurva_berangkat_dari_ujung_kurva()
    {
        var jalur = new PdfPathBuilder();

        jalur.MoveTo(0, 0);
        jalur.QuadTo(0, 100, 100, 100);
        jalur.LineTo(100, 200);

        var kontur = Assert.Single(jalur.Build().Contours);
        int akhir = kontur.Count - 1;
        Assert.Equal(100, kontur.X(akhir), 6);
        Assert.Equal(200, kontur.Y(akhir), 6);
    }
}
