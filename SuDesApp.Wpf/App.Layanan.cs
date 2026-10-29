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
        private static LogLevel BacaLevel(string? nilai, LogLevel bawaan) =>
            Enum.TryParse<LogLevel>(nilai, ignoreCase: true, out var level) && level != LogLevel.None
                ? level
                : bawaan;

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Konfigurasi dibangun SEBELUM logging: level konsol dan log berkas
            // dibaca dari appsettings, bukan ditanam di kode. Sebelumnya bagian
            // AppConfig:Logging hanya dekorasi — tidak ada yang membacanya.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            services.AddSingleton<IConfiguration>(configuration);

            // Log konsol memakai cap waktu: memudahkan menakar halaman/proses mana yang
            // lambat saat menguji performa (selisih antar-baris = durasi tahap itu).
            services.AddLogging(b =>
            {
                var levelSection = configuration.GetSection("AppConfig:Logging:LogLevel");
                LogLevel levelBawaan = BacaLevel(levelSection["Default"], LogLevel.Information);

                b.AddSimpleConsole(o =>
                {
                    o.TimestampFormat = "HH:mm:ss.fff ";
                    o.SingleLine = false;
                });
                b.AddDebug();
                b.SetMinimumLevel(levelBawaan);

                foreach (var filter in levelSection.GetChildren())
                {
                    if (filter.Key.Equals("Default", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Enum.TryParse<LogLevel>(filter.Value, ignoreCase: true, out var level) &&
                        level != LogLevel.None)
                    {
                        b.AddFilter(filter.Key, level);
                    }
                }

                var fileSection = configuration.GetSection("AppConfig:Logging:File");
                if (configuration.GetValue("AppConfig:Logging:File:Enabled", true))
                {
                    string jalur = fileSection["Path"]?.Trim() ?? string.Empty;
                    if (jalur.Length == 0) jalur = "logs/app.log";
                    if (!Path.IsPathRooted(jalur)) jalur = Path.Combine(AppContext.BaseDirectory, jalur);

                    b.AddProvider(new BerkasLogProvider(
                        jalur,
                        BacaLevel(fileSection["MinimumLevel"], LogLevel.Warning),
                        configuration.GetValue("AppConfig:Logging:File:MaxBytes", 2 * 1024 * 1024),
                        configuration.GetValue("AppConfig:Logging:File:MaxFiles", 3)));
                }
            });

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
                    // masih menyebutkannya; sejak alur trial 30 hari, instalasi
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

            // Klien publikasi APBDes pemerintah pusat (prodeskel.binapemdes.kemendagri.go.id).
            // Sendiri, seperti klien IDM: endpoint tetap per desa per tahun, parser
            // ketat di parser sendiri (ApbdesProdeskelBaca) supaya format halaman
            // pusat yang berubah menghasilkan pesan, bukan angka salah.
            services.AddSingleton<SuDesApp.Services.IKlienApbdesPusat>(sp =>
                new SuDesApp.Services.ApbdesProdeskelKlien(
                    new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(25) },
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SuDesApp.Services.ApbdesProdeskelKlien>>()));

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
            services.AddScoped<SuDesApp.Services.IVerifikasiSuratService, SuDesApp.Services.VerifikasiSuratService>();
            // Listener singleton: halaman API menyalakannya, dan pengaturannya
            // dibaca dari preferensi (bukan database) agar bisa dibuka sebelum login.
            services.AddSingleton(sp => new SuDesApp.Api.ApiListener(
                () => sp, sp.GetRequiredService<ILogger<SuDesApp.Api.ApiListener>>()));

            // ==== Server lisensi lokal (pusat administrasi di komputer ini) ====
            // Aplikasi PEMILIK siklus hidupnya: dinyalakan saat aplikasi dibuka
            // bila operator mengaktifkannya di halaman Langganan, dan dimatikan
            // saat aplikasi keluar (termasuk logoff Windows) — supaya tidak ada
            // proses server yang tertinggal setelah aplikasi ditutup.
            services.AddSingleton<SuDesApp.Services.ServerLisensiLokal>();

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
            // Pekerjaan pembaruan di latar belakang: periksa + unduh sendiri, lalu
            // tawarkan "Instal Sekarang" / "Instal Saat Keluar" lewat lonceng.
            // Singleton supaya paket yang sudah terunduh tetap satu pemilik sampai
            // aplikasi ditutup.
            services.AddSingleton<PembaruanLatarService>();
            services.AddScoped<DatabaseImportExportService>();
            services.AddSingleton<GoogleDriveService>();
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

            // Layanan cetak PDF lewat dialog sistem pada library SudesPdf.
            services.AddSingleton<PdfPrintService>();

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
                previewFactory: sp.GetRequiredService<Func<string, string, PdfPreviewViewModel>>(),
                appConfig: sp.GetRequiredService<AppConfig>(),
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
            // JANGAN mendaftarkan IdmViewModel lagi tanpa factory di sini:
            // pendaftaran terakhir yang dipakai DI, sehingga factory di atas
            // (navigasi + pratinjau PDF + pembaca dokumentasi) akan terabaikan
            // dan tombol Cetak/PDF halaman IDM kehilangan pratinjaunya.

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
