using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Helper pembuatan PDF surat — resolver generator per jenis + penulisan file
    /// sementara. Dipakai alur input surat dan pembukaan surat dari register
    /// (setara SuratManager.HasilkanSuratAsync pada WinForms).
    /// </summary>
    public static class SuratPdfHelper
    {
        public static SuDesApp.Interface.ISuratGenerator? ResolveGenerator(IServiceProvider services, string templateName)
        {
            return templateName.ToUpperInvariant() switch
            {
                SuratConstants.SKD_UMUM => services.GetService<SuDesApp.GeneratorPdf.SKDGenerator>(),
                SuratConstants.SKU => services.GetService<SuDesApp.GeneratorPdf.SKUGenerator>(),
                SuratConstants.PENGANTAR_SKCK => services.GetService<SuDesApp.GeneratorPdf.SKCKGenerator>(),
                SuratConstants.SKTM => services.GetService<SuDesApp.GeneratorPdf.SKTMGenerator>(),
                SuratConstants.KEMATIAN => services.GetService<SuDesApp.GeneratorPdf.KematianGenerator>(),
                SuratConstants.BEDANAMA => services.GetService<SuDesApp.GeneratorPdf.BedaNamaGenerator>(),
                SuratConstants.DOMISILI_WARGA => services.GetService<SuDesApp.GeneratorPdf.DomisiliWargaGenerator>(),
                SuratConstants.INSTANSI => services.GetService<SuDesApp.GeneratorPdf.DomisiliInstansiGenerator>(),
                SuratConstants.GARAPAN_SAWAH => services.GetService<SuDesApp.GeneratorPdf.GarapanGenerator>(),
                SuratConstants.IZIN_ORTU => services.GetService<SuDesApp.GeneratorPdf.IzinOrtuGenerator>(),
                SuratConstants.KENAL_LAHIR => services.GetService<SuDesApp.GeneratorPdf.KenalLahirGenerator>(),
                SuratConstants.IJIN_TINGGAL => services.GetService<SuDesApp.GeneratorPdf.IjinTinggalGenerator>(),
                SuratConstants.AHLI_WARIS => services.GetService<SuDesApp.GeneratorPdf.AhliWarisGenerator>(),
                SuratConstants.NTCR_N1 => services.GetService<SuDesApp.GeneratorPdf.NtcrGenerator>(),
                SuratConstants.NTCR_N2 => services.GetService<SuDesApp.GeneratorPdf.NtcrGenerator>(),
                SuratConstants.NTCR_N3 => services.GetService<SuDesApp.GeneratorPdf.NtcrGenerator>(),
                SuratConstants.NTCR_N4 => services.GetService<SuDesApp.GeneratorPdf.NtcrGenerator>(),
                _ => null
            };
        }

        public static async Task<string?> GeneratePdfAsync(
            IServiceProvider services,
            AppConfig appConfig,
            SuratData suratData,
            ILogger logger,
            string templateName)
        {
            try
            {
                if (suratData == null || string.IsNullOrWhiteSpace(templateName))
                    return null;

                var generator = ResolveGenerator(services, templateName);
                if (generator == null)
                {
                    logger.LogWarning("Generator untuk {Template} belum didaftarkan; PDF dilewati.", templateName);
                    return null;
                }

                var safeNomor = new string((suratData.NomorSurat ?? "NO")
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
                string fileName = $"{suratData.NamaJenis}_{safeNomor}_{suratData.ID_Surat}.pdf";
                Directory.CreateDirectory(appConfig.TempPdfFolder);
                string path = Path.Combine(appConfig.TempPdfFolder, fileName);

                // Tulis ke file sementara bernama unik dulu, lalu pindah ke nama akhir.
                // Alasan: pratinjau PDFium mem-lock file yang sedang dilihat, sehingga
                // File.Create(path) langsung ke nama akhir meledak "being used by another
                // process" saat PDF di-generate ulang dari form edit yang terbuka di atas
                // pratinjau itu sendiri. Bila nama akhir masih terkunci, pakai file unik —
                // generate tetap sukses dan pratinjau baru selalu memuat file terbaru.
                string writePath = Path.Combine(appConfig.TempPdfFolder,
                    $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmssfff}.pdf");

                using (var stream = File.Create(writePath))
                {
                    await generator.GeneratePdfAsync(stream, suratData, suratData.Keterangan);
                }

                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(writePath, path);
                    return path;
                }
                catch (Exception lockEx) when (lockEx is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(lockEx,
                        "PDF lama masih terkunci ({Path}); memakai nama file unik.", path);
                    return writePath;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gagal membuat PDF untuk surat #{Id}", suratData?.ID_Surat);
                return null;
            }
        }

        /// <summary>
        /// Regenerasi PDF surat berdasarkan ID: muat ulang data lengkap (beserta relasi)
        /// dari DB lalu generate. Dipakai pratinjau untuk menyegarkan diri setelah
        /// form edit ditutup dengan menyimpan.
        /// </summary>
        public static async Task<string?> RegeneratePdfAsync(
            IServiceProvider services,
            AppConfig appConfig,
            ILogger logger,
            string templateName,
            int idSurat)
        {
            try
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                var suratData = await unitOfWork.SuratRepository.GetByIdAsync(idSurat);
                if (suratData == null)
                {
                    logger.LogWarning("RegeneratePdfAsync: surat #{Id} tidak ditemukan.", idSurat);
                    return null;
                }

                return await GeneratePdfAsync(services, appConfig, suratData, logger,
                    suratData.NamaJenis ?? templateName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gagal meregenerasi PDF surat #{Id}", idSurat);
                return null;
            }
        }
    }
}