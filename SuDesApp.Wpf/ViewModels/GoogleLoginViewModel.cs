using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman "Login dengan Google" — padanan GoogleLoginForm (WinForms).
    /// State: belum dikonfigurasi / belum login / menghubungkan / sudah login.
    /// Setelah berhasil login, tombol utama berubah menjadi "Selesai / Buka Google Drive".
    /// </summary>
    public class GoogleLoginViewModel : ObservableObject, IDisposable
    {
        private readonly GoogleDriveService _driveService;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly ILogger<GoogleLoginViewModel> _logger;
        private readonly GoogleDriveViewModel _driveViewModel;
        private CancellationTokenSource? _cts;

        /// <summary>Batas waktu menunggu login Google di browser sebelum dibatalkan otomatis.</summary>
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(3);

        private string _infoText = string.Empty;
        private string _actionLabel = "Masuk dengan Google";
        private string? _loggedInEmail;
        private bool _isNotConfigured;
        private bool _isConnected;
        private bool _isBusy;

        public GoogleLoginViewModel(
            GoogleDriveService driveService,
            NavigationService navigation,
            IMessageService messageService,
            ILogger<GoogleLoginViewModel> logger,
            GoogleDriveViewModel driveViewModel)
        {
            _driveService = driveService ?? throw new ArgumentNullException(nameof(driveService));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _driveViewModel = driveViewModel ?? throw new ArgumentNullException(nameof(driveViewModel));

            MasukCommand = new AsyncRelayCommand(MasukAsync, () => !IsBusy);
            GantiAkunCommand = new AsyncRelayCommand(GantiAkunAsync, () => IsConnected && !IsBusy);
            BatalCommand = new RelayCommand(Batal);
        }

        /// <summary>Dipanggil setelah berhasil login agar sidebar diperbarui.</summary>
        public Action<string>? OnLoggedIn { get; set; }

        public string InfoText { get => _infoText; private set => SetProperty(ref _infoText, value); }
        public string ActionLabel { get => _actionLabel; private set => SetProperty(ref _actionLabel, value); }
        public bool IsNotConfigured { get => _isNotConfigured; private set { if (SetProperty(ref _isNotConfigured, value)) ((AsyncRelayCommand)MasukCommand).RaiseCanExecuteChanged(); } }
        public bool IsConnected { get => _isConnected; private set { if (SetProperty(ref _isConnected, value)) ((AsyncRelayCommand)GantiAkunCommand).RaiseCanExecuteChanged(); } }
        public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { ((AsyncRelayCommand)MasukCommand).RaiseCanExecuteChanged(); ((AsyncRelayCommand)GantiAkunCommand).RaiseCanExecuteChanged(); } } }

        public ICommand MasukCommand { get; }
        public ICommand GantiAkunCommand { get; }
        public ICommand BatalCommand { get; }

        /// <summary>State awal (padanan GoogleLoginForm.OnShown).</summary>
        public async Task InitializeAsync()
        {
            if (!_driveService.IsOAuthEnabled)
            {
                SetNotConfigured();
                return;
            }

            if (_driveService.HasStoredToken())
            {
                await AuthenticateCoreAsync(showErrors: false);
            }
            else
            {
                SetLoggedOut();
            }
        }

        private async Task MasukAsync()
        {
            if (IsConnected)
            {
                OpenDrive();
                return;
            }
            await AuthenticateCoreAsync(showErrors: true);
        }

        private async Task GantiAkunAsync()
        {
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Ganti Akun Google",
                "Login ulang akan menghapus token tersimpan dan membuka halaman izin Google lagi.\n\nLanjutkan?");
            if (!confirmed) return;

            var cts = GetCts();
            try
            {
                IsBusy = true;
                await Task.Run(() => _driveService.ClearTokenAsync(cts.Token), cts.Token);
                await AuthenticateCoreAsync(showErrors: true);
            }
            catch (OperationCanceledException)
            {
                SetLoggedOut();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus token Google");
                SetLoggedOut();
                await _messageService.ShowErrorAsync($"Gagal mengganti akun.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AuthenticateCoreAsync(bool showErrors)
        {
            SetConnecting();
            var cts = GetCts();
            try
            {
                // Timeout mencegah UI terkunci selamanya bila pengguna menutup
                // tab browser tanpa menyelesaikan login (callback tidak pernah tiba).
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                timeoutCts.CancelAfter(LoginTimeout);

                var email = await Task.Run(() => _driveService.GetAccountEmailAsync(timeoutCts.Token), timeoutCts.Token);

                if (string.IsNullOrWhiteSpace(email))
                {
                    SetLoggedOut();
                    if (showErrors)
                    {
                        await _messageService.ShowWarningAsync(
                            "Tidak diperoleh alamat email akun Google.\n\n" +
                            "Kemungkinan Google Drive API belum diaktifkan pada proyek Cloud yang dipakai, " +
                            "atau consent screen masih berstatus \"Testing\" tanpa akun ini sebagai test user.");
                    }
                    return;
                }

                _logger.LogInformation("Login Google berhasil untuk {Email}", email);
                SetConnected(email);
                OnLoggedIn?.Invoke(email);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                // Dibatalkan pengguna lewat tombol Batal.
                _logger.LogInformation("Login Google dibatalkan pengguna.");
                SetLoggedOut();
            }
            catch (OperationCanceledException)
            {
                // Timeout tunggu login di browser.
                _logger.LogWarning("Login Google melebihi batas waktu {Timeout} menit.", LoginTimeout.TotalMinutes);
                SetLoggedOut();
                if (showErrors)
                {
                    await _messageService.ShowWarningAsync(
                        $"Waktu tunggu login habis ({LoginTimeout.TotalMinutes:0} menit).\n\n" +
                        "Selesaikan login di browser yang terbuka, lalu klik \"Masuk dengan Google\" lagi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal login Google");
                SetLoggedOut();
                if (showErrors)
                {
                    await _messageService.ShowErrorAsync($"Gagal login Google.\n\n{ex.Message}");
                }
            }
        }

        private void OpenDrive()
        {
            try
            {
                _navigation.Navigate(_driveViewModel);
                _ = _driveViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Google Drive");
                _ = _messageService.ShowErrorAsync("Gagal membuka Google Drive: " + ex.Message);
            }
        }

        /// <summary>
        /// Kembalikan CTS aktif. CTS yang sudah dibatalkan (mis. lewat tombol Batal)
        /// diganti baru agar percobaan login berikutnya tidak langsung gagal.
        /// </summary>
        private CancellationTokenSource GetCts()
        {
            if (_cts == null || _cts.IsCancellationRequested)
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
            }
            return _cts;
        }

        /// <summary>Kembali ke halaman utama DAN batalkan proses login yang sedang berjalan.</summary>
        private void Batal()
        {
            try
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _logger.LogInformation("Proses login Google dibatalkan lewat tombol Batal.");
                    _cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membatalkan proses login Google");
            }
            _navigation.ShowDefault();
        }

        private void SetNotConfigured()
        {
            _loggedInEmail = null;
            IsConnected = false;
            IsNotConfigured = true;
            IsBusy = false;
            InfoText = "Koneksi akun Google belum diaktifkan.\n\n" +
                       "Kredensial klien OAuth (Client ID & Client Secret) belum tersimpan di komputer ini. " +
                       "Hubungi teknisi untuk menyimpannya (terenkripsi di " + GoogleDriveService.CredentialsStorePath + "), " +
                       "atau isi bagian GoogleDrive pada appsettings.json, lalu jalankan ulang aplikasi.";
            ActionLabel = "Masuk dengan Google";
        }

        private void SetLoggedOut()
        {
            _loggedInEmail = null;
            IsConnected = false;
            IsNotConfigured = false;
            IsBusy = false;
            InfoText = "Masuk dengan akun Google untuk mencadangkan arsip surat ke Google Drive.\n" +
                       "Browser Google akan terbuka untuk menyelesaikan izin.";
            ActionLabel = "Masuk dengan Google";
        }

        private void SetConnecting()
        {
            IsConnected = false;
            IsNotConfigured = false;
            IsBusy = true;
            InfoText = "Menghubungkan ke Google...\nSelesaikan login di browser bila diminta.";
            ActionLabel = "Masuk dengan Google";
        }

        private void SetConnected(string email)
        {
            _loggedInEmail = email;
            IsConnected = true;
            IsNotConfigured = false;
            IsBusy = false;
            InfoText = $"Berhasil masuk sebagai:\n{email}\n\n" +
                       "Klik \"Buka Google Drive\" untuk melanjutkan.";
            ActionLabel = "Buka Google Drive";
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}