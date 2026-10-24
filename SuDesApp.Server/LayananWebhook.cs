using System.Globalization;
using System.Text.Json;

namespace SuDesApp.Server;

/// <summary>Hasil pemrosesan satu notifikasi pembayaran.</summary>
/// <param name="Status">Kode HTTP untuk dibalas ke gateway.</param>
/// <param name="Duplikat">True bila order sudah pernah diproses (webhook ulang).</param>
/// <param name="Paket">Paket yang dibeli, bila ada.</param>
/// <param name="HariDitambah">Berapa hari akses yang ditambahkan.</param>
/// <param name="Pesan">Keterangan untuk log/ debugging.</param>
public readonly record struct HasilWebhook(
    int Status, bool Diterima, bool Duplikat = false,
    string? Paket = null, int? HariDitambah = null, string? Pesan = null);

/// <summary>
/// Pemroses webhook pembayaran Midtrans.
///
/// Dipisah dari Program.cs supaya aturannya bisa diuji tanpa HTTP server dan
/// tanpa database. Ini lapisan yang memberi hak akses, jadi setiap cabangnya
/// diuji — termasuk yang biasanya dilupakan: webhook ulang, nominal yang
/// tidak cocok, dan order yang tidak dikenal.
///
/// Urutan pemeriksaan itu sendiri yang penting dan TIDAK boleh dibalik:
/// 1. signature        — tanpa ini, siapa pun bisa memalsukan pembayaran
/// 2. tagihan harus ada  — menolak order yang tak pernah dibuat server
/// 3. nominal harus cocok  — menolak "bayar Rp1"
/// 4. perpanjangan idempoten — menolak pengulangan yang menambah hari
///
/// Langkah 2-4 ada di <see cref="SelesaikanAsync"/> sendirian, karena
/// rekonsiliasi ("payment reconciler") memakainya juga — jalur itu membaca
/// status langsung dari Midtrans sehingga tidak punya signature untuk
/// diverifikasi, tetapi aturan pemberian hak aksesnya harus identik. Satu
/// salinan aturan berarti satu tempat untuk salah, bukan dua.
/// </summary>
public sealed class LayananWebhook
{
    private readonly IRepositoryLangganan _repo;
    private readonly MidtransService _midtrans;
    private readonly PelacakWebhook? _pelacak;
    private readonly ILogger<LayananWebhook> _logger;

    public LayananWebhook(
        IRepositoryLangganan repo,
        MidtransService midtrans,
        ILogger<LayananWebhook> logger,
        PelacakWebhook? pelacak = null)
    {
        _repo = repo;
        _midtrans = midtrans;
        _pelacak = pelacak;
        _logger = logger;
    }

    /// <summary>
    /// Proses isi body notifikasi Midtrans.
    ///
    /// <para>Selain applying aturan, notifikasi ini dicatat sebagai jejak
    /// (lihat <see cref="PelacakWebhook"/>) supaya operator bisa melihat
    /// buktinya: notifikasi sungguhan datang atau tidak, signature-nya sah atau
    /// tidak, dan server menjawab apa. Pencatatannya TIDAK mengubah aturan
    /// sedikit pun — tanpa pelacak, hasil dan kode balasannya sama persis.</para>
    /// </summary>
    /// <param name="alamatIp">IP pengirim; hanya untuk ditampilkan.</param>
    /// <param name="userAgent">User-Agent pengirim; hanya untuk ditampilkan.</param>
    public async Task<HasilWebhook> ProsesAsync(string body, string? alamatIp = null, string? userAgent = null)
    {
        var waktu = DateTime.UtcNow;
        var orderId = "(tidak terbaca)";
        string? statusTransaksi = null, gross = null, fraud = null, tipeBayar = null;
        bool? signatureValid = null;

        // Semua pintu keluar dari fungsi ini lewat sini, jadi tidak ada
        // cabang yang bisa "berhasil" tanpa tercatat. Yang dicatat cukup
        // buktinya — isi mentah body tidak ikut supaya jejaknya tidak jadi
        // tempat menyimpan data pembayaran yang tidak perlu disimpan.
        HasilWebhook Catat(int status, bool diterima, bool duplikat, int? hari, string? pesan, string? paket = null)
        {
            if (_pelacak is not null)
            {
                _pelacak.Catat(new CatatanNotifikasi(
                    waktu, orderId, statusTransaksi, gross, fraud, tipeBayar,
                    signatureValid, status, diterima, duplikat, hari, pesan, alamatIp, userAgent));
            }

            ServerLog.WebhookMasuk(
                _logger, orderId, statusTransaksi,
                signatureValid switch { true => "sah", false => "tidak sah", _ => "belum dinilai" },
                status, pesan);

            return new HasilWebhook(status, diterima, duplikat, paket, hari, pesan);
        }

        JsonElement notif;
        try { notif = JsonDocument.Parse(body).RootElement; }
        catch { return Catat(400, false, false, null, "Body bukan JSON valid."); }

        if (!notif.TryGetProperty("order_id", out var oi) ||
            !notif.TryGetProperty("transaction_status", out var ts))
            return Catat(400, false, false, null, "order_id dan transaction_status wajib.");

        orderId = oi.GetString() ?? "";
        statusTransaksi = ts.GetString() ?? "";

        // ===== 1. Signature WAJIB valid =====
        // Tidak ada cabang yang melewatinya — termasuk saat mode demo.
        // Tanpa ServerKey, VerifikasiSignature selalu false, jadi webhook
        // otomatis ditolak: gagal tertutup, bukan terbuka.
        var sig = notif.TryGetProperty("signature_key", out var sk) ? sk.GetString() ?? "" : "";
        var statusCode = notif.TryGetProperty("status_code", out var sc) ? sc.GetString() ?? "" : "";
        gross = notif.TryGetProperty("gross_amount", out var ga)
            ? (ga.ValueKind == JsonValueKind.Number ? ga.GetRawText() : ga.GetString() ?? "")
            : "";

        if (!_midtrans.VerifikasiSignature(orderId, statusCode, gross, sig))
        {
            signatureValid = false;
            ServerLog.WebhookSignatureDitolak(_logger, orderId);
            return Catat(403, false, false, null, "Signature tidak valid.");
        }
        signatureValid = true;

        var fraudStatus = notif.TryGetProperty("fraud_status", out var fs) ? fs.GetString() : null;
        fraud = fraudStatus;
        tipeBayar = notif.TryGetProperty("payment_type", out var pt) ? pt.GetString() : null;

        // Langkah 2-4 ada di SelesaikanAsync supaya rekonsiliasi (yang membaca
        // status langsung dari Midtrans, tanpa notifikasi) memakai aturan yang
        // PERSIS sama — bukan salinan kedua yang bisa menyimpang.
        var hasil = await SelesaikanAsync(orderId, statusTransaksi, gross, fraudStatus, asal: "webhook")
            .ConfigureAwait(false);

        return Catat(hasil.Status, hasil.Diterima, hasil.Duplikat, hasil.HariDitambah, hasil.Pesan, hasil.Paket);
    }

