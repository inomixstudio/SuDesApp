using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SuDesApp.Services
{
    /// <summary>
    /// Pengelola kata sandi akun aplikasi.
    ///
    /// Memakai PBKDF2-SHA256 (bukan sekali SHA-256 seperti file login lama):
    /// hash diulang puluhan ribu kali sehingga menebak kata sandi dari salinan
    /// database menjadi jauh lebih mahal. Jumlah putaran disimpan per akun
    /// (<c>Pengguna.Iterasi</c>), jadi putaran bisa dinaikkan di versi
    /// berikutnya tanpa memaksa semua orang mengganti kata sandi.
    /// </summary>
    public static class KataSandiPengguna
    {
        /// <summary>Jumlah putaran bawaan (OWASP 2023 untuk PBKDF2-SHA256: 600.000; 120.000 dipilih agar login di komputer desa tetap cepat).</summary>
        public const int IterasiBawaan = 120_000;

        public const int PanjangSalt = 16;
        public const int PanjangHash = 32;

        /// <summary>Panjang minimal kata sandi baru.</summary>
        public const int PanjangMinimal = 8;

        /// <summary>Salt acak baru, dalam base64.</summary>
        public static string BuatSalt()
        {
            var salt = RandomNumberGenerator.GetBytes(PanjangSalt);
            return Convert.ToBase64String(salt);
        }

        /// <summary>Hash PBKDF2 dari kata sandi + salt, dalam base64.</summary>
        public static string HitungHash(string kataSandi, string salt, int iterasi = IterasiBawaan)
        {
            if (kataSandi is null) throw new ArgumentNullException(nameof(kataSandi));
            if (string.IsNullOrWhiteSpace(salt)) throw new ArgumentException("Salt tidak boleh kosong.", nameof(salt));
            if (iterasi <= 0) throw new ArgumentOutOfRangeException(nameof(iterasi));

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                kataSandi, Convert.FromBase64String(salt), iterasi,
                HashAlgorithmName.SHA256, PanjangHash);

            return Convert.ToBase64String(hash);
        }

        /// <summary>True bila kata sandi cocok dengan salt/hash tersimpan (perbandingan waktu tetap).</summary>
        public static bool Cocok(string? kataSandi, string? salt, string? hash, int iterasi)
        {
            if (string.IsNullOrEmpty(kataSandi) || string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(hash))
                return false;

            try
            {
                var dihitung = HitungHash(kataSandi!, salt!, iterasi <= 0 ? IterasiBawaan : iterasi);
                return CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(dihitung),
                    System.Text.Encoding.UTF8.GetBytes(hash!));
            }
            catch (FormatException)
            {
                // Salt/hash rusak di database: perlakukan sebagai tidak cocok.
                return false;
            }
        }

        /// <summary>Aturan kata sandi baru; kosong berarti lolos.</summary>
        public static IReadOnlyList<string> Validasi(string? kataSandi, string? username = null)
        {
            var temuan = new List<string>();

            if (string.IsNullOrWhiteSpace(kataSandi))
            {
                temuan.Add("Kata sandi wajib diisi.");
                return temuan;
            }

            if (kataSandi!.Length < PanjangMinimal)
                temuan.Add($"Kata sandi minimal {PanjangMinimal} karakter.");

            if (!string.IsNullOrWhiteSpace(username)
                && string.Equals(kataSandi.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase))
                temuan.Add("Kata sandi tidak boleh sama dengan nama pengguna.");

            return temuan;
        }
    }
}
