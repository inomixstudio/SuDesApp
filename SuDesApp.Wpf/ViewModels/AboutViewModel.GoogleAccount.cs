using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class AboutViewModel : ObservableObject
    {
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
            AppLogo = MuatResource("AppIcon-256.png");
            OnPropertyChanged(nameof(AppLogo));
        }

        /// <summary>
        /// Logo Sumberjaya Dev dan PT Duta Publik — dipanggil sekali, saat bagian
        /// Perusahaan dibuka. Gagal memuat tidak menghentikan halaman: kartu
        /// perusahaan tetap tampil lengkap dengan teksnya.
        /// </summary>
        private void MuatLogoPerusahaan()
        {
            if (_logoPerusahaanDimuat)
            {
                return;
            }

            _logoPerusahaanDimuat = true;
            LogoSumberjayaDev = MuatResource("Logo-SumberjayaDev.png");
            IkonSumberjayaDev = MuatResource("Ikon-SumberjayaDev.png");
            LogoDutaPublik = MuatResource("Logo-DutaPublik.png");
            IkonDutaPublik = MuatResource("Ikon-DutaPublik.png");

            OnPropertyChanged(nameof(LogoSumberjayaDev));
            OnPropertyChanged(nameof(IkonSumberjayaDev));
            OnPropertyChanged(nameof(LogoDutaPublik));
            OnPropertyChanged(nameof(IkonDutaPublik));
        }

        /// <summary>
        /// Versi dokumen hukum yang disetujui pada instalasi ini. Versi terpasang
        /// ikut ditampilkan supaya pengguna bisa membandingkan: bila dokumennya
        /// sudah diperbarui, kartu ini menandainya sebelum dialog berikutnya muncul.
        /// </summary>
        private void MuatPersetujuan()
        {
            try
            {
                Persetujuan = PersetujuanDokumenService.MuatStatus()
                    .Select(status => new PersetujuanDokumenTampil(status))
                    .ToList();
            }
            catch (Exception ex)
            {
                // Bagian ini hanya keterangan; kegagalan membaca folder dokumen
                // tidak boleh membuat halaman Tentang gagal dibuka.
                _logger.LogWarning(ex, "Gagal memuat status persetujuan dokumen");
                Persetujuan = Array.Empty<PersetujuanDokumenTampil>();
            }
        }

        /// <summary>
        /// Gambar dari folder Resources rakitan aplikasi, atau null bila tidak ada.
        ///
        /// Alamat pertama memakai nama rakitan eksplisit (pola sama dengan AppStatusBar):
        /// alamat "pack://application:,,,/Resources/..." menunjuk rakitan yang sedang
        /// berjalan — pada test host itu rakitan uji, bukan SuDesApp — sehingga tanpa
        /// nama rakitan gambarnya gagal dimuat di luar aplikasi. Alamat kedua adalah
        /// cadangan gaya lama.
        /// </summary>
        private BitmapSource? MuatResource(string namaBerkas)
        {
            string[] alamat =
            {
                $"pack://application:,,,/SuDesApp;component/Resources/{namaBerkas}",
                $"pack://application:,,,/Resources/{namaBerkas}",
            };

            foreach (var satu in alamat)
            {
                try
                {
                    var gambar = new BitmapImage();
                    gambar.BeginInit();
                    gambar.CacheOption = BitmapCacheOption.OnLoad;
                    gambar.UriSource = new Uri(satu, UriKind.Absolute);
                    gambar.EndInit();
                    gambar.Freeze();
                    return gambar;
                }
                catch (Exception ex)
                {
                    // Coba alamat berikutnya; gambar hanya pelengkap halaman.
                    _logger.LogDebug(ex, "Gambar {Berkas} tidak termuat dari {Alamat}", namaBerkas, satu);
                }
            }

            _logger.LogWarning("Gambar {Berkas} tidak ditemukan di resource aplikasi", namaBerkas);
            return null;
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
                _logger.LogDebug(ex, "Profil Google tidak termuat untuk halaman Tentang");
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
