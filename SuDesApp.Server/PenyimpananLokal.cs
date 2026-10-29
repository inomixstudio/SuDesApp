using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SuDesApp.Server;

/// <summary>Isi tabel <c>tagihan</c> yang disimpan ke berkas.</summary>
public sealed record TagihanTersimpan(string Paket, long Nominal, int Hari, string PelangganId);

/// <summary>Isi tabel <c>pembayaran</c> yang disimpan ke berkas.</summary>
public sealed record PembayaranTersimpan(
    long Nominal, string Status, DateTime Dicatat, string? Catatan, BuktiBayar? Bukti);

/// <summary>Seluruh data langganan (desa, perangkat, tagihan, pembayaran) dalam bentuk berkas.</summary>
public sealed record LanggananSnapshot(
    int Versi,
    Dictionary<string, Pelanggan> Pelanggan,
    Dictionary<string, TagihanTersimpan> Tagihan,
    List<string> Dibayar,
    Dictionary<string, Dictionary<string, DateTime>> Perangkat,
    Dictionary<string, PembayaranTersimpan> Pembayaran,
    List<string> UrutanTagihan,
    Dictionary<string, DateTime> Dibuat);

/// <summary>Isi berkas simpanan satu server lokal.</summary>
public sealed record BerkasPenyimpanan(
    int Versi,
    LanggananSnapshot? Langganan,
    List<PengumumanServer>? Pengumuman,
    List<AdminAuditEntry>? Audit);

/// <summary>
/// Penyimpanan langganan untuk server lisensi YANG DIJALANKAN LOKAL tanpa
/// database (mode Development tanpa kredensial Supabase).
///
/// Alasannya: pusat administrasi harus bisa mengelola langganan desa — paket,
/// berlaku sampai, perpanjang, tandai lunas — tanpa data itu hilang begitu
/// server berhenti. Penyimpanan memori biasa kehilangan semuanya saat proses
/// berakhir, sehingga operator harus mengisi ulang tiap kali aplikasi dibuka.
///
/// Bentuknya satu berkas JSON yang bisa dibaca manusia (bukan database):
/// jumlah desa di satu komputer desa kecil, dan berkas yang bisa diperiksa
/// operator lebih berguna daripada skema SQL baru yang harus dimigrasikan.
/// Penulisan dilakukan atomik (berkas sementara lalu dipindahkan), sehingga
/// berkas lama tetap utuh bila proses mati di tengah penulisan.
///
/// Seluruh panggilan diserialkan satu gerbang: satu operasi tulis selesai
/// (termasuk menyimpan) sebelum operasi berikutnya dimulai.
/// </summary>
public sealed class PenyimpananLokal : IRepositoryLangganan, IPengumumanStore, IAdminAuditStore, IDisposable
{
    /// <summary>Versi format berkas; dinaikkan bila susunannya berubah.</summary>
    public const int VersiBerkas = 1;

    private static readonly JsonSerializerOptions OpsiJson = new()
    {
        WriteIndented = true,
        // Berkas ini diperiksa manusia, jadi teks Indonesia tidak perlu
        // diubah menjadi \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _jalur;
    private readonly ILogger<PenyimpananLokal> _logger;
    private readonly SemaphoreSlim _gerbang = new(1, 1);
    private readonly LanggananMemori _langganan = new();
    private readonly PengumumanMemori _pengumuman = new();
    private readonly AuditMemori _audit = new();

    private PenyimpananLokal(string jalur, ILogger<PenyimpananLokal> logger)
    {
        _jalur = jalur;
        _logger = logger;
    }

    /// <summary>Jalur berkas simpanan yang sedang dipakai.</summary>
    public string Jalur => _jalur;

    /// <summary>
    /// Buka (atau buat) penyimpanan lokal di <paramref name="jalur"/>. Berkas
    /// yang rusak tidak menggagalkan server: berkas itu dipindahkan ke
    /// namanya + <c>.rusak-&lt;waktu&gt;</c> dan server mulai dari kosong —
    /// lebih baik daripada server yang tidak bisa dinyalakan.
    /// </summary>
    public static async Task<PenyimpananLokal> BukaAsync(string jalur, ILogger<PenyimpananLokal> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jalur);
        ArgumentNullException.ThrowIfNull(logger);

        var penyimpanan = new PenyimpananLokal(jalur, logger);

        if (!File.Exists(jalur))
        {
            ServerLog.PenyimpananLokalBaru(logger, jalur);
            return penyimpanan;
        }

        try
        {
            string isi = await File.ReadAllTextAsync(jalur).ConfigureAwait(false);
            var berkas = JsonSerializer.Deserialize<BerkasPenyimpanan>(isi, OpsiJson);

            if (berkas is null || berkas.Versi != VersiBerkas)
            {
                string versi = berkas is null
                    ? "kosong"
                    : berkas.Versi.ToString(System.Globalization.CultureInfo.InvariantCulture);
                penyimpanan.PindahkanBerkasRusak($"versi berkas {versi} tidak dikenal");
                return penyimpanan;
            }

            if (berkas.Langganan is { } langganan) penyimpanan._langganan.TerapkanSnapshot(langganan);
            if (berkas.Pengumuman is { } pengumuman) penyimpanan._pengumuman.TerapkanSnapshot(pengumuman);
            if (berkas.Audit is { } audit) penyimpanan._audit.TerapkanSnapshot(audit);

            ServerLog.PenyimpananLokalDibaca(
                logger,
                jalur,
                berkas.Langganan?.Pelanggan.Count ?? 0,
                berkas.Pengumuman?.Count ?? 0,
                berkas.Audit?.Count ?? 0);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            penyimpanan.PindahkanBerkasRusak(ex.Message);
        }

        return penyimpanan;
    }

