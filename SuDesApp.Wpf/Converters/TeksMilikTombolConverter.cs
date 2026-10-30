using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SuDesApp.Wpf.Converters
{
    /// <summary>
    /// Menentukan apakah teks benar-benar bagian ISI sebuah tombol, atau hanya
    /// kebetulan berada di pohon logis tombol karena tooltip.
    ///
    /// WPF menaruh isi ToolTip di pohon logis pemiliknya, sehingga aturan
    /// "teks di dalam Button mengikuti Foreground tombol" ikut mewarnai teks
    /// tooltip dengan warna tombol — pada tombol berteks putih hasilnya putih
    /// di atas permukaan tooltip yang juga terang dan tidak terbaca.
    ///
    /// Penelusuran berhenti di ToolTip pertama: tooltip selalu punya permukaan
    /// sendiri, jadi teksnya harus memakai warna tema, bukan warna tombol.
    /// </summary>
    public sealed class TeksMilikTombolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 2) return false;

            // values[0] = leluhur Button (null bila tidak ada), values[1] = teksnya.
            if (values[0] is not Button) return false;
            if (values[1] is not DependencyObject asal) return false;

            for (DependencyObject d = asal; d is not null; d = IndukBerikut(d))
            {
                if (d is ToolTip) return false;
                if (d is Button) return true;
            }

            return false;
        }

        private static DependencyObject? IndukBerikut(DependencyObject d)
        {
            if (d is FrameworkContentElement konten) return konten.Parent;

            DependencyObject? induk = null;
            try
            {
                induk = LogicalTreeHelper.GetParent(d);
            }
            catch (InvalidOperationException)
            {
                // Sebagian elemen hasil templat tidak punya induk logis.
            }

            if (induk is not null) return induk;

            return d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
