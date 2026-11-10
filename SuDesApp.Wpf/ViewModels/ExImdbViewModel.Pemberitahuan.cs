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
        // Pemberitahuan inline (pengganti dialog popup)
        // =====================================================================

        private bool _adaPesan;
        private string _pesanJudul = string.Empty;
        private string _pesanIsi = string.Empty;
        private bool _pesanBerhasil = true;

        /// <summary>Benar bila kartu hasil/kesalahan terakhir perlu ditampilkan.</summary>
        public bool AdaPesan
        {
            get => _adaPesan;
            private set => SetProperty(ref _adaPesan, value);
        }

        public string PesanJudul { get => _pesanJudul; private set => SetProperty(ref _pesanJudul, value ?? string.Empty); }
        public string PesanIsi { get => _pesanIsi; private set => SetProperty(ref _pesanIsi, value ?? string.Empty); }

        /// <summary>True = kabar berhasil (kartu hijau), false = gagal/peringatan (kartu merah).</summary>
        public bool PesanBerhasil { get => _pesanBerhasil; private set => SetProperty(ref _pesanBerhasil, value); }

        /// <summary>Baris rincian tambahan yang ditampilkan di dalam kartu kabar.</summary>
        public ObservableCollection<string> PesanRincian { get; } = new();

        public ICommand TutupTerakhirCommand { get; }

        private void TampilkanPesan(string judul, string isi, bool berhasil, params string[] rincian)
        {
            PesanJudul = judul;
            PesanIsi = isi;
            PesanBerhasil = berhasil;
            PesanRincian.Clear();

            foreach (var baris in rincian.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                PesanRincian.Add(baris);
            }

            AdaPesan = true;
        }

    }
}
