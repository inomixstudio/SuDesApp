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
        // Simpan
        // =====================================================================

        private async Task SimpanAsync(bool lanjutIsi)
        {
            if (IsBusy) return;

            // Mode baca-saja: simpan template baru diblokir, tetapi edit template
            // yang sudah ada tetap boleh — pengguna bisa memperbaiki template lama.
            if (_idTersunting == 0 && _penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            if (string.IsNullOrWhiteSpace(Nama))
            {
                Langkah = 0;
                PesanKesalahan = "Nama template wajib diisi.";
                return;
            }

            var template = TemplateDariForm();

            // Periksa seluruh isian sekaligus sebelum menyimpan.
            if (!PeriksaSemuaLangkah(out var pesanValidasi))
            {
                PesanKesalahan = pesanValidasi;
                return;
            }

            // Nama template tidak boleh sama dengan template lain agar tidak tertukar di daftar.
            if (await _repository.NamaTerpakaiAsync(template.NamaTampil, _idTersunting))
            {
                Langkah = 0;
                PesanKesalahan = $"Nama \"{template.NamaTampil}\" sudah dipakai template lain. " +
                    $"Pilih nama lain, misalnya \"{template.NamaTampil} — {DateTime.Now.Year}\".";
                return;
            }

            IsBusy = true;
            try
            {
                if (_idTersunting > 0)
                {
                    template.Id = _idTersunting;
                    template.NomorTerakhir = _nomorTerakhir;
                    template.TahunNomor = _tahunNomor;
                    template.Dibuat = _dibuat;
                    template.DibuatOleh = _dibuatOleh;
                    await _repository.UpdateAsync(template);
                }
                else
                {
                    template.Dibuat = DateTime.Now;
                    await _repository.AddAsync(template);
                    _idTersunting = template.Id;
                }

                _logger.LogInformation("Template surat disimpan: #{Id} {Nama}", template.Id, template.NamaTampil);
                Selesai?.Invoke(template, lanjutIsi);
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan template surat");
                PesanKesalahan = "Gagal menyimpan template: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Susun definisi template dari isian wizard.</summary>
        private TemplateSuratKustom TemplateDariForm()
        {
            var kunciTerpakai = new List<string>();
            var blok = _pakaiBlok
                ? Blok.Select(b => b.KeModel()).Where(b => b.Isi.Length > 0).ToList()
                : new List<BlokTeksTemplateSurat>();

            var kolom = new List<KolomTemplateSurat>();
            var bagian = new List<BagianTemplateSurat>();

            if (Bagian.Count > 0)
            {
                // Susunan baru: badan surat dari bagian terurut (teks / data diri / kolom).
                foreach (var item in Bagian)
                {
                    if (item is BagianTeksWizardItemViewModel teks)
                    {
                        var model = teks.Teks.KeModel();
                        if (model.Isi.Length == 0) continue;

                        blok.Add(model);
                        bagian.Add(new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.Teks,
                            Isi = model.Isi,
                            Rata = model.Rata,
                            Tebal = model.Tebal,
                            Miring = model.Miring
                        });
                    }
                    else if (item is BagianDataDiriWizardItemViewModel dataDiri)
                    {
                        var kelompok = new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.DataDiri,
                            JudulKelompok = (dataDiri.Judul ?? string.Empty).Trim(),
                            Kunci = dataDiri.Kunci
                        };
                        foreach (var kolomItem in dataDiri.Kolom)
                        {
                            var model = kolomItem.KeModel(kunciTerpakai);
                            if (string.IsNullOrWhiteSpace(model.Label)) continue;

                            kunciTerpakai.Add(model.Kunci);
                            model.Kelompok = kelompok.JudulKelompok;
                            kelompok.Kolom.Add(model);
                            kolom.Add(model);
                        }
                        if (kelompok.Kolom.Count > 0)
                        {
                            bagian.Add(kelompok);
                        }
                    }
                    else if (item is BagianKolomWizardItemViewModel kelompokWizard)
                    {
                        var kelompok = new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.Kolom,
                            JudulKelompok = (kelompokWizard.Judul ?? string.Empty).Trim(),
                            Kunci = kelompokWizard.Kunci,
                            Grid = kelompokWizard.Grid
                        };
                        foreach (var kolomItem in kelompokWizard.Kolom)
                        {
                            var model = kolomItem.KeModel(kunciTerpakai);
                            if (string.IsNullOrWhiteSpace(model.Label)) continue;

                            kunciTerpakai.Add(model.Kunci);
                            model.Kelompok = kelompok.JudulKelompok;
                            kelompok.Kolom.Add(model);
                            kolom.Add(model);
                        }
                        if (kelompok.Kolom.Count > 0)
                        {
                            bagian.Add(kelompok);
                        }
                    }
                }

                // Kolom daftar datar yang TIDAK berada di bagian mana pun (mis. ditambah
                // lewat jalur lama "Tambah Kolom") tetap digabung menjadi satu bagian
                // kolom terusan di urutan akhir — tidak dibuang diam-diam.
                var kolomSudah = kolom.Select(k => k.Kunci)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sisa = new List<KolomTemplateSurat>();
                foreach (var item in Kolom)
                {
                    var model = item.KeModel(kunciTerpakai);
                    if (string.IsNullOrWhiteSpace(model.Label) || kolomSudah.Contains(model.Kunci))
                    {
                        continue;
                    }
                    kolomSudah.Add(model.Kunci);
                    kunciTerpakai.Add(model.Kunci);
                    sisa.Add(model);
                }
                if (sisa.Count > 0)
                {
                    var kelompokTambahan = new BagianTemplateSurat
                    {
                        Tipe = TipeBagianTemplate.Kolom,
                        JudulKelompok = string.Empty,
                        Kunci = "kolomtambahan",
                        Grid = _pakaiGrid
                    };
                    kelompokTambahan.Kolom.AddRange(sisa);
                    bagian.Add(kelompokTambahan);
                    kolom.AddRange(sisa);
                }
            }
            else
            {
                // Template lama tanpa Bagian: pertahankan persis perilaku semula.
                foreach (var item in Kolom)
                {
                    if (!_pakaiKolom) break;
                    var model = item.KeModel(kunciTerpakai);
                    kunciTerpakai.Add(model.Kunci);
                    kolom.Add(model);
                }
            }

            return new TemplateSuratKustom
            {
                Id = _idTersunting,
                Nama = (_nama ?? string.Empty).Trim(),
                Deskripsi = (_deskripsi ?? string.Empty).Trim(),
                FormatContoh = _formatContoh,
                VersiFormat = _versiFormat,
                PakaiKop = _pakaiKop,
                Judul = _pakaiJudul ? (_judul ?? string.Empty).Trim() : string.Empty,
                SubJudul = _pakaiJudul ? (_subJudul ?? string.Empty).Trim() : string.Empty,
                PakaiNomor = _pakaiNomor,
                AwalanNomor = (_awalanNomor ?? string.Empty).Trim(),
                PolaNomor = string.IsNullOrWhiteSpace(_polaNomor) ? TemplateSuratNomor.PolaBawaan : _polaNomor.Trim(),
                PakaiTempatTanggal = _pakaiTempatTanggal,
                Blok = blok,
                Kolom = kolom,
                Bagian = bagian,
                PakaiGrid = _pakaiGrid,
                PakaiTandaTangan = _pakaiTandaTangan,
                JabatanPenandatangan = (_jabatanPenandatangan ?? string.Empty).Trim(),
                PakaiTeksKaki = _pakaiTeksKaki,
                TeksKaki = (_teksKaki ?? string.Empty).Trim(),
                NomorManual = _nomorManual,
                PenandatanganDariDataDiri = _penandatanganDariDataDiri,
                KunciPenandatanganDataDiri = (_kunciPenandatanganDataDiri ?? string.Empty).Trim(),
                NomorTerakhir = _nomorTerakhir,
                TahunNomor = _tahunNomor,
                Dibuat = _dibuat,
                DibuatOleh = _dibuatOleh
            };
        }

        /// <summary>Perbarui ketersediaan tombol setelah perubahan status.</summary>
        private void RaisePerintah()
        {
            LanjutCommand.RaiseCanExecuteChanged();
            KePratinjauCommand.RaiseCanExecuteChanged();
            SimpanCommand.RaiseCanExecuteChanged();
            SimpanDanIsiCommand.RaiseCanExecuteChanged();
            SegarkanPratinjauCommand.RaiseCanExecuteChanged();
            KembaliCommand.RaiseCanExecuteChanged();
            ((RelayCommand)TambahBlokCommand).RaiseCanExecuteChanged();
        }

    }
}
