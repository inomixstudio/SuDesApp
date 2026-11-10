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
        // Navigasi langkah
        // =====================================================================

        /// <summary>
        /// Pindah langkah dari penanda langkah (stepper). Mundur selalu boleh; maju
        /// memeriksa isi langkah sekarang lebih dulu agar galat tetap terlihat pada
        /// halaman yang salah. Mengklik langkah "Pratinjau" memeriksa semua langkah.
        /// </summary>
        private void PilihLangkah(LangkahItemViewModel? item)
        {
            if (item == null) return;
            int tujuan = item.Nomor - 1;

            if (tujuan == LangkahTerakhir)
            {
                _ = KePratinjauAsync();
                return;
            }

            if (tujuan > Langkah && !PeriksaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;
            Langkah = tujuan;
        }

        private void Kembali()
        {
            if (Langkah == 0) return;
            PesanKesalahan = string.Empty;
            Langkah--;
        }

        /// <summary>
        /// Validasi satu langkah. Sejak halaman dijadikan satu layar penyusunan,
        /// tombol "Lanjut ke Pratinjau" memvalidasi seluruh langkah sekaligus.
        /// </summary>
        private bool PeriksaSemuaLangkah(out string pesan)
        {
            int langkahSemula = Langkah;
            for (int i = 0; i < LangkahTerakhir; i++)
            {
                Langkah = i;
                if (!PeriksaLangkah(out pesan))
                {
                    Langkah = langkahSemula;
                    return false;
                }
            }
            Langkah = langkahSemula;
            pesan = string.Empty;
            return true;
        }

        private async Task KePratinjauAsync()
        {
            if (!PeriksaSemuaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;
            Langkah = LangkahTerakhir;
            await SegarkanPratinjauAsync();
        }

        private async Task LanjutAsync()
        {
            if (!PeriksaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;

            if (Langkah < LangkahTerakhir)
            {
                Langkah++;
                if (DiPratinjau)
                {
                    await SegarkanPratinjauAsync();
                }
                return;
            }

            // Di langkah pratinjau: simpan lalu lanjut ke pengisian surat.
            await SimpanAsync(lanjutIsi: true);
        }

        /// <summary>Periksa isi langkah saat ini sebelum berpindah.</summary>
        private bool PeriksaLangkah(out string pesan)
        {
            pesan = string.Empty;

            switch (Langkah)
            {
                case 0:
                    if (string.IsNullOrWhiteSpace(Nama))
                    {
                        pesan = "Nama template wajib diisi — nama inilah yang tampil pada daftar Template Surat.";
                        return false;
                    }
                    break;

                case 1:
                    if (!PakaiKop && !PakaiJudul && !PakaiNomor && !PakaiTempatTanggal)
                    {
                        pesan = "Pilih minimal satu komponen: kop, judul, nomor surat, atau tempat & tanggal.";
                        return false;
                    }
                    if (PakaiNomor)
                    {
                        if (!TemplateSuratNomor.AwalanValid(AwalanNomor, out var pesanAwalan))
                        {
                            pesan = pesanAwalan;
                            return false;
                        }
                        if (!TemplateSuratNomor.PolaValid(PolaNomor, out var pesanPola))
                        {
                            pesan = pesanPola;
                            return false;
                        }
                    }
                    break;

                // Langkah "Isi Surat": blok teks DAN kolom isian diperiksa sekaligus karena
                // keduanya disusun pada satu halaman yang sama.
                case 2:
                    if (PakaiBlok)
                    {
                        if (Bagian.Count > 0)
                        {
                            int nomorTeks = 0;
                            foreach (var bagian in Bagian.OfType<BagianTeksWizardItemViewModel>())
                            {
                                nomorTeks++;
                                if (string.IsNullOrWhiteSpace(bagian.Teks.Isi))
                                {
                                    pesan = $"Blok teks ke-{nomorTeks} masih kosong. Isi teksnya atau hapus blok tersebut.";
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < Blok.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(Blok[i].Isi))
                                {
                                    pesan = $"Blok teks ke-{i + 1} masih kosong. Isi teksnya atau hapus blok tersebut.";
                                    return false;
                                }
                            }
                        }
                    }

                    if (!PakaiKolom) break;
                    if (Bagian.Count > 0)
                    {
                        if (!Bagian.OfType<BagianKolomWizardItemViewModel>().Any() &&
                            !Bagian.OfType<BagianDataDiriWizardItemViewModel>().Any())
                        {
                            pesan = "Tambahkan bagian Kolom Isian atau Data Diri agar surat bisa diisi saat dibuat.";
                            return false;
                        }

                        int nomorBagian = 0;
                        foreach (var bagian in Bagian)
                        {
                            if (bagian is not BagianKolomWizardItemViewModel kelompok &&
                                bagian is not BagianDataDiriWizardItemViewModel)
                            {
                                continue;
                            }

                            nomorBagian++;
                            var daftar = bagian is BagianKolomWizardItemViewModel kb
                                ? kb.Kolom
                                : ((BagianDataDiriWizardItemViewModel)bagian).Kolom;

                            for (int i = 0; i < daftar.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(daftar[i].Label))
                                {
                                    pesan = $"Bagian {nomorBagian}: kolom ke-{i + 1} belum diberi nama.";
                                    return false;
                                }
                                if (daftar[i].PakaiPilihan && daftar[i].PilihanBersih().Count == 0)
                                {
                                    pesan = $"Bagian {nomorBagian}: kolom \"{daftar[i].Label}\" bertipe Pilihan, tetapi daftar pilihannya belum diisi (pisahkan dengan koma).";
                                    return false;
                                }
                            }

                            // Label boleh kembar antar-bagian, tetapi tidak boleh dalam satu kelompok.
                            var kembar = daftar
                                .GroupBy(k => (k.Label ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                                .FirstOrDefault(g => g.Count() > 1);
                            if (kembar != null)
                            {
                                pesan = $"Bagian {nomorBagian}: nama kolom \"{kembar.Key}\" dipakai lebih dari satu kali di bagian ini.";
                                return false;
                            }
                        }
                        break;
                    }

                    if (Kolom.Count == 0)
                    {
                        pesan = "Tambahkan minimal satu kolom isian, misalnya NIK atau Nama.";
                        return false;
                    }
                    for (int i = 0; i < Kolom.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(Kolom[i].Label))
                        {
                            pesan = $"Kolom ke-{i + 1} belum diberi nama.";
                            return false;
                        }
                        if (Kolom[i].PakaiPilihan && Kolom[i].PilihanBersih().Count == 0)
                        {
                            pesan = $"Kolom \"{Kolom[i].Label}\" bertipe Pilihan, tetapi daftar pilihannya belum diisi (pisahkan dengan koma).";
                            return false;
                        }
                    }

                    var kembarFlat = Kolom
                        .GroupBy(k => (k.Label ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(g => g.Count() > 1);
                    if (kembarFlat != null)
                    {
                        pesan = $"Nama kolom \"{kembarFlat.Key}\" dipakai lebih dari satu kali. Samakan hanya untuk keperluan berbeda.";
                        return false;
                    }
                    break;

                case 3:
                    if (!PakaiTandaTangan && !PakaiKop && !PakaiJudul)
                    {
                        pesan = "Surat harus memiliki kop, judul, atau tanda tangan agar tetap dikenali sebagai surat resmi.";
                        return false;
                    }
                    if (PakaiTeksKaki && string.IsNullOrWhiteSpace(TeksKaki))
                    {
                        pesan = "Teks kaki sudah dipilih, tetapi isinya masih kosong.";
                        return false;
                    }
                    break;
            }

            var susunan = TemplateDariForm();
            if (susunan.JumlahElemen == 0)
            {
                pesan = "Belum ada elemen surat yang dipilih.";
                return false;
            }

            return true;
        }


    }
}
