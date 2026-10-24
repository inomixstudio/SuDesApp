using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    public sealed class HasilSetupForm
    {
        public string FormId { get; }
        public string FormUrl { get; }
        public string EditUrl { get; }
        public string SheetUrl { get; }
        public string TabName { get; }
        public int JumlahKolom { get; }

        public HasilSetupForm(string formId, string formUrl, string editUrl, string sheetUrl, string tabName, int jumlahKolom)
        {
            FormId = formId;
            FormUrl = formUrl;
            EditUrl = editUrl;
            SheetUrl = sheetUrl;
            TabName = tabName;
            JumlahKolom = jumlahKolom;
        }
    }

    /// <summary>
    /// Pembuat formulir otomatis: membuat Google Sheet jawaban (mirror) dan Google
    /// Formulir lengkap memakai Forms API, mengizinkan akses publik, lalu menyimpan
    /// pengaturan agar poller langsung aktif. Jawaban form dibaca via Forms API dan
    /// disalin ke Sheet oleh WaSheetIngestService (Forms API tidak bisa menautkan
    /// form ke Sheet secara langsung).
    /// </summary>
    public class WaFormAutoSetupService
    {
        private readonly GoogleDriveService _drive;
        private readonly GoogleFormsService _forms;
        private readonly GoogleSheetsService _sheets;
        private readonly ILogger<WaFormAutoSetupService> _logger;

        public WaFormAutoSetupService(
            GoogleDriveService drive,
            GoogleFormsService forms,
            GoogleSheetsService sheets,
            ILogger<WaFormAutoSetupService> logger)
        {
            _drive = drive ?? throw new ArgumentNullException(nameof(drive));
            _forms = forms ?? throw new ArgumentNullException(nameof(forms));
            _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HasilSetupForm> BuatAsync(bool ganti = false, CancellationToken ct = default)
        {
            if (!_drive.IsOAuthEnabled || !_drive.HasStoredToken())
                throw new InvalidOperationException(
                    "Akun Google belum terhubung. Masuk dengan Akun Google lebih dahulu, lalu coba lagi.");

            if (ganti)
            {
                var sheetLama = WaSheetOptions.ExtractSheetId(WaSheetOptions.GetSheetUrl());
                var formLama = WaSheetOptions.GetFormId();
                if (!string.IsNullOrWhiteSpace(sheetLama))
                {
                    try { await _drive.DeleteItemAsync(sheetLama, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Gagal menghapus Sheet lama saat mengganti formulir otomatis"); }
                }
                if (!string.IsNullOrWhiteSpace(formLama))
                {
                    try { await _drive.DeleteItemAsync(formLama, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Gagal menghapus Form lama saat mengganti formulir otomatis"); }
                }
            }

            var sheet = await _sheets.BuatSpreadsheetAsync(
                WaFormKatalog.JudulSheetDefault, WaFormKatalog.TabJawabanDefault,
                WaFormKatalog.KolomSheet, ct).ConfigureAwait(false);

            InfoFormulir form;
            try
            {
                form = await _forms.BuatFormulirAsync(
                    WaFormKatalog.JudulFormDefault, WaFormKatalog.DeskripsiFormDefault,
                    WaFormKatalog.Pertanyaan, ct).ConfigureAwait(false);
            }
            catch
            {
                try { await _drive.DeleteItemAsync(sheet.SpreadsheetId, ct).ConfigureAwait(false); }
                catch (Exception exHapus) { _logger.LogWarning(exHapus, "Gagal menghapus Sheet sementara setelah pembuatan form gagal"); }
                throw;
            }

            try
            {
                await _drive.SetAnyoneReaderAsync(form.FormId, ct).ConfigureAwait(false);
            }
            catch (Exception exIzin)
            {
                _logger.LogWarning(exIzin, "Gagal mengatur form agar bisa diakses publik; form tetap dibuat");
            }

            WaSheetOptions.SetSheetUrl(sheet.Url);
            WaSheetOptions.SetTabName(sheet.TabName);
            WaSheetOptions.SetFormUrl(form.ResponderUri);
            WaSheetOptions.SetFormId(form.FormId);
            WaSheetOptions.SetAutoForm(true);
            WaSheetOptions.SetLinkModeEnabled(true);

            _logger.LogInformation("Pembuatan formulir otomatis selesai: {FormId} → Sheet {SheetId}", form.FormId, sheet.SpreadsheetId);

            return new HasilSetupForm(
                form.FormId, form.ResponderUri, form.EditUri,
                sheet.Url, sheet.TabName, WaFormKatalog.KolomSheet.Count);
        }
    }
}
