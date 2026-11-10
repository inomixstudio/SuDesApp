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
        // Lencana isian per langkah (ringkasan pada navigasi kiri)
        // =====================================================================

        /// <summary>Kolom bagian yang sudah berlangganan perubahan isi (tambah/hapus field).</summary>
        private readonly HashSet<INotifyCollectionChanged> _langgananKolomBagian = new();

        /// <summary>
        /// Perbarui lencana tiap langkah navigasi kiri, mis. "3 teks · 2 kolom",
        /// agar progres isian terlihat tanpa membuka langkahnya.
        /// </summary>
        private void PerbaruiLencana()
        {
            if (LangkahDaftar.Count < 5) return;

            // Langkah 1 — identitas template
            LangkahDaftar[0].Lencana = string.IsNullOrWhiteSpace(_nama) ? "Nama belum diisi" : "Nama terisi";

            // Langkah 2 — jumlah komponen surat yang dicentang
            int komponen = 0;
            if (_pakaiKop) komponen++;
            if (_pakaiJudul) komponen++;
            if (_pakaiNomor) komponen++;
            if (_pakaiTempatTanggal) komponen++;
            if (_pakaiBlok) komponen++;
            if (_pakaiKolom) komponen++;
            if (_pakaiGrid) komponen++;
            if (_pakaiTandaTangan) komponen++;
            if (_pakaiTeksKaki) komponen++;
            LangkahDaftar[1].Lencana = komponen > 0 ? $"{komponen} komponen aktif" : "Tidak ada komponen";

            // Langkah 3 — isi surat (susunan bagian dari atas ke bawah)
            int teks = Bagian.Count(b => b is BagianTeksWizardItemViewModel);
            int dataDiri = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            int kolom = Bagian.Sum(b => b switch
            {
                BagianKolomWizardItemViewModel kelompok => kelompok.Kolom.Count,
                BagianDataDiriWizardItemViewModel orang => orang.Kolom.Count,
                _ => 0
            });
            // Fallback template lama: bila susunan bagian belum memuat, pakai daftar datar cermin.
            if (teks == 0 && Blok.Count > 0) teks = Blok.Count;
            if (kolom == 0 && Kolom.Count > 0) kolom = Kolom.Count;

            var isi = new List<string>(3);
            if (teks > 0) isi.Add($"{teks} teks");
            if (dataDiri > 0) isi.Add($"{dataDiri} data diri");
            if (kolom > 0) isi.Add($"{kolom} kolom");
            LangkahDaftar[2].Lencana = isi.Count > 0 ? string.Join(" · ", isi) : "Kosong";

            // Langkah 4 — tata letak & kaki
            var tata = new List<string>(2);
            if (_pakaiTandaTangan)
            {
                string sumber = _penandatanganDariDataDiri
                    ? "dari Data Diri"
                    : (string.IsNullOrWhiteSpace(_jabatanPenandatangan) ? "Kepala Desa" : _jabatanPenandatangan.Trim());
                tata.Add($"Ttd: {sumber}");
            }
            if (_pakaiTeksKaki) tata.Add("Teks kaki");
            LangkahDaftar[3].Lencana = tata.Count > 0 ? string.Join(" · ", tata) : "Tidak ada";

            // Langkah 5 — pratinjau
            LangkahDaftar[4].Lencana = _pratinjau != null ? "Contoh siap" : "Belum dibuat";
        }

        /// <summary>Bagian bertambah/berkurang: kelola langganan kolomnya, lalu segarkan lencana.</summary>
        private void BagianBerubah(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null) PasangLanggananKolom(e.NewItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null) LepasLanggananKolom(e.OldItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) LepasLanggananKolom(e.OldItems.OfType<BagianWizardItemViewModel>());
                    if (e.NewItems != null) PasangLanggananKolom(e.NewItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var kolom in _langgananKolomBagian)
                    {
                        kolom.CollectionChanged -= KolomBagianBerubah;
                    }
                    _langgananKolomBagian.Clear();
                    PasangLanggananKolom(Bagian);
                    break;
            }

            PerbaruiLencana();
        }

        /// <summary>Kolom di dalam sebuah bagian berubah (tambah/hapus field) → segarkan lencana.</summary>
        private void KolomBagianBerubah(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PerbaruiLencana();
        }

        private void PasangLanggananKolom(IEnumerable<BagianWizardItemViewModel> daftar)
        {
            foreach (var bagian in daftar)
            {
                INotifyCollectionChanged? kolom = bagian switch
                {
                    BagianKolomWizardItemViewModel kelompok => kelompok.Kolom,
                    BagianDataDiriWizardItemViewModel dataDiri => dataDiri.Kolom,
                    _ => null
                };
                if (kolom != null && _langgananKolomBagian.Add(kolom))
                {
                    kolom.CollectionChanged += KolomBagianBerubah;
                }
            }
        }

        private void LepasLanggananKolom(IEnumerable<BagianWizardItemViewModel> daftar)
        {
            foreach (var bagian in daftar)
            {
                INotifyCollectionChanged? kolom = bagian switch
                {
                    BagianKolomWizardItemViewModel kelompok => kelompok.Kolom,
                    BagianDataDiriWizardItemViewModel dataDiri => dataDiri.Kolom,
                    _ => null
                };
                if (kolom != null && _langgananKolomBagian.Remove(kolom))
                {
                    kolom.CollectionChanged -= KolomBagianBerubah;
                }
            }
        }

        public bool TampilIdentitas => Langkah == 0;
        public bool TampilElemen => Langkah == 1;
        public bool TampilIsi => Langkah == 2;
        public bool TampilTataLetak => Langkah == 3;
        public bool TampilPratinjau => Langkah == LangkahTerakhir;

        /// <summary>Tombol "Lanjut" ke langkah berikutnya (bukan menuju pratinjau).</summary>
        public bool TampilTombolLanjutLangkah => Langkah < LangkahTerakhir - 1;

        /// <summary>Tombol "Lanjut ke Pratinjau" hanya di langkah penyuntingan terakhir.</summary>
        public bool TampilLanjutPratinjau => Langkah == LangkahTerakhir - 1;

        /// <summary>Halaman penyusunan (satu halaman): kebalikan dari pratinjau.</summary>
        public bool TampilEditor => Langkah < LangkahTerakhir;

        public bool BisaKembali => Langkah > 0;
        public bool DiPratinjau => Langkah == LangkahTerakhir;

        /// <summary>Tombol "Lanjut" hanya tampil sebelum langkah pratinjau.</summary>
        public bool TampilTombolLanjut => Langkah < LangkahTerakhir;

        /// <summary>Nomor langkah untuk penanda langkah, mis. "Langkah 2 dari 5".</summary>
        public string NomorLangkahTeks => $"Langkah {Langkah + 1} dari {LangkahTerakhir + 1}";

        public string JudulLangkah => LangkahDaftar.ElementAtOrDefault(Langkah)?.Judul ?? string.Empty;
        public string KeteranganLangkah => LangkahDaftar.ElementAtOrDefault(Langkah)?.Keterangan ?? string.Empty;

        /// <summary>
        /// Judul halaman ini di title bar jendela, dibaca MainWindowViewModel lewat
        /// antarmuka <see cref="IJudulHalaman"/> supaya tahap wizard langsung terlihat.
        /// </summary>
        public string JudulHalaman
        {
            get
            {
                if (DiPratinjau)
                {
                    return "Template Surat — Pratinjau";
                }

                string mode = _modeWizard switch
                {
                    TemplateSuratViewModel.ModeEdit => "Ubah Template",
                    TemplateSuratViewModel.ModeDuplikat => "Duplikat Template",
                    TemplateSuratViewModel.ModeImpor => "Template dari File Word",
                    _ => "Template Baru"
                };

                return $"Template Surat — {mode} ({NomorLangkahTeks})";
            }
        }

        /// <summary>Label tombol lanjut: menyebut langkah berikutnya supaya jelas tujuannya.</summary>
        public string TeksTombolLanjut => Langkah >= LangkahTerakhir
            ? "Simpan & Isi Surat"
            : $"Lanjut: {LangkahDaftar.ElementAtOrDefault(Langkah + 1)?.Judul ?? "Berikutnya"}";

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

        /// <summary>Pesan kesalahan langkah saat ini (kosong bila tidak ada).</summary>
        public string PesanKesalahan
        {
            get => _pesanKesalahan;
            private set
            {
                if (SetProperty(ref _pesanKesalahan, value))
                {
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public bool AdaKesalahan => !string.IsNullOrWhiteSpace(_pesanKesalahan);

        /// <summary>Pesan informasi/netral (mis. hasil pratinjau).</summary>
        public string PesanInformasi
        {
            get => _pesanInformasi;
            private set
            {
                if (SetProperty(ref _pesanInformasi, value))
                {
                    OnPropertyChanged(nameof(AdaInformasi));
                }
            }
        }

        public bool AdaInformasi => !string.IsNullOrWhiteSpace(_pesanInformasi);


    }
}
