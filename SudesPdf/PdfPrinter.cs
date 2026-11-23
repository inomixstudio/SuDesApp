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
/// Dialog cetak klasik Windows untuk dokumen SudesPdf: pengguna memilih printer,
/// rentang halaman, dan jumlah salinan. Setiap halaman dirasterkan saat diminta
/// printer agar dokumen panjang tidak ditahan sekaligus di memori.
/// </summary>
internal static class PdfPrinter
{
    public static void ShowDialogAndPrint(Document document, string documentName)
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
        if (result != System.Windows.Forms.DialogResult.OK) return;

        printDocument.PrinterSettings = dialog.PrinterSettings;
        PasangUkuranKertas(printDocument, document);
        int firstPage = printDocument.PrinterSettings.PrintRange == PrintRange.SomePages
            ? Math.Clamp(printDocument.PrinterSettings.FromPage, 1, pageCount)
            : 1;
        int lastPage = printDocument.PrinterSettings.PrintRange == PrintRange.SomePages
            ? Math.Clamp(printDocument.PrinterSettings.ToPage, firstPage, pageCount)
            : pageCount;
        int currentPage = firstPage;

        printDocument.PrintPage += (_, args) =>
        {
            Graphics graphics = args.Graphics!;
            PageSize ukuranHalaman = document.PageSizes[currentPage - 1];

            double dpiX = Math.Max(72.0, graphics.DpiX);
            double dpiY = Math.Max(72.0, graphics.DpiY);
            var satuanAsal = graphics.PageUnit;
            graphics.PageUnit = GraphicsUnit.Pixel;

            // Yang dipakai adalah LEMBAR KERTAS (PageSettings.PaperSize) dan margin
            // kerasnya, bukan MarginBounds. MarginBounds adalah kotak tinta: memakai
            // kotak itu sebagai acuan membuat seluruh halaman ikut menyusut seukuran
            // margin printer - gejala yang dilaporkan pengguna: hasil cetak sedikit
            // lebih kecil dari kertas aslinya.
            PageSettings setelan = args.PageSettings;
            double lebarLembarIn = setelan.PaperSize.Width / 100.0;
            double tinggiLembarIn = setelan.PaperSize.Height / 100.0;
            if (setelan.Landscape) (lebarLembarIn, tinggiLembarIn) = (tinggiLembarIn, lebarLembarIn);
            RectangleF kotakTinta = graphics.VisibleClipBounds;

            var kertas = new PdfPrintLayout.Kertas(
                lebarLembarIn,
                tinggiLembarIn,
                setelan.HardMarginX / 100.0,
                setelan.HardMarginY / 100.0,
                kotakTinta.Width,
                kotakTinta.Height,
                dpiX,
                dpiY);

            PdfPrintLayout.Hasil letak = PdfPrintLayout.Hitung(
                ukuranHalaman.Width,
                ukuranHalaman.Height,
                kertas);

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
    }

    /// <summary>
    /// Pilih ukuran kertas driver yang paling dekat dengan halaman PDF supaya surat F4
    /// tidak dipaksa masuk kertas A4 (dan sebaliknya). Kalau driver tidak punya
    /// ukuran itu, orientasi saja yang mengikuti halaman; PdfPrintLayout tetap menjaga
    /// halaman tidak keluar lembar dengan memperkecil secukupnya.
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

            PaperSize? cocok = PdfPrintLayout.CariUkuranKertas(
                printDocument.PrinterSettings.PaperSizes.Cast<PaperSize>(),
                halaman.Width / 72.0,
                halaman.Height / 72.0);

            PageSettings setelan = printDocument.DefaultPageSettings;
            setelan.Landscape = cocok is null
                ? halaman.Width > halaman.Height
                : PdfPrintLayout.MiringUntuk(cocok, halaman.Width, halaman.Height);
            if (cocok is not null) setelan.PaperSize = cocok;
        }
        catch (Exception eks)
        {
            // Driver yang menolak perubahan ukuran kertas tidak boleh membatalkan
            // pencetakan: penyesuaian skala di PrintPage tetap menjaga isinya utuh.
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
