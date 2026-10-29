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
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf
{
    public partial class App
    {
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

            // Windows dimatikan atau pengguna logoff: hentikan layanan lokal milik
            // aplikasi lebih dulu. Windows akan mematikan prosesnya juga, tetapi
            // tanpa ini server lisensi lokal dan API desa dibiarkan hidup sampai
            // sistem mencabut prosesnya — dan server lisensi yang dijalankan mode
            // anak (dotnet run) bisa tertinggal yatim piatu.
            SessionEnding += (_, _) =>
            {
                try { _serviceProvider?.GetService<SuDesApp.Services.ServerLisensiLokal>()?.Matikan(); }
                catch { /* sistem sedang menutup */ }
                try { _serviceProvider?.GetService<SuDesApp.Api.ApiListener>()?.Berhenti(); }
                catch { /* sistem sedang menutup */ }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    SuDesApp.Wpf.Services.LogGalat.Tulis("UNHANDLED", args.Exception);
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
                catch { /* pencatatan/dialog galat gagal — penanganan tetap dianggap selesai */ }
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
            // Tampil SEBELUM pekerjaan berat (skema dan migrasi) supaya
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

            // ==== Pemeriksaan database ====
            // Connection string disahkan SEBELUM koneksi pertama dibuka. Seluruh
            // repository, SettingsManager, dan ActivityLogService membaca AppConfig
            // yang sama — jadi satu titik ini cukup dan tidak boleh ditunda sampai
            // ada koneksi yang telanjur terbuka ke berkas yang tidak valid.
            //
            // Gagal di sini berarti data desa tidak bisa dibaca sama sekali; kalau
            // diteruskan hanya muncul galat "file is not a database" yang
            // membingungkan di setiap halaman, jadi aplikasi dihentikan dengan
            // penjelasan yang bisa ditindaklanjuti.
            splash.Tahap("Memeriksa database...");
            fasa.Restart();
            try
            {
                var appConfig = _serviceProvider.GetRequiredService<AppConfig>();
                // PastikanTerbaca membuka dan membaca seluruh berkas database —
                // CPU + I/O berat, dijalankan di thread latar supaya jendela splash
                // tetap bergeming dan tombol minimize tidak membeku.
                appConfig.DatabaseConnectionString = await Task.Run(() =>
                    BerkasDatabase.PastikanTerbaca(appConfig.DatabaseConnectionString));
                loggerStartup.LogInformation("Startup — pemeriksaan database {Ms} ms.", fasa.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _serviceProvider.GetRequiredService<ILogger<App>>()
                    .LogError(ex, "Gagal memeriksa database");                try
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


            // API Desa: listener otomatis menyala bila pengaturannya tertinggal
            // aktif dari sesi sebelumnya. Tanpa ini, sistem luar (dashboard,
            // website desa) kehilangan endpoint setiap aplikasi mulai ulang
            // sampai operator kebetulan membuka halaman API lagi.
            MulaiApiDesa();

            // Server lisensi lokal: menyala di latar belakang bila operator
            // mengaktifkannya; kegagalannya tidak menahan aplikasi dibuka.
            MulaiServerLisensiLokal();

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
            // Instalasi baru: popup meminta memulai trial 30 hari (menu vital
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

            // Hentikan API lokal lebih dulu agar port segera dilepas dan tidak ada
            // permintaan baru saat aplikasi sedang menutup/menjalankan backup.
            try
            {
                _serviceProvider.GetService<SuDesApp.Api.ApiListener>()?.Berhenti();
            }
            catch (Exception ex)
            {
                try
                {
                    _serviceProvider.GetRequiredService<ILogger<App>>()
                        .LogWarning(ex, "Gagal mematikan listener API saat aplikasi keluar.");
                }
                catch { /* proses tetap harus ditutup */ }
            }

            // Server lisensi lokal juga dimatikan DI SINI — sebelum pekerjaan
            // penutupan yang bisa memakan waktu (backup Drive sampai 2 menit) —
            // supaya proses server tidak tertinggal setelah aplikasi ditutup.
            try
            {
                _serviceProvider.GetService<SuDesApp.Services.ServerLisensiLokal>()?.Matikan();
            }
            catch (Exception ex)
            {
                try
                {
                    _serviceProvider.GetRequiredService<ILogger<App>>()
                        .LogWarning(ex, "Gagal mematikan server lisensi lokal saat aplikasi keluar.");
                }
                catch { /* proses tetap harus ditutup */ }
            }

            // Timer diam-diam tidak perlu melanjutkan proses saat aplikasi keluar.
            if (_timerPembersihanDiamDiam != null)
            {
                try { _timerPembersihanDiamDiam.Stop(); _timerPembersihanDiamDiam.Dispose(); } catch { /* timer mungkin sudah berhenti */ }
                _timerPembersihanDiamDiam = null;
            }
            // Timer pemantauan lisensi tidak perlu melanjutkan proses saat keluar.
            if (_timerLisensi != null)
            {
                try { _timerLisensi.Stop(); _timerLisensi.Dispose(); } catch { /* timer mungkin sudah berhenti */ }
                _timerLisensi = null;
            }
            // Denyut sambungan server lisensi cukup sampai di sini juga.
            try { _serviceProvider?.GetService<SuDesApp.Services.DetakSambunganServer>()?.Berhenti(); } catch { /* layanan mungkin belum dibuat */ }
            // Timer deteksi data IDM tahun baru juga cukup sampai di sini.
            try { _serviceProvider?.GetService<Wpf.Services.PemantauIdmBaru>()?.Berhenti(); }
            catch { /* layanan mungkin belum dibuat */ }
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

                    KirimNotifikasiLonceng(n => n.Success(
                        "Backup Otomatis Drive",
                        $"{uploaded.Count} berkas cadangan harian terunggah ke folder {GoogleBackupService.BackupFolderName}."));
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

                    KirimNotifikasiLonceng(n => n.Error(
                        "Backup Otomatis Drive", "Backup harian gagal: " + ex.Message));
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
        /// Nyalakan server lisensi lokal (pusat administrasi di komputer ini)
        /// bila operator mengaktifkannya, lalu sambungkan aplikasi ke alamatnya.
        ///
        /// Dijalankan di thread latar: menyalakan server memerlukan waktu (build
        /// dan start, beberapa detik pada pemakaian pertama) sehingga tidak boleh
        /// menahan tampilan. Sisa proses dari sesi yang mati mendadak dibersihkan
        /// lebih dulu supaya port-nya tidak tetap terpakai — itulah pasangan dari
        /// pembersihan saat aplikasi keluar.
        /// </summary>
        private void MulaiServerLisensiLokal()
        {
            if (!AppPreferenceStore.IsServerLisensiLokalAktif()) return;

            var lisensi = _serviceProvider!.GetRequiredService<SuDesApp.Services.LisensiManager>();
            if (lisensi.UrlServerTerkunci)
            {
                // Rilis yang terkunci pada server produksi tidak boleh diarahkan
                // ke server lokal; server lokalnya pun tidak perlu dinyalakan.
                (_serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("Lisensi"))
                    .LogInformation(
                        "Server lisensi lokal tidak dinyalakan: aplikasi terkunci pada server {Alamat}.",
                        lisensi.UrlServer);
                return;
            }

            _ = Task.Run(async () =>
            {
                var log = _serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger("Lisensi");

                try
                {
                    SuDesApp.Services.ServerLisensiLokal.BersihkanSisa();

                    var lokal = _serviceProvider!.GetRequiredService<SuDesApp.Services.ServerLisensiLokal>();
                    // Kebijakan "nyalakan lalu pakai alamatnya" milik Core: jalur
                    // ini dan tombol di halaman Langganan tidak boleh menyimpang.
                    if (!await lokal.NyalakanDanPakaiAsync(lisensi).ConfigureAwait(false))
                    {
                        log.LogWarning(
                            "Server lisensi lokal tidak bisa dinyalakan: {Alasan}",
                            lokal.GalatTerakhir ?? "-");
                        return;
                    }

                    await _serviceProvider!.GetRequiredService<SuDesApp.Services.DetakSambunganServer>()
                        .CekSekaliAsync().ConfigureAwait(false);
                    await CekLisensiSekaliAsync().ConfigureAwait(false);

                    log.LogInformation("Server lisensi lokal dipakai aplikasi di {Alamat}.", lokal.Alamat);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Server lisensi lokal gagal disiapkan");
                }
            });
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
    }
}
