using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class KematianInputView : UserControl
    {
        public KematianInputView()
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

        private void OnNikPelaporLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is KematianInputViewModel vm)
            {
                _ = vm.OnNikPelaporLostFocusAsync();
            }
        }
    }
}