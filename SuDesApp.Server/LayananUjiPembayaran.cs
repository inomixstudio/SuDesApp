namespace SuDesApp.Server;

/// <summary>
/// Opsi halaman uji pembayaran: ke mana notifikasi transaksi uji dikirim, dan
/// server ini sedang bicara ke sandbox atau production.
/// </summary>
/// <param name="UrlNotifikasi">
/// URL lengkap yang menerima notifikasi transaksi uji (header
/// <c>X-Override-Notification</c>). Null = tidak diatur, jadi notifikasi
/// mengikuti "Payment Notification URL" tingkat akun di dashboard Midtrans.
/// </param>
/// <param name="Sandbox">True = sandbox, false = production.</param>
public sealed record OpsiUjiPembayaran(string? UrlNotifikasi, bool Sandbox)
{
    /// <summary>Label lingkungan untuk ditampilkan di halaman uji.</summary>
    public string Lingkungan => Sandbox ? "sandbox" : "production";

    public static OpsiUjiPembayaran DariKonfigurasi(IConfiguration config) =>
        new(KonfigurasiMidtrans.BangunUrlWebhook(KonfigurasiMidtrans.UrlNotifikasiDasar(config)),
            KonfigurasiMidtrans.PakaiSandbox(config));
}

/// <summary>Permintaan membuat satu tagihan uji (transaksi Snap sungguhan).</summary>
/// <param name="KodeAktivasi">Kode desa uji. Bila belum terdaftar, desanya didaftarkan otomatis.</param>
/// <param name="Paket">Kode paket dari katalog server; tidak dikenal → paket bawaan.</param>
/// <param name="OverrideNotifikasi">
/// True (bawaan) = arahkan notifikasi ke <see cref="OpsiUjiPembayaran.UrlNotifikasi"/>
/// bila itu diatur. False = pakai URL dashboard apa adanya.
/// </param>
public sealed record PermintaanTagihanUji(
    string? KodeAktivasi,
    string? Paket = null,
    bool? OverrideNotifikasi = null);

/// <summary>Hasil pembuatan tagihan uji.</summary>
/// <param name="Status">Kode HTTP (200/400/404/500/503).</param>
/// <param name="OrderId">Order id transaksi Snap.</param>
/// <param name="UrlNotifikasi">URL yang akan menerima notifikasi transaksi ini.</param>
/// <param name="NotifikasiDialihkan">True = notifikasi diarahkan lewat header (bukan URL dashboard).</param>
/// <param name="DesaBaru">True = desa uji baru dibuat bersama tagihannya.</param>
public sealed record HasilTagihanUji(
    int Status,
    string? OrderId = null,
    string? KodeAktivasi = null,
    string? NamaDesa = null,
    string? PaketKode = null,
    string? PaketNama = null,
    long Nominal = 0,
    int Hari = 0,
    string? Token = null,
    string? LinkPembayaran = null,
    string? Lingkungan = null,
    string? UrlNotifikasi = null,
    bool NotifikasiDialihkan = false,
    bool DesaBaru = false,
    string? Pesan = null);

/// <summary>
/// Ringkasan status satu transaksi uji: keadaan di gateway, jejak notifikasi
/// yang masuk, dan akibatnya pada langganan desanya.
/// </summary>
public sealed record StatusUjiPembayaran(
    string OrderId,
    string KodeAktivasi,
    string NamaDesa,
    string PaketKode,
    string PaketNama,
    long Nominal,
    int Hari,
    DateTime? BerlakuSampai,
    string Lingkungan,
    string? UrlNotifikasi,
    bool GatewayDitemukan,
    string? TransactionStatus,
    string? GrossGateway,
    string? FraudGateway,
    string? CatatanGateway,
    bool WebhookMasuk,
    bool WebhookDiterima,
    IReadOnlyList<CatatanNotifikasi> Notifikasi,
    IReadOnlyList<InfoPembayaran> Pembayaran);

/// <summary>
/// Alur uji pembayaran sungguhan: buat transaksi Snap (production bila
/// servernya production), bayar lewat halaman Midtrans, lalu lihat notifikasi
/// aslinya sampai server membalas dan perpanjangan langganan terjadi.
///
/// <para>Dipisah dari <see cref="LayananTagihan"/> karena aturan转账nya beda:
/// jalur ini boleh MEMBUAT tagihan uji untuk desa uji, boleh mengarahkan
/// notifikasi ke URL khusus, dan harus melaporkan keadaan gateway + jejak
/// webhook. Aturan yang TIDAK boleh beda — harga selalu dari katalog, tagihan
/// dicatat sebelum gateway dipanggil, dan perpanjangan lewat
/// <c>PerpanjangAsync</c> yang sama — tetap dipakai apa adanya, bukan
/// disalin.</para>
///
/// <para>Semua endpoint pemanggilnya berada di bawah /admin/*, jadi tidak ada
/// jalan membuka tagihan uji tanpa token operator.</para>
/// </summary>
public sealed class LayananUjiPembayaran
{
    /// <summary>Berapa lama hasil status gateway dipakai ulang sebelum ditanyakan lagi.</summary>
    private static readonly TimeSpan JedaStatusGateway = TimeSpan.FromSeconds(5);

