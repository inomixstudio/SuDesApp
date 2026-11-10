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
        // Pemuatan data
        // =====================================================================

        /// <summary>
        /// Isi wizard untuk template baru, salin dari template yang ada (duplikat), atau
        /// dari hasil pembacaan berkas Word (<paramref name="dariImporWord"/>: nama dan
        /// susunan hasil pembacaan dibiarkan apa adanya).
        /// </summary>
        public void SiapkanBaru(TemplateSuratKustom? salinan = null, bool dariImporWord = false)
        {
            _idTersunting = 0;
            if (salinan != null)
            {
                TerapkanKeForm(salinan);

                // Nomor selalu mulai dari awal untuk template baru.
                _nomorTerakhir = 0;
                _tahunNomor = 0;
                _dibuat = default;
                _dibuatOleh = string.Empty;

                if (!dariImporWord)
                {
                    // Duplikat: namanya diberi penanda agar tidak tertukar dengan aslinya.
                    Nama = string.IsNullOrWhiteSpace(Nama) ? "Salinan template" : Nama + " (salinan)";
                }
            }

            Langkah = 0;
            PesanKesalahan = string.Empty;
            PesanInformasi = dariImporWord
                ? "Susunan surat hasil pembacaan berkas Word. Periksa tiap langkah, lalu simpan."
                : "Pilih komponen surat dengan kotak centang, lalu tekan Lanjut ke Pratinjau.";

            if (!dariImporWord && Bagian.Count == 0 && Kolom.Count == 0)
            {
                // Template baru langsung diisi kolom umum sebagai bagian "Kolom Isian"
                // — bukan daftar datar — supaya kolom itu tidak hilang diam-diam bila
                // pengguna kemudian menambahkan bagian teks / data diri / kolom lain.
                var kelompok = new BagianKolomWizardItemViewModel(string.Empty, "kolom");
                TambahKolomUmum(kelompok.Kolom);
                Bagian.Add(kelompok);
                PerbaruiCermin();
            }
            PerbaruiLencana();
        }

        /// <summary>Muat template yang sudah ada untuk disunting.</summary>
        public async Task MuatAsync(int id)
        {
            try
            {
                var template = await _repository.GetByIdAsync(id);
                if (template == null)
                {
                    await _messageService.ShowWarningAsync("Template tidak ditemukan.");
                    RequestClose?.Invoke();
                    return;
                }

                _idTersunting = template.Id;
                _nomorTerakhir = template.NomorTerakhir;
                _tahunNomor = template.TahunNomor;
                _dibuat = template.Dibuat;
                _dibuatOleh = template.DibuatOleh;
                TerapkanKeForm(template);

                Langkah = 0;
                PesanKesalahan = string.Empty;
                PesanInformasi = "Ubah bagian yang perlu, lalu simpan kembali.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat template surat #{Id}", id);
                await _messageService.ShowErrorAsync($"Gagal memuat template: {ex.Message}");
                RequestClose?.Invoke();
            }
        }

        private void TerapkanKeForm(TemplateSuratKustom template)
        {
            Nama = template.Nama;
            Deskripsi = template.Deskripsi;
            FormatContoh = template.FormatContoh;
            VersiFormat = template.VersiFormat;
            PakaiKop = template.PakaiKop;
            PakaiJudul = !string.IsNullOrWhiteSpace(template.Judul);
            Judul = template.Judul;
            SubJudul = template.SubJudul;
            PakaiNomor = template.PakaiNomor;
            AwalanNomor = template.AwalanNomor;
            PolaNomor = string.IsNullOrWhiteSpace(template.PolaNomor) ? TemplateSuratNomor.PolaBawaan : template.PolaNomor;
            PakaiTempatTanggal = template.PakaiTempatTanggal;
            PakaiGrid = template.PakaiGrid;
            PakaiTandaTangan = template.PakaiTandaTangan;
            JabatanPenandatangan = template.JabatanPenandatangan;
            PakaiTeksKaki = template.PakaiTeksKaki;
            TeksKaki = template.TeksKaki;
            NomorManual = template.NomorManual;
            PenandatanganDariDataDiri = template.PenandatanganDariDataDiri;
            KunciPenandatanganDataDiri = template.KunciPenandatanganDataDiri ?? string.Empty;

            Bagian.Clear();
            if (template.AdaBagian)
            {
                foreach (var bagian in template.Bagian)
                {
                    if (bagian.Tipe == TipeBagianTemplate.Teks)
                    {
                        Bagian.Add(new BagianTeksWizardItemViewModel(new BlokTeksItemViewModel(new BlokTeksTemplateSurat
                        {
                            Isi = bagian.Isi ?? string.Empty,
                            Rata = bagian.Rata,
                            Tebal = bagian.Tebal,
                            Miring = bagian.Miring
                        })));
                    }
                    else if (bagian.Tipe == TipeBagianTemplate.DataDiri)
                    {
                        var dataDiri = new BagianDataDiriWizardItemViewModel(bagian.JudulKelompok ?? string.Empty, bagian.Kunci ?? string.Empty);
                        foreach (var kolom in bagian.Kolom ?? new List<KolomTemplateSurat>())
                        {
                            dataDiri.Kolom.Add(new KolomItemViewModel(kolom));
                        }
                        Bagian.Add(dataDiri);
                    }
                    else
                    {
                        var kelompok = new BagianKolomWizardItemViewModel(bagian.JudulKelompok ?? string.Empty, bagian.Kunci ?? string.Empty)
                        {
                            Grid = bagian.Grid
                        };
                        foreach (var kolom in bagian.Kolom ?? new List<KolomTemplateSurat>())
                        {
                            kelompok.Kolom.Add(new KolomItemViewModel(kolom));
                        }
                        Bagian.Add(kelompok);
                    }
                }
                PerbaruiCermin();
            }

            Blok.Clear();
            foreach (var blok in template.Blok ?? new List<BlokTeksTemplateSurat>())
            {
                Blok.Add(new BlokTeksItemViewModel(blok));
            }
            PakaiBlok = Blok.Count > 0;

            Kolom.Clear();
            foreach (var kolom in template.Kolom ?? new List<KolomTemplateSurat>())
            {
                Kolom.Add(new KolomItemViewModel(kolom));
            }
            PakaiKolom = Kolom.Count > 0;

            if (Bagian.Count == 0)
            {
                // Template lama: susun ulang dari daftar datar supaya tetap bisa disunting
                // sebagai bagian berurutan tanpa mengubah isi.
                Bagian.Clear();
                foreach (var blok in Blok)
                {
                    Bagian.Add(new BagianTeksWizardItemViewModel(blok));
                }
                if (Kolom.Count > 0)
                {
                    Bagian.Add(new BagianKolomWizardItemViewModel(string.Empty, string.Empty) { Grid = template.PakaiGrid }.DenganKolom(Kolom));
                }
                PerbaruiCermin();
            }
        }


    }
}
