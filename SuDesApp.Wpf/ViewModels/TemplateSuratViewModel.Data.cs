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
        // ===== Data =====

        public ObservableCollection<TemplateSuratKustom> Items { get; }

        public string Pencarian
        {
            get => _pencarian;
            set
            {
                if (SetProperty(ref _pencarian, value))
                {
                    TerapkanFilter();
                }
            }
        }

        /// <summary>
        /// Sumber baris yang ditampilkan: seluruh template, hanya yang tersimpan,
        /// atau hanya contoh aplikasi. Memisahkan keduanya membuat pengguna tahu
        /// mana template miliknya sendiri dan mana contoh siap pakai.
        /// </summary>
        public string FilterSumber
        {
            get => _filterSumber;
            set
            {
                string baru = string.IsNullOrWhiteSpace(value) ? FilterSemua : value;
                if (SetProperty(ref _filterSumber, baru))
                {
                    OnPropertyChanged(nameof(FilterSemuaAktif));
                    OnPropertyChanged(nameof(FilterTersimpanAktif));
                    OnPropertyChanged(nameof(FilterContohAktif));
                    TerapkanFilter();
                }
            }
        }

        /// <summary>Penanda pilihan filter aktif untuk tombol pil di bilah alat.</summary>
        public bool FilterSemuaAktif => _filterSumber == FilterSemua;
        public bool FilterTersimpanAktif => _filterSumber == FilterTersimpan;
        public bool FilterContohAktif => _filterSumber == FilterContoh;

        /// <summary>Cara daftar diurutkan (lihat <see cref="UrutNama"/> dan kawan-kawan).</summary>
        public string Urutan
        {
            get => _urutan;
            set
            {
                string baru = string.IsNullOrWhiteSpace(value) ? UrutNama : value;
                if (SetProperty(ref _urutan, baru))
                {
                    TerapkanFilter();
                }
            }
        }

        public IReadOnlyList<FilterPilihan> DaftarFilter { get; } = new List<FilterPilihan>
        {
            new(FilterSemua, "Semua"),
            new(FilterTersimpan, "Tersimpan"),
            new(FilterContoh, "Contoh"),
        };

        public IReadOnlyList<FilterPilihan> DaftarUrutan { get; } = new List<FilterPilihan>
        {
            new(UrutNama, "Nama (A–Z)"),
            new(UrutDiubah, "Terakhir diubah"),
            new(UrutJudul, "Judul surat (A–Z)"),
        };

        public TemplateSuratKustom? Terpilih
        {
            get => _terpilih;
            set
            {
                if (SetProperty(ref _terpilih, value))
                {
                    OnPropertyChanged(nameof(AdaPilihan));
                    OnPropertyChanged(nameof(TeksEdit));
                    OnPropertyChanged(nameof(StatusPilihan));
                    RaisePerintah();
                }
            }
        }

        public bool AdaPilihan => _terpilih != null;

        public string TeksEdit => Terpilih != null && _contohVirtual.ContainsKey(Terpilih.Id)
            ? "Salin & Edit" : "Edit";

        /// <summary>
        /// Panduan singkat di bawah bilah alat: apa yang bisa dilakukan pada baris
        /// yang sedang dipilih, atau apa yang harus dilakukan bila belum ada pilihan.
        /// </summary>
        public string StatusPilihan
        {
            get
            {
                if (_terpilih == null)
                {
                    return "Pilih satu baris pada daftar untuk mengaktifkan Edit, Duplikat, Pratinjau, dan Isi Surat. " +
                        "Tombol Hapus hanya berlaku untuk template yang sudah tersimpan.";
                }

                string contoh = _contohVirtual.ContainsKey(_terpilih.Id)
                    ? " Contoh aplikasi bisa langsung diisi; Edit menyalinnya lebih dulu."
                    : string.Empty;
                return $"Terpilih: {_terpilih.NamaTampil} — {_terpilih.RingkasanSusunan}.{contoh}";
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePerintah();
                }
            }
        }


    }
}
