using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Gateway WhatsApp RESMI dari Meta: WhatsApp Cloud API (Graph API).
    /// Gratis dipakai — cukup daftar akun Meta for Developers, buat aplikasi
    /// WhatsApp, dan ambil token akses permanen + Phone Number ID.
    ///
    /// Aplikasi ini hanya mengirim TEKS/tautan (link Google Form & link unduh
    /// PDF), yang dalam jendela layanan 24 jam setelah pesan warga terakhir
    /// semuanya GRATIS — tidak ada biaya per pesan untuk alur layanan surat.
    ///
    /// Kirim teks:
    ///   POST https://graph.facebook.com/v21.0/{phone-number-id}/messages
    ///   Authorization: Bearer {token}
    ///   { "messaging_product": "whatsapp", "to": "62812…",
    ///     "type": "text", "text": { "preview_url": true, "body": "…" } }
    ///
    /// Pesan masuk diterima lewat MetaWebhookListener (webhook HTTPS publik).
    /// </summary>
    public class CloudApiWhatsAppGateway : IWhatsAppGateway
    {
        private const string GraphVersion = "v21.0";
        private const string GraphBase = "https://graph.facebook.com/";

        private readonly ILogger<CloudApiWhatsAppGateway> _logger;
        private readonly HttpClient _http;

        public CloudApiWhatsAppGateway(ILogger<CloudApiWhatsAppGateway> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public string Name => "WhatsApp Cloud API (Meta, resmi)";
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AppPreferenceStore.GetWaCloudApiToken()) &&
            !string.IsNullOrWhiteSpace(AppPreferenceStore.GetWaCloudApiPhoneId());

        /// <summary>
        /// Pesan masuk dikirim Meta ke webhook (bukan polling), jadi method ini
        /// mengembalikan daftar kosong — pesan masuk ditangani MetaWebhookListener.
        /// </summary>
        public Task<IReadOnlyList<WaInboundMessage>> FetchInboundAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WaInboundMessage>>(new List<WaInboundMessage>());

        public async Task<WaSendResult> SendTextAsync(string toNumber, string text, CancellationToken cancellationToken = default)
        {
            var token = AppPreferenceStore.GetWaCloudApiToken();
            var phoneId = AppPreferenceStore.GetWaCloudApiPhoneId();

            if (string.IsNullOrWhiteSpace(token))
                return new WaSendResult { Success = false, Status = "Token Cloud API belum diisi (Pengaturan Aplikasi)" };
            if (string.IsNullOrWhiteSpace(phoneId))
                return new WaSendResult { Success = false, Status = "Phone Number ID belum diisi (Pengaturan Aplikasi)" };

            try
            {
                var payload = new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to = NormalkanNomor(toNumber),
                    type = "text",
                    text = new { preview_url = true, body = text ?? string.Empty }
                };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphBase}{GraphVersion}/{phoneId.Trim()}/messages")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());

                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Cloud API respons ({Code}): {Body}", (int)response.StatusCode, body);

                if (response.IsSuccessStatusCode)
                    return new WaSendResult { Success = true, Status = "TERKIRIM (Cloud API)", Detail = body };

                return new WaSendResult { Success = false, Status = $"HTTP {(int)response.StatusCode}", Detail = body };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal kirim teks Cloud API ke {To}", toNumber);
                return new WaSendResult { Success = false, Status = "GAGAL", Detail = ex.Message };
            }
        }

        /// <summary>
        /// Cloud API mewajibkan nomor internasional lengkap tanpa awalan 0/62
        /// duplikat: 08xx → 628xx; 62xx → 62xx; +62xx → 62xx.
        /// </summary>
        internal static string NormalkanNomor(string nomor)
        {
            var digits = new string((nomor ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0") && digits.Length > 9) return "62" + digits[1..];
            return digits;
        }
    }
}
