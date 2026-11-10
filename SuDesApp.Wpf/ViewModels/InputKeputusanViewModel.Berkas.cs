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
        /// <summary>
        /// Memilih berkas lampiran. Word (.docx/.doc) dibaca untuk isi otomatis — berkas
        /// .doc lama dikonversi otomatis lebih dulu; PDF hanya dilampirkan — isinya tidak
        /// dibaca, pengguna diingatkan mengisi formulir secara manual.
        /// </summary>
        private async Task PickFileAsync()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Pilih berkas Word atau PDF",
                    Filter = "Dokumen Word / PDF|*.docx;*.doc;*.pdf|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|PDF (.pdf)|*.pdf"
                };
                if (dlg.ShowDialog() != true) return;

                if (string.Equals(Path.GetExtension(dlg.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // PDF: lampirkan saja, jangan dibaca isinya.
                    _pdfFilePath = dlg.FileName;
                    _wordFilePath = string.Empty;
                    UpdateFileState();

                    await _messageService.ShowInfoAsync(
                        "Berkas PDF dipilih.\n\nIsi formulir (Nomor, Tanggal, Tentang, Keterangan) diisi manual ya — aplikasi tidak membaca isi berkas PDF.\n\nBerkas akan tersalin ke arsip saat data disimpan.");
                    return;
                }

                // Word: lampirkan lalu isi otomatis.
                _wordFilePath = dlg.FileName;
                _pdfFilePath = string.Empty;
                UpdateFileState();

                // Auto-isi otomatis setelah berkas dipilih.
                await FillFromWordAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memilih berkas lampiran");
                await _messageService.ShowErrorAsync("Gagal membuka berkas: " + ex.Message);
            }
        }

        private void UpdateFileState()
        {
            OnPropertyChanged(nameof(WordFileDisplay));
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
            ((AsyncRelayCommand)OpenWordFileCommand).RaiseCanExecuteChanged();
        }

        private async Task FillFromWordAsync()
        {
            if (!HasWordFile) return;
            IsLoadingWord = true;
            try
            {
                var content = await _wordReader.ReadAsync(_wordFilePath);
                var fields = WordTextParser.ExtractKeputusanFields(content.FullText, content.Paragraphs);

                // Deteksi jenis dokumen dari isi berkas
                var detectedType = WordTextParser.DetectDocumentType(content.FullText);

                // PERDES tidak lagi menerima lampiran Word: naskahnya disusun
                // sebagai halaman dan diterbitkan generator PDF. Berkas seperti
                // ini dibatalkan supaya tidak tersimpan sebagai lampiran PERDES.
                if (string.Equals(detectedType, "PERDES", StringComparison.OrdinalIgnoreCase) && !IsPerdes)
                {
                    _wordFilePath = string.Empty;
                    UpdateFileState();
                    await _messageService.ShowWarningAsync(
                        "Berkas ini terdeteksi Peraturan Desa.\n\n" +
                        "Peraturan Desa disusun sebagai halaman naskah di menu PERDES, bukan sebagai lampiran Word. " +
                        "Buka tab PERDES lalu sunting halaman naskahnya.");
                    return;
                }

                if (!string.Equals(detectedType, _jenisKeputusan, StringComparison.OrdinalIgnoreCase) && detectedType != "Unknown")
                {
                    // Tawarkan opsi memindahkan formulir (dan data yang sedang diedit)
                    // ke arsip jenis yang benar, tanpa memilih ulang berkas.
                    bool pindah = await _messageService.ShowConfirmationAsync(
                        "Jenis Berkas Tidak Sesuai",
                        $"Berkas terdeteksi {LabelJenis(detectedType)}, sedangkan formulir ini untuk {LabelJenis(_jenisKeputusan)}.\n\n" +
                        $"Pindahkan formulir ke arsip {LabelJenis(detectedType)} dan lanjutkan dengan berkas ini?\n\n" +
                        $"Pilih \"Tidak\" untuk membatalkan pemilihan berkas.");

                    if (pindah)
                    {
                        // Pindahkan formulir ke arsip jenis yang benar; berkas tetap dipakai.
                        _jenisKeputusan = detectedType.ToUpperInvariant();
                        Title = (_editData != null ? "Edit " : "Input ") + LabelJenis(_jenisKeputusan);
                        OnPropertyChanged(nameof(IsPerdes));
                        OnPropertyChanged(nameof(IsBukanPerdes));
                        // Lanjut ke pengisian otomatis di bawah dengan jenis yang baru.
                    }
                    else
                    {
                        var msg = $"Pemilihan berkas dibatalkan.\n\n" +
                                  $"Silakan gunakan menu yang sesuai:\n" +
                                  $"- Surat Keputusan (SK)  -> Jenis \"SK\"\n" +
                                  $"- Peraturan Desa        -> Jenis \"Perdes\"\n" +
                                  $"- Keputusan Kepala Desa -> Jenis \"Perkades\"\n\n" +
                                  $"Atau pilih ulang berkas untuk melanjutkan di formulir ini.";
                        await _messageService.ShowWarningAsync(msg);

                        // Batalkan pemilihan file - bersihkan path dan UI
                        _wordFilePath = string.Empty;
                        UpdateFileState();
                        return; // Hentikan pengisian otomatis
                    }
                }

                bool changed = false;
                if (!string.IsNullOrWhiteSpace(fields.Nomor) && string.IsNullOrWhiteSpace(Nomor))
                {
                    Nomor = fields.Nomor; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.TanggalDdMmYyyy) && string.IsNullOrWhiteSpace(Tanggal))
                {
                    Tanggal = fields.TanggalDdMmYyyy; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.Tentang) && string.IsNullOrWhiteSpace(Tentang))
                {
                    Tentang = fields.Tentang; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.Keterangan) && string.IsNullOrWhiteSpace(Keterangan))
                {
                    Keterangan = fields.Keterangan; changed = true;
                }

                if (changed)
                    await _messageService.ShowInfoAsync("Formulir diisi otomatis dari berkas Word. Periksa dan koreksi jika diperlukan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca/mengisi dari berkas Word");
                await _messageService.ShowErrorAsync(
                    "Tidak dapat membaca berkas Word. Pastikan berkas tidak rusak dan tidak sedang dibuka di aplikasi lain.\n\n" +
                    "Berkas .doc (Word lama) dikonversi otomatis memakai Microsoft Word; pastikan Microsoft Word terpasang di komputer ini.\n\n" +
                    ex.Message);
            }
            finally
            {
                IsLoadingWord = false;
            }
        }


    }
}
