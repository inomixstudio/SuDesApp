using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Orkestrator layanan WhatsApp (singleton). Menarik pesan masuk dari
    /// gateway, menjalankan mesin percakapan, mengirim balasan, menyimpan
    /// permintaan, dan memicu event notifikasi ke UI (operator).
    /// </summary>
    public class WaEngine : IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly IWhatsAppGateway _gateway;
        private readonly ILogger<WaEngine> _logger;
        private readonly SemaphoreSlim _mutex = new(1, 1);

        /// <summary>
        /// Ingatan pesan masuk terakhir (MessageId → waktu terima). WhatsApp
        /// Cloud API mengirim ULANG webhook yang sama bila jawaban kita lambat
        /// atau gagal, dan satu payload bisa diteruskan dua kali — tanpa
        /// penyaring ini permintaan surat bisa tercatat ganda.
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _pesanTerakhir =
            new(StringComparer.Ordinal);

        /// <summary>Batas jumlah MessageId yang diingat (yang terlama dibuang).</summary>
        private const int MaksIngatanPesan = 500;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private bool _disposed;

        public event EventHandler<PermintaanWa>? RequestCreated;
        public event EventHandler<WaOutboundLogEntry>? OutboundSent;

        /// <summary>
        /// Dinaikkan saat loop polling mendeteksi jam layanan otomatis baru saja
        /// membuka (transisi tutup → buka). Auto-processor memakai ini untuk
        /// mengejar permintaan yang tertunda selama jam tutup.
        /// </summary>
        public event EventHandler? ServiceHoursOpened;

        public WaEngine(
            IServiceProvider provider,
            IWhatsAppGateway gateway,
            ILogger<WaEngine> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Jeda polling pesan masuk (default 15 detik).</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

        public IWhatsAppGateway Gateway => _gateway;
        public bool IsPolling => _loop is { IsCompleted: false };

        /// <summary>
        /// Func yang mengembalikan true bila jam layanan sedang buka. Diisi dari
        /// luar (auto-processor) agar Core tidak bergantung pada preferensi WPF.
        /// Bila null dianggap selalu buka.
        /// </summary>
        public Func<bool>? ServiceHoursOpenQuery { get; set; }

        public void StartPolling()
        {
            if (IsPolling) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => PollLoopAsync(_cts.Token));
            _logger.LogInformation("WhatsApp polling dimulai ({Interval})", PollInterval);
        }

        public async Task StopPollingAsync()
        {
            if (_cts == null) return;
            _cts.Cancel();
            try { if (_loop != null) await _loop; } catch { /* ignore */ }
            _cts.Dispose();
            _cts = null;
            _loop = null;
            _logger.LogInformation("WhatsApp polling dihentikan.");
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            // Dimulai dari "tutup" agar putaran pertama saat jam layanan sedang
            // buka ikut memicu catch-up: permintaan BARU yang tertinggal dari
            // sesi aplikasi sebelumnya (atau dari jam tutup) langsung diproses,
            // bukan menunggu transisi tutup → buka berikutnya.
            bool? sempatTutup = true;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var messages = await _gateway.FetchInboundAsync(ct);
                    foreach (var msg in messages)
                    {
                        await ProcessInboundAsync(msg, ct);
                    }

                    // Deteksi transisi tutup → buka untuk catch-up permintaan
                    // yang tertunda selama jam layanan tutup.
                    bool buka = ServiceHoursOpenQuery?.Invoke() ?? true;
                    if (buka)
                    {
                        if (sempatTutup == true)
                        {
                            _logger.LogInformation("Jam layanan WA dibuka — catch-up permintaan tertunda.");
                            ServiceHoursOpened?.Invoke(this, EventArgs.Empty);
                        }
                        sempatTutup = false;
                    }
                    else
                    {
                        sempatTutup = true;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kesalahan saat memuat pesan WhatsApp");
                }

                try { await Task.Delay(PollInterval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// Memproses satu pesan masuk (dari webhook gateway). Aman dipanggil
        /// dari thread mana pun (diberi mutex).
        /// </summary>
        public async Task ProcessInboundAsync(WaInboundMessage message, CancellationToken ct = default)
        {
            if (message == null) return;

            await _mutex.WaitAsync(ct);
            try
            {
                if (SudahDiterima(message.MessageId))
                {
                    _logger.LogInformation(
                        "Pesan WA {Id} dari {From} diabaikan — sudah pernah diproses (kiriman ulang webhook).",
                        message.MessageId, message.FromNumber);
                    return;
                }

                _logger.LogInformation("WA masuk dari {From}: {Text}", message.FromNumber, message.Text);

                using var scope = _provider.CreateScope();
                var conversation = scope.ServiceProvider.GetRequiredService<WaConversationService>();
                var result = await conversation.ProcessAsync(message.FromNumber, message.Text);

                if (!string.IsNullOrWhiteSpace(result.ReplyText))
                {
                    var send = await _gateway.SendTextAsync(message.FromNumber, result.ReplyText, ct);
                    var entry = new WaOutboundLogEntry
                    {
                        To = message.FromNumber,
                        Text = result.ReplyText,
                        Success = send.Success,
                        Status = send.Status
                    };
                    OutboundSent?.Invoke(this, entry);
                }

                if (result.CreatedRequest != null)
                {
                    RequestCreated?.Invoke(this, result.CreatedRequest);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses pesan WhatsApp dari {From}", message.FromNumber);
                try
                {
                    var fallbackText = "Maaf, terjadi kendala saat memproses permintaan. Silakan coba lagi nanti.";
                    var send = await _gateway.SendTextAsync(message.FromNumber, fallbackText, ct);
                    OutboundSent?.Invoke(this, new WaOutboundLogEntry
                    {
                        To = message.FromNumber,
                        Text = fallbackText,
                        Status = send.Status,
                        Success = send.Success
                    });
                }
                catch { /* ignore */ }
            }
            finally
            {
                _mutex.Release();
            }
        }

        /// <summary>
        /// True bila MessageId ini sudah pernah diproses. Menyimpan penanda
        /// sebelum pemrosesan dimulai (bukan sesudah) supaya kiriman ulang yang
        /// tiba bersamaan tetap tertolak.
        /// </summary>
        private bool SudahDiterima(string? messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return false;

            if (!_pesanTerakhir.TryAdd(messageId, DateTime.Now)) return true;

            if (_pesanTerakhir.Count > MaksIngatanPesan)
            {
                var kedaluwarsa = _pesanTerakhir
                    .OrderBy(kv => kv.Value)
                    .Take(_pesanTerakhir.Count - MaksIngatanPesan)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var id in kedaluwarsa) _pesanTerakhir.TryRemove(id, out _);
            }

            return false;
        }

        /// <summary>
        /// Menaikkan event permintaan baru dari luar alur percakapan — dipakai
        /// WaSheetIngestService saat jawaban Google Sheet menjadi permintaan.
        /// </summary>
        public void RaiseRequestCreated(PermintaanWa permintaan)
            => RequestCreated?.Invoke(this, permintaan);

        public async Task<List<PermintaanWa>> GetListPermintaanAsync(string? statusFilter = null)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
                return await repo.GetAllAsync(statusFilter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar permintaan WhatsApp");
                return new List<PermintaanWa>();
            }
        }

        public async Task<int> GetJumlahBelumDibacaAsync()
        {
            try
            {
                using var scope = _provider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
                return await repo.CountUnreadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghitung permintaan belum dibaca");
                return 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
            _mutex.Dispose();
        }
    }
}