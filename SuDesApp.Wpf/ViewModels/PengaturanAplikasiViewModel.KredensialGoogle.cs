using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        // =======================================================================
        // Kredensial klien OAuth Google (Client ID & Client Secret).
        //
        // Paket rilis umumnya sudah memuat kredensial tertanam (dibangun dengan
        // berkas rahasia build di luar git). Isian ini tetap wajib ada: paket yang
        // dibangun tanpa berkas rahasia itu (mis. hasil CI) TIDAK memuat kredensial,
        // sehingga teknisi harus bisa menghidupkan login Google di komputer
        // pengguna tanpa membangun ulang installer.
        // =======================================================================
        private string _googleClientId = string.Empty;
        private string _googleClientSecret = string.Empty;
        private string _googleKredensialInfo = string.Empty;
        private int _googleKredensialTone;

        public string GoogleClientId
        {
            get => _googleClientId;
            set => SetProperty(ref _googleClientId, value ?? string.Empty);
        }

        public string GoogleClientSecret
        {
            get => _googleClientSecret;
            set => SetProperty(ref _googleClientSecret, value ?? string.Empty);
        }

        /// <summary>Pesan hasil simpan/uji kredensial di kartu Kredensial Google.</summary>
        public string GoogleKredensialInfo
        {
            get => _googleKredensialInfo;
            private set => SetProperty(ref _googleKredensialInfo, value);
        }

        /// <summary>Warna kotak pesan: 0 netral, 1 sukses, 2 peringatan, 3 galat.</summary>
        public int GoogleKredensialTone
        {
            get => _googleKredensialTone;
            private set => SetProperty(ref _googleKredensialTone, value);
        }

        /// <summary>Lokasi berkas kredensial terenkripsi DPAPI (untuk ditampilkan ke teknisi).</summary>
        public string GoogleKredensialLokasi => GoogleDriveService.CredentialsStorePath;

        /// <summary>Status apakah klien OAuth sudah aktif, sekaligus sumber kredensialnya.</summary>
        public string GoogleKredensialStatus => _googleDrive == null
            ? "Layanan Google tidak tersedia pada sesi ini."
            : _googleDrive.IsOAuthEnabled
                ? "Klien OAuth aktif — sumber kredensial: " + _googleDrive.CredentialSource + "."
                : "Klien OAuth belum aktif — isi Client ID dan Client Secret di bawah, lalu simpan.";

        /// <summary>
        /// Keterangan apakah paket aplikasi ini membawa kredensial OAuth tertanam.
        /// Penting untuk mendiagnosis "login Google gagal" pada instalasi hasil
        /// build CI yang dibangun tanpa berkas rahasia.
        /// </summary>
        /// <summary>Warna kotak keterangan paket: 1 bila kredensial tertanam, 2 bila tidak.</summary>
        public int GoogleKredensialPaketTone => GoogleDriveService.EmbeddedCredentialsAvailable ? 1 : 2;

        public string GoogleKredensialPaketInfo => GoogleDriveService.EmbeddedCredentialsAvailable
            ? "Paket aplikasi ini memuat kredensial OAuth tertanam — login Google siap dipakai di komputer mana pun."
            : "Paket aplikasi ini TIDAK memuat kredensial OAuth tertanam (dibangun tanpa berkas rahasia build). " +
              "Isi Client ID & Client Secret di sini, simpan, lalu uji koneksi pada setiap komputer yang memakainya.";

        public AsyncRelayCommand SimpanKredensialGoogleCommand { get; }

        public AsyncRelayCommand UjiKredensialGoogleCommand { get; }

        /// <summary>
        /// Simpan Client ID & Client Secret terenkripsi DPAPI per user Windows
        /// (bukan plaintext di appsettings.json yang ikut terdistribusi).
        /// </summary>
        private Task SimpanKredensialGoogleAsync()
        {
            var drive = _googleDrive;
            if (drive == null)
            {
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Layanan Google tidak tersedia pada sesi ini.";
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(GoogleClientId) || string.IsNullOrWhiteSpace(GoogleClientSecret))
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Client ID dan Client Secret wajib diisi keduanya.";
                return Task.CompletedTask;
            }

            try
            {
                drive.SaveClientCredentials(GoogleClientId, GoogleClientSecret);
                GoogleKredensialTone = 1;
                GoogleKredensialInfo = "Kredensial tersimpan terenkripsi pada:\n" + GoogleDriveService.CredentialsStorePath +
                    "\n\nLangkah berikutnya: klik \"Uji Koneksi\" untuk membuka login akun Google.";
                OnPropertyChanged(nameof(GoogleKredensialStatus));
                _logger.LogInformation("Kredensial klien OAuth Google disimpan dari halaman Pengaturan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan kredensial klien OAuth Google");
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Gagal menyimpan: " + ex.Message;
            }
            return Task.CompletedTask;
        }

        /// <summary>Uji kredensial dengan membuka alur login Google dan menampilkan email hasilnya.</summary>
        private async Task UjiKredensialGoogleAsync()
        {
            var drive = _googleDrive;
            if (drive == null || !drive.IsOAuthEnabled)
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Simpan Client ID dan Client Secret terlebih dahulu, lalu uji koneksi.";
                return;
            }

            try
            {
                GoogleKredensialTone = 0;
                GoogleKredensialInfo = "Membuka login Google — selesaikan prosesnya di peramban bila diminta.";
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var email = await Task.Run(() => drive.GetAccountEmailAsync(cts.Token), cts.Token);
                if (string.IsNullOrWhiteSpace(email))
                {
                    GoogleKredensialTone = 2;
                    GoogleKredensialInfo = "Login selesai, tetapi email tidak diperoleh. Periksa Google Drive API dan OAuth consent screen.";
                }
                else
                {
                    GoogleKredensialTone = 1;
                    GoogleKredensialInfo = "Terhubung — " + email;
                }
            }
            catch (OperationCanceledException)
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Waktu login habis atau dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uji koneksi kredensial Google gagal");
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Gagal: " + ex.Message;
            }
        }

        public RelayCommand BatalCommand { get; }


    }
}
