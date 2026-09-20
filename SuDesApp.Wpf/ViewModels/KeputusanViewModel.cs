using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Baris grid buku SK / Peraturan.</summary>
    public class KeputusanRow : ObservableObject
    {
        public KeputusanRow(DataKeputusan data, int no)
        {
            Data = data;
            No = no;
        }

        public DataKeputusan Data { get; }
        public int No { get; }
        public int IdBaris => Data.IdBarisExcel;
        public string Nomor => Data.Nomor;
        public string Tanggal => Data.Tanggal.ToString("dd-MM-yyyy");
        public string Tentang => Data.Tentang;
        public string Keterangan => Data.Keterangan;
        public string FileWord => Data.FileWord!;

        /// <summary>Ekstensi berkas lampiran tanpa titik (mis. DOCX / PDF), atau kosong.</summary>
        public string? EkstensiFile =>
            string.IsNullOrWhiteSpace(FileWord) ? null : Path.GetExtension(FileWord).TrimStart('.').ToUpperInvariant();

        /// <summary>Ada berkas lampiran tersimpan.</summary>
        public bool PunyaLampiran => !string.IsNullOrWhiteSpace(FileWord);

        /// <summary>Teks gabungan untuk pencarian cepat (nomor/tentang/keterangan/tahun).</summary>
        public string SearchText => string.Join(" ", Nomor, Tentang, Keterangan, Tanggal, Data.Tanggal.Year.ToString());
    }

    /// <summary>
    /// Halaman buku SK / Peraturan (SK, Perdes, Perkades) — padanan
    /// KeputusanPeraturan (WinForms): data dari arsip Excel jenis keputusan,
    /// filter tahun, pencarian cepat, Tambah/Edit/Hapus, cetak PDF dan Batal.
    /// </summary>
    public class KeputusanViewModel : ObservableObject
    {
        private readonly IArsipKeputusanRepository _repository;
        private readonly IDesaRepository _desaRepository;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly Func<string, DataKeputusan?, DataKeputusan?, InputKeputusanViewModel> _inputFactory;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly ILogger<KeputusanViewModel> _logger;

        private string _jenisKeputusan = "SK";
        private string? _selectedTahun;
        private KeputusanRow? _selectedRow;
        private bool _isLoading;
        private string _searchText = string.Empty;
        private string _statusInfo = string.Empty;
        private int _totalJenisCount;
        private int _tampilCount;
        private int _jumlahLampiran;
        private int _tahunTerbaru;
        private System.Collections.Generic.List<DataKeputusan> _semua = new();

        public int TotalJenisCount
        {
            get => _totalJenisCount;
            private set => SetProperty(ref _totalJenisCount, value);
        }

        public int TampilCount
        {
            get => _tampilCount;
            private set => SetProperty(ref _tampilCount, value);
        }

        public int JumlahLampiran
        {
            get => _jumlahLampiran;
            private set => SetProperty(ref _jumlahLampiran, value);
        }

        public int TahunTerbaru
        {
            get => _tahunTerbaru;
            private set
            {
                if (SetProperty(ref _tahunTerbaru, value))
                {
                    OnPropertyChanged(nameof(TahunTerbaruLabel));
                }
            }
        }

        /// <summary>Tahun terbaru sebagai teks ("—" bila belum ada data).</summary>
        public string TahunTerbaruLabel => TahunTerbaru == 0 ? "—" : TahunTerbaru.ToString();

        /// <summary>True bila ada baris yang tampil (kontrol pesan "tidak ada data").</summary>
        public bool HasRows => Rows.Count > 0;

        public KeputusanViewModel(
            IArsipKeputusanRepository repository,
            IDesaRepository desaRepository,
            NavigationService navigation,
            IMessageService messageService,
            Func<string, DataKeputusan?, DataKeputusan?, InputKeputusanViewModel> inputFactory,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            ILogger<KeputusanViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SetSkCommand = new AsyncRelayCommand(() => SetJenisAsync("SK"));
            SetPerdesCommand = new AsyncRelayCommand(() => SetJenisAsync("PERDES"));
            SetPerkadesCommand = new AsyncRelayCommand(() => SetJenisAsync("PERKADES"));
            TambahCommand = new AsyncRelayCommand(AddAsync);
            EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedRow != null);
            SalinCommand = new AsyncRelayCommand(SalinAsync, () => SelectedRow != null);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => SelectedRow != null);
            CetakCommand = new AsyncRelayCommand(CetakAsync, () => SelectedTahun != null);
            SegarkanCommand = new AsyncRelayCommand(() => LoadAsync());
            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
            OpenWordFileCommand = new AsyncRelayCommand<KeputusanRow?>(OpenWordFileAsync);
        }

        public AsyncRelayCommand<KeputusanRow?> OpenWordFileCommand { get; }

        public string JenisKeputusan => _jenisKeputusan;
        public string HeaderTitle => _jenisKeputusan switch
        {
            "SK" => "SK / KEPUTUSAN",
            "PERDES" => "PERDES",
            _ => "PERKADES"
        };
        public bool IsSk => _jenisKeputusan == "SK";
        public bool IsPerdes => _jenisKeputusan == "PERDES";
        public bool IsPerkades => _jenisKeputusan == "PERKADES";

        public ObservableCollection<string> TahunOptions { get; } = new();
        public ObservableCollection<KeputusanRow> Rows { get; } = new();

        /// <summary>Kata kunci pencarian (nomor, tentang, keterangan, tahun).</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    FilterRows();
                }
            }
        }

        /// <summary>Info jumlah baris yang tampil vs total arsip jenis ini.</summary>
        public string StatusInfo
        {
            get => _statusInfo;
            private set => SetProperty(ref _statusInfo, value);
        }

        public string? SelectedTahun
        {
            get => _selectedTahun;
            set
            {
                if (SetProperty(ref _selectedTahun, value))
                {
                    FilterRows();
                    ((AsyncRelayCommand)CetakCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public KeputusanRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    ((AsyncRelayCommand)EditCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)SalinCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)HapusCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public AsyncRelayCommand SetSkCommand { get; }
        public AsyncRelayCommand SetPerdesCommand { get; }
        public AsyncRelayCommand SetPerkadesCommand { get; }
        public AsyncRelayCommand TambahCommand { get; }
        public AsyncRelayCommand EditCommand { get; }
        public AsyncRelayCommand SalinCommand { get; }
        public AsyncRelayCommand HapusCommand { get; }
        public AsyncRelayCommand CetakCommand { get; }
        public AsyncRelayCommand SegarkanCommand { get; }
        public RelayCommand BatalCommand { get; }

        /// <summary>Inisialisasi awal (dipanggil factory navigasi). Jenis: SK / PERDES / PERKADES.</summary>
        public async Task SetJenisAsync(string jenisKeputusan)
        {
            string jenis = (jenisKeputusan ?? "SK").ToUpperInvariant();

            if (_jenisKeputusan == jenis)
            {
                await LoadAsync();
                return;
            }

            _jenisKeputusan = jenis;
            OnPropertyChanged(nameof(JenisKeputusan));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(IsSk));
            OnPropertyChanged(nameof(IsPerdes));
            OnPropertyChanged(nameof(IsPerkades));
            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                _semua = await _repository.GetAllAsync();

                var jenisRows = _semua
                    .Where(k => k.JenisKeputusan.Equals(_jenisKeputusan, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var years = jenisRows
                    .Select(k => k.Tanggal.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .Select(y => y.ToString())
                    .ToList();

                TahunOptions.Clear();
                TahunOptions.Add("Semua Tahun"); // Add "All Years" option
                foreach (var year in years) TahunOptions.Add(year);

                SelectedTahun = "Semua Tahun"; // Default to show all
                TotalJenisCount = jenisRows.Count;
                TahunTerbaru = years.Count > 0 && int.TryParse(years[0], out int lastYear) ? lastYear : 0;
                FilterRows();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat arsip keputusan {Jenis}", _jenisKeputusan);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void FilterRows()
        {
            bool isAllYears = SelectedTahun == "Semua Tahun";
            int.TryParse(SelectedTahun, out int selectedYear);

            // Pencarian cepat: cocokkan di nomor/tentang/keterangan/tanggal (case-insensitive).
            string? q = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            var jenisRows = _semua
                .Where(k => k.JenisKeputusan.Equals(_jenisKeputusan, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var filtered = jenisRows
                .Where(k => (isAllYears || selectedYear == 0 || k.Tanggal.Year == selectedYear) &&
                            (q == null ||
                             k.Nomor.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             k.Tentang.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             k.Keterangan.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             k.Tanggal.ToString("dd-MM-yyyy").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             k.Tanggal.Year.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(k => k.Tanggal)
                .ToList();

            Rows.Clear();
            int no = 1;
            foreach (var item in filtered)
            {
                Rows.Add(new KeputusanRow(item, no++));
            }

            TampilCount = filtered.Count;
            JumlahLampiran = filtered.Count(k => !string.IsNullOrWhiteSpace(k.FileWord));
            OnPropertyChanged(nameof(HasRows));
            UpdateStatusInfo(jenisRows.Count, filtered.Count);
        }

        private void UpdateStatusInfo(int totalJenis, int tampil)
        {
            StatusInfo = tampil == totalJenis
                ? $"Menampilkan {tampil} data."
                : $"Menampilkan {tampil} dari {totalJenis} data.";
        }

        private Task AddAsync() => BukaFormulirAsync(null, null);

        /// <summary>
        /// Buka formulir input/edit SK/Peraturan sebagai halaman di area konten utama.
        /// Setelah tersimpan (atau dibatalkan) halaman kembali ke daftar arsip.
        /// </summary>
        private Task BukaFormulirAsync(DataKeputusan? editData, DataKeputusan? prefill)
        {
            try
            {
                var vm = _inputFactory(_jenisKeputusan, editData, prefill);
                vm.Tersimpan += () => _ = LoadAsync();
                vm.RequestClose += () => _navigation.Navigate(this);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka formulir input keputusan {Jenis}", _jenisKeputusan);
                return _messageService.ShowErrorAsync("Gagal membuka formulir input: " + ex.Message);
            }

            return Task.CompletedTask;
        }

        private async Task SalinAsync()
        {
            if (SelectedRow == null) return;
            try
            {
                var salinan = new DataKeputusan
                {
                    JenisKeputusan = SelectedRow.Data.JenisKeputusan,
                    Nomor = SelectedRow.Data.Nomor,
                    Tanggal = SelectedRow.Data.Tanggal,
                    Tentang = SelectedRow.Data.Tentang,
                    Keterangan = SelectedRow.Data.Keterangan,
                    FileWord = null
                };
                await BukaFormulirAsync(null, salinan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka formulir salin keputusan {Jenis}", _jenisKeputusan);
                await _messageService.ShowErrorAsync("Gagal membuka formulir salin: " + ex.Message);
            }
        }

        private async Task EditAsync()
        {
            if (SelectedRow == null) return;
            try
            {
                await BukaFormulirAsync(SelectedRow.Data, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka formulir edit keputusan {Jenis}", _jenisKeputusan);
                await _messageService.ShowErrorAsync("Gagal membuka formulir edit: " + ex.Message);
            }
        }

        private async Task HapusAsync()
        {
            if (SelectedRow == null) return;
            var infoFile = string.IsNullOrWhiteSpace(SelectedRow.FileWord)
                ? string.Empty
                : "\n\nBerkas lampiran terkait di folder penyimpanan juga akan dihapus.";
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Konfirmasi Hapus",
                $"Yakin ingin menghapus Nomor '{SelectedRow.Nomor}'?{infoFile}");
            if (!confirmed) return;

            try
            {
                await _repository.DeleteAsync(SelectedRow.IdBaris);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus arsip keputusan");
                await _messageService.ShowErrorAsync("Gagal menghapus data: " + ex.Message);
            }
        }

        private async Task CetakAsync()
        {
            // Filter "Semua Tahun" (bukan angka) berarti cetak seluruh tahun untuk jenis ini.
            int? year = int.TryParse(SelectedTahun, out int parsedYear) ? parsedYear : null;

            try
            {
                var desaData = await _desaRepository.GetInfoDesaAsync() ?? new DesaData();
                var dataToPrint = _semua
                    .Where(k => k.JenisKeputusan.Equals(_jenisKeputusan, StringComparison.OrdinalIgnoreCase) &&
                                (!year.HasValue || k.Tanggal.Year == year.Value))
                    .OrderByDescending(k => k.Tanggal)
                    .ToList();
                if (dataToPrint.Count == 0)
                {
                    await _messageService.ShowWarningAsync("Tidak ada data untuk dicetak.");
                    return;
                }

                var generator = new KeputusanPeraturanGenerator(desaData);
                using var stream = new MemoryStream();
                generator.GenerateKeputusanPdf(stream, dataToPrint, _jenisKeputusan, year);

                string tahunLabel = year?.ToString() ?? "SemuaTahun";
                string tempPath = Path.Combine(Path.GetTempPath(), $"Arsip_{_jenisKeputusan}_{tahunLabel}.pdf");
                await File.WriteAllBytesAsync(tempPath, stream.ToArray());

                var preview = _previewFactory($"{HeaderTitle} {year?.ToString() ?? "Semua Tahun"}", tempPath);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencetak PDF keputusan");
                await _messageService.ShowErrorAsync("Gagal mencetak PDF: " + ex.Message);
            }
        }

        /// <summary>Membuka berkas Word milik baris yang diklik (bukan bergantung SelectedRow).</summary>
        private async Task OpenWordFileAsync(KeputusanRow? row)
        {
            row ??= SelectedRow;
            if (row == null || string.IsNullOrWhiteSpace(row.FileWord)) return;
            try
            {
                var fullPath = _repository.ResolveWordFullPath(row.FileWord);
                if (fullPath == null)
                {
                    await _messageService.ShowWarningAsync("Berkas lampiran tidak ditemukan di penyimpanan.");
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka berkas lampiran");
                await _messageService.ShowErrorAsync("Gagal membuka berkas lampiran: " + ex.Message);
            }
        }
    }
}
