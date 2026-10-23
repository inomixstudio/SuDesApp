namespace SuDesApp.Server;

/// <summary>
/// Hasil satu permintaan aktivasi.
/// </summary>
/// <param name="Status">Kode HTTP untuk dibalas ke klien (200/400/403/404).</param>
/// <param name="PelangganId">Id desa, bila kodenya dikenal.</param>
/// <param name="NamaDesa">Nama desa, bila kodenya dikenal.</param>
/// <param name="Aktif">True hanya bila langganan masih berlaku.</param>
/// <param name="BerlakuSampai">Jatuh tempo akses (UTC), null bila belum pernah bayar.</param>
/// <param name="Token">Token lisensi bertanda tangan; ADA hanya saat aktif.</param>
/// <param name="Pesan">Keterangan untuk pengguna saat tidak aktif/ditolak.</param>
public sealed record HasilAktivasi(
    int Status,
    string? PelangganId = null,
    string? NamaDesa = null,
    bool Aktif = false,
    DateTime? BerlakuSampai = null,
    string? Token = null,
    string? Pesan = null)
{
    public static HasilAktivasi Ditolak(int status, string pesan) => new(status, Pesan: pesan);
}

/// <summary>
/// Aturan aktivasi lisensi — dipindahkan keluar dari <c>Program.cs</c> supaya
/// bisa diuji seperti aturan pembayaran lainnya (webhook &amp; rekonsiliasi).
///
/// Urutan pemeriksaannya penting dan tidak boleh dibalik:
/// 1. kode aktivasi wajib ada        — kode itu sendiri kredensialnya
/// 2. kode harus dikenal             — menolak kode tebakan
/// 3. batas perangkat                — satu langganan ≠ jumlah PC tak terbatas
/// 4. langganan harus masih berlaku  — token HANYA terbit bila masa aktifnya sah
///
/// Langkah 4 yang paling mudah salah: token yang terbit untuk langganan
/// kedaluwarsa akan membuat aplikasi desa berjalan penuh selama masa
/// tenggangnya. Karena itu klien <b>tidak menerima token apa pun</b> saat tidak
/// aktif — bukan token dengan masa berlaku yang sudah lewat.
///
/// Token yang terbit membawa <c>exp</c> = jatuh tempo langganan dan <c>cek</c> =
/// waktu konfirmasi ini (keduanya ditandatangani), sehingga masa tenggang offline
/// di sisi klien tidak bisa dipalsukan dengan menyunting cache lokal.
/// </summary>
public sealed class LayananAktivasi
{
    /// <summary>
    /// Masa percobaan bawaan (hari). Trial TIDAK pernah melewati Midtrans:
    /// harganya 0, dan karena itu ia TIDAK masuk katalog tagihan
    /// (<see cref="KatalogPaket"/>) — yang masuk katalog hanya paket berbayar.
    /// </summary>
    public const int HariTrial = 14;

    private readonly IRepositoryLangganan _repo;
    private readonly SigningService _signing;
    private readonly ILogger<LayananAktivasi> _logger;
    private readonly TimeProvider _waktu;

    public LayananAktivasi(
        IRepositoryLangganan repo,
        SigningService signing,
        ILogger<LayananAktivasi> logger,
        TimeProvider? waktu = null)
    {
        _repo = repo;
        _signing = signing;
        _logger = logger;
        _waktu = waktu ?? TimeProvider.System;
    }

    public async Task<HasilAktivasi> AktivasiAsync(AktivasiRequest req, CancellationToken ct = default)
    {
        // 1. Kode aktivasi = kredensial. Kosong/spasi bukan "kode salah", tapi
        //    permintaan yang belum lengkap.
        if (string.IsNullOrWhiteSpace(req.KodeAktivasi))
            return HasilAktivasi.Ditolak(400, "Kode aktivasi wajib diisi.");

        // 2. Kode harus benar-benar terbit dari server.
        var pelanggan = await _repo.CariByKodeAsync(req.KodeAktivasi).ConfigureAwait(false);
        if (pelanggan is null)
            return HasilAktivasi.Ditolak(404, "Kode aktivasi tidak dikenal.");

        // 3. Batas perangkat — dicek SEBELUM token diterbitkan, bukan sesudah.
        var (boleh, pesan) = await _repo.CatatPerangkatAsync(pelanggan.Id, req.IdPerangkat)
            .ConfigureAwait(false);
        if (!boleh)
        {
            ServerLog.AktivasiDitolak(_logger, pelanggan.Id, pesan);
            return HasilAktivasi.Ditolak(403, pesan ?? "Batas perangkat tercapai untuk langganan ini.");
        }

        // Baris langganan bisa saja belum ada (instalasi lama); keadaan itu
        // diperlakukan sebagai "belum pernah bayar", bukan galat server.
        var langganan = pelanggan.Langganan ?? new Langganan { PelangganId = pelanggan.Id };
        var sekarang = _waktu.GetUtcNow().UtcDateTime;
        var berlakuSampai = Utc(langganan.BerlakuSampai);

        // 4. Hanya langganan yang masih berlaku yang mendapat token.
        if (berlakuSampai is null || berlakuSampai.Value <= sekarang)
        {
            return new HasilAktivasi(
                Status: 200,
                PelangganId: pelanggan.Id,
                NamaDesa: pelanggan.NamaDesa,
                Aktif: false,
                BerlakuSampai: berlakuSampai,
                Pesan: berlakuSampai.HasValue
                    ? $"Langganan berakhir {berlakuSampai.Value:yyyy-MM-dd}."
                    : "Belum ada pembayaran tercatat.");
        }

        var unix = new DateTimeOffset(sekarang, TimeSpan.Zero).ToUnixTimeSeconds();
        var token = _signing.Tandatangani(new KlaimLisensi
        {
            Sub = pelanggan.Id,
            Desa = pelanggan.NamaDesa,
            Paket = langganan.Paket,
            Kode = pelanggan.KodeAktivasi,
            Iat = unix,
            Exp = new DateTimeOffset(berlakuSampai.Value, TimeSpan.Zero).ToUnixTimeSeconds(),
            // Anchor masa tenggang offline — ikut ditandatangani, jadi tidak bisa
            // dipalsukan dengan menyunting berkas cache di komputer desa.
            Cek = unix
        });

        ServerLog.AktivasiDiterima(
            _logger, pelanggan.Id, pelanggan.NamaDesa, langganan.Paket, berlakuSampai.Value);

        return new HasilAktivasi(
            Status: 200,
            PelangganId: pelanggan.Id,
            NamaDesa: pelanggan.NamaDesa,
            Aktif: true,
            BerlakuSampai: berlakuSampai,
            Token: token);
    }

