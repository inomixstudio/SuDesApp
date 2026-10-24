using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    public partial class NtcrInputView : UserControl
    {
        public NtcrInputView()
        {
            InitializeComponent();
        }

        private void OnNikLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is Input.BaseSuratInputViewModel vm)
            {
                _ = vm.OnNikLostFocusAsync();
            }
        }

        private void OnNikIstriLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is Input.NtcrInputViewModel vm)
            {
                _ = vm.OnNikIstriLostFocusAsync();
            }
        }
    }
}