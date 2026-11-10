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
        // Daftar blok & kolom
        // =====================================================================

        private void TambahBlok()
        {
            Blok.Add(new BlokTeksItemViewModel());
            PesanInformasi = "Tulis isi blok teks pada kotak yang tersedia.";
        }

        private void TambahBlokUmum()
        {
            foreach (var (isi, rata, miring) in TemplateSuratPreset.BlokUmum)
            {
                Blok.Add(new BlokTeksItemViewModel(new BlokTeksTemplateSurat
                {
                    Isi = isi,
                    Rata = rata,
                    Miring = miring
                }));
            }
            PesanInformasi = "Dua kalimat baku surat keterangan ditambahkan — silakan ubah sesuai kebutuhan.";
        }

        private void HapusBlok(BlokTeksItemViewModel? blok)
        {
            if (blok == null) return;
            Blok.Remove(blok);
        }

        private void TambahKolom()
        {
            Kolom.Add(new KolomItemViewModel());
            PesanInformasi = "Beri nama kolom, pilih tipenya, lalu tandai bila wajib diisi.";
        }

        /// <summary>
        /// Tambahkan kolom identitas yang paling sering dipakai (NIK, nama, alamat,
        /// dan lainnya) ke daftar datar tanpa menimpa kolom yang sudah ada.
        /// </summary>
        private void TambahKolomUmum()
        {
            int ditambah = TambahKolomUmum(Kolom);
            PesanInformasi = ditambah == 0
                ? "Seluruh kolom umum sudah ada."
                : $"{ditambah} kolom umum ditambahkan.";
        }

        /// <summary>Tambahkan kolom umum ke sebuah bagian (kolom isian atau data diri).</summary>
        private void TambahKolomUmumBagian(BagianWizardItemViewModel? bagian)
        {
            var target = bagian switch
            {
                BagianKolomWizardItemViewModel kelompok => kelompok.Kolom,
                BagianDataDiriWizardItemViewModel dataDiri => dataDiri.Kolom,
                _ => null
            };
            if (target == null) return;

            int ditambah = TambahKolomUmum(target);
            PerbaruiCermin();
            PesanInformasi = ditambah == 0
                ? "Kolom umum sudah lengkap pada bagian ini."
                : $"{ditambah} kolom umum ditambahkan ke bagian ini.";
        }

        /// <summary>
        /// Tambahkan preset kolom umum ke koleksi target (bagian atau daftar datar),
        /// tanpa menimpa kolom yang labelnya sudah ada, dengan kunci unik menyeluruh.
        /// Mengembalikan jumlah kolom yang benar-benar ditambahkan.
        /// </summary>
        private int TambahKolomUmum(ObservableCollection<KolomItemViewModel> target)
        {
            int ditambah = 0;
            var kunciTerpakai = KunciKolomTerpakai();
            foreach (var preset in TemplateSuratPreset.KolomUmum)
            {
                if (target.Any(k => string.Equals((k.Label ?? string.Empty).Trim(), preset.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var kolom = TemplateSuratPreset.BuatKolom(preset, kunciTerpakai);
                kunciTerpakai.Add(kolom.Kunci);
                target.Add(new KolomItemViewModel(kolom));
                ditambah++;
            }
            return ditambah;
        }

        /// <summary>
        /// Seluruh kunci kolom yang terpakai saat ini (daftar datar + semua bagian) —
        /// dipakai agar preset kolom baru selalu mendapat kunci unik di seluruh template.
        /// </summary>
        private List<string> KunciKolomTerpakai()
        {
            var daftar = new List<string>();
            foreach (var k in Kolom)
            {
                if (!string.IsNullOrWhiteSpace(k.Kunci)) daftar.Add(k.Kunci);
            }
            foreach (var bagian in Bagian)
            {
                if (bagian is BagianKolomWizardItemViewModel kelompok)
                {
                    foreach (var k in kelompok.Kolom) if (!string.IsNullOrWhiteSpace(k.Kunci)) daftar.Add(k.Kunci);
                }
                else if (bagian is BagianDataDiriWizardItemViewModel dataDiri)
                {
                    foreach (var k in dataDiri.Kolom) if (!string.IsNullOrWhiteSpace(k.Kunci)) daftar.Add(k.Kunci);
                }
            }
            return daftar;
        }

        private void HapusKolom(KolomItemViewModel? kolom)
        {
            if (kolom == null) return;
            Kolom.Remove(kolom);
        }

        private static void Geser<T>(ObservableCollection<T> daftar, T? item, int arah)
        {
            if (item == null) return;
            int posisi = daftar.IndexOf(item);
            int tujuan = posisi + arah;
            if (posisi < 0 || tujuan < 0 || tujuan >= daftar.Count) return;
            daftar.Move(posisi, tujuan);
        }


    }
}
