namespace SuDesApp.Server;

/// <summary>
/// Pengaturan rekonsiliasi. Dibaca dari konfigurasi dengan env var didahulukan
/// (pola yang sama dengan kredensial lain di server ini).
/// </summary>
/// <param name="Batas">Jumlah maksimum tagihan yang diperiksa per putaran.</param>
/// <param name="MinimalUmur">
/// Umur minimum tagihan sebelum statusnya ditanyakan ke gateway. Pembeli yang
/// baru saja membuka halaman pembayaran belum memilih metode pembayaran, jadi
/// menanyakan tagihan yang masih "baru" hanya membuang kuota API.
/// </param>
/// <param name="MaksimalUmur">
/// Umur maksimum tagihan yang masih layak ditanyakan. Transaksi Midtrans
/// kedaluwarsa jauh lebih cepat daripada ini, jadi tagihan yang lebih tua dari
/// batas ini sudah mati — dan daftar diambil urut waktu terbit lalu dipotong
/// <see cref="Batas"/>, sehingga tagihan mati akan menghabiskan kuota batch
/// selamanya bila tidak disaring.
/// </param>
/// <param name="Interval">Jarak antar putaran latar. Zero = penjadwalan mati.</param>
/// <param name="JedaAwal">Tunggu sebelum putaran pertama (biar start server tenang).</param>
public sealed record OpsiRekonsiliasi(
    int Batas,
    TimeSpan MinimalUmur,
    TimeSpan MaksimalUmur,
    TimeSpan Interval,
    TimeSpan JedaAwal)
{
    public static OpsiRekonsiliasi Bawaan => new(
        Batas: 25,
        MinimalUmur: TimeSpan.FromMinutes(10),
        MaksimalUmur: TimeSpan.FromDays(30),
        Interval: TimeSpan.FromMinutes(15),
        JedaAwal: TimeSpan.FromMinutes(1));

    /// <summary>
    /// Baca pengaturan dari konfigurasi. Nilai yang tidak masuk akal (negatif)
    /// dikembalikan ke bawaan, bukan diterima apa adanya: interval negatif
    /// akan membuat penjadwal berputar tanpa jeda dan membanjiri gateway.
    /// </summary>
    public static OpsiRekonsiliasi DariKonfigurasi(IConfiguration config)
    {
        var bawaan = Bawaan;

        int Angka(string env, string kunci, int nilaiBawaan, int minimum = 0) =>
            int.TryParse(Environment.GetEnvironmentVariable(env) ?? config[kunci], out var v) && v >= minimum
                ? v
                : nilaiBawaan;

        return new OpsiRekonsiliasi(
            Batas: Angka("REKONSILIASI_BATAS", "Rekonsiliasi:Batas", bawaan.Batas, minimum: 1),
            MinimalUmur: TimeSpan.FromMinutes(
                Angka("REKONSILIASI_MINIMAL_UMUR_MENIT", "Rekonsiliasi:MinimalUmurMenit",
                      (int)bawaan.MinimalUmur.TotalMinutes)),
            MaksimalUmur: TimeSpan.FromDays(
                Angka("REKONSILIASI_MAKSIMAL_UMUR_HARI", "Rekonsiliasi:MaksimalUmurHari",
                      (int)bawaan.MaksimalUmur.TotalDays, minimum: 1)),
            Interval: TimeSpan.FromMinutes(
                Angka("REKONSILIASI_INTERVAL_MENIT", "Rekonsiliasi:IntervalMenit",
                      (int)bawaan.Interval.TotalMinutes)),
            JedaAwal: TimeSpan.FromMinutes(
                Angka("REKONSILIASI_JEDA_AWAL_MENIT", "Rekonsiliasi:JedaAwalMenit",
                      (int)bawaan.JedaAwal.TotalMinutes)));
    }

    /// <summary>Penjadwalan latar menyala hanya bila intervalnya positif.</summary>
    public bool Terjadwal => Interval > TimeSpan.Zero;
}

