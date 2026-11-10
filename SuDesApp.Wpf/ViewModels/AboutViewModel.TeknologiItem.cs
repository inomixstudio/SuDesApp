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
    /// <summary>Satu item chip teknologi pada bagian "Teknologi".</summary>
    public class TeknologiItem
    {
        public TeknologiItem(string ikon, string nama, string keterangan, string url, string grup = "")
        {
            Ikon = ikon;
            Nama = nama;
            Keterangan = keterangan;
            Url = url;
            Grup = grup;
        }

        public string Ikon { get; }
        public string Nama { get; }
        public string Keterangan { get; }
        public string Url { get; }

        /// <summary>Nama kelompok (mis. "Data &amp; dokumen") — judul chip saat ditampilkan.</summary>
        public string Grup { get; }
    }

}
