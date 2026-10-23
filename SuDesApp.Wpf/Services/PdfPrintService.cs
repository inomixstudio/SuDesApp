using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using PdfiumViewer;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Layanan cetak PDF — padanan 1:1 alur cetak WinForms (SuratControl.PrintPdfAsync):
    /// menampilkan dialog cetak Windows standar (pilihan printer, rentang halaman,
    /// jumlah salinan) lalu mengirim pekerjaan langsung ke printer via PDFium
    /// PrintDocument. Tidak membuka pratinjau; tekan OK = langsung mencetak.
    /// </summary>
    public class PdfPrintService
    {
        private readonly ILogger<PdfPrintService> _logger;

        public PdfPrintService(ILogger<PdfPrintService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Buka dialog cetak Windows untuk PDF dari file dan kirim ke printer
        /// begitu pengguna menekan OK. Return true bila pencetakan dijalankan.
        /// </summary>
        public Task<bool> PrintPdfFileAsync(string pdfPath, string documentName)
        {
            return PrintPdfCoreAsync(() => PdfDocument.Load(pdfPath), documentName);
        }

        /// <summary>
        /// Buka dialog cetak Windows untuk PDF dari array byte dan kirim ke printer
        /// begitu pengguna menekan OK. Return true bila pencetakan dijalankan.
        /// </summary>
        public Task<bool> PrintPdfBytesAsync(byte[] pdfBytes, string documentName)
        {
            return PrintPdfCoreAsync(() => PdfDocument.Load(new MemoryStream(pdfBytes)), documentName);
        }

        /// <summary>Core: dialog cetak Windows + PDFium PrintDocument, pola WinForms.</summary>
        private async Task<bool> PrintPdfCoreAsync(Func<PdfDocument> createDocument, string documentName)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PdfDocument? document = null;
                try
                {
                    document = createDocument();

                    // Dialog cetak KLASIK Windows (bukan yang modern/experimental —
                    // versi modern tidak menampilkan preview dokumen). UseEXDialog=false
                    // menampilkan dialog klasik dengan status printer & preferensi.
                    using var printDialog = new System.Windows.Forms.PrintDialog
                    {
                        AllowSomePages = true,
                        AllowSelection = false,
                        AllowCurrentPage = false,
                        UseEXDialog = false
                    };
                    printDialog.PrinterSettings.MinimumPage = 1;
                    printDialog.PrinterSettings.MaximumPage = Math.Max(1, document.PageCount);
                    printDialog.PrinterSettings.FromPage = 1;
                    printDialog.PrinterSettings.ToPage = Math.Max(1, document.PageCount);

                    var owner = Application.Current?.MainWindow;
                    var ownerHandle = owner != null
                        ? new WindowInteropHelper(owner).EnsureHandle()
                        : IntPtr.Zero;
                    var result = ownerHandle != IntPtr.Zero
                        ? printDialog.ShowDialog(new Win32Window(ownerHandle))
                        : printDialog.ShowDialog();

                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        _logger.LogInformation("Pencetakan '{Document}' dibatalkan pengguna.", documentName);
                        completion.TrySetResult(false);
                        return;
                    }

                    // PDFium PrintDocument: PrintPage terisi otomatis dari dokumen
                    // (padanan _pdfDocument.CreatePrintDocument() pada WinForms).
                    using var printDoc = document.CreatePrintDocument();
                    printDoc.DocumentName = documentName;
                    printDoc.PrinterSettings = printDialog.PrinterSettings;
                    printDoc.Print();

                    _logger.LogInformation(
                        "PDF '{Document}' terkirim ke printer {Printer} (halaman {From}-{To}).",
                        documentName,
                        printDialog.PrinterSettings.PrinterName,
                        printDialog.PrinterSettings.FromPage,
                        printDialog.PrinterSettings.ToPage);

                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal mencetak '{Document}'", documentName);
                    completion.TrySetResult(false);
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        _ = dispatcher.InvokeAsync(() =>
                            SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                                "Cetak PDF",
                                $"Gagal mencetak: {ex.Message}",
                                SuDesApp.Utilities.AppMessageButton.Ok,
                                SuDesApp.Utilities.AppMessageIcon.Error));
                    }
                }
                finally
                {
                    try { document?.Dispose(); } catch { /* abaikan */ }
                }
            });

            return await completion.Task;
        }

        /// <summary>Adapter IWin32Window agar dialog berpusat di window utama.</summary>
        private sealed class Win32Window : System.Windows.Forms.IWin32Window
        {
            public Win32Window(IntPtr handle) => Handle = handle;

            public IntPtr Handle { get; }
        }
    }
}
