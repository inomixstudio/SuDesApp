using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views.About
{
    /// <summary>
    /// Bagian "Perusahaan" pada halaman Tentang Aplikasi: bagan naungan
    /// (PT Duta Publik → Sumberjaya Dev → SuDesApp) serta kartu kedua perusahaan
    /// beserta logonya. Isinya seluruhnya dari AboutViewModel; kode di sini hanya
    /// membuka tautan yang diklik.
    /// </summary>
    public partial class BagianPerusahaanView : UserControl
    {
        public BagianPerusahaanView()
        {
            InitializeComponent();
        }

        private void Tautan_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: string url } &&
                DataContext is AboutViewModel vm)
            {
                vm.OpenUrlCommand.Execute(url);
            }
        }
    }
}
