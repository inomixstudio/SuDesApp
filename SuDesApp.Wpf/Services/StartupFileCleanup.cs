using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pembersihan berkas lama saat aplikasi start:
    /// 1. Rotasi error.log per bulan kalender — isi log dipindah (append) ke
    ///    Logs/error_yyyyMM.log lalu error.log dikosongkan; arsip lebih tua dari
    ///    90 hari dihapus.
    /// 2. File hasil ekspor di folder Output/PDF yang lebih tua dari batas hari
    ///    (default 30) dihapus — file terkunci proses lain dilewati.
    /// Semua langkah aman: kegagalan hanya dicatat, tidak pernah mengganggu startup.
    /// </summary>
    public static class StartupFileCleanup
    {
        public const int ExportRetentionDays = 30;
        public const int LogArchiveRetentionDays = 90;

        public static void Run(AppConfig config, ILogger logger, bool cleanOldExports = true)
        {
            RotateErrorLog(logger);
            if (cleanOldExports)
            {
                CleanOldExports(config, logger, ExportRetentionDays);
            }
        }

        /// <summary>
        /// Rotasi error.log per bulan: entri lama dipindah ke Logs/error_yyyyMM.log
        /// (append bila arsip bulan berjalan sudah ada), lalu error.log dikosongkan
        /// sehingga selalu berisi log sejak rotasi terakhir saja.
        /// </summary>
        public static void RotateErrorLog(ILogger logger)
        {
            try
            {
                var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error.log");
                if (!File.Exists(errorLogPath)) return;

                var info = new FileInfo(errorLogPath);
                if (info.Length == 0) return; // Kosong — tidak ada yang dirotasi.

                var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(logsDir);

                var archiveName = $"error_{DateTime.Now:yyyyMM}.log";
                var archivePath = Path.Combine(logsDir, archiveName);

                // Append bila arsip bulan yang sama sudah ada (start berulang dalam
                // satu bulan konsolidasi ke arsip yang sama, tidak menimpa isi lama).
                var content = File.ReadAllText(errorLogPath);
                var separator = File.Exists(archivePath)
                    ? $"\n---- ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ----\n"
                    : string.Empty;
                File.AppendAllText(archivePath, separator + content);

                // Kosongkan log aktif — path tetap ada untuk AppendAllText berikutnya.
                File.WriteAllText(errorLogPath, string.Empty);

                logger.LogInformation(
                    "Rotasi error.log: {Bytes:N0} byte dipindah ke {Archive}.",
                    info.Length, archivePath);
            }
            catch (Exception ex)
            {
                // Rotasi tidak boleh mengganggu startup aplikasi.
                logger.LogWarning(ex, "Gagal merotasi error.log.");
                return;
            }

            // Arsip lebih tua dari batas retensi dihapus (di luar try utama agar
            // kegagalan hapus arsip tidak membatalkan rotasi yang sudah sukses).
            try
            {
                var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
                if (!Directory.Exists(logsDir)) return;

                var cutoffUtc = DateTime.UtcNow.AddDays(-LogArchiveRetentionDays);
                foreach (var archive in Directory.EnumerateFiles(logsDir, "error_*.log"))
                {
                    try
                    {
                        var a = new FileInfo(archive);
                        if (a.LastWriteTimeUtc < cutoffUtc)
                        {
                            a.Delete();
                            logger.LogInformation(
                                "Arsip log lama dihapus (> {Days} hari): {Name}",
                                LogArchiveRetentionDays, a.Name);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Arsip sedang dibuka (mis. dilihat di editor) — lewati,
                        // dicoba lagi pada start berikutnya.
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gagal membersihkan arsip log lama.");
            }
        }

        /// <summary>
        /// Hapus file hasil ekspor lama di folder Output/PDF (default: > 30 hari).
        /// Hanya file langsung di folder itu — subfolder tidak disentuh.
        /// </summary>
        public static void CleanOldExports(AppConfig config, ILogger logger, int olderThanDays = ExportRetentionDays)
        {
            try
            {
                var folder = config.PdfOutputPath;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return;

                var cutoffUtc = DateTime.UtcNow.AddDays(-olderThanDays);
                int deleted = 0;
                long freedBytes = 0;

                foreach (var file in Directory.EnumerateFiles(folder))
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
                        // File sedang dipakai (dibuka PDF reader, dsb.) — lewati;
                        // akan dicoba lagi pada start berikutnya.
                    }
                }

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Pembersihan Output/PDF: {Count} file ekspor lama (> {Days} hari) dihapus, {Mb:N1} MB dibebaskan.",
                        deleted, olderThanDays, freedBytes / 1024.0 / 1024.0);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan tidak boleh mengganggu startup aplikasi.
                logger.LogWarning(ex, "Gagal membersihkan folder Output/PDF.");
            }
        }
    }
}
