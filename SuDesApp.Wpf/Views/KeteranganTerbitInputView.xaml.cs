using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Form "Surat Keterangan Belum Terbit / Sudah Terbit" — lihat
    /// <see cref="KeteranganTerbitInputViewModel"/>.
    /// </summary>
    public partial class KeteranganTerbitInputView : UserControl
    {
        public KeteranganTerbitInputView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// NIK yang diketik dipakai untuk mengisi otomatis data diri warga
        /// (nama, TTL, agama, alamat) dari Data Warga — perilaku yang sama
        /// dengan form surat keterangan lain.
        /// </summary>
        private void OnNikLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is SuDesApp.Wpf.Input.BaseSuratInputViewModel vm)
            {
                _ = vm.OnNikLostFocusAsync();
            }
        }
    }
}
