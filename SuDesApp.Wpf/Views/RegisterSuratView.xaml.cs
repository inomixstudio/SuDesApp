using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class RegisterSuratView : UserControl
    {
        public RegisterSuratView()
        {
            InitializeComponent();
        }

        public RegisterSuratView(RegisterSuratViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is SuratDisplayModel model)
            {
                if (DataContext is RegisterSuratViewModel vm)
                {
                    vm.DoubleClickCommand.Execute(model);
                }
            }
        }

        private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm &&
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
            if (DataContext is RegisterSuratViewModel vm)
            {
                vm.FilterChangeCommand.Execute(null);
            }
        }

        private void DateFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm)
            {
                vm.DateFilterToggleCommand.Execute(null);
            }
        }

        private void DateFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm)
            {
                vm.DateFilterToggleCommand.Execute(null);
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm)
            {
                vm.DateChangeCommand.Execute(null);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm)
            {
                vm.SearchTextChangedCommand.Execute(null);
            }
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (DataContext is RegisterSuratViewModel vm)
            {
                var ascending = vm.ApplySort(e.Column.SortMemberPath);
                e.Column.SortDirection = ascending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending;
                e.Handled = true;
            }
        }
    }

    public class StatusToBackgroundConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningSubtleBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199)),
                "active" => Application.Current?.TryFindResource("SuccessSubtleBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231)),
                "cancelled" => Application.Current?.TryFindResource("ErrorSubtleBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226)),
                _ => Application.Current?.TryFindResource("PanelBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusToBorderConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),
                "active" => Application.Current?.TryFindResource("SuccessBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74)),
                "cancelled" => Application.Current?.TryFindResource("ErrorBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                _ => Application.Current?.TryFindResource("BorderBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusToForegroundConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var status = value as string;
            return status?.ToLowerInvariant() switch
            {
                "draft" => Application.Current?.TryFindResource("WarningTextBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9)),
                "active" => Application.Current?.TryFindResource("SuccessTextBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61)),
                "cancelled" => Application.Current?.TryFindResource("ErrorTextBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28)),
                _ => Application.Current?.TryFindResource("TextBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Proxy Freezable agar binding di dalam DataGrid (yang DataContext-nya per baris)
    /// dapat mengakses properti level ViewModel — dipakai untuk visibilitas kolom
    /// "Calon Mempelai" yang hanya tampil di Register NTCR.
    /// </summary>
    public class BindingProxy : System.Windows.Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public static readonly DependencyProperty DataContextProperty =
            System.Windows.DependencyProperty.Register(
                nameof(DataContext), typeof(object), typeof(BindingProxy),
                new System.Windows.PropertyMetadata(null));

        public object? DataContext
        {
            get => GetValue(DataContextProperty);
            set => SetValue(DataContextProperty, value);
        }
    }
}