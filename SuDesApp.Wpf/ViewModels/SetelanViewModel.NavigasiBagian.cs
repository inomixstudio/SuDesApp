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
        // Navigasi bagian halaman
        // =================================================================

        /// <summary>Daftar bagian yang ditampilkan pada navigasi kiri.</summary>
        public ObservableCollection<BagianSetelanItemViewModel> Bagian { get; }

        public RelayCommand<BagianSetelanItemViewModel> PilihBagianCommand { get; }

        /// <summary>Nomor bagian yang sedang dibuka (0 = Data Desa).</summary>
        public int BagianAktif => Bagian.Count == 0
            ? 0
            : System.Math.Max(0, Bagian.ToList().FindIndex(b => b.Aktif));

        public bool TampilDesa => Bagian.Count == 0 || Bagian[0].Aktif;
        public bool TampilPejabatDesa => Bagian.Count > 1 && Bagian[1].Aktif;
        public bool TampilPejabatKecamatan => Bagian.Count > 2 && Bagian[2].Aktif;
        public bool TampilCetak => Bagian.Count > 3 && Bagian[3].Aktif;

        private void PilihBagian(BagianSetelanItemViewModel? bagian)
        {
            if (bagian == null) return;
            TampilkanBagian(bagian.Nomor - 1);
        }

        /// <summary>Buka satu bagian dan perbarui penanda bagian aktif.</summary>
        public void TampilkanBagian(int nomor)
        {
            if (Bagian.Count == 0) return;

            int tujuan = System.Math.Clamp(nomor, 0, Bagian.Count - 1);
            for (int i = 0; i < Bagian.Count; i++)
            {
                Bagian[i].Aktif = i == tujuan;
            }

            OnPropertyChanged(nameof(BagianAktif));
            OnPropertyChanged(nameof(TampilDesa));
            OnPropertyChanged(nameof(TampilPejabatDesa));
            OnPropertyChanged(nameof(TampilPejabatKecamatan));
            OnPropertyChanged(nameof(TampilCetak));
        }


    }
}
