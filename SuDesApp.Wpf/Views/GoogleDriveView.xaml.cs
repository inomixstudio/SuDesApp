using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class GoogleDriveView : UserControl
    {
        public GoogleDriveView()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is GoogleDriveViewModel vm && vm.SelectedRow is { } baris)
            {
                PenangananGalatUi.Jalankan(() => vm.OpenItemAsync(baris), "Buka item Drive");
            }
        }

        private void DataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not GoogleDriveViewModel vm || vm.SelectedRow is not { } baris) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                PenangananGalatUi.Jalankan(() => vm.OpenItemAsync(baris), "Buka item Drive");
            }
            else if (e.Key == Key.Delete)
            {
                e.Handled = true;
                if (vm.DeleteCommand is AsyncRelayCommand delete && delete.CanExecute(null))
                {
                    delete.Execute(null);
                }
            }
        }
    }
}