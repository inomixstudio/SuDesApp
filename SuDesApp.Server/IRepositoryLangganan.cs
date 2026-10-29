namespace SuDesApp.Server;

/// <summary>
/// Penyimpanan pengumuman broadcast. Dipisah dari <see cref="IRepositoryLangganan"/>
/// supaya server Development tanpa database tetap bisa menyajikan endpoint
/// pengumuman (memori), tanpa memaksa seluruh penyimpanan langganan ikut memori.
/// </summary>
public interface IPengumumanStore
{
    Task<List<PengumumanServer>> BacaPengumumanAsync();
    Task<List<PengumumanServer>> DaftarPengumumanSemuaAsync();
    Task<string> BuatPengumumanAsync(string judul, string isi, string jenis, DateTime? mulai, DateTime? sampai);
    Task<int> HapusPengumumanAsync(string id);
}

/// <summary>
/// Penyimpanan data langganan. Dipisah dari <see cref="SupabaseStore"/> supaya
/// logika yang uang (webhook pembayaran) bisa diuji tanpa
/// database sungguhan — bagian paling rawan kerusakannya justru bagian yang
/// tak boleh bergantung pada koneksi.
/// </summary>
public interface IRepositoryLangganan
{
    Task InitializeAsync();

    Task<Pelanggan?> CariByKodeAsync(string kode);

    Task<Pelanggan> DaftarkanAsync(string kodeAktivasi, string namaDesa,
                                   string kontak = "", int maxPerangkat = 2);

    Task<(bool Bolehkan, string? Pesan)> CatatPerangkatAsync(string pelangganId, string? idPerangkat);

    // === Pusat administer (endpoint /admin/*, wajib token admin) ===
    //
    // Semua operasi di bawah ini bekerja LINTAS-TENANT: operator yang menolong
    // desa A memang harus bisa melihat desa A tanpa menjadi desa A. Karena itu
    // pemanggilnya WAJIB endpoint bertoken admin — bukan aplikasi desa.

    /// <summary>Semua desa + ringkasan langganan &amp; jumlah perangkatnya.</summary>
    Task<IReadOnlyList<RingkasPelanggan>> DaftarPelangganAsync();

    /// <summary>Perangkat terdaftar sebuah desa; <c>null</c> bila kode tidak dikenal.</summary>
    Task<IReadOnlyList<InfoPerangkat>?> DaftarPerangkatAsync(string kodeAktivasi);

    /// <summary>
    /// Hapus perangkat desa: satu baris bila <paramref name="idPerangkat"/> diisi,
    /// atau SEMUA bila null/kosong. Mengembalikan jumlah baris terhapus, atau
    /// <c>null</c> bila kode aktivasi tidak dikenal.
    /// </summary>
    Task<int?> HapusPerangkatAsync(string kodeAktivasi, string? idPerangkat);

    /// <summary>
    /// Ubah batas jumlah perangkat desa. <c>false</c> bila kode aktivasi tidak
    /// dikenal. Dipakai saat pelanggan sah butuh menambah PC (mis. ganti
    /// komputer) tanpa menunggu perubahan skema.
    /// </summary>
    Task<bool> UbahBatasPerangkatAsync(string kodeAktivasi, int batas);

    /// <summary>
    /// Riwayat pembayaran (dana masuk) sebuah desa, terbaru lebih dulu.
    /// <c>null</c> bila kode aktivasi tidak dikenal. Lintas-tenant, jadi WAJIB
    /// dipanggil lewat endpoint bertoken admin.
    /// </summary>
    Task<IReadOnlyList<InfoPembayaran>?> DaftarPembayaranAsync(string kodeAktivasi);

    /// <summary>
    /// Ubah nama desa &amp; kontaknya. Kode aktivasi SENGAJA tidak bisa diubah:
    /// kode itulah yang diketik di aplikasi desa, menggantinya berarti setiap PC
    /// desa itu harus mengaktivasi ulang. <c>false</c> bila kode tidak dikenal.
    /// </summary>
    Task<bool> UbahDesaAsync(string kodeAktivasi, string namaDesa, string kontak);

