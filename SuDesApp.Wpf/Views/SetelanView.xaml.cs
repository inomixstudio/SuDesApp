using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class SetelanView : UserControl
    {
        public SetelanView(SetelanViewModel? viewModel = null)
        {
            InitializeComponent();
            // Saat dinavigasi via DataTemplate (App.xaml), DataContext diwarisi
            // dari ContentPresenter (instance SetelanViewModel) — jangan ditimpa null.
            if (viewModel != null)
            {
                DataContext = viewModel;
            }
        }
    }
}