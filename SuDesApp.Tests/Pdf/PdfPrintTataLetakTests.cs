using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using SudesPdf;
using Xunit;

namespace SuDesApp.Tests.Pdf;

/// <summary>
/// Ukuran kertas pada jalur CETAK (PdfPrinter → PdfPrintLayout). Aturan yang
/// dijaga: surat harus keluar pada ukuran kertas aslinya, bukan "sekecil mungkin
/// agar muat di area tinta printer".
///
/// <para>Dulu halaman dipaskan ke <c>MarginBounds</c> (kotak tinta). Karena setiap
/// printer punya margin keras 4-12 mm per sisi, seluruh halaman ikut menyusut
/// seukuran margin itu — gejala yang dilaporkan pengguna: hasil cetak sedikit tidak
/// sama dengan ukuran kertas aslinya. Sekarang acuannya lembar kertas
/// (<c>PaperSize</c> + <c>Landscape</c>), halaman diperkecil hanya bila memang lebih
/// besar dari kertas, dan posisinya dipusatkan pada lembar, bukan pada kotak tinta.</para>
/// </summary>
public sealed class PdfPrintTataLetakTests
{
    private const double PtPerIn = 72.0;

    // A4 = 210 x 297 mm; F4 Indonesia = 215 x 330 mm.
    private const double A4LebarPt = 595.276;
    private const double A4TinggiPt = 841.89;
    private const double F4LebarPt = 609.45;
    private const double F4TinggiPt = 935.43;

    private static double In(double mm) => mm / 25.4;
    private const double Dpi = 600.0;

    private static PdfPrintLayout.Kertas Kertas(
        double lebarIn,
        double tinggiIn,
        double tepiKiriIn = 0.2,
        double tepiAtasIn = 0.2,
        double tepiBawahIn = 0.4,
        double dpiX = Dpi,
        double? dpiY = null) =>
        new(
            lebarIn,
            tinggiIn,
            tepiKiriIn,
            tepiAtasIn,
            // Kotak tinta = lembar dikurangi margin keras; kanan dipakai 2x tepiKiri
            // dan bawah tepiAtas + tepiBawah supaya perhitungannya menyerupai printer
            // nyata (asimetris hanya pada arah vertikal).
            (lebarIn - 2 * tepiKiriIn) * dpiX,
            (tinggiIn - tepiAtasIn - tepiBawahIn) * (dpiY ?? dpiX),
            dpiX,
            dpiY ?? dpiX);

    [Fact]
    public void A4PadaKertasA4DicetakPadaUkuranSebenarnya()
    {
        var letak = PdfPrintLayout.Hitung(
            A4LebarPt, A4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn));

        Assert.Equal(1.0, letak.SkalaFisik, 6);

