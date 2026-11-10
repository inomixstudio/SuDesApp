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
        private async Task OpenSuratDetailAsync(SuratDisplayModel model)
        {
            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                Pesan.Peringatan("Surat dibatalkan tidak dapat dibuka",
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan, jadi pratinjaunya tidak dibuka.",
                    "pratinjau-surat");
                return;
            }

            try
            {
                IsLoading = true;
                IsGeneratingPdf = true;

                _logger.LogInformation("Opening document for surat ID={ID}, Jenis={Jenis}",
                    model.OriginalSuratData.ID_Surat, model.OriginalSuratData.NamaJenis);

                var suratData = model.OriginalSuratData;

                // Baris daftar register tidak me-load relasi (warga, rincian garapan, dst.) —
                // muat ulang lengkap dari DB agar PDF tidak berisi data kosong.
                var full = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (full != null) suratData = full;

                // Surat instansi dan surat dari Template Surat tidak mewakili perorangan
                // (isinya disimpan sendiri oleh suratnya), jadi tidak perlu data warga.
                if (!string.Equals(suratData.NamaJenis, "INSTANSI", StringComparison.OrdinalIgnoreCase) &&
                    !TemplateSuratTercatat.DariTemplateSurat(suratData) &&
                    suratData.Warga == null)
                {
                    Pesan.Peringatan("Data warga tidak ditemukan",
                        "Surat ini belum terhubung ke data warga, jadi pratinjaunya tidak dapat dibuat.",
                        "pratinjau-surat");
                    return;
                }

                var hasilPdf = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (!hasilPdf.Berhasil)
                {
                    Pesan.Peringatan("PDF surat tidak dapat dibuat",
                        $"Jenis '{suratData.NamaJenis ?? "?"}' belum berhasil dibuatkan dokumennya. " +
                        $"Penyebabnya: {hasilPdf.PesanGalat}. Tutup pratinjau lain lalu ulangi.",
                        "pratinjau-surat");
                    return;
                }
                var pdfPath = hasilPdf.Jalur!;

                var preview = _previewFactory(
                    $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}", pdfPath, suratData.ID_Surat);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open document");
                // Sebelumnya error ini ditelan diam-diam sehingga klik pratinjau PDF
                // tampak "tidak berfungsi". Tampilkan penyebabnya ke pengguna.
                Pesan.Galat("Gagal membuka pratinjau PDF", $"Penyebabnya: {ex.Message}", "pratinjau-surat");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }

        /// <summary>
        /// Buka form input dalam MODE EDIT untuk surat ini: data dimuat ulang dari
        /// database ke form, nomor surat dikunci, lalu Simpan menjalankan UPDATE
        /// (bukan insert baru) dan PDF di-generate ulang.
        /// </summary>
        private async Task EditSuratAsync(SuratDisplayModel? model)
        {
            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                Pesan.Peringatan("Surat dibatalkan tidak dapat diedit",
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan, jadi isinya tidak dapat diubah.",
                    "edit-surat");
                return;
            }

            try
            {
                // Permohonan rekening koran diisi lewat formnya sendiri (bukan form input
                // surat warga), jadi edit diarahkan ke halaman itu dengan data surat lama.
                if (string.Equals(model.OriginalSuratData.NamaJenis, SuratConstants.REKENING_KORAN, StringComparison.OrdinalIgnoreCase))
                {
                    var spRk = _navigation.BukaHalaman();
                    var rekeningVm = spRk.GetRequiredService<RekeningKoranViewModel>();
                    rekeningVm.RequestClose += () => _navigation.Navigate(CreateRegisterForReturn());
                    await rekeningVm.ConfigureForEditAsync(model.ID_Surat);
                    _navigation.Navigate(rekeningVm);
                    return;
                }

                // Surat dari Template Surat diperbaiki lewat formulir pengisian template
                // (mode edit): definisi & isian dibaca dari payload surat, nomor lamanya
                // dipertahankan, dan Simpan memperbarui baris register yang sama.
                if (string.Equals(model.OriginalSuratData.NamaJenis, SuratConstants.TEMPLATE_SURAT, StringComparison.OrdinalIgnoreCase))
                {
                    var spTemplate = _navigation.BukaHalaman();
                    var isiVm = spTemplate.GetRequiredService<IsiTemplateSuratViewModel>();
                    isiVm.SebelumBatal = () => _navigation.Navigate(CreateRegisterForReturn());
                    await isiVm.ConfigureForEditAsync(model.ID_Surat);
                    _navigation.Navigate(isiVm);
                    return;
                }

                // Form edit ditampilkan di content host utama (bukan modal): navigasi ke
                // InputWindowViewModel, muat data surat, lalu kunci nomor. Saat dibatalkan,
                // kembali ke daftar Register Surat yang disegarkan.
                var spInput = _navigation.BukaHalaman();
                var vm = spInput.GetRequiredService<InputWindowViewModel>();
                vm.RequestClose += () => _navigation.Navigate(CreateRegisterForReturn());
                await vm.ConfigureForEditAsync(model.ID_Surat);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form edit surat #{Id}", model.ID_Surat);
                Pesan.Galat("Gagal membuka form edit", $"Penyebabnya: {ex.Message}", "edit-surat");
            }
        }

        /// <summary>
        /// Ekspor surat ini ke PDF di lokasi pilihan pengguna (bukan file temp).
        /// PDF dibuat ke temp dulu lalu disalin ke tujuan — generator selalu menulis
        /// ke path temp dengan nama tetap.
        /// </summary>
        private async Task ExportSuratPdfAsync(SuratDisplayModel? model)
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            if (model?.OriginalSuratData == null) return;

            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Ekspor Surat ke PDF",
                    Filter = "Dokumen PDF (*.pdf)|*.pdf",
                    FileName = $"{model.NamaJenis}_{model.NomorSurat}_{model.TanggalSurat:yyyyMMdd}.pdf"
                        .Replace("/", "-").Replace("\\", "-"),
                    DefaultExt = ".pdf"
                };
                if (sfd.ShowDialog() != true) return;

                IsLoading = true;
                IsGeneratingPdf = true;

                var suratData = model.OriginalSuratData;

                // Muat ulang lengkap dari DB agar PDF hasil ekspor berisi semua
                // relasi (warga, rincian garapan, dst.), bukan data parsial baris daftar.
                var fullForExport = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (fullForExport != null) suratData = fullForExport;

                var hasilPdf = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (!hasilPdf.Berhasil || !File.Exists(hasilPdf.Jalur))
                {
                    Pesan.Peringatan("PDF tidak dapat dibuat",
                        $"Jenis '{suratData.NamaJenis ?? "?"}' belum berhasil dibuatkan dokumennya. " +
                        $"Penyebabnya: {hasilPdf.PesanGalat ?? "berkas PDF tidak ada."}",
                        "ekspor-pdf");
                    return;
                }
                var pdfPath = hasilPdf.Jalur!;

                File.Copy(pdfPath, sfd.FileName, overwrite: true);
                Pesan.Sukses("Surat diekspor ke PDF",
                    $"Berkas disimpan di:\n{sfd.FileName}", "ekspor-pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor surat #{Id} ke PDF", model.ID_Surat);
                Pesan.Galat("Gagal mengekspor surat ke PDF", $"Penyebabnya: {ex.Message}", "ekspor-pdf");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }


    }
}
