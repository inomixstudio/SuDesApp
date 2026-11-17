namespace SuDesApp.Server;

/// <summary>
/// Hasil satu permintaan pembuatan tagihan.
/// </summary>
/// <param name="Status">Kode HTTP (200/400/404).</param>
/// <param name="OrderId">Order id yang juga tersimpan di tabel tagihan.</param>
/// <param name="Paket">Paket yang dipakai (selalu dari katalog server).</param>
public sealed record HasilTagihanBaru(
    int Status,
    string? OrderId = null,
    PaketLangganan? Paket = null,
    string? KodeAktivasi = null,
    string? Pesan = null)
{
    public static HasilTagihanBaru Ditolak(int status, string pesan) => new(status, Pesan: pesan);
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
/// <item>tagihan dicatat SEBELUM dibalas, sehingga order id yang dipakai
/// selalu punya padanan di tabel <c>tagihan</c> — operator menandai lunas
/// berdasarkan order id itu, jadi tidak ada pembayaran yang nyangkut tak
/// tercatat.</item>
/// </list>
///
/// Yang <b>tidak</b> dilakukan di sini: menentukan boleh/tidaknya desa membayar
/// (desa yang belum lunas justru harus bisa), dan memberi akses — perpanjangan
/// hanya terjadi lewat pembukuan manual
/// <c>/admin/pelanggan/{kode}/tandai-lunas</c> (<see cref="LayananPembayaranManual"/>).
/// </summary>
public sealed class LayananTagihan
{
    private readonly IRepositoryLangganan _repo;
    private readonly KatalogPaket _katalog;
    private readonly ILogger<LayananTagihan> _logger;
    private readonly TimeProvider _waktu;
    private readonly LayananAdmin _pendaftarDesa;

    public LayananTagihan(
        IRepositoryLangganan repo,
        KatalogPaket katalog,
        ILogger<LayananTagihan> logger,
        TimeProvider? waktu = null,
        LayananAdmin? pendaftarDesa = null)
    {
        _repo = repo;
        _katalog = katalog;
        _logger = logger;
        _waktu = waktu ?? TimeProvider.System;
        // Jalur "berlangganan awal" mendaftarkan desa baru dengan kode
        // otomatis — aturan pembuatan kode unik dipakai dari LayananAdmin
        // supaya aturannya tetap SATU salinan (percobaan terbatas, log
        // pendaftaran). Pendaftaran di sini TIDAK memberi akses: langganan
        // desa baru masih kosong dan baru menyala setelah operator membukukan
        // pembayarannya lewat /admin/pelanggan/{kode}/tandai-lunas.
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
                // desa baru masih kosong; masa aktifnya baru mulai
                // setelah operator mencatat uangnya masuk.
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

        // Tidak ada gateway: tagihan adalah CATATAN, bukan permintaan bayar
        // otomatis. Desa membayar lewat QRIS statis / transfer yang ditunjukkan
        // halaman Langganan, lalu operator membukunya lewat
        // /admin/pelanggan/{kode}/tandai-lunas — satu-satunya jalan yang
        // memperpanjang langganan.
        return new HasilTagihanBaru(
            Status: 200,
            OrderId: orderId,
            Paket: paket,
            KodeAktivasi: kodeBaru);
    }

    /// <summary>
    /// Susun order id: <c>SUDES-&lt;8 huruf id desa&gt;-&lt;yyyyMMdd&gt;-&lt;6 heksa acak&gt;</c>.
    ///
    /// Formatnya dipertahankan apa adanya karena order id yang sudah terbit
    /// dipakai catatan operator dan pembukuan manual. Bagian acak membuat dua tagihan
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
