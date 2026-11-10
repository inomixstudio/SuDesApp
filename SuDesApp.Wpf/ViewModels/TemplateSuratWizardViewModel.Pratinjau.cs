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
        // Pratinjau
        // =====================================================================

        private async Task SegarkanPratinjauAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var template = TemplateDariForm();
                if (template.JumlahElemen == 0)
                {
                    PesanKesalahan = "Belum ada elemen surat yang bisa dipratinjau.";
                    return;
                }

                var nilaiContoh = TemplateSuratNilai.NilaiContoh(template);
                var pdf = await _generator.BuatPdfAsync(
                    template, nilaiContoh, ContohNomor, DateTime.Now, pratinjau: true);

                if (pdf.Length == 0)
                {
                    PesanKesalahan = "Pratinjau gagal dibuat: berkas PDF kosong.";
                    return;
                }

                string berkas = await PratinjauPdf
                    .SimpanAsync((string?)null, pdf, "Pratinjau_Template")
                    .ConfigureAwait(true);

                Pratinjau = _previewFactory($"Pratinjau — {template.NamaTampil}", berkas, Kembali);
                BersihkanBerkasPratinjauLama(berkas);
                _berkasPratinjau = berkas;

                PesanKesalahan = string.Empty;
                PesanInformasi = "Periksa contoh suratnya. Kurang sesuatu? Tekan Kembali untuk menyunting komponennya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau template surat");
                PesanKesalahan = "Gagal membuat pratinjau: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Hapus berkas pratinjau sebelumnya (best-effort; gagal diabaikan).</summary>
        private void BersihkanBerkasPratinjauLama(string berkasBaru)
        {
            try
            {
                string? lama = _berkasPratinjau;
                if (string.IsNullOrEmpty(lama) || lama == berkasBaru) return;

                var info = new FileInfo(lama);
                if (info.Exists)
                {
                    info.Delete();
                }
            }
            catch
            {
                // Pratinjau lama mungkin sedang dibuka penampil PDF; biarkan saja.
            }
        }


    }
}
