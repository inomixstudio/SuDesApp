using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class GarapanInputView : UserControl
    {
        private INotifyPropertyChanged? _observedVm;

        public GarapanInputView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) => DetachVm();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachVm();
            if (e.NewValue is INotifyPropertyChanged vm)
            {
                _observedVm = vm;
                vm.PropertyChanged += OnVmPropertyChanged;
            }
        }

        private void DetachVm()
        {
            if (_observedVm != null)
            {
                _observedVm.PropertyChanged -= OnVmPropertyChanged;
                _observedVm = null;
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(GarapanInputViewModel.SelectedRincian), StringComparison.Ordinal))
                return;

            // Pastikan baris yang baru ditambahkan/dipilih selalu terlihat di viewport DataGrid.
            var grid = RincianGrid;
            var vm = DataContext as GarapanInputViewModel;
            if (grid?.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated &&
                vm?.SelectedRincian != null)
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(vm.SelectedRincian) is DataGridRow row)
                {
                    row.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                    grid.ScrollIntoView(vm.SelectedRincian);
                }
                else
                {
                    grid.ScrollIntoView(vm.SelectedRincian);
                    grid.UpdateLayout();
                    if (grid.ItemContainerGenerator.ContainerFromItem(vm.SelectedRincian) is DataGridRow row2)
                    {
                        row2.IsSelected = true;
                    }
                }
            }
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
