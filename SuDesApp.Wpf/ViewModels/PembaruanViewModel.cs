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

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel halaman Pembaruan Aplikasi.
    ///
    /// Dua jalur pembaruan:
    /// - <b>Pembaruan kecil (tambalan)</b> — bila rilis menyertakan <c>patch.json</c>
    ///   dan versi terpasang cocok, aplikasi hanya mengunduh berkas yang berubah
    ///   (beberapa ratus KB - beberapa MB), memverifikasi SHA-256, mengganti berkas
    ///   tersebut, lalu membuka aplikasi kembali. Tidak perlu installer.
    /// - <b>Pembaruan besar (installer penuh)</b> — dipakai bila rilis ditandai besar,
    ///   tanpa patch.json, atau versi terpasang terlalu jauh dari versi asal tambalan.
    /// </summary>
    public class PembaruanViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;
        private readonly PatchUpdateService _patchService;
        private readonly IMessageService _messageService;
        private readonly ILogger<PembaruanViewModel> _logger;
        private readonly CancellationTokenSource _cts = new();

        private string _currentVersionText = string.Empty;
        private string _statusText = "Siap memeriksa pembaruan";
        private bool _isChecking;
        private bool _isDownloading;
        private int _downloadPercent;

        private UpdateInfo? _updateInfo;
        private TambalanInfo? _tambalan;
        private RencanaTambalan? _rencana;

        private string _jenisPembaruanText = string.Empty;
        private string _rincianPembaruanText = string.Empty;
        private string _alasanInstallerText = string.Empty;
        private bool _penawaranTersedia;

        public PembaruanViewModel(
            UpdateService updateService,
            PatchUpdateService patchService,
            IMessageService messageService,
            ILogger<PembaruanViewModel> logger)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _currentVersionText = $"Versi Saat Ini: {GetCurrentVersion()}";

            Perbaikan = new ObservableCollection<string>();
            Riwayat = new ObservableCollection<BarisRiwayatPembaruan>();
            MuatRiwayat();

            CheckCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking && !IsDownloading);
            PerbaruiTambalanCommand = new AsyncRelayCommand(TerapkanTambalanAsync, () => BisaTambalan && !IsDownloading && !IsChecking);
            UnduhInstallerCommand = new AsyncRelayCommand(UnduhInstallerAsync, () => BisaInstaller && !IsDownloading && !IsChecking);
            BatalCommand = new RelayCommand(Batal, () => IsChecking || IsDownloading);
        }

        /// <summary>Versi aplikasi yang sedang dipakai.</summary>
        public Version GetCurrentVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version("1.0.0.0");

        public string CurrentVersionText { get => _currentVersionText; private set => SetProperty(ref _currentVersionText, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (SetProperty(ref _isChecking, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public int DownloadPercent { get => _downloadPercent; private set => SetProperty(ref _downloadPercent, value); }
        public bool ProgressVisible => IsDownloading;

        /// <summary>Judul penawaran, mis. "Pembaruan kecil tersedia: 2.4.5".</summary>
        public string JenisPembaruanText { get => _jenisPembaruanText; private set => SetProperty(ref _jenisPembaruanText, value); }

        /// <summary>Keterangan singkat ukuran & jumlah berkas, mis. "3 berkas • 1,2 MB".</summary>
        public string RincianPembaruanText { get => _rincianPembaruanText; private set => SetProperty(ref _rincianPembaruanText, value); }

        /// <summary>Alasan memakai installer penuh (kosong bila tambalan bisa dipakai).</summary>
        public string AlasanInstallerText { get => _alasanInstallerText; private set => SetProperty(ref _alasanInstallerText, value); }

        /// <summary>Benar bila ada penawaran pembaruan yang bisa ditampilkan.</summary>
        public bool PenawaranTersedia { get => _penawaranTersedia; private set => SetProperty(ref _penawaranTersedia, value); }

        /// <summary>Daftar perbaikan/penambahan yang dibawa pembaruan ini.</summary>
        public ObservableCollection<string> Perbaikan { get; }

        /// <summary>Riwayat pembaruan yang pernah dicoba dipasang (terbaru dulu).</summary>
        public ObservableCollection<BarisRiwayatPembaruan> Riwayat { get; }

        /// <summary>Benar bila pembaruan bisa dipasang sebagai tambalan kecil.</summary>
        public bool BisaTambalan => _tambalan != null && _rencana != null && _rencana.BisaDipakai;

        /// <summary>Benar bila pembaruan harus memakai installer penuh.</summary>
        public bool BisaInstaller => _updateInfo != null && !BisaTambalan && !string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl);

        public string TeksTombolTambalan => BisaTambalan
            ? $"Perbarui Sekarang (tanpa installer)"
            : "Perbarui Sekarang";

        public ICommand CheckCommand { get; }
        public ICommand PerbaruiTambalanCommand { get; }
        public ICommand UnduhInstallerCommand { get; }
        public ICommand BatalCommand { get; }

        // =====================================================================
        // Riwayat pembaruan
        // =====================================================================

        /// <summary>Benar bila ada riwayat pembaruan untuk ditampilkan.</summary>
        public bool RiwayatTersedia { get => _riwayatTersedia; private set => SetProperty(ref _riwayatTersedia, value); }

        private bool _riwayatTersedia;

        /// <summary>Muat (atau muat ulang) riwayat dari disk — dipanggil saat halaman dibuka.</summary>
        public void MuatRiwayat()
        {
            Riwayat.Clear();

            try
            {
                foreach (var entri in RiwayatPembaruanStore.Muat().Take(20))
                {
                    Riwayat.Add(new BarisRiwayatPembaruan(entri));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan tidak dapat dimuat.");
            }

            RiwayatTersedia = Riwayat.Count > 0;
        }

        /// <summary>Catat percobaan pemasangan yang gagal sebelum skrip penerap sempat menulis hasilnya.</summary>
        private void CatatRiwayatGagal(string versi, string jenis, string pesan)
        {
            try
            {
                RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                {
                    Versi = versi,
                    Jenis = jenis,
                    JumlahBerkas = 0,
                    Berhasil = false,
                    Pesan = pesan,
                    DariVersi = GetCurrentVersion().ToString(),
                    Waktu = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan gagal dicatat.");
            }
        }

        /// <summary>Baris siap tampil pada daftar riwayat pembaruan.</summary>
        public class BarisRiwayatPembaruan
        {
            public BarisRiwayatPembaruan(EntriRiwayatPembaruan entri)
            {
                Versi = string.IsNullOrWhiteSpace(entri.Versi) ? "-" : entri.Versi;
                Tanggal = entri.Waktu.ToString("dd MMM yyyy HH:mm");
                Hasil = entri.Berhasil ? "Berhasil" : "Gagal";
                Berhasil = entri.Berhasil;
                Pesan = string.IsNullOrWhiteSpace(entri.Pesan) ? "-" : entri.Pesan;

                Jenis = entri.Jenis switch
                {
                    "Installer" => "Installer penuh",
                    "Tambalan" => "Pembaruan kecil",
                    var lain => lain
                };

                var dari = string.IsNullOrWhiteSpace(entri.DariVersi) ? string.Empty : $" dari {entri.DariVersi}";
                Berkas = entri.JumlahBerkas > 0
                    ? $"{entri.JumlahBerkas} berkas{dari}"
                    : (string.IsNullOrEmpty(dari) ? "-" : $"-{dari.TrimStart('-')}".TrimStart('-'));
                if (entri.JumlahBerkas <= 0 && !string.IsNullOrEmpty(dari)) Berkas = dari.TrimStart(' ') + " • berkas tidak tercatat";
            }

            public string Versi { get; }
            public string Jenis { get; }
            public string Tanggal { get; }
            public string Hasil { get; }
            public bool Berhasil { get; }
            public string Berkas { get; }
            public string Pesan { get; }
        }

        // =====================================================================
        // Pemeriksaan
        // =====================================================================

        private async Task CheckForUpdatesAsync()
        {
            IsChecking = true;
            StatusText = "Memeriksa koneksi internet...";
            KosongkanPenawaran();

            try
            {
                if (!await IsInternetAvailableAsync())
                {
                    StatusText = "Tidak ada koneksi internet";
                    await _messageService.ShowWarningAsync("Tidak ada koneksi internet. Periksa koneksi Anda.");
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
                    StatusText = $"Sudah menggunakan versi terbaru ({GetCurrentVersion()})";
                    await _messageService.ShowInfoAsync("Anda sudah menggunakan versi terbaru.");
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
                StatusText = $"Error: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal memeriksa pembaruan: {ex.Message}");
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
            (BatalCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // =====================================================================
        // Jalur 1: pembaruan kecil (tambalan)
        // =====================================================================

        private async Task TerapkanTambalanAsync()
        {
            if (!BisaTambalan || _updateInfo == null || _tambalan == null || _rencana == null) return;

            var ringkasanPerbaikan = Perbaikan.Count == 0
                ? "Tidak ada rincian perbaikan."
                : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

            var pesan =
                $"Pembaruan kecil {_tambalan.Versi} siap dipasang.\n\n" +
                $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                $"{RincianPembaruanText}\n\n" +
                "Aplikasi akan ditutup sebentar, berkas diganti, lalu dibuka kembali otomatis. " +
                "Data surat, database, dan pengaturan tidak diubah.\n\nLanjutkan?";

            if (!await _messageService.ShowConfirmationAsync("Pembaruan Kecil", pesan)) return;

            IsDownloading = true;
            DownloadPercent = 0;
            StatusText = "Mengunduh paket pembaruan kecil...";

            var progress = new Progress<int>(persen =>
            {
                DownloadPercent = persen;
                StatusText = $"Mengunduh paket pembaruan kecil: {persen}%";
            });

            try
            {
                var siap = await _patchService.UnduhDanSiapkanAsync(
                    _updateInfo, _tambalan, _rencana, progress, _cts.Token);

                DownloadPercent = 100;
                StatusText = $"Menutup aplikasi untuk memasang {_tambalan.Versi}...";

                // Aplikasi harus benar-benar tertutup sebelum berkasnya diganti —
                // skrip penerap menunggu proses ini berakhir, lalu membuka aplikasi lagi.
                _patchService.JalankanPenerapan(siap);

                await Task.Delay(400);
                Application.Current?.Shutdown();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pembaruan dibatalkan";
                await _messageService.ShowWarningAsync("Pemasangan pembaruan kecil dibatalkan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memasang pembaruan kecil");
                StatusText = "Gagal memasang pembaruan kecil";

                // Gagal sebelum skrip penerap jalan — skrip hanya mencatat bila sudah
                // dieksekusi, jadi kegagalan unduhan/verifikasi dicatat di sini.
                CatatRiwayatGagal(_tambalan.Versi, "Tambalan", ex.Message);

                await _messageService.ShowErrorAsync(
                    $"Gagal memasang pembaruan kecil: {ex.Message}\n\n" +
                    "Silakan coba lagi, atau pakai installer penuh (tombol Unduh Installer).");
            }
            finally
            {
                IsDownloading = false;
                DownloadPercent = 0;
            }
        }

        // =====================================================================
        // Jalur 2: installer penuh
        // =====================================================================

        private async Task UnduhInstallerAsync()
        {
            if (_updateInfo == null || string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                await _messageService.ShowWarningAsync("URL unduhan belum tersedia pada informasi rilis.");
                return;
            }

            var ringkasanPerbaikan = Perbaikan.Count == 0
                ? "Tidak ada rincian perbaikan."
                : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

            var pesan = $"Pembaruan {_updateInfo.Version} memakai installer penuh.\n\n" +
                        $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                        (string.IsNullOrWhiteSpace(AlasanInstallerText) ? string.Empty : AlasanInstallerText + "\n\n") +
                        "Installer akan diunduh lalu dijalankan. Lanjutkan?";

            if (!await _messageService.ShowConfirmationAsync("Pembaruan Installer Penuh", pesan)) return;

            IsDownloading = true;
            DownloadPercent = 0;
            StatusText = "Mempersiapkan download installer...";

            var progress = new Progress<int>(persen =>
            {
                DownloadPercent = persen;
                StatusText = $"Download installer: {persen}%";
            });

            try
            {
                string downloadPath = await _updateService.DownloadUpdateAsync(_updateInfo, progress, _cts.Token);

                StatusText = $"Installer tersimpan di: {downloadPath}";
                DownloadPercent = 100;

                var runNow = await _messageService.ShowConfirmationAsync(
                    "Download Selesai",
                    $"Installer tersimpan di:\n{downloadPath}\n\nJalankan installer sekarang?");

                if (runNow)
                {
                    // Catat di riwayat: installer dijalankan pengguna. Hasil akhirnya
                    // tergantung installer; versi terpasang akan berubah bila selesai.
                    RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                    {
                        Versi = _updateInfo.Version ?? "-",
                        Jenis = "Installer",
                        JumlahBerkas = 0,
                        Berhasil = true,
                        Pesan = "Installer penuh dijalankan dari halaman Pembaruan.",
                        DariVersi = GetCurrentVersion().ToString(),
                        Waktu = DateTime.Now
                    });

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadPath,
                        UseShellExecute = true
                    });
                    Application.Current?.Shutdown();
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Download dibatalkan";
                await _messageService.ShowWarningAsync("Proses download dibatalkan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal download installer");
                StatusText = $"Error download: {ex.Message}";
                CatatRiwayatGagal(_updateInfo.Version ?? "-", "Installer", "Unduhan installer gagal: " + ex.Message);
                await _messageService.ShowErrorAsync($"Gagal mendownload installer: {ex.Message}");
            }
            finally
            {
                IsDownloading = false;
                DownloadPercent = 0;
            }
        }

        private void Batal()
        {
            _cts.Cancel();
            StatusText = "Membatalkan...";
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
