using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class PanduanWaView : UserControl
    {
        public PanduanWaView()
        {
            InitializeComponent();
        }

        private void NavBagian_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: PanduanWaBagianViewModel bagian } &&
                DataContext is PanduanWaViewModel vm)
            {
                vm.PilihBagian(bagian);
            }
        }
    }
}
