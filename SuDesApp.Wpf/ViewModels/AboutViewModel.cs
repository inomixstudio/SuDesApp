using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel untuk halaman "Tentang Aplikasi" — padanan AboutBox1 (WinForms).
    /// Halaman dibuat ringkas: hanya ringkasan yang dirender saat dibuka;
    /// detail lengkap, fitur, dan teknologi dibuka per bagian (lazy) sehingga
    /// membuka halaman tetap terasa ringan.
    /// </summary>
    public class AboutViewModel : ObservableObject
    {
        private readonly ILogger<AboutViewModel> _logger;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();

        public string AssemblyTitle { get; }
        public string AssemblyVersion { get; }
        public string AssemblyProduct { get; }
        public string AssemblyCopyright { get; }
        public string AssemblyCompany { get; }

        /// <summary>Deskripsi lengkap aplikasi — tampil di bagian "Detail Lengkap".</summary>
        public string AssemblyDescription { get; }

        /// <summary>Deskripsi satu kalimat untuk halaman utama.</summary>
        public string DeskripsiSingkat { get; } =
            "Aplikasi administrasi dan surat-menyurat desa: pembuatan surat resmi ber-PDF, " +
            "register & agenda digital, layanan permintaan surat via WhatsApp, pencadangan " +
            "otomatis ke Google Drive, hingga ekspor Excel/CSV/JSON — semuanya resmi dan gratis.";

        public IReadOnlyList<string> Features { get; }

        /// <summary>Jumlah fitur utama — untuk ringkasan di halaman utama.</summary>
        public int JumlahFitur => Features.Count;

        /// <summary>Jumlah teknologi — untuk ringkasan di halaman utama.</summary>
        public int JumlahTeknologi => Teknologi.Count;

        private AboutBagianViewModel _bagianAktif = null!;

        /// <summary>Daftar bagian yang bisa dibuka lewat chip navigasi.</summary>
        public IReadOnlyList<AboutBagianViewModel> Bagian { get; private set; } = null!;

        /// <summary>Bagian yang sedang ditampilkan — menentukan template konten.</summary>
        public AboutBagianViewModel BagianAktif
        {
            get => _bagianAktif;
            private set => SetProperty(ref _bagianAktif, value);
        }

        /// <summary>Dipanggil chip navigasi (code-behind) saat bagian dipilih.</summary>
        public void PilihBagian(AboutBagianViewModel bagian)
        {
            if (ReferenceEquals(bagian, BagianAktif))
            {
                return;
            }

            foreach (var item in Bagian)
            {
                item.IsTerpilih = ReferenceEquals(item, bagian);
            }

            BagianAktif = bagian;
        }

        /// <summary>Daftar teknologi untuk bagian "Teknologi" (ikon + nama + tautan).</summary>
        public IReadOnlyList<TeknologiItem> Teknologi { get; }

        /// <summary>Lokasi data aplikasi di komputer ini (dihitung sekali, murah).</summary>
        public string LokasiData { get; }

        /// <summary>Versi runtime .NET yang sedang dipakai.</summary>
        public string RuntimeVersi { get; } =
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        /// <summary>Durasi aplikasi berjalan (diperbarui oleh timer di code-behind).</summary>
        public string Uptime
        {
            get
            {
                var t = _uptime.Elapsed;
                return t.TotalHours >= 1
                    ? $"{(int)t.TotalHours} jam {t.Minutes} menit"
                    : t.TotalMinutes >= 1
                        ? $"{t.Minutes} menit {t.Seconds} detik"
                        : $"{t.Seconds} detik";
            }
        }

        /// <summary>Dipanggil code-behind untuk menyegarkan teks uptime.</summary>
        public void RefreshUptime() => OnPropertyChanged(nameof(Uptime));

        public AboutViewModel(ILogger<AboutViewModel> logger, GoogleDriveService? driveService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var assembly = Assembly.GetExecutingAssembly();
            AssemblyTitle = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                ?? System.IO.Path.GetFileNameWithoutExtension(assembly.Location);
            AssemblyVersion = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            AssemblyProduct = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "";
            AssemblyCopyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
            AssemblyCompany = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";
            // Paragraf dipisah "\n\n" — ditampilkan apa adanya oleh TextBlock di
            // BagianDetailLengkapView sehingga deskripsi terbaca per blok, bukan
            // satu dinding teks panjang.
            AssemblyDescription =
                "SuDesApp adalah aplikasi administrasi dan surat-menyurat desa yang membantu " +
                "pemerintah desa menyusun surat resmi secara cepat, akurat, dan konsisten. " +
                "Dilengkapi pembuatan surat otomatis berformat PDF — termasuk blanko persyaratan " +
                "pernikahan (NTCR N1–N6) yang dapat dibuat sekali isi lalu dicetak sebagai satu " +
                "berkas gabungan siap cetak — surat keterangan numpang nikah, dan Template Surat " +
                "untuk menyusun sendiri jenis surat yang belum tersedia, lengkap dengan contoh " +
                "siap pakai yang tinggal dipasang.\n\n" +
                "Layanan permintaan surat daring via WhatsApp (WhatsApp Cloud API + Google " +
                "Form/Sheet), manajemen formulir administrasi kependudukan, register surat desa " +
                "dan register NTCR yang terpisah, arsip dan agenda digital (buku agenda surat " +
                "masuk/keluar dengan lampiran PDF/gambar), serta buku SK/Perdes/Perkades dengan " +
                "lampiran.\n\n" +
                "Pencadangan data otomatis ke Google Drive serta ekspor ke Excel, CSV, dan JSON. " +
                "Kredensial Google disimpan terenkripsi di komputer.\n\n" +
                "Pembaruan aplikasi diunduh dari GitHub Releases dengan verifikasi SHA-256: " +
                "perbaikan kecil datang sebagai pembaruan tambalan yang hanya mengganti berkas " +
                "yang berubah (tanpa installer, data dan pengaturan pengguna tidak tersentuh), " +
                "sedangkan perubahan besar memakai installer penuh.";

            Features = new List<string>
            {
                "Pembuatan surat resmi desa dengan PDF otomatis dari template terstandar",
                "Blanko persyaratan pernikahan NTCR (N1–N6): sekali isi data satu pasangan, seluruh blanko tersimpan sekaligus dan tercetak dalam satu PDF gabungan siap cetak",
                "Surat keterangan numpang nikah (N8) beserta register NTCR yang terpisah dari register surat desa umum",
                "Template Surat: menyusun sendiri jenis surat baru lewat wizard (kop, judul, nomor, teks bebas, kolom isian dengan grid, tanda tangan, dan teks kaki), lengkap dengan pratinjau dan penomoran otomatis per template",
                "Surat dari Template Surat tercatat otomatis di Register Surat — dapat dicari (nomor, nama penerima, NIK, atau isi surat), dibuka lagi untuk diperbaiki, dan dicetak ulang kapan saja",
                "Contoh Template Surat siap pakai (pengantar RT/RW, izin keramaian, keterangan penghasilan, belum menikah, undangan rapat, surat kuasa, surat tugas, dan pengumuman warga) yang dapat dipasang sekali klik, dijadikan dasar template baru, atau dicetak sebagai contoh",
                "Manajemen formulir administrasi kependudukan (\u2265 20 formulir resmi desa)",
                "Manajemen data kependudukan dan arsip digital register surat",
                "Buku agenda surat masuk dan keluar dengan lampiran berkas PDF atau gambar pada tiap surat",
                "Buku SK, Perdes, dan Perkades beserta lampiran Word/PDF",
                "Layanan permintaan surat daring via WhatsApp yang diproses otomatis",
                "Pencadangan database dan formulir otomatis ke Google Drive",
                "Kredensial Google tersimpan terenkripsi di komputer tanpa berkas kunci pada instalasi",
                "Pembaruan aplikasi dari GitHub Releases: perbaikan kecil dipasang sebagai pembaruan tambalan (hanya berkas yang berubah, tanpa installer) dan diperiksa otomatis saat aplikasi dibuka, sedangkan perubahan besar memakai installer penuh — seluruhnya dengan verifikasi SHA-256",
                "Ekspor data ke Excel, CSV, dan JSON",
                "Pengaturan aplikasi bernavigasi antar bagian, termasuk pengaturan penomoran surat: awalan nomor tiap jenis surat dapat diganti sendiri (mis. SKD 470 → 471) dan langsung berlaku tanpa menutup aplikasi",
                "Multi-temakan modern (Light, Biru Office, Dark, Green, Pink, Slate)",
                "Manajemen status surat dan riwayat aktivitas terpusat"
            };

            Teknologi = new List<TeknologiItem>
            {
                new("\U0001F5A5", "WPF (.NET 8)", "Desktop Windows", "https://dotnet.microsoft.com/apps/wpf"),
                new("\u2699", "MVVM + Layanan", "DI & logging .NET", "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection"),
                new("\U0001F5C4", "SQLite", "Database lokal", "https://www.sqlite.org"),
                new("\u26A1", "Dapper", "Akses data", "https://github.com/DapperLib/Dapper"),
                new("\U0001F4C4", "QuestPDF", "Pembuatan PDF", "https://www.questpdf.com"),
                new("\U0001F4D1", "Pdfium", "Pratinjau & cetak PDF", "https://github.com/pvginkel/PdfiumViewer"),
                new("\U0001F4CA", "EPPlus", "Excel", "https://epplussoftware.com"),
                new("\U0001F4C1", "Google Drive API", "Cadangan & arsip", "https://developers.google.com/drive"),
                new("\U0001F4C8", "Google Sheets API", "Layanan online", "https://developers.google.com/sheets/api"),
                new("\U0001F4E6", "Newtonsoft.Json", "Serialisasi", "https://www.newtonsoft.com/json"),
                new("\U0001F510", "DPAPI", "Kredensial aman", "https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata"),
            };

            Bagian = new List<AboutBagianViewModel>
            {
                new("DetailAplikasi", "\u2139", "Detail aplikasi", "Identitas resmi aplikasi dan informasi instalasi pada komputer ini."),
                new("DetailLengkap", "\U0001F4C4", "Detail lengkap", "Deskripsi lengkap SuDesApp dari metadata rilis."),
                new("Fitur", "\u2B50", "Fitur utama", "Fitur utama yang tersedia di SuDesApp."),
                new("Teknologi", "\U0001F9E9", "Teknologi", "Teknologi di balik SuDesApp — klik chip untuk membuka dokumentasinya."),
                new("Dukungan", "\u2764\uFE0F", "Dukungan", "Dukungan pengembangan, kritik, dan saran."),
            };
            _bagianAktif = Bagian[0];
            Bagian[0].IsTerpilih = true;

            try
            {
                LokasiData = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                LokasiData = "-";
            }

            OpenUrlCommand = new RelayCommand<string>(OpenUrl);

            LoadAppLogo();
            if (driveService != null) _ = LoadGoogleAccountAsync(driveService);
        }

        /// <summary>Logo aplikasi (dari resource assembly) — tampil di header halaman.</summary>
        public BitmapSource? AppLogo { get; private set; }

        /// <summary>Foto profil akun Google (null bila belum login / tanpa foto).</summary>
        public BitmapSource? GooglePhoto { get; private set; }

        private string _googleName = string.Empty;

        /// <summary>Nama tampilan akun Google yang sedang terhubung.</summary>
        public string GoogleName
        {
            get => _googleName;
            private set
            {
                if (SetProperty(ref _googleName, value))
                {
                    OnPropertyChanged(nameof(GoogleInitial));
                }
            }
        }

        private string _googleEmail = string.Empty;

        /// <summary>Email akun Google yang sedang terhubung.</summary>
        public string GoogleEmail
        {
            get => _googleEmail;
            private set
            {
                if (SetProperty(ref _googleEmail, value))
                {
                    OnPropertyChanged(nameof(GoogleInitial));
                }
            }
        }

        /// <summary>Inisial untuk lingkaran cadangan bila foto profil tidak tersedia.</summary>
        public string GoogleInitial
        {
            get
            {
                var s = string.IsNullOrWhiteSpace(GoogleName) ? GoogleEmail : GoogleName;
                var first = s.Trim().Length > 0 ? s.Trim()[0] : '?';
                return char.ToUpperInvariant(first).ToString();
            }
        }

        private bool _hasGoogleAccount;

        /// <summary>Aktif bila ada akun Google terhubung — menampilkan kartu akun.</summary>
        public bool HasGoogleAccount
        {
            get => _hasGoogleAccount;
            private set => SetProperty(ref _hasGoogleAccount, value);
        }

        private bool _hasGooglePhoto;

        /// <summary>Aktif bila foto profil tersedia — menyembunyikan lingkaran inisial.</summary>
        public bool HasGooglePhoto
        {
            get => _hasGooglePhoto;
            private set => SetProperty(ref _hasGooglePhoto, value);
        }

        /// <summary>Muat logo aplikasi dari resource — gagal diabaikan (dekoratif).</summary>
        private void LoadAppLogo()
        {
            try
            {
                var logo = new BitmapImage();
                logo.BeginInit();
                logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.UriSource = new Uri("pack://application:,,,/Resources/AppIcon-256.png");
                logo.EndInit();
                logo.Freeze();
                AppLogo = logo;
                OnPropertyChanged(nameof(AppLogo));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat logo aplikasi untuk halaman Tentang");
            }
        }

        /// <summary>
        /// Ambil profil akun Google (nama, email, foto) di background — dekoratif,
        /// jadi gagal jaringan diabaikan senyap tanpa dialog. Foto di-freeze agar
        /// aman dipakai lintas thread.
        /// </summary>
        private async System.Threading.Tasks.Task LoadGoogleAccountAsync(GoogleDriveService driveService)
        {
            try
            {
                if (!driveService.IsOAuthEnabled || !driveService.HasStoredToken()) return;

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                var info = await driveService.GetAccountInfoAsync(cts.Token);
                if (info == null || string.IsNullOrWhiteSpace(info.Email)) return;

                BitmapSource? photo = null;
                if (info.PhotoBytes is { Length: > 0 })
                {
                    try
                    {
                        var img = new BitmapImage();
                        using var ms = new MemoryStream(info.PhotoBytes);
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                        img.Freeze();
                        photo = img;
                    }
                    catch (Exception exPhoto)
                    {
                        _logger.LogWarning(exPhoto, "Gagal membaca foto profil Google");
                    }
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    ApplyGoogleAccount(info.DisplayName, info.Email, photo);
                }
                else
                {
                    await dispatcher.BeginInvoke(new Action(() => ApplyGoogleAccount(info.DisplayName, info.Email, photo)));
                }
            }
            catch (Exception ex)
            {
                // Dekoratif: tidak mengganggu pengguna.
                _logger.LogInformation(ex, "Profil Google tidak termuat untuk halaman Tentang");
            }
        }

        private void ApplyGoogleAccount(string displayName, string email, BitmapSource? photo)
        {
            GoogleName = string.IsNullOrWhiteSpace(displayName) ? email : displayName;
            GoogleEmail = email;
            GooglePhoto = photo;
            HasGooglePhoto = photo != null;
            HasGoogleAccount = true;
            OnPropertyChanged(nameof(GooglePhoto));
        }

        public ICommand OpenUrlCommand { get; }

        private void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal buka URL: {Url}", url);
            }
        }
    }

    /// <summary>Satu item chip teknologi pada bagian "Teknologi".</summary>
    public class TeknologiItem
    {
        public TeknologiItem(string ikon, string nama, string keterangan, string url)
        {
            Ikon = ikon;
            Nama = nama;
            Keterangan = keterangan;
            Url = url;
        }

        public string Ikon { get; }
        public string Nama { get; }
        public string Keterangan { get; }
        public string Url { get; }
    }

    /// <summary>Satu entri navigasi bagian pada halaman Tentang Aplikasi.</summary>
    public class AboutBagianViewModel : ObservableObject
    {
        private bool _isTerpilih;

        public AboutBagianViewModel(string kunci, string ikon, string label, string deskripsiStatus)
        {
            Kunci = kunci;
            Ikon = ikon;
            Label = label;
            DeskripsiStatus = deskripsiStatus;
        }

        /// <summary>Kunci bagian — menentukan template konten mana yang dirender.</summary>
        public string Kunci { get; }

        public string Ikon { get; }

        public string Label { get; }

        /// <summary>Keterangan singkat bagian — tampil di statusbar halaman.</summary>
        public string DeskripsiStatus { get; }

        public bool IsTerpilih
        {
            get => _isTerpilih;
            set => SetProperty(ref _isTerpilih, value);
        }
    }
}
