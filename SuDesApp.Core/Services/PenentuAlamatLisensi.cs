using System;

namespace SuDesApp.Services;

/// <summary>
/// Alamat server lisensi yang dipakai satu proses, beserta status kuncinya.
/// </summary>
/// <param name="Url">Alamat yang sudah dirapikan (tanpa garis miring di ujung).</param>
/// <param name="Terkunci">
/// True bila alamat berasal dari konfigurasi rilis dan tidak boleh ditimpa
/// preferensi pengguna. Alamat loopback (server uji di komputer sendiri) tidak
/// pernah mengunci, supaya pengujian lokal tetap bisa berganti alamat.
/// </param>
public readonly record struct AlamatLisensi(string Url, bool Terkunci);

/// <summary>
/// Satu-satunya tempat yang memutuskan alamat server lisensi mana yang menang.
///
/// Aturan ini sengaja dipisah dari <c>App.xaml.cs</c> supaya bisa diuji: alamat
/// server adalah akar kepercayaan lisensi, dan kesalahan kecil di sini membuat
/// aplikasi desa menghubungi server yang salah tanpa gejala yang terlihat.
///
/// Urutan keputusan:
/// 1. Konfigurasi build yang bukan loopback (URL produksi) SELALU menang dan
///    mengunci alamat. Preferensi lama di profil pengguna — termasuk
///    <c>http://localhost:5000</c> dari build pengembangan — tidak boleh
///    menimpanya lagi.
/// 2. Bila konfigurasi masih loopback/kosong, preferensi pengguna boleh
///    menang. Ini yang membuat server uji lokal bisa diarahkan dari halaman
///    Langganan tanpa merilis ulang aplikasi.
/// 3. Bila keduanya tidak sah, pakai alamat loopback bawaan.
/// </summary>
public static class PenentuAlamatLisensi
{
    public const string AlamatBawaan = "http://localhost:5000";

    public static AlamatLisensi Tentukan(string? alamatKonfigurasi, string? alamatPreferensi)
    {
        string konfigurasi = Rapikan(alamatKonfigurasi);
        string preferensi = Rapikan(alamatPreferensi);

        if (AlamatProduksi(konfigurasi))
        {
            return new AlamatLisensi(konfigurasi, Terkunci: true);
        }

        if (AlamatSah(preferensi))
        {
            return new AlamatLisensi(preferensi, Terkunci: false);
        }

        return new AlamatLisensi(AlamatSah(konfigurasi) ? konfigurasi : AlamatBawaan, Terkunci: false);
    }

    /// <summary>Rapikan spasi dan garis miring di ujung, tanpa mengubah isi lain.</summary>
    public static string Rapikan(string? alamat) => (alamat ?? string.Empty).Trim().TrimEnd('/');

    /// <summary>Alamat absolut HTTP/HTTPS yang bisa dipanggil.</summary>
    public static bool AlamatSah(string? alamat)
    {
        return Uri.TryCreate(Rapikan(alamat), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Alamat yang menunjuk komputer sendiri (server uji lokal).</summary>
    public static bool AlamatLoopback(string? alamat)
    {
        return Uri.TryCreate(Rapikan(alamat), UriKind.Absolute, out var uri)
            && (uri.IsLoopback
                || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Alamat produksi = sah, bukan loopback, dan memakai HTTPS.</summary>
    public static bool AlamatProduksi(string? alamat)
    {
        return AlamatSah(alamat)
            && !AlamatLoopback(alamat)
            && Uri.TryCreate(Rapikan(alamat), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
