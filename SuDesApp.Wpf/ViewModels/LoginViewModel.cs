using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel LoginWindow: SATU-satunya jalur masuk aplikasi adalah akun
    /// aplikasi — username dan kata sandi (admin/admin pada pemasangan baru;
    /// akun lama Login.dat dimigrasikan ke tabel Pengguna, lihat
    /// docs/policy-peran-pengguna.md).
    ///
    /// Sejak versi 2.6.0, login utama memakai <see cref="IPenggunaService.MasukAsync"/>
    /// (PBKDF2, kunci 3× gagal, peran dari database). Akun admin/admin bawaan
    /// disisipkan ke tabel Pengguna saat pemasangan baru sehingga langsung muncul
    /// di halaman Kelola Pengguna. Login.dat masih dibaca sebagai cadangan untuk
    /// instalasi lama yang belum dimigrasi — bila cocok, akunnya dipindahkan ke
    /// tabel Pengguna secara otomatis.
    ///
    /// Satu pengecualian pada aturan migrasi itu: akun pusat penyedia
    /// (<see cref="AkunPusat"/>, penanda <c>pusat</c> di Login.dat). Akun itu
    /// masuk LANGSUNG dari berkas dan sengaja TIDAK pernah dibuatkan baris di
    /// tabel Pengguna, supaya namanya tidak muncul di halaman Kelola Pengguna.
    /// Hanya sesi dari akun inilah menu Pusat Administrasi ditampilkan.
    ///
    /// Login Google sudah DIBONGKAR dari layar login: koneksi akun Google
    /// hanya melayani fitur layanan (Drive/Sheets/WhatsApp) dan disambungkan
    /// SETELAH masuk lewat halaman Layanan Online — ia bukan jalan masuk,
    /// sehingga sesi selalu melewati peran dan kunci akun aplikasi.
    /// </summary>
    public class LoginViewModel : ObservableObject
    {
        private readonly string _loginFilePath;
        private readonly IPenggunaService? _penggunaService;
        private readonly ILogger<LoginViewModel>? _logger;
        private int _failedAttempts;
        private DateTime _lastFailedAttempt = DateTime.MinValue;

        private string _username = string.Empty;
        private string _password = string.Empty;
        private bool _showPassword;
        private string _errorMessage = string.Empty;
        private bool _hasError;
        private bool _isAutoLoginInProgress;

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

        /// <summary>
        /// Teks versi di kaki jendela login ("Surat Desa V.x.y.z  •  ...") —
        /// disusun otomatis dari atribut assembly (csproj) oleh <see cref="IdentitasAplikasi"/>,
        /// sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string FooterVersi => IdentitasAplikasi.FooterVersi;

        /// <summary>Proses verifikasi login sedang berjalan (progres tampil).</summary>
        public bool IsBusy
        {
            get => _isAutoLoginInProgress;
            private set => SetProperty(ref _isAutoLoginInProgress, value);
        }

        public ICommand LoginCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? LoginSucceeded;
        public event Action? LoginCancelled;

        public LoginViewModel(
            IPenggunaService? penggunaService = null,
            ILogger<LoginViewModel>? logger = null)
        {
            _loginFilePath = BerkasLogin.JalurBawaan;
            _penggunaService = penggunaService;
            _logger = logger;
            SiapkanBerkasAkun();
            LoginCommand = new AsyncRelayCommand(LoginAsync);
            CancelCommand = new RelayCommand(() => LoginCancelled?.Invoke());
        }

        /// <summary>
        /// Dipanggil dari startup (App.xaml.cs) sebelum LoginWindow ditampilkan:
        /// menyisipkan akun administrator default bila tabel Pengguna masih kosong.
        /// Tidak boleh fire-and-forget — bila belum selesai saat pengguna mengklik
        /// Login, MasukAsync tidak akan menemukan akun admin.
        /// </summary>
        public async Task InisialisasiAsync()
        {
            await PastikanAkunAdminAsync();
        }

        /// <summary>
        /// Siapkan berkas akun lokal: akun desa bawaan (admin/admin) pada
        /// pemasangan baru, lalu pastikan akun pusat penyedia ada. Keduanya
        /// hanya DITAMBAHKAN bila belum ada — kata sandi yang pernah berganti
        /// tidak pernah dikembalikan ke bawaan.
        /// </summary>
        private void SiapkanBerkasAkun()
        {
            try
            {
                if (!File.Exists(_loginFilePath))
                {
                    BerkasLogin.Tulis(_loginFilePath, new[]
                    {
                        BuatAkunBawaan("admin", "admin"),
                        BuatAkunBawaan(AkunPusat.Username, AkunPusat.KataSandiBawaan, pusat: true)
                    });

                    _logger?.LogInformation("Berkas akun lokal dibuat (akun desa bawaan + akun pusat).");
                    return;
                }

                // Berkas sudah ada: tambahkan akun pusat bila belum tercatat.
                // Akun desa bawaan TIDAK ditambahkan di sini — berkas lama yang
                // sudah memuatnya, dan instalasi yang akunnya sudah dimigrasikan
                // ke database tidak perlu dikembalikan ke admin/admin.
                if (!PastikanAkunPusat())
                {
                    _logger?.LogWarning(
                        "Akun pusat belum tentu ada: berkas {Berkas} tidak terbaca.", _loginFilePath);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal inisialisasi sistem login: {ex.Message}";
            }
        }

        /// <summary>Tambahkan akun pusat ke berkas akun bila belum ada. False bila berkas tidak terbaca.</summary>
        private bool PastikanAkunPusat()
        {
            var akun = BerkasLogin.Baca(_loginFilePath);
            if (akun.Count == 0)
            {
                return false;
            }

            if (akun.Exists(a => a.Pusat && AkunPusat.Namanya(a.Username)))
            {
                return true;
            }

            akun.Add(BuatAkunBawaan(AkunPusat.Username, AkunPusat.KataSandiBawaan, pusat: true));
            BerkasLogin.Tulis(_loginFilePath, akun);
            _logger?.LogInformation("Akun pusat ditambahkan ke berkas akun lokal.");
            return true;
        }

        private static AkunLogin BuatAkunBawaan(string username, string kataSandi, bool pusat = false)
        {
            string garam = BerkasLogin.BuatGaram();
            return new AkunLogin(username, garam, BerkasLogin.HitungHash(kataSandi, garam), pusat);
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
                // Jalur utama: login via PenggunaService (PBKDF2, kunci 3× gagal,
                // peran dari database). Akun yang berhasil masuk membawa peran
                // dan nama tampilan yang langsung diset ke sesi.
                if (_penggunaService != null)
                {
                    var hasil = await _penggunaService.MasukAsync(username, password);
                    if (hasil.Berhasil && hasil.Pengguna != null)
                    {
                        var akun = hasil.Pengguna;
                        CurrentUserName = akun.Username;
                        SessionContext.SetIdentitas(
                            akun.Username, "manual", akun.NamaTampilan, akun.Peran);
                        SessionContext.TandaiSesiPusat(false);
                        LoginSucceeded?.Invoke();
                        return;
                    }

                    // Akun pusat penyedia: masuk langsung dari berkas akun lokal,
                    // tanpa baris di tabel Pengguna (lihat PastikanAkunPusat).
                    if (!hasil.Terkunci && CobaMasukAkunPusat(username, password))
                    {
                        return; // LoginSucceeded sudah dipanggil di dalamnya
                    }

                    // Bila PenggunaService tidak menemukan akun, coba migrasi dari
                    // Login.dat lama — mungkin instalasi belum sempat dimigrasi.
                    if (!hasil.Terkunci && await CobaMigrasiLoginDatAsync(username, password))
                    {
                        return; // LoginSucceeded sudah dipanggil di dalam metode migrasi
                    }

                    _failedAttempts++;
                    _lastFailedAttempt = DateTime.Now;
                    ErrorMessage = hasil.Pesan;
                    return;
                }

                // Fallback bila IPenggunaService tidak tersedia (tidak seharusnya
                // terjadi lewat DI, tetapi menjaga kompatibilitas mundur).
                if (LoginViaLoginDatAsync(username, password))
                    return;

                _failedAttempts++;
                _lastFailedAttempt = DateTime.Now;
                ErrorMessage = "Username atau password salah.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal login: {ex.Message}";
            }
        }

        /// <summary>
        /// Sisipkan akun administrator default (admin/admin) ke tabel Pengguna
        /// saat database masih kosong — yaitu pemasangan baru. Akun ini langsung
        /// muncul di halaman Kelola Pengguna dan bisa diganti kata sandinya.
        /// </summary>
        private async Task PastikanAkunAdminAsync()
        {
            if (_penggunaService == null) return;

            try
            {
                if (await _penggunaService.AdaPenggunaAsync()) return;

                await _penggunaService.TambahAsync(
                    "admin", "Administrator",
                    PeranPengguna.Administrator, "admin",
                    oleh: "sistem", dariMigrasi: true);

                _logger?.LogInformation("Akun administrator default (admin/admin) disisipkan ke tabel Pengguna.");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan startup bila penyisipan gagal — Login.dat
                // masih bisa dipakai sebagai cadangan.
                _logger?.LogWarning(ex, "Gagal menyisipkan akun admin default; berlanjut dengan Login.dat.");
            }
        }

        /// <summary>
        /// Masuk sebagai akun pusat penyedia dari berkas akun lokal — TANPA
        /// membuat baris di tabel Pengguna, sehingga akun ini tidak pernah
        /// muncul di halaman Kelola Pengguna dan tidak ikut tercadang bersama
        /// data desa. Dipanggil hanya setelah login database gagal, dan hanya
        /// untuk nama pengguna yang memang akun pusat.
        ///
        /// Sesi ditandai lewat <see cref="SessionContext.TandaiSesiPusat"/>:
        /// menu Pusat Administrasi bergantung pada penanda itu, bukan sekadar
        /// nama pengguna — akun database bernama sama tetap tidak membukanya.
        /// </summary>
        private bool CobaMasukAkunPusat(string username, string password)
        {
            if (!AkunPusat.Namanya(username))
            {
                return false;
            }

            try
            {
                var akun = BerkasLogin.Cari(_loginFilePath, username);
                if (akun is not { Pusat: true } || !BerkasLogin.Cocok(akun, password))
                {
                    return false;
                }

                CurrentUserName = akun.Username;
                SessionContext.SetIdentitas(
                    akun.Username, "manual", AkunPusat.NamaTampilan, PeranPengguna.Administrator);
                SessionContext.TandaiSesiPusat(true);

                _logger?.LogInformation(
                    "Akun pusat masuk dari berkas akun lokal (tanpa baris di tabel Pengguna).");
                LoginSucceeded?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Masuk sebagai akun pusat gagal (berkas akun tidak terbaca).");
                return false;
            }
        }

        /// <summary>
        /// Migrasi akun dari Login.dat lama ke tabel Pengguna. Dipanggil saat
        /// PenggunaService tidak menemukan username yang dimasukkan — kemungkinan
        /// instalasi lama yang belum dimigrasi. Bila kata sandi cocok dengan
        /// Login.dat, akun dipindahkan ke tabel Pengguna dan login berhasil.
        ///
        /// Akun pusat DILEWATI di sini: akun itu memang harus tetap hidup di
        /// berkas lokal, bukan di tabel Pengguna (lihat <see cref="CobaMasukAkunPusat"/>).
        /// </summary>
        private async Task<bool> CobaMigrasiLoginDatAsync(string username, string password)
        {
            if (_penggunaService == null || !File.Exists(_loginFilePath)) return false;

            try
            {
                var lama = BerkasLogin.Cari(_loginFilePath, username);
                if (lama is null || lama.Pusat || !BerkasLogin.Cocok(lama, password))
                    return false;

                // Login.dat cocok — pindahkan akun ke tabel Pengguna.
                var akun = await _penggunaService.TambahAsync(
                    username, "Administrator",
                    PeranPengguna.Administrator, password,
                    oleh: "migrasi-login.dat", dariMigrasi: true);

                CurrentUserName = akun.Username;
                SessionContext.SetIdentitas(
                    akun.Username, "manual", akun.NamaTampilan, akun.Peran);
                SessionContext.TandaiSesiPusat(false);

                _logger?.LogInformation("Akun '{Username}' dimigrasi dari Login.dat ke tabel Pengguna.", username);
                LoginSucceeded?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Migrasi Login.dat gagal untuk username '{Username}'", username);
                return false;
            }
        }

        /// <summary>
        /// Cadangan bila IPenggunaService tidak tersedia: verifikasi langsung ke
        /// berkas akun lokal. Akun pusat ikut diterima di sini (tidak ada tabel
        /// Pengguna untuk dijadikan tujuan migrasi), dengan penanda sesi pusat
        /// supaya menunya tetap hanya terbuka untuk akun itu.
        /// </summary>
        private bool LoginViaLoginDatAsync(string username, string password)
        {
            var akun = BerkasLogin.Cari(_loginFilePath, username);
            if (akun is null || !BerkasLogin.Cocok(akun, password))
            {
                return false;
            }

            bool pusat = akun.Pusat && AkunPusat.Namanya(akun.Username);
            if (pusat)
            {
                CurrentUserName = akun.Username;
                SessionContext.SetIdentitas(
                    akun.Username, "manual", AkunPusat.NamaTampilan, PeranPengguna.Administrator);
            }
            else
            {
                CurrentUserName = akun.Username;
                SessionContext.Set(akun.Username, "manual");
            }

            SessionContext.TandaiSesiPusat(pusat);
            LoginSucceeded?.Invoke();
            return true;
        }

        public static string CurrentUserName { get; private set; } = "Operator";
    }
}