    /// <summary>
    /// Hapus desa beserta langganan, perangkat, tagihan, &amp; pembayarannya
    /// (CASCADE). Menolak (<see cref="HasilHapusDesa.AdaRiwayat"/>) bila desa
    /// masih punya tagihan atau pembayaran, KECUALI <paramref name="paksa"/> —
    /// yang hanya boleh bernilai true bila operator sudah memberi konfirmasi
    /// eksplisit. Angka pada <see cref="RincianHapusDesa"/> SELALU diisi (termasuk
    /// saat ditolak) supaya pemanggil tahu berapa jejak yang menahan atau hilang.
    /// </summary>
    Task<RincianHapusDesa> HapusDesaAsync(string kodeAktivasi, bool paksa);

    /// <summary>
    /// Simpan tagihan. <paramref name="nominal"/> hanya diisi oleh jalur
    /// PEMBUKUAN OPERATOR (<see cref="LayananPembayaranManual"/>) yang mencatat
    /// uang benar-benar diterima; jalur klien selalu membiarkannya null supaya
    /// harga tetap datang dari katalog <paramref name="paket"/> dan tidak bisa
    /// ditawar dari aplikasi desa.
    /// </summary>
    Task SimpanTagihanAsync(string pelangganId, string orderId, PaketLangganan paket, long? nominal = null);

    /// <summary>Tagihan yang sudah dibuat (belum dibayar) — sumber nominal &amp; durasi.</summary>
    Task<(string PelangganId, string Paket, long Nominal, int Hari)?> CariTagihanAsync(string orderId);

    /// <summary>
    /// Perpanjang secara idempoten; <c>Duplikat</c> true bila order sudah pernah
    /// diproses.
    /// </summary>
    /// <param name="catatan">
    /// Referensi bukti bayar, hanya diisi jalur pembukuan operator
    /// (<see cref="LayananPembayaranManual"/>): mis. "QRIS 17/10 21:04".
    /// Webhook &amp; rekonsiliasi membiarkannya null karena buktinya adalah
    /// notifikasi/status gateway itu sendiri. Ikut tersimpan di kolom
    /// <c>pembayaran.catatan</c> sehingga tampil di riwayat &amp; ekspor CSV.
    /// </param>
    /// <param name="bukti">
    /// Lampiran gambar bukti transfer, hanya dari pembukuan operator. Disimpan
    /// di kolom <c>pembayaran.bukti_*</c> dan disajikan kembali lewat
    /// <see cref="AmbilBuktiAsync"/>; webhook &amp; rekonsiliasi membiarkannya
    /// null karena buktinya adalah notifikasi gateway.
    /// </param>
    Task<HasilPerpanjang> PerpanjangAsync(
        string orderId, int hari, string status, string? catatan = null, BuktiBayar? bukti = null);

    /// <summary>
    /// Lampiran bukti transfer satu pembayaran; <c>null</c> bila desa/ordernya
    /// tidak ada atau pembayaran itu memang tanpa lampiran (jalur gateway).
    /// Desa ditentukan lebih dulu dari kode aktivasi, lalu isinya dibatasi ke
    /// pelanggan itu — gambar bukti tidak bisa dibaca lintas desa.
    /// </summary>
    Task<BuktiBayar?> AmbilBuktiAsync(string kodeAktivasi, string orderId);

    /// <summary>
    /// Aktifkan trial (tanpa uang): paket = <c>trial</c>, berlaku
    /// <paramref name="sekarang"/> + <paramref name="hari"/>. Tiga guard di
    /// sisi PENYIMPANAN (bukan hanya di layanan) supaya tidak ada dua jalur
    /// yang bisa bolong satu sama lain:
    ///   1. belum pernah pakai trial      — sekali per desa, selamanya;
    ///   2. langganan belum sedang aktif  — yang masih berlaku tidak perlu trial;
    /// nilainya atomik di penyimpanan. <paramref name="hari"/> datang dari
    /// konst server (<c>LayananAktivasi.HariTrial</code>), bukan dari klien.
    /// </summary>
    Task<HasilTrial> MulaiTrialAsync(string pelangganId, int hari, DateTime sekarang);

