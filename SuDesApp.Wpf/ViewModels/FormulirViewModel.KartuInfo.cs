using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class FormulirViewModel : ObservableObject, IDisposable
    {
        // =====================================================================
        // Kartu informasi inline (tawaran unduh & hasil unduhan)
        // =====================================================================

        private bool _adaTawaranUnduh;
        private string _tawaranUnduhTeks = string.Empty;

        /// <summary>Benar bila kartu tawaran "formulir belum lengkap" perlu ditampilkan.</summary>
        public bool AdaTawaranUnduh
        {
            get => _adaTawaranUnduh;
            private set => SetProperty(ref _adaTawaranUnduh, value);
        }

        public string TawaranUnduhTeks
        {
            get => _tawaranUnduhTeks;
            private set => SetProperty(ref _tawaranUnduhTeks, value ?? string.Empty);
        }

        public ICommand TerimaTawaranUnduhCommand { get; }
        public ICommand TutupTawaranUnduhCommand { get; }

        private bool _adaHasilUnduhan;
        private string _hasilUnduhanJudul = string.Empty;
        private string _hasilUnduhanRingkas = string.Empty;
        private bool _hasilUnduhanBerhasil = true;

        /// <summary>Benar bila kartu hasil unduhan terakhir perlu ditampilkan.</summary>
        public bool AdaHasilUnduhan
        {
            get => _adaHasilUnduhan;
            private set => SetProperty(ref _adaHasilUnduhan, value);
        }

        public string HasilUnduhanJudul
        {
            get => _hasilUnduhanJudul;
            private set => SetProperty(ref _hasilUnduhanJudul, value ?? string.Empty);
        }

        /// <summary>Ringkasan hasil: jumlah berhasil, dari Drive, dan yang dilewati.</summary>
        public string HasilUnduhanRingkas
        {
            get => _hasilUnduhanRingkas;
            private set => SetProperty(ref _hasilUnduhanRingkas, value ?? string.Empty);
        }

        /// <summary>True bila hasil terakhir berhasil (warna kartu hijau), false bila gagal (merah).</summary>
        public bool HasilUnduhanBerhasil
        {
            get => _hasilUnduhanBerhasil;
            private set => SetProperty(ref _hasilUnduhanBerhasil, value);
        }

        /// <summary>Rincian per formulir (satu baris per berkas), ditampilkan di kartu hasil.</summary>
        public ObservableCollection<string> HasilUnduhanRincian { get; }

        public ICommand TutupHasilUnduhanCommand { get; }

        /// <summary>Tutup kartu hasil unduhan (tombol ✕) — riwayatnya tetap ada di lonceng.</summary>
        private void TutupHasilUnduhan()
        {
            AdaHasilUnduhan = false;
            HasilUnduhanRincian.Clear();
        }

        private void TampilkanHasilUnduhan(
            string judul, string ringkas, IEnumerable<string> rincian, bool berhasil)
        {
            HasilUnduhanJudul = judul;
            HasilUnduhanRingkas = ringkas;
            HasilUnduhanBerhasil = berhasil;
            HasilUnduhanRincian.Clear();

            foreach (var baris in rincian)
            {
                HasilUnduhanRincian.Add(baris);
            }

            AdaHasilUnduhan = true;
        }

        /// <summary>
        /// Sembunyikan tawaran unduh dan jalankan unduhannya (dipakai tombol pada
        /// kartu tawaran). Pengguna yang menyetujui langsung melihat prosesnya
        /// berjalan di halaman ini — tanpa dialog popup.
        /// </summary>
        private async Task AcceptDownloadOfferAsync()
        {
            TutupTawaranUnduh();
            if (!IsLoading)
            {
                await DownloadFromSourceAsync();
            }
        }

        private void TutupTawaranUnduh()
        {
            AdaTawaranUnduh = false;
        }


    }
}
