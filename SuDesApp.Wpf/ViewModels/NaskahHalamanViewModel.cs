using System.Linq;
using SuDesApp.GeneratorPdf;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu halaman naskah Perdes yang disunting di formulir.</summary>
    public sealed class NaskahHalamanViewModel : ObservableObject
    {
        private string _judul;
        private string _isi;

        public NaskahHalamanViewModel(NaskahPerdesHalaman halaman)
        {
            _judul = halaman.Judul;
            _isi = halaman.Isi;
        }

        public string Judul
        {
            get => _judul;
            set
            {
                if (SetProperty(ref _judul, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(Ringkasan));
                }
            }
        }

        public string Isi
        {
            get => _isi;
            set
            {
                if (SetProperty(ref _isi, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(Ringkasan));
                }
            }
        }

        /// <summary>Keterangan pendek di daftar halaman: jumlah baris berisi.</summary>
        public string Ringkasan
        {
            get
            {
                int baris = string.IsNullOrWhiteSpace(Isi)
                    ? 0
                    : Isi.Split('\n').Count(b => !string.IsNullOrWhiteSpace(b));
                return baris == 0 ? "kosong" : $"{baris} baris";
            }
        }

        public NaskahPerdesHalaman KeModel() => new()
        {
            Judul = string.IsNullOrWhiteSpace(Judul) ? "Halaman" : Judul.Trim(),
            Isi = Isi
        };
    }
}
