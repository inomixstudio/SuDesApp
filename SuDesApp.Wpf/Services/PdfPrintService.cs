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


        private async Task<bool> PrintAsync(Func<Task<Document>> load, string documentName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
            try
            {
                await using var document = await load().ConfigureAwait(false);
                HasilCetak hasil = await document.PrintAsync(documentName).ConfigureAwait(false);
                TampilkanPeringatanKertas(hasil);
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

        /// <summary>
        /// Beritahu pengguna bila printer tidak bisa memasang ukuran kertas dokumen
        /// (mis. F4 215x330mm tidak ada di driver) sehingga surat diperkecil. Tanpa
        /// ini hasilnya "sedikit lebih kecil dari kertas" dan tidak ada yang tahu
        /// sebabnya - ukuran aplikasi sudah dikirim ke printer, driver yang menolaknya.
        /// </summary>
        public static void TampilkanPeringatanKertas(HasilCetak? hasil)
        {
            if (hasil is null || string.IsNullOrWhiteSpace(hasil.Peringatan)) return;
            if (hasil.Dicetak == false) return;

            void Tampilkan() => SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                "Ukuran kertas tidak tersedia di printer",
                hasil.Peringatan!,
                SuDesApp.Utilities.AppMessageButton.Ok,
                SuDesApp.Utilities.AppMessageIcon.Warning);

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Tampilkan);
            else Tampilkan();
        }
    }
}
