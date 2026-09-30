using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Data Perangkat Desa. Tidak ada logika bisnis di sini: seluruh
    /// query, validasi, dan pemuatan data milik
    /// <see cref="PerangkatDesaViewModel"/>. Code-behind hanya menerjemahkan
    /// klik ganda pada tabel menjadi perintah ubah.
    /// </summary>
    public partial class PerangkatDesaView : UserControl
    {
        public PerangkatDesaView()
        {
            InitializeComponent();
        }

        private PerangkatDesaViewModel? ViewModel => DataContext as PerangkatDesaViewModel;

        /// <summary>Klik ganda pada baris = buka form ubah, sama seperti klik tombol Ubah.</summary>
        private void TabelPerangkat_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is null) return;

            // Klik ganda pada tombol di dalam sel tidak boleh ikut membuka form.
            if (e.OriginalSource is DependencyObject sumber && CariTombol(sumber) is not null) return;

            if (sender is not DataGrid tabel) return;
            if (tabel.SelectedItem is not PerangkatDesa baris) return;

            if (ViewModel.UbahCommand.CanExecute(baris)) ViewModel.UbahCommand.Execute(baris);
        }

        private static Button? CariTombol(DependencyObject? mulai)
        {
            while (mulai is not null)
            {
                if (mulai is Button tombol) return tombol;

                DependencyObject? induk = VisualTreeHelper.GetParent(mulai);
                if (induk is null || induk is System.Windows.Media.Media3D.Visual3D) break;
                mulai = induk;
            }

            return null;
        }
    }
}
