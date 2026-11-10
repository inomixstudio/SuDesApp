using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class IdmViewModel : ObservableObject
    {
        // ===================== Keadaan pemuatan =====================

        public bool SedangMemuat
        {
            get => _sedangMemuat;
            private set
            {
                if (SetProperty(ref _sedangMemuat, value))
                {
                    OnPropertyChanged(nameof(TidakSedangMemuat));
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                    OnPropertyChanged(nameof(JudulKeadaan));
                    OnPropertyChanged(nameof(IsiKeadaan));
                    ((AsyncRelayCommand)MuatCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)CetakCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool TidakSedangMemuat => !_sedangMemuat;

        /// <summary>Pesan proses ("Menghubungi situs pusat…"), bukan galat.</summary>
        public string PesanStatus
        {
            get => _pesanStatus;
            private set
            {
                if (SetProperty(ref _pesanStatus, value)) OnPropertyChanged(nameof(AdaPesanStatus));
            }
        }

        public bool AdaPesanStatus => _pesanStatus.Length > 0;

        /// <summary>Alasan halaman belum bisa menampilkan data (desa belum dipilih, situs mati, …).</summary>
        public string PesanGalat
        {
            get => _pesanGalat;
            private set
            {
                if (SetProperty(ref _pesanGalat, value))
                {
                    OnPropertyChanged(nameof(AdaGalat));
                    OnPropertyChanged(nameof(AdaGalatDiToolbar));
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                    OnPropertyChanged(nameof(JudulKeadaan));
                    OnPropertyChanged(nameof(IsiKeadaan));
                }
            }
        }

        public bool AdaGalat => _pesanGalat.Length > 0;

        /// <summary>
        /// Situs pusat tidak bisa dihubungi tapi masih ada data terakhir yang
        /// berhasil dimuat: pesan ini menyertakan tanggal pengambilannya supaya
        /// pengguna tahu angka di layar bukan angka hari ini.
        /// </summary>
        public string PesanCadangan
        {
            get => _pesanCadangan;
            private set
            {
                if (SetProperty(ref _pesanCadangan, value)) OnPropertyChanged(nameof(AdaPesanCadangan));
            }
        }

        public bool AdaPesanCadangan => _pesanCadangan.Length > 0;

        /// <summary>
        /// Tidak ada isi yang bisa ditampilkan dan tidak sedang memuat: tempat
        /// yang seharusnya berisi kartu-kartu digantikan SATU kartu keadaan.
        /// Kartu itu menangani dua sebab sekaligus — belum pernah dimuat dan
        /// gagal dimuat — supaya halaman tidak pernah berakhir sebagai layar
        /// kosong dengan satu baris galat di toolbar.
        /// </summary>
        public bool TampilkanKartuKeadaan => !AdaData && !_sedangMemuat;

        /// <summary>Judul kartu keadaan — membedakan "belum dimuat" dan "gagal".</summary>
        public string JudulKeadaan => AdaGalat
            ? "Data pusat belum bisa dimuat"
            : "Data IDM belum dimuat";

        /// <summary>
        /// Isi kartu keadaan: alasan kegagalan dari situs/pengaturan, atau
        /// petunjuk singkat saat halaman baru dibuka.
        /// </summary>
        public string IsiKeadaan => AdaGalat ? _pesanGalat : PesanMenunggu;

        /// <summary>
        /// Baris galat di toolbar hanya dipakai saat halaman SUDAH berisi data —
        /// mis. muat ulang gagal tetapi angka lama tetap tampil. Tanpa data,
        /// pesannya sudah dibawa kartu keadaan sehingga tidak tampil dua kali.
        /// </summary>
        public bool AdaGalatDiToolbar => AdaGalat && AdaData;

        /// <summary>
        /// Teks petunjuk saat halaman belum pernah memuat data. Ditulis sebagai
        /// kalimat yang menyebut tombolnya sendiri dan sumber identitas desa,
        /// karena dua hal itulah yang paling sering jadi sebab halaman kosong.
        /// </summary>
        public string PesanMenunggu =>
            "Data diambil dari idm.kemendesa.go.id memakai nama desa pada Pengaturan Surat "
            + "(memerlukan sambungan internet).";


    }
}
