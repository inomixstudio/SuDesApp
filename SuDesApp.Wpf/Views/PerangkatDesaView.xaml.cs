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
    /// query, validasi, ekspor, dan pemuatan data milik
    /// <see cref="PerangkatDesaViewModel"/>. Code-behind hanya menerjemahkan
    /// klik ganda pada baris tabel menjadi perintah buka form (isi/tambah data
    /// diri). Halaman ini tidak lagi memakai kotak pencarian.
    /// </summary>
    public partial class PerangkatDesaView : UserControl
    {
        public PerangkatDesaView()
        {
            InitializeComponent();
        }

        private PerangkatDesaViewModel? ViewModel => DataContext as PerangkatDesaViewModel;

        private void PilihWarga_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: WargaData warga }) ViewModel?.PilihWarga(warga);
        }

        /// <summary>Klik ganda pada baris = buka form data diri (mode ubah, atau mode
        /// tambah dengan jabatan baris terpilih), sama seperti klik tombol Ubah.</summary>
        private void TabelPerangkat_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is null) return;

            // Klik ganda pada tombol di dalam baris tidak boleh ikut membuka form.
            if (e.OriginalSource is DependencyObject sumber && CariTombol(sumber) is not null) return;

            if (sender is not ListBox tabel) return;
            if (tabel.SelectedItem is not BarisPerangkatViewModel baris) return;

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
