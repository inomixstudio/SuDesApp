using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Klaim token lisensi hasil dekode <c>payload</c> token bertanda tangan.
    /// Field-nya cermin struct yang sama di server (SuDesApp.Server/SigningService.cs).
    /// </summary>
    public sealed class KlaimLisensi
    {
        [JsonPropertyName("sub")] public string Sub { get; set; } = "";
        [JsonPropertyName("desa")] public string Desa { get; set; } = "";
        [JsonPropertyName("paket")] public string Paket { get; set; } = "bulanan";
        [JsonPropertyName("kode")] public string Kode { get; set; } = "";
        /// <summary>Waktu terbit token (Unix detik, UTC).</summary>
        [JsonPropertyName("iat")] public long Iat { get; set; }
        /// <summary>Jatuh tempo lisensi (Unix detik, UTC).</summary>
        [JsonPropertyName("exp")] public long Exp { get; set; }
        /// <summary>Waktu server terakhir mengonfirmasi langganan aktif (Unix detik, UTC).</summary>
        [JsonPropertyName("cek")] public long Cek { get; set; }
    }

    /// <summary>
    /// base64url (RFC 4648 §5) — tanpa padding, karakter URL-safe. Salinan
    /// persis dari versi server; mismatch di sini membuat semua token gagal
    /// diverifikasi, jadi perubahan harus dibuat di kedua sisi bersamaan.
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

    /// <summary>
    /// Verifikasi tanda tangan token lisensi di sisi klien — ECDSA P-256.
    ///
    /// Aplikasi desktop hanya memegang KUNCI PUBLIK. Kunci privat tidak pernah
    /// masuk ke binary klien, jadi kebocoran/dekompilasi exe tidak menghasilkan
    /// forged token yang valid.
    ///
    /// Mengapa ini wajib ada: tanpa verifikasi tanda tangan, status lisensi
    /// hanya bersandar pada berkas <c>lisensi-cache.json</c> yang berupa teks
    /// biasa di %LocalAppData% — readily editable. Menyunting berkas itu
    /// (aktif=true, cekTerakhir=sekarang) memberi lisensi gratis tanpa batas,
    /// sepenuhnya offline. Dengan token bertanda tangan, penyuntingan apa pun
    /// pada cache membuat verifikasi gagal dan aplikasi gagal tertutup (fail
    /// closed), bukan terbuka.
    /// </summary>
    public static class VerifikasiLisensi
    {
        private const string Versi = "v1";

        /// <summary>
        /// Verifikasi tanda tangan dan isi token.
        /// </summary>
        /// <param name="token">Token penuh <c>v1.payload.signature</c>.</param>
        /// <param name="kunciPublikB64Url">Kunci publik SPKI base64url.</param>
        /// <param name="klaim">Klaim hasil decode bila tanda tangan sah.</param>
        /// <returns>True bila token sah secara kriptografis.</returns>
        public static bool CobaVerifikasi(string? token, string? kunciPublikB64Url, out KlaimLisensi? klaim)
        {
            klaim = null;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(kunciPublikB64Url))
                return false;

            try
            {
                var bagian = token!.Split('.');
                if (bagian.Length != 3 || !string.Equals(bagian[0], Versi, StringComparison.Ordinal))
                    return false;

                var payload = B64Url.Decode(bagian[1]);
                var tandaTangan = B64Url.Decode(bagian[2]);

                using var kunci = ECDsa.Create();
                kunci.ImportSubjectPublicKeyInfo(B64Url.Decode(kunciPublikB64Url!), out _);

                // Verifikasi tanda tangan SEBELUM deserialize payload: payload
                // datang dari jaringan/cache tak tepercaya, dan deserialize
                // harus hanya jalan atas data yang sudah terbukti otentik.
                if (!kunci.VerifyData(payload, tandaTangan, HashAlgorithmName.SHA256))
                    return false;

                klaim = JsonSerializer.Deserialize<KlaimLisensi>(payload);
                return klaim is not null;
            }
            catch (Exception ex) when (ex is FormatException
                                    or System.Security.Cryptography.CryptographicException
                                    or JsonException
                                    or ArgumentException)
            {
                // Token rusak / kunci salah / base64 tak valid — perlakukan
                // sebagai tidak sah, jangan sampai galat ini menggagalkan start.
                return false;
            }
        }
    }
}
