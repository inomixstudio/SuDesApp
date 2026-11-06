using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace SuDesApp.Wpf.Utilities;

/// <summary>
/// Pembuat gambar QR untuk WPF. Satu-satunya tempat yang mengubah keluaran
/// GDI+ QRCoder (<c>System.Drawing.Bitmap</c>) menjadi <see cref="BitmapSource"/>,
/// supaya ViewModel tidak perlu mengenal detail konversi handle.
/// </summary>
public static class QrGambar
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Ubah teks menjadi gambar QR. Mengembalikan null bila teks kosong atau
    /// QRCoder gagal — pemanggil cukup menyembunyikan kontrolnya.
    /// </summary>
    public static BitmapSource? Buat(string teks, int pikselPerModul = 20)
    {
        if (string.IsNullOrWhiteSpace(teks)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(teks, QRCodeGenerator.ECCLevel.Q);
            using var qr = new QRCode(data);
            using var bitmap = qr.GetGraphic(pikselPerModul);

            var handle = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        catch
        {
            return null;
        }
    }
}
