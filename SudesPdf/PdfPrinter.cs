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
    /// <summary>
    /// Batas atas skala raster. Printer 600 dpi butuh 8,3× halaman dalam point;
    /// di atas ±1150 dpi rasternya hanya menghabiskan memori tanpa tambahan ketajaman.
    /// </summary>
    private const double SkalaMaksimal = 16.0;

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

            // Raster dibuat persis sebanyak piksel printer untuk kotak cetak, lalu
            // digambar 1:1. Dulu halaman selalu diraster 300 dpi lalu di-bicubic ke
            // margin, sehingga seluruh huruf melewati penskalaan kedua.
            double dpiX = Math.Max(72.0, graphics.DpiX);
            double dpiY = Math.Max(72.0, graphics.DpiY);
            RectangleF margin = args.MarginBounds;
            double lebarKotakPx = margin.Width / 100.0 * dpiX;
            double tinggiKotakPx = margin.Height / 100.0 * dpiY;
            double skala = Math.Min(
                lebarKotakPx / Math.Max(1.0, ukuranHalaman.Width),
                tinggiKotakPx / Math.Max(1.0, ukuranHalaman.Height));
            skala = Math.Clamp(skala, 0.05, SkalaMaksimal);

            using var bitmap = ToBitmap(document.Render(currentPage - 1, skala));

            var satuanAsal = graphics.PageUnit;
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int kiri = (int)Math.Round(margin.Left / 100.0 * dpiX + (lebarKotakPx - bitmap.Width) / 2.0);
            int atas = (int)Math.Round(margin.Top / 100.0 * dpiY + (tinggiKotakPx - bitmap.Height) / 2.0);
            graphics.DrawImage(bitmap, new Rectangle(kiri, atas, bitmap.Width, bitmap.Height));
            graphics.PageUnit = satuanAsal;

            currentPage++;
            args.HasMorePages = currentPage <= lastPage;
        };

        printDocument.Print();
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
