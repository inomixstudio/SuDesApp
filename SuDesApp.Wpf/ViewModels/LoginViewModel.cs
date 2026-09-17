using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel LoginWindow: mendukung dua jalur login.
    /// 1. Login Google otomatis — bila ada token akun Google tersimpan
    ///    (pernah login lewat menu Login Google), form menampilkan progres
    ///    "Masuk sebagai {email}" lengkap dengan foto profil, lalu masuk
    ///    sebagai admin tanpa mengetik username/password.
    /// 2. Login manual — username &amp; password (admin/admin pada pemasangan
    ///    baru), tetap tersedia lewat tombol "Masuk dengan Username".
    /// </summary>
    public class LoginViewModel : ObservableObject
    {
        private readonly string _loginFilePath;
        private readonly GoogleDriveService? _googleDrive;
        private int _failedAttempts;
        private DateTime _lastFailedAttempt = DateTime.MinValue;

        private string _username = string.Empty;
        private string _password = string.Empty;
        private bool _showPassword;
        private string _errorMessage = string.Empty;
        private bool _hasError;
        private bool _isAutoLoginInProgress;
        private bool _isGoogleLogin;
        private string _googleEmail = string.Empty;
        private string _googleDisplayName = string.Empty;
        private byte[]? _googlePhotoBytes;
        private CancellationTokenSource? _autoLoginCts;
        private bool _manualModeForced;

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                SetProperty(ref _errorMessage, value);
                HasError = !string.IsNullOrEmpty(value);
            }
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        /// <summary>Proses verifikasi login sedang berjalan (progres tampil).</summary>
        public bool IsBusy
        {
            get => _isAutoLoginInProgress;
            private set => SetProperty(ref _isAutoLoginInProgress, value);
        }

        /// <summary>Overlay login Google sedang tampil (menutupi form manual).</summary>
        public bool IsGoogleLogin
        {
            get => _isGoogleLogin;
            private set => SetProperty(ref _isGoogleLogin, value);
        }

        /// <summary>Email akun Google yang terdeteksi.</summary>
        public string GoogleEmail
        {
            get => _googleEmail;
            private set => SetProperty(ref _googleEmail, value);
        }

        /// <summary>Nama tampilan akun Google (fallback ke email bila kosong).</summary>
        public string GoogleDisplayName
        {
            get => _googleDisplayName;
            private set => SetProperty(ref _googleDisplayName, value);
        }

        /// <summary>Foto profil akun Google (byte PNG/JPEG dari Google).</summary>
        public byte[]? GooglePhotoBytes
        {
            get => _googlePhotoBytes;
            private set => SetProperty(ref _googlePhotoBytes, value);
        }

        public ICommand LoginCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand GoogleLoginNowCommand { get; }
        public ICommand CancelGoogleLoginCommand { get; }

        public event Action? LoginSucceeded;
        public event Action? LoginCancelled;

        public LoginViewModel(GoogleDriveService? googleDrive = null)
        {
            _googleDrive = googleDrive;
            _loginFilePath = Path.Combine(AppContext.BaseDirectory, "Login.dat");
            InitializeLoginFile();
            LoginCommand = new AsyncRelayCommand(LoginAsync);
            CancelCommand = new RelayCommand(() => LoginCancelled?.Invoke());
            GoogleLoginNowCommand = new AsyncRelayCommand(StartGoogleAutoLoginAsync);
            CancelGoogleLoginCommand = new RelayCommand(CancelGoogleAutoLogin);
        }

        /// <summary>
        /// Mulai pemeriksaan token Google saat window siap (dipanggil dari view
        /// setelah Load). Bila token tersimpan ditemukan, verifikasi senyap
        /// dijalankan dan overlay "Masuk sebagai ..." tampil.
        /// </summary>
        public async void InitializeGoogleAutoLogin()
        {
            if (_manualModeForced) return;
            if (_googleDrive == null || !_googleDrive.IsOAuthEnabled || !_googleDrive.HasStoredToken())
                return;

            // Hormati preferensi pengguna: bila dimatikan di halaman Setelan,
            // form username/password selalu tampil lebih dulu (akun Google
            // masih bisa dipakai lewat tombol "Masuk dengan Akun Google").
            if (!LoginPreferenceStore.IsGoogleAutoLoginEnabled())
            {
                System.Diagnostics.Debug.WriteLine("Login otomatis Google dinonaktifkan lewat Setelan — form manual ditampilkan.");
                return;
            }

            IsBusy = true;
            IsGoogleLogin = true;
            GoogleEmail = "Memeriksa akun Google...";
            GoogleDisplayName = "";
            GooglePhotoBytes = null;
            ErrorMessage = "";

            try
            {
                var info = await _googleDrive.GetAccountInfoAsync(GetCts()).ConfigureAwait(true);
                if (info == null)
                    throw new Exception("Informasi akun tidak diperoleh.");

                GoogleEmail = info.Email;
                GoogleDisplayName = info.DisplayName;
                GooglePhotoBytes = info.PhotoBytes;

                // Login berhasil — setara admin/admin.
                CompleteGoogleLogin();
            }
            catch (OperationCanceledException)
            {
                ShowManualForm("Login Google dibatalkan.");
            }
            catch (Exception ex)
            {
                // Token bermasalah (kadaluarsa, akun dicabut, konfigurasi
                // berubah) — kembali ke form manual dengan pesan jelas.
                ShowManualForm("Login Google gagal: " + ex.Message);
            }
        }

        /// <summary>Login Google manual dari tombol (bila pengguna memilih Google).</summary>
        private async Task StartGoogleAutoLoginAsync()
        {
            if (_googleDrive == null || !_googleDrive.IsOAuthEnabled)
            {
                ErrorMessage = "Login Google belum dikonfigurasi (kredensial klien OAuth perlu disimpan di komputer ini).";
                return;
            }

            IsBusy = true;
            IsGoogleLogin = true;
            GoogleEmail = "Membuka browser untuk login Google...";
            GoogleDisplayName = "";
            GooglePhotoBytes = null;
            ErrorMessage = "";

            try
            {
                var info = await _googleDrive.GetAccountInfoAsync(GetCts()).ConfigureAwait(true);
                if (info == null)
                    throw new Exception("Informasi akun tidak diperoleh.");

                GoogleEmail = info.Email;
                GoogleDisplayName = info.DisplayName;
                GooglePhotoBytes = info.PhotoBytes;
                CompleteGoogleLogin();
            }
            catch (OperationCanceledException)
            {
                ShowManualForm("Login Google dibatalkan.");
            }
            catch (Exception ex)
            {
                ShowManualForm("Login Google gagal: " + ex.Message);
            }
        }

        private void CancelGoogleAutoLogin()
        {
            try { _autoLoginCts?.Cancel(); } catch { }
        }

        private void InitializeLoginFile()
        {
            try
            {
                if (!File.Exists(_loginFilePath))
                {
                    string salt = GenerateSalt();
                    string hashed = ComputeSaltedHash("admin", salt);
                    SecureWriteToFile($"admin:{salt}:{hashed}");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal inisialisasi sistem login: {ex.Message}";
            }
        }

        private void CompleteGoogleLogin()
        {
            CurrentUserName = string.IsNullOrWhiteSpace(GoogleDisplayName) ? GoogleEmail : GoogleDisplayName;
            SessionContext.Set(GoogleEmail, "google");
            IsBusy = false;
            IsGoogleLogin = false;
            LoginSucceeded?.Invoke();
        }

        /// <summary>Tutup overlay Google dan tampilkan form manual dengan pesan.</summary>
        private void ShowManualForm(string? message)
        {
            IsBusy = false;
            IsGoogleLogin = false;
            _manualModeForced = true;
            if (!string.IsNullOrEmpty(message))
                ErrorMessage = message;
        }

        private CancellationToken GetCts()
        {
            if (_autoLoginCts != null)
            {
                if (!_autoLoginCts.IsCancellationRequested)
                    return _autoLoginCts.Token;
                _autoLoginCts.Dispose();
                _autoLoginCts = null;
            }
            _autoLoginCts = new CancellationTokenSource();
            return _autoLoginCts.Token;
        }

        private async Task LoginAsync()
        {
            if (_failedAttempts >= 3 && (DateTime.Now - _lastFailedAttempt).TotalMinutes < 5)
            {
                ErrorMessage = "Terlalu banyak percobaan gagal. Tunggu 5 menit.";
                return;
            }

            string username = Username.ToLower().Trim();
            string password = Password.ToLower().Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ErrorMessage = "Username dan password harus diisi.";
                return;
            }

            try
            {
                string fileContent = SecureReadFromFile();
                string[] parts = fileContent.Split(':');

                if (parts.Length != 3)
                {
                    ErrorMessage = "Format file login tidak valid.";
                    return;
                }

                string storedUsername = parts[0];
                string storedSalt = parts[1];
                string storedHash = parts[2];

                string inputHash = ComputeSaltedHash(password, storedSalt);

                if (username == storedUsername && inputHash == storedHash)
                {
                    CurrentUserName = username;
                    SessionContext.Set(username, "manual");
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    _failedAttempts++;
                    _lastFailedAttempt = DateTime.Now;
                    ErrorMessage = "Username atau password salah.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal login: {ex.Message}";
            }

            await Task.CompletedTask;
        }

        public static string CurrentUserName { get; private set; } = "Operator";

        private static string GenerateSalt()
        {
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            return Convert.ToBase64String(salt);
        }

        private static string ComputeSaltedHash(string password, string salt)
        {
            using var sha256 = SHA256.Create();
            byte[] saltedPassword = Encoding.UTF8.GetBytes(password + salt);
            byte[] hash = sha256.ComputeHash(saltedPassword);
            return Convert.ToBase64String(hash);
        }

        private void SecureWriteToFile(string content)
        {
            byte[] encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(content),
                null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_loginFilePath, encrypted);
        }

        private string SecureReadFromFile()
        {
            byte[] encrypted = File.ReadAllBytes(_loginFilePath);
            byte[] decrypted = ProtectedData.Unprotect(
                encrypted,
                null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }

        public void ChangePassword(string oldPassword, string newPassword)
        {
            string fileContent = SecureReadFromFile();
            string[] parts = fileContent.Split(':');

            if (parts.Length != 3)
                throw new InvalidOperationException("Format file login tidak valid.");

            string storedUsername = parts[0];
            string storedSalt = parts[1];
            string storedHash = parts[2];

            string inputHash = ComputeSaltedHash(oldPassword.ToLower().Trim(), storedSalt);

            if (inputHash != storedHash)
                throw new UnauthorizedAccessException("Password lama salah.");

            if (newPassword.Length < 8)
                throw new ArgumentException("Password baru minimal 8 karakter.");

            string newSalt = GenerateSalt();
            string newHash = ComputeSaltedHash(newPassword.ToLower().Trim(), newSalt);
            SecureWriteToFile($"{storedUsername}:{newSalt}:{newHash}");
        }
    }
}
