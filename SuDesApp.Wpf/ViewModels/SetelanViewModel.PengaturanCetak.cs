using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class SetelanViewModel : ObservableObject
    {
        // =================================================================
        // Pengaturan cetak (ukuran kertas & logo kop). Disimpan langsung ke
        // preferensi aplikasi, jadi tidak ikut tombol "Simpan" data desa.
        // =================================================================

        private UkuranKertasSurat _ukuranKertas = UkuranKertasSurat.A4;
        private string _jalurLogo = string.Empty;

        /// <summary>Ukuran kertas A4 (bawaan aplikasi).</summary>
        public bool UkuranKertasA4
        {
            get => _ukuranKertas == UkuranKertasSurat.A4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.A4); }
        }

        /// <summary>Ukuran kertas F4/Folio (8,5 x 13 inci).</summary>
        public bool UkuranKertasF4
        {
            get => _ukuranKertas == UkuranKertasSurat.F4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.F4); }
        }

        /// <summary>Jalur gambar logo kop yang sedang dipakai (untuk pratinjau).</summary>
        public string JalurLogo
        {
            get => _jalurLogo;
            private set
            {
                if (SetProperty(ref _jalurLogo, value))
                {
                    OnPropertyChanged(nameof(AdaLogo));
                    OnPropertyChanged(nameof(KeteranganLogo));
                }
            }
        }

        public bool AdaLogo => !string.IsNullOrWhiteSpace(_jalurLogo);

        /// <summary>Keterangan asal logo: bawaan aplikasi atau pilihan pengguna.</summary>
        public string KeteranganLogo
        {
            get
            {
                if (!AdaLogo)
                {
                    return "Belum ada gambar logo — kop surat akan dicetak tanpa logo.";
                }

                return !string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo())
                    ? $"Logo pilihan Anda: {System.IO.Path.GetFileName(_jalurLogo)}"
                    : "Logo bawaan aplikasi.";
            }
        }


    }
}