    private readonly IRepositoryLangganan _repo;
    private readonly KatalogPaket _katalog;
    private readonly IPembuatTagihan _gateway;
    private readonly IPenyediaStatusPembayaran _status;
    private readonly PelacakWebhook _pelacak;
    private readonly LayananAdmin _admin;
    private readonly OpsiUjiPembayaran _opsi;
    private readonly ILogger<LayananUjiPembayaran> _logger;
    private readonly TimeProvider _waktu;

    private readonly Dictionary<string, (DateTime Waktu, HasilStatusMidtrans Status)> _cacheGateway =
        new(StringComparer.Ordinal);
    private readonly object _kunciCache = new();

    public LayananUjiPembayaran(
        IRepositoryLangganan repo,
        KatalogPaket katalog,
        IPembuatTagihan gateway,
        IPenyediaStatusPembayaran status,
        PelacakWebhook pelacak,
        LayananAdmin admin,
        OpsiUjiPembayaran opsi,
        ILogger<LayananUjiPembayaran> logger,
        TimeProvider? waktu = null)
    {
        _repo = repo;
        _katalog = katalog;
        _gateway = gateway;
        _status = status;
        _pelacak = pelacak;
        _admin = admin;
        _opsi = opsi;
        _logger = logger;
        _waktu = waktu ?? TimeProvider.System;
    }

    /// <summary>URL yang menerima notifikasi transaksi uji (null = URL dashboard).</summary>
    public string? UrlNotifikasi => _opsi.UrlNotifikasi;

    /// <summary>Label lingkungan gateway: "production" atau "sandbox".</summary>
    public string Lingkungan => _opsi.Lingkungan;

    /// <summary>
    /// Buat satu transaksi uji sungguhan untuk desa uji.
    ///
    /// <para>Urutannya sama dengan <see cref="LayananTagihan"/>: harga dari
    /// katalog, tagihan dicatat DULU, baru gateway dipanggil — supaya order id
    /// yang dipakai gateway selalu punya padanan di tabel tagihan, dan webhook
    /// tidak pernah menabrak order yang tak dikenal.</para>
    /// </summary>
    public async Task<HasilTagihanUji> BuatTagihanAsync(
        PermintaanTagihanUji req, CancellationToken ct = default)
    {
        var kode = (req.KodeAktivasi ?? "").Trim();
        if (kode.Length == 0)
            return new HasilTagihanUji(400, Pesan: "Kode aktivasi wajib diisi.");

        // Tanpa ServerKey tidak ada transaksi sungguhan yang bisa dibuat, dan
        // tautan demo bukan uji. Ini ditolak lebih awal supaya operator tidak
        // mengira sudah menguji sesuatu.
        if (!_gateway.Aktif)
            return new HasilTagihanUji(503, Pesan:
                "ServerKey Midtrans belum diisi — tagihan uji butuh gateway aktif (MIDTRANS_SERVER_KEY).");

        // Desa uji: pakai yang sudah ada, atau daftarkan baru. Pendaftaran
        // memakai LayananAdmin yang sama supaya validasi & log jejaknya sama
        // dengan pendaftaran manual (jejak "desa uji" harus kelihatan di log).
        var pelanggan = await _repo.CariByKodeAsync(kode);
        var desaBaru = false;
        if (pelanggan is null)
        {
            pelanggan = await _admin.DaftarkanAsync(kode, "Desa Uji Pembayaran", "", 1);
            desaBaru = true;
        }

        // Harga & durasi SELALU dari katalog server; klien hanya memilih nama
        // paket. Kalau nominal boleh dikirim, orang bisa bayar Rp1 untuk
        // sebulan akses — persis yang tidak boleh terjadi di jalur uji.
        var paket = _katalog.Cari(req.Paket) ?? _katalog.Bawaan;
        var orderId = LayananTagihan.BuatOrderId(pelanggan.Id, _waktu.GetUtcNow().UtcDateTime);
        await _repo.SimpanTagihanAsync(pelanggan.Id, orderId, paket);

        // Notifikasi transaksi ini boleh diarahkan ke URL khusus lewat header
        // X-Override-Notification. Tanpa itu, notifikasi jatuh ke "Payment
        // Notification URL" di dashboard akun — yang untuk uji lokal biasanya
        // tidak menunjuk ke server tempat operator membuktikan alurnya.
        var dialihkan = req.OverrideNotifikasi ?? true;
        var urlNotifikasi = dialihkan ? _opsi.UrlNotifikasi : null;
        if (dialihkan && urlNotifikasi is null)
            ServerLog.UjiTanpaUrlNotifikasi(_logger, orderId);

        var tagihan = await _gateway.BuatTagihanAsync(
            orderId, paket.Harga, $"{pelanggan.NamaDesa} - {paket.Nama}", urlNotifikasi);
        if (tagihan is null)
        {
            ServerLog.TagihanGatewayGagal(_logger, orderId, paket.Kode, paket.Harga);
            return new HasilTagihanUji(
                500, OrderId: orderId, KodeAktivasi: kode, NamaDesa: pelanggan.NamaDesa,
                PaketKode: paket.Kode, PaketNama: paket.Nama, Nominal: paket.Harga, Hari: paket.Hari,
                Lingkungan: Lingkungan, UrlNotifikasi: urlNotifikasi,
                Pesan: "Pembuatan transaksi Snap gagal — lihat log server.");
        }

        ServerLog.UjiTagihanDibuat(_logger, orderId, kode, paket.Harga, Lingkungan, urlNotifikasi);

        return new HasilTagihanUji(
            200,
            OrderId: orderId,
            KodeAktivasi: kode,
            NamaDesa: pelanggan.NamaDesa,
            PaketKode: paket.Kode,
            PaketNama: paket.Nama,
            Nominal: paket.Harga,
            Hari: paket.Hari,
            Token: tagihan.Token,
            LinkPembayaran: tagihan.RedirectUrl,
            Lingkungan: Lingkungan,
            UrlNotifikasi: urlNotifikasi ?? "(URL dashboard akun Midtrans)",
            NotifikasiDialihkan: urlNotifikasi is not null,
            DesaBaru: desaBaru,
            Pesan: "Bayar di halaman Midtrans sampai lunas, lalu tunggu notifikasi masuk.");
    }

