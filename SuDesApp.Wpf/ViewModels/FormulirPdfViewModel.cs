using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Pratinjau formulir kosong (template PDF) dari folder Templates.
    /// Padanan FormulirControl (WinForms): menampilkan PDF + tombol Cetak/Batal.
    /// </summary>
    public class FormulirPdfViewModel : PdfPreviewViewModel
    {
        public string TemplateName { get; }
        public string DisplayName { get; }

        public FormulirPdfViewModel(
            string templateName,
            NavigationService navigation,
            AppConfig appConfig,
            FileService fileService,
            ILogger<FormulirPdfViewModel> logger)
            : base(
                $"Formulir: {templateName.Replace("_", " ").ToUpperInvariant()}",
                ResolvePdfPath(templateName, appConfig, fileService, logger),
                navigation)
        {
            TemplateName = templateName;
            DisplayName = templateName.Replace("_", " ").ToUpperInvariant();
        }

        private static string? ResolvePdfPath(
            string templateName,
            AppConfig appConfig,
            FileService fileService,
            ILogger<FormulirPdfViewModel> logger)
        {
            try
            {
                string pdfPath = Path.Combine(appConfig.TemplateFolder, $"{templateName}.pdf");
                string actualPath = fileService.FindActualFilePath(pdfPath);
                if (fileService.FileExists(actualPath))
                {
                    return actualPath;
                }

                logger.LogWarning("Template formulir tidak ditemukan: {Path}", actualPath);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gagal memuat template formulir: {Template}", templateName);
                return null;
            }
        }
    }
}