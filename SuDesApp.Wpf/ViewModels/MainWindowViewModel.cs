using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILogger<MainWindowViewModel> _logger;

        private readonly FormulirMenuService _formulirMenuService;
        private readonly ThemeService _themeService;
        private readonly FeatureNavigationViewModel _featureNavigation;

        // ---------- Masa hidup IServiceScope ----------
        //
        // Halaman-halaman di bawah dibuat dari DI dengan CreateScope(). Setiap
        // scope memiliki SqliteConnection (AddScoped), jadi scope yang dibuang
        // menahan handle SQLite native SECARA PERMANEN dari connection pool.
        // Sebelumnya ada 36 panggilan CreateScope() tanpa using: dua puluh sesi
        // navigasi sudah cukup untuk menghabiskan pool, dan gejalanya
        // (SQLITE_CANTOPEN, "database is locked") muncul jauh dari sumbernya.
        // Memindahkan handle ke helper saja hanya menutup gejala, bukan sebab:
        // yang penting scope ditutup pada saat yang benar.
        //
        // Ada dua jenis scope karena ada dua jenis masa hidup:
        //  1. Scope halaman  - untuk ViewModel yang dibangun ulang tiap navigasi.
        //     Ditutup tepat sebelum halaman berikutnya dibuka.
        //  2. Scope bertahan - untuk ViewModel berumur sesi (_daftarHadirViewModel,
        //     _formulirViewModel) yang sengaja disimpan agar tidak dibangun ulang
        //     tiap kali menu dibuka. Ditutup saat aplikasi ditutup lewat Dispose().
        // Masa hidup scope halaman dimiliki NavigationService — satu-satunya
        // tempat scope boleh tertukar, yaitu saat Navigate dipanggil. VM ini cukup
        // memintanya lewat BuatScopeHalaman()/BuatScopeBertahan(); tidak ada lagi
        // slot global yang bisa menutup halaman di luar jalur navigasi.
        // Penutupan akhir terjadi di App.OnExit (container membuang NavigationService).

        /// <summary>
        /// Bersihkan sumber daya VM ini. Dipanggil MainWindow saat jendela ditutup.
        /// Scope halaman tidak ikut di sini: pemiliknya NavigationService, dibuang
        /// container di App.OnExit bersama service provider.
        /// </summary>
        public void Dispose()
        {
            // Tidak ada sumber daya milik VM ini yang perlu dibersihkan: scope
            // halaman dan scope bertahan dimiliki NavigationService.
        }

        private SuDesApp.Services.PenjagaLisensi? _penjaga;
        private readonly SuDesApp.Services.LisensiManager? _lisensi;

        /// <summary>Keadaan sambungan server lisensi bersama (dipakai baris menu Pusat Administrasi di sidebar).</summary>
        private readonly SuDesApp.Services.SambunganServerLisensi? _sambunganServer;

        public MainWindowViewModel(
            NavigationService navigation,
            IMessageService messageService,
            IServiceScopeFactory scopeFactory,
            ILogger<MainWindowViewModel> logger,
            SettingsManager settingsManager,
            FormulirMenuService formulirMenuService,
            ThemeService themeService,
            NotificationService notifications,
            PembaruanLatarService pembaruanLatar,
            IPemberitahuanSambutanStore? sambutanStore = null,
            SuDesApp.Services.PenjagaLisensi? penjaga = null,
            SuDesApp.Services.LisensiManager? lisensi = null,
            SuDesApp.Services.SambunganServerLisensi? sambunganServer = null,
            SuDesApp.Services.IIdmSimpananStore? idmSimpanan = null,
            SuDesApp.Wilayah.WilayahDataStore? wilayah = null)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _formulirMenuService = formulirMenuService ?? throw new ArgumentNullException(nameof(formulirMenuService));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _featureNavigation = new FeatureNavigationViewModel(
                ResolveFeatureRoute, _formulirMenuService, _themeService, _messageService);
            _featureNavigation.ActivePageChanged += title =>
            {
                TerapkanJudulHalaman();
            };
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _pembaruanLatar = pembaruanLatar ?? throw new ArgumentNullException(nameof(pembaruanLatar));
            _pembaruanLatar.PembaruanDitemukan += (_, _) => TandaiMenuPembaruan();
            _sambutanStore = sambutanStore ?? new PemberitahuanSambutanStore();
            _penjaga = penjaga;
            _lisensi = lisensi;
            _sambunganServer = sambunganServer;
            _idmSimpanan = idmSimpanan;
            _wilayah = wilayah;

            // Baris menu Pusat Administrasi menyebut server yang dituju DAN
            // keadaan sambungannya sejak menu dibangun — bukan hanya setelah
            // halamannya dimuat — lalu ikut berubah: bila alamat diganti di halaman
            // Langganan, dan bila detak lisensi berkala menemukan server tidak bisa
            // dihubungi. Operator bisa membedakan server uji (mis. localhost:5000)
            // dari produksi, dan tahu apakah servernya hidup, sebelum menekan menu
            // yang membawa token adminnya.
            if (lisensi != null)
            {
                void SegarkanBarisPusatAdministrasi()
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    void Terapkan() =>
                        _featureNavigation.PerbaruiBarisPusatAdministrasi(
                            lisensi.AlamatPusatAdministrasi(),
                            _sambunganServer?.Sekarang ?? SuDesApp.Services.KeadaanSambunganServer.BelumDiuji);
                    if (dispatcher is null || dispatcher.CheckAccess()) Terapkan();
                    else dispatcher.BeginInvoke(new Action(Terapkan));
                }

                lisensi.AlamatBerubah += SegarkanBarisPusatAdministrasi;
                _sambunganServer?.Berubah += SegarkanBarisPusatAdministrasi;
                SegarkanBarisPusatAdministrasi();
            }

            // Chip status langganan di statusbar (Full Service / Demo / Baca-saja)
            // bisa diklik → halaman Langganan langsung.
            LanggananChipClickCommand = new AsyncRelayCommand(ShowLanggananAsync);
            if (_penjaga != null)
            {
                _penjaga.Berubah += () =>
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    void Segarkan() => TerapkanChipLisensi(_penjaga);
                    if (dispatcher is null || dispatcher.CheckAccess()) Segarkan();
                    else dispatcher.BeginInvoke(new Action(Segarkan));
                };
                TerapkanChipLisensi(_penjaga);
            }

            // Setelah pengaturan desa disimpan, segarkan info desa di status bar
            // (padanan pemanggilan GetInfoDesaAsync setelah SetelanForm ditutup pada WinForms).
            SetelanViewModel.SettingsSaved += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                void Segarkan()
                {
                    RefreshVillageInfo();
                    // Kode desa bisa berubah → status IDM di status bar disandingkan
                    // ulang dengan cadangan yang sesuai (bila ada).
                    _ = SegarkanStatusIdmAsync();
                    _ = PeriksaDataDesaSetelahSimpanAsync();
                }

                if (dispatcher is null || dispatcher.CheckAccess())
                {
                    Segarkan();
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(Segarkan));
                }
            };

            // Status IDM di status bar (teks biasa di samping info desa): halaman
            // IDM menampilkan data → baris ini ikut diperbarui tanpa polling.
            IdmViewModel.DataDiperbarui += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                void Segarkan() => SegarkanStatusIdm();
                if (dispatcher is null || dispatcher.CheckAccess()) Segarkan();
                else dispatcher.BeginInvoke(new Action(Segarkan));
            };

            _formulirMenuService.TemplatesChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    _featureNavigation.RefreshFormulirSection();
                    return;
                }
                if (dispatcher.CheckAccess())
                {
                    _featureNavigation.RefreshFormulirSection();
                }
                else
                {
                    dispatcher.BeginInvoke(_featureNavigation.RefreshFormulirSection);
                }
            };

            _navigation.CurrentViewChanged += view =>
            {
                CurrentView = view;
                PantauJudulHalaman(view);
            };
            ShowRegisterCommand = new AsyncRelayCommand(ShowRegisterSuratAsync);
            ShowSettingsCommand = new AsyncRelayCommand(ShowSettingsAsync);
            ShowFormulirCommand = new AsyncRelayCommand(ShowFormulirAsync);
            ShowAboutCommand = new AsyncRelayCommand(ShowAboutAsync);
            ShowCatatanRilisCommand = new AsyncRelayCommand(ShowCatatanRilisAsync);

            _ = RefreshGoogleBadgeAsync();
            _ = LoadVillageInfoAsync();
            // Status IDM dari cadangan terakhir (bila desa ini pernah memuat data).
            _ = SegarkanStatusIdmAsync();

            // Pembaruan: laporkan hasil tambalan yang baru dipasang (bila ada), lalu
            // periksa versi terbaru di latar belakang. Pemeriksaan ini sengaja tidak
            // memblokir aplikasi dan langsung memberi tahu APA yang diperbaiki.
            _ = LaporkanHasilTambalanAsync();
            _ = PeriksaPembaruanLatarAsync();

            // Notifikasi sambutan cukup SEKALI seumur pemasangan — sekaligus menandakan
            // lonceng notifikasi aktif di status bar.
            KirimPemberitahuanSambutan(_sambutanStore, _notifications, LoginViewModel.CurrentUserName);
        }

        /// <summary>Pengatur tema aktif — dipakai jendela untuk menggambar ulang ikonnya
        /// saat tema berubah (ubin aksen mengikuti menu sidebar).</summary>
        public ThemeService TemaService => _themeService;
    }
}
