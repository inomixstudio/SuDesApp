using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Memberikan daftar template formulir PDF (folder Templates) ke sidebar
    /// dan memberitahu perubahan daftar (mis. setelah tambah/hapus template).
    /// Padanan UtamaService (WinForms): GetTemplateNamesAsync + RefreshFormulirAsync.
    /// </summary>
    public class FormulirMenuService
    {
        private readonly AppConfig _appConfig;
        private readonly ILogger<FormulirMenuService> _logger;

        public event Action? TemplatesChanged;

        public FormulirMenuService(AppConfig appConfig, ILogger<FormulirMenuService> logger)
        {
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Daftar nama template (nama file PDF tanpa ekstensi) di folder Templates.</summary>
        public IReadOnlyList<string> GetTemplates()
        {
            try
            {
                if (!Directory.Exists(_appConfig.TemplateFolder))
                {
                    return Array.Empty<string>();
                }

                return Directory.GetFiles(_appConfig.TemplateFolder, "*.pdf")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca template PDF di folder: {Folder}", _appConfig.TemplateFolder);
                return Array.Empty<string>();
            }
        }

        public void NotifyTemplatesChanged()
        {
            TemplatesChanged?.Invoke();
        }
    }
}