    /// <summary>
    /// Tagihan yang sudah terbit tetapi belum punya baris <c>pembayaran</c> —
    /// bahan bakar rekonsiliasi: inilah order yang statusnya perlu ditanyakan
    /// langsung ke gateway ketika webhook tidak pernah masuk.
    /// </summary>
    /// <param name="batas">Jumlah maksimum yang dikembalikan (0 = tidak ada).</param>
    /// <param name="lebihTuaDari">
    /// Cutoff (UTC): hanya tagihan yang dibuat SEBELUM waktu ini ikut diperiksa.
    /// Dipakai agar tagihan yang baru saja dibuat tidak langsung ditanyakan ke
    /// gateway (pembeli masih di halaman pembayaran). Null = tanpa batas umur.
    /// </param>
    /// <param name="lebihBaruDari">
    /// Batas bawah (UTC): tagihan yang LEBIH TUA dari ini diabaikan karena sudah
    /// mati (transaksi Midtrans kedaluwarsa jauh lebih cepat). Ini bukan sekadar
    /// kerapian: daftar diambil urut waktu terbit lalu dipotong <paramref name="batas"/>,
    /// jadi tagihan mati yang tak pernah dibayar akan MENGHABISKAN kuota batch
    /// selamanya dan membuat tagihan baru tidak pernah ikut diperiksa.
    /// </param>
    Task<IReadOnlyList<TagihanBelumDibayar>> DaftarTagihanBelumDibayarAsync(
        int batas, DateTime? lebihTuaDari = null, DateTime? lebihBaruDari = null,
        CancellationToken ct = default);
}

/// <summary>
/// Satu tagihan yang belum tercatat lunas. Hanya kolom yang dibutuhkan
/// rekonsiliasi — bukan data desa — supaya pembacaan lintas-tenant ini tetap
/// sesempit mungkin.
/// </summary>
public sealed record TagihanBelumDibayar(
    string OrderId, string PelangganId, string Paket, long Nominal, int Hari, DateTime Dibuat);

/// <summary>
/// Hasil permintaan aktivasi trial. <see cref="Pesan"/> keterangan saat ditolak;
/// <see cref="BerlakuSampai"/> (UTC) diisi hanya bila berhasil — pemanggil
/// meneruskan nilainya sama seperti hasil aktivasi biasa supaya token terbit.
/// </summary>
public sealed record HasilTrial(bool Boleh, string? Pesan = null, DateTime? BerlakuSampai = null);

