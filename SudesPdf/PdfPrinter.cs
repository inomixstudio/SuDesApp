using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SudesPdf;

/// <summary>
/// Hasil satu kali cetak: apakah job jalan, sekecil apa surat diperkecil, dan
/// peringatan bila ukuran kertas aplikasi tidak bisa dipaksakan ke printer.
/// </summary>
public sealed class HasilCetak
{
    public bool Dicetak { get; init; }

    /// <summary>1,0 = tercetak persis sebesar kertas aslinya.</summary>
    public double SkalaFisikTerendah { get; init; } = 1.0;

    /// <summary>Ukuran halaman PDF yang diminta aplikasi, mis. "215 x 330 mm".</summary>
    public string? UkuranDiajukan { get; init; }

    /// <summary>Ukuran lembar yang benar-benar dipakai driver saat cetak.</summary>
    public string? UkuranLembar { get; init; }

    /// <summary>Ditampilkan ke pengguna; null berarti tidak ada yang perlu diberitahu.</summary>
    public string? Peringatan { get; init; }

    internal static HasilCetak Dibatalkan { get; } = new() { Dicetak = false };
}

/// <summary>
/// Dialog cetak klasik Windows untuk dokumen SudesPdf: pengguna memilih printer,
/// rentang halaman, dan jumlah salinan. Setiap halaman dirasterkan saat diminta
/// printer agar dokumen panjang tidak ditahan sekaligus di memori.
/// </summary>
internal static class PdfPrinter
{
    public static HasilCetak ShowDialogAndPrint(Document document, string documentName)
    {
        using var printDocument = new PrintDocument { DocumentName = documentName };
        using var dialog = new System.Windows.Forms.PrintDialog
        {
            Document = printDocument,
            AllowSomePages = true,
            AllowSelection = false,
            AllowCurrentPage = false,
            UseEXDialog = false
        };

        int pageCount = document.PageCount;
        dialog.PrinterSettings.MinimumPage = 1;
        dialog.PrinterSettings.MaximumPage = pageCount;
        dialog.PrinterSettings.FromPage = 1;
        dialog.PrinterSettings.ToPage = pageCount;

        var mainWindow = System.Windows.Application.Current?.MainWindow;
        IntPtr ownerHandle = mainWindow is null
            ? IntPtr.Zero
            : new WindowInteropHelper(mainWindow).EnsureHandle();
        var result = ownerHandle == IntPtr.Zero
            ? dialog.ShowDialog()
            : dialog.ShowDialog(new WindowOwner(ownerHandle));
        if (result != System.Windows.Forms.DialogResult.OK) return HasilCetak.Dibatalkan;

        printDocument.PrinterSettings = dialog.PrinterSettings;
        PasangUkuranKertas(printDocument, document);
        PageSize halamanAwal = document.PageSizes[0];
        string diajukan = UbahUkuran(halamanAwal.Width / 72.0, halamanAwal.Height / 72.0);
        int firstPage = printDocument.PrinterSettings.PrintRange == PrintRange.SomePages
            ? Math.Clamp(printDocument.PrinterSettings.FromPage, 1, pageCount)
            : 1;
        int lastPage = printDocument.PrinterSettings.PrintRange == PrintRange.SomePages
            ? Math.Clamp(printDocument.PrinterSettings.ToPage, firstPage, pageCount)
            : pageCount;
        int currentPage = firstPage;
        double skalaTerendah = 1.0;
        string? lembarTernyata = null;
        bool driverMenolakUkuran = false;

        printDocument.PrintPage += (_, args) =>
        {
            Graphics graphics = args.Graphics!;
            PageSize ukuranHalaman = document.PageSizes[currentPage - 1];

            // dpi dibaca SEBELUM GetHdc: Graphics yang HDC-nya sedang dipinjam
            // menolak semua panggilan lain ("Object is currently in use elsewhere").
            double dpiAwalX = graphics.DpiX;
            double dpiAwalY = graphics.DpiY;

            // DC dibaca sebelum PageUnit diubah dan selalu ditutup ReleaseHdc.
            PrintDc.Lembar lembarDc;
            IntPtr hdc = graphics.GetHdc();
            try { lembarDc = PrintDc.Baca(hdc, dpiAwalX, dpiAwalY); }
            finally { graphics.ReleaseHdc(hdc); }

            double dpiX = Math.Max(72.0, lembarDc.DpiX > 0 ? lembarDc.DpiX : dpiAwalX);
            double dpiY = Math.Max(72.0, lembarDc.DpiY > 0 ? lembarDc.DpiY : dpiAwalY);
            var satuanAsal = graphics.PageUnit;
            graphics.PageUnit = GraphicsUnit.Pixel;

            // Yang dipakai adalah LEMBAR KERTAS SEBENARNYA (laporan DC), bukan
            // MarginBounds dan bukan PaperSize yang baru saja diisi aplikasi.
            // MarginBounds = kotak tinta: memakainya sebagai acuan membuat seluruh
            // halaman ikut menyusut seukuran margin printer. PaperSize = gema permintaan
            // aplikasi: driver yang menolak ukuran itu tetap melaporkan angka yang diminta.
            PageSettings setelan = args.PageSettings;
            double lebarDiminta = setelan.PaperSize.Width / 100.0;
            double tinggiDiminta = setelan.PaperSize.Height / 100.0;
            if (setelan.Landscape) (lebarDiminta, tinggiDiminta) = (tinggiDiminta, lebarDiminta);

            double lebarLembarIn = lembarDc.TidakDiketahui ? lebarDiminta : lembarDc.LebarIn;
            double tinggiLembarIn = lembarDc.TidakDiketahui ? tinggiDiminta : lembarDc.TinggiIn;
            double tepiKiriIn = lembarDc.TepiKiriIn > 0 ? lembarDc.TepiKiriIn : setelan.HardMarginX / 100.0;
            double tepiAtasIn = lembarDc.TepiAtasIn > 0 ? lembarDc.TepiAtasIn : setelan.HardMarginY / 100.0;
            RectangleF kotakTinta = graphics.VisibleClipBounds;

            driverMenolakUkuran |= !PdfPrintLayout.LembarSama(
                setelan.PaperSize, lebarLembarIn, tinggiLembarIn, PdfPrintLayout.ToleransiKertasIn);
            lembarTernyata ??= UbahUkuran(lebarLembarIn, tinggiLembarIn);

            var kertas = new PdfPrintLayout.Kertas(
                lebarLembarIn,
                tinggiLembarIn,
                tepiKiriIn,
                tepiAtasIn,
                kotakTinta.Width,
                kotakTinta.Height,
                dpiX,
                dpiY);

            PdfPrintLayout.Hasil letak = PdfPrintLayout.Hitung(
                ukuranHalaman.Width,
                ukuranHalaman.Height,
                kertas);
            skalaTerendah = Math.Min(skalaTerendah, letak.SkalaFisik);

            // Raster dibuat pada resolusi printer untuk kotak tujuannya, lalu digambar
            // 1:1. Bagian halaman yang jatuh di luar kotak tinta tidak dibaca sebagai
            // sumber, jadi tidak ada penskalaan kedua seperti dulu.
            using var bitmap = ToBitmap(document.Render(currentPage - 1, letak.SkalaRaster));
            Rectangle tujuan = letak.Tujuan;
            if (tujuan.Width > 0 && tujuan.Height > 0)
            {
                Rectangle sumber = PdfPrintLayout.SumberUntukTujuan(
                    letak, tujuan, bitmap.Width, bitmap.Height);
                graphics.InterpolationMode =
                    sumber.Width == tujuan.Width && sumber.Height == tujuan.Height
                        ? InterpolationMode.NearestNeighbor
                        : InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    bitmap,
                    tujuan,
                    sumber.X,
                    sumber.Y,
                    sumber.Width,
                    sumber.Height,
                    GraphicsUnit.Pixel);
            }

            graphics.PageUnit = satuanAsal;

            currentPage++;
            args.HasMorePages = currentPage <= lastPage;
        };

