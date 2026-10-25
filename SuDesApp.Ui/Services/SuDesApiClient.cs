using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Ui.Services;

/// <summary>Angka agregat desa (dari GET /sudes/api/v1/statistik).</summary>
public sealed class StatistikDesa
{
    public string VersiAplikasi { get; set; } = string.Empty;
    public DateTime DibuatPada { get; set; }
    public int TotalWarga { get; set; }
    public int WargaAktif { get; set; }
    public int WargaLakiLaki { get; set; }
    public int WargaPerempuan { get; set; }
    public int JumlahKartuKeluarga { get; set; }
    public int PerangkatTotal { get; set; }
    public int PerangkatAktif { get; set; }
    public int SuratTotal { get; set; }
    public Dictionary<string, int>? SuratPerStatusPersetujuan { get; set; }
    public int PermintaanTotal { get; set; }
    public int PermintaanBelumDibaca { get; set; }
}

/// <summary>
/// Klien HTTP ringkas untuk backend SuDesApp.Api. Satu instance dipakai bersama
/// oleh ketiga platform (host mendaftarkan BaseAddress-nya masing-masing).
/// </summary>
public sealed class SuDesApiClient
{
    /// <summary>
    /// Alamat bawaan untuk pengembangan lokal. Di Android, ganti ke
    /// http://10.0.2.2:5188/ (emulator) atau IP LAN PC (perangkat fisik).
    /// </summary>
    public const string AlamatBawaan = "http://127.0.0.1:5188/";

    private readonly HttpClient _http;
    private readonly SesiState _sesi;

