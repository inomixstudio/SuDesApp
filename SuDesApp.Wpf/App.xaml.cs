using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using Microsoft.Data.Sqlite;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf
{
    public partial class App : Application
    {
        /// <summary>
        /// Nama mutex satu-instance. Nilainya HARUS sama dengan <c>AppMutex</c> di
        /// <c>installer/SuDesApp.iss</c>: installer memakainya untuk mengetahui
        /// bahwa aplikasi sedang berjalan sebelum menimpa berkasnya. Tanpa mutex
        /// yang benar-benar dibuat aplikasi, pemeriksaan itu tidak pernah melihat
        /// apa pun dan upgrade bisa berjalan saat desa.db masih terkunci.
        /// </summary>
        internal const string NamaMutexInstansi = "SuDesApp_SingletonMutex";

        private Mutex? _mutexInstansi;
        private bool _mutexDimiliki;

        private IServiceProvider? _serviceProvider;

        /// <summary>Akses penyedia layanan global (dipakai kode non-DI seperti PdfPreviewView).</summary>
        public IServiceProvider ServiceProvider => _serviceProvider
            ?? throw new InvalidOperationException("ServiceProvider belum diinisialisasi.");

        /// <summary>
        /// Akses cepat layanan cetak PDF dari kode non-DI (mis. PdfPreviewView):
        /// dialog cetak Windows standar + PDFium → langsung ke printer.
        /// </summary>
        public static Services.PdfPrintService PrintService =>
            (App.Current as App)?.ServiceProvider.GetRequiredService<Services.PdfPrintService>()
            ?? throw new InvalidOperationException("ServiceProvider belum diinisialisasi.");

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Satu instance per sesi Windows. Dua instance yang menulis database
            // desa yang sama bisa saling mengunci (SQLITE_BUSY) dan membuat
            // perubahan salah satu instance hilang tanpa pesan. Mutex ini juga
            // dibaca installer lewat AppMutex saat upgrade.
            _mutexInstansi = new Mutex(initiallyOwned: true, NamaMutexInstansi, out bool mutexBaru);
            _mutexDimiliki = mutexBaru;
            if (!mutexBaru)
            {
                MessageBox.Show(
                    "SuDesApp sudah berjalan di komputer ini. Tutup jendela aplikasi yang " +
                    "sudah terbuka, lalu buka lagi dari menu Mulai.",
                    "SuDesApp", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Roda mouse dirantai ke penampil yang benar-benar bisa bergulir.
            // Tanpa ini, roda terasa mati begitu kursor berada di atas ListBox
            // atau DataGrid yang ScrollBar-nya dimatikan (halaman tidak ikut
            // bergulir karena kejadiannya sudah ditelan penampil dalam).
            SuDesApp.Wpf.Utilities.RodaMouseBerantai.Pasang();

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "error.log");
                    System.IO.File.AppendAllText(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED:\n{args.Exception}\n\n");
                    // Dialog bertema (fallback aman: bila dialog bertema gagal
                    // karena exception sangat dini, tampil MessageBox biasa).
                    try
                    {
                        // Kata "Error" sengaja tidak dipakai: pesan berbahasa Indonesia
                        // yang menjelaskan keadaan + langkah lanjutan lebih menenangkan.
                        SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                            "Aplikasi mengalami gangguan",
                            "Terjadi gangguan yang tidak terduga. Pekerjaan yang sudah tersimpan tetap aman.\n\n" +
                            $"Rincian: {args.Exception.Message}\n\n" +
                            "Aplikasi dapat dilanjutkan; bila gangguan berulang, buka menu Pembaruan " +
                            "atau laporkan rincian di atas.",
                            SuDesApp.Utilities.AppMessageButton.Ok, SuDesApp.Utilities.AppMessageIcon.Error);
                    }
                    catch
                    {
                        MessageBox.Show(
                            "Terjadi gangguan yang tidak terduga.\n\n" +
                            $"Rincian: {args.Exception.Message}\n\n" +
                            "Pekerjaan yang sudah tersimpan tetap aman.",
                            "Aplikasi mengalami gangguan",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch { }
                args.Handled = true;
            };

            // Galat dari perintah (AsyncRelayCommand) ditangani terpisah dari
            // DispatcherUnhandledException: perintah yang gagal sementara
            // (database terkunci, berkas sedang dipakai, jaringan putus) cukup
            // diberi tahu — bukan dialog "gangguan tidak terduga" yang terlihat
            // seperti aplikasi rusak. Ditulis ke error.log oleh LaporkanGagal.
            SuDesApp.Wpf.Mvvm.AsyncRelayCommand.PenangkapGagal = ex =>
            {
                try
                {
                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Perintah tidak selesai",
                        "Operasi tadi tidak berhasil diselesaikan. Perubahan yang sudah " +
                        "tersimpan tetap aman.\n\n" +
                        $"Rincian: {ex.Message}\n\n" +
                        "Silakan coba lagi. Bila berulang, periksa apakah berkas sedang dibuka " +
                        "di aplikasi lain atau space penyimpanan sudah hampir penuh.",
                        SuDesApp.Utilities.AppMessageButton.Ok,
                        SuDesApp.Utilities.AppMessageIcon.Warning);
                }
                catch
                {
                    MessageBox.Show(
                        "Operasi tadi tidak berhasil diselesaikan.\n\n" +
                        $"Rincian: {ex.Message}",
                        "Perintah tidak selesai",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            _serviceProvider = ConfigureServices();

            // ==== Jendela pembuka ====
            // Tampil SEBELUM pekerjaan berat (enkripsi, skema, migrasi) supaya
            // aplikasi langsung terlihat hidup — tanpa ini layar kosong selama
            // beberapa detik dan aplikasi terasa "macet" saat baru dibuka.
            var splash = new SplashStartupWindow();
            splash.Tahap("Menyiapkan aplikasi...");
            splash.Show();
            // Beri kesempatan splash digambar lebih dulu sebelum pekerjaan berat.
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);

            var totalStartup = System.Diagnostics.Stopwatch.StartNew();
            var fasa = System.Diagnostics.Stopwatch.StartNew();
            var loggerStartup = _serviceProvider.GetRequiredService<ILogger<App>>();

            // ==== Katalog sumber formulir ====
            // Daftar kabupaten dan tautan unduhannya dibaca dari berkas konfigurasi
            // di profil pengguna, sehingga operator dapat memperbarui tautan tanpa
            // merilis ulang aplikasi. Berkas rusak tidak menghentikan start: daftar
            // bawaan yang dipakai (lihat KatalogFormulirDaerah).
            SuDesApp.Wpf.Utilities.KatalogFormulirDaerah.Muat(loggerStartup);

            // ==== Enkripsi database (SQLCipher) ====
            // Connection string dijadikan bentuk final SEBELUM koneksi pertama
            // dibuka: berkas plaintext lama dicadangkan lalu dikonversi, berkas yang
            // sudah terenkripsi disisipi kunci DPAPI mesin ini. Seluruh repository,
            // SettingsManager, dan ActivityLogService membaca AppConfig yang sama —
            // jadi satu titik ini cukup dan tidak boleh ditunda sampai ada koneksi
            // yang telanjur terbuka tanpa kunci.
            //
            // Gagal di sini berarti data desa tidak bisa dibaca sama sekali; kalau
            // diteruskan hanya muncul galat "file is not a database" yang
            // membingungkan di setiap halaman, jadi aplikasi dihentikan dengan
            // penjelasan yang bisa ditindaklanjuti.
            splash.Tahap("Mengunci database (enkripsi)...");
            fasa.Restart();
            try
            {
                var appConfig = _serviceProvider.GetRequiredService<AppConfig>();
                // JaminTerkunci membuka dan membaca seluruh berkas database —
                // CPU + I/O berat, dijalankan di thread latar supaya jendela splash
                // tetap bergeming dan tombol minimize tidak membeku.
                var koneksiTerkunci = await Task.Run(() => EnkripsiDatabase.JaminTerkunci(
                    appConfig.DatabaseConnectionString, loggerStartup));
                appConfig.DatabaseConnectionString = koneksiTerkunci;
                loggerStartup.LogInformation("Startup — enkripsi database {Ms} ms.", fasa.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _serviceProvider.GetRequiredService<ILogger<App>>()
                    .LogError(ex, "Gagal menyiapkan enkripsi database");                try
                {
                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Database", ex.Message,
                        SuDesApp.Utilities.AppMessageButton.Ok, SuDesApp.Utilities.AppMessageIcon.Error);
                }
                catch
                {
                    MessageBox.Show(ex.Message, "Database",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                splash.Close();
                Shutdown();
                return;
            }

            // ==== Inisialisasi & migrasi database ====
            // Menjalankan skema desa.db.sql, menyemai daftar jenis surat dari
            // JenisSuratConfig.json (termasuk NTCR N1–N6 + N8), membuat tabel NTCR,
            // dan menjalankan migrasi kolom untuk database lama — semuanya idempoten.
            // Tanpa ini blanko NTCR tidak punya baris jenis surat sehingga nomor
            // suratnya tidak pernah terisi dan penyimpanannya gagal.
            splash.Tahap("Menyiapkan skema dan data awal...");
            fasa.Restart();
            try
            {
                // Skema, migrasi, dan penyemaian berjalan di thread latar: UI thread
                // bebas menggambar splash, dan tahapannya yang terasa bukan layar beku.
                await Task.Run(async () =>
                {
                    using var scopeInisialisasi = _serviceProvider.CreateScope();
                    await scopeInisialisasi.ServiceProvider
                        .GetRequiredService<IDatabaseInitializer>()
                        .InitializeAsync();

                    // Contoh template surat adalah data awal aplikasi, bukan berkas
                    // pada folder instalasi. Pasang setelah tabel siap agar installer
                    // baru langsung menampilkan contoh tanpa harus membuka menunya
                    // terlebih dahulu. Layanan ini idempoten dan menghormati template
                    // bawaan yang sudah pernah dihapus pengguna.
                    await scopeInisialisasi.ServiceProvider
                        .GetRequiredService<SuDesApp.Services.TemplateSuratBawaanService>()
                        .PasangOtomatisAsync();
                });
                loggerStartup.LogInformation("Startup — inisialisasi database {Ms} ms.", fasa.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                var loggerDb = _serviceProvider.GetRequiredService<ILogger<App>>();
                loggerDb.LogError(ex, "Gagal inisialisasi database saat startup");

                try
                {
                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Database",
                        "Gagal menyiapkan database:\n" + ex.Message +
                        "\n\nBeberapa fitur (mis. penomoran surat) mungkin tidak berfungsi.",
                        SuDesApp.Utilities.AppMessageButton.Ok,
                        SuDesApp.Utilities.AppMessageIcon.Warning);
                }
                catch
                {
                    MessageBox.Show("Gagal menyiapkan database: " + ex.Message,
                        "Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // ==== Mode diam-diam startup ====
            // Rotasi error.log TETAP berjalan sejak dini (kecil, perlu agar log harian
            // tetap rapi), tetapi pembersihan yang menyapu folder (TempPDF, Output/PDF)
            // ditunda beberapa menit bila mode diam-diam aktif — jadi jendela login
            // dan halaman utama muncul lebih cepat tanpa berebut disk I/O.
            StartupFileCleanup.RotateErrorLog(_serviceProvider.GetRequiredService<ILogger<App>>());
            JadwalkanPembersihanStartup();

            // Terapkan tema tersimpan SEBELUM LoginWindow ditampilkan agar warna form
            // login konsisten dengan tema utama (default: Green). Tanpa ini LoginWindow
            // selalu terbuka dengan tema default lalu berubah begitu MainWindow di-resolve.
            splash.Tahap("Menyiapkan jendela login...");
            fasa.Restart();
            _serviceProvider.GetRequiredService<ThemeService>();

            // ==== Persetujuan dokumen hukum ====
            // Muncul SEBELUM login pada pemasangan pertama, dan lagi setiap versi
            // Kebijakan Privasi / Syarat & Ketentuan dinaikkan. Ditempatkan setelah
            // tema diterapkan agar dialognya sewarna dengan login. Menolak berarti
            // aplikasi tidak dilanjutkan — sama dengan bunyi Syarat dan Ketentuan.
            // Kegagalan MEMBACA dokumen tidak boleh mengunci aplikasi: pengguna
            // desa tetap bisa bekerja, dan dialog dicoba lagi di pemakaian berikutnya.
            try
            {
                if (PersetujuanDokumenService.PerluPersetujuan())
                {
                    var persetujuan = new PersetujuanDokumenWindow { Owner = splash };
                    bool setuju = persetujuan.ShowDialog() == true && persetujuan.Disetujui;

                    loggerStartup.LogInformation(
                        "Dialog persetujuan dokumen ditampilkan — disetujui: {Setuju}", setuju);

                    if (!setuju)
                    {
                        splash.Close();
                        Shutdown();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                loggerStartup.LogWarning(ex,
                    "Pemeriksaan persetujuan dokumen gagal; aplikasi dilanjutkan tanpa dialog.");
            }

            var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();

            // Pastikan akun administrator default sudah tersisipkan ke tabel
            // Pengguna SEBELUM jendela login ditampilkan — bila belum selesai
            // saat pengguna mengklik Login, MasukAsync tidak akan menemukannya.
            var loginVm = _serviceProvider.GetRequiredService<LoginViewModel>();
            await loginVm.InisialisasiAsync();

            loggerStartup.LogInformation(
                "Startup — siap menampilkan login ({Total} ms sejak aplikasi dimulai).",
                totalStartup.ElapsedMilliseconds);
            splash.Close();
            var loginResult = loginWindow.ShowDialog();
            if (loginResult != true) { Shutdown(); return; }

            // Izin lapisan data baru ditegakkan setelah login berhasil, saat
            // LoginViewModel sudah menetapkan identitas dan peran sesi. Ini
            // menjaga jalur autentikasi/startup tetap dapat bekerja, sekaligus
            // mencegah halaman yang dibuka langsung melewati pembatasan menu.
            SuDesApp.Utilities.SessionContext.SetPenegakanAktif(true);

            fasa.Restart();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();
            loggerStartup.LogInformation("Startup — jendela utama tampil ({Ms} ms setelah login).",
                fasa.ElapsedMilliseconds);

            // Proses ini lahir dari tombol "Simpan & Mulai Ulang Sekarang" (Database
            // Desa): penandanya sudah selesai dipakai — hapus supaya keluar-buka
            // biasa berikutnya tidak salah dianggap hasil mulai ulang.
            if (MulaiUlangAplikasi.AdanyaPenandaMulaiUlang)
            {
                MulaiUlangAplikasi.BersihkanPenanda();
            }

            // Layanan WhatsApp: listener webhook pesan masuk (Meta Cloud API) +
            // poller jawaban Google Sheet (mode tautan). Keduanya aman walau
            // belum dikonfigurasi — hanya mencatat log dan tetap diam.
            StartWhatsAppServices();

            // API Desa: listener otomatis menyala bila pengaturannya tertinggal
            // aktif dari sesi sebelumnya. Tanpa ini, sistem luar (dashboard,
            // website desa) kehilangan endpoint setiap aplikasi mulai ulang
            // sampai operator kebetulan membuka halaman API lagi.
            MulaiApiDesa();

            // Pembersihan draft kedaluwarsa (> 30 hari): cek latar belakang setelah
            // MainWindow tampil — bila ada, tampilkan KONFIRMASI sebelum menghapus.
            _ = StaleDraftCleanup.RunAfterStartupAsync(
                _serviceProvider,
                _serviceProvider.GetRequiredService<ILogger<App>>());

            // ==== Pemantauan lisensi ====
            // Setelah jendela utama tampil, cek lisensi di latar belakang (sekali
            // kini, lalu berkala). Ini menyegarkan "cek terakhir" di dalam token
            // sehingga masa tenggang offline dihitung dari koneksi terakhir yang
            // benar-benar berhasil.
            MulaiPemantauanLisensi();

            // ==== Pemberitahuan lisensi saat aplikasi dijalankan ====
            // Instalasi baru: popup meminta memulai trial 14 hari (menu vital
            // terkunci sampai itu terjadi). Trial habis: popup mengingatkan dan
            // mengarahkan ke halaman pembayaran setiap aplikasi dibuka.
            TampilkanPemberitahuanLisensi(mainWindow);

            // Denyut sambungan: memulihkan chip status server lisensi tanpa
            // menunggu halaman Langganan dibuka ulang (lihat DetakSambunganServer).
            _serviceProvider!.GetRequiredService<SuDesApp.Services.DetakSambunganServer>().Mulai();

            // ==== Pengumuman broadcast ====
            // Informasi terbaru dari penyedia: dimulai 15 detik setelah jendela
            // tampil (tidak menahan penggambaran awal), lalu menagih tiap 60
            // detik. Popup hanya muncul untuk pengumuman yang belum pernah
            // ditampilkan di perangkat ini — bukan tiap detak.
            _serviceProvider!.GetRequiredService<Wpf.Services.PengumumanLatar>().Mulai();

            // ==== Deteksi data IDM tahun baru ====
            // Portal pusat bisa menerbitkan tahun baru saat aplikasi sedang
            // dipakai. Pemantau ini membandingkan cadangan IDM terakhir dengan
            // jawaban portal (45 detik setelah tampil, lalu tiap enam jam) dan
            // mengirim notifikasi lonceng bila ada tahun yang lebih baru.
            _serviceProvider!.GetRequiredService<Wpf.Services.PemantauIdmBaru>().Mulai();
        }

        /// <summary>Timer pembersihan tertunda mode diam-diam (null = tidak dijadwalkan).</summary>
        private System.Timers.Timer? _timerPembersihanDiamDiam;

        /// <summary>
        /// Jadwalkan pembersihan berkas startup (TempPDF lama, hasil ekspor lama).
        /// Mode diam-diam aktif → berjalan setelah jeda menit berlalu via timer;
        /// nonaktif → perilaku lama: langsung sebelum UI tampil.
        /// </summary>
        private void JadwalkanPembersihanStartup()
        {
            var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
            var config = ServiceProvider.GetRequiredService<AppConfig>();

            if (!AppPreferenceStore.IsStartupDiamDiam())
            {
                // Pembersihan membaca dan menghapus banyak berkas (TempPDF lama,
                // hasil ekspor) — jalankan di thread latar supaya tampilan jendela
                // login tidak tertahan I/O disk saat aplikasi baru dibuka.
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { JalankanPembersihanStartup(logger, config); }
                    catch (Exception ex) { logger.LogWarning(ex, "Pembersihan startup gagal."); }
                });
                return;
            }

            var menit = AppPreferenceStore.GetStartupDiamDiamMenit();
            var timer = new System.Timers.Timer(menit * 60_000)
            {
                AutoReset = false // cukup sekali; sisa berkas akan tersapu start berikutnya
            };
            timer.Elapsed += (_, _) =>
            {
                try
                {
                    JalankanPembersihanStartup(logger, config);
                    logger.LogInformation(
                        "Pembersihan startup (mode diam-diam) selesai setelah jeda {Menit} menit.", menit);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Pembersihan startup tertunda gagal.");
                }
                finally
                {
                    timer.Dispose();
                    if (ReferenceEquals(_timerPembersihanDiamDiam, timer)) _timerPembersihanDiamDiam = null;
                }
            };
            _timerPembersihanDiamDiam = timer;
            timer.Start();
        }

        /// <summary>Isi pembersihan startup: PDF sementara lama + hasil ekspor lama.</summary>
        private void JalankanPembersihanStartup(ILogger<App> logger, AppConfig config)
        {
            // PDF sementara lama (> 7 hari) agar TempPDF tidak menumpuk.
            // Dapat dimatikan lewat Pengaturan Aplikasi.
            if (AppPreferenceStore.IsCleanupTempPdfEnabled())
            {
                TempPdfCleanup.CleanOldFiles(config, logger);
                // Sisa konversi berkas Word lama (.doc → .docx) yang terputus.
                TempPdfCleanup.CleanOldWordConversions(logger);
                // Sisa unduhan pembaruan yang dijeda lalu tak pernah dilanjutkan lagi.
                TempPdfCleanup.CleanOldDownloadParts(logger);
            }

            // Hapus hasil ekspor lama di Output/PDF (> 30 hari).
            // Dapat dimatikan lewat Pengaturan Aplikasi.
            if (AppPreferenceStore.IsCleanupOldExportsEnabled())
            {
                StartupFileCleanup.CleanOldExports(
                    config,
                    logger,
                    StartupFileCleanup.ExportRetentionDays);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Lepaskan mutex satu-instance supaya proses berikutnya (atau installer
            // yang menunggu) langsung bisa berjalan. Instance kedua tidak memiliki
            // mutex, jadi hanya instance pemilik yang melepasnya.
            try
            {
                if (_mutexInstansi != null && _mutexDimiliki)
                {
                    _mutexInstansi.ReleaseMutex();
                }
            }
            catch { /* proses sudah berakhir secara tidak normal */ }
            finally
            {
                _mutexInstansi?.Dispose();
                _mutexInstansi = null;
                _mutexDimiliki = false;
            }

            // Instance kedua (yang ditolak mutex) keluar sebelum container dibuat.
            if (_serviceProvider is null)
            {
                base.OnExit(e);
                return;
            }

            // Timer diam-diam tidak perlu melanjutkan proses saat aplikasi keluar.
            if (_timerPembersihanDiamDiam != null)
            {
                try { _timerPembersihanDiamDiam.Stop(); _timerPembersihanDiamDiam.Dispose(); } catch { }
                _timerPembersihanDiamDiam = null;
            }
            // Timer pemantauan lisensi tidak perlu melanjutkan proses saat keluar.
            if (_timerLisensi != null)
            {
                try { _timerLisensi.Stop(); _timerLisensi.Dispose(); } catch { }
                _timerLisensi = null;
            }
            // Denyut sambungan server lisensi cukup sampai di sini juga.
            try { _serviceProvider?.GetService<SuDesApp.Services.DetakSambunganServer>()?.Berhenti(); } catch { }
            // Timer deteksi data IDM tahun baru juga cukup sampai di sini.
            try { _serviceProvider?.GetService<Wpf.Services.PemantauIdmBaru>()?.Berhenti(); }
            catch { }
            // Backup harian database ke Google Drive saat aplikasi ditutup.
            // Dijalankan latar belakang dengan batas waktu; tidak pernah
            // menunda atau menggagalkan penutupan aplikasi.
            // Dapat dimatikan lewat Pengaturan Aplikasi.
            if (AppPreferenceStore.IsBackupDriveOnExitEnabled())
            {
                TryBackupToDriveOnExit();
            }

            if (_serviceProvider is IDisposable disposable) disposable.Dispose();
            base.OnExit(e);
        }

        private void TryBackupToDriveOnExit()
        {
            try
            {
                var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("GoogleBackup");

                if (!ServiceProvider.GetRequiredService<GoogleDriveService>().IsOAuthEnabled)
                {
                    logger.LogInformation("Backup Drive dilewati: OAuth belum dikonfigurasi.");
                    return;
                }
                if (!ServiceProvider.GetRequiredService<GoogleDriveService>().HasStoredToken())
                {
                    logger.LogInformation("Backup Drive dilewati: belum ada akun Google yang terhubung.");
                    return;
                }
                if (ServiceProvider.GetRequiredService<GoogleBackupService>().IsBackupDoneToday())
                {
                    logger.LogInformation("Backup Drive dilewati: sudah dilakukan hari ini.");
                    return;
                }

                logger.LogInformation("Memulai backup harian database ke Google Drive...");
                var backup = ServiceProvider.GetRequiredService<GoogleBackupService>();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                // Jalankan di THREAD POOL (bukan UI thread) agar tidak ada
                // penangkapan DispatcherSynchronizationContext — menghindari
                // deadlock penutupan aplikasi (sinkron .GetResult() di UI thread
                // + awaite tanpa ConfigureAwait(false) = proses hang selamanya).
                var backupTask = Task.Run(
                    () => ServiceProvider.GetRequiredService<GoogleBackupService>().BackupNowAsync(cts.Token),
                    cts.Token);

                // Tunggu berbatas waktu. Selama jeda ini snapshot & upload berjalan
                // di latar belakang tanpa memblokir UI; bila melebihi batas, aplikasi
                // TETAP menutup (tugas latar belakang dihentikan saat proses keluar).
                if (!backupTask.Wait(TimeSpan.FromMinutes(2)))
                {
                    logger.LogWarning("Backup Google Drive tidak selesai dalam 2 menit — aplikasi tetap keluar.");
                    return;
                }

                var uploaded = backupTask.GetAwaiter().GetResult();
                if (uploaded.Count > 0)
                {
                    backup.MarkBackupDoneToday();
                    logger.LogInformation("Backup harian Google Drive selesai: {Files}", string.Join(", ", uploaded));

                    // Notifikasi lonceng (fallback aman bila UI sudah ditutup).
                    try
                    {
                        App.Current?.Dispatcher.Invoke(() =>
                            _serviceProvider?.GetRequiredService<NotificationService>()
                                .Success("Backup Otomatis Drive",
                                    $"{uploaded.Count} berkas cadangan harian terunggah ke folder {GoogleBackupService.BackupFolderName}."));
                    }
                    catch { /* Aplikasi sedang menutup — abaikan. */ }
                }
                else
                {
                    logger.LogWarning("Backup harian Google Drive tidak menghasilkan berkas — penanda harian tidak diisi agar dicoba lagi.");
                }
            }
            catch (OperationCanceledException)
            {
                (ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleBackup"))
                    .LogWarning("Backup Google Drive dibatalkan (melebihi 2 menit).");
            }
            catch (Exception ex)
            {
                // Jangan pernah gagalkan penutupan aplikasi karena backup.
                try
                {
                    (ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("GoogleBackup"))
                        .LogError(ex, "Backup Google Drive gagal saat aplikasi ditutup");

                    App.Current?.Dispatcher.Invoke(() =>
                        ServiceProvider.GetRequiredService<NotificationService>()
                            .Error("Backup Otomatis Drive", "Backup harian gagal: " + ex.Message));
                }
                catch { /* logger pun tidak tersedia */ }
            }
        }

        /// <summary>Jeda antar-cek lisensi selama aplikasi berjalan.</summary>
        private static readonly TimeSpan JedaCekLisensi = TimeSpan.FromHours(6);

        /// <summary>Timer pemantauan lisensi (null = belum dimulai / sudah dihentikan).</summary>
        private System.Timers.Timer? _timerLisensi;

        /// <summary>
        /// Pantau lisensi di latar belakang: sekali segera, lalu berkala.
        ///
        /// Cek ini TIDAK mengunci fitur apa pun. Gunanya menjaga masa tenggang
        /// tetap mutakhir: server menerbitkan token dengan <c>cek</c> = waktu
        /// sekarang, dan aplikasi sah sampai max(jatuh tempo, cek + 7 hari).
        /// Selama masih bisa menghubungi server, masa tenggang itu terus
        /// diperbarui; setelah 7 hari tanpa koneksi — dan tanpa pembayaran yang
        /// mencakup tanggal itu — lisensi berakhir sendiri. Bila kode aktivasi
        /// belum diisi, cek hanya membaca cache dan tidak menghubungi server.
        /// </summary>
        private void MulaiPemantauanLisensi()
        {
            // Gerbang aktif sejak aplikasi dibuka: terapkan status dari cache
            // lokal (tanpa jaringan) sebelum cek online yang pertama selesai.
            try
            {
                _serviceProvider!.GetRequiredService<SuDesApp.Services.PenjagaLisensi>()
                    .SegarkanDariCache();
            }
            catch (Exception ex)
            {
                (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("Lisensi"))
                    .LogWarning(ex, "Gagal menyegarkan penjaga lisensi dari cache saat startup");
            }

            // Cek pertama jalan di thread latar supaya tidak menahan apa pun
            // yang sedang digambar setelah login.
            _ = Task.Run(CekLisensiSekaliAsync);

            var timer = new System.Timers.Timer(JedaCekLisensi.TotalMilliseconds) { AutoReset = true };
            timer.Elapsed += (_, _) => _ = Task.Run(CekLisensiSekaliAsync);
            _timerLisensi = timer;
            timer.Start();
        }

        /// <summary>
        /// Pemberitahuan lisensi setiap aplikasi dijalankan, sesuai alur uji coba:
        ///
        /// • Instalasi baru (Demo): popup meminta memulai trial 14 hari. Menu vital
        ///   tetap terkunci bila pengguna menolak.
        /// • Trial habis / lisensi belum aktif (BacaSaja): popup mengingatkan bahwa
        ///   masa uji coba berakhir dan mengarahkan ke halaman pembayaran; menu
        ///   vital tetap terkunci sampai pembayaran selesai.
        ///
        /// Dipanggil SETELAH jendela utama tampil dan dijalankan lewat Dispatcher
        /// supaya popup tidak menghalangi penggambaran awal.
        /// </summary>
        private void TampilkanPemberitahuanLisensi(Window owner)
        {
            SuDesApp.Services.PenjagaLisensi penjaga;
            try
            {
                penjaga = _serviceProvider!.GetRequiredService<SuDesApp.Services.PenjagaLisensi>();
                penjaga.SegarkanDariCache();
            }
            catch (Exception ex)
            {
                try
                {
                    _serviceProvider!.GetRequiredService<ILogger<App>>()
                        .LogWarning(ex, "Gagal menyiapkan pemberitahuan lisensi saat startup");
                }
                catch { }
                return;
            }

            owner.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    switch (penjaga.Keadaan)
                    {
                        case SuDesApp.Services.KeadaanLisensi.Demo:
                            {
                                bool mulai = SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                                    "Mulai masa uji coba 14 hari",
                                    SuDesApp.Services.PenjagaLisensi.PesanMulaiTrial,
                                    SuDesApp.Utilities.AppMessageButton.YesNo,
                                    SuDesApp.Utilities.AppMessageIcon.Question) == true;

                                if (!mulai)
                                {
                                    // Menolak trial: menu vital tetap terkunci, dan
                                    // pengguna diberi tahu jalan keluarnya.
                                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                                        "Menu vital terkunci",
                                        SuDesApp.Services.PenjagaLisensi.PesanBelumTrial,
                                        SuDesApp.Utilities.AppMessageButton.Ok,
                                        SuDesApp.Utilities.AppMessageIcon.Warning);
                                    break;
                                }

                                // Trial server didahulukan bila kode aktivasi sudah ada
                                // dan server terjangkau; trial lokal hanya cadangan offline.
                                var percobaan = await penjaga.MulaiPercobaanAsync();
                                var (judulPercobaan, pesanPercobaan) =
                                    SuDesApp.Services.PenjagaLisensi.JelaskanPercobaan(percobaan);

                                if (percobaan.BerlakuSampai is DateTime sampaiPercobaan)
                                {
                                    pesanPercobaan += $" Berlaku sampai {sampaiPercobaan.ToLocalTime():dd MMM yyyy}.";
                                }

                                bool ditolak = percobaan.Asal
                                    is SuDesApp.Services.AsalPercobaan.DitolakServer
                                    or SuDesApp.Services.AsalPercobaan.SudahPernahTrial;

                                SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                                    judulPercobaan,
                                    pesanPercobaan,
                                    SuDesApp.Utilities.AppMessageButton.Ok,
                                    ditolak
                                        ? SuDesApp.Utilities.AppMessageIcon.Warning
                                        : SuDesApp.Utilities.AppMessageIcon.Info);
                                break;
                            }

                        case SuDesApp.Services.KeadaanLisensi.BacaSaja:
                            {
                                // Trial habis (tanpa kode aktivasi) dibedakan dari
                                // langganan berbayar yang berakhir supaya pesannya tepat.
                                bool trialHabis = penjaga.TrialPernahDimulai && !penjaga.PunyaKodeAktivasi;
                                string judul = trialHabis ? "Masa uji coba berakhir" : "Lisensi belum aktif";
                                string pesan = trialHabis
                                    ? SuDesApp.Services.PenjagaLisensi.PesanTrialHabis
                                    : SuDesApp.Services.PenjagaLisensi.PesanAksesDikunci;

                                bool bukaLangganan = SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                                    judul,
                                    pesan + "\n\nBuka halaman Langganan sekarang untuk menyelesaikan pembayaran?",
                                    SuDesApp.Utilities.AppMessageButton.YesNo,
                                    SuDesApp.Utilities.AppMessageIcon.Warning) == true;

                                if (bukaLangganan && owner.DataContext is SuDesApp.Wpf.ViewModels.MainWindowViewModel vm)
                                {
                                    await vm.ShowLanggananPublicAsync();
                                }
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        _serviceProvider!.GetRequiredService<ILogger<App>>()
                            .LogWarning(ex, "Gagal menampilkan pemberitahuan lisensi");
                    }
                    catch { }
                }
            }));
        }

        /// <summary>
        /// Satu putaran cek lisensi. Tidak pernah melempar: offline adalah
        /// keadaan normal, dan cache bertanda tangan tetap dipakai sampai masa
        /// tenggangnya habis.
        /// </summary>
        private async Task CekLisensiSekaliAsync()
        {
            var sp = _serviceProvider!;
            var logger = sp.GetRequiredService<ILogger<App>>();
            try
            {
                var lisensi = sp.GetRequiredService<SuDesApp.Services.LisensiManager>();
                var status = await lisensi.CekAsync().ConfigureAwait(false);
                sp.GetRequiredService<SuDesApp.Services.PenjagaLisensi>().Terapkan(status);
                logger.LogInformation(
                    "Lisensi: aktif={Aktif}, paket={Paket}, berlakuSampai={Sampai}, sisa={Sisa} hari.",
                    status.Aktif,
                    string.IsNullOrWhiteSpace(status.Paket) ? "-" : status.Paket,
                    status.BerlakuSampai?.ToString("yyyy-MM-dd") ?? "-",
                    status.SisaHariTenggang);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cek lisensi berkala gagal.");
            }
        }

        /// <summary>
        /// Nyalakan listener API Desa bila pengaturan aktifnya sudah tertulis
        /// dari sesi sebelumnya. Kegagalan (port terpakai, kunci belum dibuat)
        /// hanya dicatat sebagai log: halaman API tetap menampilkan alasan
        /// lengkapnya bila operator membukanya, dan Mulai() aman dipanggil
        /// berkali-kali sehingga tidak bentrok dengan dinyalakan dari halaman.
        /// </summary>
        private void MulaiApiDesa()
        {
            if (!SuDesApp.Api.ApiPengaturan.Aktif) return;

            try
            {
                var listener = _serviceProvider!.GetRequiredService<SuDesApp.Api.ApiListener>();
                if (!listener.Mulai())
                {
                    (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("API"))
                        .LogWarning("API desa tertanda aktif tetapi gagal menyala: {Alasan}",
                            listener.GalatTerakhir ?? "-");
                }
            }
            catch (Exception ex)
            {
                (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("API"))
                    .LogWarning(ex, "Gagal memulai listener API desa");
            }
        }

        /// <summary>
        /// Menjalankan layanan latar WhatsApp: listener webhook Meta Cloud API
        /// (pesan masuk warga) dan poller jawaban Google Sheet (mode tautan).
        /// </summary>
        private void StartWhatsAppServices()
        {
            try
            {
                var listener = _serviceProvider!.GetRequiredService<MetaWebhookListener>();
                listener.Start(8787);
            }
            catch (Exception ex)
            {
                (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("WhatsApp"))
                    .LogWarning(ex, "Gagal memulai listener webhook Meta Cloud API");
            }

            try
            {
                _serviceProvider!.GetRequiredService<WaSheetIngestService>().Start();
            }
            catch (Exception ex)
            {
                (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("WhatsApp"))
                    .LogWarning(ex, "Gagal memulai poller Google Sheet");
            }
        }

        /// <summary>
        /// Alamat server lisensi untuk proses ini. Aturan pemilihan (konfigurasi
        /// produksi menang dan mengunci, preferensi hanya untuk loopback) ada di
        /// <see cref="SuDesApp.Services.PenentuAlamatLisensi"/> supaya bisa diuji.
        /// </summary>
        private static SuDesApp.Services.AlamatLisensi AlamatServerLisensi(IConfiguration configuration)
        {
            return SuDesApp.Services.PenentuAlamatLisensi.Tentukan(
                configuration["Lisensi:UrlServer"],
                SuDesApp.Utilities.AppPreferenceStore.GetUrlServerLisensi());
        }

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            // Log konsol memakai cap waktu: memudahkan menakar halaman/proses mana yang
            // lambat saat menguji performa (selisih antar-baris = durasi tahap itu).
            services.AddLogging(b =>
            {
                b.AddSimpleConsole(o =>
                {
                    o.TimestampFormat = "HH:mm:ss.fff ";
                    o.SingleLine = false;
                });
                b.AddDebug();
                b.SetMinimumLevel(LogLevel.Information);
            });

            // ==== Konfigurasi & lapisan data (replikasi AddCoreServices/AddDatabaseServices/AddRepositoryServices) ====
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();

            // Lisensi/langganan (hibris + lisensi server). Kunci publik
            // penandatangan dibaca dari appsettings (Lisensi:KunciPublik) —
            // inilah akar kepercayaannya. Bila kosong, LisensiManager
            // mengambilnya sekali dari /kunci-publik server lalu memetiknya di
            // %LocalAppData% (TOFU). Verifikasi tanda tangan berjalan lokal,
            // jadi aplikasi tetap bisa diverifikasi tanpa internet.
            services.AddSingleton(new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            });
            // Keadaan sambungan server lisensi yang dicatat panggilan sungguhan
            // (cek lisensi berkala, muat katalog) — dipakai baris menu Pusat
            // Administrasi di sidebar. Singleton: satu keadaan untuk seluruh
            // aplikasi, bukan keadaan per halaman.
            services.AddSingleton<SuDesApp.Services.SambunganServerLisensi>();

            services.AddSingleton<SuDesApp.Services.LisensiManager>(sp =>
            {
                var alamat = AlamatServerLisensi(sp.GetRequiredService<IConfiguration>());
                var manager = new SuDesApp.Services.LisensiManager(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.LisensiManager>>(),
                    sp.GetRequiredService<System.Net.Http.HttpClient>(),
                    sp.GetRequiredService<SuDesApp.Services.SambunganServerLisensi>())
                {
                    // Alamat produksi dari appsettings mengunci diri: preferensi
                    // lama di profil pengguna tidak boleh menimpanya. Untuk
                    // loopback, preferensi tetap boleh menang agar server uji
                    // lokal bisa diganti dari halaman Langganan.
                    UrlServer = alamat.Url,
                    UrlServerTerkunci = alamat.Terkunci,
                    KunciPublik = sp.GetRequiredService<IConfiguration>()["Lisensi:KunciPublik"],
                    // Bukti langganan juga ditulis ke tabel LisensiAktivasi di
                    // database desa, sehingga kode aktivasi terhubung ke data dan
                    // ikut terbawa saat aplikasi mencadangkan datanya.
                    KoneksiDatabase = sp.GetRequiredService<AppConfig>().DatabaseConnectionString
                };
                manager.KodeAktivasi = SuDesApp.Utilities.AppPreferenceStore.GetKodeAktivasi();
                return manager;
            });

            // Penjaga gerbang fitur: memutuskan mode "penuh / baca-saja" dari
            // status lisensi. Nilainya diperbarui oleh detak lisensi berkala dan
            // halaman Langganan; diterapkan seketika ke cache saat startup.
            services.AddSingleton<SuDesApp.Services.PenjagaLisensi>(sp =>
            {
                var penjaga = new SuDesApp.Services.PenjagaLisensi(
                    sp.GetRequiredService<SuDesApp.Services.LisensiManager>(),
                    new SuDesApp.Services.TrialLokal(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.PenjagaLisensi>>(),
                    // Trial server butuh klien ini; tanpa kode aktivasi alur trial
                    // tetap berjalan lokal, jadi ketergantungannya opsional.
                    sp.GetRequiredService<SuDesApp.Services.LanggananKlien>())
                {
                    // Properti lama dipertahankan untuk konfigurasi yang mungkin
                    // masih menyebutkannya; sejak alur trial 14 hari, instalasi
                    // baru selalu terkunci sampai trial dimulai/dibayar.
                    KunciTanpaLangganan = string.Equals(
                        sp.GetRequiredService<IConfiguration>()["Lisensi:KunciTanpaLangganan"],
                        "true", StringComparison.OrdinalIgnoreCase)
                };
                return penjaga;
            });

            // Klien endpoint langganan publik (katalog harga & pembuatan tagihan).
            // Singleton: alamat server, HttpClient, dan jeda coba ulang setelah
            // kegagalan adalah keadaan bersama — halaman Langganan hanya
            // menampilkan hasilnya, dan katalog yang tersimpan di disk juga
            // diurus di sana (lihat docs/langganan-dan-lisensi.md).
            services.AddSingleton(sp => new SuDesApp.Services.LanggananKlien(
                sp.GetRequiredService<SuDesApp.Services.LisensiManager>(),
                sp.GetRequiredService<System.Net.Http.HttpClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.LanggananKlien>>(),
                sp.GetRequiredService<SuDesApp.Services.SambunganServerLisensi>()));

            // Denyut sambungan server lisensi: menanyakan /healthz secara
            // berkala di latar belakang supaya keadaan sambungan (chip
            // "Terhubung"/"Belum terjangkau") pulih sendiri tanpa membuka
            // ulang halaman Langganan. 30 detik saat belum tersambung,
            // 5 menit setelah tersambung.
            services.AddSingleton<SuDesApp.Services.DetakSambunganServer>();

            // Data IDM resmi Kemendesa (idm.kemendesa.go.id). HttpClient sendiri,
            // bukan HttpClient bersama: hanya klien ini yang perlu jalur cadangan
            // HTTP ketika sertifikat situs pusat kedaluwarsa, dan alamatnya tetap.
            services.AddSingleton<SuDesApp.Services.IIdmKemendesaKlien>(sp =>
                new SuDesApp.Services.IdmKemendesaKlien(
                    new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(25) },
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.IdmKemendesaKlien>>()));
            services.AddSingleton<SuDesApp.Services.IIndeksDesa2025Klien, SuDesApp.Services.IndeksDesa2025Klien>();

            // Klien pengumuman broadcast (informasi terbaru dari penyedia):
            // alamat server dari LisensiManager yang sama, HttpClient singleton.
            services.AddSingleton(sp => new SuDesApp.Services.PengumumanKlien(
                sp.GetRequiredService<SuDesApp.Services.LisensiManager>(),
                sp.GetRequiredService<System.Net.Http.HttpClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.PengumumanKlien>>()));

            // Pengingat latar: menagih pengumuman tiap 60 detik dan memunculkan
            // popup + lonceng untuk yang belum pernah tampil di perangkat ini.
            services.AddSingleton<Wpf.Services.PengumumanLatar>();

            // Deteksi data IDM tahun baru: membandingkan cadangan lokal dengan
            // portal pusat secara berkala, lalu memberi notifikasi lonceng.
            services.AddSingleton<Wpf.Services.PemantauIdmBaru>();

            services.AddSingleton<AppConfig>(sp =>
                new AppConfig(
                    sp.GetRequiredService<IConfiguration>(),
                    sp.GetService<ILogger<AppConfig>>()));

            services.AddSingleton<JenisSuratConfigLoader>(sp =>
                new JenisSuratConfigLoader(
                    sp.GetRequiredService<ICacheService>(),
                    sp.GetRequiredService<IConfiguration>(),
                    sp.GetRequiredService<ILogger<JenisSuratConfigLoader>>(),
                    configPath: Path.Combine(AppContext.BaseDirectory, "Configuration", "JenisSuratConfig.json")));
            services.AddSingleton<IJenisSuratConfigLoader>(sp => sp.GetRequiredService<JenisSuratConfigLoader>());

            // Pengaturan penomoran surat (halaman Pengaturan Aplikasi): mengubah awalan
            // nomor per jenis surat dan menyegarkan cache konfigurasi setelahnya.
            services.AddTransient<SuDesApp.Services.PenomoranSuratService>(sp =>
                new SuDesApp.Services.PenomoranSuratService(
                    sp.GetRequiredService<ILogger<SuDesApp.Services.PenomoranSuratService>>(),
                    sp.GetRequiredService<IJenisSuratConfigLoader>(),
                    sp.GetRequiredService<AppConfig>()));

            // Repositori Desa (singleton: dipakai MainWindowViewModel singleton dan tidak
            // bergantung SqliteConnection scoped).
            services.AddSingleton<IDesaRepository, DesaRepository>();

            // Query provider + interceptor + handler data surat (untuk SuratRepository).
            services.AddSingleton<QueryProvider>(sp =>
                new QueryProvider(
                    Path.Combine(AppContext.BaseDirectory, "Data", "Queries", "SuratQueries.sql"),
                    sp.GetService<ILogger<QueryProvider>>()!));
            services.AddSingleton<QueryInterceptor>();
            services.AddScoped<ISuratDataHandler, SKUDataHandler>();
            services.AddScoped<ISuratDataHandler, KematianDataHandler>();
            services.AddScoped<ISuratDataHandler, BedaNamaDataHandler>();
            services.AddScoped<ISuratDataHandler, GarapanDataHandler>();
            services.AddScoped<ISuratDataHandler, IzinOrtuHandler>();
            services.AddScoped<ISuratDataHandler, IjinTinggalDataHandler>();
            services.AddScoped<ISuratDataHandler, InstansiDataHandler>();
            services.AddScoped<ISuratDataHandler, KenalLahirDataHandler>();
            services.AddScoped<ISuratDataHandler, AhliWarisDataHandler>();
            services.AddScoped<ISuratDataHandler, PindahDataHandler>();
            services.AddScoped<ISuratDataHandler, KeramaianDataHandler>();
            // NTCR: satu class handler, diregistrasi sekali per NamaJenis agar dispatch
            // eksak di SuratRepository (h.NamaJenis.Equals(...)) cocok.
            foreach (var ntcrJenis in SuratConstants.NtcrSemua)
            {
                var namaJenis = ntcrJenis;
                services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
                    namaJenis,
                    sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
                    sp.GetRequiredService<ICacheService>()));
            }

            // Koneksi + UnitOfWork + repository (scoped per navigasi).
            services.AddScoped<SqliteConnection>(sp =>
                new SqliteConnection(sp.GetRequiredService<AppConfig>().DatabaseConnectionString));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IWargaRepository, WargaRepository>();
            services.AddScoped<IJenisSuratRepository, JenisSuratRepository>();
            services.AddScoped<ISuratRepository, SuratRepository>();
            services.AddScoped<IIzinOrtuRepository, IzinOrtuRepository>();
            services.AddScoped<IArsipSuratRepository, ArsipSuratRepository>();
            services.AddScoped<IArsipKeputusanRepository, ArsipKeputusanRepository>();
            services.AddScoped<IPermintaanWaRepository, PermintaanWaRepository>();

            // ==== Perangkat Desa (data perangkat + SK Bupati) ====
            // Repository/service scoped: keduanya memakai SqliteConnection scoped
            // yang sama seperti repository surat lain, sehingga seluruh penulisan
            // perangkat desa berada pada koneksi (dan transaksi) yang sudah dikenal.
            services.AddScoped<IPerangkatDesaRepository, PerangkatDesaRepository>();
            services.AddScoped<SuDesApp.Services.IPerangkatDesaService, SuDesApp.Services.PerangkatDesaService>();
            services.AddScoped<SuDesApp.Services.SkPerangkatLampiranService>();

            // ==== Data kependudukan ====
            // Impor massal (Excel/CSV) dipakai halaman Data Warga; Laporan Penduduk
            // memakai satu service scoped yang sama untuk layar, PDF, dan Excel
            // supaya ketiga keluaran dihitung dari satu sumber angka.
            services.AddScoped<SuDesApp.Services.IImporWargaService, SuDesApp.Services.ImporWargaService>();
            services.AddScoped<SuDesApp.Services.ILaporanPendudukService, SuDesApp.Services.LaporanPendudukService>();

            // ==== Akun, arsip tahunan, dan pusat dokumen ====
            services.AddScoped<IPenggunaRepository, PenggunaRepository>();
            services.AddScoped<SuDesApp.Services.IPenggunaService, SuDesApp.Services.PenggunaService>();
            services.AddScoped<ITutupBukuTahunRepository, TutupBukuTahunRepository>();
            // Bukti langganan (token bertanda tangan) disimpan di database desa
            // supaya kode aktivasi terhubung ke data dan ikut terbawa saat
            // pencadangan; Dao-nya disuntikkan ke LisensiManager di bawah.
            services.AddScoped<ILisensiAktivasiRepository, LisensiAktivasiRepository>();
            services.AddScoped<SuDesApp.Services.IVerifikasiPenomoranService, SuDesApp.Services.VerifikasiPenomoranService>();
            services.AddScoped<SuDesApp.Services.ITutupBukuTahunService, SuDesApp.Services.TutupBukuTahunService>();
            services.AddScoped<SuDesApp.Services.IArsipRegisterTahunanService, SuDesApp.Services.ArsipRegisterTahunanService>();

            // ==== API Desa (HTTP lokal) ====
            // Layanan per permintaan dibuat lewat scope dari provider, jadi tidak
            // ada layanan scoped yang ikut tertahan hidup selama listener berdiri.
            services.AddScoped<SuDesApp.Api.IApiRingkasanService, SuDesApp.Api.ApiRingkasanService>();
            services.AddScoped<SuDesApp.Api.IApiPermintaanService, SuDesApp.Api.ApiPermintaanService>();
            services.AddScoped<SuDesApp.Services.IVerifikasiSuratService, SuDesApp.Services.VerifikasiSuratService>();
            // Listener singleton: halaman API menyalakannya, dan pengaturannya
            // dibaca dari preferensi (bukan database) agar bisa dibuka sebelum login.
            services.AddSingleton(sp => new SuDesApp.Api.ApiListener(
                () => sp, sp.GetRequiredService<ILogger<SuDesApp.Api.ApiListener>>()));

            // Inisialisasi & migrasi database saat startup (skema, jenis surat,
            // tabel NTCR, migrasi kolom) — dipanggil lewat scope di App.OnStartup.
            services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

            // Backup/restore/statistik. Sengaja SINGLETON: ketiganya hanya
            // memerlukan AppConfig + logger, tidak menyentuh koneksi scoped.
            // DatabaseInitializer menyuntiknya, dan sebaliknya TIDAK ada
            // dependensi balik — itu yang menghindarkan siklus DI.
            services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();

            // Utilities
            services.AddSingleton<FileService>();
            services.AddSingleton<UpdateService>();
            // Pembaruan kecil (tambalan berkas): tidak perlu installer penuh untuk
            // perbaikan surat/tampilan. Didaftarkan sesudah UpdateService karena
            // memakai layanan itu untuk mengunduh berkas yang berubah.
            services.AddSingleton<PatchUpdateService>();
            services.AddScoped<DatabaseImportExportService>();
            services.AddSingleton<GoogleDriveService>();
            // Sheets API memakai token OAuth yang sama dengan Drive (mode layanan
            // online Google Sheet/Form: baca jawaban warga + tulis status balik).
            services.AddSingleton<GoogleSheetsService>();
            // Forms API membuat formulir otomatis; jawabannya disinkronkan ke Sheet.
            services.AddSingleton<GoogleFormsService>();
            services.AddSingleton<WaFormAutoSetupService>();
            services.AddSingleton<ActivityLogService>();
            services.AddSingleton<SuDesApp.Wpf.Services.PerdesSotkStore>();
            services.AddTransient<GoogleBackupService>();
            // Pembacaan berkas Word lampiran (Input SK/Peraturan). Berkas .doc lama
            // dikonversi otomatis memakai pengubah .doc → .docx di bawah.
            services.AddSingleton<IWordFileReader, WordFileReaderService>();
            // Konversi berkas Word lama (.doc) → .docx lewat Microsoft Word, dipakai
            // "Buat dari File Word" dan pembacaan lampiran SK/Peraturan supaya
            // pengguna tidak perlu konversi manual dulu.
            services.AddSingleton<IWordDocConverter, WordDocConverter>();

            // Manager & utilitas
            services.AddSingleton<SettingsManager>();

            // Data wilayah administratif Indonesia yang dibundel aplikasi
            // (provinsi s.d. desa/kelurahan + kode pos saran). Dibaca sekali lalu
            // diindeks di memori; pengguna kantor desa tidak selalu punya internet,
            // jadi pilihan wilayah tidak boleh bergantung layanan daring.
            services.AddSingleton(sp => new SuDesApp.Wilayah.WilayahDataStore(
                sp.GetRequiredService<ILogger<SuDesApp.Wilayah.WilayahDataStore>>()));

            // Penanda panduan awal (langkah pertama aplikasi): disimpan di preferensi
            // aplikasi supaya tetap berlaku antar sesi, dan bisa ditukar tiruannya saat uji.
            services.AddSingleton<SuDesApp.Utilities.IPanduanAwalStore, SuDesApp.Utilities.PanduanAwalStore>();

            // Penjaga data desa contoh: dipakai sebelum mencetak/menyimpan surat supaya
            // surat resmi tidak keluar memakai nama desa & pejabat contoh bawaan aplikasi.
            services.AddSingleton<SuDesApp.Utilities.IPeringatanDataDesaContoh, SuDesApp.Utilities.PeringatanDataDesaContoh>();

            // Penanda pemberitahuan sambutan di lonceng notifikasi: hanya boleh tampil
            // sekali seumur pemasangan, jadi penandanya ikut tersimpan di preferensi.
            services.AddSingleton<SuDesApp.Utilities.IPemberitahuanSambutanStore, SuDesApp.Utilities.PemberitahuanSambutanStore>();
            services.AddSingleton<FormulirMenuService>();

            // Message service khusus WPF
            services.AddSingleton<IMessageService, WpfMessageService>();

            // Layanan cetak PDF langsung ke printer (dialog cetak Windows + PDFium).
            services.AddSingleton<PdfPrintService>();

            // ==== WhatsApp (singleton) ====
            // Gateway tunggal: WhatsApp Cloud API resmi dari Meta (teks/tautan).
            services.AddSingleton<CloudApiWhatsAppGateway>();
            services.AddSingleton<IWhatsAppGateway, WaGatewaySelector>();
            services.AddSingleton<WaEngine>();
            // WaEngine/WaPanelViewModel menyelesaikannya dari DI (alur percakapan & pemrosesan
            // permintaan warga); WaSessionStore dibagi sebagai singleton, sisanya scoped.
            services.AddSingleton<WaSessionStore>();
            services.AddSingleton<WaAutoProcessor>();
            services.AddSingleton(sp => new MetaWebhookListener(
                sp.GetRequiredService<WaEngine>(),
                sp.GetRequiredService<ILogger<MetaWebhookListener>>(),
                () => AppPreferenceStore.GetWaCloudApiVerifyToken(),
                // App secret WAJIB diisi (Pengaturan > WhatsApp). Dulu provider ini
                // selalu mengembalikan null sehingga verifikasi tanda tangan tidak
                // pernah berjalan - siapa pun yang bisa menjangkau URL tunnel bisa
                // menyuntikkan pesan masuk yang otomatis memproses penerbitan surat.
                // Sekarang secret kosong berarti listener menolak semua request (503).
                () => AppPreferenceStore.GetWaCloudApiSecret()));
            // Poller jawaban Google Sheet (mode tautan): akses provider lewat closure
            // instance karena layanan dibuat sebelum _serviceProvider selesai diisi.
            services.AddSingleton(sp => new WaSheetIngestService(
                () => _serviceProvider,
                sp.GetRequiredService<WaEngine>(),
                sp.GetRequiredService<ILogger<WaSheetIngestService>>()));
            services.AddScoped<WaConversationService>();
            services.AddScoped<WaSuratProcessor>();

            // ==== Service dasar WPF ====
            services.AddSingleton<ThemeService>();
            services.AddSingleton<ScreenResolutionService>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<NavigationService>(sp =>
                new NavigationService(sp, sp.GetRequiredService<ILogger<NavigationService>>()));
        // ==== ViewModel & Windows (transient, di-resolve dalam scope navigasi) ====
            services.AddTransient<InputControlFactory>();
            services.AddTransient<SetelanViewModel>();
            services.AddTransient<PengaturanAplikasiViewModel>();
            // Pengaturan nomor surat: halaman mandiri (dahulu bagian Penomoran
            // di dalam Pengaturan Aplikasi).
            services.AddTransient<PengaturanNomorSuratViewModel>();
            // Langganan: halaman tersendiri di navigasi utama — langsung menerima
            // LisensiManager & PenjagaLisensi dari container.
            services.AddTransient<LanggananViewModel>();
            // Panduan awal butuh IServiceProvider untuk membuka halaman tujuannya
            // (Pengaturan Surat / Pengaturan Aplikasi) tanpa bergantung pada host.
            services.AddTransient<PanduanAwalViewModel>(sp => new PanduanAwalViewModel(
                sp.GetRequiredService<SettingsManager>(),
                sp.GetRequiredService<SuDesApp.Services.PenomoranSuratService>(),
                sp.GetRequiredService<SuDesApp.Utilities.IPanduanAwalStore>(),
                sp,
                sp.GetRequiredService<NavigationService>(),
                sp.GetRequiredService<ILogger<PanduanAwalViewModel>>()));
            services.AddTransient<FormulirViewModel>();
            services.AddTransient<RegisterSuratViewModel>();
            services.AddTransient<RegisterNtcrViewModel>();
            services.AddTransient<AgendaSuratViewModel>();
            services.AddTransient<RiwayatViewModel>();
            services.AddTransient<KeputusanViewModel>();
            services.AddTransient<DaftarHadirViewModel>();
            services.AddTransient<ExImdbViewModel>();
            services.AddTransient<GoogleLoginViewModel>();
            services.AddTransient<GoogleDriveViewModel>();
            services.AddTransient<PembaruanViewModel>();
            services.AddTransient<CatatanRilisViewModel>();
            services.AddTransient<BerandaViewModel>();
            services.AddTransient<WaPanelViewModel>();
            services.AddSingleton<PanduanWaViewModel>();
            services.AddTransient<InputWindowViewModel>();
            services.AddTransient<RekeningKoranViewModel>();
            services.AddTransient<InputAgendaViewModel>();
            services.AddTransient<InputKeputusanViewModel>();

            // Perangkat Desa: halaman data perangkat desa + penyusun SK Bupati.
            // Transient seperti halaman lain karena dibuat dari scope baru pada
            // ShowPerangkatDesaAsync; App.xaml memetakan view lewat DataTemplate
            // untuk PerangkatDesaViewModel.
            services.AddTransient<PerangkatDesaViewModel>();

            // SOTK Desa, Perdes SOTK, dan contoh Perdes dalam satu halaman menu DATA DESA.
            services.AddTransient<SotkDesaViewModel>();

            // Data kependudukan & API Desa: halaman di area konten utama, dipetakan
            // App.xaml lewat DataTemplate untuk masing-masing ViewModel.
            services.AddTransient<WargaViewModel>();
            services.AddTransient<LaporanViewModel>();
            services.AddTransient<ApiViewModel>();

            // Pusat dokumen (Dokumentasi) + pembaca dokumen. Pembaca selalu dibuka
            // untuk satu dokumen tertentu, jadi ia dibuat lewat factory yang menerima
            // judul, jalur berkas, dan aksi Kembali ke daftar dokumen.
            services.AddTransient<DokumentasiViewModel>();
            services.AddTransient<Func<string, string, Action?, DokumenBacaViewModel>>(sp =>
                (judul, jalur, kembali) => new DokumenBacaViewModel(
                    judul, jalur,
                    (_serviceProvider ?? throw new InvalidOperationException("Service provider aplikasi belum siap.")).GetRequiredService<NavigationService>(),
                    kembali,
                    (_serviceProvider ?? throw new InvalidOperationException("Service provider aplikasi belum siap.")).GetRequiredService<ILogger<DokumenBacaViewModel>>()));

            // Halaman IDM disimpan MainWindowViewModel dan dapat dibuka kembali
            // setelah navigasi lain menutup scope halaman. Karena itu factory
            // dokumentasinya harus memakai root provider, bukan provider scope.
            services.AddTransient<IdmViewModel>(sp => new IdmViewModel(
                sp.GetRequiredService<SettingsManager>(),
                sp.GetRequiredService<SuDesApp.Wilayah.WilayahDataStore>(),
                sp.GetRequiredService<SuDesApp.Services.IIdmKemendesaKlien>(),
                sp.GetRequiredService<ILogger<IdmViewModel>>(),
                simpanan: sp.GetRequiredService<SuDesApp.Services.IIdmSimpananStore>(),
                navigasi: sp.GetRequiredService<NavigationService>(),
                pemantauan: sp.GetRequiredService<SuDesApp.Services.IIdmPemantauanStore>(),
                dokumenFactory: sp.GetRequiredService<Func<string, string, Action?, DokumenBacaViewModel>>()));

            // Akun & arsip tahunan: halaman area konten utama (bukan jendela modal).
            services.AddTransient<KelolaPenggunaViewModel>();
            services.AddTransient<TutupBukuTahunViewModel>();
            services.AddTransient<VerifikasiSuratViewModel>();

            // Keuangan & pembangunan desa: APBDES (anggaran+realisasi) dan IDM.
            // IDM hanya menampilkan data resmi pusat, jadi dependensinya pengaturan
            // desa, data wilayah (penentu kode desa), dan klien Kemendesa. Cadangannya
            // (data terakhir yang berhasil dimuat + waktunya) disimpan lokal supaya
            // halaman tetap terisi saat internet mati.
            services.AddSingleton<SuDesApp.Services.IIdmSimpananStore, SuDesApp.Services.IdmSimpananStore>();
            // Catatan pemeriksaan portal terakhir (waktu + tahun terbit terbaru):
            // ditulis halaman IDM dan pemantau latar, dibaca halaman untuk indikator
            // "data terbaru yang terbit" — termasuk saat yang tampil data cadangan.
            services.AddSingleton<SuDesApp.Services.IIdmPemantauanStore, SuDesApp.Services.IdmPemantauanStore>();
            services.AddTransient<ApbdesViewModel>();
            services.AddTransient<IdmViewModel>();

            // Template Surat: jenis surat buatan pengguna sendiri.
            services.AddScoped<ITemplateSuratRepository, TemplateSuratRepository>();
            services.AddTransient<TemplateSuratGenerator>();
            // Contoh template siap pakai (pemasangan otomatis saat daftar masih kosong).
            services.AddTransient<SuDesApp.Services.TemplateSuratBawaanService>();
            // Pembacaan berkas Word (.docx) untuk membuat template surat dari berkas
            // yang sudah dipakai (bisa satu atau beberapa bentuk surat sekaligus).
            services.AddSingleton<SuDesApp.Services.ITemplateSuratWordImpor,
                SuDesApp.Services.TemplateSuratWordImporService>();
            // Pembaca berkas Word untuk arsip APBDES: teks dokumen + baris anggaran
            // (kode rekening, uraian, anggaran, realisasi) dari tabel Word.
            services.AddSingleton<SuDesApp.Services.IApbdesWordImpor,
                SuDesApp.Services.ApbdesWordImporService>();
            // Pratinjau singkat (halaman pertama) untuk dialog "Buat dari File Word":
            // pengguna melihat bentuk cetaknya sebelum bagian surat disimpan.
            services.AddTransient<SuDesApp.Wpf.Services.ITemplateSuratPratinjau,
                SuDesApp.Wpf.Services.TemplateSuratPratinjauService>();
            // Pencatatan surat template ke Register Surat (nomor, payload, edit).
            services.AddTransient<SuDesApp.Services.TemplateSuratRegisterService>();
            services.AddTransient<TemplateSuratViewModel>();
            services.AddTransient<TemplateSuratWizardViewModel>();
            services.AddTransient<IsiTemplateSuratViewModel>();

            // ViewModel input surat (dipakai InputControlFactory)
            services.AddTransient<SkdInputViewModel>();
            services.AddTransient<KematianInputViewModel>();
            services.AddTransient<KenalLahirInputViewModel>();
            services.AddTransient<SkckInputViewModel>();
            services.AddTransient<SkuInputViewModel>();
            services.AddTransient<SktmInputViewModel>();
            services.AddTransient<KeteranganTerbitInputViewModel>();
            services.AddTransient<DomisiliWargaInputViewModel>();
            services.AddTransient<DomisiliInstansiInputViewModel>();
            services.AddTransient<BedaNamaInputViewModel>();
            services.AddTransient<IzinOrtuInputViewModel>();
            services.AddTransient<IjinTinggalInputViewModel>();
            services.AddTransient<GarapanInputViewModel>();
            services.AddTransient<AhliWarisInputViewModel>();
            services.AddTransient<PengantarAdmindukInputViewModel>();
            services.AddTransient<PindahInputViewModel>();
            services.AddTransient<TidakPunyaRumahInputViewModel>();
            services.AddTransient<KeramaianInputViewModel>();
            services.AddTransient<NtcrInputViewModel>();
            // Alur paket NTCR: satu form untuk seluruh blanko N1–N6 sekaligus.
            services.AddTransient<NtcrPaketViewModel>();
            // Pemilik alur simpan surat (validasi draft eksplisit, transaksi atomik,
            // penomoran) — dipakai InputWindowViewModel & NtcrPaketService.
            services.AddScoped<SuDesApp.Services.SuratSaveService>();
            services.AddTransient<SuDesApp.Services.NtcrPaketService>();

            // ==== Generator PDF surat (transient; di-resolve dalam scope input surat) ====
            services.AddTransient<SKDGenerator>();
            services.AddTransient<SKUGenerator>();
            services.AddTransient<SKCKGenerator>();
            services.AddTransient<SKTMGenerator>();
            services.AddTransient<KeteranganTerbitGenerator>();
            services.AddTransient<KematianGenerator>();
            services.AddTransient<BedaNamaGenerator>();
            services.AddTransient<DomisiliWargaGenerator>();
            services.AddTransient<DomisiliInstansiGenerator>();
            services.AddTransient<GarapanGenerator>();
            services.AddTransient<IzinOrtuGenerator>();
            services.AddTransient<KenalLahirGenerator>();
            services.AddTransient<IjinTinggalGenerator>();
            services.AddTransient<AhliWarisGenerator>();
            services.AddTransient<PengantarAdmindukGenerator>();
            services.AddTransient<PindahGenerator>();
            services.AddTransient<TidakPunyaRumahGenerator>();
            services.AddTransient<KeramaianGenerator>();
            services.AddTransient<NtcrGenerator>();
            services.AddTransient<SuratRegisterGenerator>();
            services.AddTransient<RekeningKoranGenerator>();
            services.AddTransient<DaftarHadirGenerator>();
            // Daftar struktur perangkat desa (tombol Cetak PDF halaman Data Perangkat Desa).
            services.AddTransient<StrukturPerangkatGenerator>();
            // Formulir pendataan perangkat desa: blanko 18 kolom DATA PERANGKAT
            // untuk mengumpulkan data perangkat baru (tombol Formulir Pendataan).
            services.AddTransient<FormulirPerangkatGenerator>();
            // Generator SK Bupati: tidak mewarisi SuratGeneratorBase, jadi ia
            // mendaftarkan font "Times New Roman" sendiri lewat konstruktor
            // (lihat SkPerangkatGenerator) supaya PDF tetap tercetak walau SK
            // ini dokumen pertama pada sebuah sesi.
            services.AddTransient<SuDesApp.GeneratorPdf.SkPerangkatGenerator>();
            // Generator SOTK & Perdes: juga mendaftarkan font sendiri lewat
            // konstruktor supaya PDF tetap tercetak walau dokumen pertama sesi.
            services.AddTransient<SuDesApp.GeneratorPdf.SotkDesaGenerator>();
            services.AddTransient<SuDesApp.GeneratorPdf.PerdesSotkGenerator>();

            // ==== Factory rekanan (harus di-resolve dalam scope) ====
            // Factory form edit surat: resolve InputWindowViewModel untuk ditampilkan
            // langsung di content host utama (bukan modal). ConfigureForEditAsync
            // dipanggil oleh pemanggil setelah navigasi.
            services.AddTransient<Func<int, InputWindowViewModel>>(sp =>
                _ => sp.GetRequiredService<InputWindowViewModel>());
            services.AddTransient<Func<string, FormulirPdfViewModel>>(sp =>
                name => new FormulirPdfViewModel(
                    name,
                    sp.GetRequiredService<NavigationService>(),
                    sp.GetRequiredService<AppConfig>(),
                    sp.GetRequiredService<FileService>(),
                    sp.GetRequiredService<ILogger<FormulirPdfViewModel>>()));
            services.AddTransient<Func<string, string, PdfPreviewViewModel>>(sp =>
                (title, path) =>
                {
                    var root = _serviceProvider ?? throw new InvalidOperationException("Service provider aplikasi belum siap.");
                    return new PdfPreviewViewModel(title, path, root.GetRequiredService<NavigationService>(),
                        messageService: root.GetRequiredService<SuDesApp.Utilities.IMessageService>(),
                        peringatan: root.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>(),
                        penjaga: root.GetRequiredService<SuDesApp.Services.PenjagaLisensi>());
                });
            // Factory pratinjau surat (alur Buat/Edit Surat): menyertakan ID surat + factory form edit
            // agar tombol ✏️ Edit tampil di pratinjau.
            services.AddTransient<Func<string, string, int?, PdfPreviewViewModel>>(sp =>
                (title, path, suratId) => new PdfPreviewViewModel(
                    title, path, sp.GetRequiredService<NavigationService>(),
                    suratId, sp.GetRequiredService<Func<int, InputWindowViewModel>>(),
                    sp.GetRequiredService<SuDesApp.Utilities.IMessageService>(),
                    peringatan: sp.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>(),
                    penjaga: sp.GetRequiredService<SuDesApp.Services.PenjagaLisensi>()));
            // Formulir agenda & SK/Peraturan kini halaman di area konten utama (bukan jendela).
            services.AddTransient<Func<string, SuratKeluarMasukData?, SuratKeluarMasukData?, InputAgendaViewModel>>(sp =>
                (jenis, data, prefill) =>
                {
                    var vm = sp.GetRequiredService<InputAgendaViewModel>();
                    vm.Initialize(jenis, data, prefill);
                    return vm;
                });
            services.AddTransient<Func<string, DataKeputusan?, DataKeputusan?, InputKeputusanViewModel>>(sp =>
                (jenis, data, prefill) =>
                {
                    var vm = sp.GetRequiredService<InputKeputusanViewModel>();
                    vm.Initialize(jenis, data, prefill);
                    return vm;
                });
            // Wizard template kini halaman di area konten utama (bukan jendela terpisah).
            services.AddTransient<Func<int, TemplateSuratKustom?, TemplateSuratWizardViewModel>>(sp =>
                (mode, template) =>
                {
                    var vm = sp.GetRequiredService<TemplateSuratWizardViewModel>();
                    vm.Initialize(mode, template);
                    return vm;
                });
            services.AddTransient<Func<IsiTemplateSuratViewModel>>(sp =>
                () => sp.GetRequiredService<IsiTemplateSuratViewModel>());
            services.AddTransient<Func<string, string, Action?, PdfPreviewViewModel>>(sp =>
                (title, path, batalKembali) => new PdfPreviewViewModel(
                    title, path, sp.GetRequiredService<NavigationService>(),
                    messageService: sp.GetRequiredService<SuDesApp.Utilities.IMessageService>(),
                    batalKembali: batalKembali,
                    peringatan: sp.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>(),
                    penjaga: sp.GetRequiredService<SuDesApp.Services.PenjagaLisensi>()));

            // Singleton inti (login + main window)
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<LoginViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<LoginWindow>();
            services.AddSingleton<AboutViewModel>();
            services.AddSingleton<AboutView>();

            return services.BuildServiceProvider();
        }
    }
}
