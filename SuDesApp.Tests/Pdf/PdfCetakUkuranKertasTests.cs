using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;
using SudesPdf;
using SudesPdf.Core.Layout;
using SuDesApp.Utilities;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.Pdf;

/// <summary>
/// Guard bagian kertas yang paling sering salah: apa yang dikirim ke printer ketika
/// daftar kertas driver tidak memuat ukuran dokumen. Aturan produknya — ukuran
/// aplikasi yang dipasang ke printer, bukan surat yang dikecilkan diam-diam.
/// </summary>
public class PdfCetakUkuranKertasTests
{
    private const double PtPerIn = 72.0;
    private const double F4LebarIn = 215.0 / 25.4;   // 8,46"
    private const double F4TinggiIn = 330.0 / 25.4;  // 12,99"

    private static PaperSize Ukuran(string nama, double lebarMm, double tinggiMm) =>
        new(nama, (int)Math.Round(lebarMm / 25.4 * 100), (int)Math.Round(tinggiMm / 25.4 * 100));

    /// <summary>Driver yang hanya kenal kertas kantornya sendiri — tanpa F4.</summary>
    private static List<PaperSize> TanpaF4() =>
    [
        Ukuran("Letter", 215.9, 279.4),
        Ukuran("A4", 210.0, 297.0),
        Ukuran("A3", 297.0, 420.0),
        Ukuran("Legal", 215.9, 355.6),
    ];

    [Fact]
    public void KertasKustomMenyerahkanUkuranHalamanPersis()
    {
        PaperSize? kustom = PdfPrintLayout.BuatKertasKustom(F4LebarIn, F4TinggiIn);

        Assert.NotNull(kustom);
        // Persepuluh inci: 215 mm = 846, 330 mm = 1299. Tidak dibulatkan ke A4/Legal.
        Assert.Equal(846, kustom!.Width);
        Assert.Equal(1299, kustom.Height);
        Assert.Equal("F4 215x330mm", kustom.PaperName);
        Assert.Equal(0, kustom.RawKind); // 0 = DMPAPER_USER, formulir kustom
    }

    [Fact]
    public void NamaKertasKustomPakaiLabelUmumIndonesia()
    {
        Assert.Equal("F4 215x330mm", PdfPrintLayout.NamaKertasKustom(846, 1299));
        Assert.Equal("A4 210x297mm", PdfPrintLayout.NamaKertasKustom(827, 1169));
        Assert.Equal("Folio 216x330mm", PdfPrintLayout.NamaKertasKustom(850, 1300));
        Assert.Equal("250x330mm", PdfPrintLayout.NamaKertasKustom(984, 1299));
    }

    [Fact]
    public void NamaFormulirTidakPernahLebihPanjangDariDevmode()
    {
        // DEVMODE menyimpan dmFormName sebagai WCHAR[32] -> 31 karakter terpakai.
        PaperSize? kustom = PdfPrintLayout.BuatKertasKustom(7.13, 13.78);
        Assert.NotNull(kustom);
        Assert.True(kustom!.PaperName.Length <= 31, $"nama {kustom.PaperName.Length} karakter");
    }

    [Theory]
    [InlineData(0.5, 8.0)]    // terlalu kecil untuk jadi kertas surat
    [InlineData(8.27, 40.0)]  // spanduk, bukan kertas printer kantor
    [InlineData(0, 0)]
    [InlineData(double.NaN, 11.69)]
    public void KertasKustomMenolakUkuranDiLuarJangkauanPrinter(double lebarIn, double tinggiIn)
    {
        Assert.Null(PdfPrintLayout.BuatKertasKustom(lebarIn, tinggiIn));
    }

    [Fact]
    public void UkuranHalamanDicariDiDriverDuluSebelumFormulirKustom()
    {
        // F4 ada di daftar: pakai formulir driver supaya tray/margin driver ikut benar.
        var denganF4 = TanpaF4();
        denganF4.Add(Ukuran("F4", 215.0, 330.0));

        PaperSize? driver = PdfPrintLayout.CariUkuranKertas(denganF4, F4LebarIn, F4TinggiIn);
        Assert.NotNull(driver);
        Assert.Equal("F4", driver!.PaperName);
        Assert.True(PdfPrintLayout.LembarSama(driver, F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));
    }

