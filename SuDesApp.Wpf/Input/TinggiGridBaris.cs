using System;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Tinggi satu DataGrid pada form input yang bisa diatur operator lewat tombol
    /// besar/kecil — tanpa mengedit kode. Nilai terakhir tersimpan di preferensi
    /// aplikasi (per kunci) supaya pilihan diingat antar pemakaian.
    /// </summary>
    public sealed class TinggiGridBaris : ObservableObject
    {
        /// <summary>Tinggi bawaan bila operator belum pernah mengatur.</summary>
        public const double Bawaan = 280;
        public const double Minimal = 140;
        public const double Maksimal = 620;

        /// <summary>Perubahan tinggi per klik tombol (pixel).</summary>
        public const double Langkah = 60;

        private readonly string _kunciPref;
        private double _nilai;

        public TinggiGridBaris(string kunciPref)
        {
            _kunciPref = kunciPref ?? throw new ArgumentNullException(nameof(kunciPref));
            _nilai = Rapikan(AppPreferenceStore.GetInt(_kunciPref, (int)Bawaan));

            BesarCommand = new RelayCommand(Besarkan);
            KecilCommand = new RelayCommand(Kecilkan);
            ResetCommand = new RelayCommand(() => Nilai = Bawaan);
        }

        /// <summary>Tinggi grid saat ini (pixel); dipakai binding Height DataGrid.</summary>
        public double Nilai
        {
            get => _nilai;
            private set
            {
                double rapi = Rapikan(value);
                if (SetProperty(ref _nilai, rapi))
                {
                    // Pilihan operator tersimpan sehingga form berikutnya langsung memakainya.
                    AppPreferenceStore.SetInt(_kunciPref, (int)rapi);
                }
            }
        }

        public RelayCommand BesarCommand { get; }
        public RelayCommand KecilCommand { get; }
        public RelayCommand ResetCommand { get; }

        private void Besarkan() => Nilai = _nilai + Langkah;
        private void Kecilkan() => Nilai = _nilai - Langkah;

        private static double Rapikan(double nilai) =>
            Math.Clamp(Math.Round(nilai), Minimal, Maksimal);
    }
}
