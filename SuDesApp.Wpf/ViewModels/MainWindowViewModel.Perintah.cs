using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    // Modul perintah MainWindowViewModel; state bersama tetap di berkas utama.
    public partial class MainWindowViewModel
    {
        public AsyncRelayCommand ShowRegisterCommand { get; }
        public AsyncRelayCommand ShowSettingsCommand { get; }
        public AsyncRelayCommand ShowFormulirCommand { get; }
        public AsyncRelayCommand ShowAboutCommand { get; }
        public AsyncRelayCommand ShowCatatanRilisCommand { get; }

        /// <summary>Diminta saat pengguna memilih "Keluar" dari sidebar. MainWindow menutup window (satu konfirmasi).</summary>
        public event Action? ExitRequested;

        /// <summary>Klik chip status langganan di statusbar → halaman Langganan.</summary>
        public AsyncRelayCommand LanggananChipClickCommand { get; }

    }
}
