using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pembersihan otomatis folder TempPDF saat aplikasi start:
    /// menghapus file PDF sementara yang berumur lebih dari batas hari
    /// (default 7 hari). File yang sedang terkunci proses lain dilewati.
    /// </summary>
    public static class TempPdfCleanup
    {
        public static void CleanOldFiles(AppConfig config, ILogger logger, int olderThanDays = 7)
        {
            try
            {
                var folder = config.TempPdfFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return;
                }

                var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);
                int deleted = 0;
                long freedBytes = 0;

                foreach (var file in Directory.EnumerateFiles(folder, "*.pdf"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoffUtc)
                        {
                            freedBytes += info.Length;
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // File sedang dipakai proses lain / tidak berizin — lewati saja;
                        // akan dicoba lagi pada start berikutnya.
                    }
                }

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Pembersihan TempPDF: {Count} file lama (> {Days} hari) dihapus, {Mb:N1} MB dibebaskan.",
                        deleted, olderThanDays, freedBytes / 1024.0 / 1024.0);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan tidak boleh mengganggu startup aplikasi.
                logger.LogWarning(ex, "Gagal membersihkan folder TempPDF.");
            }
        }
    }
}
