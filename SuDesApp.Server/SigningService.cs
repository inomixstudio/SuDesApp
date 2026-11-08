using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Server;

/// <summary>
/// Klaim yang dibawa token lisensi. SEMUA medan di dalam token ditandatangani
/// server — inilah alasan token boleh dipercaya aplikasi offline: klien tidak
/// bisa mengubah <c>cek</c> (waktu konfirmasi terakhir) tanpa merusak tanda
/// tangan, sehingga masa tenggang offline tidak bisa dipalsukan lewat edit
/// berkas cache lokal.
/// </summary>
public sealed class KlaimLisensi
{
    /// <summary>Id pelanggan/desa (JWT "sub").</summary>
    [JsonPropertyName("sub")] public string Sub { get; set; } = "";
    /// <summary>Nama desa untuk ditampilkan di aplikasi.</summary>
    [JsonPropertyName("desa")] public string Desa { get; set; } = "";
    /// <summary>Nama paket langganan (mis. "bulanan").</summary>
    [JsonPropertyName("paket")] public string Paket { get; set; } = "bulanan";
    /// <summary>Kode aktivasi milik desa ini.</summary>
    [JsonPropertyName("kode")] public string Kode { get; set; } = "";
    /// <summary>Waktu token diterbitkan (Unix detik, UTC).</summary>
    [JsonPropertyName("iat")] public long Iat { get; set; }
    /// <summary>Waktu jatuh tempo lisensi (Unix detik, UTC) = berlaku_sampai.</summary>
    [JsonPropertyName("exp")] public long Exp { get; set; }
    /// <summary>
    /// Waktu server terakhir mengonfirmasi langganan aktif (Unix detik, UTC).
    /// <b>Ancang anti-putar-balik</b>: masa tenggang offline dihitung dari nilai
    /// ini, dan karena bagian dari payload yang ditandatangani, pengguna tidak
    /// bisa memperpanjang tenggang dengan mengedit berkas cache.
    /// </summary>
    [JsonPropertyName("cek")] public long Cek { get; set; }
}

/// <summary>
/// Penandatangan token lisensi — ECDSA P-256 (kurva NIST P-256).
///
/// Format token: <c>v1.&lt;payload base64url&gt;.&lt;signature base64url&gt;</c>.
///
/// DESAIN KEAMANAN
/// ---------------
/// Kunci privat hanya ada di server (berkas <c>keys/lisensi-signing.key</c>,
/// di luar git). Aplikasi desktop hanya menerima <b>kunci publik</b> yang
/// ditanam di dalam binary, sehingga verifikasi bisa berjalan penuh OFFLINE
/// tanpa memegang rahasia apa pun.
///
/// Ini menutup jalur bypass yang sebelumnya ada: bila aplikasi
/// hanya percaya berkas cache lokal, siapa pun bisa menulis
/// <c>{"aktif":true,"cekTerakhir":"&lt;sekarang&gt;"}</c> dan mendapat
/// lisensi gratis selamanya tanpa internet. Dengan token bertanda tangan,
/// modifikasi apa pun pada cache membuat verifikasi gagal (fail closed).
///
/// Rotasi kunci: ganti berkas kunci, lalu distribute ulang kunci publik baru.
/// Token lama otomatis ditolak — itu perilaku yang diinginkan saat kunci
/// dicurigakan bocor.
/// </summary>
public class SigningService
{
    /// <summary>Versi format token; part of the string untuk memisahkan rotate format.</summary>
    private const string Versi = "v1";

