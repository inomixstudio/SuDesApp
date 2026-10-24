using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuDesApp.Server;

/// <summary>
/// Integrasi Midtrans Snap untuk tagihan bulanan SuDesApp (QRIS & Virtual Account).
///
/// - Buat transaksi Snap: POST {base}/snap/v1/transactions, Basic auth dari
///   ServerKey + ":" (Base64). Response memuat token + redirect_url yang
///   dibuka pengguna untuk membayar.
/// - Webhook: verifikasi signature_key = SHA512(order_id + status_code +
///   gross_amount + ServerKey). gross_amount dipakai SEBAGAI STRING asli dari
///   webhook (biasanya "50000.00") — jangan di-parse ulang.
/// - Sandbox memakai app.sandbox.midtrans.com; production app.midtrans.com.
/// - Query status rekonsiliasi memakai host Core API yang BERBEDA:
///   api.sandbox.midtrans.com / api.midtrans.com (lihat <see cref="AmbilStatusAsync"/>).
/// </summary>
public class MidtransService : IPenyediaStatusPembayaran, IPembuatTagihan
{
    private readonly HttpClient _http;
    private readonly ILogger<MidtransService> _logger;
    private readonly string _serverKey;
    private readonly string _baseUrl;
    private readonly string _baseUrlApi;
    private readonly string[]? _enabledPayments;

    public MidtransService(IConfiguration config, ILogger<MidtransService> logger, HttpClient? http = null)
    {
        _logger = logger;
        _http = http ?? new HttpClient();

        // Environment variable DIDULUKAN atas appsettings, dan nilai kosong
        // dianggap belum diisi. appsettings.lokal.json sering memuat
        // "ServerKey": "" sebagai placeholder; dengan `??` saja, string kosong
        // itu menimpa env var dan diam-diam mematikan verifikasi signature —
        // persis kegagalan yang paling mahal di sistem ini.
        //
        // Aturan env-kosong-mengIMPORTANT: env var yang ADA tetapi KOSONG tetap
        // diabaikan (fallback ke appsettings), dan nilai kosong di kedua sumber
        // berarti "belum diisi", bukan "kunci kosong".
        _serverKey = KonfigurasiMidtrans.ServerKey(config);

        // Sandbox default; production bila MIDTRANS_SANDBOX=false atau config UseSandbox=false.
        // Nilai KOSONG dianggap belum diisi — sama seperti ServerKey di atas.
        // Platform deploy kerap menyuntik variabel yang ada tetapi kosong; tanpa
        // aturan ini, MIDTRANS_SANDBOX="" di produksi diam-diam membuat server
        // menanyakan status ke SANDBOX, sehingga tidak ada pembayaran nyata yang
        // pernah cocok dan rekonsiliasi tampak "tidak menemukan apa-apa".
        var sandbox = KonfigurasiMidtrans.PakaiSandbox(config);
        _baseUrl = sandbox ? "https://app.sandbox.midtrans.com" : "https://app.midtrans.com";
        // Host TERPISAH untuk Core API (query status transaksi). Memakai host
        // Snap di sini menghasilkan 404 yang menyesatkan: halaman Snap memang
        // bukan API status.
        _baseUrlApi = sandbox ? "https://api.sandbox.midtrans.com" : "https://api.midtrans.com";

        // Batasi metode pembayaran bila diminta (mis. ["other_qris","bca_va"]).
        // Kosong = semua yang aktif di dashboard merchant.
        var enabled = config["Midtrans:EnabledPayments"];
        if (!string.IsNullOrWhiteSpace(enabled))
        {
            try
            {
                _enabledPayments = JsonSerializer.Deserialize<string[]>(enabled);
            }
            catch (JsonException ex)
            {
                ServerLog.MidtransEnabledPaymentsInvalid(_logger, ex, enabled);
            }
        }
    }

