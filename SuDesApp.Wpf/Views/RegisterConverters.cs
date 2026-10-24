using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Konverter warna latar badge status surat (Draft / Active / Cancelled).
    /// Dipakai bersama oleh Register Surat dan Register NTCR.
    /// </summary>
    public class StatusToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningSubtleBrush") ?? new SolidColorBrush(Color.FromRgb(254, 243, 199)),
                "active" => Application.Current?.TryFindResource("SuccessSubtleBrush") ?? new SolidColorBrush(Color.FromRgb(220, 252, 231)),
                "cancelled" => Application.Current?.TryFindResource("ErrorSubtleBrush") ?? new SolidColorBrush(Color.FromRgb(254, 226, 226)),
                _ => Application.Current?.TryFindResource("PanelBrush") ?? new SolidColorBrush(Colors.LightGray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Konverter warna garis tepi badge status surat.</summary>
    public class StatusToBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningBrush") ?? new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                "active" => Application.Current?.TryFindResource("SuccessBrush") ?? new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                "cancelled" => Application.Current?.TryFindResource("ErrorBrush") ?? new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                _ => Application.Current?.TryFindResource("BorderBrush") ?? new SolidColorBrush(Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Konverter warna teks badge status surat.</summary>
    public class StatusToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningTextBrush") ?? new SolidColorBrush(Color.FromRgb(180, 83, 9)),
                "active" => Application.Current?.TryFindResource("SuccessTextBrush") ?? new SolidColorBrush(Color.FromRgb(21, 128, 61)),
                "cancelled" => Application.Current?.TryFindResource("ErrorTextBrush") ?? new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                _ => Application.Current?.TryFindResource("TextBrush") ?? new SolidColorBrush(Colors.Black)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Teks terisi -> Visible, teks kosong/null -> Collapsed. Dipakai untuk kotak
    /// pesan galat yang hanya boleh muncul kalau ada isinya.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Panah arah pengurutan pada kepala kolom tabel. Menerima tiga nilai:
    /// kolom yang sedang diurutkan, arah (true = naik), dan nama kolom
    /// kepala ini. Menghasilkan "▲", "▼", atau teks kosong bila kolom
    /// ini bukan kolom yang sedang mengurutkan. Dipakai halaman Data
    /// Perangkat Desa yang daftarnya berbentuk ListBox (bukan DataGrid).
    /// </summary>
    public class UrutArahConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 3) return string.Empty;
            if (values[0] is not string kolom || values[2] is not string kolomKepala) return string.Empty;
            if (!string.Equals(kolom, kolomKepala, StringComparison.Ordinal)) return string.Empty;
            return values[1] is false ? "▼" : "▲";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
