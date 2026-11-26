using System;
using System.Runtime.InteropServices;

namespace SudesPdf;

/// <summary>
/// Ukuran lembar yang sebenarnya, dibaca langsung dari device context printer.
///
/// <c>PageSettings.PaperSize</c> hanya menggemakan nilai yang diisi aplikasi ke
/// devmode; banyak driver memakainya sambil mengembalikan kertas lain (Microsoft
/// Print to PDF menukar formulir kustom dengan A4, driver Fax mengosongkan
/// lebarnya). PHYSICALWIDTH/PHYSICALHEIGHT adalah kertas yang benar-benar lewat di
/// roller, jadi angka itulah yang dipakai untuk menata halaman; PaperSize hanya jadi
/// pembanding untuk tahu bahwa driver menolak ukuran aplikasi.
/// </summary>
internal static class PrintDc
{
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;
    private const int PhysicalWidth = 110;
    private const int PhysicalHeight = 111;
    private const int PhysicalOffsetX = 112;
    private const int PhysicalOffsetY = 113;

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    /// <summary>Semua nilai dalam inci (dan dpi) sebagaimana dilaporkan driver.</summary>
    internal readonly record struct Lembar(
        double LebarIn,
        double TinggiIn,
        double TepiKiriIn,
        double TepiAtasIn,
        double DpiX,
        double DpiY)
    {
        /// <summary>Driver tidak memberi ukuran fisik sama sekali (0 x 0).</summary>
        public bool TidakDiketahui => LebarIn <= 0 || TinggiIn <= 0;
    }

    internal static Lembar Baca(IntPtr hdc, double dpiCadanganX, double dpiCadanganY)
    {
        if (hdc == IntPtr.Zero) return default;

        double dpiX = Pozitif(GetDeviceCaps(hdc, LogPixelsX), dpiCadanganX);
        double dpiY = Pozitif(GetDeviceCaps(hdc, LogPixelsY), dpiCadanganY);

        double lebar = Nilai(GetDeviceCaps(hdc, PhysicalWidth), dpiX);
        double tinggi = Nilai(GetDeviceCaps(hdc, PhysicalHeight), dpiY);
        double kiri = Nilai(GetDeviceCaps(hdc, PhysicalOffsetX), dpiX);
        double atas = Nilai(GetDeviceCaps(hdc, PhysicalOffsetY), dpiY);

        return new Lembar(lebar, tinggi, kiri, atas, dpiX, dpiY);
    }

    private static double Nilai(int piksel, double dpi) =>
        dpi > 0 && piksel > 0 ? piksel / dpi : 0;

    private static double Pozitif(int nilai, double cadangan) =>
        nilai > 0 ? nilai : (cadangan > 0 ? cadangan : 300);
}
