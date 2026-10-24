namespace SuDesApp.Server;

/// <summary>
/// Hasil satu penandaan lunas manual. <see cref="Status"/> adalah kode HTTP
/// yang dipakai endpoint admin; <see cref="BerlakuSampai"/> (UTC) diisi hanya
/// saat berhasil — itulah tanggal yang diucapkan kembali ke operator.
/// </summary>
public sealed record HasilTandaiLunas(
    int Status,
    string? Pesan = null,
    string? OrderId = null,
    string? KodeAktivasi = null,
    string? Paket = null,
    long Nominal = 0,
    int Hari = 0,
    DateTime? BerlakuSampai = null,
    string? TipeBukti = null,
    int UkuranBukti = 0)
{
    public bool Berhasil => Status == 200;

    /// <summary>True bila pembayaran ini ikut menyimpan lampiran bukti transfer.</summary>
    public bool AdaBukti => UkuranBukti > 0;

    public static HasilTandaiLunas Ditolak(int status, string pesan) => new(status, pesan);
}

/// <summary>
/// Pembukuan pembayaran MANUAL: uang sudah diterima operator (QRIS statis di
/// layar pembayaran aplikasi desa, atau transfer), bukan lewat gateway.
///
/// Kenapa dipisah dari <see cref="LayananWebhook"/>: webhook MEMVERIFIKASI
/// sesuatu (signature Midtrans, lalu status &amp; nominal), sedangkan di sini
/// tidak ada apa pun untuk diverifikasi — yang ada adalah keputusan operator
/// yang memegang bukti transfer. Yang TIDAK dipisah adalah akibatnya: kelas ini
/// memanggil <see cref="IRepositoryLangganan.PerpanjangAsync"/> yang sama
/// dengan webhook, jadi idempotensi (satu order = satu perpanjangan) dan
/// perpanjangan dari <c>max(berlaku_sampai, now())</c> tetap satu aturan.
///
/// Aturan yang dijaga di sini:
/// <list type="number">
/// <item>kode aktivasi wajib dan harus dikenal — tanpa itu tidak ada desa yang
/// bisa diperpanjang;</item>
/// <item>paket tak dikenal jatuh ke paket bawaan, BUKAN paket gratis (sama
/// seperti <see cref="LayananTagihan"/>);</item>
/// <item>nominal boleh berbeda dari katalog karena ia mencatat uang yang
/// benar-benar masuk, tetapi harus lebih dari nol dan perbedaannya dicatat di
/// log supaya bisa ditelusuri;</item>
/// <item>order id berawalan <see cref="AwalanOrder"/> sehingga riwayat
/// pembayaran membedakan pembayaran gateway dari pembukuan operator;</item>
/// <item>lampiran bukti transfer boleh ada atau tidak, tetapi bila ada ia harus
/// lolos <see cref="BuktiTransfer"/> — dan lampiran yang ditolak membatalkan
/// SELURUH penandaan lunas, bukan hanya lampirannya. Kalau tidak begitu,
/// operator bisa mengira bukti sudah tersimpan padahal akses desa sudah
/// bertambah tanpa jejak gambarnya.</item>
/// </list>
/// </summary>
public sealed class LayananPembayaranManual
{
    /// <summary>Nilai kolom <c>pembayaran.status</c> untuk pembukuan operator.</summary>
    public const string StatusPembayaran = "manual";

    /// <summary>Awalan order id pembukuan manual — jejak yang terlihat di riwayat.</summary>
    public const string AwalanOrder = "MANUAL";

    private readonly IRepositoryLangganan _repo;
    private readonly KatalogPaket _katalog;
    private readonly ILogger<LayananPembayaranManual> _logger;
    private readonly TimeProvider _waktu;

