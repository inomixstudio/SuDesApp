using System;
using System.Diagnostics;
using System.IO;
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
    /// ViewModel halaman Pembaruan Aplikasi — padanan UpdateApps (WinForms).
    /// Menggunakan UpdateService (Core) untuk cek versi di Google Drive.
    /// </summary>
    public class PembaruanViewModel : ObservableObject
    {
        private readonly UpdateService _updateService;
        private readonly IMessageService _messageService;
        private readonly ILogger<PembaruanViewModel> _logger;
        private readonly CancellationTokenSource _cts = new();

        private string _currentVersionText = string.Empty;
        private string _statusText = "Siap memeriksa pembaruan";
        private bool _isChecking;
        private bool _isDownloading;
        private int _downloadPercent;
        private UpdateInfo? _updateInfo;

        public PembaruanViewModel(
            UpdateService updateService,
            IMessageService messageService,
            ILogger<PembaruanViewModel> logger)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _currentVersionText = $"Versi Saat Ini: {GetCurrentVersion()}";

            CheckCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking && !IsDownloading);
            BatalCommand = new RelayCommand(Batal, () => IsChecking || IsDownloading);
        }

        public string CurrentVersionText { get => _currentVersionText; private set => SetProperty(ref _currentVersionText, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public bool IsChecking { get => _isChecking; private set { if (SetProperty(ref _isChecking, value)) ((AsyncRelayCommand)CheckCommand).RaiseCanExecuteChanged(); ((RelayCommand)BatalCommand).RaiseCanExecuteChanged(); } }
        public bool IsDownloading { get => _isDownloading; private set { if (SetProperty(ref _isDownloading, value)) { ((AsyncRelayCommand)CheckCommand).RaiseCanExecuteChanged(); ((RelayCommand)BatalCommand).RaiseCanExecuteChanged(); } } }
        public int DownloadPercent { get => _downloadPercent; private set => SetProperty(ref _downloadPercent, value); }
        public bool ProgressVisible => IsDownloading;

        public ICommand CheckCommand { get; }
        public ICommand BatalCommand { get; }

        private Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version("1.0.0.0");
        }

        private async Task CheckForUpdatesAsync()
        {
            IsChecking = true;
            StatusText = "Memeriksa koneksi internet...";
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

                if (IsNewerVersionAvailable())
                {
                    StatusText = $"Versi baru ditemukan: {_updateInfo.Version}";
                    await PromptAndDownloadUpdateAsync();
                }
                else
                {
                    StatusText = "Sudah menggunakan versi terbaru";
                    await _messageService.ShowInfoAsync("Anda sudah menggunakan versi terbaru.");
                }
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

        private async Task PromptAndDownloadUpdateAsync()
        {
            if (string.IsNullOrWhiteSpace(_updateInfo?.DownloadUrl))
            {
                await _messageService.ShowWarningAsync("URL unduhan belum tersedia pada version.json.");
                return;
            }

            var message = $"Versi baru tersedia: {_updateInfo.Version}\n" +
                          $"Catatan rilis: {_updateInfo.ReleaseNotes ?? "Tidak ada catatan."}\n\n" +
                          "Download sekarang?";

            var confirmed = await _messageService.ShowConfirmationAsync("Pembaruan Tersedia", message);
            if (confirmed)
            {
                await DownloadUpdateAsync();
            }
        }

        private async Task DownloadUpdateAsync()
        {
            IsDownloading = true;
            DownloadPercent = 0;
            StatusText = "Mempersiapkan download...";

            var progress = new Progress<int>(percent =>
            {
                DownloadPercent = percent;
                StatusText = $"Download: {percent}%";
            });

            try
            {
                string downloadPath = await _updateService.DownloadUpdateAsync(
                    _updateInfo!, progress, _cts.Token);

                StatusText = $"Pembaruan tersimpan di: {downloadPath}";
                DownloadPercent = 100;

                var runNow = await _messageService.ShowConfirmationAsync(
                    "Download Selesai",
                    $"Pembaruan tersimpan di: {downloadPath}\n\nJalankan file sekarang?");

                if (runNow)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = downloadPath,
                            UseShellExecute = true
                        });
                        Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menjalankan file pembaruan");
                        await _messageService.ShowErrorAsync($"Gagal menjalankan pembaruan: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Download dibatalkan";
                await _messageService.ShowWarningAsync("Proses download dibatalkan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal download pembaruan");
                StatusText = $"Error download: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal mendownload pembaruan: {ex.Message}");
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