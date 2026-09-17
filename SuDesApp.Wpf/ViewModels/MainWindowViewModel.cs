using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public class NavItem : ObservableObject
    {
        private string _title = string.Empty;
        private bool _isExpanded;

        public NavItem()
        {
            // Accordion eksklusif: saat item ini di-expand, semua akordeon lain
            // otomatis ditutup — hanya satu terbuka pada satu waktu. Menutup
            // sibling tidak memicu rekursi karena handler hanya bereaksi pada nilai true.
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsExpanded) && _isExpanded)
                {
                    foreach (var sibling in _allAccordions)
                    {
                        if (!ReferenceEquals(sibling, this) && sibling.IsExpanded)
                        {
                            sibling.IsExpanded = false;
                        }
                    }
                }
            };
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        public string Icon { get; set; } = "●";
        public string Description { get; set; } = string.Empty;
        public ObservableCollection<NavItem> Children { get; } = new();

        /// <summary>Ikon emoji/teks yang ditampilkan di samping judul pada template tombol.</summary>
        public string IconGlyph
        {
            get => Icon;
            set => Icon = value;
        }

        /// <summary>True jika item adalah label section (header, bukan tombol/accordion).</summary>
        public bool IsSectionHeader { get; set; }

        /// <summary>True jika item adalah accordion yang dapat di-expand (memiliki Children).</summary>
        public bool IsAccordion { get; set; }

        public bool HasChildren => Children.Count > 0;

        /// <summary>
        /// Daftar bersama seluruh akordeon sidebar. Dihubungkan sekali di BuildSidebar
        /// melalui RegisterExclusiveAccordions agar perilaku eksklusif dapat dibagi
        /// antar item tanpa membuat dependensi antar-instance yang tersembunyi.
        /// </summary>
        private static readonly List<NavItem> _allAccordions = new();

        /// <summary>
        /// Aktifkan mode akordeon eksklusif: membuka satu akordeon otomatis menutup
        /// semua akordeon lain (hanya satu terbuka pada satu waktu).
        /// </summary>
        public static void RegisterExclusiveAccordions(IEnumerable<NavItem> accordions)
        {
            _allAccordions.Clear();
            _allAccordions.AddRange(accordions);
        }

        public ICommand? Action { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    public class MainWindowViewModel : ObservableObject
    {
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly SettingsManager _settingsManager;
        private readonly FormulirMenuService _formulirMenuService;
        private readonly WaEngine _waEngine;
        private readonly ThemeService _themeService;
        private readonly NotificationService _notifications;
        private readonly WaAutoProcessor _waAutoProcessor;
        private NavItem? _formulirHeader;
        private NavItem? _formulirAccordion;
        private NavItem? _permintaanOnlineButton;
        private NavItem? _googleNavButton;
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
            WaAutoProcessor waAutoProcessor)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _formulirMenuService = formulirMenuService ?? throw new ArgumentNullException(nameof(formulirMenuService));
            _waEngine = waEngine ?? throw new ArgumentNullException(nameof(waEngine));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _waAutoProcessor = waAutoProcessor ?? throw new ArgumentNullException(nameof(waAutoProcessor));

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
                if (dispatcher is null || dispatcher.CheckAccess())
                {
                    RefreshVillageInfo();
                }
                else
                {
                    dispatcher.BeginInvoke(RefreshVillageInfo);
                }
            };

            _formulirMenuService.TemplatesChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    RefreshFormulirSection();
                    return;
                }
                if (dispatcher.CheckAccess())
                {
                    RefreshFormulirSection();
                }
                else
                {
                    dispatcher.BeginInvoke(RefreshFormulirSection);
                }
            };

            _navigation.CurrentViewChanged += view => CurrentView = view;
            ShowRegisterCommand = new AsyncRelayCommand(ShowRegisterSuratAsync);
            ShowSettingsCommand = new AsyncRelayCommand(ShowSettingsAsync);
            ShowFormulirCommand = new AsyncRelayCommand(ShowFormulirAsync);
            ShowUbahSandiCommand = new AsyncRelayCommand(ShowUbahSandiAsync);
            ShowAboutCommand = new AsyncRelayCommand(ShowAboutAsync);
            ShowCatatanRilisCommand = new AsyncRelayCommand(ShowCatatanRilisAsync);
            BuildSidebar();
            _waEngine.RequestCreated += _waAutoProcessor.HandleRequestCreated;
            _waEngine.RequestCreated += OnWaRequestCreated;

            // Jam layanan: engine bertanya status buka lewat query ini dan
            // auto-processor mengejar permintaan tertunda saat jam layanan dibuka.
            _waEngine.ServiceHoursOpenQuery = () =>
                WaServiceHours.IsOpen(DateTime.Now,
                    AppPreferenceStore.GetWaServiceOpen(), AppPreferenceStore.GetWaServiceClose(),
                    AppPreferenceStore.GetWaServiceDays());
            _waEngine.ServiceHoursOpened += _waAutoProcessor.HandleServiceHoursOpened;

            // Status gateway WA di statusbar: cek awal + polling ringan tiap
            // 10 detik agar perubahan preferensi (ganti gateway/isi token)
            // terpantau tanpa perlu mekanisme event antar halaman.
            RefreshWaGatewayStatus();
            RefreshWaSheetStatus();
            var waStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            waStatusTimer.Tick += (_, _) =>
            {
                RefreshWaGatewayStatus();
                RefreshWaSheetStatus();
            };
            waStatusTimer.Start();
            _waEngine.StartPolling();
            _ = RefreshWaBadgeAsync();
            _ = RefreshGoogleBadgeAsync();
            _ = LoadVillageInfoAsync();

            // Notifikasi sambutan sekali per sesi — sekaligus menandakan
            // lonceng notifikasi aktif di status bar.
            _notifications.Info(
                "Notifikasi aktif",
                $"Selamat datang, {LoginViewModel.CurrentUserName}. Pemberitahuan aplikasi akan muncul di sini.");
        }

        public ObservableCollection<NavItem> MenuItems { get; } = new();

        /// <summary>Pusat notifikasi lonceng status bar (ala Visual Studio).</summary>
        public NotificationService Notifications => _notifications;

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
        public AsyncRelayCommand ShowUbahSandiCommand { get; }
        public AsyncRelayCommand ShowAboutCommand { get; }
        public AsyncRelayCommand ShowCatatanRilisCommand { get; }

        /// <summary>Diminta saat pengguna memilih "Keluar" dari sidebar. MainWindow menutup window (satu konfirmasi).</summary>
        public event Action? ExitRequested;

        private void BuildSidebar()
        {
            // Susunan disesuaikan permintaan (berbeda urutan dari WinForms Utama.cs):
            // Register Surat paling atas, lalu Buat Surat Baru, lalu Surat Peraturan
            // (gabungan SK/Peraturan + Surat Masuk + Surat Keluar).
            MenuItems.Add(new NavItem { Title = "BUAT SURAT", IsSectionHeader = true });

            MenuItems.Add(NavButton("Register Surat", "\uD83D\uDCCB", () => { _ = ShowRegisterSuratAsync(); }));

            var buatSurat = new NavItem { Title = "Buat Surat Baru", Icon = "+", IsAccordion = true, IsExpanded = false };
            buatSurat.Children.Add(new NavItem { Title = "SKD Umum", Icon = "\u2022", Action = NewSurat("SKD UMUM") });
            buatSurat.Children.Add(new NavItem { Title = "Domisili Warga", Icon = "\u2022", Action = NewSurat("DOMISILI WARGA") });
            buatSurat.Children.Add(new NavItem { Title = "Domisili Instansi", Icon = "\u2022", Action = NewSurat("DOMISILI INSTANSI") });
            buatSurat.Children.Add(new NavItem { Title = "SKU", Icon = "\u2022", Action = NewSurat("SKU") });
            buatSurat.Children.Add(new NavItem { Title = "Pengantar SKCK", Icon = "\u2022", Action = NewSurat("PENGANTAR SKCK") });
            buatSurat.Children.Add(new NavItem { Title = "Izin Suami / Orang Tua", Icon = "\u2022", Action = NewSurat("IZIN ORTU / SUAMI") });
            buatSurat.Children.Add(new NavItem { Title = "SKTM", Icon = "\u2022", Action = NewSurat("SKTM") });
            buatSurat.Children.Add(new NavItem { Title = "Garapan Sawah", Icon = "\u2022", Action = NewSurat("GARAPAN SAWAH") });
            buatSurat.Children.Add(new NavItem { Title = "Kematian", Icon = "\u2022", Action = NewSurat("KEMATIAN") });
            buatSurat.Children.Add(new NavItem { Title = "Beda Nama", Icon = "\u2022", Action = NewSurat("BEDA NAMA") });
            buatSurat.Children.Add(new NavItem { Title = "Kenal Lahir", Icon = "\u2022", Action = NewSurat("KENAL LAHIR") });
            buatSurat.Children.Add(new NavItem { Title = "Tinggal Sementara", Icon = "\u2022", Action = NewSurat("IJIN TINGGAL") });
            buatSurat.Children.Add(new NavItem { Title = "Ahli Waris", Icon = "\u2022", Action = NewSurat("AHLI WARIS") });
            buatSurat.Children.Add(new NavItem { Title = "Permohonan Rekening Koran", Icon = "\u2022", Action = NewLambda(() => { _ = ShowRekeningKoranAsync(); }) });
            MenuItems.Add(buatSurat);

            // Daftar Hadir: halaman cetak daftar hadir, sengaja berdiri sendiri
            // tepat di atas Surat Peraturan.
            MenuItems.Add(NavButton("Daftar Hadir", "\uD83D\uDCCB", () => { _ = ShowDaftarHadirAsync(); }));

            // Surat Peraturan: akordeon SK/Keputusan + Perdes + Perkades
            // (Surat Masuk & Surat Keluar dipindah ke menu Surat Masuk/Keluar di bawah).
            var suratPeraturan = new NavItem { Title = "Surat Peraturan", Icon = "+", IsAccordion = true, IsExpanded = false };
            suratPeraturan.Children.Add(new NavItem { Title = "SK / Keputusan", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("SK"); }) });
            suratPeraturan.Children.Add(new NavItem { Title = "Perdes", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("PERDES"); }) });
            suratPeraturan.Children.Add(new NavItem { Title = "Perkades", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("PERKADES"); }) });
            MenuItems.Add(suratPeraturan);

            // Surat Masuk/Keluar: akordeon terpisah di bawah Surat Peraturan.
            var suratMasukKeluar = new NavItem { Title = "Surat Masuk/Keluar", Icon = "+", IsAccordion = true, IsExpanded = false };
            suratMasukKeluar.Children.Add(new NavItem { Title = "Surat Masuk", Icon = "\u2022", Action = NewLambda(() => { _ = ShowAgendaAsync("MASUK"); }) });
            suratMasukKeluar.Children.Add(new NavItem { Title = "Surat Keluar", Icon = "\u2022", Action = NewLambda(() => { _ = ShowAgendaAsync("KELUAR"); }) });
            MenuItems.Add(suratMasukKeluar);

            // NTCR (persyaratan pendaftaran pernikahan N1-N4)
            var ntcr = new NavItem { Title = "NTCR", Icon = "+", IsAccordion = true, IsExpanded = false };
            ntcr.Children.Add(new NavItem { Title = "Surat Pengantar Nikah (N1)", Icon = "\u2022", Action = NewSurat("NTCR N1") });
            ntcr.Children.Add(new NavItem { Title = "Surat Keterangan Nikah (N2)", Icon = "\u2022", Action = NewSurat("NTCR N2") });
            ntcr.Children.Add(new NavItem { Title = "Persetujuan Calon Mempelai (N3)", Icon = "\u2022", Action = NewSurat("NTCR N3") });
            ntcr.Children.Add(new NavItem { Title = "Keterangan Orang Tua (N4)", Icon = "\u2022", Action = NewSurat("NTCR N4") });
            MenuItems.Add(ntcr);

            // Register NTCR terpisah dari Register Surat umum.
            MenuItems.Add(NavButton("Register NTCR", "\uD83D\uDCCB", () => { _ = ShowRegisterNtcrAsync(); }));
            MenuItems.Add(NavButton("Riwayat Aktivitas", "\uD83E\uDDFE", () => { _ = ShowRiwayatAsync(); }));

            _permintaanOnlineButton = NavButton("Layanan Online", "\uD83D\uDCAC", () => { _ = ShowWaPanelAsync(); });
            MenuItems.Add(_permintaanOnlineButton);

            _formulirHeader = new NavItem { Title = "FORMULIR", IsSectionHeader = true };
            MenuItems.Add(_formulirHeader);
            _formulirAccordion = new NavItem { Title = "Formulir", Icon = "+", IsAccordion = true, IsExpanded = false };
            MenuItems.Add(_formulirAccordion);
            RefreshFormulirSection();

            MenuItems.Add(new NavItem { Title = "PENGATURAN", IsSectionHeader = true });
            MenuItems.Add(NavButton("Pengaturan Aplikasi", "\uD83D\uDDA5\uFE0F", () => { _ = ShowPengaturanAplikasiAsync(); }));
            MenuItems.Add(NavButton("Pengaturan Surat", "\u2699\uFE0F", () => { _ = ShowSettingsAsync(); }));
            MenuItems.Add(NavButton("Pengaturan Formulir", "\u2699\uFE0F", () => { _ = ShowFormulirAsync(); }));
            MenuItems.Add(NavButton("Ubah Kata Sandi", "\uD83D\uDD10", () => { _ = ShowUbahSandiAsync(); }));
            _googleNavButton = NavButton("Login dengan Google", "\uD83D\uDD11", () => { _ = ShowGoogleLoginAsync(); });
            MenuItems.Add(_googleNavButton);

            var pencadanganDb = new NavItem { Title = "Pencadangan Database", Icon = "+", IsAccordion = true, IsExpanded = false };
            pencadanganDb.Children.Add(new NavItem { Title = "Ekspor Database", Icon = "\u2022", Action = NewLambda(() => { _ = ShowExImdbAsync(false); }) });
            pencadanganDb.Children.Add(new NavItem { Title = "Impor Database", Icon = "\u2022", Action = NewLambda(() => { _ = ShowExImdbAsync(true); }) });
            MenuItems.Add(pencadanganDb);

            MenuItems.Add(new NavItem { Title = "BANTUAN", IsSectionHeader = true });

            var temaAccordion = new NavItem { Title = "Tema", Icon = "+", IsAccordion = true, IsExpanded = false };
            // Daftar tema dibaca langsung dari ThemeService (selalu sinkron dengan
            // GetAvailableThemes). Emerald (=Modern, tema default) ditampilkan paling
            // atas agar mudah dipilih lagi setelah pengguna berpindah tema.
            var availableThemes = ThemeService.GetAvailableThemes();
            var orderedThemes = new string[availableThemes.Length];
            int themeIndex = 0;
            orderedThemes[themeIndex++] = ThemeService.Emerald;
            foreach (var th in availableThemes)
            {
                if (th != ThemeService.Emerald)
                {
                    orderedThemes[themeIndex++] = th;
                }
            }
            foreach (var theme in orderedThemes)
            {
                string display = ThemeService.GetDisplayName(theme);
                temaAccordion.Children.Add(new NavItem
                {
                    Title = display,
                    Icon = theme == ThemeService.Emerald ? "★" : "•",
                    Action = NewLambda(() => _themeService.Apply(theme))
                });
            }
            MenuItems.Add(temaAccordion);

            // Mode akordeon eksklusif untuk seluruh sidebar: membuka satu akordeon
            // (Buat Surat Baru, Surat Peraturan, Formulir, Pencadangan Database,
            // Tema, dll.) otomatis menutup akordeon lain yang sedang terbuka.
            NavItem.RegisterExclusiveAccordions(MenuItems.Where(i => i.IsAccordion));

            MenuItems.Add(NavButton("Pembaruan", "\uD83D\uDD04", () => _ = ShowPembaruanAsync()));
            MenuItems.Add(NavButton("Panduan WhatsApp", "\uD83D\uDCD6", () => { _ = ShowPanduanWaAsync(); }));
            MenuItems.Add(NavButton("Catatan Rilis", "\uD83D\uDCCB", () => { _ = ShowCatatanRilisAsync(); }));
            MenuItems.Add(NavButton("Tentang", "\u2139\uFE0F", () => { _ = ShowAboutAsync(); }));
            MenuItems.Add(NavButton("Keluar", "\uD83D\uDEAA", () => ExitRequested?.Invoke()));
        }

        private static NavItem NavButton(string title, string icon, Action onClick) => new()
        {
            Title = title,
            Icon = icon,
            Action = new AsyncRelayCommand(async () => { onClick(); await Task.CompletedTask; })
        };

        /// <summary>
        /// Isi ulang accordion/dropdown FORMULIR: satu child per template PDF dari
        /// folder Templates; diklik membuka pratinjau (padanan
        /// UtamaService.PopulateFormulirPanel pada WinForms).
        /// </summary>
        private void RefreshFormulirSection()
        {
            if (_formulirAccordion is null)
            {
                return;
            }

            _formulirAccordion.Children.Clear();

            var templates = _formulirMenuService.GetTemplates();
            if (templates.Count == 0)
            {
                _formulirAccordion.Children.Add(new NavItem
                {
                    Title = "Tambah Template",
                    Icon = "📁",
                    Action = new AsyncRelayCommand(async () => await ShowFormulirAsync())
                });
                return;
            }

            foreach (var template in templates)
            {
                var item = FormulirTemplateButton(template);
                _formulirAccordion.Children.Add(item);
            }
        }

        private NavItem FormulirTemplateButton(string templateName)
        {
            string display = templateName.Replace("_", " ").ToUpperInvariant();
            return NavButton(display, "\uD83D\uDCC4", () => { _ = ShowFormulirPdfAsync(templateName); });
        }

        private AsyncRelayCommand NewSurat(string display) => new(async () => await LoadSuratAsync(display));

        /// <summary>Bungkus aksi sinkron menjadi AsyncRelayCommand yang valid.</summary>
        private static AsyncRelayCommand NewLambda(Action action) => new(async () => { action(); await Task.CompletedTask; });

        private void NavigateTodo(string title, string description)
        {
            try
            {
                _navigation.Navigate(new TodoViewModel { Title = title, Description = description });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal navigasi ke halaman {Title}", title);
            }
        }
        private async Task ShowRegisterSuratAsync()
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<RegisterSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Register Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Register Surat: " + ex.Message);
            }
        }

        private async Task ShowPengaturanAplikasiAsync()
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<PengaturanAplikasiViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Aplikasi");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Aplikasi: " + ex.Message);
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
                var vm = _scopeFactory.CreateScope()
                    .ServiceProvider.GetRequiredService<PengaturanAplikasiViewModel>();
                vm.SetFocusSection(sectionKey);
                _navigation.Navigate(vm);
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
        /// Navigasi ke view/ViewModel tertentu dari luar ViewModel ini (mis.
        /// banner panel Layanan Online yang mengarahkan ke halaman Login Google).
        /// </summary>
        public void NavigateToView(object view) => _navigation.Navigate(view);

        /// <summary>Klik chip WA di statusbar → Pengaturan Aplikasi, kartu Gateway WhatsApp.</summary>
        public AsyncRelayCommand WaChipClickCommand { get; }

        /// <summary>Klik chip Sheet di statusbar → Pengaturan Aplikasi, kartu Google Sheet.</summary>
        public AsyncRelayCommand SheetChipClickCommand { get; }

        private async Task ShowRiwayatAsync()
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<RiwayatViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Riwayat Aktivitas");
                await _messageService.ShowErrorAsync("Gagal membuka Riwayat Aktivitas: " + ex.Message);
            }
        }

        private async Task ShowRegisterNtcrAsync()
        {
            try
            {
                var sp = _scopeFactory.CreateScope().ServiceProvider;
                var vm = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<RegisterSuratViewModel>(sp, true);
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
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<SetelanViewModel>();
                var view = new Views.SetelanView { DataContext = vm };
                _navigation.Navigate(view);
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
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<FormulirViewModel>();
                var view = new Views.FormulirView { DataContext = vm };
                _navigation.Navigate(view);
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
                var scoped = _scopeFactory.CreateScope().ServiceProvider;
                var vm = scoped.GetRequiredService<Func<string, FormulirPdfViewModel>>()(templateName);
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
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<AgendaSuratViewModel>();
                await vm.SetJenisAsync(jenisSurat);
                _navigation.Navigate(vm);
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
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<DaftarHadirViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
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
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KeputusanViewModel>();
                await vm.SetJenisAsync(jenisKeputusan);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku SK/Peraturan {Jenis}", jenisKeputusan);
                await _messageService.ShowErrorAsync("Gagal membuka buku SK/Peraturan: " + ex.Message);
            }
        }

        private async Task ShowExImdbAsync(bool isImport)
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ExImdbViewModel>();
                vm.SetMode(isImport);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Ekspor/Impor Database");
                await _messageService.ShowErrorAsync("Gagal membuka Ekspor/Impor Database: " + ex.Message);
            }
        }

        private async Task ShowGoogleLoginAsync()
        {
            try
            {
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<GoogleLoginViewModel>();
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
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<PembaruanViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pembaruan");
                await _messageService.ShowErrorAsync("Gagal membuka Pembaruan: " + ex.Message);
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
                    if (_googleNavButton == null) return;
                    _googleNavButton.Title = string.IsNullOrWhiteSpace(email)
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

        private async Task ShowPanduanWaAsync()
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<PanduanWaViewModel>();
                _navigation.Navigate(vm);
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
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<WaPanelViewModel>();
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
                    "\nDetail lengkap di menu Layanan Online.");

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
                if (_permintaanOnlineButton == null) return;
                _permintaanOnlineButton.Title = unread > 0
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

        private async Task ShowUbahSandiAsync()
        {
            _logger.LogInformation("ShowUbahSandiAsync called");
            try
            {
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<UbahSandiViewModel>();
                var view = new Views.UbahSandiView { DataContext = vm };
                _navigation.Navigate(view);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Ubah Sandi");
                await _messageService.ShowErrorAsync("Gagal membuka Ubah Sandi: " + ex.Message);
            }
        }

        private async Task ShowAboutAsync()
        {
            try
            {
                _logger.LogInformation("ShowAboutAsync called - creating scope");
                var scope = _scopeFactory.CreateScope();
                _logger.LogInformation("ShowAboutAsync - getting AboutViewModel");
                var vm = scope.ServiceProvider.GetRequiredService<AboutViewModel>();
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
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<CatatanRilisViewModel>();
                _navigation.Navigate(vm);
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
            try
            {
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<RekeningKoranViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form Rekening Koran");
                await _messageService.ShowErrorAsync("Gagal membuka form Rekening Koran: " + ex.Message);
            }
        }

        public async Task LoadSuratAsync(string templateName)
        {
            var template = MapDisplayToTemplate(templateName);
            try
            {
                // Form ditampilkan langsung di content host utama (bukan modal) —
                // InputSuratHostView dirender melalui DataTemplate InputWindowViewModel.
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<InputWindowViewModel>();
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

        public async Task LoadVillageInfoAsync()
        {
            try
            {
                var settings = await _settingsManager.GetSettingsAsync();
                if (settings != null && !string.IsNullOrWhiteSpace(settings.NamaDesa))
                {
                    VillageInfo = "DESA " + settings.NamaDesa + "  •  KEC. " + settings.Kecamatan + "  •  KAB. " + settings.Kabupaten;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat info desa untuk status bar.");
            }
        }

        public void RefreshVillageInfo()
        {
            _ = LoadVillageInfoAsync();
        }

        private static string MapDisplayToTemplate(string display) => display.ToUpperInvariant() switch
        {
            "SKD UMUM" => SuratConstants.SKD_UMUM,
            "DOMISILI WARGA" => SuratConstants.DOMISILI_WARGA,
            "DOMISILI INSTANSI" => SuratConstants.INSTANSI,
            "SKU" => SuratConstants.SKU,
            "PENGANTAR SKCK" => SuratConstants.PENGANTAR_SKCK,
            "IZIN ORTU / SUAMI" => SuratConstants.IZIN_ORTU,
            "SKTM" => SuratConstants.SKTM,
            "GARAPAN SAWAH" => SuratConstants.GARAPAN_SAWAH,
            "KEMATIAN" => SuratConstants.KEMATIAN,
            "BEDA NAMA" => SuratConstants.BEDANAMA,
            "KENAL LAHIR" => SuratConstants.KENAL_LAHIR,
            "AHLI WARIS" => SuratConstants.AHLI_WARIS,
            "IJIN TINGGAL" => SuratConstants.IJIN_TINGGAL,
            "NTCR N1" => SuratConstants.NTCR_N1,
            "NTCR N2" => SuratConstants.NTCR_N2,
            "NTCR N3" => SuratConstants.NTCR_N3,
            "NTCR N4" => SuratConstants.NTCR_N4,
            _ => display
        };
    }
}
