using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Pembaca kunci database desa.db yang dipakai versi lama (SQLCipher),
    /// tersimpan terenkripsi DPAPI per user Windows di
    /// %LOCALAPPDATA%\SuDesApp\DesaKunciDb.bin.
    ///
    /// Kelas ini HANYA membaca — tidak pernah membuat atau menimpa kunci.
    /// Aplikasi versi ini menyimpan database baru sebagai SQLite biasa tanpa
    /// kunci, jadi kunci hanya dibutuhkan untuk membuka database yang sudah
    /// terenkripsi oleh versi sebelumnya. Membuat kunci baru secara diam-diam
    /// akan menyegel database lama selamanya, dan itu justru masalah yang
    /// membuat enkripsi otomatis dihapus.
    /// </summary>
    public static class KunciDatabase
    {
        /// <summary>Lokasi berkas untuk pengujian; null = lokasi asli milik user.</summary>
        internal static Func<string>? LokasiOverride { get; set; }

        private static string StorePath => LokasiOverride?.Invoke()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", "DesaKunciDb.bin");

        /// <summary>True bila berkas kunci ada (belum tentu bisa dibaca akun Windows ini).</summary>
        public static bool Ada => File.Exists(StorePath);

        /// <summary>Lokasi berkas untuk pesan galat & diagnosa.</summary>
        public static string LokasiTampil => StorePath;

        /// <summary>
        /// Muat kunci tersimpan. Berkas belum ada / kosong / tidak bisa didekripsi
        /// (salinan dari mesin atau akun Windows lain) → null. Pemanggil yang
        /// menemukan database terenkripsi tanpa kunci menyusun pesan galatnya
        /// sendiri lewat <c>BerkasDatabase</c>.
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
                // Berkas rusak, dibuat akun Windows lain, atau DPAPI tidak
                // tersedia: anggap kunci tidak ada — pemanggil yang menjelaskan.
                return null;
            }
        }
    }
}
