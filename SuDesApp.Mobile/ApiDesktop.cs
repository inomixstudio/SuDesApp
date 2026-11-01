using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Mobile;

/// <summary>Sesi login yang diterima dari desktop.</summary>
public sealed class SesiDesktop
{
    public string Token { get; set; } = string.Empty;
    public DateTime BerlakuSampai { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Peran { get; set; } = string.Empty;
}

/// <summary>Pengaturan surat desktop yang dipakai ponsel (baca-saja).</summary>
public sealed class PengaturanDesktop
{
    public DesaDesktop Desa { get; set; } = new();
    public DateTime DiambilPada { get; set; }
}

/// <summary>Kop dan pejabat penanda tangan dari tabel InfoDesa desktop.</summary>
public sealed class DesaDesktop
{
    public string NamaDesa { get; set; } = string.Empty;
    public string Kecamatan { get; set; } = string.Empty;
    public string Kabupaten { get; set; } = string.Empty;
    public string? JenisKabupaten { get; set; }
    public string Alamat { get; set; } = string.Empty;
    public string Kodepos { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string KepalaDesa { get; set; } = string.Empty;
    public string? NipKepalaDesa { get; set; }
    public string? JenisLembaga { get; set; }
}

/// <summary>
/// Galat dari API desktop. <see cref="Kode"/> memuat kode stabil dari amplop
/// (mis. <c>sesi_gagal</c>, <c>sesi_diperlukan</c>) supaya layar bisa membedakan
/// sandi salah dari sesi yang sudah berakhir tanpa membaca teks pesannya.
/// </summary>
public sealed class ApiDesktopException : Exception
{
    public ApiDesktopException(string pesan, string? kode = null, Exception? dalam = null)
        : base(pesan, dalam)
    {
        Kode = kode;
    }

    public string? Kode { get; }

