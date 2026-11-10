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
        // ===== Muat data =====

        /// <summary>Muat ulang seluruh template dari database.</summary>
        public async Task MuatAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // Sekali saja: bila daftar masih benar-benar kosong, contoh siap pakai
                // (pengantar RT/RW, izin keramaian, keterangan penghasilan, dan lainnya)
                // dipasang supaya pengguna bisa langsung mencetak tanpa menyusun dulu.
                await SiapkanContohBawaanAsync();

                var daftar = await _repository.GetAllAsync();

                _semua.Clear();
                _semua.AddRange(daftar);
                TambahkanContohBelumTerpasang(daftar);

                TerapkanFilter();

                // Pilihan sebelumnya dipertahankan bila template-nya masih ada.
                if (_terpilih != null)
                {
                    Terpilih = Items.FirstOrDefault(t => t.Id == _terpilih.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar template surat");
                PesanKosong = "Daftar template tidak dapat dimuat: " + ex.Message;
                Pesan.Galat("Daftar template tidak dapat dimuat",
                    "Coba buka halaman ini lagi sebentar lagi. Penyebabnya: " + ex.Message,
                    "muat-template");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void TerapkanFilter()
        {
            string cari = (_pencarian ?? string.Empty).Trim();

            IEnumerable<TemplateSuratKustom> pilihan = _semua;

            if (_filterSumber == FilterTersimpan)
            {
                pilihan = pilihan.Where(t => t.Id > 0);
            }
            else if (_filterSumber == FilterContoh)
            {
                pilihan = pilihan.Where(t => t.Id <= 0);
            }

            if (cari.Length > 0)
            {
                pilihan = pilihan.Where(t => Cocok(t, cari));
            }

            var hasil = Urutkan(pilihan).ToList();

            Items.Clear();
            foreach (var template in hasil)
            {
                Items.Add(template);
            }

            // Pilihan tidak boleh menunjuk baris yang sudah tersaring keluar.
            // Bila baris yang sama masih tampil, pakai objek barunya (MuatAsync
            // membuat objek baru dari database) agar sorotan tetap di tempat.
            if (_terpilih != null && !Items.Contains(_terpilih))
            {
                Terpilih = Items.FirstOrDefault(t => t.Id == _terpilih.Id);
            }

            PesanKosong = _semua.Count == 0
                ? "Belum ada template surat. Tekan \"Buat Template Baru\" untuk membuat jenis surat sendiri."
                : Items.Count > 0
                    ? string.Empty
                    : cari.Length > 0
                        ? $"Tidak ada template yang cocok dengan \"{cari}\". Coba kata kunci lain atau tekan Semua."
                        : $"Belum ada template pada pilihan {LabelFilter}. Tekan Semua untuk melihat seluruh template.";

            OnPropertyChanged(nameof(JumlahTotal));
            OnPropertyChanged(nameof(JumlahTampil));
            OnPropertyChanged(nameof(JumlahTersimpan));
            OnPropertyChanged(nameof(JumlahContoh));
            OnPropertyChanged(nameof(JumlahBerkolom));
            OnPropertyChanged(nameof(RingkasanTampil));
            OnPropertyChanged(nameof(PesanStatus));
        }

        private string LabelFilter => _filterSumber switch
        {
            FilterTersimpan => "Tersimpan",
            FilterContoh => "Contoh",
            _ => "Semua"
        };

        /// <summary>
        /// Urutkan hasil saringan. Daftar dari database sudah berurut nama, tetapi
        /// contoh yang belum tersimpan ditambahkan di akhir; pengurutan ulang di
        /// sini menyatukan keduanya sesuai pilihan pengguna.
        /// </summary>
        private IEnumerable<TemplateSuratKustom> Urutkan(IEnumerable<TemplateSuratKustom> daftar) => _urutan switch
        {
            UrutDiubah => daftar
                .OrderByDescending(t => t.Diubah)
                .ThenBy(t => t.NamaTampil, StringComparer.CurrentCultureIgnoreCase),
            UrutJudul => daftar
                .OrderBy(t => string.IsNullOrWhiteSpace(t.Judul) ? "~" : t.Judul, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.NamaTampil, StringComparer.CurrentCultureIgnoreCase),
            _ => daftar.OrderBy(t => t.NamaTampil, StringComparer.CurrentCultureIgnoreCase)
        };

        private void PilihFilter(string? nilai) => FilterSumber = nilai ?? FilterSemua;

        private static bool Cocok(TemplateSuratKustom template, string cari)
        {
            bool CocokDengan(string? teks) => !string.IsNullOrEmpty(teks) &&
                teks.Contains(cari, StringComparison.OrdinalIgnoreCase);

            return CocokDengan(template.Nama)
                || CocokDengan(template.Judul)
                || CocokDengan(template.Deskripsi)
                || CocokDengan(template.AwalanNomor)
                || CocokDengan(template.RingkasanSusunan);
        }


    }
}