    /// <summary>
    /// Terapkan aturan pembayaran pada satu order: tagihan harus ada, nominal
    /// harus cocok, status harus benar-benar berduit, lalu perpanjangan yang
    /// idempoten.
    ///
    /// Dipanggil dari DUA jalur yang kredibilitasnya sudah terbukti lebih dulu:
    /// <list type="bullet">
    /// <item>webhook — setelah signature SHA512 Midtrans diverifikasi;</item>
    /// <item>rekonsiliasi — setelah status diminta langsung ke Midtrans memakai
    /// ServerKey (tidak ada yang bisa memalsukannya, tidak ada signature yang
    /// perlu dipercaya dari pihak ketiga).</item>
    /// </list>
    /// Karena itu fungsi ini TIDAK memeriksa signature: pemanggilnya yang wajib
    /// sudah memastikannya. Keuntungan memusatkannya di sini: batas nominal dan
    /// penjaga idempotensi hanya ada satu, jadi jalur backstop tidak bisa lebih
    /// longgar daripada jalur utama.
    /// </summary>
    /// <param name="fraudStatus">
    /// <c>fraud_status</c> Midtrans (kartu): "accept" / "challenge" / "deny".
    /// Null bila tidak dikirim (pembayaran non-kartu) — diperlakukan sebagai lolos.
    /// </param>
    /// <param name="asal">"webhook" atau "rekonsiliasi" — hanya untuk log.</param>
    public async Task<HasilWebhook> SelesaikanAsync(
        string orderId, string status, string grossAmount, string? fraudStatus = null, string asal = "webhook")
    {
        // ===== 2. Tagihan harus benar-benar dibuat server =====
        var tagihan = await _repo.CariTagihanAsync(orderId).ConfigureAwait(false);
        if (tagihan is null)
        {
            ServerLog.WebhookOrderTakDikenal(_logger, asal, orderId);
            return new HasilWebhook(404, false, Pesan: $"Order {orderId} tidak dikenal.");
        }
        var (_, paket, nominal, hari) = tagihan.Value;

        // ===== 3. Nominal harus sama dengan yang ditagihkan =====
        if (!CokNominal(grossAmount, nominal))
        {
            ServerLog.WebhookNominalTidakCocok(_logger, asal, grossAmount, nominal, orderId);
            return new HasilWebhook(400, false, Pesan: "Nominal tidak sesuai tagihan.");
        }

        // ===== 4. Hanya status berduit yang memperpanjang =====
        if (!MidtransService.StatusBerhasil.Contains(status))
            return new HasilWebhook(200, true, Pesan: $"Status '{status}' tidak memperpanjang.");

        // Kartu yang ditandai FDS (challenge/deny) BELUM menjadi uang: hak
        // akses tidak diberikan sampai statusnya berubah menjadi settlement
        // yang diterima. Pembayaran non-kartu tidak mengirim fraud_status,
        // jadi ketiadaan field itu bukan alasan untuk menolak.
        if (!string.IsNullOrEmpty(fraudStatus) &&
            !fraudStatus.Equals("accept", StringComparison.OrdinalIgnoreCase))
        {
            ServerLog.WebhookFraudBelumAman(_logger, asal, orderId, status, fraudStatus);
            return new HasilWebhook(200, true, Pesan: $"fraud_status '{fraudStatus}' belum aman — tidak memperpanjang.");
        }

        var hasil = await _repo.PerpanjangAsync(orderId, hari, status).ConfigureAwait(false);
        if (!hasil.Diterima)
            return new HasilWebhook(404, false, Pesan: $"Order {orderId} tidak ditemukan.");

        // Saat duplikat, jangan laporkan seolah-olah hari baru ditambahkan:
        // tidak ada akses yang bertambah. Yang perlu diberitahukan hanya bahwa
        // order ini sudah pernah diproses.
        return new HasilWebhook(200, true, hasil.Duplikat,
            paket, hasil.Duplikat ? null : hari);
    }

    /// <summary>
    /// Cocokkan <c>gross_amount</c> dengan nominal tagihan. Midtrans mengirim
    /// angka sebagai teks ("50000.00", kadang tanpa pecahan), jadi dibandingkan
    /// sebagai angka — bukan string — dengan toleransi setengah rupiah.
    /// </summary>
    internal static bool CokNominal(string grossAmount, long harga) =>
        double.TryParse(grossAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var nilai)
        && Math.Abs(nilai - harga) < 0.5;
}
