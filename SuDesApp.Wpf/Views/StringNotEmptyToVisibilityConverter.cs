using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SuDesApp.Wpf.Views
{
    /// <summary>String kosong/null → Collapsed; selain itu → Visible.</summary>
    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
