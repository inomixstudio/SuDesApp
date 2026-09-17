using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Identitas pengguna sesi aktif — diisi saat login (Google atau manual)
    /// dan dibaca oleh ActivityLogService saat mencatat aktivitas.
    /// </summary>
    public static class SessionContext
    {
        /// <summary>Identitas pengguna aktif: email Google atau username admin.</summary>
        public static string CurrentUser { get; set; } = "admin";

        /// <summary>Metode login: "google" atau "manual".</summary>
        public static string LoginMethod { get; set; } = "manual";

        /// <summary>Waktu login sesi aktif.</summary>
        public static DateTime LoginTime { get; private set; } = DateTime.Now;

        public static void Set(string user, string method)
        {
            CurrentUser = string.IsNullOrWhiteSpace(user) ? "admin" : user.Trim();
            LoginMethod = string.IsNullOrWhiteSpace(method) ? "manual" : method.Trim();
            LoginTime = DateTime.Now;
        }

        /// <summary>Tampilkan nama pendek untuk UI (email Google dipotong bila terlalu panjang).</summary>
        public static string Display => CurrentUser.Length <= 28 ? CurrentUser : CurrentUser.Substring(0, 27) + "…";
    }

    /// <summary>Baris riwayat aktivitas untuk tampilan.</summary>
    public class ActivityEntry
    {
        public int Id { get; set; }
        public DateTime Waktu { get; set; }
        public string Pengguna { get; set; } = "";
        public string Metode { get; set; } = "";
        public string JenisDokumen { get; set; } = "";
        public string Dokumen { get; set; } = "";
        public string Aksi { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Pencatat riwayat aktivitas pengguna ke tabel ActivityLog (SQLite,
    /// self-heal). Semua penulisan dijalankan di antrean latar belakang agar
    /// tidak pernah memperlambat atau menggagalkan operasi utama — kegagalan
    /// pencatatan hanya dicatat di log, tidak pernah di-lempar ke pemanggil.
    /// </summary>
    public class ActivityLogService
    {
        private const int MaxRows = 20000; // batas penyimpanan; terpangkas saat melebihi
        private readonly string _connectionString;
        private readonly ILogger<ActivityLogService> _logger;
        private readonly BlockingCollection<ActivityEntry> _queue = new(boundedCapacity: 5000);
        private readonly CancellationTokenSource _cts = new();
        private Task? _writerTask;

        public ActivityLogService(AppConfig appConfig, ILogger<ActivityLogService> logger)
        {
            _connectionString = appConfig.DatabaseConnectionString;
            _logger = logger;
            _writerTask = Task.Run(WriterLoopAsync);
        }

        /// <summary>Catat aktivitas (non-blocking; antrean latar belakang).
        /// Dapat dimatikan lewat Pengaturan Aplikasi — bila nonaktif, panggilan
        /// ini no-op sehingga tidak ada penulisan ke database.</summary>
        public void Log(string jenisDokumen, string dokumen, string aksi, string? detail = null)
        {
            if (!AppPreferenceStore.IsActivityLoggingEnabled()) return;

            var entry = new ActivityEntry
            {
                Waktu = DateTime.Now,
                Pengguna = SessionContext.CurrentUser,
                Metode = SessionContext.LoginMethod,
                JenisDokumen = jenisDokumen ?? "",
                Dokumen = dokumen ?? "",
                Aksi = aksi ?? "",
                Detail = detail ?? ""
            };
            if (!_queue.IsAddingCompleted)
            {
                try { _queue.TryAdd(entry); } catch { /* antrean penuh — abaikan */ }
            }
        }

        /// <summary>Ambil riwayat terbaru (default 500 baris).</summary>
        public async Task<List<ActivityEntry>> GetRecentAsync(int limit = 500, string? filter = null)
        {
            await EnsureTableAsync().ConfigureAwait(false);
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            var sql = "SELECT Id, Waktu, Pengguna, Metode, JenisDokumen, Dokumen, Aksi, Detail FROM ActivityLog";
            if (!string.IsNullOrWhiteSpace(filter))
                sql += " WHERE Pengguna LIKE @f OR JenisDokumen LIKE @f OR Dokumen LIKE @f OR Aksi LIKE @f OR Detail LIKE @f";
            sql += " ORDER BY Id DESC LIMIT @lim";

            var rows = (await conn.QueryAsync<ActivityEntry>(sql, new { f = $"%{filter}%", lim = limit }).ConfigureAwait(false)).ToList();
            return rows;
        }

        /// <summary>Bersihkan seluruh riwayat.</summary>
        public async Task ClearAsync()
        {
            await EnsureTableAsync().ConfigureAwait(false);
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await conn.ExecuteAsync("DELETE FROM ActivityLog").ConfigureAwait(false);
            _logger.LogInformation("Riwayat aktivitas dibersihkan");
        }

        /// <summary>Antrean penulis latar belakang — batch insert efisien.</summary>
        private async Task WriterLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (!_queue.TryTake(out var entry, (int)TimeSpan.FromSeconds(1).TotalMilliseconds, _cts.Token))
                        continue;

                    var batch = new List<ActivityEntry> { entry };
                    while (batch.Count < 50 && _queue.TryTake(out var more))
                        batch.Add(more);

                    await WriteBatchAsync(batch).ConfigureAwait(false);
                    if (_queue.Count == 0) await FlushPruneAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menulis batch riwayat aktivitas");
                }
            }
        }

        private async Task WriteBatchAsync(List<ActivityEntry> batch)
        {
            await EnsureTableAsync().ConfigureAwait(false);
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO ActivityLog (Waktu, Pengguna, Metode, JenisDokumen, Dokumen, Aksi, Detail)
VALUES (@Waktu, @Pengguna, @Metode, @JenisDokumen, @Dokumen, @Aksi, @Detail)", batch).ConfigureAwait(false);
        }

        /// <summary>Pangkas baris lama agar tabel tidak tumbuh tanpa batas.</summary>
        private async Task FlushPruneAsync()
        {
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);
                var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ActivityLog").ConfigureAwait(false);
                if (count <= MaxRows) return;
                await conn.ExecuteAsync(@"
DELETE FROM ActivityLog WHERE Id IN (
    SELECT Id FROM ActivityLog ORDER BY Id DESC LIMIT -1 OFFSET @keep)", new { keep = MaxRows }).ConfigureAwait(false);
                _logger.LogInformation("Riwayat aktivitas dipangkas: {Count} -> {Keep}", count, MaxRows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memangkas riwayat aktivitas");
            }
        }

        private async Task EnsureTableAsync()
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await conn.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ActivityLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Waktu TEXT NOT NULL,
    Pengguna TEXT NOT NULL,
    Metode TEXT NOT NULL,
    JenisDokumen TEXT NOT NULL,
    Dokumen TEXT NOT NULL,
    Aksi TEXT NOT NULL,
    Detail TEXT NOT NULL)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_activitylog_waktu ON ActivityLog(Waktu)");
        }
    }
}
