using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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

        /// <summary>Glyph chip pada header accordion (default: folder). Memakai font ikon
        /// monokrom agar warnanya mengikuti tema, bukan emoji berwarna tetap.</summary>
        public string GroupIcon { get; set; } = "\uE8B7";

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

        private bool _isActive;

        /// <summary>True bila halaman item ini sedang ditampilkan (disorot di sidebar).</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }

    /// <summary>
    /// Pembungkus perintah tombol sidebar: menandai halaman sebagai aktif lebih dulu,
    /// lalu meneruskan eksekusi dan status enabled ke perintah aslinya.
    /// </summary>
    internal sealed class PenandaAktifCommand : ICommand
    {
        private readonly ICommand _asli;
        private readonly Action _saatDipilih;

        public PenandaAktifCommand(ICommand asli, Action saatDipilih)
        {
            _asli = asli ?? throw new ArgumentNullException(nameof(asli));
            _saatDipilih = saatDipilih ?? throw new ArgumentNullException(nameof(saatDipilih));
            _asli.CanExecuteChanged += (_, e) => CanExecuteChanged?.Invoke(this, e);
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _asli.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            _saatDipilih();
            _asli.Execute(parameter);
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
        private NavItem? _pembaruanButton;
        private DaftarHadirViewModel? _daftarHadirViewModel;
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

            _navigation.CurrentViewChanged += view =>
            {
                CurrentView = view;
                PantauJudulHalaman(view);
            };
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

            // Pembaruan: laporkan hasil tambalan yang baru dipasang (bila ada), lalu
            // periksa versi terbaru di latar belakang. Pemeriksaan ini sengaja tidak
            // memblokir aplikasi dan langsung memberi tahu APA yang diperbaiki.
            _ = LaporkanHasilTambalanAsync();
            _ = PeriksaPembaruanLatarAsync();

            // Notifikasi sambutan sekali per sesi — sekaligus menandakan
            // lonceng notifikasi aktif di status bar.
            _notifications.Info(
                "Notifikasi aktif",
                $"Selamat datang, {LoginViewModel.CurrentUserName}. Pemberitahuan aplikasi akan muncul di sini.");
        }

        public ObservableCollection<NavItem> MenuItems { get; } = new();

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

        /// <summary>Tombol Beranda (halaman pembuka) — dipakai untuk menandainya aktif saat startup.</summary>
        private NavItem? _berandaButton;

        /// <summary>Semua tombol navigasi sidebar (tanpa akordeon/section) untuk penanda halaman aktif.</summary>
        private readonly List<NavItem> _semuaTombolNav = new();

        private string _halamanAktif = "Beranda";

        /// <summary>Nama menu terakhir yang diklik — cadangan bila halaman tidak punya judul sendiri.</summary>
        private string _judulDasarHalaman = "Beranda";

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
            HalamanAktif = string.IsNullOrWhiteSpace(judul) ? _judulDasarHalaman : judul!;
        }

        private bool _sidebarCiut;

        /// <summary>
        /// True bila sidebar dalam mode ciut (hanya ikon). Template sidebar memakai
        /// nilai ini untuk menyembunyikan label dan memusatkan ikon.
        /// </summary>
        public bool SidebarCiut
        {
            get => _sidebarCiut;
            private set
            {
                if (SetProperty(ref _sidebarCiut, value))
                {
                    OnPropertyChanged(nameof(SidebarTerbuka));
                }
            }
        }

        /// <summary>Kebalikan <see cref="SidebarCiut"/> — memudahkan binding Visibility.</summary>
        public bool SidebarTerbuka => !_sidebarCiut;

        /// <summary>Ubah mode sidebar (dipanggil jendela saat tombol ciut/lebar diklik).</summary>
        public void SetSidebarCiut(bool ciut) => SidebarCiut = ciut;

        private void BuildSidebar()
        {
            // Beranda paling atas: halaman ringkasan yang juga menjadi tampilan
            // pertama setelah login (area konten tidak kosong lagi).
            _berandaButton = NavButton("Beranda", "\uE80F", () => { _ = ShowBerandaAsync(); });
            _berandaButton.Description = "Ringkasan surat, pintasan cepat, dan aktivitas terbaru";
            MenuItems.Add(_berandaButton);

            // Susunan disesuaikan permintaan (berbeda urutan dari WinForms Utama.cs):
            // Register Surat paling atas, lalu Buat Surat Baru, lalu Surat Peraturan
            // (gabungan SK/Peraturan + Surat Masuk + Surat Keluar).
            MenuItems.Add(new NavItem { Title = "BUAT SURAT", IsSectionHeader = true });

            MenuItems.Add(NavButton("Register Surat", "\uE8F1", () => { _ = ShowRegisterSuratAsync(); }));

            var buatSurat = new NavItem { Title = "Buat Surat Baru", Icon = "\uE710", GroupIcon = "\uE710", IsAccordion = true, IsExpanded = false };
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

            // Template Surat (buat sendiri) sengaja menjadi item tersendiri, tepat
            // di atas Daftar Hadir.
            MenuItems.Add(NavButton("Template Surat (buat sendiri)", "\uE8A5", () => { _ = ShowTemplateSuratAsync(); }));

            // Daftar Hadir: halaman cetak daftar hadir, sengaja berdiri sendiri
            // tepat di atas Surat Peraturan.
            MenuItems.Add(NavButton("Daftar Hadir", "\uE716", () => { _ = ShowDaftarHadirAsync(); }));

            // Surat Peraturan: akordeon SK/Keputusan + Perdes + Perkades
            // (Surat Masuk & Surat Keluar dipindah ke menu Surat Masuk/Keluar di bawah).
            var suratPeraturan = new NavItem { Title = "Surat Peraturan", Icon = "\uE7C3", GroupIcon = "\uE7C3", IsAccordion = true, IsExpanded = false };
            suratPeraturan.Children.Add(new NavItem { Title = "SK / Keputusan", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("SK"); }) });
            suratPeraturan.Children.Add(new NavItem { Title = "Perdes", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("PERDES"); }) });
            suratPeraturan.Children.Add(new NavItem { Title = "Perkades", Icon = "\u2022", Action = NewLambda(() => { _ = ShowKeputusanAsync("PERKADES"); }) });
            MenuItems.Add(suratPeraturan);

            // Surat Masuk/Keluar: akordeon terpisah di bawah Surat Peraturan.
            var suratMasukKeluar = new NavItem { Title = "Surat Masuk/Keluar", Icon = "\uE896", GroupIcon = "\uE896", IsAccordion = true, IsExpanded = false };
            suratMasukKeluar.Children.Add(new NavItem { Title = "Surat Masuk", Icon = "\u2022", Action = NewLambda(() => { _ = ShowAgendaAsync("MASUK"); }) });
            suratMasukKeluar.Children.Add(new NavItem { Title = "Surat Keluar", Icon = "\u2022", Action = NewLambda(() => { _ = ShowAgendaAsync("KELUAR"); }) });
            MenuItems.Add(suratMasukKeluar);

            // NTCR — formulir persyaratan pernikahan Model N1–N6 sesuai Keputusan
            // Dirjen Bimas Islam No. 473 Tahun 2020. Ada dua alur: paket pernikahan
            // (sekali isi data satu pasangan lalu seluruh blanko dicetak dalam satu
            // berkas) DAN blanko perorangan (membuat satu blanko N1–N6 atau N8 saja).
            // Model N7 sudah dihapus dari aplikasi karena diterbitkan KUA, bukan
            // kantor desa.
            var ntcr = new NavItem { Title = "NTCR", Icon = "\uE77B", GroupIcon = "\uE77B", IsAccordion = true, IsExpanded = false };
            ntcr.Children.Add(new NavItem
            {
                Title = "Paket N1–N6 (satu pasangan)",
                Icon = "\uE8F1",
                Action = NewLambda(() => { _ = ShowNtcrPaketAsync(); }),
                Description = "Sekali isi data satu pasangan: seluruh blanko yang dicentang dibuat & dicetak dalam satu berkas PDF."
            });
            // Blanko perorangan: buat satu jenis surat NTCR saja, tanpa paket. Judul
            // diambil dari NtcrKatalog supaya seragam dengan menu dan cetakan.
            foreach (var blanko in NtcrKatalog.Semua)
            {
                var namaJenis = blanko.NamaJenis;
                ntcr.Children.Add(new NavItem
                {
                    Title = blanko.Judul,
                    Icon = "\u2022",
                    Action = NewLambda(() => { _ = LoadSuratAsync(namaJenis); }),
                    Description = $"{blanko.Kode} — {blanko.Keterangan}"
                });
            }
            MenuItems.Add(ntcr);

            // Register NTCR terpisah dari Register Surat umum.
            MenuItems.Add(NavButton("Register NTCR", "\uE8F1", () => { _ = ShowRegisterNtcrAsync(); }));
            MenuItems.Add(NavButton("Riwayat Aktivitas", "\uE81C", () => { _ = ShowRiwayatAsync(); }));

            _permintaanOnlineButton = NavButton("Layanan Online", "\uE774", () => { _ = ShowWaPanelAsync(); });
            MenuItems.Add(_permintaanOnlineButton);

            _formulirHeader = new NavItem { Title = "FORMULIR", IsSectionHeader = true };
            MenuItems.Add(_formulirHeader);
            _formulirAccordion = new NavItem { Title = "Formulir", Icon = "\uE8A5", GroupIcon = "\uE8A5", IsAccordion = true, IsExpanded = false };
            MenuItems.Add(_formulirAccordion);
            RefreshFormulirSection();

            MenuItems.Add(new NavItem { Title = "PENGATURAN", IsSectionHeader = true });
            MenuItems.Add(NavButton("Pengaturan Aplikasi", "\uE713", () => { _ = ShowPengaturanAplikasiAsync(); }));
            MenuItems.Add(NavButton("Pengaturan Surat", "\uE713", () => { _ = ShowSettingsAsync(); }));
            MenuItems.Add(NavButton("Pengaturan Formulir", "\uE713", () => { _ = ShowFormulirAsync(); }));
            MenuItems.Add(NavButton("Ubah Kata Sandi", "\uE72E", () => { _ = ShowUbahSandiAsync(); }));
            _googleNavButton = NavButton("Login dengan Google", "\uE77B", () => { _ = ShowGoogleLoginAsync(); });
            MenuItems.Add(_googleNavButton);

            var pencadanganDb = new NavItem { Title = "Pencadangan Database", Icon = "\uE74E", GroupIcon = "\uE74E", IsAccordion = true, IsExpanded = false };
            pencadanganDb.Children.Add(new NavItem { Title = "Ekspor Database", Icon = "\u2022", Action = NewLambda(() => { _ = ShowExImdbAsync(false); }) });
            pencadanganDb.Children.Add(new NavItem { Title = "Impor Database", Icon = "\u2022", Action = NewLambda(() => { _ = ShowExImdbAsync(true); }) });
            MenuItems.Add(pencadanganDb);

            MenuItems.Add(new NavItem { Title = "BANTUAN", IsSectionHeader = true });

            var temaAccordion = new NavItem { Title = "Tema", Icon = "\uE790", GroupIcon = "\uE790", IsAccordion = true, IsExpanded = false };
            // Daftar tema dibaca langsung dari ThemeService (selalu sinkron dengan
            // GetAvailableThemes). Tema default ditampilkan paling atas agar mudah
            // dipilih lagi setelah pengguna berpindah tema.
            var availableThemes = ThemeService.GetAvailableThemes();
            var orderedThemes = new string[availableThemes.Length];
            int themeIndex = 0;
            orderedThemes[themeIndex++] = ThemeService.DefaultTheme;
            foreach (var th in availableThemes)
            {
                if (th != ThemeService.DefaultTheme)
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
                    Icon = theme == ThemeService.DefaultTheme ? "★" : "•",
                    Action = NewLambda(() => _themeService.Apply(theme))
                });
            }
            MenuItems.Add(temaAccordion);

            // Mode akordeon eksklusif untuk seluruh sidebar: membuka satu akordeon
            // (Buat Surat Baru, Surat Peraturan, Formulir, Pencadangan Database,
            // Tema, dll.) otomatis menutup akordeon lain yang sedang terbuka.
            NavItem.RegisterExclusiveAccordions(MenuItems.Where(i => i.IsAccordion));

            _pembaruanButton = NavButton("Pembaruan", "\uE896", () => _ = ShowPembaruanAsync());
            MenuItems.Add(_pembaruanButton);
            MenuItems.Add(NavButton("Panduan WhatsApp", "\uE8F1", () => { _ = ShowPanduanWaAsync(); }));
            MenuItems.Add(NavButton("Catatan Rilis", "\uE7C3", () => { _ = ShowCatatanRilisAsync(); }));
            MenuItems.Add(NavButton("Tentang", "\uE946", () => { _ = ShowAboutAsync(); }));
            MenuItems.Add(NavButton("Keluar", "\uE711", () => ExitRequested?.Invoke()));

            // Terakhir: pasang penanda halaman aktif pada semua tombol navigasi.
            PasangPenandaHalamanAktif(MenuItems);
        }

        /// <summary>
        /// Bungkus perintah setiap tombol navigasi agar halaman yang dibuka tersorot di
        /// sidebar dan namanya muncul di title bar. Berlaku juga untuk anak akordeon.
        /// </summary>
        private void PasangPenandaHalamanAktif(IEnumerable<NavItem> items)
        {
            foreach (var item in items)
            {
                if (item.Action is not null && !item.IsAccordion)
                {
                    if (!_semuaTombolNav.Contains(item))
                    {
                        _semuaTombolNav.Add(item);
                    }

                    if (item.Action is not PenandaAktifCommand)
                    {
                        var asli = item.Action;
                        var target = item;
                        item.Action = new PenandaAktifCommand(asli, () => SetHalamanAktif(target));
                    }
                }

                if (item.Children.Count > 0)
                {
                    PasangPenandaHalamanAktif(item.Children);
                }
            }
        }

        /// <summary>Tandai satu item sidebar sebagai halaman aktif dan perbarui title bar.</summary>
        private void SetHalamanAktif(NavItem? item)
        {
            foreach (var lain in _semuaTombolNav)
            {
                lain.IsActive = ReferenceEquals(lain, item);
            }

            _judulDasarHalaman = string.IsNullOrWhiteSpace(item?.Title) ? "Beranda" : item!.Title;
            HalamanAktif = _judulDasarHalaman;
        }

        /// <summary>Buka halaman Beranda (dipakai saat startup oleh MainWindow).</summary>
        public void BukaBeranda()
        {
            SetHalamanAktif(_berandaButton);
            _ = ShowBerandaAsync();
        }

        private async Task ShowBerandaAsync()
        {
            try
            {
                var vm = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<BerandaViewModel>();
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
                    Icon = "\u2022",
                    Action = new AsyncRelayCommand(async () => await ShowFormulirAsync())
                });
                return;
            }

            foreach (var template in templates)
            {
                var item = FormulirTemplateButton(template);
                _formulirAccordion.Children.Add(item);
            }

            // Item formulir dibuat ulang setiap refresh — ikut dipasangi penanda aktif.
            PasangPenandaHalamanAktif(_formulirAccordion.Children);
        }

        private NavItem FormulirTemplateButton(string templateName)
        {
            string display = templateName.Replace("_", " ").ToUpperInvariant();
            return NavButton(display, "\uE8A5", () => { _ = ShowFormulirPdfAsync(templateName); });
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

        /// <summary>
        /// Buka alur paket NTCR: satu form untuk seluruh blanko N1–N6, disimpan
        /// sekaligus dan dicetak menjadi satu berkas PDF gabungan.
        /// </summary>
        private async Task ShowNtcrPaketAsync()
        {
            try
            {
                var sp = _scopeFactory.CreateScope().ServiceProvider;
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
                var sp = _scopeFactory.CreateScope().ServiceProvider;
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
                // Daftar Hadir bersifat "template sesi": instance VM disimpan selama
                // aplikasi berjalan sehingga isian terakhir (setelah cetak PDF) tetap
                // tampil saat menu dibuka lagi. Keluar aplikasi → instance hilang dan
                // menu kembali ke isian default.
                if (_daftarHadirViewModel is null)
                {
                    var scope = _scopeFactory.CreateScope();
                    _daftarHadirViewModel = scope.ServiceProvider.GetRequiredService<DaftarHadirViewModel>();
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
                _ => Task.CompletedTask
            };
        }

        /// <summary>Tandai menu Pembaruan bahwa ada versi baru yang menunggu dipasang.</summary>
        private void TandaiMenuPembaruan()
        {
            var dispatcher = Application.Current?.Dispatcher;
            Action update = () =>
            {
                if (_pembaruanButton == null) return;
                if (!_pembaruanButton.Title.Contains("(baru)", StringComparison.Ordinal))
                {
                    _pembaruanButton.Title = "Pembaruan (baru)";
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

        /// <summary>
        /// Buka halaman Template Surat: pengguna menyusun sendiri jenis surat yang
        /// belum tersedia di menu Buat Surat (wizard → pratinjau → pengisian → cetak).
        /// </summary>
        private async Task ShowTemplateSuratAsync()
        {
            try
            {
                var scope = _scopeFactory.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<TemplateSuratViewModel>();
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
            // Surat keterangan numpang nikah (numpang kawin) — N8.
            "NUMPANG NIKAH" => SuratConstants.NTCR_N8,
            "BEDA NAMA" => SuratConstants.BEDANAMA,
            "KENAL LAHIR" => SuratConstants.KENAL_LAHIR,
            "AHLI WARIS" => SuratConstants.AHLI_WARIS,
            "IJIN TINGGAL" => SuratConstants.IJIN_TINGGAL,
            _ => display
        };
    }
}