    [Fact]
    public void DriverTanpaF4JatuhKeFormulirKustomBukanKertasTerdekat()
    {
        Assert.Null(PdfPrintLayout.CariUkuranKertas(TanpaF4(), F4LebarIn, F4TinggiIn));

        PaperSize? kustom = PdfPrintLayout.BuatKertasKustom(F4LebarIn, F4TinggiIn);
        Assert.NotNull(kustom);
        Assert.True(PdfPrintLayout.LembarSama(kustom, F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));

        // Bukan A4 yang "hampir mirip": F4 tidak pernah dipaksa jadi 210x297.
        Assert.False(PdfPrintLayout.LembarSama(Ukuran("A4", 210.0, 297.0), F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));
    }

    [Fact]
    public void LembarSamaTidakPeduliOrientasi()
    {
        PaperSize potret = new("F4 215x330mm", 846, 1299);      // persepuluh inci
        PaperSize mendatar = new("F4 330x215mm", 1299, 846);

        Assert.True(PdfPrintLayout.LembarSama(potret, F4TinggiIn, F4LebarIn, PdfPrintLayout.ToleransiKertasIn));
        Assert.True(PdfPrintLayout.LembarSama(mendatar, F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));
        Assert.False(PdfPrintLayout.LembarSama(null, F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));
        Assert.False(PdfPrintLayout.LembarSama(new PaperSize("Kosong", 0, 0), F4LebarIn, F4TinggiIn, PdfPrintLayout.ToleransiKertasIn));
    }

    /// <summary>
    /// Driver yang menolak formulir kustom tetap menyerahkan surat utuh: tata letak
    /// memakai lembar SEBENARNYA dari DC, jadi isinya diperkecil, bukan dipotong di
    /// kaki halaman. Ini kasus nyata "Microsoft Print to PDF" di mesin pengembangan.
    /// </summary>
    [Fact]
    public void DriverYangTukarKertasTetapMenyerahkanSuratUtuh()
    {
        const double dpi = 600;
        double a4LebarIn = 210.0 / 25.4;
        double a4TinggiIn = 297.0 / 25.4;
        double margin = 0.2;

        var letak = PdfPrintLayout.Hitung(
            609.45,
            935.43,
            new PdfPrintLayout.Kertas(
                a4LebarIn,
                a4TinggiIn,
                margin,
                margin,
                (a4LebarIn - 2 * margin) * dpi,
                (a4TinggiIn - 2 * margin) * dpi,
                dpi,
                dpi));

        Assert.True(letak.SkalaFisik < 0.95, $"harus diperkecil, bukan dipotong (skala {letak.SkalaFisik})");
        Assert.Equal(a4LebarIn, letak.LembarLebarIn, 4);   // lembar laporan DC yang dipakai
        Assert.Equal(a4TinggiIn, letak.LembarTinggiIn, 4);

        // Diperkecil ke UKURAN LEMBAR, bukan ke kotak tinta: menyusut ke kotak tinta
        // adalah bug lama yang membuat surat ±4% lebih kecil dari kertasnya.
        double lembarTinggiPx = a4TinggiIn * dpi;
        Assert.InRange((double)letak.HalamanTinggiPx, lembarTinggiPx - 2, lembarTinggiPx + 2);

        // Sisi yang hilang paling jauh seukuran margin keras (kosong memang di situ),
        // bukan setengah halaman.
        double marginPx = margin * dpi;
        Assert.True(letak.Halaman.Y >= -marginPx - 1, $"atas terpotong {marginPx - letak.Halaman.Y}px");
        Assert.True(letak.Halaman.Bottom <= letak.KotakTinggiPx + marginPx + 1, "kaki surat terpotong");
        Assert.False(letak.Tujuan.IsEmpty);
    }

    [Fact]
    public void LembarDcTanpaAngkaDianggapTidakDiketahui()
    {
        // Driver "Fax" melapor 0 x 0; PaperSize tetap dipakai supaya tidak bagi nol.
        Assert.True(new PrintDc.Lembar(0, 0, 0, 0, 600, 600).TidakDiketahui);
        Assert.False(new PrintDc.Lembar(8.46, 12.99, 0.2, 0.2, 600, 600).TidakDiketahui);
        Assert.True(PrintDc.Baca(IntPtr.Zero, 600, 600).TidakDiketahui);
    }