/// <summary>Implementasi in-memory untuk uji. Meniru perilaku penting SupabaseStore.</summary>
public sealed class LanggananMemori : IRepositoryLangganan
{
    private readonly Dictionary<string, Pelanggan> _pelanggan = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Paket, long Nominal, int Hari, string PelangganId)> _tagihan =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _dibayar = new(StringComparer.Ordinal);

    /// <summary>pelangganId → (id perangkat → terakhir terlihat). Timestamp ditiru agar daftar perangkat punya urutan yang sama dengan SupabaseStore.</summary>
    private readonly Dictionary<string, Dictionary<string, DateTime>> _perangkat = new(StringComparer.Ordinal);

    /// <summary>order_id → (nominal, status, waktu dicatat, catatan, lampiran bukti). Meniru tabel `pembayaran`.</summary>
    private readonly Dictionary<string, (long Nominal, string Status, DateTime Dicatat, string? Catatan, BuktiBayar? Bukti)> _pembayaran = new(StringComparer.Ordinal);

    /// <summary>Urutan terbit tagihan — meniru ORDER BY dibuat di Postgres.</summary>
    private readonly List<string> _urutanTagihan = new();
    private readonly Dictionary<string, DateTime> _dibuat = new(StringComparer.Ordinal);

    /// <summary>
    /// Seluruh isi penyimpanan sebagai snapshot yang bisa disimpan ke berkas
    /// (lihat <see cref="PenyimpananLokal"/>). Dipakai mode lokal tanpa database
    /// supaya desa, langganan, tagihan, dan pembayaran tetap ada setelah server
    /// berhenti — bukan hilang seperti penyimpanan memori biasa.
    /// </summary>
    public LanggananSnapshot AmbilSnapshot() => new(
        1,
        new Dictionary<string, Pelanggan>(_pelanggan, StringComparer.OrdinalIgnoreCase),
        _tagihan.ToDictionary(
            kv => kv.Key,
            kv => new TagihanTersimpan(kv.Value.Paket, kv.Value.Nominal, kv.Value.Hari, kv.Value.PelangganId),
            StringComparer.Ordinal),
        _dibayar.ToList(),
        _perangkat.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, DateTime>(kv.Value, StringComparer.Ordinal),
            StringComparer.Ordinal),
        _pembayaran.ToDictionary(
            kv => kv.Key,
            kv => new PembayaranTersimpan(
                kv.Value.Nominal, kv.Value.Status, kv.Value.Dicatat, kv.Value.Catatan, kv.Value.Bukti),
            StringComparer.Ordinal),
        _urutanTagihan.ToList(),
        new Dictionary<string, DateTime>(_dibuat, StringComparer.Ordinal));

    /// <summary>Ganti seluruh isi penyimpanan dengan snapshot hasil <see cref="AmbilSnapshot"/>.</summary>
    public void TerapkanSnapshot(LanggananSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _pelanggan.Clear();
        foreach (var (kode, pelanggan) in snapshot.Pelanggan) _pelanggan[kode] = pelanggan;

        _tagihan.Clear();
        foreach (var (order, tagihan) in snapshot.Tagihan)
        {
            _tagihan[order] = (tagihan.Paket, tagihan.Nominal, tagihan.Hari, tagihan.PelangganId);
        }

        _dibayar.Clear();
        foreach (string order in snapshot.Dibayar) _dibayar.Add(order);

        _perangkat.Clear();
        foreach (var (pelangganId, perangkat) in snapshot.Perangkat)
        {
            _perangkat[pelangganId] = new Dictionary<string, DateTime>(perangkat, StringComparer.Ordinal);
        }

        _pembayaran.Clear();
        foreach (var (order, bayar) in snapshot.Pembayaran)
        {
            _pembayaran[order] = (bayar.Nominal, bayar.Status, bayar.Dicatat, bayar.Catatan, bayar.Bukti);
        }

        _urutanTagihan.Clear();
        _urutanTagihan.AddRange(snapshot.UrutanTagihan);

        _dibuat.Clear();
        foreach (var (order, waktu) in snapshot.Dibuat) _dibuat[order] = waktu;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<Pelanggan?> CariByKodeAsync(string kode) =>
        Task.FromResult(_pelanggan.TryGetValue(kode?.Trim() ?? "", out var p) ? p : null);

    public Task<Pelanggan> DaftarkanAsync(string kodeAktivasi, string namaDesa,
                                           string kontak = "", int maxPerangkat = 2)
    {
        var kode = kodeAktivasi.Trim();
        if (_pelanggan.TryGetValue(kode, out var ada)) return Task.FromResult(ada);

        var p = new Pelanggan
        {
            Id = Guid.NewGuid().ToString("N"),
            KodeAktivasi = kode,
            NamaDesa = namaDesa,
            Kontak = kontak,
            MaxPerangkat = maxPerangkat <= 0 ? 2 : maxPerangkat,
            Langganan = new Langganan { PelangganId = "pending" }
        };
        _pelanggan[kode] = p;
        return Task.FromResult(p);
    }

    public Task<(bool Bolehkan, string? Pesan)> CatatPerangkatAsync(string pelangganId, string? idPerangkat)
    {
        if (string.IsNullOrWhiteSpace(idPerangkat)) return Task.FromResult((true, (string?)null));

        var batas = _pelanggan.Values.FirstOrDefault(p => p.Id == pelangganId)?.MaxPerangkat ?? 2;
        var map = _perangkat.GetValueOrDefault(pelangganId);
        if (map is null) _perangkat[pelangganId] = map = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        // Keputusan dihitung SEBELUM menulis (sama seperti SupabaseStore):
        // perangkat yang ditolak tidak boleh tercatat, dan perangkat yang sudah
        // dikenal tidak dihitung sebagai perangkat baru.
        if (!map.ContainsKey(idPerangkat) && map.Count >= batas)
            return Task.FromResult((Bolehkan: false, Pesan: (string?)$"Batas {batas} perangkat tercapai untuk langganan ini."));

        map[idPerangkat] = DateTime.UtcNow;
        return Task.FromResult((true, (string?)null));
    }

    public Task<IReadOnlyList<RingkasPelanggan>> DaftarPelangganAsync()
    {
        var hasil = _pelanggan.Values
            .Select(p => new RingkasPelanggan(
                p.Id, p.KodeAktivasi, p.NamaDesa, p.MaxPerangkat,
                _perangkat.GetValueOrDefault(p.Id)?.Count ?? 0,
                p.Langganan?.Paket ?? "bulanan",
                p.Langganan?.BerlakuSampai))
            .OrderBy(x => x.NamaDesa, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<RingkasPelanggan>>(hasil);
    }

    public Task<IReadOnlyList<InfoPerangkat>?> DaftarPerangkatAsync(string kodeAktivasi)
    {
        var p = _pelanggan.GetValueOrDefault(kodeAktivasi?.Trim() ?? "");
        if (p is null) return Task.FromResult<IReadOnlyList<InfoPerangkat>?>(null);

        var map = _perangkat.GetValueOrDefault(p.Id);
        IReadOnlyList<InfoPerangkat> hasil = map is null
            ? Array.Empty<InfoPerangkat>()
            : map.Select(kv => new InfoPerangkat(kv.Key, kv.Value))
                 .OrderByDescending(x => x.TerakhirLihat)
                 .ToList();
        return Task.FromResult<IReadOnlyList<InfoPerangkat>?>(hasil);
    }

    public Task<int?> HapusPerangkatAsync(string kodeAktivasi, string? idPerangkat)
    {
        var p = _pelanggan.GetValueOrDefault(kodeAktivasi?.Trim() ?? "");
        if (p is null) return Task.FromResult<int?>(null);
        if (!_perangkat.TryGetValue(p.Id, out var map)) return Task.FromResult<int?>(0);

        if (string.IsNullOrWhiteSpace(idPerangkat))
        {
            var semua = map.Count;
            map.Clear();
            return Task.FromResult<int?>(semua);
        }

        return Task.FromResult<int?>(map.Remove(idPerangkat.Trim()) ? 1 : 0);
    }

    public Task<bool> UbahBatasPerangkatAsync(string kodeAktivasi, int batas)
    {
        if (!_pelanggan.TryGetValue(kodeAktivasi?.Trim() ?? "", out var p)) return Task.FromResult(false);
        p.MaxPerangkat = batas <= 0 ? 2 : batas;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Riwayat pembayaran desa, terbaru lebih dulu. Hanya order milik pelanggan
    /// ini yang dikembalikan — memakai paket dari tagihan terkait seperti
    /// LEFT JOIN di SupabaseStore.
    /// </summary>
    public Task<IReadOnlyList<InfoPembayaran>?> DaftarPembayaranAsync(string kodeAktivasi)
    {
        var p = _pelanggan.GetValueOrDefault(kodeAktivasi?.Trim() ?? "");
        if (p is null) return Task.FromResult<IReadOnlyList<InfoPembayaran>?>(null);

        var hasil = _pembayaran
            .Where(kv => _tagihan.TryGetValue(kv.Key, out var t) && t.PelangganId == p.Id)
            .OrderByDescending(kv => kv.Value.Dicatat)
            .Select(kv => new InfoPembayaran(
                kv.Key, kv.Value.Nominal, kv.Value.Status, kv.Value.Dicatat,
                _tagihan.TryGetValue(kv.Key, out var t) ? t.Paket : null,
                kv.Value.Catatan,
                kv.Value.Bukti is not null,
                kv.Value.Bukti?.Tipe,
                kv.Value.Bukti?.Isi.Length ?? 0))
            .ToList();

        return Task.FromResult<IReadOnlyList<InfoPembayaran>?>(hasil);
    }

    /// <summary>Meniru pengambilan lampiran SupabaseStore: desa dulu, lalu order miliknya saja.</summary>
    public Task<BuktiBayar?> AmbilBuktiAsync(string kodeAktivasi, string orderId)
    {
        var p = _pelanggan.GetValueOrDefault(kodeAktivasi?.Trim() ?? "");
        var order = orderId?.Trim() ?? "";
        if (p is null || order.Length == 0) return Task.FromResult<BuktiBayar?>(null);

        var milikDesa = _tagihan.TryGetValue(order, out var t) && t.PelangganId == p.Id;
        if (!milikDesa) return Task.FromResult<BuktiBayar?>(null);

        return Task.FromResult(_pembayaran.TryGetValue(order, out var bayar) ? bayar.Bukti : null);
    }

    public Task<bool> UbahDesaAsync(string kodeAktivasi, string namaDesa, string kontak)
    {
        if (!_pelanggan.TryGetValue(kodeAktivasi?.Trim() ?? "", out var p)) return Task.FromResult(false);
        p.NamaDesa = namaDesa.Trim();
        p.Kontak = kontak ?? "";
        return Task.FromResult(true);
    }

    /// <summary>
    /// Hapus desa + perangkatnya, menolak bila masih ada tagihan/pembayaran —
    /// meniru guard di SupabaseStore supaya janji "jejak uang tidak hilang"
    /// ikut teruji tanpa database. <paramref name="paksa"/> menembus guard itu
    /// (hanya boleh dari konfirmasi eksplisit operator).
    /// </summary>
    public Task<RincianHapusDesa> HapusDesaAsync(string kodeAktivasi, bool paksa)
    {
        var kode = kodeAktivasi?.Trim() ?? "";
        if (!_pelanggan.TryGetValue(kode, out var p))
            return Task.FromResult(new RincianHapusDesa(HasilHapusDesa.TidakDikenal, 0, 0));

        var tagihan = _tagihan
            .Where(kv => kv.Value.PelangganId == p.Id)
            .Select(kv => kv.Key)
            .ToList();
        var pembayaran = _pembayaran.Keys
            .Where(o => _tagihan.TryGetValue(o, out var t) && t.PelangganId == p.Id)
            .ToList();

        if (tagihan.Count + pembayaran.Count > 0 && !paksa)
            return Task.FromResult(new RincianHapusDesa(HasilHapusDesa.AdaRiwayat, tagihan.Count, pembayaran.Count));

        foreach (var order in tagihan) _tagihan.Remove(order);
        foreach (var order in pembayaran) _pembayaran.Remove(order);

        _pelanggan.Remove(kode);
        _perangkat.Remove(p.Id);
        return Task.FromResult(new RincianHapusDesa(HasilHapusDesa.Terhapus, tagihan.Count, pembayaran.Count));
    }

    public Task SimpanTagihanAsync(string pelangganId, string orderId, PaketLangganan paket, long? nominal = null)
    {
        if (!_tagihan.ContainsKey(orderId)) _urutanTagihan.Add(orderId);
        _tagihan[orderId] = (paket.Kode, nominal ?? paket.Harga, paket.Hari, pelangganId);
        _dibuat[orderId] = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Meniru semantik penting SupabaseStore: yang dikembalikan hanya tagihan
    /// yang BELUM punya baris pembayaran, paling tua lebih dulu, dipotong
    /// <paramref name="batas"/>.
    /// </summary>
    public Task<IReadOnlyList<TagihanBelumDibayar>> DaftarTagihanBelumDibayarAsync(
        int batas, DateTime? lebihTuaDari = null, DateTime? lebihBaruDari = null, CancellationToken ct = default)
    {
        var hasil = new List<TagihanBelumDibayar>();
        if (batas <= 0) return Task.FromResult<IReadOnlyList<TagihanBelumDibayar>>(hasil);

        foreach (var order in _urutanTagihan)
        {
            if (_dibayar.Contains(order)) continue;
            if (!_tagihan.TryGetValue(order, out var t)) continue;

            var dibuat = _dibuat.TryGetValue(order, out var d) ? d : DateTime.UtcNow;
            if (lebihTuaDari is not null && dibuat > lebihTuaDari.Value) continue;
            if (lebihBaruDari is not null && dibuat < lebihBaruDari.Value) continue;

            hasil.Add(new TagihanBelumDibayar(order, t.PelangganId, t.Paket, t.Nominal, t.Hari, dibuat));
            if (hasil.Count >= batas) break;
        }

        return Task.FromResult<IReadOnlyList<TagihanBelumDibayar>>(hasil);
    }

    public Task<(string PelangganId, string Paket, long Nominal, int Hari)?> CariTagihanAsync(string orderId)
    {
        if (!_tagihan.TryGetValue(orderId, out var t)) return Task.FromResult<(string, string, long, int)?>(null);
        return Task.FromResult<(string PelangganId, string Paket, long Nominal, int Hari)?>
            ((t.PelangganId, t.Paket, t.Nominal, t.Hari));
    }

    /// <summary>
    /// Meniru semantik penting SupabaseStore: order yang diproses dua kali
    /// hanya menambah hak akses SATU kali.
    /// </summary>
    public Task<HasilPerpanjang> PerpanjangAsync(
        string orderId, int hari, string status, string? catatan = null, BuktiBayar? bukti = null)
    {
        if (!_tagihan.TryGetValue(orderId, out var t))
            return Task.FromResult(new HasilPerpanjang(false, false, null));

        if (!_dibayar.Add(orderId))
            return Task.FromResult(new HasilPerpanjang(true, true, t.PelangganId));

        // Catatan dirapikan seperti SupabaseStore: spasi di tepi dibuang, dan
        // catatan yang kosong menjadi null (bukan string berisi spasi).
        var catatanRapi = string.IsNullOrWhiteSpace(catatan) ? null : catatan.Trim();

        // Baris pembayaran: idempoten lewat _dibayar di atas, jadi satu order
        // hanya menghasilkan satu baris — sama seperti PRIMARY KEY(order_id).
        _pembayaran[orderId] = (t.Nominal, status, DateTime.UtcNow, catatanRapi, bukti);

        var p = _pelanggan.Values.FirstOrDefault(x => x.Id == t.PelangganId);
        if (p?.Langganan != null)
        {
            var dasar = p.Langganan.BerlakuSampai ?? DateTime.UtcNow;
            if (dasar < DateTime.UtcNow) dasar = DateTime.UtcNow;
            p.Langganan.BerlakuSampai = dasar.AddDays(hari);
            p.Langganan.Paket = t.Paket;
            p.Langganan.TotalTerbayar++;
        }
        return Task.FromResult(new HasilPerpanjang(true, false, t.PelangganId));
    }

    /// <summary>
    /// Trial in-memory: guard dua lapis yang sama dengan SupabaseStore supaya
    /// aturan layanan (LayananAktivasi) diuji terhadap perilaku penyimpanan
    /// yang benar, bukan terhadap tiruan yang memudahkan.
    /// </summary>
    public Task<HasilTrial> MulaiTrialAsync(string pelangganId, int hari, DateTime sekarang)
    {
        var p = _pelanggan.Values.FirstOrDefault(x => x.Id == pelangganId);
        if (p is null)
            return Task.FromResult(new HasilTrial(false, "Desa tidak dikenal."));

        if (p.TrialSudah)
            return Task.FromResult(new HasilTrial(false,
                "Trial sudah pernah dipakai untuk desa ini."));

        var langganan = p.Langganan ??= new Langganan { PelangganId = p.Id };
        var berlaku = langganan.BerlakuSampai;
        if (berlaku is not null && berlaku.Value > sekarang)
            return Task.FromResult(new HasilTrial(false,
                $"Langganan masih aktif s.d. {berlaku.Value:yyyy-MM-dd}; trial tidak perlu."));

        langganan.Paket = "trial";
        langganan.BerlakuSampai = sekarang.AddDays(hari);
        p.TrialSudah = true;
        return Task.FromResult(new HasilTrial(true, BerlakuSampai: langganan.BerlakuSampai));
    }
}