    /// <summary>
    /// Keadaan satu transaksi uji: apa yang said gateway, notifikasi apa saja
    /// yang masuk, dan sudah berapa hari akses desanya bertambah.
    /// Null bila order id-nya tidak dikenal server.
    /// </summary>
    public async Task<StatusUjiPembayaran?> AmbilStatusAsync(
        string? orderId, CancellationToken ct = default)
    {
        var order = (orderId ?? "").Trim();
        if (order.Length == 0) return null;

        var tagihan = await _repo.CariTagihanAsync(order);
        if (tagihan is null) return null;
        var (pelangganId, paketKode, nominal, hari) = tagihan.Value;

        // Desa & langganannya: daftar ini lintas-tenant, jadi dipakai hanya di
        // endpoint admin bertoken.
        var daftar = await _repo.DaftarPelangganAsync();
        var desa = daftar.FirstOrDefault(d => string.Equals(d.Id, pelangganId, StringComparison.Ordinal));
        var kodeDesa = desa?.KodeAktivasi ?? "";

        var pembayaran = kodeDesa.Length > 0
            ? await _repo.DaftarPembayaranAsync(kodeDesa) ?? (IReadOnlyList<InfoPembayaran>)Array.Empty<InfoPembayaran>()
            : Array.Empty<InfoPembayaran>();

        var status = await AmbilStatusGatewayAsync(order, ct);
        var notifikasi = _pelacak.Untuk(order);

        return new StatusUjiPembayaran(
            OrderId: order,
            KodeAktivasi: kodeDesa,
            NamaDesa: desa?.NamaDesa ?? "",
            PaketKode: paketKode,
            PaketNama: _katalog.Cari(paketKode)?.Nama ?? paketKode,
            Nominal: nominal,
            Hari: hari,
            BerlakuSampai: desa?.BerlakuSampai,
            Lingkungan: Lingkungan,
            UrlNotifikasi: _opsi.UrlNotifikasi ?? "(URL dashboard akun Midtrans)",
            GatewayDitemukan: status.Ditemukan,
            TransactionStatus: status.TransactionStatus,
            GrossGateway: status.GrossAmount,
            FraudGateway: status.FraudStatus,
            CatatanGateway: status.Pesan,
            WebhookMasuk: notifikasi.Count > 0,
            WebhookDiterima: notifikasi.Any(n => n.Diterima),
            Notifikasi: notifikasi,
            Pembayaran: pembayaran);
    }

    /// <summary>
    /// Status transaksi di gateway, ditanyakan paling sering tiap 5 detik.
    ///
    /// <para>Halaman uji menyegarkan tampilan tiap beberapa detik, dan setiap
    /// penyegaran yang menembak Core API Midtrans bisa kena batas laju API
    /// merchant. Karena itu hasil terakhir dipakai ulang selama
    /// <see cref="JedaStatusGateway"/> — untuk uji "menunggu notifikasi" yang
    /// berjalan puluhan menit, ini selisih antara API normal dan Too Many
    /// Requests.</para>
    /// </summary>
    private async Task<HasilStatusMidtrans> AmbilStatusGatewayAsync(string orderId, CancellationToken ct)
    {
        var sekarang = _waktu.GetUtcNow().UtcDateTime;

        lock (_kunciCache)
        {
            if (_cacheGateway.TryGetValue(orderId, out var cached) &&
                sekarang - cached.Waktu < JedaStatusGateway)
                return cached.Status;
        }

        var status = await _status.AmbilStatusAsync(orderId, ct);

        lock (_kunciCache)
        {
            _cacheGateway[orderId] = (sekarang, status);
        }

        return status;
    }
}
