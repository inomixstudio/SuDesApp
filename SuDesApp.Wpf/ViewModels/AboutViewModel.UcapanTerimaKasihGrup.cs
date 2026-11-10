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
    /// <summary>Satu kelompok ucapan terima kasih pada bagian "Thanks to".</summary>
    public class UcapanTerimaKasihGrup
    {
        public UcapanTerimaKasihGrup(string ikon, string judul, List<string> butir)
        {
            Ikon = ikon;
            Judul = judul;
            Butir = butir;
        }

        public string Ikon { get; }
        public string Judul { get; }
        public List<string> Butir { get; }
    }

}
