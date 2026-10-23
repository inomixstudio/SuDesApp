using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class BedaNamaInputView : UserControl
    {
        public BedaNamaInputView()
        {
            InitializeComponent();
        }

        private void OnNikLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is BaseSuratInputViewModel vm)
            {
                _ = vm.OnNikLostFocusAsync();
            }
        }

        private void OnNik2LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is BedaNamaInputViewModel vm)
            {
                _ = vm.OnNik2LostFocusAsync();
            }
        }
    }
}