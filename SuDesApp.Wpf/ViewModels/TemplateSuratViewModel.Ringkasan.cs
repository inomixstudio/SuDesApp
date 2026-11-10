using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class TemplateSuratViewModel : ObservableObject
    {
        // ===== Ringkasan =====

        public int JumlahTotal => _semua.Count;
        public int JumlahTampil => Items.Count;
        public int JumlahTersimpan => _semua.Count(t => t.Id > 0);
        public int JumlahContoh => _contohVirtual.Count;
        public int JumlahBerkolom => _semua.Count(t => t.AdaKolom);

        /// <summary>
        /// Ringkasan daftar di bilah alat: jumlah yang tampil, sumber yang sedang
        /// disaring, dan kata kunci pencarian — supaya keadaan daftar tidak perlu
        /// ditebak dari jumlah baris saja.
        /// </summary>
        public string RingkasanTampil
        {
            get
            {
                string cari = (_pencarian ?? string.Empty).Trim();
                string sumber = _filterSumber switch
                {
                    FilterTersimpan => "hanya tersimpan",
                    FilterContoh => "hanya contoh",
                    _ => string.Empty
                };

                string teks = $"Menampilkan {Items.Count} dari {_semua.Count} template";
                if (sumber.Length > 0) teks += " · " + sumber;
                if (cari.Length > 0) teks += $" · cari \"{cari}\"";
                return teks;
            }
        }

        /// <summary>Ringkasan satu baris untuk bilah status di bawah halaman.</summary>
        public string PesanStatus =>
            $"{JumlahTotal} template · {JumlahTersimpan} tersimpan · {JumlahContoh} contoh siap pakai · {JumlahBerkolom} siap diisi";

        /// <summary>Pesan pada daftar ketika belum ada data yang cocok.</summary>
        public string PesanKosong
        {
            get => _pesanKosong;
            private set
            {
                if (SetProperty(ref _pesanKosong, value))
                {
                    OnPropertyChanged(nameof(AdaPesanKosong));
                }
            }
        }

        /// <summary>True bila daftar kosong/tidak ada hasil pencarian (pesan ditampilkan).</summary>
        public bool AdaPesanKosong => !string.IsNullOrWhiteSpace(_pesanKosong);


    }
}