        int lebarSeharusnya = (int)Math.Round(A4LebarPt / PtPerIn * Dpi);
        int tinggiSeharusnya = (int)Math.Round(A4TinggiPt / PtPerIn * Dpi);
        Assert.Equal(lebarSeharusnya, letak.HalamanLebarPx);
        Assert.Equal(tinggiSeharusnya, letak.HalamanTinggiPx);
    }

    [Fact]
    public void KotakTintaPrinterTidakMenyusutkanHalamanLagi()
    {
        // Laser printer representative: margin keras 5 mm sisi/atas, 10 mm bawah.
        var letak = PdfPrintLayout.Hitung(
            A4LebarPt,
            A4TinggiPt,
            Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn, In(5), In(5), In(10)));

        Assert.Equal(1.0, letak.SkalaFisik, 6);

        // Rumus lama membandingkan halaman dengan KOTAK TINTA, jadi hasilnya kecil
        // daripada ini — delta itulah yang dilaporkan sebagai "ukuran kertas tidak pas".
        double kotakLebarIn = A4LebarPt / PtPerIn - 2 * In(5);
        double kotakTinggiIn = A4TinggiPt / PtPerIn - In(5) - In(10);
        double skalaLama = Math.Min(
            kotakLebarIn / (A4LebarPt / PtPerIn),
            kotakTinggiIn / (A4TinggiPt / PtPerIn));
        Assert.True(skalaLama < 0.97, $"skala lama {skalaLama} seharusnya menyusut (inilah bug-nya)");
    }

    [Fact]
    public void HalamanF4PadaKertasA4HanyaDiperkecilSecukupnya()
    {
        var letak = PdfPrintLayout.Hitung(F4LebarPt, F4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn));

        double muatTinggi = (A4TinggiPt / PtPerIn) / (F4TinggiPt / PtPerIn);
        Assert.Equal(Math.Round(muatTinggi, 6), Math.Round(letak.SkalaFisik, 6), 6);
        Assert.True(letak.SkalaFisik is > 0.9 and < 1.0);

        // Tidak boleh ada yang keluar lembar: kotak halaman pas di dalam kertas.
        Assert.True(letak.HalamanTinggiPx <= (int)Math.Round(A4TinggiPt / PtPerIn * Dpi));
        Assert.True(letak.HalamanLebarPx <= (int)Math.Round(A4LebarPt / PtPerIn * Dpi));
    }

    [Fact]
    public void HalamanA4PadaKertasF4TidakPernahDiperbesar()
    {
        var letak = PdfPrintLayout.Hitung(A4LebarPt, A4TinggiPt, Kertas(F4LebarPt / PtPerIn, F4TinggiPt / PtPerIn));

        Assert.Equal(1.0, letak.SkalaFisik, 6);
        Assert.Equal((int)Math.Round(A4LebarPt / PtPerIn * Dpi), letak.HalamanLebarPx);
    }

    [Fact]
    public void HalamanDipusatkanPadaLembarBukanPadaKotakTinta()
    {
        // Kertas F4, halaman A4: sisa lebar dibagi rata pada lembar kertasnya.
        var letak = PdfPrintLayout.Hitung(A4LebarPt, A4TinggiPt, Kertas(F4LebarPt / PtPerIn, F4TinggiPt / PtPerIn, 0.2, 0.2));

        double jedaSeharusnyaPx = (F4LebarPt / PtPerIn * Dpi - letak.HalamanLebarPx) / 2.0;
        double posisiPadaLembar = letak.HalamanX + 0.2 * Dpi;   // graphics → koordinat kertas
        // Pembulatan ke piksel printer: selisih maksimal 1 piksel.
        Assert.True(
            Math.Abs(jedaSeharusnyaPx - posisiPadaLembar) <= 1.5,
            $"halaman tidak terpusat di lembar: seharusnya {jedaSeharusnyaPx:F1}px, nyata {posisiPadaLembar:F1}px");
    }

    [Fact]
    public void PosisiHalamanDiukurDariTepiKertasBukanDariKotakTinta()
    {
        // Kertas = halaman (A4 di A4) dengan margin keras 5 mm: titik (0,0) graphics
        // adalah kiri-atas KOTAK TINTA, jadi halaman harus dimulai tepat 5 mm ke
        // kiri dari situ supaya tepi kertasnya tetap berimpit.
        var letak = PdfPrintLayout.Hitung(
            A4LebarPt, A4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn, In(5), In(5), In(10)));

        double posisiPadaLembarPx = letak.HalamanX + In(5) * Dpi;   // graphics → koordinat kertas
        Assert.True(
            Math.Abs(posisiPadaLembarPx) <= 1.5,
            $"kop bergeser {posisiPadaLembarPx:F1} piksel dari tepi kertas sebenarnya");

        // Rumus lama MENAMBAH margin lagi (x = margin.Left + ...), sehingga posisi
        // ikut bergeser seukuran margin keras. Selisih itu yang tidak boleh kembali.
        Assert.True(letak.HalamanX < 0, "halaman harus mulai dari tepi kertas, bukan tepi kotak tinta");
    }

    [Fact]
    public void BagianDiLuarKotakTintaDipotongBenardanSumbernyaIkutGeser()
    {
        var letak = PdfPrintLayout.Hitung(
            A4LebarPt, A4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn, In(5), In(5), In(10)));

        Rectangle tujuan = letak.Tujuan;
        Rectangle halaman = letak.Halaman;

        // Kotak tinta selalu lebih kecil dari lembar → tepi kiri-atas dan kanan-bawah
        // terpotong, dan bagian raster yang dibaca ikut bergeser (tidak boleh bulat).
        Assert.Equal(0, tujuan.X);
        Assert.Equal(0, tujuan.Y);
        Assert.True(tujuan.Width < halaman.Width, "sisi kanan harus terpotong kotak tinta");
        Assert.True(tujuan.Height < halaman.Height, "sisi bawah harus terpotong kotak tinta");

        int rasterLebar = (int)Math.Round(A4LebarPt * letak.SkalaRaster);
        int rasterTinggi = (int)Math.Round(A4TinggiPt * letak.SkalaRaster);
        Rectangle sumber = PdfPrintLayout.SumberUntukTujuan(letak, tujuan, rasterLebar, rasterTinggi);

        Assert.True(sumber.X > 0, "sisi kiri yang terpotong tidak boleh ikut dibaca");
        Assert.True(sumber.Y > 0, "sisi atas yang terpotong tidak boleh ikut dibaca");
        Assert.InRange(sumber.Width, 1, rasterLebar);
        Assert.InRange(sumber.Height, 1, rasterTinggi);

        // Perbandingan sumber : raster harus sama dengan perbandingan bagian halaman
        // yang dicetak, kalau tidak suratnya bergeser atau melar.
        Assert.Equal(
            (double)(tujuan.X - halaman.X) / halaman.Width,
            (double)sumber.X / rasterLebar,
            3);
        Assert.Equal(
            (double)tujuan.Height / halaman.Height,
            (double)sumber.Height / rasterTinggi,
            3);
    }

    [Fact]
    public void SumberTidakPernahKeluarRaster()
    {
        var letak = PdfPrintLayout.Hitung(A4LebarPt, A4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn));
        Rectangle halaman = letak.Halaman;      // X-nya negatif (menutupi margin keras)
        Rectangle sumber = PdfPrintLayout.SumberUntukTujuan(letak, halaman, 4000, 5600);

        Assert.Equal(0, sumber.X);
        Assert.Equal(0, sumber.Y);
        Assert.True(sumber.Width <= 4000);
        Assert.True(sumber.Height <= 5600);
    }

    [Theory]
    [InlineData(72.0, 1.0)]
    [InlineData(300.0, 4.166667)]
    [InlineData(600.0, 8.333333)]
    [InlineData(1200.0, 16.0)]     // dipangkas: Document.Render menolak skala > 16
    public void SkalaRasterPengertianTinggiPrinterTetapAman(double dpi, double seharusnya)
    {
        var letak = PdfPrintLayout.Hitung(
            A4LebarPt, A4TinggiPt, Kertas(A4LebarPt / PtPerIn, A4TinggiPt / PtPerIn, dpiX: dpi, dpiY: dpi));

        Assert.Equal(seharusnya, letak.SkalaRaster, 2);
        Assert.True(letak.SkalaRaster <= PdfPrintLayout.SkalaRasterMaksimal);
        Assert.True(letak.SkalaRaster > 0);
    }

    private static PaperSize Ukuran(string nama, double lebarMm, double tinggiMm) =>
        new(nama, (int)Math.Round(lebarMm / 25.4 * 100), (int)Math.Round(tinggiMm / 25.4 * 100));

    private static List<PaperSize> DaftarKertas() =>
    [
        Ukuran("Letter", 215.9, 279.4),
        Ukuran("Legal", 215.9, 355.6),
        Ukuran("A4", 210.1, 296.9),
        Ukuran("A3", 296.9, 420.1),
        Ukuran("F4 / Folio", 215.0, 330.0),
    ];

    [Theory]
    [InlineData(A4LebarPt, A4TinggiPt, "A4")]
    [InlineData(F4LebarPt, F4TinggiPt, "F4 / Folio")]
    [InlineData(A4TinggiPt, A4LebarPt, "A4")]          // landscape: orientasi diabaikan
    [InlineData(612.0, 936.0, "F4 / Folio")]             // Folio 8,5 x 13 inci
    public void CariUkuranKertasMemilihYangPalingDekat(double lebarPt, double tinggiPt, string namaSeharusnya)
    {
        PaperSize? pilih = PdfPrintLayout.CariUkuranKertas(
            DaftarKertas(), lebarPt / PtPerIn, tinggiPt / PtPerIn);

        Assert.NotNull(pilih);
        Assert.Equal(namaSeharusnya, pilih!.PaperName);
    }

    [Fact]
    public void CariUkuranKertasMenolakKertasYangTidakAdaPadanannya()
    {
        // 9 x 15 inci tidak ada di daftar mana pun (bukan sekadar beda tipis).
        Assert.Null(PdfPrintLayout.CariUkuranKertas(DaftarKertas(), 9.0, 15.0));
        Assert.Null(PdfPrintLayout.CariUkuranKertas(null, 8.27, 11.69));
        Assert.Null(PdfPrintLayout.CariUkuranKertas(DaftarKertas(), 0, 11.69));
        Assert.Null(PdfPrintLayout.CariUkuranKertas([new PaperSize("Kosong", 0, 0)], 8.27, 11.69));
    }

    [Theory]
    [InlineData(210.1, 296.9, A4LebarPt, A4TinggiPt, false)]     // potret di atas kertas potret
    [InlineData(210.1, 296.9, A4TinggiPt, A4LebarPt, true)]      // landscape di atas kertas potret
    [InlineData(296.9, 210.1, A4TinggiPt, A4LebarPt, false)]     // landscape di atas kertas mendatar
    public void MiringUntukMenyesuaikanOrientasiKertas(
        double kertasLebarMm, double kertasTinggiMm, double halamanLebarPt, double halamanTinggiPt, bool seharusnya)
    {
        var ukuran = Ukuran("X", kertasLebarMm, kertasTinggiMm);
        Assert.Equal(
            seharusnya,
            PdfPrintLayout.MiringUntuk(ukuran, halamanLebarPt, halamanTinggiPt));
    }

    [Fact]
    public void InputRusakTidakMelemparDanSkalaTetapAman()
    {
        var letak = PdfPrintLayout.Hitung(0, 0, default);
        Assert.True(double.IsFinite(letak.SkalaFisik));
        Assert.InRange(letak.SkalaFisik, 0.01, 1.0);
        Assert.InRange(letak.SkalaRaster, PdfPrintLayout.SkalaRasterMinimal, PdfPrintLayout.SkalaRasterMaksimal);
        Assert.True(letak.HalamanLebarPx > 0);
        Assert.True(letak.HalamanTinggiPx > 0);
    }
}