        printDocument.Print();
        return BangunHasil(diajukan, lembarTernyata, skalaTerendah, driverMenolakUkuran);
    }

    /// <summary>
    /// Bandingkan lembar yang benar-benar dipakai driver dengan ukuran yang diminta
    /// aplikasi. Selama surat masih tercetak 1:1 (lembar driver justru lebih besar) tidak
    /// ada yang perlu dikatakan; peringatan muncul ketika isi harus diperkecil, baik
    /// karena kertasnya memang lebih kecil maupun karena driver menolak formulir kustom.
    /// </summary>
    private static HasilCetak BangunHasil(
        string ukuranDiajukan,
        string? lembarTernyata,
        double skalaTerendah,
        bool driverMenolakUkuran)
    {
        string? peringatan = null;
        if (skalaTerendah < 0.995)
        {
            string sebab = driverMenolakUkuran
                ? $"printer menolak ukuran itu dan kembali ke lembar {lembarTernyata ?? "ukuran bawaan printer"}"
                : $"lembar yang tersedia hanya {lembarTernyata ?? "ukuran bawaan printer"}";

            peringatan =
                $"Surat berukuran {ukuranDiajukan}, tetapi {sebab} sehingga hasil cetak " +
                $"hanya {Math.Round(skalaTerendah * 100)}% dari ukuran aslinya. Ganti kertas " +
                "di printer, atau tambahkan formulir kertas dengan ukuran itu di driver " +
                "printer supaya hasilnya pas.";
        }

        return new HasilCetak
        {
            Dicetak = true,
            SkalaFisikTerendah = skalaTerendah,
            UkuranDiajukan = ukuranDiajukan,
            UkuranLembar = lembarTernyata,
            Peringatan = peringatan,
        };
    }

    /// <summary>Ukuran inci jadi label mm, mis. "215 x 330 mm".</summary>
    private static string UbahUkuran(double lebarIn, double tinggiIn)
    {
        int mmLebar = (int)Math.Round(lebarIn * 25.4);
        int mmTinggi = (int)Math.Round(tinggiIn * 25.4);
        int besar = Math.Max(mmLebar, mmTinggi);
        int kecil = Math.Min(mmLebar, mmTinggi);
        return $"{kecil} x {besar} mm";
    }

    /// <summary>
    /// Paksa ukuran kertas HALAMAN PDF ke printer. Pertama cari padanan di daftar
    /// driver (F4 215x330mm cocok dengan "Folio 8,5x13"); kalau driver tidak punya
    /// yang sedekat itu, kirim formulir kustom dengan ukuran halaman persis supaya
    /// printer yang menyetel kertasnya, bukan suratnya yang dikecilkan.
    /// </summary>
    /// <remarks>
    /// Diubah SESUDAH dialog: pengguna masih bisa memilih kertas lain di Preferences,
    /// tapi pilihan itu sering hanya mengikuti bawaan driver sementara dokumen sudah
    /// punya ukuran halaman yang pasti. Perubahan ukuran kertas dilakukan pada
    /// DefaultPageSettings sebelum Print() karena devmode dibuat sekali untuk seluruh
    /// job - mengubahnya per halaman tidak dijamin dipatuhi driver.
    /// </remarks>
    private static void PasangUkuranKertas(PrintDocument printDocument, Document document)
    {
        try
        {
            if (document.PageCount == 0) return;
            PageSize halaman = document.PageSizes[0];
            if (halaman.Width <= 0 || halaman.Height <= 0) return;

            double lebarIn = halaman.Width / 72.0;
            double tinggiIn = halaman.Height / 72.0;
            PaperSize? cocok = PdfPrintLayout.CariUkuranKertas(
                printDocument.PrinterSettings.PaperSizes.Cast<PaperSize>(),
                lebarIn,
                tinggiIn) ?? PdfPrintLayout.BuatKertasKustom(lebarIn, tinggiIn);

            PageSettings setelan = printDocument.DefaultPageSettings;
            setelan.Landscape = cocok is null
                ? halaman.Width > halaman.Height
                : PdfPrintLayout.MiringUntuk(cocok, halaman.Width, halaman.Height);
            if (cocok is not null) setelan.PaperSize = cocok;
        }
        catch (Exception eks)
        {
            // Driver yang menolak perubahan ukuran kertas tidak boleh membatalkan
            // pencetakan: penyesuaian skala di PrintPage tetap menjaga isinya utuh
            // dan BangunHasil memberi tahu pengguna bahwa hasilnya tidak 1:1.
            System.Diagnostics.Debug.WriteLine($"PasangUkuranKertas dilewati: {eks.Message}");
        }
    }

    private static Bitmap ToBitmap(Core.RasterImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < image.Height; y++)
            {
                IntPtr row = IntPtr.Add(data.Scan0, y * data.Stride);
                Marshal.Copy(image.Pixels, y * image.Width * 4, row, image.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private sealed class WindowOwner : System.Windows.Forms.IWin32Window
    {
        public WindowOwner(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}
