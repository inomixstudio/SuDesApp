using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class RegisterSuratViewModel : ObservableObject
    {
        private void ClearSearch()
        {
            SearchText = string.Empty;
            _ = HandleFilterChangeAsync();
        }

        private async Task GoToFirstPageAsync()
        {
            if (_currentPage == 1) return;
            _currentPage = 1;
            await LoadDataSafeAsync();
        }

        private async Task GoToLastPageAsync()
        {
            if (_currentPage == TotalPages) return;
            _currentPage = TotalPages;
            await LoadDataSafeAsync();
        }

        private async Task ChangeStatusAsync(string newStatus)
        {
            var target = SelectedSurat;
            if (target == null)
            {
                Pesan.Peringatan("Belum ada surat yang dipilih",
                    "Pilih dulu satu baris surat di daftar, lalu ubah statusnya.", "status-surat");
                return;
            }

            if (string.Equals(target.Status, newStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(target.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                Pesan.Peringatan("Status surat dibatalkan tidak dapat diubah",
                    $"Surat nomor {target.NomorSurat} sudah dibatalkan, jadi statusnya kini tetap.",
                    "status-surat");
                return;
            }

            try
            {
                var ok = await _unitOfWork.SuratRepository.UpdateStatusAsync(target.ID_Surat, newStatus);
                if (ok)
                {
                    target.Status = newStatus;
                    target.StatusDisplay = GetStatusDisplayName(newStatus);
                    if (target.OriginalSuratData != null)
                        target.OriginalSuratData.Status = newStatus;
                    _ = LoadDataSafeAsync();
                    _ = LoadStatusSummaryAsync();
                }
                else
                {
                    Pesan.Galat("Gagal mengubah status surat",
                        "Perubahan status tidak tersimpan di database. Coba ulangi sebentar lagi.",
                        "status-surat");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change status for surat ID={Id}", target.ID_Surat);
                Pesan.Galat("Gagal mengubah status surat",
                    $"Penyebabnya: {ex.Message}", "status-surat");
            }
        }

        /// <summary>
        /// Cetak langsung surat ini ke printer: PDF dibuat ke temp lalu dikirim ke
        /// dialog cetak Windows (padanan alur cetak WinForms) — tanpa pratinjau.
        /// </summary>
        private async Task PrintSuratDirectAsync(SuratDisplayModel? model)
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                Pesan.Peringatan("Surat dibatalkan tidak dapat dicetak",
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan, jadi tidak dicetak.",
                    "cetak-surat");
                return;
            }

            // Data desa masih contoh: surat resmi akan keluar dengan kop contoh.
            // Pengguna diberi kesempatan menghentikan pencetakan lebih dulu.
            if (_peringatan != null &&
                !await _peringatan.BolehLanjutAsync(
                    $"Surat nomor {model.NomorSurat} akan dicetak langsung ke printer.", _messageService))
            {
                return;
            }

            try
            {
                IsLoading = true;
                IsGeneratingPdf = true;

                var suratData = model.OriginalSuratData;

                // Baris daftar register tidak me-load relasi (warga, rincian garapan, dst.) —
                // muat ulang lengkap dari DB agar PDF yang dicetak berisi data utuh.
                var fullForPrint = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (fullForPrint != null) suratData = fullForPrint;

                var hasilPdf = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (!hasilPdf.Berhasil || !File.Exists(hasilPdf.Jalur))
                {
                    Pesan.Peringatan("PDF surat tidak dapat dibuat",
                        $"Jenis '{suratData.NamaJenis ?? "?"}' belum berhasil dibuatkan dokumennya. " +
                        $"Penyebabnya: {hasilPdf.PesanGalat ?? "berkas PDF tidak ada."}",
                        "cetak-surat");
                    return;
                }
                var pdfPath = hasilPdf.Jalur!;

                var printed = await _pdfPrintService.PrintPdfFileAsync(
                    pdfPath, $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}");

                if (printed)
                {
                    Pesan.Sukses("Surat terkirim ke printer",
                        $"Surat nomor {model.NomorSurat} sudah dikirim ke printer.", "cetak-surat");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencetak surat #{Id}", model.ID_Surat);
                Pesan.Galat("Gagal mencetak surat", $"Penyebabnya: {ex.Message}", "cetak-surat");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }


    }
}
