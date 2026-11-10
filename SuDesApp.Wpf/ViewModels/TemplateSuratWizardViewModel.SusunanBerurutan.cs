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
        // Susunan berurutan (bagian)
        // =====================================================================

        private void TambahBagianTeks()
        {
            var teks = new BlokTeksItemViewModel();
            Bagian.Add(new BagianTeksWizardItemViewModel(teks));
            PerbaruiCermin();
            PesanInformasi = "Blok teks ditambahkan pada urutan terakhir — tulis isinya pada kotak yang tersedia.";
        }

        /// <summary>Bagian Kolom Isian kosong: kolomnya ditambah satu-satu oleh pengguna.</summary>
        private void TambahBagianKolom()
        {
            int urutan = Bagian.Count(b => b is BagianKolomWizardItemViewModel);
            var kelompok = new BagianKolomWizardItemViewModel(string.Empty, $"kolom{urutan}");
            Bagian.Add(kelompok);
            PerbaruiCermin();
            PesanInformasi = "Bagian kolom isian ditambahkan — tambahkan kolom yang harus diisi saat membuat surat.";
        }

        /// <summary>Data diri ringkas: hanya NIK, Nama, Jabatan.</summary>
        private void TambahBagianDataDiriRingkas()
        {
            int urutan = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            var kunci = $"datadiri{urutan}";
            var bagian = new BagianDataDiriWizardItemViewModel($"Data Diri {urutan + 1}", kunci);
            foreach (var preset in TemplateSuratPreset.DataDiri.NamaJabatan)
            {
                bagian.Kolom.Add(new KolomItemViewModel(BuatDataDiriKolom(kunci, preset)));
            }
            Bagian.Add(bagian);
            PerbaruiCermin();
            PesanInformasi = "Blok data diri ditambahkan — pilih orangnya saat mengisi surat.";
        }

        /// <summary>Data diri lengkap (NIK, Nama, Jabatan, TTL, Alamat, dan lainnya).</summary>
        private void TambahBagianDataDiri()
        {
            int urutan = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            var kunci = $"datadiri{urutan}";
            var bagian = new BagianDataDiriWizardItemViewModel($"Data Diri {urutan + 1}", kunci);
            foreach (var preset in TemplateSuratPreset.DataDiri.Lengkap)
            {
                bagian.Kolom.Add(new KolomItemViewModel(BuatDataDiriKolom(kunci, preset)));
            }
            Bagian.Add(bagian);
            PerbaruiCermin();
            PesanInformasi = "Blok data diri lengkap ditambahkan — seluruh field identitas dicetak.";
        }

        private static KolomTemplateSurat BuatDataDiriKolom(string kunci, TemplateSuratPreset.FieldDataDiri preset)
        {
            return new KolomTemplateSurat
            {
                Label = preset.Label,
                Tipe = preset.Tipe,
                Wajib = preset.Wajib,
                Pilihan = preset.Pilihan == null ? new List<string>() : new List<string>(preset.Pilihan),
                Kunci = $"{kunci}_{TemplateSuratKunci.Slug(preset.Label)}",
                Kelompok = "Data Diri"
            };
        }

        private void HapusBagian(BagianWizardItemViewModel? bagian)
        {
            if (bagian == null) return;
            Bagian.Remove(bagian);
            PerbaruiCermin();
        }

        private void TambahKolomBagian(BagianWizardItemViewModel? bagian)
        {
            var kolomItem = new KolomItemViewModel();
            if (bagian is BagianKolomWizardItemViewModel kelompok)
            {
                kelompok.Kolom.Add(kolomItem);
            }
            else if (bagian is BagianDataDiriWizardItemViewModel dataDiri)
            {
                dataDiri.Kolom.Add(kolomItem);
            }
            else
            {
                return;
            }
            PesanInformasi = "Kolom ditambahkan pada bagian ini — beri nama dan tipenya.";
        }

        private void HapusKolomBagian(KolomItemViewModel? kolom)
        {
            if (kolom == null) return;
            var bagian = Bagian
                .FirstOrDefault(b => b is BagianKolomWizardItemViewModel kelompok
                    ? kelompok.Kolom.Contains(kolom)
                    : b is BagianDataDiriWizardItemViewModel dataDiri && dataDiri.Kolom.Contains(kolom));
            switch (bagian)
            {
                case BagianKolomWizardItemViewModel kelompok:
                    kelompok.Kolom.Remove(kolom);
                    break;
                case BagianDataDiriWizardItemViewModel dataDiri:
                    dataDiri.Kolom.Remove(kolom);
                    break;
            }
            PerbaruiCermin();
        }

        private void GeserKolomBagian(KolomItemViewModel? kolom, int arah)
        {
            if (kolom == null) return;
            var bagian = Bagian
                .FirstOrDefault(b => b is BagianKolomWizardItemViewModel kelompok
                    ? kelompok.Kolom.Contains(kolom)
                    : b is BagianDataDiriWizardItemViewModel dataDiri && dataDiri.Kolom.Contains(kolom));
            switch (bagian)
            {
                case BagianKolomWizardItemViewModel kelompok:
                    Geser(kelompok.Kolom, kolom, arah);
                    break;
                case BagianDataDiriWizardItemViewModel dataDiri:
                    Geser(dataDiri.Kolom, kolom, arah);
                    break;
            }
        }

        /// <summary>
        /// Jagalah daftar datar <see cref="Blok"/> dan <see cref="Kolom"/> tetap sejalan
        /// dengan susunan <see cref="Bagian"/> supaya alur lama (cetak, isian, ringkasan)
        /// tetap bekerja tanpa membaca bagian satuan.
        /// </summary>
        private void PerbaruiCermin()
        {
            Blok.Clear();
            foreach (var bagian in Bagian.OfType<BagianTeksWizardItemViewModel>())
            {
                Blok.Add(bagian.Teks);
            }

            Kolom.Clear();
            foreach (var kolom in Bagian
                .OfType<BagianKolomWizardItemViewModel>()
                .SelectMany(b => b.Kolom)
                .Concat(Bagian.OfType<BagianDataDiriWizardItemViewModel>().SelectMany(b => b.Kolom)))
            {
                Kolom.Add(kolom);
            }

            OnPropertyChanged(nameof(DaftarPenandatangan));
            PerbaruiLencana();
        }


    }
}
