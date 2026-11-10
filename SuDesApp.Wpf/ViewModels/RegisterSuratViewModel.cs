using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public class SuratDisplayModel : ObservableObject
    {
        private string _statusDisplay = string.Empty;
        private string _status = string.Empty;

        public int ID_Surat { get; set; }
        public DateTime TanggalSurat { get; set; }
        public string NomorSurat { get; set; } = string.Empty;
        public string NamaDisplay { get; set; } = string.Empty;
        public string TTLDisplay { get; set; } = string.Empty;
        public string JenisKelaminDisplay { get; set; } = string.Empty;
        public string AlamatDisplay { get; set; } = string.Empty;
        public string Keperluan { get; set; } = string.Empty;
        public string NamaJenis { get; set; } = string.Empty;

        /// <summary>Label jenis surat yang ramah baca (mis. "N1 - Surat Pengantar Nikah").</summary>
        public string JenisSuratLabel { get; set; } = string.Empty;
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }
        public string StatusDisplay
        {
            get => _statusDisplay;
            set => SetProperty(ref _statusDisplay, value);
        }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public SuratData ?OriginalSuratData { get; set; }
    }

    public class FilterItem : ObservableObject
    {
        private string _displayName = string.Empty;
        private string _filterValue = string.Empty;

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }
        public string FilterValue
        {
            get => _filterValue;
            set => SetProperty(ref _filterValue, value);
        }

        public override string ToString() => DisplayName;
        public override bool Equals(object? obj) => obj is FilterItem other && string.Equals(FilterValue, other.FilterValue, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => FilterValue?.GetHashCode() ?? 0;
    }

    public partial class RegisterSuratViewModel : ObservableObject
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly ILogger _logger;
        private readonly SettingsManager _settingsManager;
        private readonly AppConfig _appConfig;
        private readonly FileService _fileService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMessageService _messageService;
        private readonly IPeringatanDataDesaContoh? _peringatan;
        protected readonly IServiceProvider _serviceProvider;
        // Pemilik masa hidup scope halaman. Buka scope baru lewat
        // _navigation.BukaHalaman() — jangan CreateScope() lokal di sini,
        // lihat CreateRegisterForReturn().
        protected readonly NavigationService _navigation;
        private readonly Func<string, string, int?, PdfPreviewViewModel> _previewFactory;
        private readonly PdfPrintService _pdfPrintService;
        private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;
        private CancellationTokenSource? _loadCancellation;

        /// <summary>Gerbang serialisasi: Microsoft.Data.Sqlite tidak aman untuk query
        /// konkuren pada satu koneksi bersama — semua akses repository dari VM ini
        /// harus lewat lock ini.</summary>
        private readonly SemaphoreSlim _dbGate = new(1, 1);

        private const int PageSize = 50;
        private const string AllTypesFilterValue = "ALL_TYPES";
        private const string AllStatusFilterValue = "ALL_STATUS";
        private const string GroupKeteranganDesaFilterValue = "GROUP_KETERANGAN_DESA";
        private const string DisplayNameSemuaJenis = "Semua Jenis";
        private const string DisplayNameSemuaStatus = "Semua Status";
        private const string DisplayNameSuratKeteranganDesa = "Surat Keterangan Desa";

        private int _currentPage = 1;
        private int _totalRecords = 0;
        private List<SuratData> _currentDisplayData = new();
        private bool _isComboBoxInitialized = false;
        private bool _isInitializing = false;
        private int _loadSequence = 0;
        private string _sortBy = "ID_Surat";
        private string _sortTampil = "ID_Surat";
        private bool _sortAscending = false;
        private int _pendingFilterRequests = 0;
        private bool _suppressFilterEvents = false;

        /// <summary>
        /// Kabar halaman yang tampil di dalam daftar (bukan dialog popup): hasil ekspor,
        /// statistik, kegagalan cetak, dan sebab sebuah tombol tidak bisa dilanjutkan.
        /// Kartu berkunci sama saling menimpa, jadi menekan tombol yang sama dua kali
        /// hanya menyisakan kabar terakhir. Kelas turunan (Register NTCR) ikut memakainya.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        public RegisterSuratViewModel(
            IUnitOfWork unitOfWork,
            ILoggerFactory loggerFactory,
            SettingsManager settingsManager,
            AppConfig appConfig,
            FileService fileService,
            IMessageService messageService,
            IServiceProvider serviceProvider,
            NavigationService navigation,
            Func<string, string, int?, PdfPreviewViewModel> previewFactory,
            PdfPrintService pdfPrintService,
            IPeringatanDataDesaContoh? peringatan = null,
            SuDesApp.Services.PenjagaLisensi? penjaga = null)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            // Kategori log mengikuti kelas register yang sebenarnya dipakai, sehingga
            // aksi Register NTCR tidak tercatat seolah-olah Register Surat.
            _logger = _loggerFactory.CreateLogger(GetType());
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _pdfPrintService = pdfPrintService ?? throw new ArgumentNullException(nameof(pdfPrintService));
            _peringatan = peringatan;
            _penjaga = penjaga;

            SuratItems = new ObservableCollection<SuratDisplayModel>();
            JenisSuratItems = new ObservableCollection<FilterItem>();
            StatusItems = new ObservableCollection<FilterItem>();
            TahunItems = new ObservableCollection<string>();

            LoadCommand = new AsyncRelayCommand(LoadDataSafeAsync);
            PreviousPageCommand = new AsyncRelayCommand(() => HandlePageNavigationAsync(-1));
            NextPageCommand = new AsyncRelayCommand(() => HandlePageNavigationAsync(1));
            FirstPageCommand = new AsyncRelayCommand(GoToFirstPageAsync);
            LastPageCommand = new AsyncRelayCommand(GoToLastPageAsync);
            FilterChangeCommand = new AsyncRelayCommand(HandleFilterChangeAsync);
            DraftFilterCommand = new AsyncRelayCommand(ToggleDraftFilterAsync);
            SearchTextChangedCommand = new AsyncRelayCommand(HandleSearchTextChangeAsync);
            ClearSearchCommand = new RelayCommand(ClearSearch);
            DateFilterToggleCommand = new AsyncRelayCommand(HandleDateFilterToggleAsync);
            DateChangeCommand = new AsyncRelayCommand(HandleDateChangeAsync);
            ExportCommand = new AsyncRelayCommand(ExportDatabaseAsync);
            StatisticsCommand = new AsyncRelayCommand(ShowStatisticsAsync);
            PrintRegisterCommand = new AsyncRelayCommand(PrintRegisterAsync);
            ChangeStatusCommand = new AsyncRelayCommand<string>(ChangeStatusAsync);
            DoubleClickCommand = new AsyncRelayCommand<SuratDisplayModel>(OpenSuratDetailAsync);
            PreviewSuratCommand = new AsyncRelayCommand<SuratDisplayModel>(OpenSuratDetailAsync);
            PrintSuratCommand = new AsyncRelayCommand<SuratDisplayModel>(PrintSuratDirectAsync);
            EditSuratCommand = new AsyncRelayCommand<SuratDisplayModel>(EditSuratAsync);
            ExportSuratPdfCommand = new AsyncRelayCommand<SuratDisplayModel>(ExportSuratPdfAsync);
            SortCommand = new RelayCommand<string>(p => ApplySort(p));

            _ = InitializeAsync();
        }

        public ObservableCollection<SuratDisplayModel> SuratItems { get; }
        public ObservableCollection<FilterItem> JenisSuratItems { get; }
        public ObservableCollection<FilterItem> StatusItems { get; }
        public ObservableCollection<string> TahunItems { get; }

        /// <summary>
        /// Judul halaman register. Register Surat adalah register surat desa umum;
        /// <see cref="RegisterNtcrViewModel"/> punya register sendiri dengan judul
        /// dan kolomnya sendiri.
        /// </summary>
        public virtual string PageTitle => "Register Surat";

        /// <summary>Nama dasar berkas ekspor/cetak register (tanpa stamp waktu).</summary>
        protected virtual string RegisterFileBaseName => "RegisterSurat";

        /// <summary>
        /// Jenis surat yang tidak pernah tampil di register ini. Register Surat umum
        /// menyembunyikan blanko NTCR (punya register sendiri); Register NTCR
        /// mengoverride menjadi null karena seluruh isinya memang NTCR.
        /// </summary>
        protected virtual IReadOnlyList<string>? ExcludedJenisNames => SuratConstants.NtcrSemua;

        public ICommand LoadCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand FilterChangeCommand { get; }
        public ICommand DraftFilterCommand { get; }
        public ICommand SearchTextChangedCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand DateFilterToggleCommand { get; }
        public ICommand DateChangeCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand StatisticsCommand { get; }
        public ICommand PrintRegisterCommand { get; }
        public ICommand ChangeStatusCommand { get; }
        public ICommand DoubleClickCommand { get; }
        public ICommand PreviewSuratCommand { get; }
        public ICommand PrintSuratCommand { get; }
        public ICommand EditSuratCommand { get; }
        public ICommand ExportSuratPdfCommand { get; }
        public ICommand SortCommand { get; }

        private int _totalSuratCount;
        public int TotalSuratCount
        {
            get => _totalSuratCount;
            set => SetProperty(ref _totalSuratCount, value);
        }

        private int _activeSuratCount;
        public int ActiveSuratCount
        {
            get => _activeSuratCount;
            set => SetProperty(ref _activeSuratCount, value);
        }

        private int _draftSuratCount;
        public int DraftSuratCount
        {
            get => _draftSuratCount;
            set => SetProperty(ref _draftSuratCount, value);
        }

        private int _cancelledSuratCount;
        public int CancelledSuratCount
        {
            get => _cancelledSuratCount;
            set => SetProperty(ref _cancelledSuratCount, value);
        }

        private FilterItem? _selectedJenisSurat;
        public FilterItem? SelectedJenisSurat
        {
            get => _selectedJenisSurat;
            set => SetProperty(ref _selectedJenisSurat, value);
        }

        private FilterItem? _selectedStatus;
        public FilterItem? SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                if (SetProperty(ref _selectedStatus, value))
                    OnPropertyChanged(nameof(IsDraftFilterActive));
            }
        }

        private string? _selectedTahun;
        public string? SelectedTahun
        {
            get => _selectedTahun;
            set => SetProperty(ref _selectedTahun, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private SuratDisplayModel? _selectedSurat;
        public SuratDisplayModel? SelectedSurat
        {
            get => _selectedSurat;
            set => SetProperty(ref _selectedSurat, value);
        }

        private bool _isDateFilterEnabled;
        public bool IsDateFilterEnabled
        {
            get => _isDateFilterEnabled;
            set => SetProperty(ref _isDateFilterEnabled, value);
        }

        // Rentang tanggal filter diketik sebagai teks dd-mm-yyyy (bukan pemilih
        // tanggal); hasil bacaan terakhir yang sah disimpan terpisah supaya
        // penyaringan tidak memakai teks yang masih setengah diketik.
        private string _tanggalMulai = TanggalIsian.KeTeks(DateTime.Now.AddDays(-30));
        private string _tanggalSelesai = TanggalIsian.KeTeks(DateTime.Now);
        private DateTime? _tanggalMulaiSah = DateTime.Now.AddDays(-30);
        private DateTime? _tanggalSelesaiSah = DateTime.Now;

        public string TanggalMulai
        {
            get => _tanggalMulai;
            set
            {
                if (SetProperty(ref _tanggalMulai, value ?? string.Empty)) BacaRentangTanggal(out _);
            }
        }

        public string TanggalSelesai
        {
            get => _tanggalSelesai;
            set
            {
                if (SetProperty(ref _tanggalSelesai, value ?? string.Empty)) BacaRentangTanggal(out _);
            }
        }

        /// <summary>
        /// Baca kedua isian tanggal filter; isian kosong berarti batas itu tidak
        /// dipakai. Hasil bacaan yang sah disimpan agar penyaringan tetap memakai
        /// tanggal yang benar walau teksnya baru sebagian diubah.
        /// </summary>
        private bool BacaRentangTanggal(out string pesan)
        {
            var kekurangan = new List<string>();

            if (!TanggalIsian.Baca(_tanggalMulai, "Tanggal Mulai", out _tanggalMulaiSah, out string p1))
                kekurangan.Add(p1);
            if (!TanggalIsian.Baca(_tanggalSelesai, "Tanggal Selesai", out _tanggalSelesaiSah, out string p2))
                kekurangan.Add(p2);

            pesan = string.Join(Environment.NewLine, kekurangan);
            return kekurangan.Count == 0;
        }

        private string _pageInfo = "Halaman 1 dari 1 (0 total)";
        public string PageInfo
        {
            get => _pageInfo;
            set => SetProperty(ref _pageInfo, value);
        }

        private string _statusSummary = "Memuat statistik...";
        public string StatusSummary
        {
            get => _statusSummary;
            set => SetProperty(ref _statusSummary, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _isGeneratingPdf;

        /// <summary>True saat PDF surat sedang di-generate (pratinjau/cetak/ekspor) — menampilkan overlay spinner.</summary>
        public bool IsGeneratingPdf
        {
            get => _isGeneratingPdf;
            set => SetProperty(ref _isGeneratingPdf, value);
        }

        /// <summary>True bila filter status saat ini = Draft (tombol cepat di toolbar menyala).</summary>
        public bool IsDraftFilterActive =>
            string.Equals(SelectedStatus?.FilterValue, "Draft", StringComparison.OrdinalIgnoreCase);

        /// <summary>Nomor surat Draft yang belum selesai — jumlahnya = DraftSuratCount.</summary>
        public string DraftBadgeText => DraftSuratCount > 0 ? DraftSuratCount.ToString() : string.Empty;

        /// <summary>Badge hanya tampil saat ada draft yang menunggu dilengkapi.</summary>
        public bool HasDraftBadge => DraftSuratCount > 0;

        public bool CanPreviousPage => !IsLoading && _currentPage > 1;
        public bool CanNextPage => !IsLoading && _currentPage < TotalPages;
        public bool HasData => SuratItems.Count > 0;

        private int TotalPages => Math.Max(1, (int)Math.Ceiling((double)_totalRecords / PageSize));


    }
}
