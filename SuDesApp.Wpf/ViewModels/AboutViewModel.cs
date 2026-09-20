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
    /// </summary>
    public class AboutViewModel : ObservableObject
    {
        private readonly ILogger<AboutViewModel> _logger;

        public string AssemblyTitle { get; }
        public string AssemblyVersion { get; }
        public string AssemblyProduct { get; }
        public string AssemblyCopyright { get; }
        public string AssemblyCompany { get; }
        public string AssemblyDescription { get; }
        public IReadOnlyList<string> Features { get; }

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
            AssemblyDescription =
                "SuDesApp adalah aplikasi administrasi dan surat-menyurat desa yang membantu " +
                "pemerintah desa menyusun surat resmi secara cepat, akurat, dan konsisten. " +
                "Dilengkapi pembuatan surat otomatis berformat PDF — termasuk blanko persyaratan " +
                "pernikahan (NTCR N1–N6) yang dapat dibuat sekali isi lalu dicetak sebagai satu " +
                "berkas gabungan siap cetak — surat keterangan numpang nikah, dan Template Surat " +
                "untuk menyusun sendiri jenis surat yang belum tersedia, lengkap dengan contoh " +
                "siap pakai yang tinggal dipasang — layanan " +
                "permintaan surat daring via WhatsApp (WhatsApp Cloud API + Google Form/Sheet), " +
                "manajemen formulir administrasi kependudukan, register surat desa dan register " +
                "NTCR yang terpisah, arsip dan agenda digital (buku agenda surat masuk/keluar " +
                "dengan lampiran PDF/gambar), buku SK/Perdes/Perkades dengan " +
                "lampiran, pencadangan data otomatis ke Google Drive, serta ekspor ke Excel, CSV, dan JSON. " +
                "Kredensial Google disimpan terenkripsi di komputer. Pembaruan aplikasi diunduh dari " +
                "GitHub Releases dengan verifikasi SHA-256: perbaikan kecil datang sebagai pembaruan " +
                "tambalan yang hanya mengganti berkas yang berubah (tanpa installer, data dan pengaturan " +
                "pengguna tidak tersentuh), sedangkan perubahan besar memakai installer penuh.";

            Features = new List<string>
            {
                "Pembuatan surat resmi desa dengan PDF otomatis dari template terstandar",
                "Blanko persyaratan pernikahan NTCR (N1–N6): sekali isi data satu pasangan, seluruh blanko tersimpan sekaligus dan tercetak dalam satu PDF gabungan siap cetak",
                "Surat keterangan numpang nikah (N8) beserta register NTCR yang terpisah dari register surat desa umum",
                "Template Surat: menyusun sendiri jenis surat baru lewat wizard (kop, judul, nomor, teks bebas, kolom isian dengan grid, tanda tangan, dan teks kaki), lengkap dengan pratinjau dan penomoran otomatis per template",
                "Surat dari Template Surat tercatat otomatis di Register Surat — dapat dicari (nomor, nama penerima, NIK, atau isi surat), dibuka lagi untuk diperbaiki, dan dicetak ulang kapan saja",
                "Contoh Template Surat siap pakai (pengantar RT/RW, izin keramaian, keterangan penghasilan, belum menikah, undangan rapat, dan pengumuman warga) yang dapat dipasang sekali klik, dijadikan dasar template baru, atau dicetak sebagai contoh",
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
}