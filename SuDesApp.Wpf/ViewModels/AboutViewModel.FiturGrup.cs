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
    /// <summary>Satu kelompok fitur pada bagian "Fitur utama".</summary>
    public class FiturGrup
    {
        public FiturGrup(string judul, string ikon, string keterangan, List<string> butir)
        {
            Judul = judul;
            Ikon = ikon;
            Keterangan = keterangan;
            Butir = butir;
        }

        public string Judul { get; }

        public string Ikon { get; }

        /// <summary>Kalimat pembuka kelompok — menjelaskan isi kelompoknya.</summary>
        public string Keterangan { get; }

        public List<string> Butir { get; }
    }

}
