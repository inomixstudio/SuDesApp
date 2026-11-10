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
        // =====================================================================
        // Buat template dari berkas Word
        // =====================================================================

        /// <summary>
        /// Buat template surat dari berkas Word yang sudah dipakai sehari-hari: .docx
        /// langsung dibaca, berkas .doc lama dikonversi otomatis lebih dulu (Microsoft Word).
        ///
        /// Berkas yang memuat satu bentuk surat langsung dibuka di wizard (mode impor)
        /// supaya susunannya bisa diperiksa sebelum disimpan. Berkas yang memuat beberapa
        /// bentuk surat (beberapa halaman/bagian) menampilkan dialog pemilihan, sehingga
        /// bagian yang dipilih dapat disimpan sekaligus sebagai beberapa template.
        /// </summary>
        private async Task BuatDariWordAsync()
        {
            if (IsBusy) return;

            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pilih berkas Word untuk dijadikan template surat",
                    Filter = "Dokumen Word|*.docx;*.doc|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|Semua Berkas (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog() != true) return;

                IsBusy = true;
                var hasil = await _wordImpor.BacaAsync(dialog.FileName);

                // Pembacaan selesai: halaman bebas lagi supaya daftar bisa dimuat ulang
                // setelah template hasil pembacaan disimpan.
                IsBusy = false;

                if (hasil.Kandidat.Count == 0)
                {
                    string catatan = hasil.Peringatan.Count == 0
                        ? string.Empty
                        : "\n\n" + string.Join("\n", hasil.Peringatan);
                    Pesan.Peringatan("Tidak ada surat yang bisa dibaca",
                        "Pastikan isi berkas berupa teks, bukan gambar atau hasil pindai." + catatan,
                        "impor-word");
                    return;
                }

                var namaTerpakai = _semua.Select(t => t.NamaTampil).ToList();

                // Satu surat saja: buka wizard supaya pengguna bisa memeriksa dan menyimpan.
                if (hasil.Kandidat.Count == 1)
                {
                    var kandidat = hasil.Kandidat[0];
                    kandidat.Definisi.Nama = TemplateSuratWordImporService.NamaUnik(
                        kandidat.Definisi.Nama, namaTerpakai);
                    BukaWizardDenganTemplate(ModeImpor, kandidat.Definisi);
                    return;
                }

                // Beberapa surat dalam satu berkas: pengguna memilih bagian yang disimpan.
                // Setiap bagian bisa dipratinjau (halaman pertama) sebelum diputuskan.
                var terpilih = WordImporWindow.Show(
                    hasil, namaTerpakai, template => _pratinjauService.BuatAsync(template));
                if (terpilih == null || terpilih.Count == 0) return;

                int berhasil = 0;
                var gagal = new List<string>();
                foreach (var (nama, template) in terpilih)
                {
                    try
                    {
                        template.Dibuat = DateTime.Now;
                        await _repository.AddAsync(template);
                        berhasil++;
                        _logger.LogInformation("Template surat dari berkas Word dibuat: {Nama}", nama);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyimpan template hasil pembacaan berkas Word: {Nama}", nama);
                        gagal.Add($"{nama}: {ex.Message}");
                    }
                }

                await MuatAsync();

                if (gagal.Count > 0)
                {
                    Pesan.Peringatan("Sebagian template gagal disimpan",
                        $"{berhasil} template dibuat, {gagal.Count} gagal disimpan:\n\n" + string.Join("\n", gagal),
                        "impor-word");
                    return;
                }

                Pesan.Sukses("Template dari berkas Word dibuat",
                    $"{berhasil} template surat dibuat dari berkas \"{hasil.NamaBerkas}\".\n\n" +
                    "Pilih salah satu pada daftar: tekan Edit untuk menyesuaikan susunannya, atau " +
                    "Isi Surat untuk langsung mencetak.",
                    "impor-word");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat template surat dari berkas Word");
                Pesan.Galat("Gagal membaca berkas Word",
                    "Pastikan berkasnya berformat .doc/.docx, tidak rusak, dan tidak sedang dibuka di " +
                    "aplikasi lain.\n\n" + ex.Message,
                    "impor-word");
            }
            finally
            {
                IsBusy = false;
            }
        }


    }
}
