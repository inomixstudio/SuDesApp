using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PembaruanViewModel : ObservableObject
{
        // =====================================================================
        // Unduhan installer: jeda & lanjutkan tanpa kehilangan bagian terunduh
        // =====================================================================

        /// <summary>
        /// Periksa sisa unduhan installer dari percobaan sebelumnya (dijeda, koneksi
        /// putus, atau aplikasi ditutup). Sisanya sengaja tidak dihapus, jadi pengguna
        /// bisa melanjutkan tanpa mengunduh ulang bagian yang sudah ada. Dipanggil
        /// setiap penawaran pembaruan disusun.
        /// </summary>
        public void PeriksaUnduhanTersisa()
        {
            if (IsDownloading)
            {
                return;
            }

            _unduhanInstaller = null;
            UnduhanDijeda = false;
            CatatanUnduhan = string.Empty;

            if (_updateInfo != null)
            {
                try
                {
                    var unduhan = _updateService.BuatUnduhanInstaller(_updateInfo);
                    if (unduhan != null && unduhan.SelarasDenganDisk() > 0)
                    {
                        _unduhanInstaller = unduhan;
                        UnduhanDijeda = true;
                        CatatanUnduhan = unduhan.Ringkasan;
                        DownloadPercent = unduhan.Persen;

                        TampilkanPesan(    JenisPesanInline.Info,
                            "Unduhan installer bisa dilanjutkan",
                            $"Installer pembaruan {_updateInfo.Version} sudah terunduh sebagian ({unduhan.Ringkasan}). " +
                            "Tekan Lanjutkan Unduhan untuk meneruskannya — bagian yang sudah ada tidak diunduh ulang.",
                            "sisa-unduhan");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sisa unduhan installer tidak dapat diperiksa.");
                }
            }

            OnPropertyChanged(nameof(AdaUnduhanTersisa));
            OnPropertyChanged(nameof(InfoSisaUnduhan));
            OnPropertyChanged(nameof(TeksTombolInstaller));
            OnPropertyChanged(nameof(JedaLanjutTersedia));
            RaiseCommandStates();
        }

        /// <summary>Tombol Jeda/Lanjutkan: hentikan sementara, atau teruskan unduhan.</summary>
        private void JedaAtauLanjut()
        {
            if (IsDownloading)
            {
                JedaUnduhan();
                return;
            }

            if (UnduhanDijeda)
            {
                _ = UnduhInstallerAsync();
            }
        }

        /// <summary>
        /// Hentikan unduhan sementara. Bagian yang sudah terunduh tetap tersimpan di
        /// berkas sementara, jadi meneruskan tidak mengulang dari awal.
        /// </summary>
        private void JedaUnduhan()
        {
            UnduhanDijeda = true;
            StatusText = "Menghentikan unduhan sementara...";
            _ctsOperasi?.Cancel();
        }


    }
}
