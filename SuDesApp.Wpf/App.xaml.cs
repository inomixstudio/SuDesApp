using System;
using System.IO;
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

            _serviceProvider = ConfigureServices();

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
            try
            {
                var appConfig = _serviceProvider.GetRequiredService<AppConfig>();
                appConfig.DatabaseConnectionString = EnkripsiDatabase.JaminTerkunci(
                    appConfig.DatabaseConnectionString,
                    _serviceProvider.GetRequiredService<ILogger<App>>());
            }
            catch (Exception ex)
            {
                _serviceProvider.GetRequiredService<ILogger<App>>()
                    .LogError(ex, "Gagal menyiapkan enkripsi database");
                try
                {
                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Database", ex.Message,
                        SuDesApp.Utilities.AppMessageButton.Ok,
                        SuDesApp.Utilities.AppMessageIcon.Error);
                }
                catch
                {
                    MessageBox.Show(ex.Message, "Database",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Shutdown();
                return;
            }

            // ==== Inisialisasi & migrasi database ====
            // Menjalankan skema desa.db.sql, menyemai daftar jenis surat dari
            // JenisSuratConfig.json (termasuk NTCR N1–N6 + N8), membuat tabel NTCR,
            // dan menjalankan migrasi kolom untuk database lama — semuanya idempoten.
            // Tanpa ini blanko NTCR tidak punya baris jenis surat sehingga nomor
            // suratnya tidak pernah terisi dan penyimpanannya gagal.
            try
            {
                using var scopeInisialisasi = _serviceProvider.CreateScope();
                await scopeInisialisasi.ServiceProvider
                    .GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync();
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
            _serviceProvider.GetRequiredService<ThemeService>();

            var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
            var loginResult = loginWindow.ShowDialog();
            if (loginResult != true) { Shutdown(); return; }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();

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

            // Pembersihan draft kedaluwarsa (> 30 hari): cek latar belakang setelah
            // MainWindow tampil — bila ada, tampilkan KONFIRMASI sebelum menghapus.
            _ = StaleDraftCleanup.RunAfterStartupAsync(
                _serviceProvider,
                _serviceProvider.GetRequiredService<ILogger<App>>());
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
                JalankanPembersihanStartup(logger, config);
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
            // Timer diam-diam tidak perlu melanjutkan proses saat aplikasi keluar.
            if (_timerPembersihanDiamDiam != null)
            {
                try { _timerPembersihanDiamDiam.Stop(); _timerPembersihanDiamDiam.Dispose(); } catch { }
                _timerPembersihanDiamDiam = null;
            }
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

        private void InitializeDatabase()
        {
            // Database initialization skipped for minimal WPF migration
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
                () => null)); // app secret opsional — bisa diisi bila ingin verifikasi tanda tangan
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
            services.AddSingleton<NavigationService>(sp => new NavigationService(sp));
        // ==== ViewModel & Windows (transient, di-resolve dalam scope navigasi) ====
            services.AddTransient<InputControlFactory>();
            services.AddTransient<SetelanViewModel>();
            services.AddTransient<PengaturanAplikasiViewModel>();
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
                    sp.GetRequiredService<NavigationService>(),
                    kembali,
                    sp.GetRequiredService<ILogger<DokumenBacaViewModel>>()));

            // Akun & arsip tahunan: halaman area konten utama (bukan jendela modal).
            services.AddTransient<KelolaPenggunaViewModel>();
            services.AddTransient<TutupBukuTahunViewModel>();
            services.AddTransient<VerifikasiSuratViewModel>();

            // Template Surat: jenis surat buatan pengguna sendiri.
            services.AddScoped<ITemplateSuratRepository, TemplateSuratRepository>();
            services.AddTransient<TemplateSuratGenerator>();
            // Contoh template siap pakai (pemasangan otomatis saat daftar masih kosong).
            services.AddTransient<SuDesApp.Services.TemplateSuratBawaanService>();
            // Pembacaan berkas Word (.docx) untuk membuat template surat dari berkas
            // yang sudah dipakai (bisa satu atau beberapa bentuk surat sekaligus).
            services.AddSingleton<SuDesApp.Services.ITemplateSuratWordImpor,
                SuDesApp.Services.TemplateSuratWordImporService>();
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
            services.AddTransient<DomisiliWargaInputViewModel>();
            services.AddTransient<DomisiliInstansiInputViewModel>();
            services.AddTransient<BedaNamaInputViewModel>();
            services.AddTransient<IzinOrtuInputViewModel>();
            services.AddTransient<IjinTinggalInputViewModel>();
            services.AddTransient<GarapanInputViewModel>();
            services.AddTransient<AhliWarisInputViewModel>();
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
            services.AddTransient<KematianGenerator>();
            services.AddTransient<BedaNamaGenerator>();
            services.AddTransient<DomisiliWargaGenerator>();
            services.AddTransient<DomisiliInstansiGenerator>();
            services.AddTransient<GarapanGenerator>();
            services.AddTransient<IzinOrtuGenerator>();
            services.AddTransient<KenalLahirGenerator>();
            services.AddTransient<IjinTinggalGenerator>();
            services.AddTransient<AhliWarisGenerator>();
            services.AddTransient<NtcrGenerator>();
            services.AddTransient<SuratRegisterGenerator>();
            services.AddTransient<RekeningKoranGenerator>();
            services.AddTransient<DaftarHadirGenerator>();
            // Generator SK Bupati: tidak mewarisi SuratGeneratorBase, jadi ia
            // mendaftarkan font "Times New Roman" sendiri lewat konstruktor
            // (lihat SkPerangkatGenerator) supaya PDF tetap tercetak walau SK
            // ini dokumen pertama pada sebuah sesi.
            services.AddTransient<SuDesApp.GeneratorPdf.SkPerangkatGenerator>();

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
                (title, path) => new PdfPreviewViewModel(title, path, sp.GetRequiredService<NavigationService>(),
                    messageService: sp.GetRequiredService<SuDesApp.Utilities.IMessageService>(),
                    peringatan: sp.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>()));
            // Factory pratinjau surat (alur Buat/Edit Surat): menyertakan ID surat + factory form edit
            // agar tombol ✏️ Edit tampil di pratinjau.
            services.AddTransient<Func<string, string, int?, PdfPreviewViewModel>>(sp =>
                (title, path, suratId) => new PdfPreviewViewModel(
                    title, path, sp.GetRequiredService<NavigationService>(),
                    suratId, sp.GetRequiredService<Func<int, InputWindowViewModel>>(),
                    sp.GetRequiredService<SuDesApp.Utilities.IMessageService>(),
                    peringatan: sp.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>()));
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
                    peringatan: sp.GetRequiredService<SuDesApp.Utilities.IPeringatanDataDesaContoh>()));

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