    public LayananPembayaranManual(
        IRepositoryLangganan repo,
        KatalogPaket katalog,
        ILogger<LayananPembayaranManual> logger,
        TimeProvider? waktu = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _katalog = katalog ?? throw new ArgumentNullException(nameof(katalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _waktu = waktu ?? TimeProvider.System;
    }

    /// <summary>
    /// Tandai lunas satu desa, lalu perpanjang masa aktifnya sekarang juga.
    /// Mengembalikan kode HTTP agar endpoint hanya meneruskan hasilnya.
    /// </summary>
    /// <param name="paket">Kode paket; null/tak dikenal → paket bawaan katalog.</param>
    /// <param name="nominal">Uang yang diterima; null → harga katalog paket itu.</param>
    /// <param name="catatan">Referensi bebas (mis. "QRIS 17/10 21:04") yang
    /// ikut tersimpan di kolom <c>pembayaran.catatan</c> — jadi bukti transfer
    /// tetap bisa ditelusuri dari riwayat pembayaran desa dan ekspor CSV, bukan
    /// hanya terbaca di log server.</param>
    /// <param name="teksBuktiBase64">Lampiran gambar bukti transfer dari
    /// pembukuan manual (base64 atau data URL hasil pembaca berkas di peramban),
    /// boleh kosong. Isinya dinilai <see cref="BuktiTransfer"/> SEBELUM apa pun
    /// dicatat: lampiran yang tidak sah ditolak tanpa memperpanjang langganan.</param>
    /// <param name="namaBukti">Nama berkas asli untuk tampilan; boleh kosong.</param>
    public async Task<HasilTandaiLunas> TandaiLunasAsync(
        string kodeAktivasi, string? paket, long? nominal, string? catatan,
        string? teksBuktiBase64 = null, string? namaBukti = null)
    {
        // Lampiran dinilai lebih dulu: ia satu-satunya bagian yang datang dari
        // berkas mentah, dan menolaknya belakangan berarti langganan sudah
        // terlanjur diperpanjang untuk bukti yang tidak tersimpan.
        var lampiran = BuktiTransfer.Baca(teksBuktiBase64, namaBukti);
        if (!lampiran.Diterima)
        {
            return HasilTandaiLunas.Ditolak(400, lampiran.Pesan!);
        }

        var bukti = lampiran.Bukti;
        var kode = kodeAktivasi?.Trim() ?? string.Empty;
        if (kode.Length == 0)
            return HasilTandaiLunas.Ditolak(400, "Kode aktivasi wajib diisi.");

        var pelanggan = await _repo.CariByKodeAsync(kode).ConfigureAwait(false);
        if (pelanggan is null)
            return HasilTandaiLunas.Ditolak(404, $"Desa '{kode}' tidak dikenal.");

        var pilihan = _katalog.Cari(paket) ?? _katalog.Bawaan;

        var uang = nominal ?? pilihan.Harga;
        if (uang <= 0)
            return HasilTandaiLunas.Ditolak(400, "Nominal yang diterima harus lebih dari nol.");

        var orderId = BuatOrderId(pelanggan.Id, _waktu.GetUtcNow().UtcDateTime);

        // Tagihan ditulis lebih dulu dengan nominal yang dicatat operator:
        // PerpanjangAsync membaca nominal & durasi dari tabel tagihan, jadi
        // angka pembukuan dan angka yang memperpanjang tidak bisa berbeda.
        await _repo.SimpanTagihanAsync(pelanggan.Id, orderId, pilihan, uang).ConfigureAwait(false);

        var hasil = await _repo.PerpanjangAsync(orderId, pilihan.Hari, StatusPembayaran, catatan, bukti)
            .ConfigureAwait(false);
        if (!hasil.Diterima)
        {
            ServerLog.PembayaranManualGagal(_logger, kode, orderId);
            return HasilTandaiLunas.Ditolak(500, "Perpanjangan manual gagal diterapkan — lihat log server.");
        }

        var setelah = await _repo.CariByKodeAsync(kode).ConfigureAwait(false);
        var berlaku = setelah?.Langganan?.BerlakuSampai;

        if (uang != pilihan.Harga)
        {
            ServerLog.PembayaranManualSelisih(_logger, kode, uang, pilihan.Kode, pilihan.Harga);
        }

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            ServerLog.PembayaranManualLunas(
                _logger, kode, pelanggan.NamaDesa, pilihan.Kode, uang, pilihan.Hari, orderId,
                string.IsNullOrWhiteSpace(catatan) ? string.Empty : $", catatan: {catatan.Trim()}",
                bukti is null ? string.Empty : $", lampiran: {BuktiTransfer.Ringkas(bukti)}");
        }

        return new HasilTandaiLunas(
            Status: 200,
            Pesan: bukti is null
                ? "Pembayaran dicatat dan langganan diperpanjang."
                : $"Pembayaran dicatat dengan lampiran bukti ({BuktiTransfer.Ringkas(bukti)}) " +
                  "dan langganan diperpanjang.",
            OrderId: orderId,
            KodeAktivasi: kode,
            Paket: pilihan.Kode,
            Nominal: uang,
            Hari: pilihan.Hari,
            BerlakuSampai: berlaku,
            TipeBukti: bukti?.Tipe,
            UkuranBukti: bukti?.Isi.Length ?? 0);
    }

    /// <summary>
    /// Order id pembukuan manual: <c>MANUAL-&lt;8 huruf id desa&gt;-&lt;yyyyMMdd&gt;-&lt;6 heksa acak&gt;</c>.
    /// Bentuknya mengikuti order id tagihan supaya enak dibaca, dengan awalan
    /// berbeda supaya jelas bukan transaksi gateway. Bagian acak bisa
    /// disuntikkan pada uji agar hasilnya dapat diprediksi.
    /// </summary>
    public static string BuatOrderId(string pelangganId, DateTime waktuUtc, string? acak = null)
    {
        var id = pelangganId ?? string.Empty;
        var prefix = id.Length > 8 ? id[..8] : id;
        var tanggal = (waktuUtc.Kind == DateTimeKind.Utc
                ? waktuUtc
                : DateTime.SpecifyKind(waktuUtc, DateTimeKind.Utc))
            .ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var sufiks = acak ?? Guid.NewGuid().ToString("N")[..6];
        return $"{AwalanOrder}-{prefix}-{tanggal}-{sufiks}";
    }
}
