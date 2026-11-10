using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class ExImdbViewModel : ObservableObject
    {

        // =====================================================================
        // Ekspor (kartu 1)
        // =====================================================================

        /// <summary>Lokasi berkas cadangan yang akan dibuat.</summary>
        public string ExportPath
        {
            get => _exportPath;
            set
            {
                if (SetProperty(ref _exportPath, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(KeteranganTujuanEkspor));
                    OnPropertyChanged(nameof(ExportNamaBerkas));
                    OnPropertyChanged(nameof(ExportFolder));
                }
            }
        }

        /// <summary>Nama berkas tujuan, dipakai petak ringkas di kartu Ekspor.</summary>
        public string ExportNamaBerkas
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0) return "Belum ditentukan";
                try
                {
                    var nama = Path.GetFileName(path);
                    return nama.Length == 0 ? "Belum ditentukan" : nama;
                }
                catch (ArgumentException)
                {
                    return "Nama berkas tidak valid";
                }
            }
        }

        /// <summary>Folder tujuan, dipakai petak ringkas di kartu Ekspor.</summary>
        public string ExportFolder
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0) return "-";
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    return string.IsNullOrEmpty(dir) ? "-" : dir!;
                }
                catch (ArgumentException)
                {
                    return "-";
                }
            }
        }

        public string KeteranganTujuanEkspor
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0)
                {
                    return "Belum ada tujuan. Tekan Pilih Lokasi untuk menentukan nama & folder berkas cadangan.";
                }

                var dir = Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(dir)
                    ? "Tujuan cadangan belum lengkap — pilih folder lewat tombol Pilih Lokasi."
                    : "Cadangan akan disimpan di folder: " + dir;
            }
        }

    }
}