    /// <summary>Apakah layanan siap dipakai (ServerKey terisi).</summary>
    public bool Aktif => !string.IsNullOrWhiteSpace(_serverKey);

    /// <summary>Buat tagihan Snap dan kembalikan token + tautan pembayarannya.</summary>
    /// <param name="namaBarang">Nama desa &amp; paket; dipotong 50 karakter (batas Midtrans).</param>
    /// <param name="urlNotifikasi">
    /// URL notifikasi KHUSUS untuk transaksi ini. Bila diisi, dikirim sebagai
    /// header <c>X-Override-Notification</c> — Midtrans lalu mengirim
    /// notifikasi transaksi ini HANYA ke URL itu, bukan ke "Payment Notification
    /// URL" yang disetel di dashboard akun.
    ///
    /// <para>Dipakai jalur uji pembayaran: begitulah notifikasi sungguhan bisa
    /// diarahkan ke server tempat operator membuktikan alurnya, tanpa mengubah
    /// pengaturan akun (yang berlaku untuk semua transaksi).</para>
    ///
    /// <para>URL yang tidak valid TIDAK dikirim — request tetap dibuat, hanya
    /// notifikasinya mengikuti dashboard. Menolak seluruh transaksi demi header
    /// yang tidak bisa dipakai lebih berbahaya daripada menerima notifikasi di
    /// tempat lain.</para></param>
    public async Task<TagihanGateway?> BuatTagihanAsync(
        string orderId, long nominal, string namaBarang, string? urlNotifikasi = null)
    {
        if (!Aktif) return null;

        var namaItem = $"Langganan SuDesApp - {namaBarang}";
        if (namaItem.Length > 50) namaItem = namaItem[..50];

        var body = new Dictionary<string, object?>
        {
            ["transaction_details"] = new Dictionary<string, object?>
            {
                ["order_id"] = orderId,
                ["gross_amount"] = nominal
            },
            ["item_details"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "SUDES-BULANAN-1",
                    ["price"] = nominal,
                    ["quantity"] = 1,
                    ["name"] = namaItem
                }
            }
        };
        if (_enabledPayments is { Length: > 0 })
            body["enabled_payments"] = _enabledPayments;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/snap/v1/transactions");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverKey}:")));

        // Header notifikasi per-transaksi. Midtrans menerima URL absolut di
        // sini (bukan path relatif), jadi nilainya harus URL lengkap yang
        // bisa diakses dari internet.
        if (!string.IsNullOrWhiteSpace(urlNotifikasi))
        {
            if (KonfigurasiMidtrans.UrlValid(urlNotifikasi))
            {
                req.Headers.TryAddWithoutValidation("X-Override-Notification", urlNotifikasi.Trim());
            }
            else
            {
                ServerLog.MidtransNotifikasiUjiTidakValid(_logger, orderId, urlNotifikasi);
            }
        }

        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            ServerLog.MidtransSnapGagal(_logger, (int)resp.StatusCode, raw);
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var token = root.TryGetProperty("token", out var t) ? t.GetString() : null;
        var redirect = root.TryGetProperty("redirect_url", out var r) ? r.GetString() : null;
        return new TagihanGateway(orderId, nominal, redirect, token);
    }

    /// <summary>
    /// Verifikasi signature webhook Midtrans.
    /// signature_key harus = SHA512(order_id + status_code + gross_amount + ServerKey).
    /// Semua komponen dipakai persis seperti dikirim (string, termasuk nol di belakang koma).
    /// </summary>
    public bool VerifikasiSignature(string orderId, string statusCode, string grossAmount, string signatureKey)
    {
        if (!Aktif || string.IsNullOrEmpty(signatureKey)) return false;
        var input = string.Concat(orderId, statusCode, grossAmount, _serverKey);
        var expected = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();
        return FixedTimeEquals(expected, signatureKey.ToLowerInvariant());
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    /// <summary>
    /// Tanyakan status transaksi langsung ke Midtrans (Core API) — jalur
    /// backstop untuk webhook yang tidak pernah masuk atau datang tidak urut.
    ///
    /// GET {api}/v2/{order_id}/status dengan Basic auth ServerKey + ":".
    /// Tidak pernah melempar: 404, HTTP gagal, dan gangguan jaringan semuanya
    /// menjadi <see cref="HasilStatusMidtrans.TidakDiketahui"/>, karena "tidak
    /// bisa dipastikan" tidak boleh diperlakukan sama dengan "belum dibayar".
    /// </summary>
    public async Task<HasilStatusMidtrans> AmbilStatusAsync(string orderId, CancellationToken ct = default)
    {
        if (!Aktif) return HasilStatusMidtrans.TidakDiketahui("ServerKey Midtrans belum diisi.");
        if (string.IsNullOrWhiteSpace(orderId))
            return HasilStatusMidtrans.TidakDiketahui("order_id kosong.");

        // order_id dirangkai sebagai segmen URL, jadi harus di-escape; nilai
        // yang mengandung '#' atau '/' tidak boleh mengubah alamat endpoint.
        var url = $"{_baseUrlApi}/v2/{Uri.EscapeDataString(orderId)}/status";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverKey}:")));
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Midtrans juga membalas 404 sebelum pembeli memilih metode
                // pembayaran, jadi ini BUKAN bukti pembayaran gagal.
                return HasilStatusMidtrans.TidakDiketahui("Transaksi belum ada di Midtrans (404).");
            }

            if (!resp.IsSuccessStatusCode)
            {
                ServerLog.MidtransStatusGagal(_logger, orderId, (int)resp.StatusCode, raw);
                return HasilStatusMidtrans.TidakDiketahui($"Gateway membalas HTTP {(int)resp.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? Teks(string nama) =>
                root.TryGetProperty(nama, out var el)
                    ? (el.ValueKind == JsonValueKind.Number ? el.GetRawText() : el.GetString())
                    : null;

            var statusTransaksi = Teks("transaction_status");
            if (string.IsNullOrWhiteSpace(statusTransaksi))
            {
                // Core API Midtrans menjawab HTTP 200 Even untuk order yang
                // BELUM ada: badannya {"status_code":"404","status_message":
                // "Transaction doesn't exist."} (order Snap yang belum dibuka
                // pembeli). Jadi HTTP saja tidak cukup — jawaban tanpa
                // transaction_status tidak boleh diartikan "transaksi
                // ditemukan" (lihat kontrak HasilStatusMidtrans), dan tidak
                // boleh dianggap "belum dibayar" (rekonsiliasi menghitungnya
                // tidakPasti, bukan lunas).
                var kodeStatus = Teks("status_code");
                var pesanGateway = Teks("status_message");
                return HasilStatusMidtrans.TidakDiketahui(
                    string.IsNullOrWhiteSpace(kodeStatus)
                        ? "Gateway tidak mengirim status transaksi."
                        : $"Gateway menjawab status_code {kodeStatus}" +
                          (string.IsNullOrWhiteSpace(pesanGateway) ? "." : $": {pesanGateway}"));
            }

            return new HasilStatusMidtrans(
                Ditemukan: true,
                TransactionStatus: statusTransaksi,
                GrossAmount: Teks("gross_amount"),
                FraudStatus: Teks("fraud_status"),
                Pesan: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Termasuk timeout. Dihitung gagal, bukan lunas — dan tidak
            // menggagalkan order lain di putaran yang sama.
            ServerLog.MidtransQueryGagal(_logger, ex, orderId);
            return HasilStatusMidtrans.TidakDiketahui("Gagal menghubungi gateway: " + ex.Message);
        }
    }

    /// <summary>Status webhook yang berarti dana masuk.</summary>
    public static readonly HashSet<string> StatusBerhasil = new(StringComparer.OrdinalIgnoreCase)
    { "settlement", "capture" };
}
