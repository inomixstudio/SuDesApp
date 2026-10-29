using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SudesPdf;

namespace SuDesApp.Wpf.Services
{
    /// <summary>Membuka dialog cetak sistem dari dokumen yang dirender SudesPdf.</summary>
    public class PdfPrintService
    {
        private readonly ILogger<PdfPrintService> _logger;

        public PdfPrintService(ILogger<PdfPrintService> logger) =>
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Task<bool> PrintPdfFileAsync(string pdfPath, string documentName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
            return PrintAsync(() => Document.LoadAsync(pdfPath), documentName);
        }

        public Task<bool> PrintPdfBytesAsync(byte[] pdfBytes, string documentName)
        {
            ArgumentNullException.ThrowIfNull(pdfBytes);
            return PrintAsync(() => Document.LoadAsync(pdfBytes), documentName);
        }

        private async Task<bool> PrintAsync(Func<Task<Document>> load, string documentName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
            try
            {
                await using var document = await load().ConfigureAwait(false);
                await document.PrintAsync(documentName).ConfigureAwait(false);
                _logger.LogInformation("PDF '{Document}' dikirim ke dialog cetak sistem.", documentName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencetak '{Document}'", documentName);
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null)
                {
                    await dispatcher.InvokeAsync(() =>
                        SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                            "Cetak PDF",
                            $"Gagal mencetak: {ex.Message}",
                            SuDesApp.Utilities.AppMessageButton.Ok,
                            SuDesApp.Utilities.AppMessageIcon.Error));
                }
                return false;
            }
        }
    }
}
