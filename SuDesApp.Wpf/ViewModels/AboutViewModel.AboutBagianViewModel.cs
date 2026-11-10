using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu entri navigasi bagian pada halaman Tentang Aplikasi.</summary>
    public class AboutBagianViewModel : ObservableObject
    {
        private bool _isTerpilih;

        public AboutBagianViewModel(string kunci, string ikon, string label, string deskripsiStatus)
        {
            Kunci = kunci;
            Ikon = ikon;
            Label = label;
            DeskripsiStatus = deskripsiStatus;
        }

        /// <summary>Kunci bagian — menentukan template konten mana yang dirender.</summary>
        public string Kunci { get; }

        public string Ikon { get; }

        public string Label { get; }

        /// <summary>Keterangan singkat bagian — tampil di statusbar halaman.</summary>
        public string DeskripsiStatus { get; }

        public bool IsTerpilih
        {
            get => _isTerpilih;
            set => SetProperty(ref _isTerpilih, value);
        }
    }

}
