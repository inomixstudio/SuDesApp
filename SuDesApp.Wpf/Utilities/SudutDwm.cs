using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Membulatkan sudut jendela lewat DWM Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE)
    /// tanpa mengubah cara jendela dibuat: WindowChrome, grip resize, dan AllowsTransparency
    /// tidak disentuh sehingga resize/drag tetap berfungsi normal — DWM hanya memotong
    /// sudut hasil compositing (bukan Window region, bukan alpha per-piksel).
    ///
    /// Batasan sesuai dokumen Microsoft "Apply rounded corners in desktop apps for Windows 11":
    /// - Hanya Windows 11 (build 22000+); Windows 10 dan sistem tanpa DWM modern dibiarkan persegi.
    /// - Jendela dengan AllowsTransparency (lapisan alpha per-piksel) TIDAK bisa dibulatkan
    ///   DWM — karena itu atribut ini hanya dipasang pada jendela ber-frame
    ///   (MainWindow, WordImporWindow); dialog kartu sudah membulat via ClipToBounds.
    /// - Saat maximized/snapped DWM sengaja TIDAK membulatkan (by design); jendela kembali
    ///   membulat otomatis saat di-restore.
    /// </summary>
    public static class SudutDwm
    {
        // Nilai dari dwmapi.h: DWMWINDOWATTRIBUTE dan DWM_WINDOW_CORNER_PREFERENCE.
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        /// <summary>Dipakai dengan PreserveSig=true agar kegagalan DWM tidak melempar
        /// exception di tengah pembukaan jendela (diamkan, jendela tampil persegi).</summary>
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Pasang pembulatan sudut pada jendela. Panggil setelah HWND terbentuk
        /// (OnSourceInitialized); bila HWND belum ada atau bukan Windows 11,
        /// jendela dilewati tanpa error.
        /// </summary>
        public static void Bulatkan(Window jendela)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

            IntPtr hwnd = new WindowInteropHelper(jendela).Handle;
            if (hwnd == IntPtr.Zero) return;

            int preferensi = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preferensi, sizeof(int));
        }
    }
}
