using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class SktmInputView : UserControl
    {
        public SktmInputView()
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
    }
}
