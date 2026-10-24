namespace SuDesApp.Server;

/// <summary>
/// Hasil satu permintaan pembuatan tagihan.
/// </summary>
/// <param name="Status">Kode HTTP (200/400/404, atau 500 bila gateway gagal).</param>
/// <param name="OrderId">Order id yang juga tersimpan di tabel tagihan.</param>
/// <param name="Paket">Paket yang dipakai (selalu dari katalog server).</param>
/// <param name="Token">Token Snap, bila gateway aktif.</param>
/// <param name="LinkPembayaran">Tautan pembayaran; di mode demo berisi tautan contoh.</param>
/// <param name="Mode">"midtrans" atau "demo".</param>
public sealed record HasilTagihanBaru(
    int Status,
    string? OrderId = null,
    PaketLangganan? Paket = null,
    string? Token = null,
    string? LinkPembayaran = null,
    string? Mode = null,
    string? KodeAktivasi = null,
    string? Pesan = null)
{
    public static HasilTagihanBaru Ditolak(int status, string pesan) => new(status, Pesan: pesan);

    /// <summary>True bila tagihan gagal dibuat di gateway (bukan permintaan yang salah).</summary>
    public bool GagalGateway => Status == 500;
}

/// <summary>
/// Aturan pembuatan tagihan — dipindahkan keluar dari <c>Program.cs</c> supaya
/// pemilihan harga bisa diuji seperti aturan pembayaran lainnya.
///
/// Aturan yang dijaga di sini:
/// <list type="number">
/// <item>dua jalur: kode aktivasi TERISI = perpanjang (harus dikenal, tagihan
/// adalah kredensial); kode KOSONG = berlangganan awal — wajib membawa
/// NamaDesa, desa baru didaftarkan dengan kode otomatis yang tidak
/// memberi akses sampai pembayaran lunas;</item>
/// <item><b>harga &amp; durasi selalu dari katalog server</b> — permintaan klien
/// hanya memilih NAMA paket. Kode paket yang tidak dikenal jatuh ke paket
/// bawaan, bukan ke paket gratis;</item>
/// <item>tagihan dicatat SEBELUM gateway dipanggil, sehingga order id yang
/// dipakai gateway selalu punya padanan di tabel <c>tagihan</c> — webhook dan
/// rekonsiliasi menolak order yang tidak ada di sana, jadi urutan ini yang
/// membuat pembayaran tidak pernah nyangkut tak tercatat.</item>
/// </list>
///
/// Yang <b>tidak</b> dilakukan di sini: menentukan boleh/tidaknya desa membayar
/// (desa yang belum lunas justru harus bisa), dan memberi akses —
/// perpanjangan tetap hanya terjadi lewat webhook/rekonsiliasi.
/// </summary>
public sealed class LayananTagihan
{
    private readonly IRepositoryLangganan _repo;
    private readonly KatalogPaket _katalog;
    private readonly IPembuatTagihan _gateway;
    private readonly ILogger<LayananTagihan> _logger;
    private readonly TimeProvider _waktu;
    private readonly LayananAdmin _pendaftarDesa;

    public LayananTagihan(
        IRepositoryLangganan repo,
        KatalogPaket katalog,
        IPembuatTagihan gateway,
        ILogger<LayananTagihan> logger,
        TimeProvider? waktu = null,
        LayananAdmin? pendaftarDesa = null)
    {
        _repo = repo;
        _katalog = katalog;
        _gateway = gateway;
        _logger = logger;
        _waktu = waktu ?? TimeProvider.System;
        // Jalur "berlangganan awal" mendaftarkan desa baru dengan kode
        // otomatis — aturan pembuatan kode unik dipakai dari LayananAdmin
        // supaya aturannya tetap SATU salinan (percobaan terbatas, log
        // pendaftaran). Pendaftaran di sini TIDAK memberi akses: langganan
        // desa baru masih kosong dan hanya menyala setelah pembayaran
        // lunas lewat webhook/rekonsiliasi.
        _pendaftarDesa = pendaftarDesa ?? throw new ArgumentNullException(nameof(pendaftarDesa));
    }

