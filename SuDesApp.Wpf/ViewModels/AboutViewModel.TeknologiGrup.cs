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
    /// <summary>Satu kelompok chip teknologi pada bagian "Teknologi".</summary>
    public class TeknologiGrup
    {
        public TeknologiGrup(string judul, List<TeknologiItem> item)
        {
            Judul = judul;
            Item = item;
        }

        public string Judul { get; }

        public List<TeknologiItem> Item { get; }
    }

}
