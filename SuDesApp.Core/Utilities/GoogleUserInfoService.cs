using Google.Apis.Drive.v3;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>Informasi profil akun Google yang sedang login.</summary>
    public sealed class GoogleAccountInfo
    {
        public string Email { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public byte[]? PhotoBytes { get; init; }
    }

    /// <summary>
    /// Utilitas pengambilan profil akun Google dari klien Drive yang sudah
    /// terautentikasi: email, nama tampilan, dan foto profil (diunduh dari
    /// photoLink Google sebagai byte agar bisa ditampilkan di UI).
    /// </summary>
    public static class GoogleUserInfoService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// Ambil profil akun yang sedang login. Mengembalikan null bila
        /// informasi email tidak diperoleh; foto bersifat opsional.
        /// </summary>
        public static async Task<GoogleAccountInfo?> GetAccountInfoAsync(DriveService client, CancellationToken ct = default)
        {
            // API Drive v3 mewajibkan parameter fields pada About.Get().
            var request = client.About.Get();
            request.Fields = "user(displayName,emailAddress,photoLink)";
            var about = await request.ExecuteAsync(ct).ConfigureAwait(false);
            var user = about?.User;
            if (user == null || string.IsNullOrEmpty(user.EmailAddress))
                return null;

            byte[]? photo = null;
            try
            {
                if (!string.IsNullOrEmpty(user.PhotoLink))
                    photo = await Http.GetByteArrayAsync(user.PhotoLink, ct).ConfigureAwait(false);
            }
            catch
            {
                // Foto profil opsional — kegagalan unduh tidak menggagalkan login.
            }

            return new GoogleAccountInfo
            {
                Email = user.EmailAddress,
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.EmailAddress : user.DisplayName,
                PhotoBytes = photo
            };
        }
    }
}
