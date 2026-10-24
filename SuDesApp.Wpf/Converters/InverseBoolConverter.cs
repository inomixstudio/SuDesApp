using System;
using System.Globalization;
using System.Windows.Data;

namespace SuDesApp.Wpf.Converters
{
    /// <summary>
    /// Membalik nilai bool: true menjadi false dan sebaliknya.
    /// Dipakai untuk sepasang RadioButton yang isinya saling melengkapi.
    /// </summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool b && b);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool b && b);
        }
    }
}
