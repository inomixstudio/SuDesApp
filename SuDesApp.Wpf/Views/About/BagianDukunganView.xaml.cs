using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views.About
{
    public partial class BagianDukunganView : UserControl
    {
        public BagianDukunganView()
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
