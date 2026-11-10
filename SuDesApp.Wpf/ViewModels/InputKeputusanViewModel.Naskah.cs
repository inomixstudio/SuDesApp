using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class InputKeputusanViewModel : ObservableObject, IJudulHalaman
    {
        private void MuatNaskahAwal()
        {
            HalamanNaskah.Clear();
            HalamanTerpilih = null;
            InfoNaskah = string.Empty;

            if (!IsPerdes)
            {
                UpdateNaskahState();
                return;
            }

            // Edit memakai PDF arsip sendiri; Salin memakai PDF baris sumber.
            string? sumberNaskah = _storedFileName ?? _naskahSalinan;
            if (!string.IsNullOrWhiteSpace(sumberNaskah))
            {
                string? pdfPath = _repository.ResolveWordFullPath(sumberNaskah);
                if (pdfPath != null)
                {
                    try
                    {
                        var naskah = NaskahPerdesStore.Muat(pdfPath);
                        if (naskah != null)
                        {
                            IsiHalamanDariModel(naskah.Halaman);
                            InfoNaskah = _naskahSalinan != null
                                ? $"{naskah.Halaman.Count} halaman naskah tersalin dari data sumber. Sunting lalu simpan untuk menerbitkan PDF baru."
                                : $"{naskah.Halaman.Count} halaman naskah tersimpan dimuat. Sunting lalu simpan untuk menerbitkan PDF baru.";
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Naskah sampingan tidak dapat dibaca untuk {Berkas}", sumberNaskah);
                        InfoNaskah = "Naskah tersimpan tidak dapat dibaca. Isi halaman baru untuk menggantikan PDF lama.";
                        UpdateNaskahState();
                        return;
                    }
                }
            }

            InfoNaskah = sumberNaskah != null
                ? "Naskah lama tidak tersedia untuk disunting; PDF lama tetap tersimpan sampai Anda mengisi halaman naskah."
                : "Belum ada halaman. Tambahkan halaman atau pilih salah satu contoh Perdes di daftar.";
            UpdateNaskahState();
        }

        private void IsiHalamanDariModel(IEnumerable<NaskahPerdesHalaman> halaman)
        {
            foreach (var lama in HalamanNaskah)
            {
                lama.PropertyChanged -= Halaman_PropertyChanged;
            }
            HalamanNaskah.Clear();
            foreach (var item in halaman)
            {
                var halamanBaru = new NaskahHalamanViewModel(item);
                halamanBaru.PropertyChanged += Halaman_PropertyChanged;
                HalamanNaskah.Add(halamanBaru);
            }

            HalamanTerpilih = HalamanNaskah.FirstOrDefault();
            UpdateNaskahState();
        }

        private void UpdateNaskahState()
        {
            OnPropertyChanged(nameof(HasNaskah));
            ((AsyncRelayCommand)PratinjauNaskahCommand).RaiseCanExecuteChanged();
            ((RelayCommand)HapusHalamanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)NaikkanHalamanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)TurunkanHalamanCommand).RaiseCanExecuteChanged();
        }

        private void TambahHalaman()
        {
            var halaman = new NaskahHalamanViewModel(new NaskahPerdesHalaman
            {
                Judul = $"Halaman {HalamanNaskah.Count + 1}",
                Isi = string.Empty
            });
            halaman.PropertyChanged += Halaman_PropertyChanged;
            HalamanNaskah.Add(halaman);
            HalamanTerpilih = halaman;
            InfoNaskah = "Halaman baru ditambahkan. Isi teks naskah pada kotak di sebelah kanan.";
            UpdateNaskahState();
        }

        private void HapusHalaman()
        {
            if (_halamanTerpilih == null) return;
            int indeks = HalamanNaskah.IndexOf(_halamanTerpilih);
            _halamanTerpilih.PropertyChanged -= Halaman_PropertyChanged;
            HalamanNaskah.Remove(_halamanTerpilih);
            HalamanTerpilih = HalamanNaskah.Count == 0
                ? null
                : HalamanNaskah[Math.Clamp(indeks, 0, HalamanNaskah.Count - 1)];
            InfoNaskah = HalamanNaskah.Count == 0
                ? "Belum ada halaman naskah."
                : "Halaman dihapus.";
            UpdateNaskahState();
        }

        private void GeserHalaman(int arah)
        {
            if (_halamanTerpilih == null) return;
            int indeks = HalamanNaskah.IndexOf(_halamanTerpilih);
            int tujuan = indeks + arah;
            if (tujuan < 0 || tujuan >= HalamanNaskah.Count) return;
            HalamanNaskah.Move(indeks, tujuan);
            HalamanTerpilih = _halamanTerpilih;
            UpdateNaskahState();
        }

        private void Halaman_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NaskahHalamanViewModel.Isi))
            {
                UpdateNaskahState();
            }
        }


    }
}
