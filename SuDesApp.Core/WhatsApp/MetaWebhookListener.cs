using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Menerima pesan MASUK dari WhatsApp Cloud API (Meta) via webhook.
    ///
    /// 1) Verifikasi subscription (dilakukan sekali di dashboard Meta):
    ///      GET /wa-webhook?hub.mode=subscribe&hub.verify_token=…&hub.challenge=…
    ///    Jawab dengan challenge polos bila verify token cocok.
    ///
    /// 2) Pesan masuk warga:
    ///      POST /wa-webhook  (JSON, x-hub-signature-256 = HMAC SHA256 body
    ///    dengan app secret — diverifikasi bila secret diisi, opsional).
    ///
    /// Payload pesan teks dipetakan dari
    ///   entry[0].changes[0].value.messages[0] { from, text.body, id }.
    /// Setiap pesan valid langsung diteruskan ke WaEngine.
    ///
    /// Untuk aplikasi desktop, URL publik HTTPS diperoleh lewat tunnel
    /// (cloudflared/ngrok) atau port-forwarding + sertifikat ke port listener ini.
    /// </summary>
    public class MetaWebhookListener : IDisposable
    {
        private readonly ILogger<MetaWebhookListener> _logger;
        private readonly WaEngine _engine;
        private readonly Func<string?> _verifyTokenProvider;
        private readonly Func<string?> _appSecretProvider;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public MetaWebhookListener(
            WaEngine engine,
            ILogger<MetaWebhookListener> logger,
            Func<string?> verifyTokenProvider,
            Func<string?> appSecretProvider)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _verifyTokenProvider = verifyTokenProvider ?? throw new ArgumentNullException(nameof(verifyTokenProvider));
            _appSecretProvider = appSecretProvider;
        }

        /// <summary>Mulai mendengarkan pada port yang dikonfigurasi (default 8787).</summary>
        public void Start(int port)
        {
            if (_listener != null) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/wa-webhook/");
            try
            {
                _listener.Start();
                _logger.LogInformation("Meta webhook listener aktif di port {Port}", port);
                _ = Task.Run(() => LoopAsync(_cts!.Token));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Gagal memulai webhook listener di port {Port}. " +
                    "Jalankan aplikasi sebagai admin ATAU reservasi URL: " +
                    "netsh http add urlacl url=http://+:{Port}/wa-webhook/ user=Everyone", port, port);
                _listener = null;
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); _listener?.Close(); } catch { /* ignore */ }
            _listener = null;
            _cts = null;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleAsync(ctx, ct), ct);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Kesalahan pada loop webhook listener");
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            try
            {
                if (!ctx.Request.Url!.AbsolutePath.EndsWith("/wa-webhook"))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                // 1) Verifikasi subscription Meta (GET).
                if (ctx.Request.HttpMethod == "GET")
                {
                    var mode = ctx.Request.QueryString["hub.mode"];
                    var token = ctx.Request.QueryString["hub.verify_token"];
                    var challenge = ctx.Request.QueryString["hub.challenge"];

                    var expected = _verifyTokenProvider() ?? string.Empty;
                    if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && token == expected)
                    {
                        var buf = System.Text.Encoding.UTF8.GetBytes(challenge ?? string.Empty);
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "text/plain";
                        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 403;
                    }
                    ctx.Response.Close();
                    return;
                }

                // 2) Pesan masuk (POST JSON).
                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream))
                    body = await reader.ReadToEndAsync();

                // Verifikasi tanda tangan bila app secret dikonfigurasi (opsional).
                if (_appSecretProvider != null)
                {
                    var secret = _appSecretProvider();
                    if (!string.IsNullOrWhiteSpace(secret))
                    {
                        var sig = ctx.Request.Headers["X-Hub-Signature-256"] ?? string.Empty;
                        if (!VerifikasiTandaTangan(secret, body, sig))
                        {
                            _logger.LogWarning("Webhook Meta ditolak: tanda tangan tidak sah.");
                            ctx.Response.StatusCode = 403;
                            ctx.Response.Close();
                            return;
                        }
                    }
                }

                // Satu payload Meta dapat memuat BEBERAPA pesan sekaligus
                // (entri/change/messages berlapis) — semuanya harus diproses,
                // bukan hanya yang pertama, agar pesan warga tidak hilang.
                var inbound = ParsePayloadMany(body);
                foreach (var msg in inbound)
                {
                    _logger.LogInformation("WA masuk (Cloud API webhook) dari {From}: {Text}", msg.FromNumber, msg.Text);
                    await _engine.ProcessInboundAsync(msg, ct);
                }

                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memproses webhook Meta");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Peta payload webhook Meta → pesan masuk (pesan pertama saja).
        /// Mengembalikan null bila payload bukan pesan chat (status update, dsb.).
        /// Dipertahankan untuk pemanggil lama — jalur webhook memakai
        /// <see cref="ParsePayloadMany"/> agar pesan yang dibatch tidak hilang.
        /// </summary>
        internal static WaInboundMessage? ParsePayload(string json)
        {
            var semua = ParsePayloadMany(json);
            return semua.Count > 0 ? semua[0] : null;
        }

        /// <summary>
        /// Peta payload webhook Meta → SELURUH pesan masuk di dalamnya.
        /// Meta dapat mengirim beberapa entri/change dalam satu POST (mis. dua
        /// pesan warga beruntun atau pesan dari beberapa nomor), dan setiap
        /// entri dapat memuat lebih dari satu pesan. Pesan non-teks (stiker,
        /// gambar, status pengiriman) dilewati. Payload tanpa pesan chat
        /// mengembalikan daftar kosong — pemanggil tetap menjawab 200 agar Meta
        /// tidak mengirim ulang terus-menerus.
        /// </summary>
        internal static IReadOnlyList<WaInboundMessage> ParsePayloadMany(string json)
        {
            var hasil = new List<WaInboundMessage>();
            if (string.IsNullOrWhiteSpace(json)) return hasil;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                    return hasil;

                foreach (var e in entries.EnumerateArray())
                {
                    if (!e.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("value", out var v)) continue;
                        if (!v.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var m in messages.EnumerateArray())
                        {
                            if (m.TryGetProperty("type", out var t) && t.GetString() != "text")
                                continue; // sticker/gambar/audio dsb. tidak diproses

                            var from = m.TryGetProperty("from", out var f) ? f.GetString() : null;
                            if (string.IsNullOrWhiteSpace(from)) continue;

                            var text = string.Empty;
                            if (m.TryGetProperty("text", out var txt) && txt.TryGetProperty("body", out var b))
                                text = b.GetString() ?? string.Empty;

                            hasil.Add(new WaInboundMessage
                            {
                                MessageId = (m.TryGetProperty("id", out var id) ? id.GetString() : null)
                                            ?? Guid.NewGuid().ToString("N"),
                                FromNumber = from!,
                                Text = text,
                                ReceivedAt = DateTime.Now
                            });
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return hasil;
            }

            return hasil;
        }

        /// <summary>Validasi X-Hub-Signature-256 = "sha256=" + HMAC-SHA256(body, appSecret).</summary>
        internal static bool VerifikasiTandaTangan(string appSecret, string body, string signatureHeader)
        {
            try
            {
                if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                    return false;

                var key = System.Text.Encoding.UTF8.GetBytes(appSecret);
                var data = System.Text.Encoding.UTF8.GetBytes(body);
                using var hmac = new System.Security.Cryptography.HMACSHA256(key);
                var hash = hmac.ComputeHash(data);
                var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

                return string.Equals(expected, signatureHeader.Trim(), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => Stop();
    }
}
