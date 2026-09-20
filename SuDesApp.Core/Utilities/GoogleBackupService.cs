using Google.Apis.Drive.v3;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Cadangkan data aplikasi ke Google Drive (folder "SuDesApp-Backup"):
    /// 1. Database desa.db  — snapshot konsisten via VACUUM INTO.
    /// 2. Folder lampiran   — ArsipKeputusanFiles di-zip (berkas Word/PDF arsip).
    /// 3. Template surat    — seluruh isi folder Templates di-zip (kecuali
    ///    folder lampiran dan cadangan internal).
    /// Cadangan lama di Drive dipangkas otomatis per kelompok
    /// (maks. <see cref="MaxBackups"/> berkas per kelompok).
    /// Berkas yang sedang terkunci (dibuka di Word/Excel) dilewati, bukan
    /// menggagalkan backup.
    /// </summary>
    public class GoogleBackupService
    {
        public const string BackupFolderName = "SuDesApp-Backup";
        private const string DbPrefix = "SuDesApp-Backup_";
        private const string LampiranPrefix = "SuDesApp-Lampiran_";
        private const string TemplatePrefix = "SuDesApp-Template_";
        private const string LampiranFolderName = "ArsipKeputusanFiles";
        private const string InternalBackupFolderName = "backups";
        private const int MaxBackups = 30;

        private readonly GoogleDriveService _driveService;
        private readonly string _databaseConnectionString;
        private readonly string _templateFolder;
        private readonly ILogger<GoogleBackupService> _logger;

        public GoogleBackupService(
            GoogleDriveService driveService,
            AppConfig appConfig,
            ILogger<GoogleBackupService> logger)
        {
            _driveService = driveService ?? throw new ArgumentNullException(nameof(driveService));
            _databaseConnectionString = appConfig.DatabaseConnectionString;
            _templateFolder = appConfig.TemplateFolder;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Jalankan backup penuh: snapshot + zip -> unggah -> pangkas cadangan lama.
        /// Mengembalikan nama berkas-berkas cadangan yang berhasil diunggah.
        /// </summary>
        public async Task<List<string>> BackupNowAsync(CancellationToken ct = default)
        {
            var uploaded = new List<string>();
            var folderId = await _driveService.FindOrCreateFolderAsync(BackupFolderName, ct: ct)
                .ConfigureAwait(false);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");

            // 1. Database (snapshot konsisten meski sedang dipakai)
            var dbPath = Path.Combine(Path.GetTempPath(), $"{DbPrefix}{stamp}.db");
            try
            {
                await CreateSnapshotAsync(dbPath).ConfigureAwait(false);
                if (File.Exists(dbPath))
                {
                    var name = await UploadAndCleanupAsync(dbPath, folderId, ct).ConfigureAwait(false);
                    if (name != null) uploaded.Add(name);
                }
            }
            finally
            {
                TryDeleteTemp(dbPath);
            }

            // 2. Folder lampiran (ArsipKeputusanFiles)
            var lampiranDir = Path.Combine(_templateFolder, LampiranFolderName);
            var lampiranZip = Path.Combine(Path.GetTempPath(), $"{LampiranPrefix}{stamp}.zip");
            try
            {
                int count = await Task.Run(() => CreateZipSkippingLocked(
                    lampiranDir, lampiranZip, skipDirectories: null, searchPattern: "*.*", ct))
                    .ConfigureAwait(false);
                if (count > 0)
                {
                    _logger.LogInformation("Zip lampiran dibuat: {Count} berkas", count);
                    var name = await UploadAndCleanupAsync(lampiranZip, folderId, ct).ConfigureAwait(false);
                    if (name != null) uploaded.Add(name);
                }
                else
                {
                    _logger.LogInformation("Backup lampiran dilewati: folder kosong atau semua berkas terkunci.");
                }
            }
            finally
            {
                TryDeleteTemp(lampiranZip);
            }

            // 3. Template surat (folder Templates tanpa folder lampiran & backup internal)
            var templateZip = Path.Combine(Path.GetTempPath(), $"{TemplatePrefix}{stamp}.zip");
            try
            {
                int count = await Task.Run(() => CreateZipSkippingLocked(
                    _templateFolder, templateZip,
                    skipDirectories: new[] { LampiranFolderName, InternalBackupFolderName },
                    searchPattern: "*.*", ct))
                    .ConfigureAwait(false);
                if (count > 0)
                {
                    _logger.LogInformation("Zip template dibuat: {Count} berkas", count);
                    var name = await UploadAndCleanupAsync(templateZip, folderId, ct).ConfigureAwait(false);
                    if (name != null) uploaded.Add(name);
                }
                else
                {
                    _logger.LogInformation("Backup template dilewati: folder kosong atau semua berkas terkunci.");
                }
            }
            finally
            {
                TryDeleteTemp(templateZip);
            }

            // 4. Pangkas cadangan lama per kelompok
            await PruneOldBackupsAsync(folderId, DbPrefix, ct).ConfigureAwait(false);
            await PruneOldBackupsAsync(folderId, LampiranPrefix, ct).ConfigureAwait(false);
            await PruneOldBackupsAsync(folderId, TemplatePrefix, ct).ConfigureAwait(false);

            _logger.LogInformation("Backup ke Google Drive selesai: {Count} berkas ({Names})",
                uploaded.Count, string.Join(", ", uploaded));
            return uploaded;
        }

        /// <summary>Apakah backup sudah dilakukan hari ini (menurut catatan lokal)?</summary>
        public bool IsBackupDoneToday()
        {
            try
            {
                var path = StampPath;
                if (!File.Exists(path)) return false;
                return File.ReadAllText(path).Trim() == DateTime.Now.ToString("yyyy-MM-dd");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Catat tanggal backup terakhir (penanda agar tidak berulang dalam sehari).</summary>
        public void MarkBackupDoneToday()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StampPath)!);
                File.WriteAllText(StampPath, DateTime.Now.ToString("yyyy-MM-dd"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyimpan penanda backup harian");
            }
        }

        // =====================================================================
        // Pembantu
        // =====================================================================

        /// <summary>Unggah berkas lalu hapus berkas sementara. Null bila gagal.</summary>
        private async Task<string?> UploadAndCleanupAsync(string path, string folderId, CancellationToken ct)
        {
            try
            {
                var fileName = Path.GetFileName(path);

                // Cegah duplikat: bila berkas dengan nama sama sudah ada di
                // folder cadangan (mis. backup diulang dalam menit yang sama),
                // hapus dulu agar Drive tidak menumpuk salinan.
                try
                {
                    var existing = await _driveService.ListItemsAsync(folderId, ct).ConfigureAwait(false);
                    foreach (var item in existing.Where(i => !i.IsFolder && i.Name == fileName))
                    {
                        await _driveService.DeleteItemAsync(item.Id!, ct).ConfigureAwait(false);
                        _logger.LogInformation("Cadangan lama dengan nama sama dihapus: {File}", fileName);
                    }
                }
                catch (Exception exDedupe)
                {
                    _logger.LogWarning(exDedupe, "Gagal memeriksa/membersihkan cadangan lama: {File}", fileName);
                }

                var uploaded = await _driveService.UploadFileAsync(path, folderId, fileName, ct: ct)
                    .ConfigureAwait(false);
                return uploaded?.Name ?? fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunggah berkas cadangan: {File}", Path.GetFileName(path));
                return null;
            }
        }

        /// <summary>
        /// Snapshot konsisten dengan VACUUM INTO — sama seperti pola
        /// DatabaseImportExportService.ExportDatabaseAsync, namun berdiri sendiri.
        /// </summary>
        private async Task CreateSnapshotAsync(string targetPath)
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);

            await using var conn = new SqliteConnection(_databaseConnectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            var safe = targetPath.Replace("'", "''");
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{safe}'";
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Zip isi direktori, melewati berkas yang sedang terkunci (dibuka di
        /// Word/Excel) dengan peringatan — kegagalan satu berkas tidak
        /// menggagalkan backup. Mengembalikan jumlah berkas yang dimasukkan.
        /// </summary>
        private int CreateZipSkippingLocked(
            string sourceDir, string zipPath, string[]? skipDirectories, string searchPattern, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                return 0;

            var skipSet = (skipDirectories ?? Array.Empty<string>())
                .Select(d => d.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant())
                .ToHashSet();

            if (File.Exists(zipPath)) File.Delete(zipPath);

            int added = 0, skipped = 0;
            using (var zipStream = new FileStream(zipPath, FileMode.CreateNew))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var files = Directory.EnumerateFiles(sourceDir, searchPattern, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    var relative = Path.GetRelativePath(sourceDir, file);
                    var topDir = relative.Split(Path.DirectorySeparatorChar)[0];
                    if (skipSet.Contains(topDir.ToLowerInvariant()) ||
                        skipSet.Contains(relative.ToLowerInvariant()))
                    {
                        continue;
                    }

                    try
                    {
                        // FileShare.ReadWrite: bolehkan berkas yang sedang dibuka.
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
                        var entry = archive.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        fs.CopyTo(entryStream);
                        added++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        skipped++;
                        _logger.LogWarning("Berkas dilewati saat zip (terkunci/terproteksi): {File}", file);
                    }
                }
            }

            if (added == 0)
            {
                // Jangan tinggalkan zip kosong.
                TryDeleteTemp(zipPath);
            }
            if (skipped > 0)
            {
                _logger.LogWarning("{Skipped} berkas dilewati saat zip {Dir}", skipped, sourceDir);
            }
            return added;
        }

        /// <summary>Hapus cadangan lama di Drive untuk satu kelompok prefix, sisakan MaxBackups terbaru.</summary>
        private async Task PruneOldBackupsAsync(string folderId, string prefix, CancellationToken ct)
        {
            try
            {
                var items = await _driveService.ListItemsAsync(folderId, ct).ConfigureAwait(false);
                var oldBackups = items
                    .Where(i => !i.IsFolder && i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase) // nama memuat tanggal => urut waktu
                    .Skip(MaxBackups)
                    .ToList();

                foreach (var old in oldBackups)
                {
                    try
                    {
                        await _driveService.DeleteItemAsync(old.Id!, ct).ConfigureAwait(false);
                        _logger.LogInformation("Cadangan lama dihapus dari Drive: {Name}", old.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal menghapus cadangan lama: {Name}", old.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memangkas cadangan lama {Prefix} (backup tetap berjalan)", prefix);
            }
        }

        private void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghapus berkas sementara: {Path}", path);
            }
        }

        private string StampPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", "GoogleBackupLast.stamp");
    }
}
