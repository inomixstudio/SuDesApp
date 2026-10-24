using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman input/edit data buku SK / Peraturan (SK, Perdes, Perkades) yang
    /// ditanam di area konten menu utama (bukan jendela terpisah). DataContext
    /// (InputKeputusanViewModel) datang dari navigasi; penutupan halaman diatur
    /// oleh ViewModel lewat event RequestClose.
    /// </summary>
    public partial class InputKeputusanView : UserControl
    {
        public InputKeputusanView()
        {
            InitializeComponent();
        }
    }
}
