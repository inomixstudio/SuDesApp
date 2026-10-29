using System;
using System.IO;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Satu tempat untuk menemukan <c>powershell.exe</c>.
    ///
    /// Aplikasi menjalankan skrip pendek (memasang pembaruan, menerapkan tambalan,
    /// membuka ulang dirinya) lewat PowerShell. Nama telanjang bergantung pada PATH
    /// proses induk; bila aplikasi dibuka dari shell pengembangan yang PATH-nya
    /// tidak memuat System32, skrip tidak pernah berjalan padahal semua berkasnya
    /// sudah siap. Jalur lengkap Windows PowerShell dipakai lebih dulu, dengan nama
    /// telanjang sebagai cadangan.
    /// </summary>
    public static class PowerShellWindows
    {
        public static string JalurExecutable()
        {
            try
            {
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windows))
                {
                    var lengkap = Path.Combine(
                        windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

                    if (File.Exists(lengkap)) return lengkap;
                }
            }
            catch
            {
                // Folder Windows tidak terbaca (konfigurasi tidak lazim) — pakai nama telanjang.
            }

            return "powershell.exe";
        }
    }
}