    /// <summary>
    /// Aktifkan trial 14 hari (tanpa pembayaran) untuk satu desa, lalu terbitkan
    /// token persis seperti aktivasi biasa — klien tidak membedakan keduanya.
    ///
    /// Urutan guard (sama pentingnya dengan aktivasi):
    ///   1. kode aktivasi wajib ada;
    ///   2. kode harus dikenal;
    ///   3. batas perangkat — penuh berarti tidak bisa mencoba (trial dimangsa
    ///      di penyimpanan, jadi percobaan pamela tidak menghabiskan trial);
    ///   4. penyimpanan yang memutuskan trial boleh atau tidak
    ///      (sekali per desa + belum sedang aktif), dengan hari yang datang
    ///      dari konst server — <see cref="AktivasiRequest"/> tidak mengirim
    ///      jumlah hari apa pun.
    /// </summary>
    public async Task<HasilAktivasi> AktifkanTrialAsync(AktivasiRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.KodeAktivasi))
            return HasilAktivasi.Ditolak(400, "Kode aktivasi wajib diisi.");

        var pelanggan = await _repo.CariByKodeAsync(req.KodeAktivasi).ConfigureAwait(false);
        if (pelanggan is null)
            return HasilAktivasi.Ditolak(404, "Kode aktivasi tidak dikenal.");

        var (boleh, pesan) = await _repo.CatatPerangkatAsync(pelanggan.Id, req.IdPerangkat)
            .ConfigureAwait(false);
        if (!boleh)
        {
            ServerLog.TrialDitolakPerangkat(_logger, pelanggan.Id, pesan);
            return HasilAktivasi.Ditolak(403, pesan ?? "Batas perangkat tercapai untuk langganan ini.");
        }

        var sekarang = _waktu.GetUtcNow().UtcDateTime;
        var trial = await _repo.MulaiTrialAsync(pelanggan.Id, HariTrial, sekarang).ConfigureAwait(false);
        if (!trial.Boleh || trial.BerlakuSampai is null)
        {
            ServerLog.TrialDitolak(_logger, pelanggan.Id, trial.Pesan);
            return HasilAktivasi.Ditolak(403, trial.Pesan ?? "Trial tidak bisa diaktifkan.");
        }

        // Trial diberikan: berlakuSampai dari PENYIMPANAN (bukan perhitungan
        // layanan), jadi jam perangkat berbeda tidak bisa melarangkan masa trial.
        var berlakuSampai = Utc(trial.BerlakuSampai);
        var unix = new DateTimeOffset(sekarang, TimeSpan.Zero).ToUnixTimeSeconds();
        var token = _signing.Tandatangani(new KlaimLisensi
        {
            Sub = pelanggan.Id,
            Desa = pelanggan.NamaDesa,
            Paket = "trial",
            Kode = pelanggan.KodeAktivasi,
            Iat = unix,
            Exp = new DateTimeOffset(berlakuSampai!.Value, TimeSpan.Zero).ToUnixTimeSeconds(),
            Cek = unix
        });

        ServerLog.TrialAktif(_logger, pelanggan.Id, pelanggan.NamaDesa, berlakuSampai.Value);

        return new HasilAktivasi(
            Status: 200,
            PelangganId: pelanggan.Id,
            NamaDesa: pelanggan.NamaDesa,
            Aktif: true,
            BerlakuSampai: berlakuSampai,
            Token: token);
    }

    /// <summary>
    /// Waktu jatuh tempo selalu diperlakukan sebagai UTC.
    ///
    /// <c>Langganan.BerlakuSampai</c> berjanji UTC, tetapi nilai yang datang dari
    /// penyimpanan bisa bertipe <see cref="DateTimeKind.Unspecified"/> — dan
    /// kalau dibiarkan, <c>new DateTimeOffset(nilai)</c> akan membacanya sebagai
    /// waktu LOKAL server. Di server yang bukan UTC, itu menggeser masa berlaku
    /// (dan klaim <c>exp</c> di token) beberapa jam tanpa ada yang menyadarinya.
    /// </summary>
    private static DateTime? Utc(DateTime? waktu) =>
        waktu is null
            ? null
            : waktu.Value.Kind == DateTimeKind.Utc
                ? waktu.Value
                : DateTime.SpecifyKind(waktu.Value, DateTimeKind.Utc);
}
