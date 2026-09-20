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
                        SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                            "Error", $"Error tidak tertangani:\n{args.Exception.Message}",
                            SuDesApp.Utilities.AppMessageButton.Ok, SuDesApp.Utilities.AppMessageIcon.Error);
                    }
                    catch
                    {
                        MessageBox.Show($"Error tidak tertangani:\n{args.Exception.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch { }
                args.Handled = true;
            };

            _serviceProvider = ConfigureServices();

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
            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            var config = _serviceProvider.GetRequiredService<AppConfig>();

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
                var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("GoogleBackup");

                if (!_serviceProvider.GetRequiredService<GoogleDriveService>().IsOAuthEnabled)
                {
                    logger.LogInformation("Backup Drive dilewati: OAuth belum dikonfigurasi.");
                    return;
                }
                if (!_serviceProvider.GetRequiredService<GoogleDriveService>().HasStoredToken())
                {
                    logger.LogInformation("Backup Drive dilewati: belum ada akun Google yang terhubung.");
                    return;
                }
                if (_serviceProvider.GetRequiredService<GoogleBackupService>().IsBackupDoneToday())
                {
                    logger.LogInformation("Backup Drive dilewati: sudah dilakukan hari ini.");
                    return;
                }

                logger.LogInformation("Memulai backup harian database ke Google Drive...");
                var backup = _serviceProvider.GetRequiredService<GoogleBackupService>();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                // Jalankan di THREAD POOL (bukan UI thread) agar tidak ada
                // penangkapan DispatcherSynchronizationContext — menghindari
                // deadlock penutupan aplikasi (sinkron .GetResult() di UI thread
                // + awaite tanpa ConfigureAwait(false) = proses hang selamanya).
                var backupTask = Task.Run(
                    () => _serviceProvider.GetRequiredService<GoogleBackupService>().BackupNowAsync(cts.Token),
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
                (_serviceProvider.GetRequiredService<ILoggerFactory>()!
                    .CreateLogger("GoogleBackup"))
                    .LogWarning("Backup Google Drive dibatalkan (melebihi 2 menit).");
            }
            catch (Exception ex)
            {
                // Jangan pernah gagalkan penutupan aplikasi karena backup.
                try
                {
                    (_serviceProvider.GetRequiredService<ILoggerFactory>()!
                        .CreateLogger("GoogleBackup"))
                        .LogError(ex, "Backup Google Drive gagal saat aplikasi ditutup");

                    App.Current?.Dispatcher.Invoke(() =>
                        _serviceProvider?.GetRequiredService<NotificationService>()
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
            services.AddLogging(b => { b.AddConsole(); b.AddDebug(); b.SetMinimumLevel(LogLevel.Information); });

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
            services.AddSingleton<IWordFileReader, WordFileReaderService>();

            // Manager & utilitas
            services.AddSingleton<SettingsManager>();
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
            services.AddTransient<PanduanWaViewModel>();
            services.AddTransient<UbahSandiViewModel>();
            services.AddTransient<InputWindowViewModel>();
            services.AddTransient<RekeningKoranViewModel>();
            services.AddTransient<InputAgendaViewModel>();
            services.AddTransient<InputKeputusanViewModel>();

            // Template Surat: jenis surat buatan pengguna sendiri.
            services.AddScoped<ITemplateSuratRepository, TemplateSuratRepository>();
            services.AddTransient<TemplateSuratGenerator>();
            // Contoh template siap pakai (pemasangan otomatis saat daftar masih kosong).
            services.AddTransient<SuDesApp.Services.TemplateSuratBawaanService>();
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
                (title, path) => new PdfPreviewViewModel(title, path, sp.GetRequiredService<NavigationService>()));
            // Factory pratinjau surat (alur Buat/Edit Surat): menyertakan ID surat + factory form edit
            // agar tombol ✏️ Edit tampil di pratinjau.
            services.AddTransient<Func<string, string, int?, PdfPreviewViewModel>>(sp =>
                (title, path, suratId) => new PdfPreviewViewModel(
                    title, path, sp.GetRequiredService<NavigationService>(),
                    suratId, sp.GetRequiredService<Func<int, InputWindowViewModel>>(),
                    sp.GetRequiredService<SuDesApp.Utilities.IMessageService>()));
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
                    batalKembali: batalKembali));

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