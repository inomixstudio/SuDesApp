using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pembersihan otomatis draft surat yang tidak pernah dilengkapi:
    /// draft berstatus Draft dengan perubahan terakhir lebih tua dari batas hari
    /// (default 30) diusulkan untuk dihapus saat aplikasi start — SELALU dengan
    /// konfirmasi pengguna terlebih dahulu, satu dialog ringkas untuk seluruh batch.
    /// </summary>
    public static class StaleDraftCleanup
    {
        public const int StaleDays = 30;

        /// <summary>Dipanggil dari App.OnStartup setelah MainWindow tampil:
        /// menunggu UI stabil, lalu menjalankan pemeriksaan (tidak pernah melempar).</summary>
        public static async Task RunAfterStartupAsync(IServiceProvider serviceProvider, ILogger logger)
        {
            try
            {
                // Mode diam-diam startup: pemeriksaan draft (membuka database) juga
                // ikut ditunda beberapa menit agar pemuatan awal benar-benar ringan.
                if (AppPreferenceStore.IsStartupDiamDiam())
                {
                    await Task.Delay(TimeSpan.FromMinutes(AppPreferenceStore.GetStartupDiamDiamMenit()));
                }

                // Jeda agar pemeriksaan tidak berebut koneksi DB dengan pemuatan awal Register.
                await Task.Delay(TimeSpan.FromSeconds(5));
                await CheckAndCleanupAsync(serviceProvider, logger);
            }
            catch (Exception ex)
            {
                // Pembersihan tidak boleh mengganggu jalannya aplikasi.
                logger.LogWarning(ex, "Gagal menjalankan pembersihan draft kedaluwarsa.");
            }
        }

        public static async Task CheckAndCleanupAsync(IServiceProvider serviceProvider, ILogger logger)
        {
            var unitOfWork = serviceProvider.GetRequiredService<IUnitOfWork>();
            var messages = serviceProvider.GetRequiredService<IMessageService>();

            var cutoff = DateTime.Now.AddDays(-StaleDays);

            var drafts = (await unitOfWork.SuratRepository.GetFilteredAsync(
                new FilterConditions { Status = "Draft" },
                sortBy: "ID_Surat",
                ascending: true,
                skip: 0,
                take: 10000))?.ToList();

            if (drafts == null || drafts.Count == 0) return;

            // "Tidak dilengkapi dalam 30 hari" = perubahan terakhir (UpdatedAt,
            // fallback CreatedAt) lebih tua dari cutoff.
            var stale = drafts
                .Select(d => (Data: d, Ref: d.UpdatedAt == default ? d.CreatedAt : d.UpdatedAt))
                .Where(x => x.Ref != default && x.Ref < cutoff)
                .OrderBy(x => x.Ref)
                .Select(x => x.Data)
                .ToList();

            if (stale.Count == 0)
            {
                logger.LogDebug("Tidak ada draft kedaluwarsa (> {Days} hari).", StaleDays);
                return;
            }

            var numbers = string.Join(", ", stale.Take(5)
                .Select(d => string.IsNullOrWhiteSpace(d.NomorSurat) ? $"#{d.ID_Surat}" : d.NomorSurat));
            var more = stale.Count > 5 ? $" dan {stale.Count - 5} lainnya" : "";

            var confirmed = await messages.ShowConfirmationAsync(
                "Pembersihan Draft Lama",
                $"Ditemukan {stale.Count} surat berstatus Draft yang tidak dilengkapi lebih dari {StaleDays} hari " +
                $"(terakhir diubah sebelum {cutoff:dd-MM-yyyy}):\n\n{numbers}{more}\n\n" +
                "Hapus semua draft kedaluwarsa ini?");

            if (!confirmed)
            {
                logger.LogInformation("Pembersihan {Count} draft kedaluwarsa dibatalkan pengguna.", stale.Count);
                return;
            }

            int deleted = 0, failed = 0;
            foreach (var draft in stale)
            {
                try
                {
                    if (await unitOfWork.SuratRepository.DeleteAsync(draft.ID_Surat))
                        deleted++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(ex, "Gagal menghapus draft kedaluwarsa #{Id}", draft.ID_Surat);
                }
            }

            logger.LogInformation(
                "Pembersihan draft kedaluwarsa selesai: {Deleted} dihapus, {Failed} gagal.",
                deleted, failed);

            if (deleted > 0)
            {
                await messages.ShowInfoAsync(
                    $"{deleted} draft kedaluwarsa telah dihapus dari Register." +
                    (failed > 0 ? $" ({failed} gagal dihapus — detail di error.log)" : string.Empty));
            }
        }
    }
}
