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
using OfficeOpenXml;
using OfficeOpenXml.Style;
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
        private string ?_statusDisplay;
        private string ?_status;

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
        private string ?_displayName;
        private string ?_filterValue;

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
        public override bool Equals(object obj) => obj is FilterItem other && string.Equals(FilterValue, other.FilterValue, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => FilterValue?.GetHashCode() ?? 0;
    }

    public class RegisterSuratViewModel : ObservableObject
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly ILogger _logger;
        private readonly SettingsManager _settingsManager;
        private readonly AppConfig _appConfig;
        private readonly FileService _fileService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMessageService _messageService;
        protected readonly IServiceProvider _serviceProvider;
        private readonly NavigationService _navigation;
        private readonly Func<string, string, int?, PdfPreviewViewModel> _previewFactory;
        private readonly PdfPrintService _pdfPrintService;
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
        private bool _sortAscending = false;
        private int _pendingFilterRequests = 0;
        private bool _suppressFilterEvents = false;

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
            PdfPrintService pdfPrintService)
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

        private FilterItem _selectedJenisSurat;
        public FilterItem SelectedJenisSurat
        {
            get => _selectedJenisSurat;
            set => SetProperty(ref _selectedJenisSurat, value);
        }

        private FilterItem _selectedStatus;
        public FilterItem SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                if (SetProperty(ref _selectedStatus, value))
                    OnPropertyChanged(nameof(IsDraftFilterActive));
            }
        }

        private string _selectedTahun;
        public string SelectedTahun
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

        private DateTime _tanggalMulai = DateTime.Now.AddDays(-30);
        public DateTime TanggalMulai
        {
            get => _tanggalMulai;
            set => SetProperty(ref _tanggalMulai, value);
        }

        private DateTime _tanggalSelesai = DateTime.Now;
        public DateTime TanggalSelesai
        {
            get => _tanggalSelesai;
            set => SetProperty(ref _tanggalSelesai, value);
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

        private async Task InitializeAsync()
        {
            _isInitializing = true;
            try
            {
                _logger.LogInformation("Loading RegisterSurat data...");
                await Task.WhenAll(
                    LoadJenisSuratKeComboBoxAsync(),
                    LoadStatusKeComboBoxAsync(),
                    LoadTahunKeComboBoxAsync()
                );
                _isComboBoxInitialized = true;
                await LoadDataSafeAsync();
                await LoadStatusSummaryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize RegisterSurat");
            }
            finally
            {
                // Event filter dari view (SelectionChanged/TextChanged yang terpicu
                // binding awal) ditunda selama inisialisasi, lalu di-flush SEKALI di sini —
                // data dimuat sekali per gugus perubahan, bukan tumpukan konkuren.
                _isInitializing = false;
                await FlushPendingFilterChangeAsync();
            }
        }

        private async Task LoadJenisSuratKeComboBoxAsync()
        {
            try
            {
                _logger.LogInformation("Loading jenis surat to ComboBox...");
                JenisSuratItems.Clear();

                foreach (var item in await BuildJenisFilterItemsAsync())
                {
                    JenisSuratItems.Add(item);
                }

                SelectedJenisSurat = JenisSuratItems.FirstOrDefault();
                _logger.LogInformation("Loaded {Count} jenis surat", JenisSuratItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load jenis surat");
            }
        }

        /// <summary>
        /// Susun pilihan filter jenis surat untuk register ini. Register Surat umum
        /// memuat semua jenis surat desa kecuali blanko NTCR; Register NTCR
        /// mengoverride-nya dengan daftar blanko N1–N8 saja.
        /// </summary>
        protected virtual async Task<List<FilterItem>> BuildJenisFilterItemsAsync()
        {
            var items = new List<FilterItem>
            {
                new FilterItem { DisplayName = DisplayNameSemuaJenis, FilterValue = AllTypesFilterValue },
                new FilterItem { DisplayName = DisplayNameSuratKeteranganDesa, FilterValue = GroupKeteranganDesaFilterValue }
            };

            var allJenis = await _unitOfWork.JenisSuratRepository.GetAllJenisSuratAsync();
            var allDisplayNames = await _unitOfWork.JenisSuratRepository.GetJenisSuratDisplayNamesAsync();

            var ntcrSet = new HashSet<string>(SuratConstants.NtcrSemua, StringComparer.OrdinalIgnoreCase);

            foreach (var jenis in allJenis.Where(j => j?.NamaJenis != null && !ntcrSet.Contains(j.NamaJenis)))
            {
                var displayName = allDisplayNames.TryGetValue(jenis.NamaJenis!, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(jenis.NamaJenis.Replace("_", " ").ToLower());
                items.Add(new FilterItem { DisplayName = displayName, FilterValue = jenis.NamaJenis.ToUpperInvariant() });
            }

            return items;
        }

        private async Task LoadStatusKeComboBoxAsync()
        {
            try
            {
                _logger.LogInformation("Loading status to ComboBox...");
                StatusItems.Clear();

                var items = new List<FilterItem>
                {
                    new FilterItem { DisplayName = DisplayNameSemuaStatus, FilterValue = AllStatusFilterValue }
                };

                foreach (var status in SuratConstants.ValidStatus)
                {
                    items.Add(new FilterItem
                    {
                        DisplayName = GetStatusDisplayName(status),
                        FilterValue = status
                    });
                }

                foreach (var item in items)
                    StatusItems.Add(item);

                SelectedStatus = StatusItems.FirstOrDefault();
                _logger.LogInformation("Loaded {Count} status items", StatusItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load status");
            }
        }

        private static string GetStatusDisplayName(string status) => status switch
        {
            "Draft" => "Draft",
            "Active" => "Aktif",
            "Cancelled" => "Dibatalkan",
            _ => status
        };

        private async Task LoadTahunKeComboBoxAsync()
        {
            try
            {
                var years = await _unitOfWork.JenisSuratRepository.GetAvailableYearsAsync();
                TahunItems.Clear();
                TahunItems.Add("Semua Tahun");
                foreach (var year in years)
                    TahunItems.Add(year);

                var currentYear = DateTime.Now.Year.ToString();
                SelectedTahun = TahunItems.Contains(currentYear) ? currentYear : "Semua Tahun";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tahun");
                TahunItems.Clear();
                TahunItems.Add("Semua Tahun");
                TahunItems.Add(DateTime.Now.Year.ToString());
                SelectedTahun = "Semua Tahun";
            }
        }

        private async Task LoadDataSafeAsync()
        {
            CancelCurrentLoad();
            _loadCancellation = new CancellationTokenSource();
            int seq = ++_loadSequence;

            try
            {
                await LoadDataAsync(_loadCancellation.Token, seq);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Data load cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load data");
                // Hanya muat kosong bila masih load terbaru (latest-wins).
                if (seq == _loadSequence)
                {
                    UpdateUIWithEmptyData();
                }
            }
        }

        private async Task LoadDataAsync(CancellationToken cancellationToken, int seq)
        {
            _logger.LogInformation("Loading data page {Page}...", _currentPage);
            IsLoading = true;
            UpdateCommandStates();

            try
            {
                if (_unitOfWork.SuratRepository == null)
                {
                    _logger.LogError("SuratRepository is null");
                    return;
                }

                var filters = BuildFilterConditions();
                _logger.LogDebug("Filters: {@Filters}", filters);

                // PENTING: query dijalankan BERURUTAN + di bawah _dbGate.
                // Microsoft.Data.Sqlite tidak thread-safe untuk query konkuren
                // pada satu koneksi bersama — konkurensi memicu korupsi native
                // (crash 0x800703E9 stack overflow di luar kode user).
                List<SuratData> data;
                int total;
                await _dbGate.WaitAsync(cancellationToken);
                try
                {
                    data = (await _unitOfWork.SuratRepository.GetFilteredAsync(
                        filters,
                        sortBy: _sortBy,
                        ascending: _sortAscending,
                        skip: (_currentPage - 1) * PageSize,
                        take: PageSize,
                        cancellationToken: cancellationToken)).ToList();

                    total = await _unitOfWork.SuratRepository.CountAsync(filters, cancellationToken);
                }
                finally
                {
                    _dbGate.Release();
                }

                // Buang hasil basi bila sudah ada load yang lebih baru (latest-wins).
                if (seq != _loadSequence)
                {
                    _logger.LogDebug("Discarding stale load #{Seq} (current #{Current})", seq, _loadSequence);
                    return;
                }

                _currentDisplayData = data;
                _totalRecords = total;

                _logger.LogDebug("Loaded {Count} records, total {Total}", _currentDisplayData.Count, _totalRecords);

                UpdateDataGridViewAndPaging();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data");
                if (seq == _loadSequence)
                {
                    UpdateUIWithEmptyData();
                }
            }
            finally
            {
                if (seq == _loadSequence)
                {
                    IsLoading = false;
                    UpdateCommandStates();
                }
            }
        }

        private FilterConditions BuildFilterConditions()
        {
            var filters = new FilterConditions();

            try
            {
                var selectedJenis = SelectedJenisSurat;
                if (selectedJenis?.FilterValue != null && selectedJenis.FilterValue != AllTypesFilterValue)
                {
                    filters.JenisSurat = selectedJenis.FilterValue;
                }

                // Register Surat umum memisahkan NTCR: jenis NTCR tidak tampil di
                // daftar umum (di Register NTCR justru hanya NTCR yang tampil).
                var excluded = ExcludedJenisNames;
                if (excluded != null && excluded.Count > 0)
                {
                    filters.ExcludeJenisNames = new List<string>(excluded);
                }

                var selectedStatus = SelectedStatus;
                if (selectedStatus?.FilterValue != null && selectedStatus.FilterValue != AllStatusFilterValue)
                {
                    filters.Status = selectedStatus.FilterValue;
                }

                if (!string.IsNullOrWhiteSpace(SelectedTahun) && SelectedTahun != "Semua Tahun")
                {
                    filters.Tahun = SelectedTahun;
                }

                if (IsDateFilterEnabled)
                {
                    filters.TanggalMulai = TanggalMulai;
                    filters.TanggalSelesai = TanggalSelesai;
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    filters.SearchText = SearchText.Trim();
                }

                return filters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build filters");
                return new FilterConditions();
            }
        }

        private void UpdateDataGridViewAndPaging()
        {
            SuratItems.Clear();
            foreach (var surat in _currentDisplayData)
            {
                var statusValue = string.IsNullOrWhiteSpace(surat.Status) ? "Draft" : surat.Status;
                var model = NewDisplayModel();
                model.ID_Surat = surat.ID_Surat;
                model.TanggalSurat = surat.TanggalSurat;
                model.NomorSurat = surat.NomorSurat ?? "-";
                model.NamaDisplay = GetNamaDisplay(surat);
                model.TTLDisplay = GetTTLDisplay(surat);
                model.JenisKelaminDisplay = GetJenisKelaminDisplay(surat);
                model.AlamatDisplay = GetAlamatDisplay(surat);
                model.Keperluan = surat.Keperluan ?? "-";
                model.NamaJenis = surat.NamaJenis ?? "-";
                model.JenisSuratLabel = GetJenisSuratLabel(surat);
                model.Status = statusValue;
                model.StatusDisplay = GetStatusDisplayName(statusValue);
                model.CreatedAt = surat.CreatedAt == default ? DateTime.Now : surat.CreatedAt;
                model.UpdatedAt = surat.UpdatedAt;
                model.OriginalSuratData = surat;

                // Register turunan (mis. Register NTCR) menambah kolomnya sendiri.
                FillDisplayModelExtras(model, surat);

                SuratItems.Add(model);
            }

            UpdatePagingControls();
        }

        /// <summary>Buat model baris register. Register NTCR memakai modelnya sendiri.</summary>
        protected virtual SuratDisplayModel NewDisplayModel() => new SuratDisplayModel();

        /// <summary>
        /// Isi kolom tambahan yang hanya ada di register turunan. Register Surat umum
        /// tidak menambahkan apa pun.
        /// </summary>
        protected virtual void FillDisplayModelExtras(SuratDisplayModel model, SuratData surat)
        {
        }

        /// <summary>
        /// Register yang dibuka kembali setelah form edit ditutup — Register Surat
        /// kembali ke Register Surat, Register NTCR kembali ke Register NTCR.
        /// </summary>
        protected virtual object CreateRegisterForReturn() =>
            _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<RegisterSuratViewModel>();

        private void UpdatePagingControls()
        {
            PageInfo = $"Halaman {_currentPage} dari {TotalPages} ({_totalRecords} total)";
            OnPropertyChanged(nameof(CanPreviousPage));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(HasData));
        }

        private void UpdateUIWithEmptyData()
        {
            _currentDisplayData = new List<SuratData>();
            _totalRecords = 0;
            UpdateDataGridViewAndPaging();
        }

        private void CancelCurrentLoad()
        {
            try
            {
                if (_loadCancellation != null && !_loadCancellation.IsCancellationRequested)
                    _loadCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("CancellationTokenSource already disposed");
            }
            finally
            {
                _loadCancellation?.Dispose();
                _loadCancellation = null;
            }
        }

        private async Task HandlePageNavigationAsync(int pageChange)
        {
            var newPage = _currentPage + pageChange;
            if (newPage < 1) return;
            _currentPage = newPage;
            await LoadDataSafeAsync();
        }

        /// <summary>
        /// Perubahan filter (jenis/status/tahun/tanggal) masuk lewat sini.
        /// Saat inisialisasi berjalan (binding awal menyalakan SelectionChanged),
        /// permintaan ditunda dan di-flush SEKALI setelah inisialisasi — sehingga
        /// data hanya dimuat sekali per gugus perubahan filter, bukan berulang kali.
        /// </summary>
        private async Task HandleFilterChangeAsync()
        {
            if (_suppressFilterEvents) return; // perubahan programatik (mis. toggle Draft) sudah memuat sendiri
            if (_isInitializing)
            {
                _pendingFilterRequests++;
                return;
            }
            if (!_isComboBoxInitialized) return;

            if (_pendingFilterRequests > 0)
            {
                // Ada permintaan yang tertunda dari masa inisialisasi: buang sekarang,
                // state filter yang aktif di properti sudah final.
                _pendingFilterRequests = 0;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        /// <summary>Dipanggil di akhir inisialisasi: flush TUNDA-an jadi SATU reload.
        /// Bila tidak ada permintaan tertunda, tidak ada reload tambahan sama sekali.</summary>
        private async Task FlushPendingFilterChangeAsync()
        {
            if (_pendingFilterRequests == 0) return;
            _pendingFilterRequests = 0;
            _logger.LogDebug("Flushing pending filter change(s) as one reload");
            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        /// <summary>
        /// Tombol cepat Draft di toolbar: aktifkan filter status Draft bila belum aktif,
        /// atau kembalikan ke Semua Status bila sedang aktif (toggle).
        /// </summary>
        private async Task ToggleDraftFilterAsync()
        {
            if (_isInitializing)
            {
                _pendingFilterRequests++;
                return;
            }
            if (!_isComboBoxInitialized) return;            // Programatik: tahan event SelectionChanged dari ComboBox agar tidak
            // terjadi dobel load (toggle sudah memuat sendiri di bawah).
            _suppressFilterEvents = true;
            try
            {
                SelectedStatus = IsDraftFilterActive!
                    ? StatusItems.FirstOrDefault(i => i.FilterValue == AllStatusFilterValue)
                    : StatusItems.FirstOrDefault(i => string.Equals(i.FilterValue, "Draft", StringComparison.OrdinalIgnoreCase))
                      ?? StatusItems.FirstOrDefault();
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        private async Task HandleSearchTextChangeAsync()
        {
            if (_isInitializing) return;
            CancelCurrentLoad();
            _loadCancellation = new CancellationTokenSource();

            try
            {
                await Task.Delay(500, _loadCancellation.Token);
                _currentPage = 1;
                await LoadDataAsync(_loadCancellation.Token, ++_loadSequence);
                await LoadStatusSummaryAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Search operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during search");
            }
        }

        private async Task HandleDateFilterToggleAsync()
        {
            if (_isComboBoxInitialized && !_isInitializing)
            {
                _currentPage = 1;
                await LoadDataSafeAsync();
            }
        }

        private async Task HandleDateChangeAsync()
        {
            if (!_isComboBoxInitialized || _isInitializing || !IsDateFilterEnabled) return;
            if (TanggalMulai > TanggalSelesai)
            {
                await _messageService.ShowWarningAsync("Tanggal mulai tidak boleh lebih baru dari tanggal selesai.");
                return;
            }
            _currentPage = 1;
            await LoadDataSafeAsync();
        }

        private async Task LoadStatusSummaryAsync()
        {
            try
            {
                var filters = BuildFilterConditions();
                var counts = new Dictionary<string, int>();

                // Serialisasi via _dbGate: hindari query konkuren pada koneksi bersama.
                await _dbGate.WaitAsync();
                try
                {
                    foreach (var status in SuratConstants.ValidStatus)
                    {
                        var statusFilter = new FilterConditions
                        {
                            JenisSurat = filters.JenisSurat,
                            ExcludeJenisNames = filters.ExcludeJenisNames,
                            Tahun = filters.Tahun,
                            SearchText = filters.SearchText,
                            TanggalMulai = filters.TanggalMulai,
                            TanggalSelesai = filters.TanggalSelesai,
                            Status = status
                        };
                        counts[status] = await _unitOfWork.SuratRepository.CountAsync(statusFilter);
                    }
                }
                finally
                {
                    _dbGate.Release();
                }

                ActiveSuratCount = counts.GetValueOrDefault("Active", 0);
                DraftSuratCount = counts.GetValueOrDefault("Draft", 0);
                CancelledSuratCount = counts.GetValueOrDefault("Cancelled", 0);
                TotalSuratCount = ActiveSuratCount + DraftSuratCount + CancelledSuratCount;
                OnPropertyChanged(nameof(DraftBadgeText));
                OnPropertyChanged(nameof(HasDraftBadge));

                if (counts.Any())
                {
                    var summaryText = string.Join(" | ", counts.Select(kvp => $"{GetStatusDisplayName(kvp.Key)}: {kvp.Value}"));
                    StatusSummary = $"Status: {summaryText} | Total: {TotalSuratCount}";
                }
                else
                {
                    StatusSummary = "Tidak ada data statistik status.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load status summary");
                StatusSummary = "Gagal memuat statistik status.";
            }
        }

        private async Task ExportDatabaseAsync()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = $"Ekspor Data {PageTitle}",
                    Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv|JSON Data (*.json)|*.json",
                    FileName = $"{RegisterFileBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                    DefaultExt = ".xlsx"
                };

                if (sfd.ShowDialog() != true) return;

                IsLoading = true;
                var filter = BuildFilterConditions();
                var exportData = (await _unitOfWork.SuratRepository.GetFilteredAsync(filter, "TanggalSurat", false, 0, 50000))?.ToList() ?? new List<SuratData>();

                if (!exportData.Any())
                {
                    await _messageService.ShowInfoAsync("Tidak ada data yang sesuai filter untuk diekspor.");
                    return;
                }

                var extension = Path.GetExtension(sfd.FileName).ToLowerInvariant();

                if (extension == ".xlsx")
                {
                    ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");
                    using var package = new ExcelPackage();
                    var ws = package.Workbook.Worksheets.Add(PageTitle);

                    string[] headers = { "No", "ID Surat", "Tanggal Surat", "Nomor Surat", "Nama Pemohon / Instansi", "TTL", "JK", "Alamat", "Keperluan", "Jenis Surat", "Status", "Waktu Dibuat" };
                    for (int col = 0; col < headers.Length; col++)
                    {
                        var cell = ws.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(37, 99, 235));
                        cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    }
                    ws.Row(1).Height = 26;

                    int rowIdx = 2;
                    int num = 1;
                    foreach (var s in exportData)
                    {
                        ws.Cells[rowIdx, 1].Value = num++;
                        ws.Cells[rowIdx, 2].Value = s.ID_Surat;
                        ws.Cells[rowIdx, 3].Value = s.TanggalSurat.ToString("dd-MM-yyyy");
                        ws.Cells[rowIdx, 4].Value = s.NomorSurat ?? "-";
                        ws.Cells[rowIdx, 5].Value = GetNamaDisplay(s);
                        ws.Cells[rowIdx, 6].Value = GetTTLDisplay(s);
                        ws.Cells[rowIdx, 7].Value = GetJenisKelaminDisplay(s);
                        ws.Cells[rowIdx, 8].Value = GetAlamatDisplay(s);
                        ws.Cells[rowIdx, 9].Value = s.Keperluan ?? "-";
                        ws.Cells[rowIdx, 10].Value = s.NamaJenis ?? "-";
                        ws.Cells[rowIdx, 11].Value = GetStatusDisplayName(s.Status ?? "Draft");
                        ws.Cells[rowIdx, 12].Value = s.CreatedAt.ToString("dd-MM-yyyy HH:mm");

                        if (rowIdx % 2 == 1)
                        {
                            ws.Cells[rowIdx, 1, rowIdx, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            ws.Cells[rowIdx, 1, rowIdx, headers.Length].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                        }

                        rowIdx++;
                    }

                    ws.Cells[1, 1, rowIdx - 1, headers.Length].AutoFitColumns(10, 45);
                    await package.SaveAsAsync(new FileInfo(sfd.FileName));
                }
                else if (extension == ".json")
                {
                    var jsonItems = exportData.Select((s, i) => new
                    {
                        No = i + 1,
                        s.ID_Surat,
                        TanggalSurat = s.TanggalSurat.ToString("yyyy-MM-dd"),
                        s.NomorSurat,
                        NamaLengkap = GetNamaDisplay(s),
                        TTL = GetTTLDisplay(s),
                        JenisKelamin = GetJenisKelaminDisplay(s),
                        Alamat = GetAlamatDisplay(s),
                        s.Keperluan,
                        s.NamaJenis,
                        Status = GetStatusDisplayName(s.Status ?? "Draft"),
                        CreatedAt = s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                    var jsonStr = System.Text.Json.JsonSerializer.Serialize(jsonItems, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(sfd.FileName, jsonStr);
                }
                else
                {
                    using var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8);
                    await sw.WriteLineAsync("No,ID_Surat,TanggalSurat,NomorSurat,NamaLengkap,TTL,JK,Alamat,Keperluan,JenisSurat,Status,CreatedAt");
                    int i = 1;
                    foreach (var s in exportData)
                    {
                        string Escape(string? val) => $"\"{(val ?? "-").Replace("\"", "\"\"")}\"";
                        await sw.WriteLineAsync(string.Join(",",
                            i++,
                            s.ID_Surat,
                            Escape(s.TanggalSurat.ToString("dd-MM-yyyy")),
                            Escape(s.NomorSurat),
                            Escape(GetNamaDisplay(s)),
                            Escape(GetTTLDisplay(s)),
                            Escape(GetJenisKelaminDisplay(s)),
                            Escape(GetAlamatDisplay(s)),
                            Escape(s.Keperluan),
                            Escape(s.NamaJenis),
                            Escape(GetStatusDisplayName(s.Status ?? "Draft")),
                            Escape(s.CreatedAt.ToString("dd-MM-yyyy HH:mm"))));
                    }
                }

                await _messageService.ShowInfoAsync($"Berhasil mengekspor {exportData.Count} surat ke file:\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor register surat");
                await _messageService.ShowErrorAsync($"Gagal mengekspor data: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowStatisticsAsync()
        {
            try
            {
                var stats = await _unitOfWork.SuratRepository.GetDatabaseStatsAsync();
                if (stats == null || stats.Count == 0)
                {
                    await _messageService.ShowInfoAsync("Tidak ada data statistik.");
                    return;
                }

                var lines = stats.Select(kvp => $"{kvp.Key}: {kvp.Value}");
                await _messageService.ShowInfoAsync(string.Join(Environment.NewLine, lines));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show statistics");
                await _messageService.ShowErrorAsync($"Gagal memuat statistik: {ex.Message}");
            }
        }

        private async Task PrintRegisterAsync()
        {
            try
            {
                IsLoading = true;
                var filter = BuildFilterConditions();
                var allFilteredData = await _unitOfWork.SuratRepository.GetFilteredAsync(filter, "TanggalSurat", false, 0, 2000);
                var suratList = allFilteredData?.ToList() ?? new List<SuratData>();
                if (!suratList.Any())
                {
                    await _messageService.ShowInfoAsync("Tidak ada data surat yang cocok untuk dicetak.");
                    return;
                }

                var generator = _serviceProvider.GetRequiredService<SuDesApp.GeneratorPdf.SuratRegisterGenerator>();
                var tempFolder = _appConfig.TempPdfFolder;
                Directory.CreateDirectory(tempFolder);
                var tempPdfPath = Path.Combine(tempFolder, $"{RegisterFileBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                using (var fs = File.Create(tempPdfPath))
                {
                    await generator.GenerateRegisterPdfAsync(fs, suratList);
                }

                // Cetak menjadi PDF: navigasi ke halaman pratinjau PDF agar pengguna
                // dapat menyimpan (Ekspor PDF) atau mencetaknya dari pratinjau.
                var scoped = _serviceProvider.CreateScope();
                var vmFactory = scoped.ServiceProvider.GetRequiredService<Func<string, string, int?, PdfPreviewViewModel>>();
                var previewVm = vmFactory($"Buku {PageTitle} ({suratList.Count} Dokumen)", tempPdfPath, null);
                _navigation.Navigate(new PdfPreviewView { DataContext = previewVm });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to print register");
                await _messageService.ShowErrorAsync($"Gagal mencetak register: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Terapkan klik sortir kolom pada register; kembalikan arah sortir terbaru
        /// (true = menaik). Dipanggil dari event Sorting DataGrid.
        /// </summary>
        public bool ApplySort(string? memberPath)
        {
            var repoColumn = MapSortColumn(memberPath);
            if (_sortBy == repoColumn)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortBy = repoColumn;
                _sortAscending = true;
            }

            _ = LoadDataSafeAsync();
            return _sortAscending;
        }

        public bool IsSortedDescending => !_sortAscending;

        /// <summary>Petakan nama properti tampilan ke kolom sortir repositori (whitelist SQL).</summary>
        private static string MapSortColumn(string? memberPath) => memberPath switch
        {
            "ID_Surat" => "ID_Surat",
            "TanggalSurat" => "Tanggal",
            "NomorSurat" => "NomorSurat",
            "NamaDisplay" => "Nama",
            "NamaJenis" => "JenisSurat",
            "Status" => "Status",
            "CreatedAt" => "CreatedAt",
            _ => "ID_Surat"
        };

        private void ClearSearch()
        {
            SearchText = string.Empty;
            _ = HandleFilterChangeAsync();
        }

        private async Task GoToFirstPageAsync()
        {
            if (_currentPage == 1) return;
            _currentPage = 1;
            await LoadDataSafeAsync();
        }

        private async Task GoToLastPageAsync()
        {
            if (_currentPage == TotalPages) return;
            _currentPage = TotalPages;
            await LoadDataSafeAsync();
        }

        private async Task ChangeStatusAsync(string newStatus)
        {
            var target = SelectedSurat;
            if (target == null)
            {
                await _messageService.ShowWarningAsync("Pilih surat yang akan diubah statusnya terlebih dahulu.");
                return;
            }

            if (string.Equals(target.Status, newStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(target.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await _messageService.ShowWarningAsync("Surat yang sudah dibatalkan tidak dapat diubah statusnya.");
                return;
            }

            try
            {
                var ok = await _unitOfWork.SuratRepository.UpdateStatusAsync(target.ID_Surat, newStatus);
                if (ok)
                {
                    target.Status = newStatus;
                    target.StatusDisplay = GetStatusDisplayName(newStatus);
                    target.OriginalSuratData.Status = newStatus;
                    _ = LoadDataSafeAsync();
                    _ = LoadStatusSummaryAsync();
                }
                else
                {
                    await _messageService.ShowErrorAsync("Gagal mengubah status surat.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change status for surat ID={Id}", target.ID_Surat);
                await _messageService.ShowErrorAsync($"Gagal mengubah status: {ex.Message}");
            }
        }

        /// <summary>
        /// Cetak langsung surat ini ke printer: PDF dibuat ke temp lalu dikirim ke
        /// dialog cetak Windows (padanan alur cetak WinForms) — tanpa pratinjau.
        /// </summary>
        private async Task PrintSuratDirectAsync(SuratDisplayModel? model)
        {
            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await _messageService.ShowWarningAsync(
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan dan tidak dapat dicetak.");
                return;
            }

            try
            {
                IsLoading = true;
                IsGeneratingPdf = true;

                var suratData = model.OriginalSuratData;

                // Baris daftar register tidak me-load relasi (warga, rincian garapan, dst.) —
                // muat ulang lengkap dari DB agar PDF yang dicetak berisi data utuh.
                var fullForPrint = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (fullForPrint != null) suratData = fullForPrint;

                var pdfPath = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (pdfPath == null || !File.Exists(pdfPath))
                {
                    await _messageService.ShowWarningAsync(
                        $"PDF surat tidak dapat dibuat untuk jenis '{suratData.NamaJenis ?? "?"}'. " +
                        "Detail penyebab ada di log aplikasi (error.log).");
                    return;
                }

                var printed = await _pdfPrintService.PrintPdfFileAsync(
                    pdfPath, $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}");

                if (printed)
                {
                    await _messageService.ShowInfoAsync(
                        $"Surat nomor {model.NomorSurat} terkirim ke printer.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencetak surat #{Id}", model.ID_Surat);
                await _messageService.ShowErrorAsync($"Gagal mencetak surat: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }

        private async Task OpenSuratDetailAsync(SuratDisplayModel model)
        {
            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await _messageService.ShowWarningAsync(
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan dan tidak dapat dibuka.");
                return;
            }

            try
            {
                IsLoading = true;
                IsGeneratingPdf = true;

                _logger.LogInformation("Opening document for surat ID={ID}, Jenis={Jenis}",
                    model.OriginalSuratData.ID_Surat, model.OriginalSuratData.NamaJenis);

                var suratData = model.OriginalSuratData;

                // Baris daftar register tidak me-load relasi (warga, rincian garapan, dst.) —
                // muat ulang lengkap dari DB agar PDF tidak berisi data kosong.
                var full = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (full != null) suratData = full;

                // Surat instansi dan surat dari Template Surat tidak mewakili perorangan
                // (isinya disimpan sendiri oleh suratnya), jadi tidak perlu data warga.
                if (!string.Equals(suratData.NamaJenis, "INSTANSI", StringComparison.OrdinalIgnoreCase) &&
                    !TemplateSuratTercatat.DariTemplateSurat(suratData) &&
                    suratData.Warga == null)
                {
                    await _messageService.ShowWarningAsync("Data warga tidak ditemukan untuk surat ini.");
                    return;
                }

                var pdfPath = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (pdfPath == null)
                {
                    await _messageService.ShowWarningAsync(
                        $"PDF surat tidak dapat dibuat untuk jenis '{suratData.NamaJenis ?? "?"}'. " +
                        "Detail penyebab ada di log aplikasi (error.log). Coba tutup pratinjau lain lalu ulangi.");
                    return;
                }

                var preview = _previewFactory(
                    $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}", pdfPath, suratData.ID_Surat);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open document");
                // Sebelumnya error ini ditelan diam-diam sehingga klik pratinjau PDF
                // tampak "tidak berfungsi". Tampilkan penyebabnya ke pengguna.
                await _messageService.ShowErrorAsync($"Gagal membuka pratinjau PDF: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }

        /// <summary>
        /// Buka form input dalam MODE EDIT untuk surat ini: data dimuat ulang dari
        /// database ke form, nomor surat dikunci, lalu Simpan menjalankan UPDATE
        /// (bukan insert baru) dan PDF di-generate ulang.
        /// </summary>
        private async Task EditSuratAsync(SuratDisplayModel? model)
        {
            if (model?.OriginalSuratData == null) return;

            if (string.Equals(model.OriginalSuratData.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await _messageService.ShowWarningAsync(
                    $"Surat nomor {model.NomorSurat} berstatus dibatalkan dan tidak dapat diedit.");
                return;
            }

            try
            {
                // Permohonan rekening koran diisi lewat formnya sendiri (bukan form input
                // surat warga), jadi edit diarahkan ke halaman itu dengan data surat lama.
                if (string.Equals(model.OriginalSuratData.NamaJenis, SuratConstants.REKENING_KORAN, StringComparison.OrdinalIgnoreCase))
                {
                    var scopeRk = _serviceProvider.CreateScope();
                    var rekeningVm = scopeRk.ServiceProvider.GetRequiredService<RekeningKoranViewModel>();
                    rekeningVm.RequestClose += () => _navigation.Navigate(CreateRegisterForReturn());
                    await rekeningVm.ConfigureForEditAsync(model.ID_Surat);
                    _navigation.Navigate(rekeningVm);
                    return;
                }

                // Surat dari Template Surat diperbaiki lewat formulir pengisian template
                // (mode edit): definisi & isian dibaca dari payload surat, nomor lamanya
                // dipertahankan, dan Simpan memperbarui baris register yang sama.
                if (string.Equals(model.OriginalSuratData.NamaJenis, SuratConstants.TEMPLATE_SURAT, StringComparison.OrdinalIgnoreCase))
                {
                    var scopeTemplate = _serviceProvider.CreateScope();
                    var isiVm = scopeTemplate.ServiceProvider.GetRequiredService<IsiTemplateSuratViewModel>();
                    isiVm.SebelumBatal = () => _navigation.Navigate(CreateRegisterForReturn());
                    await isiVm.ConfigureForEditAsync(model.ID_Surat);
                    _navigation.Navigate(isiVm);
                    return;
                }

                // Form edit ditampilkan di content host utama (bukan modal): navigasi ke
                // InputWindowViewModel, muat data surat, lalu kunci nomor. Saat dibatalkan,
                // kembali ke daftar Register Surat yang disegarkan.
                var scope = _serviceProvider.CreateScope();
                var vm = scope.ServiceProvider.GetRequiredService<InputWindowViewModel>();
                vm.RequestClose += () => _navigation.Navigate(CreateRegisterForReturn());
                await vm.ConfigureForEditAsync(model.ID_Surat);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form edit surat #{Id}", model.ID_Surat);
                await _messageService.ShowErrorAsync($"Gagal membuka form edit: {ex.Message}");
            }
        }

        /// <summary>
        /// Ekspor surat ini ke PDF di lokasi pilihan pengguna (bukan file temp).
        /// PDF dibuat ke temp dulu lalu disalin ke tujuan — generator selalu menulis
        /// ke path temp dengan nama tetap.
        /// </summary>
        private async Task ExportSuratPdfAsync(SuratDisplayModel? model)
        {
            if (model?.OriginalSuratData == null) return;

            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Ekspor Surat ke PDF",
                    Filter = "Dokumen PDF (*.pdf)|*.pdf",
                    FileName = $"{model.NamaJenis}_{model.NomorSurat}_{model.TanggalSurat:yyyyMMdd}.pdf"
                        .Replace("/", "-").Replace("\\", "-"),
                    DefaultExt = ".pdf"
                };
                if (sfd.ShowDialog() != true) return;

                IsLoading = true;
                IsGeneratingPdf = true;

                var suratData = model.OriginalSuratData;

                // Muat ulang lengkap dari DB agar PDF hasil ekspor berisi semua
                // relasi (warga, rincian garapan, dst.), bukan data parsial baris daftar.
                var fullForExport = await _unitOfWork.SuratRepository.GetByIdAsync(suratData.ID_Surat);
                if (fullForExport != null) suratData = fullForExport;

                var pdfPath = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, suratData.NamaJenis);

                if (pdfPath == null || !File.Exists(pdfPath))
                {
                    await _messageService.ShowWarningAsync(
                        $"PDF tidak dapat dibuat untuk jenis '{suratData.NamaJenis ?? "?"}'. " +
                        "Detail penyebab ada di log aplikasi (error.log).");
                    return;
                }

                File.Copy(pdfPath, sfd.FileName, overwrite: true);
                await _messageService.ShowInfoAsync($"Surat diekspor ke:\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor surat #{Id} ke PDF", model.ID_Surat);
                await _messageService.ShowErrorAsync($"Gagal mengekspor PDF: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                IsGeneratingPdf = false;
            }
        }

        private string GetNamaDisplay(SuratData surat)
        {
            if (surat.Instansi != null && !string.IsNullOrEmpty(surat.Instansi.NamaInstansi))
                return surat.Instansi.NamaInstansi;
            // Surat dari Template Surat tidak memakai data kependudukan: nama penerimanya
            // dibaca dari isian surat itu sendiri (mis. kolom "Nama" atau "Ditujukan Kepada").
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string namaTemplate = TemplateSuratTercatat.NamaPenerimaTampil(surat);
                return namaTemplate.Length > 0 ? namaTemplate : "-";
            }

            if (surat.Warga != null && !string.IsNullOrEmpty(surat.Warga.Nama))
                return surat.Warga.Nama;
            return "-";
        }

        private string GetTTLDisplay(SuratData surat)
        {
            // Surat dari Template Surat: TTL datang dari kolom isian templatenya.
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string ttlTemplate = TemplateSuratTercatat.TempatTanggalLahirTampil(surat);
                return ttlTemplate.Length > 0 ? ttlTemplate : "-";
            }

            if (surat.Warga == null) return "-";
            return $"{surat.Warga.TempatLahir ?? "-"}, {surat.Warga.TanggalLahir ?? "-"}";
        }

        private string GetJenisKelaminDisplay(SuratData surat)
        {
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string jkTemplate = TemplateSuratTercatat.JenisKelaminTampil(surat);
                return jkTemplate.Length > 0 ? jkTemplate : "-";
            }

            if (surat.Warga == null || string.IsNullOrEmpty(surat.Warga.JenisKelamin)) return "-";
            return surat.Warga.JenisKelamin.Substring(0, 1).ToUpper();
        }

        private string GetAlamatDisplay(SuratData surat)
        {
            // Surat dari Template Surat menyimpan alamat bebas (bukan dusun/desa/
            // kecamatan) di dalam isian suratnya.
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string alamatTemplate = TemplateSuratTercatat.AlamatPenerimaTampil(surat);
                return alamatTemplate.Length > 0 ? alamatTemplate : "-";
            }

            if (surat.Warga == null)
                return surat.Instansi?.AlamatInstansi ?? "-";

            var parts = new[]
            {
                surat.Warga.Dusun,
                surat.Warga.Desa,
                surat.Warga.Kecamatan,
                surat.Warga.Kabupaten
            }.Where(p => !string.IsNullOrWhiteSpace(p));

            return parts.Any() ? string.Join(", ", parts) : "-";
        }

        private string GetJenisSuratLabel(SuratData surat)
        {
            return surat.NamaJenis?.ToUpperInvariant() switch
            {
                "NTCR_N1" => "N1 - Surat Pengantar Nikah",
                "NTCR_N2" => "N2 - Permohonan Kehendak Nikah",
                "NTCR_N3" => "N3 - Permohonan Pencatatan Isbat",
                "NTCR_N4" => "N4 - Persetujuan Calon Pengantin",
                "NTCR_N5" => "N5 - Surat Izin Orang Tua",
                "NTCR_N6" => "N6 - Ket. Kematian Suami/Istri",
                "NTCR_N8" => "N8 - Ket. Numpang Nikah",
                "REKENING_KORAN" => "Permohonan Rekening Koran",
                "TEMPLATE_SURAT" => GetLabelTemplateSurat(surat),
                "BEDANAMA" => "Surat Ket. Beda Data",
                "SKD_UMUM" => "SKD Umum",
                "DOMISILI_WARGA" => "Domisili Warga",
                "INSTANSI" => "Domisili Instansi/Lembaga",
                "SKU" => "SKU (Surat Ket. Usaha)",
                "PENGANTAR_SKCK" => "Pengantar SKCK",
                "IZIN_ORTU" => "Izin Suami/Orang Tua",
                "GARAPAN_SAWAH" => "Ket. Garapan Sawah",
                "KEMATIAN" => "Surat Kematian",
                "SKTM" => "SKTM (Surat Ket. Tidak Mampu)",
                _ => surat.NamaJenis?.Replace("_", " ") ?? "-"
            };
        }

        /// <summary>
        /// Label jenis surat untuk surat dari Template Surat: menyebut nama templatenya
        /// (dibaca dari payload surat) supaya daftar register tetap informatif.
        /// </summary>
        private static string GetLabelTemplateSurat(SuratData surat)
        {
            var payload = TemplateSuratTercatat.FromJson(surat?.AdditionalData);
            string nama = payload?.NamaTemplate?.Trim() ?? string.Empty;

            return nama.Length == 0 ? "Template Surat" : $"Template: {nama}";
        }

        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(CanPreviousPage));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(HasData));
            (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PrintRegisterCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}