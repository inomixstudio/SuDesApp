using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // ----- Keadaan -----
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(AdanyaData));
                    SegarkanPerintah();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>Catat pesan singkat tanpa memuat ulang daftar (dipakai halaman).</summary>
        public void CatatStatus(string pesan) => StatusText = pesan;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value)) JadwalkanMuatUlang();
            }
        }

        /// <summary>Filter status; string kosong berarti semua status.</summary>
        public string FilterStatus
        {
            get => _filterStatus;
            set
            {
                if (SetProperty(ref _filterStatus, value ?? string.Empty))
                {
                    Halaman = 1;
                    _ = LoadAsync(true);
                }
            }
        }

        /// <summary>
        /// Filter wilayah dengan format "RT:001" / "RW:002" / "Dusun:Dusun I".
        /// Nilai disimpan apa adanya sebagai "<jenis>:<nilai>" supaya dropdown
        /// RT, RW, dan dusun bisa memakai satu daftar.
        /// </summary>
        public string FilterWilayah
        {
            get => _filterWilayah;
            set
            {
                if (SetProperty(ref _filterWilayah, value ?? string.Empty))
                {
                    Halaman = 1;
                    _ = LoadAsync(true);
                }
            }
        }

        public int Halaman
        {
            get => _halaman;
            private set
            {
                if (SetProperty(ref _halaman, value)) SegarkanPerintah();
            }
        }

        public int TotalData
        {
            get => _totalData;
            private set { if (SetProperty(ref _totalData, value)) OnPropertyChanged(nameof(RingkasanPaginasi)); }
        }

        public int TotalHalaman
        {
            get => _totalHalaman;
            private set { if (SetProperty(ref _totalHalaman, value)) OnPropertyChanged(nameof(RingkasanPaginasi)); }
        }

        public int JumlahDitampilkan
        {
            get => _jumlahDitampilkan;
            private set => SetProperty(ref _jumlahDitampilkan, value);
        }

        public bool AdanyaData => Items.Count > 0;

        public string RingkasanPaginasi =>
            TotalData == 0
                ? "Tidak ada warga yang sesuai filter."
                : $"Halaman {Halaman} dari {TotalHalaman} • total {TotalData} warga";


    }
}
