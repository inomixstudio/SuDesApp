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
        // Pemeriksaan
        // =====================================================================

        private async Task CheckForUpdatesAsync()
        {
            IsChecking = true;
            StatusText = "Memeriksa koneksi internet...";
            KosongkanPenawaran();
            BersihkanPesan();

            try
            {
                if (!await IsInternetAvailableAsync())
                {
                    StatusText = "Tidak ada koneksi internet";
                    TampilkanPesan(    JenisPesanInline.Peringatan,
                        "Tidak ada koneksi internet",
                        "Pemeriksaan pembaruan memerlukan koneksi internet. Periksa sambungan jaringan Anda, lalu periksa lagi.",
                        "hasil-periksa");
                    return;
                }

                StatusText = "Memeriksa pembaruan tersedia...";
                _updateInfo = await _updateService.CheckForUpdatesAsync();

                if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.Version))
                {
                    StatusText = "Gagal mendapatkan informasi versi";
                    return;
                }

                if (!IsNewerVersionAvailable())
                {
                    StatusText = $"Sudah menggunakan versi terbaru ({IdentitasAplikasi.VersiSingkat})";
                    TampilkanPesan(    JenisPesanInline.Sukses,
                        "Sudah versi terbaru",
                        $"Anda memakai versi {IdentitasAplikasi.VersiSingkat}. Tidak ada pembaruan yang perlu dipasang saat ini.",
                        "hasil-periksa");
                    return;
                }

                StatusText = $"Versi baru ditemukan: {_updateInfo.Version}";
                await SusunPenawaranAsync(_updateInfo);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pemeriksaan dibatalkan";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal cek pembaruan");
                StatusText = "Pemeriksaan pembaruan gagal — lihat keterangan di halaman.";
                TampilkanPesan(    JenisPesanInline.Galat,
                    "Gagal memeriksa pembaruan",
                    ex.Message + "\nPemeriksaan bisa diulang kapan saja dengan tombol Periksa Pembaruan.",
                    "hasil-periksa");
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Tentukan jenis pembaruan: tambalan kecil (bila rilis menyediakannya dan
        /// versi terpasang cocok) atau installer penuh, beserta alasannya.
        /// </summary>
        public async Task SusunPenawaranAsync(UpdateInfo rilis)
        {
            _tambalan = null;
            _rencana = null;
            Perbaikan.Clear();

            _tambalan = await _patchService.AmbilPenawaranAsync(rilis);

            if (_tambalan == null)
            {
                AlasanInstallerText =
                    "Rilis ini tidak menyediakan pembaruan kecil, sehingga dipasang memakai installer penuh.";
            }
            else
            {
                _rencana = _patchService.SusunRencana(_tambalan, GetCurrentVersion().ToString());

                if (!_rencana.BisaDipakai)
                {
                    AlasanInstallerText = _rencana.AlasanTidakBisa;
                }
                else
                {
                    AlasanInstallerText = string.Empty;
                    RincianPembaruanText = RangkumRencana(_tambalan, _rencana);
                }
            }

            JenisPembaruanText = BisaTambalan
                ? $"Pembaruan kecil tersedia: {_tambalan!.Versi} — tanpa installer"
                : $"Pembaruan tersedia: {rilis.Version} — perlu installer penuh";

            foreach (var baris in AmbilDaftarPerbaikan(rilis, _tambalan?.Ringkasan ?? new List<string>()))
            {
                Perbaikan.Add(baris);
            }

            PenawaranTersedia = true;
            OnPropertyChanged(nameof(BisaTambalan));
            OnPropertyChanged(nameof(BisaInstaller));
            OnPropertyChanged(nameof(TeksTombolTambalan));
            RaiseCommandStates();

            // Unduhan installer yang terhenti sebelumnya (dijeda, koneksi putus, atau
            // aplikasi ditutup) bisa diteruskan tanpa mengunduh ulang bagiannya.
            PeriksaUnduhanTersisa();

            _logger.LogInformation("Penawaran pembaruan {Versi}: tambalan={Tambalan} ({Alasan})",
                rilis.Version, BisaTambalan, BisaTambalan ? "-" : AlasanInstallerText);
        }

        private static string RangkumRencana(TambalanInfo tambalan, RencanaTambalan rencana)
        {
            var ukuran = FormatUkuran(rencana.TotalByte);
            var teks = $"{rencana.BerkasDiganti.Count} berkas berubah • {ukuran} • dari versi " +
                       $"{string.Join(", ", tambalan.DariVersi)} ke {tambalan.Versi}";

            if (rencana.BerkasSudahSama > 0) teks += $" • {rencana.BerkasSudahSama} berkas sudah sama";
            if (rencana.BerkasDilindungi.Count > 0) teks += $" • {rencana.BerkasDilindungi.Count} berkas data pengguna tidak diganti";

            return teks;
        }

        /// <summary>
        /// Daftar perbaikan yang ditampilkan: dari <c>ringkasan</c> patch.json bila ada,
        /// kalau tidak dari catatan rilis GitHub.
        /// </summary>
        private static IEnumerable<string> AmbilDaftarPerbaikan(UpdateInfo rilis, IReadOnlyList<string> ringkasanManifest)
        {
            if (ringkasanManifest != null && ringkasanManifest.Count > 0) return ringkasanManifest;

            return (rilis?.ReleaseNotes ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(b => b.Trim().TrimStart('-', '*', '#'))
                .Where(b => b.Length > 2)
                .Take(20)
                .ToList();
        }

        private static string FormatUkuran(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return $"{Math.Max(1, byteCount / 1024)} KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

        private async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync("https://www.google.com", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private bool IsNewerVersionAvailable()
        {
            if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.Version)) return false;
            if (!Version.TryParse(_updateInfo.Version, out var latestVersion)) return false;

            return latestVersion > GetCurrentVersion();
        }

        private void KosongkanPenawaran()
        {
            _tambalan = null;
            _rencana = null;
            Perbaikan.Clear();
            PenawaranTersedia = false;
            JenisPembaruanText = string.Empty;
            RincianPembaruanText = string.Empty;
            AlasanInstallerText = string.Empty;
            OnPropertyChanged(nameof(BisaTambalan));
            OnPropertyChanged(nameof(BisaInstaller));
            RaiseCommandStates();
        }

        private void RaiseCommandStates()
        {
            (CheckCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PerbaruiTambalanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (UnduhInstallerCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (JedaLanjutCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BersihkanRiwayatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }


    }
}
