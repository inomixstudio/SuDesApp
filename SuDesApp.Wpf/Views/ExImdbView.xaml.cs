using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class ExImdbView
    {
        public ExImdbView()
        {
            InitializeComponent();
        }

        private void NavBagian_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border { Tag: AboutBagianViewModel bagian } &&
                DataContext is ExImdbViewModel vm)
            {
                vm.PilihBagian(bagian);
            }
        }
    }
}
