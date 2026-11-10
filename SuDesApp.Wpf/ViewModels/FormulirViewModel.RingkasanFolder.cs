using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class FormulirViewModel : ObservableObject, IDisposable
    {
        // =====================================================================
        // Ringkasan folder & sumber (kartu informasi halaman)
        // =====================================================================

        public string FolderTemplates => _appConfig.TemplateFolder;

        public string NamaFolderTemplates
        {
            get
            {
                var bersih = _appConfig.TemplateFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var nama = Path.GetFileName(bersih);
                return string.IsNullOrWhiteSpace(nama) ? "Templates" : nama;
            }
        }

        /// <summary>Jumlah formulir yang tersimpan beserta ukuran totalnya, untuk kartu ringkasan.</summary>
        public string RingkasanJumlah => Templates.Count == 0
            ? "Belum ada berkas"
            : $"{Templates.Count} formulir  •  {FormulirTemplate.FormatUkuran(Templates.Sum(t => t.UkuranByte))}";

        /// <summary>Alamat halaman sumber formulir resmi yang dipakai tombol Unduh dari Sumber.</summary>
        public string SumberFormulirTeks => SourceUrl.Host;


    }
}
