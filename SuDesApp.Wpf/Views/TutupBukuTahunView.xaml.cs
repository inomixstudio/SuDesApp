using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman tutup buku tahunan. DataContext dipasang lewat DataTemplate di
    /// App.xaml, jadi view ini tidak perlu mencari ViewModel.
    /// </summary>
    public partial class TutupBukuTahunView : UserControl
    {
        public TutupBukuTahunView()
        {
            InitializeComponent();
        }
    }
}
