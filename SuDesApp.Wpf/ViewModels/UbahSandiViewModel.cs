using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel untuk halaman Ubah Password.
    /// </summary>
    public class UbahSandiViewModel : ObservableObject
    {
        private readonly ILogger<UbahSandiViewModel> _logger;
        private readonly string _loginFilePath;

        private string _passwordLama = string.Empty;
        private string _passwordBaru = string.Empty;
        private string _konfirmasiPassword = string.Empty;
        private bool _tampilkanPassword;
        private string _statusMessage = string.Empty;
        private bool _isError;

        public UbahSandiViewModel(ILogger<UbahSandiViewModel> logger)
        {
            _logger = logger;
            _loginFilePath = Path.Combine(AppContext.BaseDirectory, "Login.dat");
            SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        }

        public string PasswordLama { get => _passwordLama; set { SetProperty(ref _passwordLama, value); ((AsyncRelayCommand)SaveCommand).RaiseCanExecuteChanged(); } }
        public string PasswordBaru { get => _passwordBaru; set { SetProperty(ref _passwordBaru, value); ((AsyncRelayCommand)SaveCommand).RaiseCanExecuteChanged(); } }
        public string KonfirmasiPassword { get => _konfirmasiPassword; set { SetProperty(ref _konfirmasiPassword, value); ((AsyncRelayCommand)SaveCommand).RaiseCanExecuteChanged(); } }
        public bool TampilkanPassword { get => _tampilkanPassword; set => SetProperty(ref _tampilkanPassword, value); }
        public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => SetProperty(ref _isError, value); }
        public string StatusColor => IsError ? "#C62828" : "#2E7D32";

        public ICommand SaveCommand { get; }

        private bool CanSave() =>
            !string.IsNullOrEmpty(PasswordLama) &&
            !string.IsNullOrEmpty(PasswordBaru) &&
            PasswordBaru.Length >= 8 &&
            PasswordBaru == KonfirmasiPassword;

        private string GenerateSalt()
        {
            byte[] salt = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }

        private string ComputeSaltedHash(string password, string salt)
        {
            using var sha256 = SHA256.Create();
            byte[] saltedPassword = Encoding.UTF8.GetBytes(password + salt);
            byte[] hash = sha256.ComputeHash(saltedPassword);
            return Convert.ToBase64String(hash);
        }

        private void SecureWriteToFile(string content)
        {
            byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(
                Encoding.UTF8.GetBytes(content),
                null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_loginFilePath, encrypted);
        }

        private string SecureReadFromFile()
        {
            byte[] encrypted = File.ReadAllBytes(_loginFilePath);
            byte[] decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                encrypted,
                null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }

        private async Task SaveAsync()
        {
            try
            {
                if (!File.Exists(_loginFilePath))
                {
                    StatusMessage = "File login tidak ditemukan.";
                    IsError = true;
                    return;
                }

                var content = SecureReadFromFile();
                var parts = content.Split(':');
                if (parts.Length != 3)
                {
                    StatusMessage = "Format file login tidak valid.";
                    IsError = true;
                    return;
                }

                var storedUsername = parts[0];
                var storedSalt = parts[1];
                var storedHash = parts[2];

                var oldHash = ComputeSaltedHash(PasswordLama, storedSalt);
                if (oldHash != storedHash)
                {
                    StatusMessage = "Password lama salah.";
                    IsError = true;
                    return;
                }

                var newSalt = GenerateSalt();
                var newHash = ComputeSaltedHash(PasswordBaru, newSalt);
                SecureWriteToFile($"{storedUsername}:{newSalt}:{newHash}");

                StatusMessage = "Password berhasil diubah!";
                IsError = false;
                _logger.LogInformation("Password berhasil diubah");

                PasswordLama = string.Empty;
                PasswordBaru = string.Empty;
                KonfirmasiPassword = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah password");
                StatusMessage = $"Gagal mengubah password: {ex.Message}";
                IsError = true;
            }
        }
    }
}