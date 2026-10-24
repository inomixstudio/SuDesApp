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
    /// aplikasi — username dan kata sandi (pemasangan baru mendapat kata sandi
    /// acak yang ditampilkan sekali; akun lama Login.dat dimigrasikan ke tabel
    /// Pengguna, lihat docs/policy-peran-pengguna.md).
    ///
    /// Sejak versi 2.6.0, login utama memakai <see cref="IPenggunaService.MasukAsync"/>
    /// (PBKDF2, kunci 3× gagal, peran dari database). Akun administrator awal
    /// disisipkan ke tabel Pengguna saat pemasangan baru sehingga langsung muncul
    /// di halaman Kelola Pengguna. Login.dat masih dibaca sebagai cadangan untuk
    /// instalasi lama yang belum dimigrasi — bila cocok, akunnya dipindahkan ke
    /// tabel Pengguna secara otomatis.
    ///
    /// Satu pengecualian pada aturan migrasi itu: akun pusat penyedia
    /// (<see cref="AkunPusat"/>). Akun itu sengaja TIDAK pernah dibuatkan baris
    /// di tabel Pengguna, supaya namanya tidak muncul di halaman Kelola
    /// Pengguna. Kata sandinya tidak lagi disimpan di kode maupun Login.dat —
    /// hanya dibaca dari provisioning mesin. Hanya sesi dari akun inilah menu
    /// Pusat Administrasi ditampilkan.
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
        // Sandi bawaan yang pernah terbit di repo publik. Konstanta ini TIDAK
        // dipakai untuk masuk — hanya untuk mengenali akun lama yang belum
        // mengganti sandinya agar bisa dirotasi otomatis.
        private const string SandiBawaanLama = "admin";

        private int _failedAttempts;
        private DateTime _lastFailedAttempt = DateTime.MinValue;

        // Kata sandi administrator awal dibuat acak per instalasi (bukan
        // admin/admin) dan hanya ditampilkan sekali di layar login.
        private readonly string _sandiAdminAwal = SandiAcak.Buat();
        private string _infoKredensialAwal = string.Empty;

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

        /// <summary>True bila ada kredensial awal yang harus ditampilkan di layar login.</summary>
        public bool HasInfoKredensial => InfoKredensialAwal.Length > 0;

        /// <summary>
        /// Keterangan sekali-tampil berisi kata sandi akun administrator yang
        /// baru dibuat/dirotasi. Tidak pernah disimpan ke berkas atau log.
        /// </summary>
        public string InfoKredensialAwal
        {
            get => _infoKredensialAwal;
            private set
            {
                SetProperty(ref _infoKredensialAwal, value);
                OnPropertyChanged(nameof(HasInfoKredensial));
            }
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
        /// Siapkan berkas akun lokal. Pada pemasangan baru, akun desa bawaan
        /// memakai kata sandi acak per instalasi (bukan admin/admin) yang hanya
        /// ditampilkan sekali di layar login. Akun pusat TIDAK lagi ditulis ke
        /// berkas ini — sandinya dibaca dari provisioning mesin (lihat
        /// <see cref="AkunPusat"/>).
        /// </summary>
        private void SiapkanBerkasAkun()
        {
            try
            {
                if (!File.Exists(_loginFilePath))
                {
                    BerkasLogin.Tulis(_loginFilePath, new[]
                    {
                        BuatAkunBawaan("admin", _sandiAdminAwal)
                    });

                    _logger?.LogInformation("Berkas akun lokal dibuat dengan kata sandi administrator acak.");
                    return;
                }

                HapusAkunPusatLama();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal inisialisasi sistem login: {ex.Message}";
            }
        }

        /// <summary>
        /// Hapus akun pusat model lama dari Login.dat. Sebelumnya akun itu
        /// disemai dengan kata sandi bawaan yang sudah terbit di repo publik;
        /// menyisakannya berarti sandi itu tetap berlaku. Sandi pusat sekarang
        /// hanya datang dari provisioning mesin.
        /// </summary>
        private void HapusAkunPusatLama()
        {
            var akun = BerkasLogin.Baca(_loginFilePath);
            int dihapus = akun.RemoveAll(a => a.Pusat);
            if (dihapus == 0)
            {
                return;
            }

            BerkasLogin.Tulis(_loginFilePath, akun);
            _logger?.LogInformation(
                "{Jumlah} akun pusat model lama dihapus dari berkas akun lokal.", dihapus);
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

                        // Sandi bawaan lama yang sudah terbit di repo publik
                        // dirotasi saat dipakai pertama kali — pengguna langsung
                        // menerima sandi baru yang hanya tampil sekali.
                        await RotasiSandiBawaanLamaAsync(akun, password);
                        LoginSucceeded?.Invoke();
                        return;
                    }

                    // Akun pusat penyedia: masuk memakai sandi provisioning
                    // mesin, tanpa baris di tabel Pengguna (lihat AkunPusat).
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
        /// Sisipkan akun administrator awal ke tabel Pengguna saat database
        /// masih kosong — yaitu pemasangan baru. Kata sandinya dibuat acak per
        /// instalasi (bukan admin/admin) dan hanya ditampilkan sekali di layar
        /// login. Akun ini langsung muncul di halaman Kelola Pengguna dan bisa
        /// diganti kata sandinya.
        /// </summary>
        private async Task PastikanAkunAdminAsync()
        {
            if (_penggunaService == null) return;

            try
            {
                if (await _penggunaService.AdaPenggunaAsync()) return;

                await _penggunaService.TambahAsync(
                    "admin", "Administrator",
                    PeranPengguna.Administrator, _sandiAdminAwal,
                    oleh: "sistem", dariMigrasi: true);

                SamakanSandiAdminLoginDat(_sandiAdminAwal);
                TampilkanKredensialAwal(
                    "Akun administrator dibuat untuk instalasi ini dengan kata sandi acak.",
                    _sandiAdminAwal);
                _logger?.LogInformation("Akun administrator awal disisipkan dengan kata sandi acak.");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan startup bila penyisipan gagal — Login.dat
                // masih bisa dipakai sebagai cadangan.
                _logger?.LogWarning(ex, "Gagal menyisipkan akun administrator awal; berlanjut dengan Login.dat.");
            }
        }

        /// <summary>
        /// Samakan kata sandi akun <c>admin</c> di Login.dat dengan sandi akun
        /// database. Diperlukan karena Login.dat masih dipakai sebagai jalur
        /// cadangan/migrasi: sandi berbeda di kedua tempat membuat akun yang
        /// sama berperilaku berbeda tergantung jalur login.
        /// </summary>
        private void SamakanSandiAdminLoginDat(string kataSandi)
        {
            try
            {
                var akun = BerkasLogin.Baca(_loginFilePath);
                int indeks = akun.FindIndex(a =>
                    !a.Pusat && a.Username.Equals("admin", StringComparison.OrdinalIgnoreCase));

                var baru = BuatAkunBawaan("admin", kataSandi);
                if (indeks >= 0)
                {
                    akun[indeks] = baru;
                }
                else
                {
                    akun.Add(baru);
                }

                BerkasLogin.Tulis(_loginFilePath, akun);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Gagal menyamakan kata sandi admin di berkas akun lokal.");
            }
        }

        /// <summary>
        /// Rotasi kata sandi bawaan lama (<c>admin</c>) yang sudah pernah terbit
        /// di repo publik. Hanya berlaku bila akun benar-benar masih memakai
        /// sandi itu; sandi yang sudah diganti pengguna tidak disentuh.
        /// </summary>
        private async Task RotasiSandiBawaanLamaAsync(Pengguna akun, string kataSandiDipakai)
        {
            if (_penggunaService == null) return;
            if (!akun.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)) return;
            if (!kataSandiDipakai.Equals(SandiBawaanLama, StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                string baru = SandiAcak.Buat();
                if (await _penggunaService.UbahKataSandiAsync(akun.ID, kataSandiDipakai, baru))
                {
                    SamakanSandiAdminLoginDat(baru);
                    TampilkanKredensialAwal(
                        "Kata sandi bawaan administrator sudah tidak berlaku dan telah diganti.",
                        baru);
                    _logger?.LogInformation("Kata sandi administrator bawaan dirotasi otomatis.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Gagal merotasi kata sandi administrator bawaan.");
            }
        }

        /// <summary>
        /// Siapkan keterangan sekali-tampil berisi kredensial administrator yang
        /// baru dibuat atau dirotasi. Teks ini hanya hidup di memori.
        /// </summary>
        private void TampilkanKredensialAwal(string keterangan, string kataSandi)
        {
            InfoKredensialAwal =
                keterangan + "\n\n" +
                "Pengguna: admin\n" +
                "Kata sandi: " + kataSandi + "\n\n" +
                "Catat sekarang — sandi ini tidak ditampilkan lagi. " +
                "Ganti lewat halaman Kelola Pengguna setelah masuk.";
            Username = "admin";
        }

        /// <summary>
        /// Masuk sebagai akun pusat penyedia — TANPA membuat baris di tabel
        /// Pengguna, sehingga akun ini tidak pernah muncul di halaman Kelola
        /// Pengguna dan tidak ikut tercadang bersama data desa.
        ///
        /// Kata sandinya TIDAK disimpan di kode maupun Login.dat: dibaca dari
        /// provisioning mesin (<see cref="AkunPusat.VariabelSandi"/> atau
        /// <see cref="AkunPusat.NamaBerkasSandi"/>). Tanpa provisioning, akun
        /// pusat tidak bisa dipakai sama sekali.
        ///
        /// Sesi ditandai lewat <see cref="SessionContext.TandaiSesiPusat"/>:
        /// menu Pusat Administrasi bergantung pada penanda itu, bukan sekadar
        /// nama pengguna — akun database bernama sama tetap tidak membukanya.
        /// </summary>
        private bool CobaMasukAkunPusat(string username, string password)
        {
            if (!AkunPusat.Namanya(username) || !AkunPusat.CocokSandi(password))
            {
                return false;
            }

            CurrentUserName = AkunPusat.Username;
            SessionContext.SetIdentitas(
                AkunPusat.Username, "manual", AkunPusat.NamaTampilan, PeranPengguna.Administrator);
            SessionContext.TandaiSesiPusat(true);

            _logger?.LogInformation(
                "Akun pusat masuk memakai sandi provisioning mesin (tanpa baris di tabel Pengguna).");
            LoginSucceeded?.Invoke();
            return true;
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
