using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class TemplateSuratWizardViewModel : ObservableObject, IJudulHalaman
    {
        // =====================================================================
        // Data yang diisi pengguna
        // =====================================================================

        public string Nama
        {
            get => _nama;
            set
            {
                if (SetProperty(ref _nama, value))
                {
                    PerbaruiLencana();
                }
            }
        }
        public string Deskripsi { get => _deskripsi; set => SetProperty(ref _deskripsi, value); }
        public bool FormatContoh { get => _formatContoh; set => SetProperty(ref _formatContoh, value); }
        public int VersiFormat { get => _versiFormat; set => SetProperty(ref _versiFormat, Math.Max(1, value)); }

        public bool PakaiKop
        {
            get => _pakaiKop;
            set
            {
                if (SetProperty(ref _pakaiKop, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Menyertakan judul surat (kotak centang komponen).</summary>
        public bool PakaiJudul
        {
            get => _pakaiJudul;
            set
            {
                if (SetProperty(ref _pakaiJudul, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string Judul { get => _judul; set => SetProperty(ref _judul, value); }
        public string SubJudul { get => _subJudul; set => SetProperty(ref _subJudul, value); }

        /// <summary>Menyertakan blok teks bebas pada surat (kotak centang komponen).</summary>
        public bool PakaiBlok
        {
            get => _pakaiBlok;
            set
            {
                if (SetProperty(ref _pakaiBlok, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Menyertakan kolom isian pada surat (kotak centang komponen).</summary>
        public bool PakaiKolom
        {
            get => _pakaiKolom;
            set
            {
                if (SetProperty(ref _pakaiKolom, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiNomor
        {
            get => _pakaiNomor;
            set
            {
                if (SetProperty(ref _pakaiNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                    PerbaruiLencana();
                }
            }
        }

        public string AwalanNomor
        {
            get => _awalanNomor;
            set
            {
                if (SetProperty(ref _awalanNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        public string PolaNomor
        {
            get => _polaNomor;
            set
            {
                if (SetProperty(ref _polaNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        public bool PakaiTempatTanggal
        {
            get => _pakaiTempatTanggal;
            set
            {
                if (SetProperty(ref _pakaiTempatTanggal, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiGrid
        {
            get => _pakaiGrid;
            set
            {
                if (SetProperty(ref _pakaiGrid, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiTandaTangan
        {
            get => _pakaiTandaTangan;
            set
            {
                if (SetProperty(ref _pakaiTandaTangan, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string JabatanPenandatangan
        {
            get => _jabatanPenandatangan;
            set
            {
                if (SetProperty(ref _jabatanPenandatangan, value))
                {
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiTeksKaki
        {
            get => _pakaiTeksKaki;
            set
            {
                if (SetProperty(ref _pakaiTeksKaki, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string TeksKaki { get => _teksKaki; set => SetProperty(ref _teksKaki, value); }

        public ObservableCollection<BlokTeksItemViewModel> Blok { get; }
        public ObservableCollection<KolomItemViewModel> Kolom { get; }

        /// <summary>
        /// Susunan badan surat yang baru: bagian terurut (teks, data diri, kolom isian)
        /// yang menentukan tata letak dari atas ke bawah.
        /// </summary>
        public ObservableCollection<BagianWizardItemViewModel> Bagian { get; }

        /// <summary>Nomor surat diketik manual saat mengisi surat (tanpa hitungan otomatis).</summary>
        public bool NomorManual
        {
            get => _nomorManual;
            set
            {
                if (SetProperty(ref _nomorManual, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        /// <summary>Penandatangan memakai salah satu orang dari blok Data Diri (bukan Kepala Desa otomatis).</summary>
        public bool PenandatanganDariDataDiri
        {
            get => _penandatanganDariDataDiri;
            set
            {
                if (SetProperty(ref _penandatanganDariDataDiri, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Kunci blok Data Diri yang menjadi penandatangan (kosong = Kepala Desa).</summary>
        public string KunciPenandatanganDataDiri { get => _kunciPenandatanganDataDiri; set => SetProperty(ref _kunciPenandatanganDataDiri, value); }

        /// <summary>Daftar blok Data Diri yang bisa dipilih menjadi penandatangan surat.</summary>
        public IReadOnlyList<PenandatanganDataDiriOpsi> DaftarPenandatangan
        {
            get
            {
                var daftar = new List<PenandatanganDataDiriOpsi>();
                int nomor = 0;
                foreach (var bagian in Bagian.OfType<BagianDataDiriWizardItemViewModel>())
                {
                    nomor++;
                    string judul = (bagian.Judul ?? string.Empty).Trim();
                    if (judul.Length == 0)
                    {
                        judul = "Data Diri " + nomor;
                    }
                    daftar.Add(new PenandatanganDataDiriOpsi(bagian.Kunci, judul));
                }
                return daftar;
            }
        }

        /// <summary>Contoh nomor surat dari awalan & pola yang dipilih.</summary>
        public string ContohNomor => TemplateSuratNomor.Bangun(PolaNomor, AwalanNomor, 1, DateTime.Now.Year);

        /// <summary>Ringkasan susunan surat untuk pratinjau & daftar.</summary>
        public string RingkasanSusunan => TemplateDariForm().RingkasanSusunan;

        /// <summary>Pratinjau PDF langkah terakhir.</summary>
        public PdfPreviewViewModel? Pratinjau
        {
            get => _pratinjau;
            private set
            {
                if (SetProperty(ref _pratinjau, value))
                {
                    OnPropertyChanged(nameof(AdaPratinjau));
                    PerbaruiLencana();
                }
            }
        }

        public bool AdaPratinjau => _pratinjau != null;


    }
}