/// <summary>
/// Hasil satu putaran rekonsiliasi. Angka-angkanya dipisah per jenis supaya
/// operator bisa membedakan "banyak yang belum bayar" (normal) dari "banyak
/// yang perlu diperiksa" (nominal tak cocok / gateway bermasalah).
/// </summary>
/// <param name="Diperiksa">Jumlah tagihan belum dibayar yang masuk putaran ini.</param>
/// <param name="Diselesaikan">Order yang baru saja diberi masa aktif.</param>
/// <param name="SudahDiselesaikan">Order yang ternyata sudah pernah diproses (webhook menang duluan).</param>
/// <param name="BelumBayar">Status gateway bukan dana masuk (pending/expire/deny/…).</param>
/// <param name="PerluDiperiksa">Nominal tak cocok atau order tak dikenal — butuh mata manusia.</param>
/// <param name="TidakPasti">Status tidak bisa dipastikan (404/HTTP gagal/jaringan).</param>
public sealed record RingkasanRekonsiliasi(
    int Diperiksa,
    int Diselesaikan,
    int SudahDiselesaikan,
    int BelumBayar,
    int PerluDiperiksa,
    int TidakPasti,
    IReadOnlyList<string> Catatan)
{
    public static RingkasanRekonsiliasi Kosong(params string[] catatan) =>
        new(0, 0, 0, 0, 0, 0, catatan);

    /// <summary>True bila ada perubahan hak akses pada putaran ini.</summary>
    public bool AdaPerubahan => Diselesaikan > 0;
}

/// <summary>
/// Rekonsiliasi pembayaran: menyelesaikan tagihan yang belum tercatat lunas
/// dengan MENANYAKAN STATUSNYA LANGSUNG KE GATEWAY.
///
/// Kenapa perlu ada: webhook adalah jalur utama, tapi ia bisa tidak pernah
/// sampai (deploy berubah, URL salah, notifikasi hilang, server sempat mati).
/// Pembeli yang uangnya sudah masuk tetapi hak aksesnya tidak bertambah akan
/// menelepon desa — dan tanpa backstop ini satu-satunya obatnya adalah
/// memperbaiki data secara manual.
///
/// Yang dipertahankan dari webhook, tanpa kecuali:
/// <list type="bullet">
/// <item><b>aturan penyelesaian yang sama</b> — keduanya memanggil
/// <see cref="LayananWebhook.SelesaikanAsync"/>: tagihan harus ada, nominal
/// harus cocok, status harus benar-benar berduit, fraud_status kartu harus
/// aman;</item>
/// <item><b>idempotensi yang sama</b> — perpanjangan tetap dijaga PRIMARY KEY
/// <c>pembayaran.order_id</c>, jadi order yang sudah diproses webhook tidak
/// menambah hari saat rekonsiliasi menjumpainya lagi;</item>
/// <item><b>fail-closed</b> — status yang tidak bisa dipastikan TIDAK pernah
/// dianggap lunas. Rekonsiliasi tidak pernah memberi akses atas dasar dugaan.</item>
/// </list>
///
/// Satu-satunya perbedaan prosedural: di sini tidak ada signature yang perlu
/// diverifikasi, karena statusnya diminta sendiri oleh server memakai ServerKey
/// (tidak ada pihak ketiga yang bisa memalsukannya).
/// </summary>
public sealed class LayananRekonsiliasi
{
    private readonly IRepositoryLangganan _repo;
    private readonly IPenyediaStatusPembayaran _status;
    private readonly LayananWebhook _webhook;
    private readonly ILogger<LayananRekonsiliasi> _logger;
    private readonly OpsiRekonsiliasi _opsi;

    public LayananRekonsiliasi(
        IRepositoryLangganan repo,
        IPenyediaStatusPembayaran status,
        LayananWebhook webhook,
        ILogger<LayananRekonsiliasi> logger,
        OpsiRekonsiliasi? opsi = null)
    {
        _repo = repo;
        _status = status;
        _webhook = webhook;
        _logger = logger;
        _opsi = opsi ?? OpsiRekonsiliasi.Bawaan;
    }

