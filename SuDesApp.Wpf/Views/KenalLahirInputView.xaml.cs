using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class KenalLahirInputView : UserControl
    {
        public KenalLahirInputView()
        {
            InitializeComponent();
        }

        private void OnNikAyahLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is KenalLahirInputViewModel vm)
            {
                _ = vm.OnAyahNikLostFocusAsync();
            }
        }

        private void OnNikIbuLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is KenalLahirInputViewModel vm)
            {
                _ = vm.OnIbuNikLostFocusAsync();
            }
        }
    }
}