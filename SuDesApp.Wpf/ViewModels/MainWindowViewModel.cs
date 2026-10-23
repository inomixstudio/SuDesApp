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
using SuDesApp.WhatsApp;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Cache instance halaman yang sering dibuka (Pengaturan Aplikasi, Pengaturan
        /// Surat, Catatan Rilis). ViewModel-nya tetap dibuat baru setiap dibuka (agar
        /// nilainya segar), tetapi pohon visual halaman tidak dibangun ulang dari nol —
        /// yang membuat perpindahan menu terasa ringan.
        /// </summary>
        private readonly Dictionary<Type, UserControl> _cacheHalaman = new();
        private readonly ILogger<MainWindowViewModel> _logger;

        /// <summary>Cadangan data IDM (untuk status IDM di status bar); null bila tidak disediakan.</summary>
        private readonly SuDesApp.Services.IIdmSimpananStore? _idmSimpanan;

        /// <summary>Data wilayah (penentu kode desa) untuk mencocokkan cadangan IDM; null bila tidak disediakan.</summary>
        private readonly SuDesApp.Wilayah.WilayahDataStore? _wilayah;
        private readonly SettingsManager _settingsManager;
        private readonly FormulirMenuService _formulirMenuService;
        private readonly WaEngine _waEngine;
        private readonly ThemeService _themeService;
        private readonly NotificationService _notifications;
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
        //     _formulirViewModel, _panduanWaViewModel) yang sengaja disimpan agar
        //     tidak dibangun ulang tiap kali menu dibuka. Ditutup saat aplikasi
        //     ditutup lewat Dispose().
        // Masa hidup scope halaman dimiliki NavigationService — satu-satunya
        // tempat scope boleh tertukar, yaitu saat Navigate dipanggil. VM ini cukup
        // memintanya lewat BuatScopeHalaman()/BuatScopeBertahan(); tidak ada lagi
        // slot global yang bisa menutup halaman di luar jalur navigasi.
        // Penutupan akhir terjadi di App.OnExit (container membuang NavigationService).
        private System.Windows.Threading.DispatcherTimer? _waStatusTimer;

        private void OnWaStatusTick(object? sender, System.EventArgs e)
        {
            RefreshWaGatewayStatus();
            RefreshWaSheetStatus();
        }

        private IServiceProvider BuatScopeHalaman() => _navigation.BukaHalaman();

        private IServiceProvider BuatScopeBertahan() => _navigation.BukaBertahan();

        /// <summary>
        /// Bersihkan sumber daya VM ini. Dipanggil MainWindow saat jendela ditutup.
        /// Scope halaman tidak ikut di sini: pemiliknya NavigationService, dibuang
        /// container di App.OnExit bersama service provider.
        /// </summary>
        public void Dispose()
        {
            if (_waStatusTimer is not null)
            {
                _waStatusTimer.Stop();
                _waStatusTimer.Tick -= OnWaStatusTick;
                _waStatusTimer = null;
            }
        }

        /// <summary>Judul tetap notifikasi peringatan data desa contoh (dipakai untuk mencarinya lagi).</summary>
        private const string JudulNotifDataDesaContoh = "Data desa masih contoh — ganti dengan data Anda";

        /// <summary>Peringatan data desa contoh sudah dikirim pada sesi ini.</summary>
        private bool _sudahIngatkanDataDesaContoh;
        private readonly WaAutoProcessor _waAutoProcessor;

        /// <summary>Penanda pemberitahuan sambutan (cukup sekali seumur pemasangan).</summary>
        private readonly IPemberitahuanSambutanStore _sambutanStore;
        private SuDesApp.Services.PenjagaLisensi? _penjaga;
        private readonly SuDesApp.Services.LisensiManager? _lisensi;

        /// <summary>Keadaan sambungan server lisensi bersama (dipakai baris menu Pusat Administrasi di sidebar).</summary>
        private readonly SuDesApp.Services.SambunganServerLisensi? _sambunganServer;
        private DaftarHadirViewModel? _daftarHadirViewModel;
        private FormulirViewModel? _formulirViewModel;
        private PanduanWaViewModel? _panduanWaViewModel;
        private object? _panduanWaView; // cache view juga untuk menghindari rebuild XAML
        private object? _currentView;
        private string _villageInfo = "DESA ... KECAMATAN ... KABUPATEN ...";
        private string _userName = "Operator";

        public MainWindowViewModel(
            NavigationService navigation,
            IMessageService messageService,
            IServiceScopeFactory scopeFactory,
            ILogger<MainWindowViewModel> logger,
            SettingsManager settingsManager,
            FormulirMenuService formulirMenuService,
            WaEngine waEngine,
            ThemeService themeService,
            NotificationService notifications,
            WaAutoProcessor waAutoProcessor,
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
            _waEngine = waEngine ?? throw new ArgumentNullException(nameof(waEngine));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _featureNavigation = new FeatureNavigationViewModel(
                ResolveFeatureRoute, _formulirMenuService, _themeService, _messageService);
            _featureNavigation.ActivePageChanged += title =>
            {
                TerapkanJudulHalaman();
            };
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _waAutoProcessor = waAutoProcessor ?? throw new ArgumentNullException(nameof(waAutoProcessor));
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

            // Chip status di statusbar bisa diklik → Pengaturan Aplikasi langsung
            // menggulir ke kartu terkait (Gateway WhatsApp / Google Sheet).
            WaChipClickCommand = new AsyncRelayCommand(() =>
                OpenSettingsFocusAsync(SuDesApp.Wpf.Views.PengaturanAplikasiView.SectionGatewayWa));
            SheetChipClickCommand = new AsyncRelayCommand(() =>
                OpenSettingsFocusAsync(SuDesApp.Wpf.Views.PengaturanAplikasiView.SectionGoogleSheet));

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
            _waEngine.RequestCreated += _waAutoProcessor.HandleRequestCreated;
            _waEngine.RequestCreated += OnWaRequestCreated;

            // Jam layanan: engine bertanya status buka lewat query ini dan
            // auto-processor mengejar permintaan tertunda saat jam layanan dibuka.
            _waEngine.ServiceHoursOpenQuery = () =>
                WaServiceHours.IsOpen(DateTime.Now,
                    AppPreferenceStore.GetWaServiceOpen(), AppPreferenceStore.GetWaServiceClose(),
                    AppPreferenceStore.GetWaServiceDays());
            _waEngine.ServiceHoursOpened += _waAutoProcessor.HandleServiceHoursOpened;

            // Permintaan warga yang tadi DITAHAN karena data desa masih contoh dibuka
            // kembali tepat saat data desa disimpan — bukan cuma dikabarkan ke operator,
            // tapi diproses ulang sendiri sampai suratnya terkirim. Dipasang di
            // SettingsManager (sumber penyimpanan data desa), bukan di satu halaman,
            // supaya penyimpan data desa mana pun ikut membuka penahanannya.
            _settingsManager.PengaturanDesaTersimpan += (_, _) =>
                _ = _waAutoProcessor.LanjutkanPermintaanTertahanAsync();

            // Status gateway WA di statusbar: cek awal + polling ringan tiap
            // 10 detik agar perubahan preferensi (ganti gateway/isi token)
            // terpantau tanpa perlu mekanisme event antar halaman.
            RefreshWaGatewayStatus();
            RefreshWaSheetStatus();
            // Timer disimpan sebagai field, bukan variabel lokal. Sebelumnya ia lokal,
            // jadi tidak ada satu pun kode yang bisa menjentikannya: hanya
            // reference Dispatcher yang menahannya tetap hidup. Akibatnya timer
            // berdetak tiap 10 detik sepanjang proses - termasuk setelah jendela
            // ditutup - menulis status ke ViewModel yang tidak terlihat lagi.
            // Kalau alur keluar-masuk pernah membangun ulang ViewModel ini,
            // jumlah timer akan berlipat tiap kali.
            _waStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _waStatusTimer.Tick += OnWaStatusTick;
            _waStatusTimer.Start();
            _waEngine.StartPolling();

            // Penahanan dari sesi sebelumnya (aplikasi ditutup sebelum data desa diisi,
            // atau data desa dilengkapi lewat pemulihan database) ikut dilepas saat
            // aplikasi dibuka — sekali saja, di latar belakang.
            _ = _waAutoProcessor.LanjutkanPermintaanTertahanAsync();

            _ = RefreshWaBadgeAsync();
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

        /// <summary>
        /// Kirim pemberitahuan sambutan bila belum pernah ditampilkan. Penandanya ditulis
        /// saat notifikasi dikirim, sehingga membuka aplikasi berikutnya — atau setelah
        /// daftar notifikasi dibersihkan pengguna — tidak memunculkannya kembali.
        ///
        /// Terbuka untuk uji supaya aturannya bisa diperiksa tanpa membangun seluruh
        /// jendela utama (yang butuh layanan WhatsApp, tema, dan database).
        /// </summary>
        /// <returns>True bila notifikasi sambutan benar-benar dikirim.</returns>
        public static bool KirimPemberitahuanSambutan(
            IPemberitahuanSambutanStore store, NotificationService notifikasi, string namaPengguna)
        {
            if (store is null || notifikasi is null || !store.AmbilSekali())
            {
                return false;
            }

            notifikasi.Info(
                "Notifikasi aktif",
                $"Selamat datang, {namaPengguna}. Pemberitahuan aplikasi akan muncul di sini.");

            return true;
        }

        public ObservableCollection<NavItem> MenuItems => _featureNavigation.MenuItems;

        /// <summary>Pusat notifikasi lonceng status bar (ala Visual Studio).</summary>
        public NotificationService Notifications => _notifications;

        /// <summary>
        /// Layanan pemeriksaan rilis — dipakai pemasangan pembaruan otomatis saat
        /// aplikasi ditutup (lihat MainWindow.SiapkanPembaruanOtomatisSaatKeluarAsync).
        /// Null aman: fitur otomatis cukup dilewati bila layanan tidak tersedia.
        /// </summary>
        public UpdateService? PembaruanUpdateService
        {
            get
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetService<UpdateService>();
                }
                catch { return null; }
            }
        }

        /// <summary>Layanan tambalan untuk pemasangan otomatis saat ditutup. Null aman.</summary>
        public PatchUpdateService? PembaruanPatchService
        {
            get
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    return scope.ServiceProvider.GetService<PatchUpdateService>();
                }
                catch { return null; }
            }
        }

        public object? CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        public string VillageInfo
        {
            get => _villageInfo;
            set => SetProperty(ref _villageInfo, value);
        }

        private string _screenInfo = string.Empty;

        /// <summary>Ringkasan resolusi layar untuk chip status bar (diisi ScreenResolutionService saat startup).</summary>
        public string ScreenInfo
        {
            get => _screenInfo;
            set => SetProperty(ref _screenInfo, value);
        }

        // ==== Status IDM di status bar (pojok kiri, di samping info desa) ====

        private string _statusIdmTampil = StatusIdmBar.BelumAdaData;

        /// <summary>
        /// Status IDM desa di status bar — teks BIASA (bukan chip, permintaan
        /// tampilan): "Status Desa (IDM): Maju  •  0,7444 (2024)". Sumbernya
        /// cadangan halaman IDM (hasil terakhir yang berhasil diambil dari
        /// idm.kemendesa.go.id untuk desa pada Pengaturan Surat); belum pernah
        /// ada → "Status Desa (IDM): belum ada data".
        /// </summary>
        public string StatusIdmTampil
        {
            get => _statusIdmTampil;
            private set => SetProperty(ref _statusIdmTampil, value);
        }

        /// <summary>Baca ulang status IDM dari cadangan (startup, simpan pengaturan, halaman IDM memuat data).</summary>
        private void SegarkanStatusIdm() => _ = SegarkanStatusIdmAsync();

        internal async Task SegarkanStatusIdmAsync()
        {
            try
            {
                if (_idmSimpanan == null || _wilayah == null)
                {
                    StatusIdmTampil = StatusIdmBar.BelumAdaData;
                    if (_featureNavigation.IndeksDesa != null) _featureNavigation.IndeksDesa.StatusText = "Data IDM belum dimuat";
                    return;
                }

                string kode = await IdmDesaAktif.KodeAsync(_settingsManager, _wilayah);
                var cadangan = _idmSimpanan.Muat();
                StatusIdmTampil = StatusIdmBar.Susun(cadangan, kode);
                if (_featureNavigation.IndeksDesa != null)
                {
                    _featureNavigation.IndeksDesa.StatusText = SusunRingkasanIdmSidebar(cadangan, kode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyusun status IDM untuk status bar.");
            }
        }

        private static string SusunRingkasanIdmSidebar(SuDesApp.Services.IdmSimpanan? cadangan, string kodeDesa)
        {
            if (cadangan == null
                || cadangan.Hasil is not { Count: > 0 }
                || string.IsNullOrWhiteSpace(kodeDesa)
                || !string.Equals(cadangan.KodeDesa, kodeDesa, StringComparison.Ordinal)
                || cadangan.WaktuMuat is not DateTime waktuMuat)
            {
                return "Data IDM belum dimuat";
            }

            int tahun = cadangan.Hasil.Max(hasil => hasil.Ringkasan.Tahun);
            string tanggal = waktuMuat.ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
            return $"Data {tahun} · Diperbarui {tanggal}";
        }

        /// <summary>
        /// Cocokkan cadangan IDM dengan kode desa pada Pengaturan Surat (lewat data
        /// wilayah, sama seperti halaman IDM), lalu susun teksnya. Tanpa cadangan,
        /// kode tidak cocok, atau desa belum dipilih → "Status Desa (IDM): belum ada data".
        /// </summary>
        internal async Task<string> HitungStatusIdmAsync()
        {
            string kosong = StatusIdmBar.BelumAdaData;
            if (_idmSimpanan == null || _wilayah == null) return kosong;

            string kode = await IdmDesaAktif.KodeAsync(_settingsManager, _wilayah);
            if (kode.Length == 0) return kosong;

            return StatusIdmBar.Susun(_idmSimpanan.Muat(), kode);
        }

        // ==== Indikator gateway WhatsApp di statusbar ====

        private string _waGatewayStatus = "WA: …";
        private string _waGatewayStatusTooltip = "Memeriksa gateway WhatsApp…";

        /// <summary>Teks singkat status gateway, mis. "WA: Terhubung".</summary>
        public string WaGatewayStatus
        {
            get => _waGatewayStatus;
            private set => SetProperty(ref _waGatewayStatus, value);
        }

        public string WaGatewayStatusTooltip
        {
            get => _waGatewayStatusTooltip;
            private set => SetProperty(ref _waGatewayStatusTooltip, value);
        }

        /// <summary>
        /// Hitung status gateway: token/Phone ID kosong → "Token belum diisi";
        /// lengkap → "Terhubung". (Gateway tunggal: WhatsApp Cloud API.)
        /// </summary>
        public void RefreshWaGatewayStatus()
        {
            try
            {
                var token = AppPreferenceStore.GetWaCloudApiToken();
                var phoneId = AppPreferenceStore.GetWaCloudApiPhoneId();
                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(phoneId))
                {
                    WaGatewayStatus = "WA: Token belum diisi";
                    WaGatewayStatusTooltip = "Gateway WhatsApp Cloud API aktif tetapi access token / Phone Number ID masih kosong.\nIsi di Pengaturan → Pengaturan Aplikasi → Gateway WhatsApp.";
                }
                else
                {
                    WaGatewayStatus = "WA: Terhubung (Cloud API)";
                    WaGatewayStatusTooltip = "Gateway WhatsApp Cloud API (Meta, resmi) aktif — pesan keluar berupa teks/tautan via Graph API.\nStatus token dapat diverifikasi via tombol Uji koneksi di Pengaturan.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memperbarui status gateway WA");
            }
        }

        // ==== Indikator kesehatan mode tautan Google Sheet di statusbar ====

        private string _waSheetStatus = "Sheet: …";
        private string _waSheetStatusTooltip = "Memeriksa mode tautan Google Sheet…";
        private bool _waSheetChipVisible;
        private bool _sheetApiCheckRunning;
        private string? _sheetApiCheckedKey;

        /// <summary>Teks singkat kesehatan mode tautan Sheet, mis. "Sheet: Siap".</summary>
        public string WaSheetStatus
        {
            get => _waSheetStatus;
            private set => SetProperty(ref _waSheetStatus, value);
        }

        public string WaSheetStatusTooltip
        {
            get => _waSheetStatusTooltip;
            private set => SetProperty(ref _waSheetStatusTooltip, value);
        }

        /// <summary>Chip hanya tampil saat mode tautan aktif.</summary>
        public bool WaSheetChipVisible
        {
            get => _waSheetChipVisible;
            private set => SetProperty(ref _waSheetChipVisible, value);
        }

        /// <summary>
        /// Peringatan proaktif mode tautan (Google Form/Sheet): URL belum diisi,
        /// akun Google belum terhubung, atau Google Sheets API belum diaktifkan —
        /// masing-masing dengan arah perbaikan di tooltip. Pemeriksaan jaringan
        /// (uji baca Sheet) hanya dijalankan sekali per konfigurasi agar polling
        /// 10 detik tidak menghantam API Google.
        /// </summary>
        public void RefreshWaSheetStatus()
        {
            try
            {
                if (!WaSheetOptions.IsLinkModeEnabled())
                {
                    WaSheetChipVisible = false;
                    return;
                }
                WaSheetChipVisible = true;

                var sheetId = WaSheetOptions.ExtractSheetId(WaSheetOptions.GetSheetUrl());
                if (string.IsNullOrWhiteSpace(sheetId))
                {
                    WaSheetStatus = "Sheet: URL belum diisi";
                    WaSheetStatusTooltip = "Mode tautan aktif tetapi URL Google Sheet jawaban belum diisi — balasan sementara masih memakai percakapan format.\n" +
                        "Perbaikan: buka Pengaturan → Pengaturan Aplikasi → Google Sheet (Mode Tautan), tempel URL Sheet jawaban.\n" +
                        "Panduan langkah demi langkah: menu BANTUAN → Panduan WhatsApp, Bagian D.";
                    return;
                }

                GoogleDriveService drive;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    drive = scope.ServiceProvider.GetRequiredService<GoogleDriveService>();
                }
                catch (Exception exResolve)
                {
                    _logger.LogWarning(exResolve, "Gagal mengakses GoogleDriveService untuk indikator Sheet");
                    return; // biarkan chip pada teks terakhir; dicoba lagi pada tick berikutnya
                }

                if (!drive.IsOAuthEnabled)
                {
                    WaSheetStatus = "Sheet: Login belum dikonfigurasi";
                    WaSheetStatusTooltip = "Mode tautan aktif tetapi klien OAuth Google belum dikonfigurasi di komputer ini.\n" +
                        "Perbaikan: hubungi teknisi untuk menyimpan Client ID & Client Secret (lihat Panduan WhatsApp Bagian D).";
                    return;
                }

                if (!drive.HasStoredToken())
                {
                    WaSheetStatus = "Sheet: Google belum terhubung";
                    WaSheetStatusTooltip = "Mode tautan aktif tetapi akun Google belum terhubung — aplikasi belum bisa membaca jawaban form atau mengirim tautan unduh PDF.\n" +
                        "Perbaikan: masuk lewat 'Login dengan Google' di sidebar, pakai akun yang punya akses ke Sheet jawaban (Editor).";
                    return;
                }

                // Google terhubung → cek ketersediaan Sheets API + akses baca.
                // Hanya sekali per kombinasi (sheetId, tab); ulang bila konfigurasi berubah.
                var tab = WaSheetOptions.GetTabName();
                var key = sheetId + "|" + tab;
                if (_sheetApiCheckRunning || _sheetApiCheckedKey == key) return;

                _sheetApiCheckRunning = true;
                _sheetApiCheckedKey = key;
                WaSheetStatus = "Sheet: Memeriksa…";
                WaSheetStatusTooltip = "Memeriksa akses ke Google Sheet jawaban…";

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var sheets = scope.ServiceProvider.GetRequiredService<GoogleSheetsService>();
                        var (ok, pesan) = await sheets.TestConnectionAsync(sheetId, tab);

                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        void Apply()
                        {
                            if (ok)
                            {
                                WaSheetStatus = "Sheet: Siap";
                                WaSheetStatusTooltip = "Mode tautan aktif dan siap: akun Google terhubung dan Sheet jawaban terbaca.\n" +
                                    "Pesan apa pun dari warga dibalas tautan form; jawaban diproses otomatis dan tautan unduh PDF dikirim balik.\n" +
                                    "Status pemrosesan per baris terlihat di kolom Status pada Google Sheet.";
                            }
                            else if (pesan != null && pesan.StartsWith("Google Sheets API belum aktif", StringComparison.Ordinal))
                            {
                                WaSheetStatus = "Sheet: API belum aktif";
                                WaSheetStatusTooltip = "Google Sheets API belum diaktifkan pada proyek Google Cloud yang dipakai Client ID.\n" +
                                    "Perbaikan (sekali saja, gratis):\n" +
                                    "1. Buka https://console.cloud.google.com/apis/library\n" +
                                    "2. Pilih proyek yang dipakai aplikasi.\n" +
                                    "3. Cari 'Google Sheets API' → klik Enable.\n" +
                                    "Status diperiksa ulang otomatis setelah aplikasi dibuka ulang.\n\n" +
                                    "Detail: " + pesan;
                            }
                            else
                            {
                                // Gangguan jaringan/lainnya — izinkan pemeriksaan ulang pada tick berikutnya.
                                _sheetApiCheckedKey = null;
                                WaSheetStatus = "Sheet: Tidak terhubung";
                                WaSheetStatusTooltip = "Gagal membaca Google Sheet jawaban (kemungkinan koneksi internet).\n" +
                                    "Aplikasi akan memeriksa ulang otomatis.\n\nDetail: " + pesan;
                            }
                        }

                        if (dispatcher is null || dispatcher.CheckAccess()) Apply();
                        else _ = dispatcher.BeginInvoke(Apply);
                    }
                    catch (Exception exCheck)
                    {
                        _logger.LogWarning(exCheck, "Gagal memeriksa Sheets API untuk indikator statusbar");
                    }
                    finally
                    {
                        _sheetApiCheckRunning = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memperbarui status mode tautan Sheet");
            }
        }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        public AsyncRelayCommand ShowRegisterCommand { get; }
        public AsyncRelayCommand ShowSettingsCommand { get; }
        public AsyncRelayCommand ShowFormulirCommand { get; }
        public AsyncRelayCommand ShowAboutCommand { get; }
        public AsyncRelayCommand ShowCatatanRilisCommand { get; }

        /// <summary>Diminta saat pengguna memilih "Keluar" dari sidebar. MainWindow menutup window (satu konfirmasi).</summary>
        public event Action? ExitRequested;

        private string _halamanAktif = "Beranda";

        /// <summary>ViewModel halaman yang judulnya sedang dipantau (lihat <see cref="IJudulHalaman"/>).</summary>
        private INotifyPropertyChanged? _viewJudulDipantau;

        /// <summary>Nama halaman yang sedang dibuka — ditampilkan di title bar jendela.</summary>
        public string HalamanAktif
        {
            get => _halamanAktif;
            private set => SetProperty(ref _halamanAktif, value);
        }

        /// <summary>
        /// Sambungkan judul title bar ke halaman yang baru tampil. Halaman yang memakai
        /// <see cref="IJudulHalaman"/> (mis. wizard Template Surat) boleh mengganti judulnya
        /// sendiri kapan saja — perubahan terpantau lewat PropertyChanged.
        /// </summary>
        private void PantauJudulHalaman(object? view)
        {
            if (_viewJudulDipantau is not null)
            {
                _viewJudulDipantau.PropertyChanged -= HalamanJudulBerubah;
                _viewJudulDipantau = null;
            }

            if (view is IJudulHalaman && view is INotifyPropertyChanged notifier)
            {
                _viewJudulDipantau = notifier;
                notifier.PropertyChanged += HalamanJudulBerubah;
            }

            TerapkanJudulHalaman();
        }

        private void HalamanJudulBerubah(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(IJudulHalaman.JudulHalaman))
            {
                TerapkanJudulHalaman();
            }
        }

        /// <summary>Tulis judul title bar: judul halaman bila ada, selain itu nama menu terakhir.</summary>
        private void TerapkanJudulHalaman()
        {
            string? judul = (CurrentView as IJudulHalaman)?.JudulHalaman;
            HalamanAktif = string.IsNullOrWhiteSpace(judul) ? _featureNavigation.JudulHalamanAktif : judul!;
        }

        private SidebarMode _modeSidebar = SidebarMode.Terbuka;

        /// <summary>Pengatur tema aktif — dipakai jendela untuk menggambar ulang ikonnya
        /// saat tema berubah (ubin aksen mengikuti menu sidebar).</summary>
        public ThemeService TemaService => _themeService;

        /// <summary>
        /// Tampilan sidebar yang sedang dipakai: terbuka penuh atau ikon saja. Template
        /// sidebar memakai <see cref="SidebarCiut"/> untuk menyembunyikan label.
        /// </summary>
        public SidebarMode ModeSidebar
        {
            get => _modeSidebar;
            private set
            {
                if (!SetProperty(ref _modeSidebar, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(SidebarCiut));
                OnPropertyChanged(nameof(SidebarTerbuka));
                OnPropertyChanged(nameof(SidebarModeTeks));
                OnPropertyChanged(nameof(ModeSidebarBerikut));
            }
        }

        /// <summary>True bila sidebar hanya menampilkan ikon (mode ikon saja).</summary>
        public bool SidebarCiut => _modeSidebar != SidebarMode.Terbuka;

        /// <summary>Kebalikan <see cref="SidebarCiut"/> — memudahkan binding Visibility.</summary>
        public bool SidebarTerbuka => _modeSidebar == SidebarMode.Terbuka;

        /// <summary>Keterangan tampilan yang sedang dipakai (tooltip tombol).</summary>
        public string SidebarModeTeks => SidebarModePrefs.Teks(_modeSidebar);

        /// <summary>Mode yang akan dipakai bila tombol ganti tampilan ditekan.</summary>
        public SidebarMode ModeSidebarBerikut => SidebarModePrefs.Berikutnya(_modeSidebar);

        /// <summary>Ubah mode sidebar (dipanggil jendela saat tombol ganti tampilan diklik).</summary>
        public void SetSidebarMode(SidebarMode mode) => ModeSidebar = mode;

        /// <summary>
        /// Kompatibilitas: ciut = mode ikon saja, lebar = tampilan penuh.
        /// </summary>
        public void SetSidebarCiut(bool ciut)
            => SetSidebarMode(ciut ? SidebarMode.IkonRingkas : SidebarMode.Terbuka);

        /// <summary>Buka halaman awal dan sorot item Beranda pada sidebar.</summary>
        public void BukaBeranda()
        {
            _featureNavigation.PilihBeranda();
            _ = ShowBerandaAsync();
        }

        private Action ResolveFeatureRoute(string route)
        {
            // Gerbang menu vital: selama lisensi belum aktif (instalasi baru
            // belum trial, trial habis, atau langganan berakhir), rute pembuatan/
            // perubahan data tidak dijalankan. Daftar rutenya ada di Core supaya
            // sama dengan aturan yang diuji.
            if (SuDesApp.Services.PenjagaLisensi.ApakahMenuVital(route) &&
                _penjaga is { Terkunci: true })
            {
                return () => _ = TolakMenuVitalAsync(route);
            }

            if (route.StartsWith("surat:", StringComparison.Ordinal))
            {
                string jenis = route["surat:".Length..];
                return () => _ = LoadSuratAsync(jenis);
            }
            if (route.StartsWith("keputusan:", StringComparison.Ordinal))
            {
                string jenis = route["keputusan:".Length..];
                return () => _ = ShowKeputusanAsync(jenis);
            }
            if (route.StartsWith("agenda:", StringComparison.Ordinal))
            {
                string jenis = route["agenda:".Length..];
                return () => _ = ShowAgendaAsync(jenis);
            }
            if (route.StartsWith("template-pdf:", StringComparison.Ordinal))
            {
                string nama = route["template-pdf:".Length..];
                return () => _ = ShowFormulirPdfAsync(nama);
            }
            return route switch
            {
                "beranda" => () => _ = ShowBerandaAsync(),
                "google-login" => () => _ = ShowGoogleLoginAsync(),
                "register-surat" => () => _ = ShowRegisterSuratAsync(),
                "rekening-koran" => () => _ = ShowRekeningKoranAsync(),
                "template-surat" => () => _ = ShowTemplateSuratAsync(),
                "daftar-hadir" => () => _ = ShowDaftarHadirAsync(),
                "ntcr-paket" => () => _ = ShowNtcrPaketAsync(),
                "register-ntcr" => () => _ = ShowRegisterNtcrAsync(),
                "tutup-buku" => ShowTutupBuku,
                "verifikasi-surat" => ShowVerifikasiSurat,
                "riwayat" => () => _ = ShowRiwayatAsync(),
                "layanan-online" => () => _ = ShowWaPanelAsync(),
                "perangkat-desa" => () => _ = ShowPerangkatDesaAsync(),
                "sotk-desa" => () => _ = ShowSotkDesaAsync(),
                "apbdes" => () => _ = ShowApbdesAsync(),
                "idm" => () => _ = ShowIdmAsync(),
                "warga" => () => _ = ShowWargaAsync(),
                "laporan-penduduk" => () => _ = ShowLaporanAsync(),
                "pengaturan-aplikasi" => () => _ = ShowPengaturanAplikasiAsync(),
                "pengaturan-surat" => () => _ = ShowSettingsAsync(),
                "penomoran-surat" => () => _ = ShowPengaturanNomorSuratAsync(),
                "pengaturan-formulir" => () => _ = ShowFormulirAsync(),
                "kelola-pengguna" => () => _ = ShowKelolaPenggunaAsync(),
                "cadangkan-pulihkan" => () => _ = ShowExImdbAsync(),
                "langganan" => () => _ = ShowLanggananAsync(),
                "pusat-administrasi" => () => _ = BukaPusatAdministrasiAsync(),
                "formulir" => () => _ = ShowFormulirAsync(),
                "pembaruan" => () => _ = ShowPembaruanAsync(),
                "panduan-awal" => () => _ = ShowPanduanAwalAsync(),
                "dokumentasi" => () => _ = ShowDokumentasiAsync(),
                "panduan-whatsapp" => () => _ = ShowPanduanWaAsync(),
                "api-desa" => ShowApi,
                "catatan-rilis" => () => _ = ShowCatatanRilisAsync(),
                "tentang" => () => _ = ShowAboutAsync(),
                "keluar" => () => ExitRequested?.Invoke(),
                _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Rute fitur tidak dikenal.")
            };
        }

        private async Task ShowBerandaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<BerandaViewModel>();
                vm.BuatSuratDiminta += jenis => _ = LoadSuratAsync(jenis);
                vm.BukaHalamanDiminta += halaman => _ = BukaDariBerandaAsync(halaman);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Beranda");
                await _messageService.ShowErrorAsync("Gagal membuka Beranda: " + ex.Message);
            }
        }

        /// <summary>Teruskan aksi cepat dari Beranda ke halaman yang bersangkutan.</summary>
        private Task BukaDariBerandaAsync(string halaman) => halaman switch
        {
            "REGISTER" => ShowRegisterSuratAsync(),
            "RIWAYAT" => ShowRiwayatAsync(),
            "PENGATURAN" => ShowSettingsAsync(),
            "TEMPLATE" => ShowTemplateSuratAsync(),
            _ => Task.CompletedTask
        };

        private async Task ShowRegisterSuratAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<RegisterSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Register Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Register Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Langganan: status masa aktif lisensi, kode aktivasi, dan
        /// perpanjangan. Berdiri sendiri di navigasi menu utama (bukan bagian
        /// Pengaturan Aplikasi), karena urusan pembayaran dicari tersendiri.
        /// </summary>
        private async Task ShowLanggananAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<LanggananViewModel>();
                NavigasiHalaman<Views.LanggananView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Langganan");
                await _messageService.ShowErrorAsync("Gagal membuka halaman Langganan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman pusat administrasi server lisensi (rute /admin) DI DALAM
        /// aplikasi lewat WebView2, dengan tombol Buka di Browser sebagai cadangan.
        /// Halaman itu milik server — bukan halaman WPF — dan meminta token admin
        /// yang diketik operator di halaman itu sendiri.
        ///
        /// Alamatnya berasal dari <see cref="SuDesApp.Services.LisensiManager.AlamatPusatAdministrasi"/>:
        /// server yang sama dengan yang dipakai halaman Langganan, sehingga menu ini
        /// tidak bisa menunjuk server yang berbeda dari yang dipakai aplikasi; baris
        /// menu di sidebar menyebut alamat itu dan keadaan sambungannya sejak awal.
        /// </summary>
        private async Task BukaPusatAdministrasiAsync()
        {
            // Penjagaan kedua: walau menu hanya dibuat untuk akun pusat, tindakan
            // ini tidak pernah membuka halaman operator dari sesi akun desa.
            if (!AkunPusat.SesiIniPusat)
            {
                _logger.LogWarning("Pusat administrasi diminta dari sesi non-pusat; permintaan ditolak.");
                await _messageService.ShowWarningAsync(
                    "Halaman pusat administrasi hanya tersedia untuk akun pusat.");
                return;
            }

            string alamat = _lisensi?.AlamatPusatAdministrasi() ?? string.Empty;
            if (alamat.Length == 0)
            {
                await _messageService.ShowWarningAsync(
                    "Alamat server lisensi belum diatur, jadi pusat administrasi belum bisa dibuka. " +
                    "Isi alamat server di halaman Langganan (bagian LANGGANAN) lalu buka menu ini lagi.");
                return;
            }

            try
            {
                var halaman = new Views.PusatAdministrasiView();
                _navigation.Navigate(halaman);
                await halaman.MuatAsync(alamat);
                _logger.LogInformation("Pusat administrasi dibuka di dalam aplikasi: {Alamat}", alamat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pusat administrasi {Alamat}", alamat);
                await _messageService.ShowErrorAsync("Gagal membuka pusat administrasi: " + ex.Message);
            }
        }

        private async Task ShowPengaturanAplikasiAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PengaturanAplikasiViewModel>();
                NavigasiHalaman<Views.PengaturanAplikasiView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Aplikasi");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Aplikasi: " + ex.Message);
            }
        }

        /// <summary>Klik menu Pengaturan Nomor Surat → halaman mandirinya.</summary>
        private async Task ShowPengaturanNomorSuratAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PengaturanNomorSuratViewModel>();
                NavigasiHalaman<Views.PengaturanNomorSuratView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Nomor Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Nomor Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka Pengaturan Aplikasi lalu langsung menggulir ke kartu dengan kunci
        /// seksi yang diberikan (Gateway WhatsApp / Google Sheet) — dipanggil
        /// klik pada chip status WA/Sheet di statusbar.
        /// </summary>
        private async Task OpenSettingsFocusAsync(string sectionKey)
        {
            try
            {
                var vm = BuatScopeHalaman()
                    .GetRequiredService<PengaturanAplikasiViewModel>();
                vm.SetFocusSection(sectionKey);
                NavigasiHalaman<Views.PengaturanAplikasiView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Aplikasi (fokus {Seksi})", sectionKey);
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Aplikasi: " + ex.Message);
            }
        }

        /// <summary>
        /// Versi publik untuk pemanggil dari luar (mis. banner panel Layanan
        /// Online): buka Pengaturan Aplikasi fokus ke kartu terkait.
        /// </summary>
        public Task OpenSettingsFocusPublicAsync(string sectionKey)
            => OpenSettingsFocusAsync(sectionKey);

        /// <summary>
        /// Buka halaman Login dengan Google dari luar ViewModel ini (banner panel
        /// Layanan Online). Lewat jalur ini scope halamannya ikut dikelola
        /// NavigationService — code-behind view tidak boleh membuat scope sendiri.
        /// </summary>
        public Task ShowGoogleLoginPublicAsync() => ShowGoogleLoginAsync();

        /// <summary>
        /// Buka halaman Langganan dari luar ViewModel (mis. popup startup yang
        /// mengingatkan trial habis). Memakai jalur navigasi yang sama dengan
        /// tombol chip Langganan — tidak ada jalur kedua.
        /// </summary>
        public Task ShowLanggananPublicAsync() => ShowLanggananAsync();

        /// <summary>
        /// Tampilkan halaman dengan pohon visual yang dipertahankan antar bukaan:
        /// instance halaman dibuat sekali lalu digunakan lagi untuk ViewModel baru,
        /// sehingga membuka kembali halaman tidak membangun ulang seluruh elemennya.
        /// </summary>
        private void NavigasiHalaman<TView>(object viewModel) where TView : UserControl, new()
        {
            if (!_cacheHalaman.TryGetValue(typeof(TView), out var halaman))
            {
                halaman = new TView();
                _cacheHalaman[typeof(TView)] = halaman;
            }

            halaman.DataContext = viewModel;
            _navigation.Navigate(halaman);
        }

        /// <summary>Klik chip WA di statusbar → Pengaturan Aplikasi, kartu Gateway WhatsApp.</summary>
        public AsyncRelayCommand WaChipClickCommand { get; }

        /// <summary>Klik chip Sheet di statusbar → Pengaturan Aplikasi, kartu Google Sheet.</summary>
        public AsyncRelayCommand SheetChipClickCommand { get; }

        /// <summary>Klik chip status langganan di statusbar → halaman Langganan.</summary>
        public AsyncRelayCommand LanggananChipClickCommand { get; }

        // ==== Indikator status langganan di statusbar (Full Service / Demo / Baca-saja) ====

        private string _langgananStatus = string.Empty;
        private string _langgananStatusTooltip = string.Empty;
        private bool _langgananChipVisible;
        private int _langgananTone;

        /// <summary>Teks chip langganan di statusbar: "Full Service", "Demo", atau "Baca-saja" —
        /// dilengkapi sisa hari selama masih ada, supaya masa aktif terbaca langsung di chip
        /// tanpa perlu menahan kursor ke tooltip.</summary>
        public string LanggananStatus
        {
            get => _langgananStatus;
            private set => SetProperty(ref _langgananStatus, value);
        }

        public string LanggananStatusTooltip
        {
            get => _langgananStatusTooltip;
            private set => SetProperty(ref _langgananStatusTooltip, value);
        }

        /// <summary>Chip tampil selama penjaga lisensi tersedia (selalu, pada aplikasi normal).</summary>
        public bool LanggananChipVisible
        {
            get => _langgananChipVisible;
            private set => SetProperty(ref _langgananChipVisible, value);
        }

        /// <summary>Nada warna chip: 1 = aktif (hijau), 2 = Demo (kuning), 3 = baca-saja (merah).</summary>
        public int LanggananTone
        {
            get => _langgananTone;
            private set => SetProperty(ref _langgananTone, value);
        }

        /// <summary>
        /// Segarkan chip status langganan dari penjaga lisensi — satu sumber
        /// dengan gerbang fitur, jadi chip tidak bisa berbeda dari perilaku
        /// aplikasinya: "Full Service" (langganan sah), "Demo" (belum
        /// berlangganan), "Baca-saja" (langganan berakhir).
        /// </summary>
        private void TerapkanChipLisensi(SuDesApp.Services.PenjagaLisensi? penjaga)
        {
            LanggananChipVisible = penjaga is not null;
            if (penjaga is null)
            {
                LanggananStatus = string.Empty;
                LanggananStatusTooltip = string.Empty;
                _featureNavigation.TerapkanKunciLisensi(false, null);
                return;
            }

            // Gembok di sidebar mengikuti aturan yang sama dengan gerbang rute:
            // menu vital ditandai SEBELUM diklik, dan kalimat tooltip-nya
            // menyesuaikan jalan keluar yang tersedia (trial vs pembayaran).
            bool terkunci = penjaga.Terkunci;
            _featureNavigation.TerapkanKunciLisensi(
                terkunci,
                terkunci ? SuDesApp.Services.PenjagaLisensi.TooltipTerkunci(penjaga.Keadaan) : null);

            // Teks & nada berasal dari kebijakan Core (teruji); di sini hanya
            // kalimat tooltip yang khas UI. Sisa hari memakai sumber yang tepat:
            // trial punya hitungannya sendiri, langganan memakai masa tenggang.
            int sisaHari = penjaga.Keadaan == SuDesApp.Services.KeadaanLisensi.Trial
                ? penjaga.SisaHariTrial
                : penjaga.Status?.SisaHariTenggang ?? 0;
            LanggananStatus = SuDesApp.Services.PenjagaLisensi.LabelChip(penjaga.Keadaan, sisaHari);
            LanggananTone = SuDesApp.Services.PenjagaLisensi.NadaKeadaan(penjaga.Keadaan);

            var sampai = penjaga.Status?.BerlakuSampai?.ToLocalTime().ToString("dd MMM yyyy");
            var trialSampai = penjaga.TrialBerakhirUtc?.ToLocalTime().ToString("dd MMM yyyy");
            LanggananStatusTooltip = penjaga.Keadaan switch
            {
                SuDesApp.Services.KeadaanLisensi.Berlangganan =>
                    "Langganan desa aktif" +
                    (sampai is null ? "" : $" s.d. {sampai}") +
                    ". Klik untuk membuka halaman Langganan.",

                SuDesApp.Services.KeadaanLisensi.Trial =>
                    "Trial 14 hari aktif" +
                    (trialSampai is null ? "" : $" sampai {trialSampai}") +
                    $" (sisa {penjaga.SisaHariTrial} hari). Klik untuk membuka halaman Langganan.",

                SuDesApp.Services.KeadaanLisensi.BacaSaja =>
                    SuDesApp.Services.PenjagaLisensi.PesanTrialHabis +
                    " Klik untuk membuka halaman Langganan.",

                _ =>
                    "Mode uji coba belum dimulai: menu vital terkunci. Klik untuk memulai " +
                    "trial 14 hari atau mengaktifkan langganan."
            };
        }

        /// <summary>
        /// Penjaga alur pembuatan surat baru: mode baca-saja menolak membuka form
        /// dengan pesan yang mengarahkan ke halaman Langganan. True = boleh lanjut.
        /// </summary>
        private async Task<bool> BolehBuatSuratBaruAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(SuDesApp.Services.PenjagaLisensi.PesanAksesDikunci);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Dipanggil ketika pengguna menekan menu VITAL saat lisensi terkunci.
        /// Instalasi baru (Demo) ditawari memulai trial 14 hari langsung dari
        /// sini; trial habis / langganan berakhir diarahkan ke halaman Langganan.
        /// Menu tetap tidak dibuka sampai trial dimulai atau pembayaran selesai.
        /// </summary>
        private async Task TolakMenuVitalAsync(string route)
        {
            var penjaga = _penjaga;
            if (penjaga is null)
            {
                return;
            }

            if (penjaga.Keadaan == SuDesApp.Services.KeadaanLisensi.Demo)
            {
                bool mulai = await _messageService.ShowConfirmationAsync(
                    "Menu terkunci — mulai trial 14 hari",
                    SuDesApp.Services.PenjagaLisensi.PesanMulaiTrial);

                if (!mulai)
                {
                    await _messageService.ShowInfoAsync(
                        SuDesApp.Services.PenjagaLisensi.PesanBelumTrial);
                    return;
                }

                // Kode aktivasi sudah ada & server terjangkau → trial server
                // (sekali per desa, bertanda tangan). Trial lokal hanya dipakai
                // bila kode belum ada atau server tidak terjangkau.
                var percobaan = await penjaga.MulaiPercobaanAsync();
                var (judulPercobaan, pesanPercobaan) =
                    SuDesApp.Services.PenjagaLisensi.JelaskanPercobaan(percobaan);

                if (percobaan.BerlakuSampai is DateTime sampaiPercobaan)
                {
                    pesanPercobaan +=
                        $" Berlaku sampai {sampaiPercobaan.ToLocalTime():dd MMM yyyy}.";
                }

                bool ditolak = percobaan.Asal
                    is SuDesApp.Services.AsalPercobaan.DitolakServer
                    or SuDesApp.Services.AsalPercobaan.SudahPernahTrial;

                await _messageService.ShowMessageAsync(
                    pesanPercobaan,
                    judulPercobaan,
                    SuDesApp.Utilities.AppMessageButton.Ok,
                    ditolak
                        ? SuDesApp.Utilities.AppMessageIcon.Warning
                        : SuDesApp.Utilities.AppMessageIcon.Info);

                // Menu hanya dibuka bila gerbangnya benar-benar sudah terbuka —
                // trial yang ditolak server tidak boleh membuka halaman.
                if (!penjaga.Terkunci)
                {
                    ResolveFeatureRoute(route)();
                }
                return;
            }

            // Trial habis / langganan berakhir: menu tetap terkunci, pengguna
            // diarahkan menyelesaikan pembayaran.
            await _messageService.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanTrialHabis);
            await ShowLanggananAsync();
        }

        private async Task ShowRiwayatAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<RiwayatViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Riwayat Aktivitas");
                await _messageService.ShowErrorAsync("Gagal membuka Riwayat Aktivitas: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Perangkat Desa: tabel jabatan perangkat desa (Kepala
        /// Desa sampai Kadus) berisi orang yang memegangnya, lengkap dengan
        /// ekspor Excel dan cetak PDF. View dimuat App.xaml lewat DataTemplate
        /// untuk <see cref="PerangkatDesaViewModel"/>.
        ///
        /// Data dimuat SEBELUM navigasi, sama seperti halaman Data Warga: tanpa
        /// ini halaman terbuka dengan kartu KPI dan tabel yang masih kosong —
        /// halaman ini tidak punya tombol Muat Ulang, jadi pemuatan awal ini
        /// satu-satunya pembacaan database saat dibuka.
        /// </summary>
        private async Task ShowPerangkatDesaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PerangkatDesaViewModel>();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Perangkat Desa");
                await _messageService.ShowErrorAsync("Gagal membuka Perangkat Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman SOTK Desa: dokumen Susunan Organisasi dan Tata Kerja
        /// Pemerintah Desa dari data perangkat tersimpan. Data dimuat SEBELUM
        /// navigasi seperti halaman Perangkat Desa supaya ringkasan keterisian
        /// langsung tampil saat halaman terbuka.
        /// </summary>
        private async Task ShowSotkDesaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<SotkDesaViewModel>();

                // Contoh Perdes dan naskah SOTK memakai jalur Buku Perdes yang
                // sama: isian awal dibuka di formulir PERDES, bukan dokumen
                // terpisah di halaman SOTK.
                vm.BukaPerdesUntukDisuntingAsync = BukaPerdesDariSotkAsync;
                vm.BukaBukuPerdesAsync = () => ShowKeputusanAsync("PERDES");
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka SOTK Desa");
                await _messageService.ShowErrorAsync("Gagal membuka SOTK Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Data Warga: pencarian, filter, mutasi status, Kartu
        /// Keluarga, dan ekspor. View dipetakan App.xaml lewat DataTemplate untuk
        /// <see cref="WargaViewModel"/>, jadi baris pertama langsung dimuat di sini
        /// supaya tabel tidak tampak kosong saat halaman terbuka.
        ///
        /// Navigasikan lebih dulu agar halaman Data Warga langsung tampak; daftar
        /// kemudian dimuat asinkron. Rekapitulasi mengikuti di latar belakang.
        /// </summary>
        private async Task ShowWargaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<WargaViewModel>();
                _navigation.Navigate(vm);
                await vm.LoadAsync(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Data Warga");
                await _messageService.ShowErrorAsync("Gagal membuka Data Warga: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Laporan Penduduk: rekapitulasi yang bisa dicetak PDF atau
        /// disimpan Excel dari satu sumber angka yang sama.
        /// </summary>
        private async Task ShowLaporanAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<LaporanViewModel>();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Laporan Penduduk");
                await _messageService.ShowErrorAsync("Gagal membuka Laporan Penduduk: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Kelola Pengguna: daftar akun, peran, dan reset kata sandi.
        /// Hanya Administrator yang boleh memakainya (ditegakkan PenggunaService).
        /// </summary>
        private async Task ShowKelolaPenggunaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<KelolaPenggunaViewModel>();
                await vm.MuatAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Kelola Pengguna");
                await _messageService.ShowErrorAsync("Gagal membuka Kelola Pengguna: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Tutup Buku Tahunan: status setiap tahun, verifikasi nomor,
        /// dan rekap yang bisa dicetak. MuatCommand dijalankan lewat perintahnya
        /// karena pemuatan di halaman ini memang dipicu dari tombol Muat.
        /// </summary>
        private void ShowTutupBuku()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<TutupBukuTahunViewModel>();
                vm.MuatCommand.Execute(null);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Tutup Buku Tahunan");
                _ = _messageService.ShowErrorAsync("Gagal membuka Tutup Buku Tahunan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman APBDES: rancangan anggaran dan realisasi per jenis,
        /// tersimpan lokal. Terbuka untuk semua peran.
        /// </summary>
        private async Task ShowApbdesAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ApbdesViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka APBDES");
                await _messageService.ShowErrorAsync("Gagal membuka APBDES: " + ex.Message);
            }
        }

        private IdmViewModel? _idmViewModel;

        /// <summary>
        /// Buka halaman IDM (Indeks Desa Membangun): skor IKS/IKE/IKL, IDM, dan rincian
        /// indikator resmi untuk desa pada Pengaturan Surat. Halaman hanya
        /// menampilkan data idm.kemendesa.go.id, jadi pengambilan datanya dimulai
        /// setelah halaman tampil supaya pengguna melihat keadaan "memuat".
        ///
        /// Instance VM disimpan agar tidak memuat ulang data setiap kali halaman
        /// dibuka — hanya sekali saat aplikasi berjalan. Untuk memuat ulang, pengguna
        /// bisa menekan tombol "Muat ulang" di halaman tersebut.
        /// </summary>
        private async Task ShowIdmAsync()
        {
            try
            {
                // Gunakan instance yang sudah ada jika masih tersedia
                if (_idmViewModel == null)
                {
                    _idmViewModel = BuatScopeHalaman().GetRequiredService<IdmViewModel>();
                    _navigation.Navigate(_idmViewModel);
                    await _idmViewModel.MuatAsync();
                }
                else
                {
                    // Halaman sudah pernah dimuat, tampilkan saja tanpa memuat ulang
                    _navigation.Navigate(_idmViewModel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka IDM");
                await _messageService.ShowErrorAsync("Gagal membuka IDM: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Verifikasi Surat: masukkan kode verifikasi yang tercetak di
        /// surat untuk memeriksa keasliannya. Terbuka untuk semua peran, termasuk
        /// Kades dan Auditor yang tidak membuat surat.
        /// </summary>
        private void ShowVerifikasiSurat()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<VerifikasiSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Verifikasi Surat");
                _ = _messageService.ShowErrorAsync("Gagal membuka Verifikasi Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Dokumentasi: daftar dokumen bawaan aplikasi (regulasi,
        /// panduan, kebijakan). Membaca satu dokumen membuka halaman baca lewat
        /// <see cref="DokumenBacaViewModel"/>, yang daftarnya tidak di sidebar.
        /// </summary>
        private async Task ShowDokumentasiAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<DokumentasiViewModel>();
                await vm.InitializeAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Dokumentasi");
                _ = _messageService.ShowErrorAsync("Gagal membuka Dokumentasi: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman API Desa: nyala/mati listener HTTP lokal, port, dan kunci
        /// API. Pengaturan dibaca ulang setiap halaman dibuka karena disimpan di
        /// preferensi aplikasi, bukan di database.
        /// </summary>
        private void ShowApi()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ApiViewModel>();
                vm.MuatUlang();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka API Desa");
                _ = _messageService.ShowErrorAsync("Gagal membuka API Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka alur paket NTCR: satu form untuk seluruh blanko N1–N6, disimpan
        /// sekaligus dan dicetak menjadi satu berkas PDF gabungan.
        /// </summary>
        private async Task ShowNtcrPaketAsync()
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<NtcrPaketViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
                await vm.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka paket NTCR");
                await _messageService.ShowErrorAsync("Gagal membuka paket NTCR: " + ex.Message);
            }
        }

        private async Task ShowRegisterNtcrAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<RegisterNtcrViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Register NTCR");
                await _messageService.ShowErrorAsync("Gagal membuka Register NTCR: " + ex.Message);
            }
        }

        private async Task ShowSettingsAsync()
        {
            _logger.LogInformation("ShowSettingsAsync called");
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<SetelanViewModel>();
                NavigasiHalaman<Views.SetelanView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan: " + ex.Message);
            }
        }

        private async Task ShowFormulirAsync()
        {
            _logger.LogInformation("ShowFormulirAsync called");
            try
            {
                // Pengaturan Formulir bersifat "template sesi": instance VM disimpan
                // selama aplikasi berjalan sehingga daftar template tidak perlu di-scan
                // ulang (plus centang/pencarian pengguna) setiap kali menu dibuka —
                // sama seperti Daftar Hadir. Tombol Segarkan tetap menyegarkan daftar.
                if (_formulirViewModel is null)
                {
                    var sp = BuatScopeBertahan();
                    _formulirViewModel = sp.GetRequiredService<FormulirViewModel>();
                }

                _navigation.Navigate(new Views.FormulirView { DataContext = _formulirViewModel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Formulir");
                await _messageService.ShowErrorAsync("Gagal membuka Formulir: " + ex.Message);
            }
        }

        private async Task ShowFormulirPdfAsync(string templateName)
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<Func<string, FormulirPdfViewModel>>()(templateName);
                // Navigasikan instance view BARU (bukan via DataTemplate) agar PDF termuat
                // ulang setiap kali template dipilih — ContentPresenter yang merecycle elemen
                // lama akan tetap menampilkan PDF yang pertama dimuat.
                _navigation.Navigate(new Views.PdfPreviewView { DataContext = vm });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pratinjau formulir {Template}", templateName);
                await _messageService.ShowErrorAsync("Gagal membuka pratinjau formulir: " + ex.Message);
            }
        }

        private async Task ShowAgendaAsync(string jenisSurat)
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<AgendaSuratViewModel>();

                // Tampilkan halaman dulu, muat data menyusul (lihat ShowKeputusanAsync).
                _navigation.Navigate(vm);
                await vm.SetJenisAsync(jenisSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku agenda surat {Jenis}", jenisSurat);
                await _messageService.ShowErrorAsync("Gagal membuka buku agenda: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Daftar Hadir: pilih kolom yang dicetak, isi peserta, lalu
        /// cetak PDF dengan kop surat desa (lihat DaftarHadirViewModel).
        /// </summary>
        private async Task ShowDaftarHadirAsync()
        {
            try
            {
                // Daftar Hadir bersifat "template sesi": instance VM disimpan selama
                // aplikasi berjalan sehingga isian terakhir (setelah cetak PDF) tetap
                // tampil saat menu dibuka lagi. Keluar aplikasi → instance hilang dan
                // menu kembali ke isian default.
                if (_daftarHadirViewModel is null)
                {
                    var sp = BuatScopeBertahan();
                    _daftarHadirViewModel = sp.GetRequiredService<DaftarHadirViewModel>();
                    _daftarHadirViewModel.RequestClose += () => _navigation.ShowDefault();
                }

                _navigation.Navigate(_daftarHadirViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Daftar Hadir");
                await _messageService.ShowErrorAsync("Gagal membuka Daftar Hadir: " + ex.Message);
            }
        }

        private async Task ShowKeputusanAsync(string jenisKeputusan)
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<KeputusanViewModel>();

                // Halaman ditampilkan lebih dulu (judul + grid kosong ber-indikator
                // memuat), baru data dimuat — klik menu terasa langsung merespons
                // walau arsipnya besar.
                _navigation.Navigate(vm);
                await vm.SetJenisAsync(jenisKeputusan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku SK/Peraturan {Jenis}", jenisKeputusan);
                await _messageService.ShowErrorAsync("Gagal membuka buku SK/Peraturan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka formulir Buku Perdes dari menu SOTK Desa. Dipakai oleh contoh
        /// Perdes (isian awal + lampiran contoh) dan oleh tautan "Buka Buku
        /// Perdes". Setelah disimpan, pengguna tetap berada di halaman PERDES
        /// seperti alur peraturan desa biasa.
        /// </summary>
        private async Task BukaPerdesDariSotkAsync(DataKeputusan prefill, string? lampiranContohPath)
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<KeputusanViewModel>();
                _navigation.Navigate(vm);
                await vm.SetJenisAsync("PERDES");
                await vm.TambahDenganIsianAsync(prefill, lampiranContohPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka formulir Perdes dari SOTK Desa");
                await _messageService.ShowErrorAsync("Gagal membuka formulir Perdes: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Pencadangan Database — satu halaman berisi kartu Ekspor
        /// (membuat salinan cadangan) dan kartu Impor (memulihkan dari cadangan).
        /// </summary>
        private async Task ShowExImdbAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ExImdbViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pencadangan Database");
                await _messageService.ShowErrorAsync("Gagal membuka Pencadangan Database: " + ex.Message);
            }
        }

        private async Task ShowGoogleLoginAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<GoogleLoginViewModel>();
                vm.OnLoggedIn = email => { _ = RefreshGoogleBadgeAsync(); };

                // Bagian sinkron InitializeAsync (set state awal: belum dikonfigurasi /
                // belum login / "Menghubungkan…") berjalan seketika, tapi navigasi TIDAK
                // menunggu validasi token ke server Google — halaman tampil instan dan
                // status ter-update menyusul di background.
                var initTask = vm.InitializeAsync();
                _navigation.Navigate(vm);
                await initTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Login dengan Google");
                await _messageService.ShowErrorAsync("Gagal membuka Login dengan Google: " + ex.Message);
            }
        }

        private async Task ShowPembaruanAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PembaruanViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pembaruan");
                await _messageService.ShowErrorAsync("Gagal membuka Pembaruan: " + ex.Message);
            }
        }

        /// <summary>
        /// Laporkan hasil pemasangan pembaruan kecil yang dijalankan skrip penerap saat
        /// aplikasi ditutup. Hasilnya ditulis ke berkas di %LOCALAPPDATA% dan dibaca
        /// satu kali di sini supaya pengguna tahu apakah pembaruan benar-benar masuk.
        /// </summary>
        private Task LaporkanHasilTambalanAsync()
        {
            try
            {
                var hasil = PatchUpdateService.AmbilHasilTerakhir();
                if (hasil == null) return Task.CompletedTask;

                // Catat ke riwayat pembaruan agar bisa diperiksa kapan saja dari
                // halaman Pembaruan (versi, tanggal, jumlah berkas, hasil).
                try
                {
                    RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                    {
                        Versi = hasil.Versi,
                        Jenis = "Tambalan",
                        JumlahBerkas = hasil.JumlahBerkas,
                        Berhasil = hasil.Berhasil,
                        Pesan = hasil.Pesan,
                        Waktu = hasil.Waktu == default ? DateTime.Now : hasil.Waktu
                    });
                }
                catch (Exception exRiwayat)
                {
                    _logger.LogDebug(exRiwayat, "Riwayat pembaruan gagal dicatat.");
                }

                if (hasil.Berhasil)
                {
                    _notifications.Success(
                        $"Pembaruan {hasil.Versi} berhasil dipasang",
                        hasil.Pesan + " Aplikasi sudah memakai versi terbaru.",
                        "pembaruan");
                }
                else
                {
                    _notifications.Error(
                        $"Pembaruan {hasil.Versi} gagal dipasang",
                        hasil.Pesan + "\nCoba ulangi dari menu Pembaruan, atau pakai installer penuh (menu Pembaruan).",
                        "pembaruan");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hasil pemasangan pembaruan tidak terbaca.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Periksa pembaruan di latar belakang setelah pengguna masuk. Notifikasi
        /// menyebut dengan jelas APA yang diperbaiki dan jenis pembaruannya: tambalan
        /// kecil (tanpa installer) atau installer penuh. Gagal memeriksa (mis. tanpa
        /// internet) cukup dicatat di log — tidak mengganggu pekerjaan pengguna.
        /// </summary>
        private async Task PeriksaPembaruanLatarAsync()
        {
            try
            {
                // Bisa dimatikan di Pengaturan Aplikasi (mis. aplikasi dipakai luring).
                if (!AppPreferenceStore.IsPeriksaPembaruanSaatMulai()) return;

                // Mode diam-diam startup: tunda pemeriksaan online sampai jeda menit
                // berlalu — jaringan tidak berebut dengan pemuatan awal halaman.
                if (AppPreferenceStore.IsStartupDiamDiam())
                {
                    await Task.Delay(TimeSpan.FromMinutes(AppPreferenceStore.GetStartupDiamDiamMenit()));
                }

                using var scope = _scopeFactory.CreateScope();
                var updateService = scope.ServiceProvider.GetRequiredService<UpdateService>();
                var patchService = scope.ServiceProvider.GetRequiredService<PatchUpdateService>();

                if (!updateService.IsAvailable) return;

                var rilis = await updateService.CheckForUpdatesAsync();
                if (rilis == null || string.IsNullOrWhiteSpace(rilis.Version)) return;

                var versiTerpasang = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                                     ?? new Version(0, 0);
                if (!Version.TryParse(rilis.Version, out var versiRilis) || versiRilis <= versiTerpasang) return;

                var tambalan = await patchService.AmbilPenawaranAsync(rilis);
                var rencana = patchService.SusunRencana(tambalan, versiTerpasang.ToString());
                bool bisaTambalan = rencana.BisaDipakai;

                var perbaikan = (tambalan?.Ringkasan is { Count: > 0 } ringkasan)
                    ? ringkasan
                    : DaftarPerbaikanDariCatatan(rilis.ReleaseNotes);

                var daftar = perbaikan.Count == 0
                    ? string.Empty
                    : string.Join("\n", perbaikan.Take(6).Select(p => "• " + p)) + "\n";

                var keterangan = bisaTambalan
                    ? $"Perbaikan ini cukup dipasang sebagai pembaruan kecil: {rencana.BerkasDiganti.Count} berkas " +
                      $"({FormatUkuranKb(rencana.TotalByte)}) — tanpa installer, data surat & pengaturan aman.\n"
                    : "Pembaruan ini memakai installer penuh.\n";

                _notifications.Info(
                    bisaTambalan ? $"Pembaruan kecil {rilis.Version} tersedia" : $"Pembaruan {rilis.Version} tersedia",
                    keterangan + daftar + "Buka menu Pembaruan untuk memasang.",
                    "pembaruan");

                TandaiMenuPembaruan();
            }
            catch (Exception ex)
            {
                // Termasuk tanpa internet / repo tidak terjangkau: cukup dicatat.
                _logger.LogDebug(ex, "Pemeriksaan pembaruan latar belakang dilewati.");
            }
        }

        /// <summary>Daftar perbaikan dari catatan rilis GitHub (baris non-kosong).</summary>
        private static List<string> DaftarPerbaikanDariCatatan(string? catatan)
        {
            return (catatan ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(b => b.Trim().TrimStart('-', '*', '#'))
                .Where(b => b.Length > 2)
                .Take(15)
                .ToList();
        }

        private static string FormatUkuranKb(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return $"{Math.Max(1, byteCount / 1024)} KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

        /// <summary>
        /// Buka halaman terkait sebuah notifikasi lonceng (dipanggil dari flyout
        /// saat item diklik). Kunci tujuan dipetakan ke navigasi yang sama dengan
        /// tombol sidebar supaya tidak ada dua jalur ke halaman yang sama.
        /// </summary>
        public Task BukaTujuanNotifikasiAsync(NotificationItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.TujuanMenu))
            {
                return Task.CompletedTask;
            }

            return item.TujuanMenu switch
            {
                "pembaruan" => ShowPembaruanAsync(),
                "layanan-online" => ShowWaPanelAsync(),
                "pengaturan-surat" => ShowSettingsAsync(),
                "idm" => ShowIdmAsync(),
                _ => Task.CompletedTask
            };
        }

        /// <summary>Tandai menu Pembaruan bahwa ada versi baru yang menunggu dipasang.</summary>
        private void TandaiMenuPembaruan()
        {
            var dispatcher = Application.Current?.Dispatcher;
            Action update = () =>
            {
                var pembaruan = _featureNavigation.Pembaruan;
                if (pembaruan == null) return;
                if (!pembaruan.Title.Contains("(baru)", StringComparison.Ordinal))
                {
                    pembaruan.Title = "Pembaruan (baru)";
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                _ = dispatcher.BeginInvoke(update);
            }
        }

        /// <summary>Segarkan badge akun Google di statusbar (bisa dipanggil dari luar, mis. banner panel Layanan Online).</summary>
        public Task RefreshGoogleBadgePublicAsync() => RefreshGoogleBadgeAsync();

        private async Task RefreshGoogleBadgeAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drive = scope.ServiceProvider.GetRequiredService<GoogleDriveService>();
                string? email = null;
                if (drive.IsOAuthEnabled && drive.HasStoredToken())
                {
                    try
                    {
                        email = await drive.GetAccountEmailAsync(default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal memeriksa status login Google");
                    }
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                Action update = () =>
                {
                    var google = _featureNavigation.Google;
                    if (google == null) return;
                    google.Title = string.IsNullOrWhiteSpace(email)
                        ? "Login dengan Google"
                        : ShortSidebarAccount(email);
                };
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    update();
                }
                else
                {
                    _ = dispatcher.BeginInvoke(update);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memperbarui badge Login Google");
            }
        }

        private static string ShortSidebarAccount(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "Google";
            return email.Length <= 22 ? email : email.Substring(0, 21) + "…";
        }

        /// <summary>
        /// Buka halaman Panduan Awal (langkah mengisi data desa, pejabat, dan nomor
        /// surat). Bisa dibuka kapan saja dari menu Panduan Awal.
        /// </summary>
        public async Task ShowPanduanAwalAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PanduanAwalViewModel>();
                vm.PanduanDitutup += BukaBeranda;
                await vm.PeriksaAsync();
                _featureNavigation.TandaiPanduanAwal();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka panduan awal");
                await _messageService.ShowErrorAsync("Gagal membuka panduan awal: " + ex.Message);
            }
        }

        /// <summary>
        /// Dipanggil sekali setelah pengguna masuk: bila data desa/pejabat belum beres
        /// dan pengguna belum pernah menutup panduan, halaman Panduan Awal dibuka
        /// menggantikan Beranda. Kalau datanya sudah beres, panduan hanya ditandai
        /// selesai dan tidak pernah muncul mengganggu.
        /// </summary>
        /// <returns>True bila panduan benar-benar dibuka (pemanggil tidak perlu membuka Beranda).</returns>
        public async Task<bool> TampilkanPanduanAwalJikaPerluAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PanduanAwalViewModel>();
                await vm.PeriksaAsync();

                if (!vm.PerluDitampilkanSaatMulai)
                {
                    _logger.LogInformation("Panduan awal tidak perlu ditampilkan (data sudah beres atau sudah ditutup).");
                    return false;
                }

                vm.PanduanDitutup += BukaBeranda;
                _featureNavigation.TandaiPanduanAwal();
                _navigation.Navigate(vm);
                _logger.LogInformation("Panduan awal dibuka otomatis: data desa/pejabat belum lengkap.");
                return true;
            }
            catch (Exception ex)
            {
                // Panduan hanya bantuan: kegagalannya tidak boleh menghalangi aplikasi.
                _logger.LogWarning(ex, "Panduan awal tidak dapat ditampilkan otomatis.");
                return false;
            }
        }

        private async Task ShowPanduanWaAsync()
        {
            try
            {
                // Panduan WhatsApp bersifat statis: instance VM + View disimpan
                // selama aplikasi berjalan agar halaman tidak perlu dibangun ulang
                // setiap kali dibuka (XAML kompleks + parsing berat dihindarkan).
                if (_panduanWaViewModel is null)
                {
                    var sp = BuatScopeBertahan();
                    _panduanWaViewModel = sp.GetRequiredService<PanduanWaViewModel>();
                    _panduanWaView = new Views.PanduanWaView { DataContext = _panduanWaViewModel };
                }

                _navigation.Navigate(_panduanWaView!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka panduan WhatsApp");
                await _messageService.ShowErrorAsync("Gagal membuka panduan WhatsApp: " + ex.Message);
            }
        }

        private async Task ShowWaPanelAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<WaPanelViewModel>();
                vm.UnreadCountChanged += () => _ = RefreshWaBadgeAsync();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka panel Layanan Online");
                await _messageService.ShowErrorAsync("Gagal membuka panel Layanan Online: " + ex.Message);
            }
        }

        private void OnWaRequestCreated(object? sender, PermintaanWa p)
        {
            try
            {
                var namaPemohon = string.IsNullOrWhiteSpace(p.NamaWarga) ? "Warga" : p.NamaWarga;

                // Notifikasi lonceng status bar (ringan, tak mengganggu, tak
                // memblokir dengan dialog modal). Pesan multi-line:
                _notifications.Info(
                    "Permintaan Surat Baru",
                    namaPemohon + " meminta " + WaFormatParser.TampilanJenis(p.NamaJenis) +
                    ".\nKode: " + p.KodePermintaan +
                    "\nDetail lengkap di menu Layanan Online.",
                    "layanan-online");

                // Badge menu diperbarui lokal — tanpa query database ulang setiap
                // permintaan masuk. Hanya notifikasi permintaan WA yang dihitung
                // (notifikasi lain seperti backup Drive tidak ikut menciut badge).
                _waNotifUnread++;
                UpdateWaBadge(_waUnreadSynced + _waNotifUnread);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menampilkan notifikasi permintaan WA");
            }
        }

        /// <summary>Unread DB pada sinkronisasi terakhir (start / aksi panel).</summary>
        private int _waUnreadSynced;

        /// <summary>Notifikasi "Permintaan Surat Baru" sejak sinkronisasi terakhir — hanya untuk badge menu.</summary>
        private int _waNotifUnread;

        /// <summary>
        /// Query database sekali (saat start & saat panel ditutup) untuk menyinkronkan
        /// baseline unread. Pembaruan harian (permintaan masuk) cukup dari angka
        /// notifikasi — tanpa query ulang, tanpa beban DB.
        /// </summary>
        private async Task RefreshWaBadgeAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
                int unread = await repo.CountUnreadAsync(default);

                // DB adalah sumber kebenaran saat sinkronisasi: reset counter lokal.
                _waUnreadSynced = unread;
                _waNotifUnread = 0;
                UpdateWaBadge(unread);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memperbarui badge permintaan WA");
            }
        }

        private void UpdateWaBadge(int unread)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            Action update = () =>
            {
                var layanan = _featureNavigation.LayananOnline;
                if (layanan == null) return;
                layanan.Title = unread > 0
                    ? $"Layanan Online ({unread})"
                    : "Layanan Online";
            };
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                _ = dispatcher.BeginInvoke(update);
            }
        }

        private async Task ShowAboutAsync()
        {
            try
            {
                _logger.LogInformation("ShowAboutAsync called - creating scope");
                var sp = BuatScopeHalaman();
                _logger.LogInformation("ShowAboutAsync - getting AboutViewModel");
                var vm = sp.GetRequiredService<AboutViewModel>();
                _logger.LogInformation("ShowAboutAsync - creating AboutView");
                var view = new Views.AboutView { DataContext = vm };
                _logger.LogInformation("ShowAboutAsync - navigating");
                _navigation.Navigate(view);
                _logger.LogInformation("ShowAboutAsync navigation complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Tentang Aplikasi: {Message}", ex.Message);
                await _messageService.ShowErrorAsync("Gagal membuka Tentang Aplikasi: " + ex.Message);
            }
        }

        private async Task ShowCatatanRilisAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<CatatanRilisViewModel>();
                NavigasiHalaman<Views.CatatanRilisView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Catatan Rilis");
                await _messageService.ShowErrorAsync("Gagal membuka Catatan Rilis: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka form Permohonan Rekening Koran (migrasi dari WinForms
        /// Views/Tambahan/PermohonanRekeningKoran.cs) di content host utama.
        /// </summary>
        private async Task ShowRekeningKoranAsync()
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<RekeningKoranViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form Rekening Koran");
                await _messageService.ShowErrorAsync("Gagal membuka form Rekening Koran: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Template Surat: pengguna menyusun sendiri jenis surat yang
        /// belum tersedia di menu Buat Surat (wizard → pratinjau → pengisian → cetak).
        /// </summary>
        private async Task ShowTemplateSuratAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<TemplateSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Template Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Template Surat: " + ex.Message);
            }
        }

        public async Task LoadSuratAsync(string templateName)
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            var template = MapDisplayToTemplate(templateName);
            try
            {
                // Form ditampilkan langsung di content host utama (bukan modal) —
                // InputSuratHostView dirender melalui DataTemplate InputWindowViewModel.
                var vm = BuatScopeHalaman().GetRequiredService<InputWindowViewModel>();
                vm.Configure(template);
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
            }
            catch (NotSupportedException)
            {
                await _messageService.ShowInfoAsync(
                    "Jenis surat \"" + templateName + "\" belum didukung.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka input surat {Template}", template);
                await _messageService.ShowErrorAsync("Gagal membuka input surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Ringkasan info desa untuk status bar — huruf BIASA (bukan kapital semua,
        /// permintaan tampilan); dipisah dari pemuatan supaya bisa diuji unit.
        /// </summary>
        internal static string SusunInfoDesa(string namaDesa, string kecamatan, string kabupaten) =>
            "Desa " + namaDesa + "  •  Kec. " + kecamatan + "  •  Kab. " + kabupaten;

        public async Task LoadVillageInfoAsync()
        {
            try
            {
                var settings = await _settingsManager.GetSettingsAsync();
                if (settings != null && !string.IsNullOrWhiteSpace(settings.NamaDesa))
                {
                    VillageInfo = SusunInfoDesa(settings.NamaDesa, settings.Kecamatan ?? string.Empty, settings.Kabupaten ?? string.Empty);
                }

                IngatkanDataDesaContoh(settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat info desa untuk status bar.");
            }
        }

        /// <summary>
        /// Data desa bawaan aplikasi masih berupa contoh ("Nama Desa" dan sejenisnya).
        /// Selama masih contoh, pengguna diingatkan lewat lonceng notifikasi setiap kali
        /// aplikasi dibuka — notifikasi bisa diklik untuk langsung membuka Pengaturan Surat.
        /// Begitu data desa sudah diisi, notifikasi ini tidak dibuat lagi dan yang lama
        /// dibuang dari daftar (lihat PengaturanDesaDisimpan).
        /// </summary>
        private void IngatkanDataDesaContoh(SuDesApp.Data.Models.DesaData? desa)
        {
            var fieldContoh = SuDesApp.Data.Models.DesaContoh.FieldContoh(desa);
            if (fieldContoh.Count == 0)
            {
                // Sudah diisi: cukup pastikan tidak ada sisa peringatan lama.
                HapusPeringatanDataDesaContoh();
                return;
            }

            if (_sudahIngatkanDataDesaContoh) return;   // sekali per sesi saja
            _sudahIngatkanDataDesaContoh = true;

            _notifications.Warning(
                JudulNotifDataDesaContoh,
                $"Data berikut masih berisi contoh bawaan aplikasi: {SuDesApp.Data.Models.DesaContoh.RingkasField(fieldContoh)}." +
                "\nBuka Pengaturan Surat, lalu isi data desa Anda yang sebenarnya supaya surat yang dicetak tidak memakai data contoh.",
                "pengaturan-surat");
        }

        /// <summary>Buang peringatan data desa contoh dari lonceng (tidak berisik lagi).</summary>
        private void HapusPeringatanDataDesaContoh()
        {
            if (!_sudahIngatkanDataDesaContoh) return;
            _sudahIngatkanDataDesaContoh = false;

            var item = _notifications.Items.FirstOrDefault(i =>
                string.Equals(i.Title, JudulNotifDataDesaContoh, StringComparison.Ordinal));
            if (item != null)
            {
                _notifications.Hapus(item);
            }
        }

        /// <summary>
        /// Dipanggil setelah data desa disimpan di Pengaturan Surat: bila data contoh
        /// sudah diganti, peringatan pada lonceng langsung dibuang supaya tidak
        /// mengganggu lagi.
        /// </summary>
        private async Task PeriksaDataDesaSetelahSimpanAsync()
        {
            if (!_sudahIngatkanDataDesaContoh) return;

            try
            {
                var desa = await _settingsManager.GetSettingsAsync();
                if (!SuDesApp.Data.Models.DesaContoh.MasihContoh(desa))
                {
                    HapusPeringatanDataDesaContoh();
                    _logger.LogInformation("Data desa sudah disesuaikan: peringatan data contoh dibuang.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gagal memeriksa data desa setelah disimpan.");
            }
        }

        public void RefreshVillageInfo()
        {
            _ = LoadVillageInfoAsync();
        }

        private static string MapDisplayToTemplate(string display) => BuatSuratCatalog.ResolveTemplate(display);
    }
}