    /// <summary>True bila token yang dipakai sudah tidak berlaku (401).</summary>
    public bool SesiBerakhir =>
        string.Equals(Kode, "sesi_diperlukan", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Klien API yang berjalan di aplikasi desktop (<c>/sudes/api/v1</c>).
///
/// Ponsel hanya memakai dua kelompok endpoint: masuk/keluar (<c>/sesi</c>) dan
/// pengaturan surat (<c>/pengaturan</c>). Setiap permintaan membawa kunci API
/// dari halaman API Desa, dan endpoint yang memuat data akun juga membawa token
/// Bearer hasil login.
/// </summary>
public sealed class ApiDesktop
{
    private const string Prefix = "/sudes/api/v1";

    private static readonly JsonSerializerOptions Opsi = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HttpClient HttpBawaan = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly HttpClient _http;
    private readonly string _kunciApi;

    public ApiDesktop(string alamat, string kunciApi, HttpClient? http = null)
    {
        Alamat = RapikanAlamat(alamat);
        _kunciApi = (kunciApi ?? string.Empty).Trim();
        _http = http ?? HttpBawaan;
    }

    /// <summary>Alamat dasar tanpa garis miring akhir, mis. <c>http://192.168.1.5:8790</c>.</summary>
    public string Alamat { get; }

    /// <summary>
    /// Normalkan alamat yang diketik pengguna: boleh tanpa skema (diberi
    /// <c>http://</c>), boleh dengan garis miring akhir, dan spasi tepi dibuang.
    /// </summary>
    public static string RapikanAlamat(string? alamat)
    {
        var bersih = (alamat ?? string.Empty).Trim();
        if (bersih.Length == 0) return string.Empty;

        if (!bersih.Contains("://", StringComparison.Ordinal))
            bersih = "http://" + bersih;

        // Alamat yang tidak sah (mis. "http://") ditolak di sini supaya pesan
        // galatnya jelas, bukan "galat URI" dari HttpClient.
        if (!Uri.TryCreate(bersih, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ApiDesktopException(
                "Alamat desktop tidak sah. Contoh: http://192.168.1.10:8790");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    /// <summary>Masuk memakai akun desktop. Galat sandi diteruskan apa adanya.</summary>
    public async Task<SesiDesktop> MasukAsync(
        string username, string kataSandi, CancellationToken ct = default)
    {
        var data = await MintaAsync<SesiDesktop>(
            HttpMethod.Post,
            "/sesi",
            new { username = (username ?? string.Empty).Trim(), kata_sandi = kataSandi },
            token: null,
            ct).ConfigureAwait(false);

        return data ?? throw new ApiDesktopException("Desktop tidak mengirim data sesi.");
    }

    /// <summary>Pengaturan surat desktop: kop dan pejabat penanda tangan.</summary>
    public async Task<PengaturanDesktop> AmbilPengaturanAsync(
        string token, CancellationToken ct = default)
    {
        var data = await MintaAsync<PengaturanDesktop>(
            HttpMethod.Get, "/pengaturan", null, token, ct).ConfigureAwait(false);

        return data ?? throw new ApiDesktopException("Desktop tidak mengirim data pengaturan.");
    }

    /// <summary>Akhiri sesi di desktop. Kegagalan diabaikan pemanggil.</summary>
    public async Task KeluarAsync(string token, CancellationToken ct = default)
    {
        await MintaAsync<object?>(HttpMethod.Delete, "/sesi", null, token, ct)
            .ConfigureAwait(false);
    }

    private async Task<T?> MintaAsync<T>(
        HttpMethod metode, string jalur, object? badan, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_kunciApi))
            throw new ApiDesktopException("Kunci API belum diisi. Salin dari halaman API Desa di desktop.");

        using var permintaan = new HttpRequestMessage(metode, new Uri(Alamat + Prefix + jalur));
        permintaan.Headers.TryAddWithoutValidation("X-Api-Key", _kunciApi);
        if (!string.IsNullOrWhiteSpace(token))
            permintaan.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        if (badan != null)
            permintaan.Content = JsonContent.Create(badan, options: Opsi);

        HttpResponseMessage jawaban;
        try
        {
            jawaban = await _http.SendAsync(permintaan, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiDesktopException(
                $"Desktop tidak menjawab dalam 15 detik. Periksa apakah SuDesApp masih berjalan di {Alamat}.",
                dalam: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiDesktopException(
                $"Tidak bisa menghubungi desktop di {Alamat}. Pastikan API Desa aktif dan HP berada di jaringan yang sama.",
                dalam: ex);
        }

        using (jawaban)
        {
            string isi = await jawaban.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            Amplop<T>? amplop = null;
            try
            {
                amplop = JsonSerializer.Deserialize<Amplop<T>>(isi, Opsi);
            }
            catch (JsonException)
            {
                // Bukan jawaban API: biasanya alamat menunjuk halaman lain atau
                // server yang salah, jadi pesannya menyebut alamatnya.
                throw new ApiDesktopException(
                    $"Jawaban dari {Alamat} bukan dari API SuDesApp (HTTP {(int)jawaban.StatusCode}). " +
                    "Periksa alamat dan port desktop.");
            }

            if (amplop == null)
                throw new ApiDesktopException("Desktop mengirim jawaban kosong.");

            if (jawaban.IsSuccessStatusCode && amplop.Sukses)
                return amplop.Data;

            string pesan = PesanRamah(jawaban.StatusCode, amplop.Kode, amplop.Pesan);
            throw new ApiDesktopException(pesan, amplop.Kode);
        }
    }

    private static string PesanRamah(HttpStatusCode status, string? kode, string? pesan)
    {
        if (!string.IsNullOrWhiteSpace(pesan)) return pesan!;

        return kode switch
        {
            "api_key_salah" => "Kunci API tidak cocok dengan yang ada di desktop.",
            "api_key_kosong" => "Kunci API belum dibuat di desktop (halaman API Desa).",
            "sesi_gagal" => "Nama pengguna atau kata sandi salah.",
            "sesi_diperlukan" => "Sesi sudah berakhir. Sambungkan ulang.",
            "cakupan_tidak_cukup" => "Kunci API ini tidak mencakup endpoint yang dipanggil. Pakai kunci cakupan Penuh.",
            _ => $"Desktop menolak permintaan (HTTP {(int)status})."
        };
    }

    /// <summary>Bentuk jawaban API desktop: <c>{ sukses, kode, pesan, data }</c>.</summary>
    private sealed class Amplop<T>
    {
        public bool Sukses { get; set; }
        public string? Kode { get; set; }
        public string? Pesan { get; set; }
        public T? Data { get; set; }
    }
}
