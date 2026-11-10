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
        /// <summary>Membuka berkas lampiran: yang baru dipilih (Word/PDF) atau yang tersimpan di arsip.</summary>
        private async Task OpenAttachedFileAsync()
        {
            string? target = null;
            if (!string.IsNullOrWhiteSpace(_wordFilePath)) target = _wordFilePath;
            else if (!string.IsNullOrWhiteSpace(_pdfFilePath)) target = _pdfFilePath;
            else if (!string.IsNullOrWhiteSpace(_storedFileName)) target = _repository.ResolveWordFullPath(_storedFileName);

            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            {
                await _messageService.ShowWarningAsync("Berkas tidak ditemukan.");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka berkas: {Path}", target);
                await _messageService.ShowErrorAsync("Gagal membuka berkas: " + ex.Message);
            }
        }

        private async Task SaveAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            try
            {
                var errors = Validate();
                if (errors.Count > 0)
                {
                    await _messageService.ShowWarningAsync(
                        "Data belum lengkap atau format salah:\n\n" + string.Join("\n", errors));
                    return;
                }

                // Validasi nomor duplikat untuk jenis arsip yang sama (SK/PERDES/PERKADES),
                // kecuali data yang sedang diedit sendiri.
                string nomorBaru = Nomor.Trim();
                var semua = await _repository.GetAllAsync();
                bool duplikat = semua.Any(k =>
                    k.JenisKeputusan.Equals(_jenisKeputusan, StringComparison.OrdinalIgnoreCase) &&
                    k.Nomor.Equals(nomorBaru, StringComparison.OrdinalIgnoreCase) &&
                    k.IdBarisExcel != (_editData?.IdBarisExcel ?? 0));
                if (duplikat)
                {
                    await _messageService.ShowWarningAsync(
                        $"Nomor '{nomorBaru}' sudah terdaftar untuk {LabelJenis(_jenisKeputusan)}.\n\n" +
                        "Gunakan nomor yang berbeda, atau ubah data lama melalui tombol Edit di daftar.");
                    return;
                }

                // PERDES adalah dokumen naskah: baris tanpa halaman menghasilkan
                // arsip tanpa PDF yang tidak bisa dibuka/dicetak. Konfirmasikan
                // bila pengguna memang ingin menyimpan metadata lebih dulu.
                if (IsPerdes && !HasNaskah)
                {
                    bool lanjut = await _messageService.ShowConfirmationAsync(
                        "Simpan Tanpa Naskah?",
                        "PERDES belum memiliki halaman naskah, sehingga PDF tidak akan dibuat dan baris arsip tampil tanpa berkas.\n\nTetap simpan datanya?");
                    if (!lanjut) return;
                }

                // PERDES: PDF diterbitkan dari naskah halaman SEBELUM data disimpan.
                // Bila generator gagal, tidak ada baris arsip setengah jadi.
                byte[]? pdfNaskah = null;
                if (IsPerdes && HasNaskah)
                {
                    try
                    {
                        var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true) ?? new DesaData();
                        pdfNaskah = new NaskahPerdesGenerator(
                            desa, AmbilNaskah(), Nomor, ParseDate(Tanggal)!.Value).Buat();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal membuat PDF naskah Perdes");
                        await _messageService.ShowErrorAsync(
                            "PDF naskah gagal dibuat, data belum disimpan:\n\n" + ex.Message);
                        return;
                    }
                }

                var item = new DataKeputusan
                {
                    JenisKeputusan = _jenisKeputusan,
                    Nomor = Nomor.Trim(),
                    Tanggal = ParseDate(Tanggal)!.Value,
                    Tentang = Tentang.Trim(),
                    Keterangan = Keterangan.Trim(),
                    FileWord = _editData?.FileWord
                };
                string? berkasLama = item.FileWord;

                if (_editData != null)
                {
                    item.IdBarisExcel = _editData.IdBarisExcel;
                    await _repository.UpdateAsync(item);
                    _logger.LogInformation("Arsip keputusan diedit: Id={Id}", item.IdBarisExcel);
                }
                else
                {
                    await _repository.AddAsync(item);
                    _logger.LogInformation("Arsip keputusan ditambahkan: Nomor={Nomor} Jenis={Jenis}", item.Nomor, item.JenisKeputusan);
                }

                // Simpan berkas SEBELUM halaman ditutup agar FileWord sudah terbaru
                // saat daftar induk memuat ulang.
                if (pdfNaskah != null)
                {
                    try
                    {
                        await SaveNaskahPdfAsync(item, pdfNaskah, berkasLama);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyimpan PDF naskah Perdes (data sudah tersimpan)");
                        await _messageService.ShowErrorAsync(
                            "Data tersimpan, tetapi PDF naskah gagal ditulis:\n\n" + ex.Message);
                    }
                }
                else if (HasNewAttachment)
                {
                    try
                    {
                        await SaveAttachmentAsync(item.IdBarisExcel, item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyalin berkas lampiran (data sudah tersimpan)");
                        await _messageService.ShowErrorAsync("Data tersimpan, tapi berkas gagal disalin:\n\n" + ex.Message);
                    }
                }

                // Beri tahu bila data dipindah ke arsip jenis lain (formulir dipindah otomatis).
                if (!item.JenisKeputusan.Equals(_jenisKeputusanAwal, StringComparison.OrdinalIgnoreCase))
                {
                    await _messageService.ShowInfoAsync(
                        $"Data tersimpan di arsip {LabelJenis(item.JenisKeputusan)}.\n\n" +
                        $"Buka jenis tersebut di Buku SK / Peraturan untuk melihat datanya.");
                }

                // Halaman ditutup agar daftar induk memuat ulang dan melihat FileWord terbaru.
                Tersimpan?.Invoke();
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan arsip keputusan");
                await _messageService.ShowErrorAsync($"Gagal menyimpan data: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        }


    }
}
