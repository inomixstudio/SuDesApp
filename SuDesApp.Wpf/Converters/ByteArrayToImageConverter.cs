using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SuDesApp.Wpf.Converters
{
    /// <summary>
    /// Konversi byte gambar (PNG/JPEG dari foto profil Google) menjadi
    /// BitmapImage untuk binding Image.Source di XAML. Null/byte kosong
    /// menghasilkan null (UI menampilkan fallback inisial/ikon).
    /// </summary>
    public class ByteArrayToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not byte[] bytes || bytes.Length == 0)
                return null;

            try
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze(); // aman untuk akses lintas thread UI
                return image;
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
