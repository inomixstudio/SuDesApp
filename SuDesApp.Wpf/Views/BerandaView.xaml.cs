using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Beranda. Ringkasan dimuat saat halaman pertama ditampilkan dan bisa
    /// dimuat ulang lewat tombol "Muat Ulang Ringkasan".
    /// </summary>
    public partial class BerandaView : UserControl
    {
        public BerandaView()
        {
            InitializeComponent();
            Loaded += BerandaView_Loaded;
        }

        private async void BerandaView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is BerandaViewModel vm)
            {
                await vm.MuatAsync();
            }
        }
    }
}