    /// <summary>
    /// Guard sumber: PdfPrinter harus mempercayai DC, bukan gema PaperSize, dan HDC
    /// wajib dilepas. Versi lama hanya membaca PaperSize sehingga driver yang
    /// diam-diam berganti kertas membuat kaki surat hilang.
    /// </summary>
    [Fact]
    public void PdfPrinterMemakaiLembarDariDcDanMelepasHdc()
    {
        string sumber = File.ReadAllText(Path.Combine(
            CariAkarRepo(), "SudesPdf", "PdfPrinter.cs"));

        Assert.Contains("graphics.GetHdc()", sumber, StringComparison.Ordinal);
        Assert.Contains("graphics.ReleaseHdc(hdc)", sumber, StringComparison.Ordinal);
        Assert.Contains("finally { graphics.ReleaseHdc(hdc); }", sumber, StringComparison.Ordinal);

        // Graphics yang sedang dipinjam HDC-nya menolak panggilan lain; dpi harus
        // dibaca lebih dulu. Urutan inilah yang membuat cetak crash di printer nyata.
        int bacaDpi = sumber.IndexOf("double dpiAwalX = graphics.DpiX;", StringComparison.Ordinal);
        int pinjamHdc = sumber.IndexOf("graphics.GetHdc()", StringComparison.Ordinal);
        Assert.True(bacaDpi >= 0 && pinjamHdc >= 0 && bacaDpi < pinjamHdc,
            "graphics.DpiX dibaca setelah GetHdc -> GDI+ melempar 'Object is currently in use elsewhere'");
        Assert.DoesNotContain("PrintDc.Baca(hdc, graphics.DpiX", sumber, StringComparison.Ordinal);
        Assert.Contains("lembarDc.TidakDiketahui ? lebarDiminta : lembarDc.LebarIn", sumber, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bukti bahwa "pengaturan aplikasi dipakai di printer": DEVMODE yang akan dikirim
    /// ke driver benar-benar membawa ukuran halaman PDF, bukan bawaan printer.
    ///
    /// Jebakan satuan: PaperSize .NET menyimpan PERSEPULUH INCI, DEVMODE menyimpan
    /// PERSEPULUH MILIMETER dengan dmPaperSize = 256 (DMPAPER_USER). dmFormName
    /// sengaja tidak diperiksa karena .NET mengosongkannya untuk formulir kustom —
    /// driver membaca lebar/tinggi, bukan namanya.
    /// </summary>
    [Fact]
    public void DevmodeMembawaUkuranKertasAplikasi()
    {
        if (PrinterSettings.InstalledPrinters.Count == 0) return; // kontainer tanpa printer

        using var printDocument = new PrintDocument();
        try
        {
            printDocument.PrinterSettings = new PrinterSettings();
            if (!printDocument.PrinterSettings.IsValid) return;

            PaperSize kustom = PdfPrintLayout.BuatKertasKustom(F4LebarIn, F4TinggiIn)!;
            printDocument.DefaultPageSettings.PaperSize = kustom;

            IntPtr devmode = printDocument.PrinterSettings.GetHdevmode(printDocument.DefaultPageSettings);
            Assert.True(CobaBacaDevmode(devmode, out DevmodeIsi isi), "DEVMODE printer bawaan tidak terbaca");

            Assert.True(
                (isi.Fields & (DmPaperSize | DmPaperWidth | DmPaperLength)) == (DmPaperSize | DmPaperWidth | DmPaperLength),
                $"driver tidak disuruh memakai ukuran kertas aplikasi (dmFields=0x{isi.Fields:X})");
            Assert.Equal(DmPaperUser, isi.PaperSize);

            int mmLebar = (int)Math.Round(kustom.Width * 25.4 / 100.0 * 10.0);
            int mmTinggi = (int)Math.Round(kustom.Height * 25.4 / 100.0 * 10.0);
            Assert.Equal(mmLebar, isi.PaperWidth);
            Assert.Equal(mmTinggi, isi.PaperLength);
        }
        catch (DllNotFoundException)
        {
            // System.Drawing.Common tidak tersedia (non-Windows) — tidak ada yang bisa diperiksa.
        }
        catch (InvalidPrinterException)
        {
            // Printer bawaan hilang di tengah tes; driver lain masih dicoba di tes unit murni.
        }
    }

    // ---- DEVMODEW (pack 2): dmSize @68, dmFields DWORD @72, dmPaperSize @78,
    // dmPaperLength @80, dmPaperWidth @82, dmFormName WCHAR[32] @100.
    private const int DmPaperSize = 0x0002;
    private const int DmPaperLength = 0x0004;
    private const int DmPaperWidth = 0x0008;
    private const short DmPaperUser = 256; // DMPAPER_USER: ukuran kustom dari dmPaperWidth/Length

    private readonly record struct DevmodeIsi(int Fields, short PaperSize, short PaperWidth, short PaperLength, string FormName);

    /// <summary>
    /// Satu ukuran kertas untuk tiga tempat: PDF desktop, PDF Android, dan konstanta
    /// PageSizes mesin SudesPdf. Dulu berbeda (607,5 pt = 214,3 mm di generator,
    /// 609,45 pt = 215 mm di mesin) — hasil cetak Android dan desktop tidak lagi
    /// identik dan surat tidak mengisi kertas F4 yang sebenarnya.
    /// </summary>
    [Fact]
    public void UkuranKertasSamaDiDesktopAndroidDanMesinPdf()
    {
        (float a4Lebar, float a4Tinggi) = PengaturanCetak.Dimensi(UkuranKertasSurat.A4);
        (float f4Lebar, float f4Tinggi) = PengaturanCetak.Dimensi(UkuranKertasSurat.F4);

        Assert.Equal(210, Math.Round(a4Lebar * 25.4 / 72.0));
        Assert.Equal(297, Math.Round(a4Tinggi * 25.4 / 72.0));
        Assert.Equal(215, Math.Round(f4Lebar * 25.4 / 72.0));
        Assert.Equal(330, Math.Round(f4Tinggi * 25.4 / 72.0));

        Assert.Equal(PageSizes.A4.Width, a4Lebar, 1);
        Assert.Equal(PageSizes.A4.Height, a4Tinggi, 1);
        Assert.Equal(PageSizes.F4.Width, f4Lebar, 1);
        Assert.Equal(PageSizes.F4.Height, f4Tinggi, 1);

        string suratPdf = File.ReadAllText(Path.Combine(
            CariAkarRepo(), "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android", "SuratPdf.kt"));
        Assert.Equal(AmbilKonstanta(suratPdf, "A4_LEBAR"), a4Lebar, 1);
        Assert.Equal(AmbilKonstanta(suratPdf, "A4_TINGGI"), a4Tinggi, 1);
        Assert.Equal(AmbilKonstanta(suratPdf, "F4_LEBAR"), f4Lebar, 1);
        Assert.Equal(AmbilKonstanta(suratPdf, "F4_TINGGI"), f4Tinggi, 1);
    }

    private static float AmbilKonstanta(string sumber, string nama)
    {
        Match cocok = Regex.Match(sumber, @"const val " + nama + @"\s*=\s*([0-9.]+)f");
        Assert.True(cocok.Success, $"const val {nama} tidak ditemukan di SuratPdf.kt.");
        return float.Parse(cocok.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CariAkarRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo tidak ditemukan.");
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    /// <summary>
    /// GetHdevmode() mengembalikan HGLOBAL, bukan pointer DEVMODE: tanpa GlobalLock
    /// yang terbaca hanya sampah, dan tes seperti ini bisa "lulus" tanpa memeriksa
    /// apa pun.
    /// </summary>
    private static bool CobaBacaDevmode(IntPtr hglobal, out DevmodeIsi isi)
    {
        isi = default;
        if (hglobal == IntPtr.Zero) return false;

        IntPtr alamat = GlobalLock(hglobal);
        if (alamat == IntPtr.Zero) return false;
        try
        {
            int ukuran = Marshal.ReadInt16(alamat, 68); // dmSize, tanpa dmDriverExtra
            if (ukuran < 164) return false;             // harus memuat dmFormName (100..164)

            byte[] bytes = new byte[ukuran];
            Marshal.Copy(alamat, bytes, 0, bytes.Length);

            int fields = BitConverter.ToInt32(bytes, 72);
            short paperSize = BitConverter.ToInt16(bytes, 78);
            short paperLength = BitConverter.ToInt16(bytes, 80);
            short paperWidth = BitConverter.ToInt16(bytes, 82);
            string form = System.Text.Encoding.Unicode.GetString(bytes, 100, 64).Split('\0')[0];

            isi = new DevmodeIsi(fields, paperSize, paperWidth, paperLength, form);
            return true;
        }
        finally
        {
            GlobalUnlock(hglobal);
        }
    }
}
