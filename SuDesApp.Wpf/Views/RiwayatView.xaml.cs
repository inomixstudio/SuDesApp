using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class RiwayatView : UserControl
    {
        public RiwayatView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is RiwayatViewModel vm)
                {
                    _ = vm.LoadAsync();
                }
            };
        }
    }
}
