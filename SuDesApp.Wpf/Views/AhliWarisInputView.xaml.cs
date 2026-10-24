using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.Input;

namespace SuDesApp.Wpf.Views
{
    public partial class AhliWarisInputView : UserControl
    {
        private INotifyPropertyChanged? _observedVm;

        public AhliWarisInputView()
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
            if (!string.Equals(e.PropertyName, nameof(AhliWarisInputViewModel.SelectedAnak), StringComparison.Ordinal))
                return;

            // Pastikan baris yang baru ditambahkan/dipilih selalu terlihat di viewport DataGrid
            // dan sel Nama langsung masuk mode edit (konsisten dengan pola grid Garapan).
            var grid = AnakGrid;
            var vm = DataContext as AhliWarisInputViewModel;
            if (grid?.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated &&
                vm?.SelectedAnak != null)
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(vm.SelectedAnak) is DataGridRow row)
                {
                    row.IsSelected = true;
                    grid.ScrollIntoView(vm.SelectedAnak);
                    grid.UpdateLayout();
                    grid.CurrentCell = new DataGridCellInfo(vm.SelectedAnak, grid.Columns[0]);
                    grid.BeginEdit();
                }
                else
                {
                    grid.ScrollIntoView(vm.SelectedAnak);
                    grid.UpdateLayout();
                    if (grid.ItemContainerGenerator.ContainerFromItem(vm.SelectedAnak) is DataGridRow row2)
                    {
                        row2.IsSelected = true;
                    }
                }
            }
        }
    }
}
