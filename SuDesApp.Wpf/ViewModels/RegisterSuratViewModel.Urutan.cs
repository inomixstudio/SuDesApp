using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class RegisterSuratViewModel : ObservableObject
    {
        /// <summary>
        /// Terapkan klik sortir kolom pada register; kembalikan arah sortir terbaru
        /// (true = menaik). Dipanggil dari tombol kepala kolom (SortCommand).
        /// </summary>
        public bool ApplySort(string? memberPath)
        {
            var repoColumn = MapSortColumn(memberPath);
            if (_sortBy == repoColumn)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortBy = repoColumn;
                _sortAscending = true;
            }

            _sortTampil = memberPath ?? "ID_Surat";

            OnPropertyChanged(nameof(SortColumn));
            OnPropertyChanged(nameof(SortColumnTampil));
            OnPropertyChanged(nameof(IsSortedAscending));

            _ = LoadDataSafeAsync();
            return _sortAscending;
        }

        /// <summary>
        /// Nama kolom SORTIR REPOSITORI yang aktif (dipakai saat memanggil query).
        /// </summary>
        public string SortColumn => _sortBy;

        /// <summary>
        /// Nama kolom yang diklik pengguna, apa adanya seperti yang ditulis di
        /// kepala kolom XAML ("TanggalSurat", "NamaDisplay", "NamaJenis").
        ///
        /// <see cref="SortColumn"/> tidak bisa dipakai untuk dicari panah ▲/▼:
        /// MapSortColumn mengubah beberapa nama (mis. "TanggalSurat" → "Tanggal",
        /// "NamaDisplay" → "Nama"), sedangkan MultiBinding di XAML membandingkan
        /// SortColumn dengan literal nama properti. Akibatnya panah tidak pernah
        /// muncul untuk kolom Tanggal, Nama, dan Jenis Surat — sorted diam-diam
        /// tanpa umpan balik ke pengguna.
        /// </summary>
        public string SortColumnTampil => _sortTampil;

        /// <summary>Benar bila pengurutan aktif menaik.</summary>
        public bool IsSortedAscending => _sortAscending;

        /// <summary>Petakan nama properti tampilan ke kolom sortir repositori (whitelist SQL).</summary>
        private static string MapSortColumn(string? memberPath) => memberPath switch
        {
            "ID_Surat" => "ID_Surat",
            "TanggalSurat" => "Tanggal",
            "NomorSurat" => "NomorSurat",
            "NamaDisplay" => "Nama",
            "NamaJenis" => "JenisSurat",
            "Status" => "Status",
            "CreatedAt" => "CreatedAt",
            _ => "ID_Surat"
        };


    }
}
