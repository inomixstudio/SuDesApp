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
        // ===== Interval pemantauan data IDM (6 jam / 24 jam / hanya saat dibuka) =====

        /// <summary>
        /// Pilihan interval pemantauan IDM. Urutannya mengikuti aturan di
        /// <see cref="IntervalPemantauanIdmPrefs"/>: paling sering → harian →
        /// tanpa pengulangan.
        /// </summary>
        public static IReadOnlyList<PilihanIntervalIdmVM> DaftarIntervalIdm()
            => new[]
            {
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.EnamJam),
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.DuaPuluhEmpatJam),
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.HanyaSaatDibuka)
            };

        public IReadOnlyList<PilihanIntervalIdmVM> PilihanIntervalIdm { get; }
            = DaftarIntervalIdm();

        private PilihanIntervalIdmVM _intervalIdmTerpilih;

        /// <summary>
        /// Interval pemantauan IDM yang dipilih. Tersimpan langsung saat dipilih
        /// dan langsung diterapkan ke pemantau latar — tanpa menutup aplikasi.
        /// </summary>
        public PilihanIntervalIdmVM IntervalIdmTerpilih
        {
            get => _intervalIdmTerpilih;
            set
            {
                if (value == null || ReferenceEquals(value, _intervalIdmTerpilih)) return;
                if (!SetProperty(ref _intervalIdmTerpilih, value)) return;

                IntervalPemantauanIdmPrefs.Simpan(value.Nilai);
                OnPropertyChanged(nameof(IntervalIdmKeterangan));
                _statusSesaat.Tampilkan($"Tersimpan — pemantauan IDM: {value.Judul}");

                // Terapkan segera: hentikan pengulangan, atau ganti jeda timer
                // pemantau yang sedang berjalan. Null-safe karena halaman
                // pengaturan juga dipakai di sesi tanpa layanan latar (uji).
                _provider.GetService<PemantauIdmBaru>()?.TerapkanPreferensi();
            }
        }

        /// <summary>Penjelasan pilihan interval pemantauan yang sedang dipilih.</summary>
        public string IntervalIdmKeterangan => _intervalIdmTerpilih.Keterangan;

        private KecepatanAnimasiPilihanVM _kecepatanAnimasiTerpilih;

        /// <summary>
        /// Kecepatan animasi yang dipilih. Tersimpan langsung saat dipilih, dan
        /// langsung berlaku pada animasi berikutnya — tidak perlu menutup aplikasi.
        /// </summary>
        public KecepatanAnimasiPilihanVM KecepatanAnimasiTerpilih
        {
            get => _kecepatanAnimasiTerpilih;
            set
            {
                if (value == null || ReferenceEquals(value, _kecepatanAnimasiTerpilih)) return;
                if (!SetProperty(ref _kecepatanAnimasiTerpilih, value)) return;

                KecepatanAnimasiPrefs.Simpan(value.Nilai);

                OnPropertyChanged(nameof(KecepatanAnimasiContohInfo));
                _statusSesaat.Tampilkan($"Tersimpan — kecepatan animasi: {value.Judul}");
            }
        }

        /// <summary>Keterangan kecepatan yang sedang berlaku beserta durasi nyatanya.</summary>
        public string KecepatanAnimasiContohInfo =>
            $"Contoh di bawah memakai kecepatan {_kecepatanAnimasiTerpilih.Judul}: " +
            $"sidebar {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSidebar)}, " +
            $"dropdown {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarDropdown)}, " +
            $"sorotan menu {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSorotMasuk)}.";

        public PengaturanAplikasiViewModel(
            NavigationService navigation,
            ILogger<PengaturanAplikasiViewModel> logger,
            IServiceProvider provider)
        {
            _navigation = navigation;
            _logger = logger;
            _provider = provider;
            _statusSesaat = new StatusSesaat(p => StatusMessage = p, StatusSiap);

            _googleDrive = _provider.GetService<GoogleDriveService>();
            _appConfig = _provider.GetService<AppConfig>();
            _pesan = _provider.GetService<IMessageService>();

            PilihBerkasDatabaseCommand = new AsyncRelayCommand(PilihBerkasDatabaseAsync);
            PakaiDatabaseBawaanCommand = new RelayCommand(PakaiDatabaseBawaan, () => DatabasePunyaPilihan);
            BukaFolderDatabaseCommand = new RelayCommand(BukaFolderDatabase);
            SimpanMulaiUlangDatabaseCommand = new AsyncRelayCommand(SimpanMulaiUlangDatabaseAsync, () => !DatabaseSibuk);

            // Kredensial OAuth: nilai yang sudah tersimpan dimuat supaya teknisi
            // bisa memeriksa/memperbarui tanpa mengetik ulang dari nol.
            var kredensialTersimpan = GoogleClientCredentials.Load();
            _googleClientId = kredensialTersimpan?.ClientId ?? string.Empty;
            _googleClientSecret = kredensialTersimpan?.ClientSecret ?? string.Empty;
            SimpanKredensialGoogleCommand = new AsyncRelayCommand(SimpanKredensialGoogleAsync);
            UjiKredensialGoogleCommand = new AsyncRelayCommand(UjiKredensialGoogleAsync);

            // Langganan sudah pindah menjadi halaman tersendiri di navigasi menu
            // utama (LanggananViewModel / LanggananView) — tidak lagi di sini.

            // Kecepatan animasi yang sedang berlaku dipasang sebagai pilihan terpilih;
            // nilainya ditulis langsung ke bidang (bukan properti) supaya membuka halaman
            // ini tidak dianggap sebagai perubahan pengguna.
            _kecepatanAnimasiTerpilih = PilihanKecepatanAnimasi
                .First(p => p.Nilai == KecepatanAnimasiPrefs.SaatIni);

            // Sama seperti kecepatan animasi: diisi lewat bidang (bukan properti)
            // supaya membuka halaman ini tidak dianggap sebagai perubahan pengguna.
            _intervalIdmTerpilih = PilihanIntervalIdm
                .First(p => p.Nilai == IntervalPemantauanIdmPrefs.SaatIni);

            SiapkanBagian();

            TambahBaris(new SettingRowVM(
                "Backup otomatis ke Google Drive saat aplikasi ditutup",
                "Mengunggah cadangan database, lampiran, dan template ke folder SuDesApp-Backup di Google Drive setiap aplikasi ditutup (maksimal sekali sehari, hanya bila ada akun Google yang terhubung).",
                AppPreferenceStore.IsBackupDriveOnExitEnabled(),
                v => AppPreferenceStore.SetBackupDriveOnExitEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Backup otomatis formulir ke Google Drive",
                "Menyinkronkan template formulir PDF ke folder SuDesApp-Formulir di Google Drive setiap ada unduhan baru atau template ditambah/dihapus pada Pengaturan Formulir. Berjalan hanya bila ada akun Google yang terhubung.",
                AppPreferenceStore.IsAutoBackupFormulirDriveEnabled(),
                v => AppPreferenceStore.SetAutoBackupFormulirDriveEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Periksa pembaruan aplikasi saat dibuka",
                "Saat aktif, aplikasi memeriksa rilis terbaru di latar belakang ketika dibuka lalu memberitahu lewat lonceng notifikasi apa yang diperbaiki. Perbaikan kecil dipasang sebagai pembaruan tambalan (hanya berkas yang berubah, tanpa installer), perubahan besar memakai installer penuh. Saat nonaktif, pemeriksaan hanya dijalankan dari menu Pembaruan.",
                AppPreferenceStore.IsPeriksaPembaruanSaatMulai(),
                v => AppPreferenceStore.SetPeriksaPembaruanSaatMulai(v)));

            TambahBaris(new SettingRowVM(
                "Unduh pembaruan otomatis di latar belakang",
                "Saat aktif, pembaruan yang ditemukan langsung diunduh di latar belakang tanpa perlu membuka menu Pembaruan. Setelah selesai, lonceng notifikasi menawarkan \"Instal Sekarang\" atau \"Instal Saat Keluar\". Saat nonaktif, pembaruan hanya diberitahukan dan diunduh bila Anda memintanya dari menu Pembaruan.",
                AppPreferenceStore.IsUnduhPembaruanOtomatis(),
                v => AppPreferenceStore.SetUnduhPembaruanOtomatis(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Batas ukuran unduhan otomatis (MB)",
                "Ukuran maksimal installer penuh yang boleh diunduh di latar belakang (10-2048 MB, bawaan 300 MB). Rilis yang lebih besar tetap diberitahukan lewat notifikasi dan hanya diunduh bila Anda memintanya dari menu Pembaruan, sehingga kuota internet tidak terpakai tanpa sepengetahuan Anda.",
                AppPreferenceStore.GetBatasUkuranUnduhanMb(),
                v => AppPreferenceStore.SetBatasUkuranUnduhanMb(v),
                () => AppPreferenceStore.GetBatasUkuranUnduhanMb(),
                min: 10, maks: 2048));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Batas ukuran pembaruan kecil otomatis (MB)",
                "Ukuran maksimal perubahan yang boleh diunduh & dipasang sebagai pembaruan kecil di latar belakang (1-500 MB, bawaan 25 MB). Paket yang lebih besar tetap ditawarkan lewat notifikasi dan dipasang manual dari menu Pembaruan.",
                AppPreferenceStore.GetBatasUkuranTambalanMb(),
                v => AppPreferenceStore.SetBatasUkuranTambalanMb(v),
                () => AppPreferenceStore.GetBatasUkuranTambalanMb(),
                min: 1, maks: 500));

            TambahBaris(new SettingRowVM(
                "Mode diam-diam saat aplikasi dibuka",
                "Saat aktif, pekerjaan latar yang tidak penting bagi pengguna — pemeriksaan pembaruan online serta pembersihan PDF sementara, hasil ekspor lama, dan draft kedaluwarsa — ditunda beberapa menit setelah aplikasi dibuka, sehingga jendela login dan halaman utama muncul lebih cepat. Rotasi log tetap berjalan sejak dini karena kecil dan diperlukan.",
                AppPreferenceStore.IsStartupDiamDiam(),
                v => AppPreferenceStore.SetStartupDiamDiam(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Jeda mode diam-diam (menit)",
                "Berapa menit pekerjaan latar ditunda setelah aplikasi dibuka (1-60 menit, bawaan 5 menit). Semakin besar jeda, semakin ringan pembukaan aplikasi; pembaruan dan pembersihan tetap berjalan otomatis setelah jeda berlalu.",
                AppPreferenceStore.GetStartupDiamDiamMenit(),
                v => AppPreferenceStore.SetStartupDiamDiamMenit(v),
                () => AppPreferenceStore.GetStartupDiamDiamMenit(),
                min: 1, maks: 60));

            TambahBaris(new SettingRowVM(
                "Riwayat aktivitas",
                "Mencatat siapa (email Google atau admin) yang membuat, mengubah, mengubah status, dan menghapus surat maupun arsip. Catatannya tampil pada menu Riwayat Aktivitas.",
                AppPreferenceStore.IsActivityLoggingEnabled(),
                v => AppPreferenceStore.SetActivityLoggingEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Bersihkan PDF sementara saat aplikasi dibuka",
                "Menghapus berkas pratinjau PDF lama di folder TempPDF agar tidak menumpuk selama pemakaian.",
                AppPreferenceStore.IsCleanupTempPdfEnabled(),
                v => AppPreferenceStore.SetCleanupTempPdfEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Hapus hasil ekspor lama (lebih dari 30 hari)",
                "Berkas PDF hasil ekspor pada folder Output/PDF yang berumur lebih dari 30 hari dihapus otomatis ketika aplikasi dibuka.",
                AppPreferenceStore.IsCleanupOldExportsEnabled(),
                v => AppPreferenceStore.SetCleanupOldExportsEnabled(v)));

            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        /// <summary>Sesi aktif sekarang (dibaca ulang tiap halaman dibuka).</summary>
        public string SesiAktif =>
            $"{SessionContext.Display} — {SessionContext.LoginMethod} — sesi dimulai {SessionContext.LoginTime:dd-MM-yyyy HH:mm}";

        /// <summary>Lokasi penyimpanan preferensi.</summary>
        public string LokasiPreferensi => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp", "login_prefs.json");


    }
}
