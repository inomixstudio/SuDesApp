namespace SuDesApp.Server;

/// <summary>
/// Aturan "pusat administer": mengelola perangkat desa dari sisi operator.
///
/// Dipisah dari lambda endpoint dengan alasan yang sama seperti
/// <see cref="LayananAktivasi"/>: inilah jalur yang bisa MEMBUKA kuota lisensi
/// (menghapus perangkat) dan mengubah batasnya, jadi harus bisa diuji tanpa
/// HTTP server maupun database. Lambda hanya mengadaptasi HTTP → hasil.
///
/// Setiap perubahan yang berhasil dicatat di log (level Warning) supaya aksi
/// operator meninggalkan jejak: tanpa itu, "kenapa kuota desa ini tiba-tiba
/// berkurang" tidak punya jawaban.
/// </summary>
public sealed class LayananAdmin
{
    private readonly IRepositoryLangganan _repo;
    private readonly ILogger<LayananAdmin> _logger;

    public LayananAdmin(IRepositoryLangganan repo, ILogger<LayananAdmin> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>Semua desa + ringkasan langganan (untuk memilih desa yang ditolong).</summary>
    public Task<IReadOnlyList<RingkasPelanggan>> DaftarPelangganAsync() => _repo.DaftarPelangganAsync();

    /// <summary>Perangkat sebuah desa; null bila kode aktivasi tidak dikenal.</summary>
    public Task<IReadOnlyList<InfoPerangkat>?> DaftarPerangkatAsync(string kodeAktivasi) =>
        _repo.DaftarPerangkatAsync(kodeAktivasi?.Trim() ?? "");

    /// <summary>
    /// Hapus perangkat desa. <paramref name="idPerangkat"/> null/kosong berarti
    /// SEMUA perangkat direset. Mengembalikan jumlah terhapus, atau null bila
    /// kode tidak dikenal.
    /// </summary>
    public async Task<int?> HapusPerangkatAsync(string kodeAktivasi, string? idPerangkat)
    {
        var kode = kodeAktivasi?.Trim() ?? "";
        var perangkat = string.IsNullOrWhiteSpace(idPerangkat) ? null : idPerangkat.Trim();
        var dihapus = await _repo.HapusPerangkatAsync(kode, perangkat);

        if (dihapus is int jumlah)
        {
            ServerLog.AdminHapusPerangkat(
                _logger, jumlah, kode, perangkat is null ? " (semua)" : $" (id {perangkat})");
        }

        return dihapus;
    }

    /// <summary>Ubah batas jumlah perangkat desa; false bila kode tidak dikenal.</summary>
    public async Task<bool> UbahBatasAsync(string kodeAktivasi, int batas)
    {
        var kode = kodeAktivasi?.Trim() ?? "";
        var berhasil = await _repo.UbahBatasPerangkatAsync(kode, batas);

        if (berhasil)
            ServerLog.AdminUbahBatas(_logger, kode, batas);

        return berhasil;
    }

    /// <summary>Riwayat pembayaran sebuah desa; null bila kode aktivasi tidak dikenal.</summary>
    public Task<IReadOnlyList<InfoPembayaran>?> DaftarPembayaranAsync(string kodeAktivasi) =>
        _repo.DaftarPembayaranAsync(kodeAktivasi?.Trim() ?? "");

    /// <summary>
    /// Lampiran bukti transfer satu pembayaran; null bila desa/ordernya tidak
    /// ada atau pembayaran itu tanpa lampiran. Pembacaan saja — tidak ada
    /// aturan uang di sini — tetapi tetap lewat layanan supaya endpoint tidak
    /// memegang repositori langsung dan batas "desa ini saja" tetap satu tempat.
    /// </summary>
    public Task<BuktiBayar?> AmbilBuktiAsync(string kodeAktivasi, string orderId) =>
        _repo.AmbilBuktiAsync(kodeAktivasi?.Trim() ?? "", orderId?.Trim() ?? "");

    /// <summary>
    /// Data satu desa (untuk mengisi formulir "ubah data desa"); null bila kode
    /// tidak dikenal. Memakai pencarian kode aktivasi yang memang lintas-tenant.
    /// </summary>
    public Task<Pelanggan?> CariDesaAsync(string kodeAktivasi) =>
        _repo.CariByKodeAsync(kodeAktivasi?.Trim() ?? "");

    /// <summary>
    /// Ubah nama &amp; kontak desa (kode aktivasi tidak bisa diubah). <c>false</c>
    /// bila kode tidak dikenal. Dicatat level Warning supaya perubahan data desa
    /// punya jejak.
    /// </summary>
    public async Task<bool> UbahDesaAsync(string kodeAktivasi, string namaDesa, string kontak)
    {
        if (string.IsNullOrWhiteSpace(namaDesa))
            throw new ArgumentException("Nama desa wajib diisi.", nameof(namaDesa));

        var kode = kodeAktivasi?.Trim() ?? "";
        var berhasil = await _repo.UbahDesaAsync(kode, namaDesa, kontak ?? "");

        if (berhasil)
            ServerLog.AdminUbahDesa(_logger, kode, namaDesa.Trim());

        return berhasil;
    }

    /// <summary>
    /// Hapus desa. Ditolak (<see cref="HasilHapusDesa.AdaRiwayat"/>) bila masih
    /// ada tagihan/pembayaran — jejak uang tidak boleh hilang. Nama desa dibaca
    /// SEBELUM dihapus supaya jejak audit menyebut desa mana yang dibuang.
    ///
    /// <paramref name="paksa"/> menembus guard itu dan SENGAJA memakai channel
    /// terpisah di log (bukan hanya menambah angka): penghapusan yang ikut
    /// membuang pembayaran adalah peristiwa yang berbeda kelasnya, jadi harus
    /// mudah dicari seseorang yang membaca log. Angka tagihan &amp; pembayaran
    /// selalu ikut dicatat supaya terlihat berapa banyak jejak yang musnah.
    /// </summary>
    public async Task<RincianHapusDesa> HapusDesaAsync(string kodeAktivasi, bool paksa)
    {
        var kode = kodeAktivasi?.Trim() ?? "";
        var desa = await _repo.CariByKodeAsync(kode);
        var hasil = await _repo.HapusDesaAsync(kode, paksa);

        switch (hasil.Status)
        {
            case HasilHapusDesa.Terhapus when paksa:
                ServerLog.AdminHapusPaksa(
                    _logger, kode, desa?.NamaDesa ?? "?", hasil.Tagihan, hasil.Pembayaran);
                break;

            case HasilHapusDesa.Terhapus:
                ServerLog.AdminHapusDesa(_logger, kode, desa?.NamaDesa ?? "?");
                break;
        }

        return hasil;
    }

    /// <summary>
    /// Daftarkan desa baru (kode aktivasi → desa). Idempoten: kode yang sudah ada
    /// dikembalikan apa adanya. Pendaftaran MEMBERI hak akses baru, jadi dicatat
    /// level Warning seperti mutasi admin lain; pendaftaran yang dilewati cukup
    /// level Information agar tidak terbaca seolah desa baru dibuat.
    /// </summary>
    public async Task<Pelanggan> DaftarkanAsync(string kodeAktivasi, string namaDesa,
                                                string kontak, int maxPerangkat)
    {
        if (string.IsNullOrWhiteSpace(kodeAktivasi))
            throw new ArgumentException("Kode aktivasi wajib diisi.", nameof(kodeAktivasi));
        if (string.IsNullOrWhiteSpace(namaDesa))
            throw new ArgumentException("Nama desa wajib diisi.", nameof(namaDesa));

        var kode = kodeAktivasi.Trim();
        var sudahAda = await _repo.CariByKodeAsync(kode) is not null;
        var p = await _repo.DaftarkanAsync(kode, namaDesa, kontak, maxPerangkat);

        if (sudahAda)
            ServerLog.AdminPendaftaranDilewati(_logger, p.KodeAktivasi);
        else
            ServerLog.AdminMendaftarkanDesa(_logger, p.KodeAktivasi, p.NamaDesa, p.MaxPerangkat);

        return p;
    }

    /// <summary>
    /// Daftarkan desa baru dengan kode aktivasi yang DIBUATKAN SERVER
    /// (<see cref="GeneratorKodeAktivasi"/>) — operator cukup menyebut nama
    /// desa, lalu kode yang dihasilkan dikirim ke desa lewat WhatsApp.
    ///
    /// Keunikan kode diverifikasi ke penyimpanan SEBELUM mendaftar: generator
    /// sendiri tidak tahu isi database, jadi pengecekannya di sini, dengan
    /// percobaan ulang yang terbatas (benturan kode acak 31^8 praktis tidak
    /// pernah terjadi, tetapi batasnya tetap ada supaya kegagalan penyimpanan
    /// tidak berubah loop tak berujung).
    /// </summary>
    public async Task<Pelanggan> DaftarkanDenganKodeBaruAsync(
        string namaDesa, string kontak, int maxPerangkat, int maksPercobaan = 5)
    {
        if (string.IsNullOrWhiteSpace(namaDesa))
            throw new ArgumentException("Nama desa wajib diisi.", nameof(namaDesa));
        if (maksPercobaan < 1)
            throw new ArgumentException("Maksimum percobaan minimal 1.", nameof(maksPercobaan));

        for (var percobaan = 1; ; percobaan++)
        {
            var kode = GeneratorKodeAktivasi.Buat();
            if (await _repo.CariByKodeAsync(kode) is not null)
            {
                if (percobaan >= maksPercobaan)
                    throw new InvalidOperationException(
                        $"Gagal menemukan kode aktivasi unik setelah {maksPercobaan} percobaan.");
                continue;
            }

            var p = await _repo.DaftarkanAsync(kode, namaDesa.Trim(), kontak, maxPerangkat);
            ServerLog.AdminMendaftarkanDesa(_logger, p.KodeAktivasi, p.NamaDesa, p.MaxPerangkat);
            return p;
        }
    }
}
