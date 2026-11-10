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
        // Keadaan halaman
        // =====================================================================

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool IsExporting
        {
            get => _isExporting;
            private set { if (SetProperty(ref _isExporting, value)) RaiseCommandStates(); }
        }

        public bool IsImporting
        {
            get => _isImporting;
            private set { if (SetProperty(ref _isImporting, value)) RaiseCommandStates(); }
        }

        /// <summary>Benar bila kartu konfirmasi impor sedang menunggu jawaban pengguna.</summary>
        public bool AdaKonfirmasiImpor
        {
            get => _adaKonfirmasiImpor;
            private set
            {
                if (SetProperty(ref _adaKonfirmasiImpor, value))
                {
                    OnPropertyChanged(nameof(TampilTombolImpor));
                }
            }
        }

        /// <summary>Tombol "Pulihkan Database" hanya tampil bila kartu konfirmasi belum terbuka.</summary>
        public bool TampilTombolImpor => !_adaKonfirmasiImpor && !_isImporting;

        public bool AdaKesibukan => IsExporting || IsImporting;

    }
}
