using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    public partial class KeramaianInputView : UserControl
    {
        public KeramaianInputView()
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
    }
}
