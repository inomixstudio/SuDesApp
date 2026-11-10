using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // =====================================================================
        // Mutasi: pindah / meninggal / pendatang
        // =====================================================================

        /// <summary>Permintaan ubah status beserta ketikanya (dipakai sebagai parameter perintah).</summary>
        public class UbahStatusRequest
        {
            public int IdWarga { get; init; }
            public string Nama { get; init; } = string.Empty;
            public string StatusBaru { get; init; } = StatusWargaTipe.Aktif;
            public string Keterangan { get; init; } = string.Empty;
        }

        private async Task UbahStatusAsync(UbahStatusRequest? request)
        {
            if (request == null) return;

            if (!StatusWargaTipe.Valid(request.StatusBaru))
            {
                await _messageService.ShowErrorAsync("Status warga tidak dikenal.");
                return;
            }

            bool benar = await _messageService.ShowConfirmationAsync(
                "Ubah Status Warga",
                $"Ubah status warga \"{request.Nama}\" menjadi {StatusWargaTipe.Normalisasi(request.StatusBaru)}?\n\n" +
                "Data surat yang pernah dibuat tidak ikut berubah.");
            if (!benar) return;

            IsBusy = true;
            try
            {
                await _warga.UbahStatusWargaAsync(
                    request.IdWarga,
                    request.StatusBaru,
                    DateTime.Today.ToString("yyyy-MM-dd"),
                    string.IsNullOrWhiteSpace(request.Keterangan) ? null : request.Keterangan);

                _activityLog.Log("WARGA", request.Nama, "Mutasi",
                    $"Status diubah menjadi {StatusWargaTipe.Normalisasi(request.StatusBaru)}");

                await LoadAsync(true);
                StatusText = $"Status warga diperbarui: {StatusWargaTipe.Normalisasi(request.StatusBaru)}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status warga");
                await _messageService.ShowErrorAsync("Gagal mengubah status warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }


    }
}
