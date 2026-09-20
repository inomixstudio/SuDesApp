using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Membalik bool menjadi Visibility: true → Collapsed, false → Visible.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool tampil = value is bool b && b;
            return tampil ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Collapsed;
        }
    }
}