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
        // =====================================================================
        // Lokasi berkas database desa.
        //
        // Database desa boleh disimpan di folder datanya sendiri (mis. drive D
        // atau folder arsip desa) supaya tidak tercampur berkas aplikasi dan
        // tidak perlu disalin ulang setiap aplikasi diperbarui. Pilihannya
        // disimpan di preferensi pengguna lalu dibaca AppConfig saat aplikasi
        // dibuka — jadi baru berlaku setelah aplikasi ditutup lalu dibuka lagi.
        // =====================================================================

        /// <summary>Buka pemilih berkas untuk menentukan lokasi database desa.</summary>
        public AsyncRelayCommand PilihBerkasDatabaseCommand { get; }

        /// <summary>Simpan pilihan lokasi lalu tutup & buka kembali aplikasi sekarang.</summary>
        public AsyncRelayCommand SimpanMulaiUlangDatabaseCommand { get; }

        /// <summary>Kembalikan penggunaan database ke lokasi bawaan (folder aplikasi).</summary>
        public RelayCommand PakaiDatabaseBawaanCommand { get; }

        /// <summary>Buka folder lokasi database yang sedang dipakai di Windows Explorer.</summary>
        public RelayCommand BukaFolderDatabaseCommand { get; }

        /// <summary>Lokasi database yang sedang dipakai aplikasi (dibaca saat start-up).</summary>
        public string LokasiDatabaseAktif => string.IsNullOrWhiteSpace(_appConfig?.DatabasePath)
            ? "(tidak diketahui)"
            : _appConfig!.DatabasePath;

        /// <summary>Lokasi database bawaan dari appsettings (desa.db di folder aplikasi).</summary>
        public string LokasiDatabaseBawaan => string.IsNullOrWhiteSpace(_appConfig?.DatabasePathBawaan)
            ? "(tidak diketahui)"
            : _appConfig!.DatabasePathBawaan;

        /// <summary>True bila lokasi yang sedang dipakai berasal dari pilihan pengguna.</summary>
        public bool DatabaseAktifKustom => _appConfig?.DatabaseKustom ?? false;

        /// <summary>True bila sedang dipakai lokasi bawaan (tombol kembali ke bawaan dimatikan).</summary>
        public bool DatabasePakaiBawaan => !DatabaseAktifKustom && !DatabasePunyaPilihan;

        /// <summary>Ada pilihan lokasi tersimpan — baik sudah berlaku maupun menunggu aplikasi dibuka ulang.</summary>
        public bool DatabasePunyaPilihan => !string.IsNullOrWhiteSpace(AppPreferenceStore.GetJalurDatabase());

        /// <summary>
        /// Keterangan pilihan lokasi yang sudah tersimpan tetapi BELUM berlaku karena
        /// aplikasi belum dibuka ulang; kosong bila tidak ada pilihan menggantung.
        /// </summary>
        public string DatabasePilihanTersimpan
        {
            get
            {
                var jalur = AppPreferenceStore.GetJalurDatabase();
                if (string.IsNullOrWhiteSpace(jalur)) return string.Empty;

                return jalur.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $"Menunggu aplikasi dibuka ulang: {jalur}";
            }
        }

        /// <summary>True bila ada pilihan lokasi yang menunggu aplikasi dibuka ulang.</summary>
        public bool AdanyaDatabasePilihanTersimpan => DatabasePilihanTersimpan.Length > 0;

        private string _infoDatabase = string.Empty;

        /// <summary>Hasil tindakan terakhir pada bagian Database (kosong = belum ada tindakan).</summary>
        public string InfoDatabase
        {
            get => _infoDatabase;
            private set
            {
                if (SetProperty(ref _infoDatabase, value))
                    OnPropertyChanged(nameof(AdanyaInfoDatabase));
            }
        }

        /// <summary>True bila ada keterangan hasil tindakan untuk ditampilkan.</summary>
        public bool AdanyaInfoDatabase => InfoDatabase.Length > 0;

        /// <summary>Pilih berkas database desa lain lewat pemilih berkas Windows.</summary>
        private Task PilihBerkasDatabaseAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pilih berkas database desa",
                    Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false,
                    InitialDirectory = FolderDatabase(),
                };

                if (dialog.ShowDialog() != true) return Task.CompletedTask;

                var jalur = Path.GetFullPath(dialog.FileName);

                if (jalur.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase))
                {
                    InfoDatabase = "Berkas itu sudah dipakai aplikasi saat ini.";
                    return Task.CompletedTask;
                }

                // Periksa lebih dahulu agar berkas yang startup pasti tolak tidak
                // tersimpan sebagai pilihan: kondisi itu bisa mengunci pengguna
                // keluar dari aplikasi sebelum ia sempat memilih database lain.
                // Database terenkripsi versi lama BOLEH dipilih selama kuncinya
                // tersedia — aplikasi membukanya memakai kunci itu.
                var status = BerkasDatabase.Periksa(jalur);

                switch (status)
                {
                    case StatusBerkasDatabase.BerkasLamaTerkunci:
                        InfoDatabase =
                            "Berkas ini database terenkripsi dari versi SuDesApp lama, tetapi " +
                            "berkas kuncinya tidak ada atau tidak cocok di komputer ini. Pilihan " +
                            "tidak disimpan. Salin DesaKunciDb.bin dari komputer asal ke " +
                            $"{BerkasDatabase.LokasiBerkasKunci}, atau pulihkan cadangan .db yang " +
                            "tidak terenkripsi.";
                        return Task.CompletedTask;

                    case StatusBerkasDatabase.TidakTerbaca:
                        InfoDatabase =
                            "Berkas SQLite ini tidak dapat dibaca dan kemungkinan rusak atau " +
                            "tidak lengkap. Pilihan tidak disimpan. Pilih berkas lain atau " +
                            "pulihkan dari cadangan.";
                        return Task.CompletedTask;

                    case StatusBerkasDatabase.Terkunci:
                        InfoDatabase =
                            "Berkas database sedang dipakai proses lain atau aksesnya ditolak, " +
                            "sehingga bentuknya belum bisa diperiksa. Pilihan tidak disimpan. " +
                            "Tutup aplikasi lain yang memakai berkas itu lalu pilih kembali.";
                        return Task.CompletedTask;
                }

                AppPreferenceStore.SetJalurDatabase(jalur);
                SegarkanInfoDatabase();
                _statusSesaat.Tampilkan(status == StatusBerkasDatabase.Terenkripsi
                    ? "Lokasi database disimpan (database terenkripsi — kuncinya tersedia di komputer ini) — berlaku setelah aplikasi dibuka ulang."
                    : "Lokasi database disimpan — berlaku setelah aplikasi dibuka ulang.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memilih berkas database");
                InfoDatabase = "Berkas database tidak dapat dipilih dari sini.";
            }

            return Task.CompletedTask;
        }

        /// <summary>Kembali memakai database bawaan aplikasi (folder aplikasi).</summary>
        private void PakaiDatabaseBawaan()
        {
            AppPreferenceStore.SetJalurDatabase(null);
            SegarkanInfoDatabase();
            _statusSesaat.Tampilkan(DatabaseAktifKustom
                ? "Pilihan lokasi dihapus — aplikasi memakai database bawaan setelah dibuka ulang."
                : "Sudah memakai database bawaan aplikasi.");
        }

        /// <summary>
        /// Tombol "Simpan & Mulai Ulang Sekarang" pada bagian Database Desa.
        /// Catatan penting tentang urutan data: PilihBerkasDatabaseAsync sudah
        /// menyimpan pilihan ke preferensi SAAAT berkas dipilih (bukan saat tombol
        /// ini ditekan), jadi tombol ini hanya (1) memastikan memang ada perubahan
        /// yang menunggu diterapkan, (2) meminta konfirmasi terakhir, lalu
        /// (3) menutup dan membuka kembali aplikasi.
        /// </summary>
        private async Task SimpanMulaiUlangDatabaseAsync()
        {            // Tidak ada yang perlu diterapkan = jangan buang waktu pengguna
            // dengan mulai ulang sia-sia. Dua keadaan demikian: (1) pilihan tersimpan
            // SUDAH dipakai aplikasi saat ini, (2) tidak ada pilihan dan aplikasi
            // memang sedang memakai lokasi bawaan.
            var pilihan = AppPreferenceStore.GetJalurDatabase();
            var sudahBerlaku = !string.IsNullOrWhiteSpace(pilihan)
                && pilihan.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase);

            if (sudahBerlaku)
            {
                InfoDatabase = "Lokasi database yang tersimpan sudah dipakai aplikasi saat ini — "
                    + "tidak ada perubahan yang menunggu diterapkan.";
                return;
            }

            if (string.IsNullOrWhiteSpace(pilihan) && !DatabaseAktifKustom)
            {
                InfoDatabase = "Pilih berkas database terlebih dahulu (atau tekan \"Kembali ke Bawaan\" "
                    + "bila sedang memakai lokasi lain) — belum ada perubahan yang menunggu diterapkan.";
                return;
            }

            var namaLokasi = DatabaseAktifKustom
                ? Path.GetFileName(LokasiDatabaseAktif)
                : "database bawaan (folder aplikasi)";

            var setuju = await KonfirmasiDatabaseAsync(
                "Simpan & Mulai Ulang Sekarang",
                "Aplikasi akan ditutup lalu dibuka kembali sekarang juga supaya lokasi database baru "
                + "(\"" + namaLokasi + "\") langsung dipakai.\n\n"
                + "Pastikan tidak ada proses cetak atau pengisian surat yang sedang berjalan.");

            if (!setuju) return;

            try
            {
                DatabaseSibuk = true;

                // 1. Pasang penanda "lahir dari mulai ulang" untuk proses baru.
                MulaiUlangAplikasi.PasangPenanda();

                // 2. Jadwalkan pembukaan kembali: proses pembantu menunggu proses ini
                //    benar-benar selesai (termasuk backup Drive di OnExit) sebelum
                //    menjalankan exe yang sama. Gagal jadwal = JANGAN menutup aplikasi.
                if (!MulaiUlangAplikasi.JadwalkanProsesBaru())
                {
                    MulaiUlangAplikasi.BersihkanPenanda();
                    InfoDatabase = "Pembukaan kembali gagal dijadwalkan — aplikasi tidak ditutup. "
                        + "Lokasi database tetap tersimpan dan berlaku pada pembukaan berikutnya.";
                    return;
                }

                InfoDatabase = "Menutup aplikasi untuk mulai ulang…";

                // 3. Tutup jendela utama dengan melewati dialog "Yakin ingin keluar?"
                //    (penutupan ini disengaja) lalu OnExit berjalan normal — backup
                //    Drive saat keluar ikut selesai sebelum proses baru mengambil alih.
                (Application.Current.MainWindow as MainWindow)?.KeluarUntukMulaiUlang();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan mulai ulang untuk penerapan lokasi database");
                MulaiUlangAplikasi.BersihkanPenanda();
                InfoDatabase = "Mulai ulang gagal dijalankan: " + ex.Message;
            }
            finally
            {
                // Shutdown sudah berjalan — ini hanya jaring pengaman bila penutupan
                // dibatalkan (mis. jendela ditutup lewat tombol lain).
                DatabaseSibuk = false;
            }
        }

        /// <summary>Buka folder tempat database yang sedang dipakai berada.</summary>
        private void BukaFolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(LokasiDatabaseAktif);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    InfoDatabase = "Folder database tidak ditemukan.";
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                _statusSesaat.Tampilkan("Folder database dibuka: " + folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder database");
                InfoDatabase = "Folder database tidak dapat dibuka dari sini.";
            }
        }

        /// <summary>Folder awal pemilih berkas: folder database yang sedang dipakai.</summary>
        private string? FolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(LokasiDatabaseAktif);
                return Directory.Exists(folder) ? folder : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Konfirmasi ke pengguna; bila layanan pesan tidak tersedia, pilihan dianggap setuju.</summary>
        private async Task<bool> KonfirmasiDatabaseAsync(string judul, string pesan)
        {
            if (_pesan == null) return true;

            try
            {
                return await _pesan.ShowConfirmationAsync(judul, pesan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dialog konfirmasi database gagal ditampilkan");
                return true;
            }
        }

        /// <summary>Segarkan seluruh tampilan bagian Database setelah pilihan berubah.</summary>
        private void SegarkanInfoDatabase()
        {
            InfoDatabase = DatabasePunyaPilihan
                ? "Lokasi database tersimpan. Tutup lalu buka kembali aplikasi supaya lokasi baru dipakai."
                : "Aplikasi memakai database bawaan di folder aplikasi.";

            OnPropertyChanged(nameof(DatabasePunyaPilihan));
            OnPropertyChanged(nameof(DatabasePakaiBawaan));
            OnPropertyChanged(nameof(DatabasePilihanTersimpan));
            OnPropertyChanged(nameof(AdanyaDatabasePilihanTersimpan));
            PakaiDatabaseBawaanCommand.RaiseCanExecuteChanged();
        }


    }
}
