using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class LanggananViewModel : ObservableObject
    {
        // =======================================================================
        // Statusbar halaman.
        // =======================================================================

        /// <summary>Pesan tetap saat tidak ada aksi yang baru dikerjakan.</summary>
        private const string StatusSiap = "Status lisensi dapat diperiksa ulang kapan saja lewat tombol Cek Status.";

        private string _statusMessage = StatusSiap;
        private readonly StatusSesaat _statusSesaat;

        /// <summary>Pesan pada statusbar halaman (berubah sesaat setelah aksi).</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                JalankanDiUi(() => SetProperty(ref _statusMessage, value));
            }
        }

    }
}
