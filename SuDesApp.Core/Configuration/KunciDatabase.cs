using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Penyimpanan kunci enkripsi database desa.db (SQLCipher) secara terenkripsi
    /// DPAPI per user Windows, di %LOCALAPPDATA%\SuDesApp\DesaKunciDb.bin.
    ///
    /// Kunci database adalah satu-satunya penjaga berkas desa.db, jadi tidak boleh
    /// disimpan plaintext di appsettings.json atau berkas preferensi — keduanya
    /// mudah ikut terkopi bersama folder aplikasi. DPAPI CurrentUser konsisten
    /// dengan penyimpanan kunci API dan token Google: hanya akun Windows yang
    /// mengenkripsi yang dapat mendekripsi, sehingga salinan desa.db yang bocor
    /// tidak terbaca di mesin lain.
    ///
    /// Kunci dibuat acak 32 byte (256 bit) oleh <see cref="MuatAtauBuat"/>; tidak
    /// ada kunci bawaan yang bisa ditebak. Kehilangan berkas kunci = database tidak
    /// bisa dibuka lagi, jadi jangan pernah menghapusnya selain bersama cadangan
    /// database yang utuh.
    /// </summary>
    public static class KunciDatabase
    {
        /// <summary>Panjang kunci acak dalam byte (256 bit).</summary>
        public const int PanjangKunci = 32;

        /// <summary>Lokasi berkas untuk pengujian; null = lokasi asli milik user.</summary>
        internal static Func<string>? LokasiOverride { get; set; }

        private static string StorePath => LokasiOverride?.Invoke()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", "DesaKunciDb.bin");

        /// <summary>True bila berkas kunci sudah ada (belum tentu bisa dibaca).</summary>
        public static bool Ada => File.Exists(StorePath);

        /// <summary>Lokasi berkas untuk pesan galat & diagnosa.</summary>
        public static string LokasiTampil => StorePath;

        /// <summary>
        /// Muat kunci tersimpan. Berkas belum ada / kosong / tidak bisa didekripsi
        /// (salinan dari mesin akun Windows lain) → null; pemanggil yang butuh
        /// kunci wajib menangani null lewat <see cref="MuatAtauBuat"/> atau galat
        /// yang jelas.
        /// </summary>
        public static string? Muat()
        {
            try
            {
                if (!File.Exists(StorePath)) return null;

                var isi = ProtectedData.Unprotect(
                    File.ReadAllBytes(StorePath),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser);
                var kunci = Encoding.UTF8.GetString(isi).Trim();
                return kunci.Length == 0 ? null : kunci;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Muat kunci; bila belum pernah dibuat, buat acak 256 bit lalu simpan.
        /// Berkas ADA tetapi tidak bisa dibaca → lempar galat jelas: membuat kunci
        /// baru diam-diam justru menyegel database lama selamanya (kuncinya hilang).
        /// </summary>
        public static string MuatAtauBuat()
        {
            var kunci = Muat();
            if (kunci != null) return kunci;

            if (File.Exists(StorePath))
                throw new InvalidOperationException(
                    $"Berkas kunci database ada tetapi tidak bisa dibaca: {StorePath}\n" +
                    "Kemungkinan berkas rusak atau dibuat oleh akun Windows lain. " +
                    "Jangan membuat kunci baru sebelum berkas kunci lama dipulihkan — " +
                    "database yang sudah terenkripsi tidak akan bisa dibuka tanpa kunci aslinya.");

            kunci = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PanjangKunci));
            Simpan(kunci);
            return kunci;
        }

        /// <summary>Simpan kunci (base64) ke berkas DPAPI; menimpa isi lama.</summary>
        public static void Simpan(string kunci)
        {
            if (string.IsNullOrWhiteSpace(kunci))
                throw new ArgumentException("Kunci database tidak boleh kosong.", nameof(kunci));

            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var terenkripsi = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(kunci),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, terenkripsi);
        }
    }
}
