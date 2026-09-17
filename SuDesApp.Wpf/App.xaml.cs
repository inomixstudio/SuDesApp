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

            // Pembersihan folder PDF sementara sebelum UI tampil — hapus PDF lama (> 7 hari)
            // agar TempPDF tidak menumpuk sepanjang pemakaian aplikasi.
            // Dapat dimatikan lewat Pengaturan Aplikasi.
            if (AppPreferenceStore.IsCleanupTempPdfEnabled())
            {
                TempPdfCleanup.CleanOldFiles(
                    _serviceProvider.GetRequiredService<AppConfig>(),
                    _serviceProvider.GetRequiredService<ILogger<App>>());
            }

            // Rotasi error.log per bulan (arsip > 90 hari dihapus) + hapus file
            // hasil ekspor lama di Output/PDF (> 30 hari) saat aplikasi start.
            // Pembersihan ekspor dapat dimatikan lewat Pengaturan Aplikasi.
            StartupFileCleanup.Run(
                _serviceProvider.GetRequiredService<AppConfig>(),
                _serviceProvider.GetRequiredService<ILogger<App>>(),
                cleanOldExports: AppPreferenceStore.IsCleanupOldExportsEnabled());

            // Terapkan tema tersimpan SEBELUM LoginWindow ditampilkan agar warna form
            // login konsisten dengan tema utama (default: Emerald). Tanpa ini LoginWindow
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

        protected override void OnExit(ExitEventArgs e)
        {
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
                (_serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleBackup"))
                    .LogWarning("Backup Google Drive dibatalkan (melebihi 2 menit).");
            }
            catch (Exception ex)
            {
                // Jangan pernah gagalkan penutupan aplikasi karena backup.
                try
                {
                    (_serviceProvider.GetRequiredService<ILoggerFactory>()
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

            // Repositori Desa (singleton: dipakai MainWindowViewModel singleton dan tidak
            // bergantung SqliteConnection scoped).
            services.AddSingleton<IDesaRepository, DesaRepository>();

            // Query provider + interceptor + handler data surat (untuk SuratRepository).
            services.AddSingleton<QueryProvider>(sp =>
                new QueryProvider(
                    Path.Combine(AppContext.BaseDirectory, "Data", "Queries", "SuratQueries.sql"),
                    sp.GetService<ILogger<QueryProvider>>()));
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
            // NTCR (N1-N4): satu class handler, diregistrasi 4x dengan NamaJenis berbeda
            // agar dispatch eksak di SuratRepository (h.NamaJenis.Equals(...)) cocok.
            services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
                SuratConstants.NTCR_N1,
                sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
                sp.GetRequiredService<ICacheService>()));
            services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
                SuratConstants.NTCR_N2,
                sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
                sp.GetRequiredService<ICacheService>()));
            services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
                SuratConstants.NTCR_N3,
                sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
                sp.GetRequiredService<ICacheService>()));
            services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
                SuratConstants.NTCR_N4,
                sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
                sp.GetRequiredService<ICacheService>()));

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

            // Utilities
            services.AddSingleton<FileService>();
            services.AddSingleton<UpdateService>();
            services.AddScoped<DatabaseImportExportService>();
            services.AddSingleton<GoogleDriveService>();
            // Sheets API memakai token OAuth yang sama dengan Drive (mode layanan
            // online Google Sheet/Form: baca jawaban warga + tulis status balik).
            services.AddSingleton<GoogleSheetsService>();
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
            services.AddTransient<AgendaSuratViewModel>();
            services.AddTransient<RiwayatViewModel>();
            services.AddTransient<KeputusanViewModel>();
            services.AddTransient<DaftarHadirViewModel>();
            services.AddTransient<ExImdbViewModel>();
            services.AddTransient<GoogleLoginViewModel>();
            services.AddTransient<GoogleDriveViewModel>();
            services.AddTransient<PembaruanViewModel>();
            services.AddTransient<CatatanRilisViewModel>();
            services.AddTransient<WaPanelViewModel>();
            services.AddTransient<PanduanWaViewModel>();
            services.AddTransient<UbahSandiViewModel>();
            services.AddTransient<InputWindowViewModel>();
            services.AddTransient<RekeningKoranViewModel>();
            services.AddTransient<InputAgendaViewModel>();
            services.AddTransient<InputKeputusanViewModel>();
            services.AddTransient<InputAgendaWindow>();
            services.AddTransient<InputKeputusanWindow>();

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
            services.AddTransient<Func<string, SuratKeluarMasukData?, InputAgendaWindow>>(sp =>
                (jenis, data) => { var w = sp.GetRequiredService<InputAgendaWindow>(); w.Initialize(jenis, data); return w; });
            services.AddTransient<Func<string, DataKeputusan?, InputKeputusanWindow>>(sp =>
                (jenis, data) => { var w = sp.GetRequiredService<InputKeputusanWindow>(); w.Initialize(jenis, data); return w; });

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