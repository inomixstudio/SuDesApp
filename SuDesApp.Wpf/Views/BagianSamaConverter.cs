using System.Globalization;
using System.Windows.Data;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Pembanding penanda bagian pada tombol pemilih bagian halaman Perangkat
    /// Desa: nilai pertama = penanda tombol (<c>BagianAktif.Bagian</c>), nilai
    /// kedua = bagian yang sedang tampil (<c>BagianAktif</c>). Keduanya nama
    /// anggota enum <c>BagianPerangkat</c>, jadi cukup dibandingkan sebagai teks.
    /// </summary>
    public class BagianSamaConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not string penanda || values[1] is not string aktif)
            {
                return false;
            }

            return string.Equals(penanda, aktif, StringComparison.Ordinal);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
