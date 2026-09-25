using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views.About
{
    public partial class BagianTeknologiView : UserControl
    {
        public BagianTeknologiView()
        {
            InitializeComponent();
        }

        private void ChipTeknologi_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: TeknologiItem item } &&
                DataContext is AboutViewModel vm)
            {
                vm.OpenUrlCommand.Execute(item.Url);
            }
        }
    }
}
