using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class FormulirViewModel : ObservableObject, IDisposable
    {
        // =====================================================================
        // Sumber formulir mengikuti kabupaten pada Pengaturan Surat/Desa
        // =====================================================================

        private SumberFormulirAktif _sumberAktif = SumberFormulirAktif.Memuat;

        /// <summary>Label desa pada kartu sumber; jujur menyebut bila belum diisi.</summary>
        public string NamaDesaAktif => _sumberAktif.NamaDesa;

        /// <summary>Identitas ringkas desa + kabupaten untuk chip kepala halaman.</summary>
        public string IdentitasDesaAktif => _sumberAktif.IdentitasDesa;

        /// <summary>
        /// Kabupaten/kota yang dipilih pada Pengaturan Surat → Data Desa. Bila
        /// sumber resminya dikenal, nama dari katalog dipakai agar konsisten
        /// (mis. “Kota Sukabumi”, bukan sekadar “Sukabumi”).
        /// </summary>
        public string KabupatenAktif => _sumberAktif.Kabupaten;

        /// <summary>Kabupaten ini bisa diunduh otomatis oleh aplikasi (INSAN Karawang).</summary>
        public bool UnduhanOtomatis => _sumberAktif.UnduhanOtomatis;

        /// <summary>Kabupaten dikenali, tetapi formulirnya masih diunduh manual dari situs resmi.</summary>
        public bool UnduhManual => _sumberAktif.UnduhManual;

        /// <summary>Kabupaten belum dipilih pada Pengaturan Surat/Desa.</summary>
        public bool KabupatenBelumDiatur => _sumberAktif.BelumDiatur;

        /// <summary>Kabupaten sudah dipilih, tetapi belum ada tautan resmi yang dikenal.</summary>
        public bool SumberTidakTersedia => _sumberAktif.TidakTersedia;

        /// <summary>Ada halaman resmi yang bisa dibuka/diunduh untuk kabupaten ini.</summary>
        public bool AdaSumberDaerah => _sumberAktif.AdaSumber;

        /// <summary>Label singkat keadaan sumber pada chip kartu.</summary>
        public string StatusSumberTeks => _sumberAktif.StatusTeks;

        /// <summary>Ikon kecil pada chip keadaan sumber.</summary>
        public string IkonStatusSumber => _sumberAktif.IkonStatus;

        /// <summary>Alamat sumber yang sedang berlaku (kosong bila tidak ada).</summary>
        public string SumberAktifTeks => _sumberAktif.SumberTeks;

        /// <summary>
        /// Lokasi berkas konfigurasi sumber formulir — ditampilkan agar operator
        /// tahu berkas mana yang harus disunting untuk menambah daerah baru.
        /// </summary>
        public string KonfigurasiSumberTeks =>
            $"Daftar sumber dapat diperbarui tanpa rilis di {KatalogFormulirDaerah.Path}. " +
            "Setelah menyunting, tekan Segarkan.";

        /// <summary>
        /// Penjelasan keadaan sumber — selalu menyebut apa yang bisa dilakukan
        /// sekarang, termasuk ajakan menambah formulir manual bila tautan resmi
        /// untuk kabupaten terpilih belum tersedia.
        /// </summary>
        public string KeteranganSumber => _sumberAktif.Keterangan;

        /// <summary>Langkah menambah formulir manual saat kabupaten belum punya tautan resmi.</summary>
        public string PetunjukManual => _sumberAktif.PetunjukManual;

        public ICommand BukaSitusCommand { get; }
        public ICommand BukaPengaturanDesaCommand { get; }

        /// <summary>Muat ulang data desa dan tentukan sumber formulirnya.</summary>
        private async Task MuatSumberFormulirAsync()
        {
            // Baca ulang berkas konfigurasi lebih dulu: suntingan operator (termasuk
            // daerah baru) langsung terpakai saat halaman disegarkan.
            KatalogFormulirDaerah.Muat(_logger);

            DesaData? desa = null;

            if (_pengaturan != null)
            {
                try
                {
                    desa = await _pengaturan.GetSettingsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal membaca pengaturan desa untuk sumber formulir");
                }
            }

            _sumberAktif = SumberFormulirAktif.Dari(desa);
            BeritahuSumber();
        }

        /// <summary>Beri tahu tampilan bahwa keadaan sumber berubah.</summary>
        private void BeritahuSumber()
        {
            foreach (string nama in new[]
                     {
                         nameof(NamaDesaAktif), nameof(KabupatenAktif), nameof(UnduhanOtomatis),
                         nameof(IdentitasDesaAktif),
                         nameof(UnduhManual), nameof(KabupatenBelumDiatur), nameof(SumberTidakTersedia),
                         nameof(AdaSumberDaerah), nameof(StatusSumberTeks), nameof(IkonStatusSumber),
                         nameof(SumberAktifTeks), nameof(KonfigurasiSumberTeks), nameof(KeteranganSumber),
                         nameof(PetunjukManual), nameof(PesanKosong), nameof(JudulKosong),
                     })
            {
                OnPropertyChanged(nama);
            }

            (DownloadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Data desa disimpan di halaman lain → sesuaikan sumber tanpa membuka ulang halaman.</summary>
        private void OnPengaturanDesaTersimpan(object? sender, DesaData desa)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => TerapkanPengaturanDesa(desa));
                return;
            }

            TerapkanPengaturanDesa(desa);
        }

        private void TerapkanPengaturanDesa(DesaData? desa)
        {
            _sumberAktif = SumberFormulirAktif.Dari(desa);
            BeritahuSumber();

            // Tawaran unduh lama bisa jadi tidak relevan lagi setelah kabupaten
            // berganti (mis. dari Karawang ke daerah tanpa unduhan otomatis).
            if (!UnduhanOtomatis)
            {
                AdaTawaranUnduh = false;
            }
        }

        /// <summary>Buka halaman sumber formulir di browser bawaan.</summary>
        private void BukaSitusSumber()
        {
            var sumber = _sumberAktif.Sumber;
            if (sumber is null)
            {
                StatusText = "Belum ada tautan sumber formulir untuk kabupaten ini.";
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sumber.Url,
                    UseShellExecute = true
                });
                StatusText = $"Membuka {sumber.TautanTampil} di browser...";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka sumber formulir {Url}", sumber.Url);
                StatusText = $"Situs {sumber.TautanTampil} tidak dapat dibuka dari sini.";
            }
        }

        /// <summary>Buka Pengaturan Surat tepat pada bagian Data Desa.</summary>
        private void BukaPengaturanDesa()
        {
            if (_navigation is null)
            {
                StatusText = "Buka Pengaturan Surat → Data Desa untuk memilih kabupaten.";
                return;
            }

            _navigation.ShowSetelanBagianSurat(0);
            StatusText = "Membuka Pengaturan Surat — bagian Data Desa.";
        }

        /// <summary>Benar bila cadangan otomatis ke Google Drive ikut aktif (perlu login Drive).</summary>
        public bool CadanganDriveAktif => IsDriveReady && AppPreferenceStore.IsAutoBackupFormulirDriveEnabled();

        /// <summary>Keterangan keadaan cadangan Drive untuk kartu informasi — selalu bisa dibaca pengguna.</summary>
        public string KeteranganCadanganDrive
        {
            get
            {
                if (!AppPreferenceStore.IsAutoBackupFormulirDriveEnabled())
                {
                    return "Cadangan otomatis ke Google Drive sedang dimatikan di Pengaturan Aplikasi. " +
                           "Formulir tetap aman di folder Templates komputer ini.";
                }

                return IsDriveReady
                    ? "Cadangan otomatis ke Google Drive aktif. Formulir baru ikut diunggah ke folder " +
                      $"\u201c{DriveFormulirFolderName}\u201d supaya bisa dipakai ulang di komputer lain."
                    : "Belum masuk Google Drive, jadi cadangan otomatis belum berjalan — formulir tetap " +
                      "tersimpan di folder Templates. Masuk lewat menu Login dengan Google bila ingin " +
                      "cadangan otomatis dan unduhan dari Drive.";
            }
        }

        /// <summary>
        /// Drive dipakai hanya bila OAuth dikonfigurasi DAN token tersimpan
        /// (user sudah login). Tanpa token, Drive sengaja tidak disentuh agar
        /// proses unduh tidak memunculkan jendela browser login.
        /// </summary>
        private bool IsDriveReady =>
            _driveService != null && _driveService.IsOAuthEnabled && _driveService.HasStoredToken();

        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand DownloadCommand { get; }


    }
}
