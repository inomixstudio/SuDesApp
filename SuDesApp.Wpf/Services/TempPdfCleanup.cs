using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pembersihan otomatis berkas sementara saat aplikasi start:
    /// 1. folder TempPDF (PDF sementara lebih tua dari 7 hari),
    /// 2. folder sementara konversi Word lama (.doc → .docx) yang tertinggal
    ///    saat konversi terputus (mis. aplikasi ditutup di tengah proses).
    /// File yang sedang terkunci proses lain dilewati.
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

        /// <summary>
        /// Hapus sisa unduhan pembaruan (<c>SuDesApp_&lt;versi&gt;.exe.part</c>) yang
        /// tertinggal — mis. unduhan yang dijeda lalu tidak pernah dilanjutkan lagi.
        /// Batas waktunya sengaja panjang (30 hari): selama itu sisa unduhan masih
        /// dipakai untuk melanjutkan tanpa mengunduh ulang. Berkas yang sedang
        /// diunduh sekarang (baru diubah) tentu tidak tersentuh.
        /// </summary>
        public static void CleanOldDownloadParts(ILogger logger, int olderThanDays = 30)
        {
            try
            {
                var folder = Path.GetTempPath();
                var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);
                int deleted = 0;
                long freedBytes = 0;

                foreach (var file in Directory.EnumerateFiles(folder, "SuDesApp_*.exe" +
                    SuDesApp.Utilities.UnduhanBerkasBerlanjut.AkhiranSementara))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc >= cutoffUtc) continue;

                        freedBytes += info.Length;
                        info.Delete();
                        deleted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Sedang dipakai proses lain — lewati, dicoba lagi pada start berikutnya.
                    }
                }

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Pembersihan sisa unduhan pembaruan: {Count} berkas lama (> {Days} hari) dihapus, {Mb:N1} MB dibebaskan.",
                        deleted, olderThanDays, freedBytes / 1024.0 / 1024.0);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan tidak boleh mengganggu startup aplikasi.
                logger.LogWarning(ex, "Gagal membersihkan sisa unduhan pembaruan.");
            }
        }

        /// <summary>
        /// Hapus berkas .docx sementara hasil konversi berkas Word lama (.doc) yang
        /// tertinggal lebih dari <paramref name="olderThanDays"/> hari — berkas ini hanya
        /// dipakai sesaat saat membaca berkas Word, jadi sisanya pasti sisa gangguan.
        /// </summary>
        public static void CleanOldWordConversions(ILogger logger, int olderThanDays = 1)
        {
            try
            {
                var folder = SuDesApp.Utilities.WordDocConverter.FolderSementara;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

                var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);
                int deleted = 0;

                foreach (var file in Directory.EnumerateFiles(folder, "*.docx"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoffUtc)
                        {
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Berkas sedang dibaca proses lain — lewati.
                    }
                }

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Pembersihan konversi Word: {Count} berkas .docx sementara dihapus.", deleted);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan tidak boleh mengganggu startup aplikasi.
                logger.LogWarning(ex, "Gagal membersihkan folder sementara konversi Word.");
            }
        }
    }
}
