using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class TemplateSuratViewModel : ObservableObject
    {
        // ===== Perintah =====

        public RelayCommand BuatBaruCommand { get; }
        public AsyncRelayCommand EditCommand { get; }
        public RelayCommand DuplikatCommand { get; }
        public AsyncRelayCommand HapusCommand { get; }
        public AsyncRelayCommand IsiCommand { get; }
        public AsyncRelayCommand PratinjauCommand { get; }
        public AsyncRelayCommand SegarkanCommand { get; }

        /// <summary>Susun template baru dari berkas Word (.docx) yang sudah dipakai.</summary>
        public AsyncRelayCommand BuatDariWordCommand { get; }

        public RelayCommand BersihkanPencarianCommand { get; }

        /// <summary>Pilih sumber daftar dari tombol pil di bilah alat.</summary>
        public RelayCommand<string> PilihFilterCommand { get; }

        /// <summary>Keterangan singkat setelah contoh bawaan disiapkan otomatis.</summary>
        public string InfoBawaan
        {
            get => _infoBawaan;
            private set
            {
                if (SetProperty(ref _infoBawaan, value))
                {
                    OnPropertyChanged(nameof(AdaInfoBawaan));
                }
            }
        }

        public bool AdaInfoBawaan => !string.IsNullOrWhiteSpace(_infoBawaan);


    }
}
