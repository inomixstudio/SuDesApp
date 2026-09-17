using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class KeputusanView : UserControl
    {
        public KeputusanView()
        {
            InitializeComponent();
        }

        private void DataGridKeputusan_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.SelectedItem is KeputusanRow selectedRow)
            {
                if (DataContext is KeputusanViewModel vm && vm.OpenWordFileCommand.CanExecute(selectedRow))
                {
                    vm.OpenWordFileCommand.Execute(selectedRow);
                }
            }
        }
    }
}