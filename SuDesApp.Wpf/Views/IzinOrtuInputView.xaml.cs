using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class IzinOrtuInputView : UserControl
    {
        public IzinOrtuInputView()
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

        private void OnNikAnakLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is IzinOrtuInputViewModel vm)
            {
                _ = vm.OnNikAnakLostFocusAsync();
            }
        }
    }
}