using SuDesApp.Utilities;

namespace SuDesApp.Supabase;

/// <summary>
/// Konfigurasi koneksi Supabase untuk sinkronisasi surat Android.
///
/// Rahasia (kata sandi basis data) disimpan lewat
/// <see cref="AppPreferenceStore.SetSecret"/> — DPAPI per akun Windows, bukan
/// teks polos di berkas. Sisanya (host, tenant, URL REST, kunci publik) bukan
/// rahasia dan disimpan apa adanya.
///
/// Nilai bawaan host/pooler mengikuti proyek Supabase desa; kata sandi dan
/// tenant diisi operator saat menghubungkan aplikasi ke Supabase.
/// </summary>
public sealed record SupabasePengaturan
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 6543;
    public string Pengguna { get; init; } = "";
    public string KataSandi { get; init; } = "";
    public string Tenant { get; init; } = "";
    public string UrlRest { get; init; } = "";
    public string KunciPublik { get; init; } = "";

    /// <summary>True bila keempat nilai wajib sudah ada — syarat sinkron bisa jalan.</summary>
    public bool Lengkap =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Pengguna) &&
        !string.IsNullOrWhiteSpace(KataSandi) &&
        !string.IsNullOrWhiteSpace(Tenant);

    /// <summary>
    /// String koneksi Npgsql. <c>Trust Server Certificate=true</c> karena
    /// pooler Supabase memakai sertifikat yang tidak cocok dengan nama host
    /// (<c>aws-&lt;region&gt;.pooler.supabase.com</c>) — tanpa ini koneksi
    /// ditolak saat validasi sertifikat.
    /// </summary>
    public string ConnectionString =>
        $"Host={Host};Port={Port};Username={Pengguna};Password={KataSandi};" +
        "Database=postgres;SSL Mode=Require;Trust Server Certificate=true;Include Error Detail=true";

    private const string KHost = "Supabase:Host";
    private const string KPort = "Supabase:Port";
    private const string KPengguna = "Supabase:Pengguna";
    private const string KKataSandi = "Supabase:KataSandi";
    private const string KTenant = "Supabase:Tenant";
    private const string KUrlRest = "Supabase:UrlRest";
    private const string KKunciPublik = "Supabase:KunciPublik";

    /// <summary>Baca konfigurasi dari penyimpanan preferensi. Tidak pernah melempar.</summary>
    public static SupabasePengaturan Muat()
    {
        var port = 6543;
        try
        {
            var teks = AppPreferenceStore.GetString(KPort, null);
            if (!string.IsNullOrWhiteSpace(teks) && int.TryParse(teks, out var p)) port = p;
        }
        catch
        {
            // Preferensi rusak bukan alasan menolak mulai; pakai bawaan.
        }

        return new SupabasePengaturan
        {
            Host = AppPreferenceStore.GetString(KHost, null)?.Trim() ?? "",
            Port = port,
            Pengguna = AppPreferenceStore.GetString(KPengguna, null)?.Trim() ?? "",
            KataSandi = AppPreferenceStore.GetSecret(KKataSandi) ?? "",
            Tenant = AppPreferenceStore.GetString(KTenant, null)?.Trim() ?? "",
            UrlRest = AppPreferenceStore.GetString(KUrlRest, null)?.Trim() ?? "",
            KunciPublik = AppPreferenceStore.GetString(KKunciPublik, null)?.Trim() ?? "",
        };
    }

    /// <summary>Simpan konfigurasi. Kata sandi lewat DPAPI; sisanya teks polos.</summary>
    public void Simpan()
    {
        AppPreferenceStore.SetString(KHost, Host);
        AppPreferenceStore.SetString(KPort, Port.ToString());
        AppPreferenceStore.SetString(KPengguna, Pengguna);
        AppPreferenceStore.SetSecret(KKataSandi, KataSandi);
        AppPreferenceStore.SetString(KTenant, Tenant);
        AppPreferenceStore.SetString(KUrlRest, UrlRest);
        AppPreferenceStore.SetString(KKunciPublik, KunciPublik);
    }

    /// <summary>Bersihkan konfigurasi (mis. saat memutuskan hubungan Supabase).</summary>
    public static void Hapus()
    {
        AppPreferenceStore.SetString(KHost, string.Empty);
        AppPreferenceStore.SetString(KPort, string.Empty);
        AppPreferenceStore.SetString(KPengguna, string.Empty);
        AppPreferenceStore.SetSecret(KKataSandi, null);
        AppPreferenceStore.SetString(KTenant, string.Empty);
        AppPreferenceStore.SetString(KUrlRest, string.Empty);
        AppPreferenceStore.SetString(KKunciPublik, string.Empty);
    }
}
