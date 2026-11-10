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
        // Contoh template siap pakai
        // =====================================================================

        /// <summary>
        /// Sekali per pemakaian halaman: pasang contoh bawaan bila daftar masih kosong.
        /// Penanda di profil pengguna membuat contoh yang sudah dihapus tidak kembali.
        /// </summary>
        private async Task SiapkanContohBawaanAsync()
        {
            if (_sudahSiapkanBawaan) return;
            _sudahSiapkanBawaan = true;

            try
            {
                var terpasang = await _bawaanService.PasangOtomatisAsync();
                if (terpasang.Count > 0)
                {
                    InfoBawaan = $"{terpasang.Count} contoh template siap pakai sudah disiapkan: " +
                        string.Join(", ", terpasang.Select(t => t.NamaTampil)) + ". " +
                        "Contoh-contoh ini bisa langsung diisi dan dicetak, disunting, atau dihapus seperti template biasa.";
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyiapkan contoh template bawaan");
            }
        }

        private void RaisePerintah()
        {
            BuatBaruCommand.RaiseCanExecuteChanged();
            EditCommand.RaiseCanExecuteChanged();
            DuplikatCommand.RaiseCanExecuteChanged();
            HapusCommand.RaiseCanExecuteChanged();
            IsiCommand.RaiseCanExecuteChanged();
            PratinjauCommand.RaiseCanExecuteChanged();
            SegarkanCommand.RaiseCanExecuteChanged();
            BuatDariWordCommand.RaiseCanExecuteChanged();
            PilihFilterCommand.RaiseCanExecuteChanged();
        }

    }
}
