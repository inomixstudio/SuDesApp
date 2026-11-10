using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        // =====================================================================
        // Pesan status halaman.
        //
        // Identitas aplikasi (ikon, nama, versi, pengembang) tidak lagi disimpan di
        // sini — statusbar kaki halaman memakai kontrol bersama AppStatusBar yang
        // membacanya langsung dari atribut assembly.
        // =====================================================================

        /// <summary>Pesan tetap saat tidak ada perubahan yang baru disimpan.</summary>
        private const string StatusSiap = "Semua perubahan tersimpan otomatis — tidak ada yang perlu diklik Simpan.";

        private string _statusMessage = StatusSiap;
        private readonly StatusSesaat _statusSesaat;

        /// <summary>Pesan pada statusbar halaman (berubah sesaat setelah menyimpan).</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>Tambahkan baris sakelar, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBaris(SettingRowVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowVM.Nilai))
                    _statusSesaat.Tampilkan($"Tersimpan — {baris.Judul}");
            };
            Rows.Add(baris);
        }

        /// <summary>Tambahkan baris bernilai angka, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBarisAngka(SettingRowAngkaVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowAngkaVM.TeksNilai))
                    _statusSesaat.Tampilkan($"Tersimpan — {baris.Judul}");
            };
            RowsAngka.Add(baris);
        }


    }
}