    public SuDesApiClient(HttpClient http, SesiState sesi)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _sesi = sesi ?? throw new ArgumentNullException(nameof(sesi));
    }

    /// <summary>
    /// Lampirkan token sesi sebagai Authorization: Bearer pada permintaan
    /// berikutnya. Dipanggil di awal tiap metode karena instance HttpClient ini
    /// dimiliki satu circuit/satu aplikasi — mutasi header aman dan selalu
    /// mencerminkan sesi terkini (tanpa token = endpoint internal menjawab 401).
    /// </summary>
    private void SiapkanSesi()
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(_sesi.Token)
            ? null
            : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _sesi.Token);
    }

    // ==== Sesi login (POST/GET/DELETE /sudes/api/v1/sesi) ====

    /// <summary>
    /// Coba masuk dengan nama pengguna + kata sandi. null bila backend tidak
    /// terjangkau; Selain itu isi Pesan siap tampil (termasuk pesan terkunci
    /// 3x gagal dari PenggunaService).
    /// </summary>
    public async Task<HasilMasuk?> MasukAsync(string username, string kataSandi, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("sudes/api/v1/sesi",
                new { username, kata_sandi = kataSandi }, ct);
            return await res.Content.ReadFromJsonAsync<HasilMasuk>(cancellationToken: ct)
                   ?? new HasilMasuk { Sukses = false, Pesan = "Respons server tidak terbaca." };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return new HasilMasuk { Sukses = false, Pesan = "Respons server tidak terbaca." };
        }
    }

    /// <summary>Identitas sesi yang sedang berlaku, atau null bila token tidak sah/kedaluwarsa.</summary>
    public async Task<InfoSesi?> SiapaAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            return await _http.GetFromJsonAsync<InfoSesi>("sudes/api/v1/sesi/siapa", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Akhiri sesi di server. Gagal diam-diam tidak apa — token tetap kedaluwarsa sendiri.</summary>
    public async Task KeluarAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            await _http.DeleteAsync("sudes/api/v1/sesi", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    /// <summary>
    /// Angka ringkasan desa, atau null bila backend tidak terjangkau —
    /// pemanggil menampilkan pesan "backend belum jalan", bukan kegagalan senyap.
    /// </summary>
    public async Task<StatistikDesa?> StatistikAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            return await _http.GetFromJsonAsync<StatistikDesa>("sudes/api/v1/statistik", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Daftar jenis surat yang bisa diminta lewat API.</summary>
    public async Task<IReadOnlyList<string>> JenisSuratAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            return await _http.GetFromJsonAsync<string[]>("sudes/api/v1/jenis-surat", ct)
                   ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Katalog menu surat (Buat Surat Baru + blanko NTCR) dari server. Daftarnya
    /// berasal dari Core yang sama dengan menu desktop, jadi sidebar web tidak
    /// punya salinan sendiri yang bisa ketinggalan. Null bila backend tidak
    /// terjangkau — sidebar tetap tampil dengan item dasar.
    /// </summary>
    public async Task<MenuAplikasi?> MenuAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            return await _http.GetFromJsonAsync<MenuAplikasi>("sudes/api/v1/menu", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ajukan permintaan surat baru dari formulir warga. Memakai jalur publik
    /// (tanpa kunci API) — jalur berkey /permintaan tetap untuk sistem desa.
    /// Mengembalikan HasilPermintaan (bisa gagal validasi) atau null bila backend
    /// tidak terjangkau.
    /// </summary>
    public async Task<HasilPermintaan?> AjukanAsync(PermintaanMasuk masuk, CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            var res = await _http.PostAsJsonAsync("sudes/api/v1/permintaan-publik", masuk, ct);
            var hasil = await res.Content.ReadFromJsonAsync<HasilPermintaan>(cancellationToken: ct);
            return hasil ?? new HasilPermintaan
            {
                Sukses = false,
                Pesan = "Respons server tidak terbaca."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            // 401/403/429 dari server datang tanpa badan JSON yang dikenali.
            return new HasilPermintaan
            {
                Sukses = false,
                Pesan = "Permintaan ditolak server. Coba lagi beberapa saat lagi."
            };
        }
    }

    /// <summary>Antrean permintaan untuk operator (muka internal aplikasi).</summary>
    public async Task<IReadOnlyList<BarisPermintaan>> AntreanAsync(
        string? status = null, CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            var url = string.IsNullOrWhiteSpace(status)
                ? "sudes/api/v1/permintaan"
                : $"sudes/api/v1/permintaan?status={Uri.EscapeDataString(status)}";
            return await _http.GetFromJsonAsync<List<BarisPermintaan>>(url, ct)
                   ?? new List<BarisPermintaan>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new List<BarisPermintaan>();
        }
    }

    // ==== Pengguna & peran (izin KelolaPengguna — hanya Administrator) ====

    /// <summary>
    /// Daftar akun desa. Galat dibedakan: 401 (sesi habis) dan 403 (bukan
    /// Administrator) masing-masing memberi pesan yang bisa ditindaklanjuti,
    /// bukan daftar kosong yang terlihat seperti "belum ada akun".
    /// </summary>
    public async Task<HasilDaftarPengguna> DaftarPenggunaAsync(CancellationToken ct = default)
    {
        try
        {
            SiapkanSesi();
            var res = await _http.GetAsync("sudes/api/v1/pengguna", ct);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new HasilDaftarPengguna { Pesan = "Sesi sudah berakhir. Masuk ulang untuk melanjutkan." };
            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return new HasilDaftarPengguna { Pesan = "Hanya Administrator yang boleh mengelola pengguna." };
            if (!res.IsSuccessStatusCode)
                return new HasilDaftarPengguna { Pesan = $"Server menjawab {(int)res.StatusCode}." };

            var daftar = await res.Content.ReadFromJsonAsync<List<BarisPengguna>>(cancellationToken: ct);
            return new HasilDaftarPengguna { Sukses = true, Daftar = daftar ?? new List<BarisPengguna>() };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HasilDaftarPengguna
            {
                Pesan = "Backend SuDesApp.Api tidak terjangkau — nyalakan server lalu muat ulang halaman."
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return new HasilDaftarPengguna { Pesan = "Respons server tidak terbaca." };
        }
    }

    /// <summary>Buat akun baru. Kata sandi diperiksa aturan Core (panjang & kemiripan nama).</summary>
    public Task<HasilAksi> TambahPenggunaAsync(
        string username, string nama, string peran, string kataSandi, CancellationToken ct = default)
        => KirimAksiAsync(() => _http.PostAsJsonAsync("sudes/api/v1/pengguna",
            new { username, nama, peran, kata_sandi = kataSandi }, ct), ct);

    /// <summary>Ubah nama, peran, atau status aktif sebuah akun.</summary>
    public Task<HasilAksi> UbahPenggunaAsync(
        int id, string nama, string peran, bool aktif, CancellationToken ct = default)
        => KirimAksiAsync(() => _http.PutAsJsonAsync($"sudes/api/v1/pengguna/{id}",
            new { nama, peran, aktif }, ct), ct);

    /// <summary>Reset kata sandi akun lain (Administrator). Sesi akun itu ikut diputus.</summary>
    public Task<HasilAksi> ResetKataSandiPenggunaAsync(
        int id, string kataSandiBaru, CancellationToken ct = default)
        => KirimAksiAsync(() => _http.PostAsJsonAsync($"sudes/api/v1/pengguna/{id}/kata-sandi",
            new { kata_sandi = kataSandiBaru }, ct), ct);

    /// <summary>Hapus akun. Akun Administrator aktif terakhir dilindungi oleh Core.</summary>
    public Task<HasilAksi> HapusPenggunaAsync(int id, CancellationToken ct = default)
        => KirimAksiAsync(() => _http.DeleteAsync($"sudes/api/v1/pengguna/{id}", ct), ct);

    /// <summary>
    /// Jalankan satu aksi tulis ke endpoint pengguna dan terjemahkan jawabannya
    /// menjadi HasilAksi. Badan galat server berbentuk { sukses, pesan }.
    /// </summary>
    private async Task<HasilAksi> KirimAksiAsync(
        Func<Task<HttpResponseMessage>> kirim, CancellationToken ct)
    {
        try
        {
            SiapkanSesi();
            using var res = await kirim();

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new HasilAksi { Pesan = "Sesi sudah berakhir. Masuk ulang untuk melanjutkan." };
            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return new HasilAksi { Pesan = "Hanya Administrator yang boleh mengelola pengguna." };

            var hasil = await res.Content.ReadFromJsonAsync<HasilAksi>(cancellationToken: ct);
            if (hasil is null)
                return new HasilAksi { Pesan = res.IsSuccessStatusCode ? "Perubahan tersimpan." : $"Server menjawab {(int)res.StatusCode}." };

            return hasil;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HasilAksi { Pesan = "Backend SuDesApp.Api tidak terjangkau — coba lagi setelah server dijalankan." };
        }
        catch (System.Text.Json.JsonException)
        {
            return new HasilAksi { Pesan = "Respons server tidak terbaca." };
        }
    }
}

/// <summary>Jawaban operasi tulis pada /pengguna.</summary>
public sealed class HasilAksi
{
    public bool Sukses { get; set; }
    public string? Pesan { get; set; }

    /// <summary>Jumlah token yang diputus karena wewenang/akun berubah.</summary>
    public int SesiDiputus { get; set; }
}

/// <summary>Jawaban GET /pengguna yang membedakan galat dari daftar kosong.</summary>
public sealed class HasilDaftarPengguna
{
    public bool Sukses { get; set; }
    public string? Pesan { get; set; }
    public IReadOnlyList<BarisPengguna> Daftar { get; set; } = Array.Empty<BarisPengguna>();
}

/// <summary>Satu akun desa (GET /sudes/api/v1/pengguna) — hash & salt tidak ikut dikirim.</summary>
public sealed class BarisPengguna
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Peran { get; set; } = string.Empty;
    public string PeranTampil { get; set; } = string.Empty;
    public bool Aktif { get; set; }
    public DateTime? LoginTerakhir { get; set; }
    public string? DibuatOleh { get; set; }
    public DateTime? DibuatPada { get; set; }
}

/// <summary>
/// Badan POST /sudes/api/v1/permintaan. Nama field snake_case sesuai kontrak
/// eksternal di ApiModels.ApiPermintaanMasuk ([JsonPropertyName]).
/// </summary>
public sealed class PermintaanMasuk
{
    [JsonPropertyName("referensi")]
    public string? Referensi { get; set; }

    [JsonPropertyName("nama_warga")]
    public string? NamaWarga { get; set; }

    [JsonPropertyName("nik")]
    public string? Nik { get; set; }

    [JsonPropertyName("no_hp")]
    public string? NomorHp { get; set; }

    [JsonPropertyName("jenis_surat")]
    public string? JenisSurat { get; set; }

    [JsonPropertyName("pesan")]
    public string? Pesan { get; set; }
}

/// <summary>Katalog menu aplikasi (GET /sudes/api/v1/menu).</summary>
public sealed class MenuAplikasi
{
    public List<MenuSuratItem> Surat { get; set; } = new();
    public List<MenuNtcrItem> Ntcr { get; set; } = new();
}

/// <summary>Satu pilihan pada menu "Buat Surat Baru".</summary>
public sealed class MenuSuratItem
{
    public string Label { get; set; } = string.Empty;
    public string Jenis { get; set; } = string.Empty;
}

/// <summary>Satu blanko NTCR pada menu.</summary>
public sealed class MenuNtcrItem
{
    public string Judul { get; set; } = string.Empty;
    public string Jenis { get; set; } = string.Empty;
    public string Kode { get; set; } = string.Empty;
    public string? Keterangan { get; set; }
}

/// <summary>Jawaban POST /sudes/api/v1/permintaan.</summary>
public sealed class HasilPermintaan
{
    public bool Sukses { get; set; }
    public string? Pesan { get; set; }
    public DataPermintaan? Data { get; set; }
}

public sealed class DataPermintaan
{
    public string? Kode { get; set; }
    public bool SudahAda { get; set; }
    public string? Pesan { get; set; }
}

/// <summary>Satu baris antrean operator (GET /sudes/api/v1/permintaan).</summary>
public sealed class BarisPermintaan
{
    public string Kode { get; set; } = string.Empty;
    public DateTime Tanggal { get; set; }
    public string Jenis { get; set; } = string.Empty;
    public string? Nama { get; set; }
    public string? NomorHp { get; set; }
    public string? Nik { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Sumber { get; set; } = string.Empty;
    public bool Dibaca { get; set; }
}

/// <summary>Jawaban POST /sudes/api/v1/sesi (login).</summary>
public sealed class HasilMasuk
{
    public bool Sukses { get; set; }
    public string? Pesan { get; set; }

    /// <summary>True bila gagal karena akun terkunci 3x percobaan salah.</summary>
    public bool Terkunci { get; set; }

    public string? Token { get; set; }
    public InfoSesi? Pengguna { get; set; }
}
