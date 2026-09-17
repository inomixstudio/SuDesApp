using System;
using System.Windows;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Dialog input/edit data buku agenda Surat Masuk/Keluar.
    /// </summary>
    public partial class InputAgendaWindow : Window
    {
        private readonly InputAgendaViewModel _viewModel;

        public InputAgendaWindow(InputAgendaViewModel viewModel)
        {
            InitializeComponent();

            // Chrome bertema (tombol tutup title bar memakai AppChromeCommands.Close).
            ChromeWindowBehavior.Attach(this);

            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;
            _viewModel.RequestClose += () =>
            {
                DialogResult = true;
                Close();
            };
        }

        /// <summary>Dipanggil oleh factory DI setelah window dibuat.</summary>
        public void Initialize(string jenisSurat, SuratKeluarMasukData? editData)
        {
            _viewModel.Initialize(jenisSurat, editData);
        }

        /// <summary>Seret area judul untuk memindah jendela.</summary>
        private void CaptionBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* jendela sedang dimodali */ }
            }
        }
    }
}