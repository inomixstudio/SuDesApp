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
        // Pencarian & pemilihan
        // =====================================================================

        private string _searchText = string.Empty;

        /// <summary>Kotak pencarian: menyaring nama berkas maupun kode formulir (mis. "F-1").</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    RefreshTampilan();
                }
            }
        }

        public bool AdaPencarian => _searchText.Trim().Length > 0;

        /// <summary>Benar bila pencarian aktif dan tidak ada baris yang cocok.</summary>
        public bool KosongKarenaPencarian => TemplatesTampil.Count == 0 && Templates.Count > 0;

        /// <summary>Benar bila folder Templates memang belum berisi PDF sama sekali.</summary>
        public bool KosongTanpaFormulir => Templates.Count == 0;

        /// <summary>Judul keadaan kosong — menyebut kabupaten yang sedang dipakai.</summary>
        public string JudulKosong => _sumberAktif.JudulKosong;

        /// <summary>
        /// Petunjuk keadaan kosong yang mengikuti sumber yang tersedia: unduh
        /// otomatis, buka situs resmi lalu tambah manual, atau tambah manual saja.
        /// </summary>
        public string PesanKosong => _sumberAktif.PesanKosong;

        /// <summary>Pesan saat pencarian tidak menemukan baris yang cocok.</summary>
        public string PesanCari =>
            $"Tidak ada formulir yang cocok dengan pencarian \u201c{_searchText.Trim()}\u201d.";

        public int JumlahTerpilih => Templates.Count(t => t.IsChecked);

        public bool AdaTerpilih => Templates.Count(t => t.IsChecked) > 0;

        /// <summary>Ringkasan pilihan untuk statusbar: "3 formulir dipilih" / "Belum ada yang dipilih".</summary>
        public string TeksTerpilih
        {
            get
            {
                int jumlah = Templates.Count(t => t.IsChecked);
                return jumlah == 0 ? "Belum ada yang dipilih" : $"{jumlah} formulir dipilih";
            }
        }

        /// <summary>Benar bila seluruh baris yang tampil sudah dicentang.</summary>
        public bool SemuaTampilTerpilih => TemplatesTampil.Count > 0 && TemplatesTampil.All(t => t.IsChecked);

        /// <summary>Label tombol pilih-semua yang berubah sesuai keadaan centang saat ini.</summary>
        public string TeksPilihSemua => SemuaTampilTerpilih ? "Kosongkan pilihan" : "Pilih semua tampil";

        public ICommand TogglePilihSemuaCommand { get; }

        /// <summary>Buka folder Templates di Windows Explorer (agar berkas mudah diperiksa/disalin).</summary>
        public ICommand BukaFolderCommand { get; }


    }
}
