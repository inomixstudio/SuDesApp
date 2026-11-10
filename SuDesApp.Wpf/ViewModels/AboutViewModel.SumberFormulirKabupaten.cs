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

    /// <summary>
    /// Satu tautan sumber formulir kabupaten lain pada bagian "Thanks to".
    /// Formulir adminduk berlaku nasional (Permendagri 109/2019), jadi tautan
    /// ini hanya memudahkan desa yang berbatasan dengan kabupaten lain.
    /// </summary>
    public class SumberFormulirKabupaten
    {
        public SumberFormulirKabupaten(string nama, string tautanTampil, string url)
        {
            Nama = nama;
            TautanTampil = tautanTampil;
            Url = url;
        }

        /// <summary>Nama daerah, mis. "Kabupaten Bandung".</summary>
        public string Nama { get; }

        /// <summary>Teks alamat siap tampil (tanpa skema).</summary>
        public string TautanTampil { get; }

        /// <summary>Alamat lengkap yang dibuka saat kartu diklik.</summary>
        public string Url { get; }
    }

}
