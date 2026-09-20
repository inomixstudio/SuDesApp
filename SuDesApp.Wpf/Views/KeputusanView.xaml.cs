using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class KeputusanView : UserControl
    {
        public KeputusanView()
        {
            InitializeComponent();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(SearchBox.Text);
            if (SearchWatermark != null)
            {
                SearchWatermark.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            }
            if (ClearSearchBtn != null)
            {
                ClearSearchBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
        }

        private void DataGridKeputusan_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.SelectedItem is KeputusanRow)
            {
                if (DataContext is KeputusanViewModel vm && vm.EditCommand.CanExecute(null))
                {
                    vm.EditCommand.Execute(null);
                }
            }
        }
    }
}