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
        private NaskahPerdes AmbilNaskah() => new()
        {
            Judul = Tentang,
            Halaman = HalamanNaskah.Select(h => h.KeModel()).ToList()
        };

        /// <summary>
        /// Pratinjau naskah yang sedang disunting di jendela terpisah, sehingga
        /// isian formulir dan halaman naskah tidak hilang saat pratinjau ditutup.
        /// </summary>
        private async Task PratinjauNaskahAsync()
        {
            if (!HasNaskah)
            {
                await _messageService.ShowInfoAsync("Isi minimal satu halaman naskah sebelum membuka pratinjau.");
                return;
            }

            try
            {
                var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true) ?? new DesaData();
                DateTime tanggal = ParseDate(Tanggal) ?? DateTime.Today;
                byte[] pdf = new NaskahPerdesGenerator(desa, AmbilNaskah(), Nomor, tanggal).Buat();

                string path = await PratinjauPdf
                    .SimpanAsync(_folderPdfSementara, pdf, "Pratinjau_Perdes")
                    .ConfigureAwait(true);

                TampilkanPratinjau(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau naskah Perdes");
                await _messageService.ShowErrorAsync("Pratinjau naskah gagal dibuat: " + ex.Message);
            }
        }

        private void TampilkanPratinjau(string path)
        {
            Window? jendela = null;
            var preview = _previewFactory(
                $"Pratinjau Naskah {LabelJenis(_jenisKeputusan)}", path, () => jendela?.Close());
            jendela = new Window
            {
                Title = preview.Title,
                Content = new SuDesApp.Wpf.Views.PdfPreviewView { DataContext = preview },
                Owner = Application.Current?.MainWindow,
                Width = 1100,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            jendela.ShowDialog();
        }


        private static string LabelJenis(string jenis) => jenis switch
        {
            "PERDES" => "Peraturan Desa",
            "PERKADES" => "Peraturan Kepala Desa",
            _ => "Surat Keputusan"
        };


    }
}
