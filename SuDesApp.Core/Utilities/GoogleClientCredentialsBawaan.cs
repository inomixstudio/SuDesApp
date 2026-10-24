using System;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Kredensial klien OAuth Google yang tertanam (embed) di dalam aplikasi.
    ///
    /// LATAR: kredensial semula diisi manual oleh teknisi lewat halaman Pengaturan
    /// lalu disimpan terenkripsi DPAPI per user Windows — artinya setiap instalasi
    /// baru (dan setiap user Windows baru) harus mengulang langkah itu. Sekarang
    /// kredensial dipasangkan langsung di sini sehingga ikut terbawa di dalam
    /// installer dan login Google/Drive/Formulir/Sheet langsung siap pakai.
    ///
    /// Nilai di bawah TIDAK ditampilkan di halaman Pengaturan dan tidak boleh
    /// diubah oleh pengguna aplikasi — hanya pemegang kode sumber yang mengaturnya.
    ///
    /// RAHASIA: berkas yang ter-commit ke git sengaja hanya memuat PLACEHOLDER.
    /// Nilai asli disimpan di GoogleClientCredentialsBawaan.lokal.cs yang
    /// dikecualikan dari git (.gitignore). Bila berkas lokal itu ada, proyek
    /// memakainya dan mengabaikan placeholder ini (lihat kondisi di
    /// SuDesApp.Core.csproj). Salin berkas lokal antar mesin build secara manual,
    /// JANGAN pernah lewat git.
    /// </summary>
#if !GOOGLE_KREDENSIAL_LOKAL
    internal static class GoogleClientCredentialsBawaan
    {
        /// <summary>Client ID OAuth (Google Cloud Console → OAuth 2.0 Client IDs, tipe Desktop app).</summary>
        /// <remarks>PLACEHOLDER — nilai asli ada di GoogleClientCredentialsBawaan.lokal.cs.</remarks>
        public const string ClientId = "GANTI_CLIENT_ID.apps.googleusercontent.com";

        /// <summary>Client Secret OAuth dari halaman yang sama.</summary>
        /// <remarks>PLACEHOLDER — nilai asli ada di GoogleClientCredentialsBawaan.lokal.cs.</remarks>
        public const string ClientSecret = "GANTI_CLIENT_SECRET";

        /// <summary>Benar bila pasangan kredensial bawaan sudah diisi nilai asli.</summary>
        public static bool Tersedia =>
            !string.IsNullOrWhiteSpace(ClientId)
            && !ClientId.StartsWith("GANTI_", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(ClientSecret)
            && !ClientSecret.StartsWith("GANTI_", StringComparison.Ordinal);
    }
#endif
}
