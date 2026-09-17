using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Penyimpan kredensial klien OAuth Google (clientId + clientSecret) yang
    /// dienkripsi DPAPI per user Windows (CurrentUser).
    ///
    /// LATAR: dulu clientId/clientSecret ditulis plaintext di appsettings.json yang
    /// ikut ter-distribusi ke setiap instalasi. Sekarang appsettings tidak lagi
    /// memuat secret; kredensial diisi sekali oleh teknisi/admin komputer kantor
    /// lewat <see cref="Save"/> dan disimpan terenkripsi di profil user:
    /// %LOCALAPPDATA%\SuDesApp\GoogleClientCredentials.bin
    ///
    /// DPAPI CurrentUser: hanya akun Windows yang mengenkripsi yang dapat
    /// mendekripsi — sinkron dengan penyimpanan token Google yang sudah
    /// berada di profil user yang sama.
    /// </summary>
    public class GoogleClientCredentials
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SuDesApp", "GoogleClientCredentials.bin");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        public static bool Exists() => File.Exists(StorePath);

        /// <summary>Simpan kredensial terenkripsi DPAPI (CurrentUser) ke profil user.</summary>
        public static void Save(string clientId, string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                throw new ArgumentException("clientId dan clientSecret wajib diisi.");

            var payload = JsonSerializer.Serialize(new GoogleClientCredentials
            {
                ClientId = clientId.Trim(),
                ClientSecret = clientSecret.Trim()
            });

            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            var encrypted = ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(payload),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, encrypted);
        }

        /// <summary>
        /// Muat kredensial terenkripsi. Mengembalikan null bila file belum ada
        /// atau tidak dapat dibaca/didekripsi (mis. dipindah antar user Windows).
        /// </summary>
        public static GoogleClientCredentials? Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return null;

                var decrypted = ProtectedData.Unprotect(
                    File.ReadAllBytes(StorePath),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser);

                return JsonSerializer.Deserialize<GoogleClientCredentials>(
                    System.Text.Encoding.UTF8.GetString(decrypted), JsonOptions);
            }
            catch (Exception)
            {
                // File korup / dibuat user Windows lain / dibaca tanpa hak DPAPI —
                // perlakukan seolah belum dikonfigurasi agar alur setup berjalan normal.
                return null;
            }
        }

        /// <summary>Lokasi penyimpanan (untuk pesan setup &amp; diagnostik).</summary>
        public static string StoreDisplayPath => StorePath;
    }
}