    /// <summary>
    /// Identitas kunci (kid): 16 hex pertama SHA-256 dari kunci publik SPKI.
    /// Token baru: <c>v2.&lt;kid&gt;.&lt;payload&gt;.&lt;signature&gt;</c> — klien memilih
    /// kunci verifikasi yang cocok. Format lama <c>v1.payload.signature</c> tetap
    /// diterima selama masa transisi (dianggap kid kunci tertanam).
    /// </summary>
    public static string KidDariPublik(string kunciPublikB64Url)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(kunciPublikB64Url));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private readonly ECDsa _privat;
    private readonly string _kunciPublikB64Url;
    private readonly ILogger<SigningService> _logger;

    /// <param name="basis">
    /// Direktori dasar untuk jalur kunci bawaan saat <c>Lisensi:KeyPath</c> dan
    /// <c>LISENSI_KEY_PATH</c> sama-sama kosong. Di server ini diisi
    /// <c>ContentRootPath</c> supaya kunci jatuh di <c>&lt;proyek&gt;/keys/</c>,
    /// bukan di direktori kerja proses (yang bisa berbeda antara `dotnet run`,
    /// Visual Studio, dan layanan terpasang).
    /// </param>
    public SigningService(IConfiguration config, ILogger<SigningService> logger, string? basis = null)
    {
        _logger = logger;

        // Env var DIDULUKAN atas appsettings. Urutan config ?? env yang dipakai
        // sebelumnya salah: appsettings.lokal.json dimuat paling akhir di
        // Program.cs sehingga selalu menang atas environment variable. Begitu
        // berkas lokal itu mendefinisikan Lisensi:KeyPath, LISENSI_KEY_PATH
        // diabaikan - padahal dokumentasi (docs/deploy-server.md) justru
        // mengarahkan operator untuk menaruh kunci di volume persisten lewat
        // env tersebut. Akibatnya container membuat kunci ECDSA baru tiap
        // restart dan SETIAP token lisensi pelanggan yang sudah terbit
        // langsung invalid, tanpa satu pun pesan error.
        //
        // NILAI KOSONG ≠ TIDAK DIISI. appsettings.json bawaan sengaja memuat
        // "KeyPath": "" sebagai placeholder, dan string kosong bukan null —
        // sehingga `?? jalur-bawaan` tidak pernah dipakai. Path.GetFullPath("")
        // lalu melempar ArgumentException tepat saat start, dan server lisensi
        // TIDAK PERNAH hidup (klien selalu melihat "server tidak bekerja").
        // IsNullOrWhiteSpace membuat kosong dan spasi berperilaku seperti tidak
        // diisi, sehingga tersambung ke kunci yang sudah ada di keys/.
        var path = Environment.GetEnvironmentVariable("LISENSI_KEY_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = config["Lisensi:KeyPath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(basis ?? Directory.GetCurrentDirectory(), "keys", "lisensi-signing.key");

        (_privat, _kunciPublikB64Url) = MuatAtauBuat(path);
    }

    /// <summary>
    /// Kunci publik SubjectPublicKeyInfo dalam base64url — inilah yang ditanam
    /// di aplikasi (appsettings <c>Lisensi:KunciPublik</c>) dan yang dipakai
    /// <see cref="Verifikasi"/> sebagai akar kepercayaan.
    /// </summary>
    public string KunciPublikB64Url => _kunciPublikB64Url;

    /// <summary>
    /// Muat pasangan kunci dari disk, atau buat baru bila belum ada. Kunci privat
    /// disimpan sebagai PKCS#8 base64; publik diekspor ulang tiap kali dimuat
    /// sehingga keduanya dijamin konsisten.
    /// </summary>
    private (ECDsa Kunci, string PublikB64Url) MuatAtauBuat(string path)
    {
        var penuh = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(penuh);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        ECDsa kunci;
        if (File.Exists(penuh))
        {
            var isi = File.ReadAllText(penuh).Trim();
            kunci = ECDsa.Create();
            kunci.ImportPkcs8PrivateKey(Convert.FromBase64String(isi), out _);
            ServerLog.KunciLisensiDimuat(_logger, penuh);
        }
        else
        {
            // Auto-generate TIDAK boleh terjadi di Production eksplisit:
            // volume kunci yang hilang berarti identitas penandatangan berubah
            // diam-diam dan SEMUA token lama mati. Deploy Railway/Render
            // selalu men-set ASPNETCORE_ENVIRONMENT=Production, jadi kondisi
            // ini menutup jalur itu. Bila env tidak diset (uji lokal / xunit),
            // auto-generate diizinkan agar perilaku lama tetap teruji.
            var lingkungan = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            if (string.Equals(lingkungan, "Production", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Kunci penandatangan lisensi tidak ditemukan di '{penuh}'. " +
                    "Pasang volume kunci (LISENSI_KEY_PATH) sebelum start; menolak membuat kunci baru di Production.");
            }
            kunci = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pkcs8 = kunci.ExportPkcs8PrivateKey();
            File.WriteAllText(penuh, Convert.ToBase64String(pkcs8));
            ServerLog.KunciLisensiDibuat(_logger, penuh);
        }

        return (kunci, B64Url.Encode(kunci.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Tandatangani klaim menjadi token yang bisa diverifikasi offline (format v2 + kid).</summary>
    public string Tandatangani(KlaimLisensi klaim)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(klaim);
        var payloadB64 = B64Url.Encode(payload);

        // Yang ditandatangani hanya payload. Prefix v2+kid dibaca terpisah saat
        // verifikasi, jadi tidak perlu ikut di dalam payload.
        var tandaTangan = _privat.SignData(payload, HashAlgorithmName.SHA256);
        var kid = KidDariPublik(_kunciPublikB64Url);

        return $"v2.{kid}.{payloadB64}.{B64Url.Encode(tandaTangan)}";
    }

    /// <summary>
    /// Verifikasi token terhadap kunci publik. Dipakai oleh pengujian dan
    /// endpoint diagnostik — aplikasi desktop punya salinan implementasi
    /// yang sama persis (tanpa kunci privat) di SuDesApp.Core.
    /// </summary>
    public bool CobaVerifikasi(string token, string kunciPublikB64Url, out KlaimLisensi? klaim)
    {
        klaim = null;
        try
        {
            var bagian = token.Split('.');

            // Format baru v2: v2.<kid>.<payload>.<sig>
            if (bagian.Length == 4 && bagian[0] == "v2")
            {
                var payloadV2 = B64Url.Decode(bagian[2]);
                var tandaTanganV2 = B64Url.Decode(bagian[3]);
                if (!string.Equals(bagian[1], KidDariPublik(kunciPublikB64Url), StringComparison.Ordinal))
                    return false;
                using var kunciV2 = ECDsa.Create();
                kunciV2.ImportSubjectPublicKeyInfo(B64Url.Decode(kunciPublikB64Url), out _);
                if (!kunciV2.VerifyData(payloadV2, tandaTanganV2, HashAlgorithmName.SHA256)) return false;
                klaim = JsonSerializer.Deserialize<KlaimLisensi>(payloadV2);
                return klaim is not null;
            }

            // Format lama v1 (transisi): v1.<payload>.<sig>
            if (bagian.Length != 3 || bagian[0] != Versi) return false;

            var payload = B64Url.Decode(bagian[1]);
            var tandaTangan = B64Url.Decode(bagian[2]);

            using var kunci = ECDsa.Create();
            kunci.ImportSubjectPublicKeyInfo(B64Url.Decode(kunciPublikB64Url), out _);
            if (!kunci.VerifyData(payload, tandaTangan, HashAlgorithmName.SHA256)) return false;

            klaim = JsonSerializer.Deserialize<KlaimLisensi>(payload);
            return klaim is not null;
        }
        catch (Exception ex)
        {
            ServerLog.VerifikasiTokenGagal(_logger, ex);
            return false;
        }
    }
}

/// <summary>
/// base64url (RFC 4648 §5): varian tanpa padding dan dengan karakter URL-safe.
/// Dipakai di sisi server dan disalin persis ke sisi klien agar format token
/// sama.
/// </summary>
public static class B64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: throw new FormatException("Panjang base64url tidak valid.");
        }
        return Convert.FromBase64String(b64);
    }
}