    /// <summary>
    /// Jalur bawaan: <c>PenyimpananLokal:Path</c> dari konfigurasi bila diisi,
    /// selain itu <c>%LOCALAPPDATA%\SuDesApp\server-lisensi\langganan.json</c>
    /// (atau folder aplikasi bila LOCALAPPDATA tidak tersedia).
    /// </summary>
    public static string JalurBawaan(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? dariKonfigurasi = configuration["PenyimpananLokal:Path"];
        if (!string.IsNullOrWhiteSpace(dariKonfigurasi)) return dariKonfigurasi.Trim();

        string akar = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(akar)) akar = AppContext.BaseDirectory;
        return Path.Combine(akar, "SuDesApp", "server-lisensi", "langganan.json");
    }

    /// <summary>
    /// Simpan seluruh isi ke berkas. Kegagalan menulis TIDAK menggagalkan
    /// operasi yang sedang dilayani: keadaan di memori tetap sah untuk sesi ini,
    /// dan pesannya masuk log supaya operator tahu berkasnya tidak tersimpan.
    /// </summary>
    private void Simpan()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_jalur);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var berkas = new BerkasPenyimpanan(
                VersiBerkas,
                _langganan.AmbilSnapshot(),
                _pengumuman.AmbilSnapshot(),
                _audit.AmbilSnapshot().ToList());

            string teks = JsonSerializer.Serialize(berkas, OpsiJson);

            // Tulis ke berkas sementara lalu pindahkan: berkas tujuan tidak
            // pernah setengah tertulis walau proses berhenti mendadak.
            string sementara = _jalur + ".tmp";
            File.WriteAllText(sementara, teks);
            File.Move(sementara, _jalur, overwrite: true);
        }
        catch (Exception ex)
        {
            ServerLog.PenyimpananLokalGagalSimpan(_logger, ex, _jalur);
        }
    }

    /// <summary>
    /// Jalankan satu operasi tulis: tunggu giliran, kerjakan, lalu simpan ke
    /// berkas sebelum melepas giliran.
    /// </summary>
    private async Task<T> TulisAsync<T>(Func<Task<T>> operasi)
    {
        await _gerbang.WaitAsync().ConfigureAwait(false);
        try
        {
            T hasil = await operasi().ConfigureAwait(false);
            Simpan();
            return hasil;
        }
        finally
        {
            _gerbang.Release();
        }
    }

    private async Task TulisAsync(Func<Task> operasi)
    {
        await _gerbang.WaitAsync().ConfigureAwait(false);
        try
        {
            await operasi().ConfigureAwait(false);
            Simpan();
        }
        finally
        {
            _gerbang.Release();
        }
    }

    private void PindahkanBerkasRusak(string alasan)
    {
        try
        {
            string waktu = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string rusak = _jalur + ".rusak-" + waktu;
            File.Move(_jalur, rusak, overwrite: true);
            ServerLog.PenyimpananLokalRusak(_logger, alasan, rusak);
        }
        catch (Exception ex)
        {
            ServerLog.PenyimpananLokalGagalPindah(_logger, ex, _jalur);
        }
    }

    /// <summary>Gerbang internal dibuang saat server berhenti (dipanggil container).</summary>
    public void Dispose() => _gerbang.Dispose();

    // ------------------------------------------------------------ langganan

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<Pelanggan?> CariByKodeAsync(string kode) => _langganan.CariByKodeAsync(kode);

    public Task<Pelanggan> DaftarkanAsync(string kodeAktivasi, string namaDesa,
                                          string kontak = "", int maxPerangkat = 2) =>
        TulisAsync(() => _langganan.DaftarkanAsync(kodeAktivasi, namaDesa, kontak, maxPerangkat));

    public Task<(bool Bolehkan, string? Pesan)> CatatPerangkatAsync(string pelangganId, string? idPerangkat) =>
        TulisAsync(() => _langganan.CatatPerangkatAsync(pelangganId, idPerangkat));

    public Task<IReadOnlyList<RingkasPelanggan>> DaftarPelangganAsync() => _langganan.DaftarPelangganAsync();

    public Task<IReadOnlyList<InfoPerangkat>?> DaftarPerangkatAsync(string kodeAktivasi) =>
        _langganan.DaftarPerangkatAsync(kodeAktivasi);

    public Task<int?> HapusPerangkatAsync(string kodeAktivasi, string? idPerangkat) =>
        TulisAsync(() => _langganan.HapusPerangkatAsync(kodeAktivasi, idPerangkat));

    public Task<bool> UbahBatasPerangkatAsync(string kodeAktivasi, int batas) =>
        TulisAsync(() => _langganan.UbahBatasPerangkatAsync(kodeAktivasi, batas));

    public Task<IReadOnlyList<InfoPembayaran>?> DaftarPembayaranAsync(string kodeAktivasi) =>
        _langganan.DaftarPembayaranAsync(kodeAktivasi);

    public Task<BuktiBayar?> AmbilBuktiAsync(string kodeAktivasi, string orderId) =>
        _langganan.AmbilBuktiAsync(kodeAktivasi, orderId);

    public Task<bool> UbahDesaAsync(string kodeAktivasi, string namaDesa, string kontak) =>
        TulisAsync(() => _langganan.UbahDesaAsync(kodeAktivasi, namaDesa, kontak));

    public Task<RincianHapusDesa> HapusDesaAsync(string kodeAktivasi, bool paksa) =>
        TulisAsync(() => _langganan.HapusDesaAsync(kodeAktivasi, paksa));

    public Task SimpanTagihanAsync(string pelangganId, string orderId, PaketLangganan paket, long? nominal = null) =>
        TulisAsync(() => _langganan.SimpanTagihanAsync(pelangganId, orderId, paket, nominal));

    public Task<(string PelangganId, string Paket, long Nominal, int Hari)?> CariTagihanAsync(string orderId) =>
        _langganan.CariTagihanAsync(orderId);

    public Task<HasilPerpanjang> PerpanjangAsync(
        string orderId, int hari, string status, string? catatan = null, BuktiBayar? bukti = null) =>
        TulisAsync(() => _langganan.PerpanjangAsync(orderId, hari, status, catatan, bukti));

    public Task<HasilTrial> MulaiTrialAsync(string pelangganId, int hari, DateTime sekarang) =>
        TulisAsync(() => _langganan.MulaiTrialAsync(pelangganId, hari, sekarang));

    public Task<IReadOnlyList<TagihanBelumDibayar>> DaftarTagihanBelumDibayarAsync(
        int batas, DateTime? lebihTuaDari = null, DateTime? lebihBaruDari = null, CancellationToken ct = default) =>
        _langganan.DaftarTagihanBelumDibayarAsync(batas, lebihTuaDari, lebihBaruDari, ct);

    // ----------------------------------------------------------- pengumuman

    public Task<List<PengumumanServer>> BacaPengumumanAsync() => _pengumuman.BacaPengumumanAsync();

    public Task<List<PengumumanServer>> DaftarPengumumanSemuaAsync() =>
        _pengumuman.DaftarPengumumanSemuaAsync();

    public Task<string> BuatPengumumanAsync(
        string judul, string isi, string jenis, DateTime? mulai, DateTime? sampai) =>
        TulisAsync(() => _pengumuman.BuatPengumumanAsync(judul, isi, jenis, mulai, sampai));

    public Task<int> HapusPengumumanAsync(string id) => TulisAsync(() => _pengumuman.HapusPengumumanAsync(id));

    // ---------------------------------------------------------------- audit

    public Task CatatAuditAdminAsync(AdminAuditEntry entry) => TulisAsync(() => _audit.CatatAuditAdminAsync(entry));
}
