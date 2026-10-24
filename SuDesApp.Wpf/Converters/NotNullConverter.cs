using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SuDesApp.Wpf.Converters
{
    /// <summary>
    /// Mengembalikan true bila nilai bukan null / DependencyProperty.UnsetValue.
    /// Dipakai gaya TextBlock untuk mendeteksi apakah elemen berada di dalam
    /// sebuah Button (lewat RelativeSource FindAncestor); bila ya, warna teks
    /// mengikuti Foreground tombol sehingga tulisan pada tombol berwarna selalu
    /// kontras (mis. putih di atas tombol hijau tema hijau).
    /// </summary>
    public class NotNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && value != DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
