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
        // ===== Aksi =====

        /// <summary>Buka wizard: baru, menyunting yang terpilih, atau menyalinnya.</summary>
        private void BukaWizard(int mode) =>
            BukaWizardDenganTemplate(mode, mode == ModeBaru ? null : Terpilih);

        /// <summary>
        /// Tampilkan wizard penyusunan template di area konten menu utama (bukan jendela
        /// terpisah). Selesai menyimpan atau batal akan mengembalikan pengguna ke halaman
        /// daftar Template Surat.
        /// </summary>
        private void BukaWizardDenganTemplate(int mode, TemplateSuratKustom? template)
        {
            try
            {
                var wizard = _wizardFactory(mode, template);

                // Bila pengguna memilih "Simpan & Isi Surat", navigasi diambil alih oleh
                // formulir pengisian — halaman wizard tidak dikembalikan ke daftar agar
                // tidak menimpa navigasi itu.
                var lanjutKePengisian = false;

                wizard.Selesai += (hasil, lanjutIsi) =>
                {
                    _ = MuatAsync();

                    if (lanjutIsi && hasil != null)
                    {
                        lanjutKePengisian = true;
                        _ = BukaPengisianAsync(hasil);
                    }
                };
                wizard.RequestClose += () =>
                {
                    if (!lanjutKePengisian)
                    {
                        _navigation.Navigate(this);
                    }
                };

                _navigation.Navigate(wizard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka wizard template surat (mode {Mode})", mode);
                Pesan.Galat("Gagal membuka wizard Template Surat",
                    "Penyebabnya: " + ex.Message, "wizard-template");
            }
        }

        private void BukaWizardBaru()
        {
            if (_penjaga is { BacaSaja: true })
            {
                _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }
            BukaWizard(ModeBaru);
        }

        /// <summary>
        /// Sunting satu template. Contoh aplikasi disalin lebih dulu supaya contoh
        /// aslinya tetap utuh dan salinannya bisa diubah seperti template biasa.
        /// </summary>
        private async Task BukaEditAsync(TemplateSuratKustom? target)
        {
            if (target == null) return;

            if (!_contohVirtual.TryGetValue(target.Id, out string? kode))
            {
                Terpilih = target;
                BukaWizardDenganTemplate(ModeEdit, target);
                return;
            }

            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            try
            {
                IsBusy = true;
                var tersalin = await _bawaanService.PasangAsync(new[] { kode });
                var template = tersalin.FirstOrDefault()
                    ?? (await _repository.GetAllAsync()).FirstOrDefault(t =>
                        string.Equals(t.NamaTampil, target.NamaTampil, StringComparison.OrdinalIgnoreCase));
                if (template == null)
                {
                    Pesan.Peringatan("Template tidak dapat disalin",
                        "Nama template contoh sudah dipakai template lain. Ubah nama template tersebut dahulu, lalu coba lagi.",
                        "salin-template-bawaan");
                    return;
                }

                var idVirtual = target.Id;
                _contohVirtual.Remove(idVirtual);
                _semua.RemoveAll(t => t.Id == idVirtual);
                _semua.Add(template);
                TerapkanFilter();
                Terpilih = template;
                BukaWizardDenganTemplate(ModeEdit, template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin template bawaan untuk diedit: {Kode}", kode);
                Pesan.Galat("Gagal menyalin template bawaan", ex.Message, "salin-template-bawaan");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Buka formulir pengisian untuk satu baris template — dipakai tombol cepat
        /// "Isi" pada kartu dan klik ganda baris, tanpa harus memilih lalu mencari
        /// tombol di bilah alat.
        /// </summary>
        public async Task BukaIsiAsync(TemplateSuratKustom? template)
        {
            if (template == null || IsBusy) return;
            Terpilih = template;
            await BukaPengisianAsync(template);
        }

        /// <summary>Buka pratinjau contoh surat untuk satu baris template.</summary>
        public async Task BukaPratinjauAsync(TemplateSuratKustom? template)
        {
            if (template == null || IsBusy) return;
            Terpilih = template;
            await PratinjauTemplateAsync(template);
        }

        /// <summary>Buka penyuntingan untuk satu baris template (contoh disalin dulu).</summary>
        public async Task BukaUbahAsync(TemplateSuratKustom? template)
        {
            if (template == null || IsBusy) return;
            Terpilih = template;
            await BukaEditAsync(template);
        }

        /// <summary>Isi surat dari baris yang sedang dipilih (Enter / klik ganda).</summary>
        public Task IsiTerpilihAsync() => BukaIsiAsync(Terpilih);

        private void BukaWizardDuplikat()
        {
            if (_penjaga is { BacaSaja: true })
            {
                _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }
            BukaWizard(ModeDuplikat);
        }

        private void TambahkanContohBelumTerpasang(IEnumerable<TemplateSuratKustom> tersimpan)
        {
            _contohVirtual.Clear();
            var namaTersimpan = new HashSet<string>(
                tersimpan.Select(t => t.NamaTampil.Trim()), StringComparer.OrdinalIgnoreCase);
            int idVirtual = -1;
            foreach (var contoh in TemplateSuratBawaan.Daftar)
            {
                if (namaTersimpan.Contains(contoh.Nama)) continue;
                var template = contoh.Salinan();
                template.Id = idVirtual;
                template.Dibuat = DateTime.MinValue;
                template.Diubah = DateTime.MinValue;
                _contohVirtual[idVirtual] = contoh.Kode;
                _semua.Add(template);
                idVirtual--;
            }

            if (_contohVirtual.Count > 0)
                InfoBawaan = "Contoh bawaan yang belum disalin tetap terlihat di daftar. Pilih lalu tekan Salin & Edit untuk membuat salinan yang dapat diubah.";
        }

        private async Task HapusAsync()
        {
            if (Terpilih == null || Terpilih.Id <= 0) return;

            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Hapus Template",
                $"Hapus template \"{Terpilih.NamaTampil}\"? Surat yang sudah pernah dicetak tidak terpengaruh, " +
                "tetapi template ini tidak bisa dikembalikan.");

            if (!lanjut) return;

            try
            {
                int id = Terpilih.Id;
                await _repository.DeleteAsync(id);
                Terpilih = null;
                await MuatAsync();
                _logger.LogInformation("Template surat #{Id} dihapus", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus template surat");
                Pesan.Galat("Gagal menghapus template",
                    "Template belum terhapus. Penyebabnya: " + ex.Message, "hapus-template");
            }
        }

        /// <summary>Buka formulir pengisian surat memakai template terpilih.</summary>
        private Task IsiAsync() => Terpilih == null ? Task.CompletedTask : BukaPengisianAsync(Terpilih);

        /// <summary>
        /// Tampilkan formulir pengisian untuk satu template. Tombol Batal pada
        /// formulir mengembalikan pengguna ke daftar template ini (bukan ke halaman kosong).
        /// </summary>
        private async Task BukaPengisianAsync(TemplateSuratKustom template)
        {
            try
            {
                var vm = _isiFactory();
                vm.SebelumBatal = () =>
                {
                    _ = MuatAsync();
                    _navigation.Navigate(this);
                };
                await vm.ConfigureAsync(template);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pengisian surat dari template");
                Pesan.Galat("Gagal membuka pengisian surat",
                    "Formulir isian template tidak dapat dibuka. Penyebabnya: " + ex.Message,
                    "isi-template");
            }
        }

        /// <summary>
        /// Buat contoh surat (memakai isi contoh) dari template terpilih supaya susunan
        /// surat dapat diperiksa sebelum benar-benar dipakai.
        /// </summary>
        private Task PratinjauAsync() =>
            Terpilih == null ? Task.CompletedTask : PratinjauTemplateAsync(Terpilih);

        private async Task PratinjauTemplateAsync(TemplateSuratKustom pilihan)
        {
            try
            {
                // Contoh virtual ber-Id negatif tidak ada di database; GetByIdAsync
                // mengembalikan null dan definisi dari daftar yang dipakai.
                var template = await _repository.GetByIdAsync(pilihan.Id) ?? pilihan;
                if (template.JumlahElemen == 0)
                {
                    Pesan.Peringatan("Template belum bisa dicetak",
                        "Template ini belum memiliki elemen yang bisa dicetak. Tambahkan elemennya lewat Edit dulu, ya.",
                        "pratinjau-template");
                    return;
                }

                var nilaiContoh = TemplateSuratNilai.NilaiContoh(template);
                int urut;
                string nomor = template.NomorBerikutnya(DateTime.Now, out urut);
                var pdf = await _generator.BuatPdfAsync(
                    template, nilaiContoh, nomor, DateTime.Now, pratinjau: true);

                if (pdf.Length == 0)
                {
                    Pesan.Peringatan("Pratinjau gagal dibuat",
                        "Berkas PDF-nya kosong, jadi pratinjau tidak dibuka.", "pratinjau-template");
                    return;
                }

                string berkas = await PratinjauPdf
                    .SimpanAsync((string?)null, pdf, "Pratinjau_Template")
                    .ConfigureAwait(true);

                _navigation.Navigate(_previewFactory($"Pratinjau template — {template.NamaTampil}", berkas));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau template surat");
                Pesan.Galat("Gagal membuat pratinjau template",
                    "Penyebabnya: " + ex.Message, "pratinjau-template");
            }
        }


    }
}
