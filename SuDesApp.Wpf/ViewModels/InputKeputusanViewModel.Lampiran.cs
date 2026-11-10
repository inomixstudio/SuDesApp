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
        /// <summary>Ada berkas baru (Word/PDF) yang belum tersalin ke arsip.</summary>
        private bool HasNewAttachment => !string.IsNullOrWhiteSpace(_wordFilePath) || !string.IsNullOrWhiteSpace(_pdfFilePath);

        /// <summary>Menyalin berkas Word/PDF ke folder penyimpanan dan memperbarui kolom FileWord.</summary>
        private async Task SaveAttachmentAsync(int id, DataKeputusan item)
        {
            try
            {
                string sourcePath = !string.IsNullOrWhiteSpace(_wordFilePath) ? _wordFilePath : _pdfFilePath;
                var ext = Path.GetExtension(sourcePath) ?? ".docx";
                var (fileName, destPath) = _repository.ResolveWordStorage(id, item.JenisKeputusan, item.Nomor, ext);

                // Hapus versi lama jika ada dan berbeda dari yang baru.
                var oldPath = string.IsNullOrWhiteSpace(item.FileWord) ? null : _repository.ResolveWordFullPath(item.FileWord);
                if (!string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(oldPath))
                {
                    try { File.Delete(oldPath); } catch { /* berkas lama terkunci — versi baru tetap disalin */ }
                }

                _fileService.EnsureDirectoryExists(Path.GetDirectoryName(destPath) ?? destPath);
                await _fileService.CopyFileAsync(sourcePath, destPath, overwrite: true);

                item.FileWord = fileName;
                await _repository.UpdateAsync(item); // persist kolom FileWord
                _storedFileName = fileName;
                _wordFilePath = string.Empty;
                _pdfFilePath = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin berkas lampiran ke penyimpanan");
                await _messageService.ShowErrorAsync("Berkas terpilih gagal disalin; data tetap tersimpan.\n\n" + ex.Message);
            }
        }

        /// <summary>
        /// Tulis PDF naskah ke arsip, simpan sumber naskah di sampingnya, lalu
        /// perbarui kolom FileWord. Berkas lama (termasuk Word) dibersihkan bila
        /// jalurnya berbeda.
        /// </summary>
        private async Task SaveNaskahPdfAsync(DataKeputusan item, byte[] pdf, string? berkasLama)
        {
            var (fileName, destPath) = _repository.ResolveWordStorage(
                item.IdBarisExcel, item.JenisKeputusan, item.Nomor, ".pdf");
            var oldPath = string.IsNullOrWhiteSpace(berkasLama)
                ? null
                : _repository.ResolveWordFullPath(berkasLama);

            _fileService.EnsureDirectoryExists(Path.GetDirectoryName(destPath) ?? destPath);
            await File.WriteAllBytesAsync(destPath, pdf).ConfigureAwait(true);

            item.FileWord = fileName;
            await _repository.UpdateAsync(item); // persist kolom FileWord
            NaskahPerdesStore.Simpan(destPath, AmbilNaskah());
            _storedFileName = fileName;

            if (oldPath != null && !string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(oldPath);
                    NaskahPerdesStore.Hapus(oldPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Berkas Perdes lama tidak dapat dihapus: {Path}", oldPath);
                }
            }
        }

        private List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Nomor)) errors.Add("- Nomor wajib diisi.");
            if (string.IsNullOrWhiteSpace(Tentang)) errors.Add("- Kolom 'Tentang' wajib diisi.");
            if (string.IsNullOrWhiteSpace(Keterangan)) errors.Add("- Keterangan wajib diisi.");

            if (string.IsNullOrWhiteSpace(Tanggal)) errors.Add("- Tanggal wajib diisi.");
            else if (ParseDate(Tanggal) == null) errors.Add("- Format tanggal tidak valid (DD-MM-YYYY).");

            return errors;
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParseExact(value, "dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) ? date : null;
        }


    }
}
