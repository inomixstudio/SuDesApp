using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class RegisterNtcrView : UserControl
    {
        public RegisterNtcrView()
        {
            InitializeComponent();
        }

        public RegisterNtcrView(RegisterNtcrViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is SuratDisplayModel model)
            {
                if (DataContext is RegisterNtcrViewModel vm)
                {
                    vm.DoubleClickCommand.Execute(model);
                }
            }
        }

        private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm &&
                e.OriginalSource is DependencyObject source)
            {
                var row = FindAncestor<DataGridRow>(source);
                if (row?.Item is SuratDisplayModel model)
                {
                    vm.SelectedSurat = model;
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                if (current is T match)
                {
                    return match;
                }
            }
            return null;
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                vm.FilterChangeCommand.Execute(null);
            }
        }

        private void DateFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                vm.DateFilterToggleCommand.Execute(null);
            }
        }

        private void DateFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                vm.DateFilterToggleCommand.Execute(null);
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                vm.DateChangeCommand.Execute(null);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                vm.SearchTextChangedCommand.Execute(null);
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (DataContext is RegisterNtcrViewModel vm)
            {
                var ascending = vm.ApplySort(e.Column.SortMemberPath);
                e.Column.SortDirection = ascending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending;
                e.Handled = true;
            }
        }
    }
}