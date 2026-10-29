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
    /// aplikasi — username dan kata sandi (pemasangan baru memakai kredensial
    /// bawaan admin/admin; akun lama Login.dat dimigrasikan ke tabel Pengguna,
    /// lihat docs/policy-peran-pengguna.md).
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
    /// Pengguna. Kata sandinya memakai bawaan <c>adminpusat11</c> (boleh
    /// ditimpa provisioning mesin) dan tidak pernah ditulis ke Login.dat.
    /// Hanya sesi dari akun inilah menu Pusat Administrasi ditampilkan.
    ///
    /// Login Google sudah DIBONGKAR dari layar login: koneksi akun Google
    /// hanya melayani pencadangan Drive dan disambungkan SETELAH masuk —
    /// ia bukan jalan masuk, sehingga sesi selalu melewati peran dan kunci
    /// akun aplikasi.
    /// </summary>
    public class LoginViewModel : ObservableObject
    {
        private readonly string _loginFilePath;
        private readonly IPenggunaService? _penggunaService;
        private readonly ILogger<LoginViewModel>? _logger;
        /// <summary>Nama akun administrator bawaan.</summary>
        public const string UsernameAdminBawaan = "admin";

        /// <summary>
        /// Kata sandi akun administrator bawaan. TETAP (bukan dibuat acak per
        /// instalasi) dan tidak pernah dirotasi otomatis: operator harus selalu
        /// bisa masuk dengan kredensial yang terdokumentasi. Penggantian sandi
        /// hanya terjadi bila pengguna sendiri melakukannya di Kelola Pengguna.
        /// </summary>
        public const string SandiAdminBawaan = "admin";

        private readonly PembatasPercobaanLogin _pembatas;
        private readonly PembatasPercobaanLogin _pembatasPusat;

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
            ILogger<LoginViewModel>? logger = null,
            Func<DateTime>? waktu = null)
        {
            _loginFilePath = BerkasLogin.JalurBawaan;
            _penggunaService = penggunaService;
            _logger = logger;
            _pembatas = new PembatasPercobaanLogin(waktu);
            _pembatasPusat = new PembatasPercobaanLogin(waktu, new KunciPercobaanPusat());
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
        /// memakai kredensial tetap <c>admin/admin</c> — tidak dibuat acak dan
        /// tidak diganti otomatis. Akun pusat TIDAK ditulis ke berkas ini;
        /// sandinya dibaca dari <see cref="AkunPusat"/>.
        /// </summary>
        private void SiapkanBerkasAkun()
        {
            try
            {
                if (!File.Exists(_loginFilePath))
                {
                    BerkasLogin.Tulis(_loginFilePath, new[]
                    {
                        BuatAkunBawaan(UsernameAdminBawaan, SandiAdminBawaan)
                    });

                    _logger?.LogInformation("Berkas akun lokal dibuat dengan akun administrator bawaan.");
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
            string username = Username.ToLower().Trim();
            // Sandi tidak diturunkan ke huruf kecil: hash dibuat dari sandi apa
            // adanya, jadi normalisasi di sini membuat sandi berhuruf besar gagal.
            string password = Password.Trim();

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
                    // Akun pusat tidak punya baris database, jadi pemeriksaan
                    // layanan tidak akan menahannya. Periksa batas SEBELUM memanggil
                    // layanan agar percobaan yang ditahan tidak menyentuh database.
                    bool akunPusat = AkunPusat.Namanya(username);
                    if (akunPusat && !_pembatasPusat.BolehMencoba)
                    {
                        ErrorMessage = PesanTunggu(_pembatasPusat);
                        return;
                    }

                    var hasil = await _penggunaService.MasukAsync(username, password);
                    if (hasil.Berhasil && hasil.Pengguna != null)
                    {
                        var akun = hasil.Pengguna;
                        CurrentUserName = akun.Username;
                        SessionContext.SetIdentitas(
                            akun.Username, "manual", akun.NamaTampilan, akun.Peran);
                        SessionContext.TandaiSesiPusat(false);

                        // Kredensial bawaan TIDAK dirotasi: admin/admin tetap
                        // berlaku sampai pengguna sendiri menggantinya.
                        CatatLoginBerhasil();
                        LoginSucceeded?.Invoke();
                        return;
                    }

                    // Akun pusat penyedia: masuk memakai sandi bawaan/provisioning,
                    // tanpa baris di tabel Pengguna (lihat AkunPusat). Hanya jalur
                    // ini yang memakai penghitung _pembatasPusat; kegagalan akun
                    // database sudah dihitung PenggunaService dan tidak boleh ikut
                    // mengunci akun pusat.
                    if (!hasil.Terkunci && akunPusat)
                    {
                        if (CobaMasukAkunPusat(username, password))
                        {
                            return; // LoginSucceeded sudah dipanggil di dalamnya
                        }

                        _pembatasPusat.CatatGagal();
                    }

                    // Bila PenggunaService tidak menemukan akun, coba migrasi dari
                    // Login.dat lama — mungkin instalasi belum sempat dimigrasi.
                    if (!hasil.Terkunci && await CobaMigrasiLoginDatAsync(username, password))
                    {
                        return; // LoginSucceeded sudah dipanggil di dalam metode migrasi
                    }

                    ErrorMessage = hasil.Pesan;
                    return;
                }

                // Fallback bila IPenggunaService tidak tersedia (tidak seharusnya
                // terjadi lewat DI, tetapi menjaga kompatibilitas mundur).
                // Akun pusat memakai pembatas menetap; akun Login.dat lain memakai
                // pembatas memori karena tidak ada layanan yang menghitungnya.
                bool akunPusatFallback = AkunPusat.Namanya(username);
                var pembatas = akunPusatFallback ? _pembatasPusat : _pembatas;
                if (!pembatas.BolehMencoba)
                {
                    ErrorMessage = PesanTunggu(pembatas);
                    return;
                }

                // Akun pusat dicek lebih dulu: ia tidak punya baris di Login.dat.
                if (CobaMasukAkunPusat(username, password))
                    return;
                if (LoginViaLoginDatAsync(username, password))
                    return;

                pembatas.CatatGagal();
                ErrorMessage = "Username atau password salah.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal login: {ex.Message}";
            }
        }

        /// <summary>
        /// Sisipkan akun administrator bawaan ke tabel Pengguna saat database
        /// masih kosong — yaitu pemasangan baru — dengan kredensial tetap
        /// admin/admin. Akun ini langsung muncul di halaman Kelola Pengguna dan
        /// bisa diganti kata sandinya oleh pengguna.
        ///
        /// Bila database SUDAH berisi akun, satu pemutakhiran sekali-jalan
        /// dikerjakan untuk instalasi lama yang akun admin-nya terlanjur dibuat
        /// dengan sandi acak (lihat <see cref="PastikanKredensialBawaanSekaliAsync"/>).
        /// </summary>
        private async Task PastikanAkunAdminAsync()
        {
            if (_penggunaService == null) return;

            try
            {
                if (await _penggunaService.AdaPenggunaAsync())
                {
                    await PastikanKredensialBawaanSekaliAsync();
                    return;
                }

                await _penggunaService.TambahAsync(
                    UsernameAdminBawaan, "Administrator",
                    PeranPengguna.Administrator, SandiAdminBawaan,
                    oleh: "sistem", dariMigrasi: true);

                SamakanSandiAdminLoginDat(SandiAdminBawaan);
                AppPreferenceStore.SetKredensialBawaanDisiapkan(true);
                _logger?.LogInformation("Akun administrator bawaan disisipkan.");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan startup bila penyisipan gagal — Login.dat
                // masih bisa dipakai sebagai cadangan.
                _logger?.LogWarning(ex, "Gagal menyisipkan akun administrator bawaan; berlanjut dengan Login.dat.");
            }
        }

        /// <summary>
        /// Pemutakhiran sekali-jalan untuk instalasi yang lebih dulu memakai
        /// sandi administrator acak: akun <c>admin</c> dikembalikan ke kredensial
        /// bawaan (admin/admin).
        ///
        /// Hanya berjalan SEKALI, ditandai preferensi. Tanpa penanda itu setiap
        /// pembukaan aplikasi akan menimpa sandi yang sudah diganti pengguna —
        /// justru perilaku yang ingin dihindari. Bila tanda sudah ada, fungsi
        /// ini tidak menyentuh apa pun.
        /// </summary>
        private async Task PastikanKredensialBawaanSekaliAsync()
        {
            if (_penggunaService == null || AppPreferenceStore.SudahDisiapkanKredensialBawaan())
            {
                return;
            }

            try
            {
                var admin = await _penggunaService.AmbilAsync(UsernameAdminBawaan);
                if (admin is not null)
                {
                    // Bila penyimpanan gagal, penanda SENGAJA belum dipasang:
                    // pemulihan dicoba lagi pada pembukaan berikutnya, bukan
                    // membiarkan pengguna terkunci dengan sandi acak lama.
                    if (!await _penggunaService.PasangKataSandiAwalAsync(admin.ID, SandiAdminBawaan))
                    {
                        _logger?.LogWarning("Kata sandi administrator bawaan belum tersimpan; akan dicoba lagi.");
                        return;
                    }

                    SamakanSandiAdminLoginDat(SandiAdminBawaan);
                    _logger?.LogInformation(
                        "Kredensial administrator dipulihkan ke bawaan (admin/admin) satu kali.");
                }

                AppPreferenceStore.SetKredensialBawaanDisiapkan(true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Gagal memulihkan kredensial administrator bawaan.");
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
        /// <summary>
        /// Masuk sebagai akun pusat penyedia — TANPA membuat baris di tabel
        /// Pengguna, sehingga akun ini tidak pernah muncul di halaman Kelola
        /// Pengguna dan tidak ikut tercadang bersama data desa.
        ///
        /// Kata sandi bawaannya <c>adminpusat11</c> (lihat
        /// <see cref="AkunPusat.SandiBawaan"/>); provisioning mesin
        /// (<see cref="AkunPusat.VariabelSandi"/> atau
        /// <see cref="AkunPusat.NamaBerkasSandi"/>) boleh menimpanya bila mesin
        /// itu memang memakai sandi lain. Sandi tidak pernah ditulis ke
        /// Login.dat.
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
                "Akun pusat masuk tanpa baris di tabel Pengguna.");
            CatatLoginBerhasil();
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
                CatatLoginBerhasil();
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
            CatatLoginBerhasil();
            LoginSucceeded?.Invoke();
            return true;
        }

        /// <summary>Login berhasil mengakhiri masa tunggu; tanpa ini satu salah ketik sesudahnya langsung mengunci lagi.</summary>
        private void CatatLoginBerhasil()
        {
            _pembatas.CatatBerhasil();
            // Keadaan pusat juga dibersihkan oleh login yang berhasil, sama
            // seperti limiter bersama sebelum dipisah: satu login sah tidak
            // boleh meninggalkan masa tunggu lama di belakangnya.
            _pembatasPusat.CatatBerhasil();
        }

        private static string PesanTunggu(PembatasPercobaanLogin pembatas) =>
            $"Terlalu banyak percobaan gagal. Coba lagi dalam " +
            $"{(int)Math.Ceiling(pembatas.SisaTunggu.TotalMinutes)} menit.";

        public static string CurrentUserName { get; private set; } = "Operator";
    }
}
