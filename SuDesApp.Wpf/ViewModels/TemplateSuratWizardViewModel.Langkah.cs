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
        // Langkah
        // =====================================================================

        public ObservableCollection<LangkahItemViewModel> LangkahDaftar { get; }

        public int Langkah
        {
            get => _langkah;
            private set
            {
                if (SetProperty(ref _langkah, value))
                {
                    OnPropertyChanged(nameof(TampilIdentitas));
                    OnPropertyChanged(nameof(TampilElemen));
                    OnPropertyChanged(nameof(TampilIsi));
                    OnPropertyChanged(nameof(TampilTombolLanjutLangkah));
                    OnPropertyChanged(nameof(TampilLanjutPratinjau));
                    OnPropertyChanged(nameof(JudulHalaman));
                    OnPropertyChanged(nameof(TampilTataLetak));
                    OnPropertyChanged(nameof(TampilPratinjau));
                    OnPropertyChanged(nameof(TampilEditor));
                    OnPropertyChanged(nameof(BisaKembali));
                    OnPropertyChanged(nameof(DiPratinjau));
                    OnPropertyChanged(nameof(TampilTombolLanjut));
                    OnPropertyChanged(nameof(JudulLangkah));
                    OnPropertyChanged(nameof(KeteranganLangkah));
                    OnPropertyChanged(nameof(NomorLangkahTeks));
                    OnPropertyChanged(nameof(TeksTombolLanjut));
                    PerbaruiPenandaLangkah();
                }
            }
        }

        /// <summary>Perbarui penanda langkah aktif/selesai pada daftar langkah.</summary>
        private void PerbaruiPenandaLangkah()
        {
            foreach (var langkah in LangkahDaftar)
            {
                langkah.Aktif = langkah.Nomor - 1 == Langkah;
                langkah.Selesai = langkah.Nomor - 1 < Langkah;
            }
        }


    }
}
