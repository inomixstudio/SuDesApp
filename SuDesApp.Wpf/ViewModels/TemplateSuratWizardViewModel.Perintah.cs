using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class TemplateSuratWizardViewModel : ObservableObject, IJudulHalaman
    {
        // =====================================================================
        // Perintah
        // =====================================================================

        public AsyncRelayCommand LanjutCommand { get; }
        public AsyncRelayCommand KePratinjauCommand { get; }
        public RelayCommand KembaliCommand { get; }
        public RelayCommand<LangkahItemViewModel> PilihLangkahCommand { get; }
        public AsyncRelayCommand SimpanCommand { get; }
        public AsyncRelayCommand SimpanDanIsiCommand { get; }
        public AsyncRelayCommand SegarkanPratinjauCommand { get; }
        public RelayCommand BatalCommand { get; }

        public RelayCommand TambahBlokCommand { get; }
        public RelayCommand TambahBlokUmumCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> HapusBlokCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> NaikBlokCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> TurunBlokCommand { get; }

        public RelayCommand TambahKolomCommand { get; }
        public RelayCommand TambahKolomUmumCommand { get; }
        public RelayCommand<KolomItemViewModel> HapusKolomCommand { get; }
        public RelayCommand<KolomItemViewModel> NaikKolomCommand { get; }
        public RelayCommand<KolomItemViewModel> TurunKolomCommand { get; }

        // ===== Perintah susunan berurutan (bagian) =====

        public RelayCommand TambahBagianTeksCommand { get; }
        public RelayCommand TambahBagianDataDiriCommand { get; }
        public RelayCommand TambahBagianDataDiriRingkasCommand { get; }
        public RelayCommand TambahBagianKolomCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> HapusBagianCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> NaikBagianCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> TurunBagianCommand { get; }

        /// <summary>Tambah satu kolom di dalam bagian kolom isian atau data diri.</summary>
        public RelayCommand<BagianWizardItemViewModel> TambahKolomBagianCommand { get; }

        /// <summary>Tambah kolom identitas umum (NIK, nama, alamat, dan lainnya) ke bagian kolom isian atau data diri.</summary>
        public RelayCommand<BagianWizardItemViewModel> TambahKolomUmumBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> HapusKolomBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> NaikKolomBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> TurunKolomBagianCommand { get; }


    }
}
