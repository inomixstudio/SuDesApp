using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Panduan Awal. Seluruh perilakunya ada di view model
    /// (<see cref="ViewModels.PanduanAwalViewModel"/>): tampilan ini murni XAML
    /// supaya binding-nya ikut diperiksa uji XAML bawaan repo.
    /// </summary>
    public partial class PanduanAwalView : UserControl
    {
        public PanduanAwalView()
        {
            InitializeComponent();
        }
    }
}