    /// <summary>
    /// Jalankan satu putaran. Tidak pernah melempar untuk kegagalan yang bisa
    /// diduga (gateway mati, koneksi putus, satu order bermasalah): kegagalan
    /// dicatat di ringkasan + log, dan putaran berikutnya tetap berjalan.
    /// </summary>
    public async Task<RingkasanRekonsiliasi> JalankanAsync(CancellationToken ct = default)
    {
        // Tanpa kredensial gateway, tidak ada yang bisa ditanyakan. Ini keadaan
        // normal di mode pengembangan (tagihan demo), jadi bukan galat.
        if (!_status.Aktif)
        {
            ServerLog.RekonsiliasiTanpaServerKey(_logger);
            return RingkasanRekonsiliasi.Kosong("ServerKey Midtrans belum diisi — rekonsiliasi dilewati.");
        }

        IReadOnlyList<TagihanBelumDibayar> daftar;
        try
        {
            var sekarang = DateTime.UtcNow;
            var cutoff = _opsi.MinimalUmur > TimeSpan.Zero
                ? sekarang - _opsi.MinimalUmur
                : (DateTime?)null;
            var batasBawah = _opsi.MaksimalUmur > TimeSpan.Zero
                ? sekarang - _opsi.MaksimalUmur
                : (DateTime?)null;
            daftar = await _repo.DaftarTagihanBelumDibayarAsync(_opsi.Batas, cutoff, batasBawah, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var pesan = ex is Npgsql.PostgresException { SqlState: "42883" }
                ? "Fungsi dir_tagihan_belum_dibayar belum ada di database — jalankan SuDesApp.Server/schema.sql " +
                  "(berkas itu aman diulang) atau set SUPABASE_MIGRATE_PASSWORD agar server menerapkannya saat start."
                : "Gagal membaca daftar tagihan belum dibayar: " + ex.Message;
            ServerLog.RekonsiliasiDaftarGagal(_logger, ex);
            return RingkasanRekonsiliasi.Kosong(pesan);
        }

        if (daftar.Count == 0)
        {
            ServerLog.RekonsiliasiTanpaTagihan(_logger);
            return RingkasanRekonsiliasi.Kosong();
        }

        var diselesaikan = 0;
        var sudah = 0;
        var belumBayar = 0;
        var perluDiperiksa = 0;
        var tidakPasti = 0;
        var catatan = new List<string>();

        foreach (var tagihan in daftar)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var status = await _status.AmbilStatusAsync(tagihan.OrderId, ct).ConfigureAwait(false);
                if (!status.Ditemukan || string.IsNullOrWhiteSpace(status.TransactionStatus))
                {
                    tidakPasti++;
                    ServerLog.RekonsiliasiStatusBelumPasti(_logger, tagihan.OrderId, status.Pesan);
                    continue;
                }

                // Aturan yang sama dengan webhook — nominal, status berduit,
                // fraud_status, dan idempotensi ditangani di satu tempat itu.
                var hasil = await _webhook
                    .SelesaikanAsync(tagihan.OrderId, status.TransactionStatus!, status.GrossAmount ?? "",
                                     status.FraudStatus, asal: "rekonsiliasi")
                    .ConfigureAwait(false);

                if (hasil.HariDitambah is > 0)
                {
                    diselesaikan++;
                    catatan.Add($"Order {tagihan.OrderId}: +{hasil.HariDitambah} hari ({tagihan.Paket}).");
                }
                else if (hasil.Duplikat)
                {
                    // Webhook sudah memprosesnya lebih dulu (atau putaran
                    // rekonsiliasi sebelumnya) — tidak ada akses tambahan.
                    sudah++;
                }
                else if (hasil.Status == 400)
                {
                    perluDiperiksa++;
                    catatan.Add(
                        $"Order {tagihan.OrderId}: nominal gateway '{status.GrossAmount}' tidak cocok dengan " +
                        $"tagihan {tagihan.Nominal} — periksa manual.");
                }
                else if (hasil.Status == 404)
                {
                    perluDiperiksa++;
                    catatan.Add($"Order {tagihan.OrderId}: tagihan hilang dari database saat akan diperpanjang.");
                }
                else
                {
                    belumBayar++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Satu order bermasalah (mis. galat database) tidak boleh
                // membatalkan sisanya: pembayaran desa lain sudah menunggu.
                tidakPasti++;
                ServerLog.RekonsiliasiOrderGagal(_logger, ex, tagihan.OrderId);
            }
        }

        var ringkasan = new RingkasanRekonsiliasi(
            daftar.Count, diselesaikan, sudah, belumBayar, perluDiperiksa, tidakPasti, catatan);

        // Putaran terjadwal yang tidak mengubah apa pun tidak perlu menulis baris
        // Information setiap interval — itu yang membuat server.log membengkak
        // tanpa memberi informasi. Ringkasan tetap dicatat bila ada perpanjangan
        // atau order yang perlu diperiksa.
        if (ringkasan.Diselesaikan > 0 || ringkasan.PerluDiperiksa > 0 || ringkasan.TidakPasti > 0)
        {
            ServerLog.RekonsiliasiSelesai(
                _logger, ringkasan.Diperiksa, ringkasan.Diselesaikan, ringkasan.SudahDiselesaikan,
                ringkasan.BelumBayar, ringkasan.PerluDiperiksa, ringkasan.TidakPasti);
        }

        return ringkasan;
    }
}
