using System;
using System.Text.RegularExpressions;

namespace SuDesApp.Services
{
    /// <summary>
    /// Aturan bentuk kode aktivasi manual, satu-satunya tempat yang menjawab
    /// "kode yang sah itu seperti apa".
    ///
    /// Bentuk resmi: <c>SUDES-XXXX-XXXX</c> — awalan SUDES, dua segmen
    /// empat karakter, huruf besar (dibuat server lewat
    /// <c>GeneratorKodeAktivasi</c> di SuDesApp.Server dengan alfabet tanpa
    /// karakter rancu: tanpa 0/O dan 1/I/L). Pencarian di server sensitif
    /// huruf besar, jadi kode yang diketik huruf kecil otomatis gagal
    /// ("tidak dikenal") walau kodenya benar — desa yang menyalin kode
    /// dari WhatsApp juga sering membawa spasi, baris baru, atau tanda
    /// hubung yang hilang. Kelas ini menormalisasi ketikan itu sebelum
    /// dikirim, dan memberi tahu pemakai bila bentuknya belum lengkap.
    /// </summary>
    public static class FormatKodeAktivasi
    {
        public static string Normalisasi(string? kode)
        {
            if (string.IsNullOrWhiteSpace(kode)) return string.Empty;

            // Sering terjadi saat menyalin dari WhatsApp/PDF: spasi, tab,
            // baris baru, dan spasi tak-terputus ikut terbawa. Semuanya
            // dibuang; tanda hubung dipertahankan (dicek ulang di bawah).
            var tanpaSpasi = Regex.Replace(kode.Trim(), @"\s+", string.Empty, RegexOptions.CultureInvariant)
                .ToUpperInvariant();

            // "SUDESK7Q24831" (tanpa tanda hubung), "SUDES K7Q2 4831" (spasi),
            // dan "SUDES-K7Q24831" (hubung hilang) semuanya disisipi
            // tanda hubungnya supaya menjadi bentuk resmi.
            const string awalan = "SUDES";
            var tanpaHubung = tanpaSpasi.Replace("-", string.Empty);
            if (tanpaHubung.StartsWith(awalan, StringComparison.Ordinal)
                && tanpaHubung.Length == awalan.Length + 8)
            {
                var badan = tanpaHubung.Substring(awalan.Length, 8);
                return $"{awalan}-{badan.Substring(0, 4)}-{badan.Substring(4, 4)}";
            }

            // Tanda hubung ganda dari tempel yang berantakan dilipat jadi satu.
            return Regex.Replace(tanpaSpasi, @"-{2,}", "-", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Bentuk resmi <c>SUDES-XXXX-XXXX</c>: segmen badan hanya huruf/angka
        /// (0-9, A-Z). Karakter ambigu 0/O/1/I/L tetap diterima di sini
        /// supaya galatnya "kode tidak dikenal" dari server, bukan "format
        /// salah" yang menyesatkan — hanya penulisan yang jelas tidak
        /// berbentuk kode yang ditolak di sini.
        /// </summary>
        public static bool BentukSah(string? kode)
        {
            if (string.IsNullOrWhiteSpace(kode)) return false;
            var ternormalisasi = Normalisasi(kode);
            return Regex.IsMatch(ternormalisasi, @"^SUDES-[A-Z0-9]{4}-[A-Z0-9]{4}$", RegexOptions.CultureInvariant);
        }

        /// <summary>Panjang bentuk resmi total, mis. 13 untuk SUDES-XXXX-XXXX.</summary>
        public static int PanjangSah => 13;
    }
}