    public async Task<HasilTagihanBaru> BuatAsync(BuatTagihanRequest req, CancellationToken ct = default)
    {
        // Dua jalur pembayaran (lihat BuatTagihanRequest):
        //   kode terisi → PERPANJANG (desa sudah terdaftar);
        //   kode kosong → BERLANGGANAN AWAL (desa baru, kode diterbitkan).
        Pelanggan pelanggan;
        string? kodeBaru = null;
        if (string.IsNullOrWhiteSpace(req.KodeAktivasi))
        {
            if (string.IsNullOrWhiteSpace(req.NamaDesa))
                return HasilTagihanBaru.Ditolak(400,
                    "Untuk berlangganan awal, isi nama desa (belum punya kode aktivasi). " +
                    "Desa yang sudah punya kode, isi kodenya untuk perpanjangan.");

            var nama = req.NamaDesa.Trim();
            if (nama.Length > 120)
                return HasilTagihanBaru.Ditolak(400, "Nama desa terlalu panjang (maksimum 120 karakter).");

            try
            {
                // Pendaftaran MEMBERI data pelanggan, TIDAK memberi akses:
                // langganan desa baru masih kosong; masa aktifnya baru mulai
                // setelah pembayaran lunas lewat webhook/rekonsiliasi.
                pelanggan = await _pendaftarDesa.DaftarkanDenganKodeBaruAsync(nama, kontak: "", maxPerangkat: 2)
                    .ConfigureAwait(false);
                kodeBaru = pelanggan.KodeAktivasi;
                ServerLog.TagihanBerlanggananAwal(_logger, kodeBaru, nama);
            }
            catch (Exception ex)
            {
                ServerLog.TagihanBerlanggananAwalGagal(_logger, ex, nama);
                return HasilTagihanBaru.Ditolak(500,
                    "Pendaftaran desa baru gagal — lihat log server.");
            }
        }
        else
        {
            var ditemukan = await _repo.CariByKodeAsync(req.KodeAktivasi.Trim()).ConfigureAwait(false);
            if (ditemukan is null)
                return HasilTagihanBaru.Ditolak(404, "Kode aktivasi tidak dikenal.");
            pelanggan = ditemukan;
        }

        // Paket dipilih klien, HARGA selalu milik server. Kode tak dikenal →
        // paket bawaan (paling murah), bukan paket termahal dan bukan gratis.
        var paket = _katalog.Cari(req.Paket) ?? _katalog.Bawaan;

        var orderId = BuatOrderId(pelanggan.Id, _waktu.GetUtcNow().UtcDateTime);
        await _repo.SimpanTagihanAsync(pelanggan.Id, orderId, paket).ConfigureAwait(false);

        if (!_gateway.Aktif)
        {
            // Mode demo (Development tanpa ServerKey): tautan contoh, TANPA token
            // gateway. Harga yang ditampilkan tetap harga server supaya halaman
            // checkout tidak pernah menampilkan angka yang berbeda dari tagihan.
            return new HasilTagihanBaru(
                Status: 200,
                OrderId: orderId,
                Paket: paket,
                LinkPembayaran: $"contoh://qris/{orderId}/{paket.Harga}",
                Mode: "demo",
                KodeAktivasi: kodeBaru);
        }

        var tagihan = await _gateway.BuatTagihanAsync(orderId, paket.Harga, $"{pelanggan.NamaDesa} - {paket.Nama}")
            .ConfigureAwait(false);
        if (tagihan is null)
        {
            ServerLog.TagihanGatewayGagal(_logger, orderId, paket.Kode, paket.Harga);
            return new HasilTagihanBaru(
                Status: 500, OrderId: orderId, Paket: paket, Mode: "midtrans",
                Pesan: "Pembuatan transaksi Snap gagal — lihat log server.");
        }

        return new HasilTagihanBaru(
            Status: 200,
            OrderId: orderId,
            Paket: paket,
            Token: tagihan.Token,
            LinkPembayaran: tagihan.RedirectUrl,
            Mode: "midtrans",
            KodeAktivasi: kodeBaru);
    }

    /// <summary>
    /// Susun order id: <c>SUDES-&lt;8 huruf id desa&gt;-&lt;yyyyMMdd&gt;-&lt;6 heksa acak&gt;</c>.
    ///
    /// Formatnya dipertahankan apa adanya karena order id yang sudah terbit
    /// dipakai Midtrans dan catatan operator. Bagian acak membuat dua tagihan
    /// dari desa yang sama pada hari yang sama tetap berbeda; tanpa itu, tagihan
    /// kedua akan menimpa/menabrak order id yang sudah ada (tabel
    /// <c>tagihan</c> dan <c>pembayaran</c> berkunci order_id).
    /// </summary>
    public static string BuatOrderId(string pelangganId, DateTime waktuUtc, string? acak = null)
    {
        var id = pelangganId ?? "";
        var prefix = id.Length > 8 ? id[..8] : id;   // id pendek tidak boleh melempar
        var tanggal = (waktuUtc.Kind == DateTimeKind.Utc ? waktuUtc : DateTime.SpecifyKind(waktuUtc, DateTimeKind.Utc))
            .ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var sufiks = acak ?? Guid.NewGuid().ToString("N")[..6];
        return $"SUDES-{prefix}-{tanggal}-{sufiks}";
    }
}
