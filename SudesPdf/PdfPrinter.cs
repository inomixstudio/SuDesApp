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
    private const double RenderScale = 300.0 / 72.0;

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
            using var bitmap = ToBitmap(document.Render(currentPage - 1, RenderScale));
            RectangleF target = FitPageInside(bitmap, args.MarginBounds);

            Graphics graphics = args.Graphics!;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(bitmap, target);

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

    private static RectangleF FitPageInside(Bitmap bitmap, Rectangle bounds)
    {
        float scale = Math.Min((float)bounds.Width / bitmap.Width, (float)bounds.Height / bitmap.Height);
        float width = bitmap.Width * scale;
        float height = bitmap.Height * scale;
        return new RectangleF(
            bounds.Left + (bounds.Width - width) / 2f,
            bounds.Top + (bounds.Height - height) / 2f,
            width,
            height);
    }

    private sealed class WindowOwner : System.Windows.Forms.IWin32Window
    {
        public WindowOwner(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}
