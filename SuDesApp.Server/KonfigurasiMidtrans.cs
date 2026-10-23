namespace SuDesApp.Server;

/// <summary>
/// Pembacaan konfigurasi Midtrans yang dipakai LEBIH DARI SATU tempat.
///
/// <para>Dipisah supaya aturan "environment variable menang atas appsettings,
/// nilai kosong berarti belum diisi" hanya ada satu salinan. Tiga nilai ini
/// menentukan apakah server benar-benar bicara dengan uang sungguhan, jadi
/// berbeda pembacaan di dua tempat berarti satu bagian server Diam-diam
/// menyala di sandbox sementara bagian lain mengira production — dan uji
/// "production" yang hasilnya tidak masuk akal tanpa ada yang sadar.</para>
///
/// <para>Aturan yang dijaga di sini:</para>
/// <list type="bullet">
/// <item><c>MIDTRANS_SERVER_KEY</c> — kunci API; kosong = gateway tidak aktif;</item>
/// <item><c>MIDTRANS_SANDBOX</c> — "false" berarti production, selain itu sandbox;</item>
/// <item><c>MIDTRANS_NOTIFICATION_URL</c> — URL publik server yang MENERIMA
/// notifikasi transaksi uji (header <c>X-Override-Notification</c>).</item>
/// </list>
/// </summary>
public static class KonfigurasiMidtrans
{
    public const string EnvServerKey = "MIDTRANS_SERVER_KEY";
    public const string EnvSandbox = "MIDTRANS_SANDBOX";
    public const string EnvUrlNotifikasi = "MIDTRANS_NOTIFICATION_URL";

    /// <summary>Path endpoint webhook di server ini.</summary>
    public const string PathWebhook = "/webhook-pembayaran";

    /// <summary>
    /// ServerKey Midtrans. Environment variable menang atas appsettings; nilai
    /// kosong (di kedua sumber) berarti "belum diisi" — bukan "kunci kosong".
    /// </summary>
    public static string ServerKey(IConfiguration config)
    {
        var dariEnv = Environment.GetEnvironmentVariable(EnvServerKey);
        var dariConfig = string.IsNullOrWhiteSpace(dariEnv) ? config["Midtrans:ServerKey"] : dariEnv;
        return string.IsNullOrWhiteSpace(dariConfig) ? "" : dariConfig.Trim();
    }

    /// <summary>
    /// Sandbox atau production. <c>false</c> (huruf besar/kecil) = production;
    /// apa pun else (termasuk kosong) = sandbox, supaya server yang salah
    /// konfigurasi lebih mungkin gagal diuji daripada diam-diam memakai uang
    /// sungguhan. Environment variable menang atas appsettings.
    /// </summary>
    public static bool PakaiSandbox(IConfiguration config)
    {
        var envSandbox = Environment.GetEnvironmentVariable(EnvSandbox);
        return string.IsNullOrWhiteSpace(envSandbox)
            ? !string.Equals(config["Midtrans:UseSandbox"], "false", StringComparison.OrdinalIgnoreCase)
            : !string.Equals(envSandbox, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// URL dasar (tanpa path webhook) yang dipakai untuk mengarahkan notifikasi
    /// transaksi uji. Null bila tidak diisi atau tidak bisa dipakai.
    /// </summary>
    public static string? UrlNotifikasiDasar(IConfiguration config)
    {
        var dariEnv = Environment.GetEnvironmentVariable(EnvUrlNotifikasi);
        var dariConfig = string.IsNullOrWhiteSpace(dariEnv) ? config["Midtrans:NotificationUrl"] : dariEnv;
        if (string.IsNullOrWhiteSpace(dariConfig)) return null;

        var dasar = dariConfig.Trim().TrimEnd('/');
        return UrlValid(dasar) ? dasar : null;
    }

    /// <summary>
    /// Gabung URL dasar dengan path webhook server ini. Bila URL dasar sudah
    /// berakhiran dengan path webhook, ia dipakai apa adanya — jadi operator
    /// boleh menulis <c>https://tunnel.example/webhook-pembayaran</c> maupun
    /// <c>https://tunnel.example</c> tanpa perlu tahu bedanya.
    /// </summary>
    public static string? BangunUrlWebhook(string? dasar)
    {
        if (string.IsNullOrWhiteSpace(dasar)) return null;

        var bersih = dasar.Trim().TrimEnd('/');
        if (!UrlValid(bersih)) return null;

        return bersih.EndsWith(PathWebhook, StringComparison.OrdinalIgnoreCase)
            ? bersih
            : bersih + PathWebhook;
    }

    /// <summary>
    /// Apakah URL ini bisa dipakai sebagai tujuan notifikasi: absolut, skema
    /// http/https, dan punya host. URL relatif atau skema asing ditolak —
    /// Midtrans hanya mengantar notifikasi ke URL HTTP(S).
    /// </summary>
    public static bool UrlValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return !string.IsNullOrWhiteSpace(uri.Host);
    }
}
