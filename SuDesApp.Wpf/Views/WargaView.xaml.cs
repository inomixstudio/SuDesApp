using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Data Warga. Tidak ada logika bisnis di sini: seluruh query,
    /// validasi, dan pemuatan data milik <see cref="WargaViewModel"/>. Code-behind
    /// ini hanya menerjemahkan klik sel tabel menjadi perintah ViewModel.
    /// </summary>
    public partial class WargaView : UserControl
    {
        public WargaView()
        {
            InitializeComponent();
        }

        private WargaViewModel? ViewModel => DataContext as WargaViewModel;

        /// <summary>Klik ganda pada baris = buka form ubah, sama seperti klik tombol Ubah.</summary>
        private void TabelWarga_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel is not { IsBusy: false } vm) return;
            if (e.OriginalSource is not DependencyObject asal) return;
            if (asal is TextBlock || asal is Run) return;

            if (TabelWarga.SelectedItem is WargaBaris baris)
            {
                if (vm.UbahCommand.CanExecute(baris))
                    vm.UbahCommand.Execute(baris);
            }
        }

        /// <summary>
        /// Terapkan status yang dipilih di dropdown baris tersebut. Status dikirim
        /// sebagai <see cref="WargaViewModel.UbahStatusRequest"/> supaya
        /// ViewModel tetap satu-satunya tempat meminta konfirmasi dan menulis
        /// ke database.
        /// </summary>
        private void TerapkanStatus_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not { IsBusy: false } vm) return;
            if (((FrameworkElement)sender).DataContext is not WargaBaris baris) return;

            if (!StatusWargaTipe.Valid(baris.StatusSapuan))
            {
                vm.CatatStatus("Pilih status warga yang valid terlebih dahulu.");
                return;
            }

            if (string.Equals(baris.StatusSapuan, baris.StatusTampil, StringComparison.OrdinalIgnoreCase))
            {
                vm.CatatStatus($"Status warga \"{baris.Nama}\" tidak diubah.");
                return;
            }

            var request = new WargaViewModel.UbahStatusRequest
            {
                IdWarga = baris.ID_Warga,
                Nama = baris.Nama,
                StatusBaru = baris.StatusSapuan
            };

            if (vm.UbahStatusCommand.CanExecute(request))
                vm.UbahStatusCommand.Execute(request);
        }
    }
}
