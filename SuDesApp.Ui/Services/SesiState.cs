namespace SuDesApp.Ui.Services;

/// <summary>Identitas ringkas sesi yang diterima dari POST /sudes/api/v1/sesi.</summary>
public sealed class InfoSesi
{
    public string Username { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string Peran { get; set; } = string.Empty;
    public DateTime? BerlakuSampai { get; set; }
}

/// <summary>
/// Identitas sesi login yang sedang berjalan, dibagikan ke seluruh UI
/// (satu instance per host: per circuit di browser, per aplikasi di MAUI).
///
/// State ini hanya menampung token + identitas; panggilan HTTP tetap lewat
/// <see cref="SuDesApiClient"/> yang melampirkan token sebagai Authorization:
/// Bearer. Perubahan diberitahukan lewat event <see cref="Berubah"/> supaya
/// AuthenticationStateProvider dan layout ikut menyesuaikan diri.
///
/// Persistensi lintas-refresh memakai localStorage lewat JS interop dari
/// komponen (Masuk.razor) — state ini sengaja tidak memegang IJSRuntime agar
/// aman didaftarkan sebagai layanan singleton di Blazor Server maupun MAUI.
/// </summary>
public sealed class SesiState
{
    /// <summary>Kunci localStorage tempat token disimpan antar-refresh halaman.</summary>
    public const string KunciPenyimpanan = "sudes.sesi";

    public string? Token { get; private set; }
    public InfoSesi? Pengguna { get; private set; }
    public bool Adakah => !string.IsNullOrEmpty(Token) && Pengguna is not null;

    /// <summary>Dievaluasi ulang setiap AuthenticationStateProvider diminta state.</summary>
    public event Action? Berubah;

    public void Tetapkan(string token, InfoSesi pengguna)
    {
        Token = token ?? throw new ArgumentNullException(nameof(token));
        Pengguna = pengguna ?? throw new ArgumentNullException(nameof(pengguna));
        Berubah?.Invoke();
    }

    public void Bersihkan()
    {
        Token = null;
        Pengguna = null;
        Berubah?.Invoke();
    }
}
