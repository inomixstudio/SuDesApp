using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Baris grid buku agenda Surat Masuk/Keluar.</summary>
    public class AgendaRow : ObservableObject
    {
        public AgendaRow(SuratKeluarMasukData data, int no)
        {
            Data = data;
            No = no;
        }

        public SuratKeluarMasukData Data { get; }
        public int No { get; }
        public int IdBaris => Data.IdBarisExcel;
        public string NomorSurat => Data.NomorSurat;
        public string TanggalSurat => Data.TanggalSurat.ToString("dd-MM-yyyy");
        public string TanggalTerimaKirim => Data.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy") ?? "";
        public string AsalTujuan => Data.AsalTujuan;
        public string Perihal => Data.Perihal;
        public string IsiRingkas => Data.IsiRingkas;
        public string Keterangan => Data.Keterangan;
    }

    /// <summary>
    /// Halaman buku agenda Surat Masuk/Keluar — padanan SuratKeluarMasuk (WinForms):
    /// data dari arsip Excel berformat MASUK/KELUAR, filter tahun,
    /// Tambah/Edit/Hapus, dan cetak PDF via generator.
    /// </summary>
    public class AgendaSuratViewModel : ObservableObject
    {
        private readonly IArsipSuratRepository _repository;
        private readonly IDesaRepository _desaRepository;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly Func<string, SuratKeluarMasukData?, InputAgendaWindow> _inputWindowFactory;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly ILogger<AgendaSuratViewModel> _logger;

        private string _jenisSurat = "MASUK";
        private string? _selectedTahun;
        private AgendaRow? _selectedRow;
        private bool _isLoading;
        private System.Collections.Generic.List<SuratKeluarMasukData> _semua = new();

        public AgendaSuratViewModel(
            IArsipSuratRepository repository,
            IDesaRepository desaRepository,
            NavigationService navigation,
            IMessageService messageService,
            Func<string, SuratKeluarMasukData?, InputAgendaWindow> inputWindowFactory,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            ILogger<AgendaSuratViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _inputWindowFactory = inputWindowFactory ?? throw new ArgumentNullException(nameof(inputWindowFactory));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SetMasukCommand = new AsyncRelayCommand(() => SetJenisAsync("MASUK"));
            SetKeluarCommand = new AsyncRelayCommand(() => SetJenisAsync("KELUAR"));
            TambahCommand = new AsyncRelayCommand(AddAsync);
            EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedRow != null);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => SelectedRow != null);
            CetakCommand = new AsyncRelayCommand(CetakAsync, () => SelectedTahun != null);
            SegarkanCommand = new AsyncRelayCommand(() => LoadAsync());
            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        public string JenisSurat => _jenisSurat;
        public string HeaderTitle => $"SURAT {_jenisSurat}";
        public bool IsMasuk => _jenisSurat == "MASUK";
        public bool IsKeluar => _jenisSurat == "KELUAR";
        public string TahunLabel => _jenisSurat == "MASUK" ? "Tahun Masuk" : "Tahun Keluar";

        public ObservableCollection<string> TahunOptions { get; } = new();
        public ObservableCollection<AgendaRow> Rows { get; } = new();

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

        public AgendaRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    ((AsyncRelayCommand)EditCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)HapusCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public AsyncRelayCommand SetMasukCommand { get; }
        public AsyncRelayCommand SetKeluarCommand { get; }
        public AsyncRelayCommand TambahCommand { get; }
        public AsyncRelayCommand EditCommand { get; }
        public AsyncRelayCommand HapusCommand { get; }
        public AsyncRelayCommand CetakCommand { get; }
        public AsyncRelayCommand SegarkanCommand { get; }
        public RelayCommand BatalCommand { get; }

        /// <summary>Inisialisasi awal (dipanggil factory navigasi).</summary>
        public async Task SetJenisAsync(string jenisSurat)
        {
            string jenis = (jenisSurat ?? "MASUK").ToUpperInvariant();
            if (_jenisSurat == jenis)
            {
                await LoadAsync();
                return;
            }

            _jenisSurat = jenis;
            OnPropertyChanged(nameof(JenisSurat));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(IsMasuk));
            OnPropertyChanged(nameof(IsKeluar));
            OnPropertyChanged(nameof(TahunLabel));
            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                _semua = await _repository.GetAllAsync();

                var years = _semua
                    .Where(s => s.JenisSurat.Equals(_jenisSurat, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.TanggalSurat.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .Select(y => y.ToString())
                    .ToList();

                TahunOptions.Clear();
                foreach (var year in years) TahunOptions.Add(year);

                SelectedTahun = TahunOptions.FirstOrDefault();
                if (SelectedTahun == null) FilterRows();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat arsip surat {Jenis}", _jenisSurat);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void FilterRows()
        {
            int.TryParse(SelectedTahun, out int selectedYear);
            var filtered = _semua
                .Where(s => s.JenisSurat.Equals(_jenisSurat, StringComparison.OrdinalIgnoreCase) &&
                            (selectedYear == 0 || s.TanggalSurat.Year == selectedYear))
                .OrderByDescending(s => s.TanggalSurat)
                .ToList();

            Rows.Clear();
            int no = 1;
            foreach (var item in filtered)
            {
                Rows.Add(new AgendaRow(item, no++));
            }
        }

        private async Task AddAsync()
        {
            var window = _inputWindowFactory(_jenisSurat, null);
            window.Owner = System.Windows.Application.Current?.MainWindow;
            if (window.ShowDialog() == true)
            {
                await LoadAsync();
            }
        }

        private async Task EditAsync()
        {
            if (SelectedRow == null) return;
            var window = _inputWindowFactory(_jenisSurat, SelectedRow.Data);
            window.Owner = System.Windows.Application.Current?.MainWindow;
            if (window.ShowDialog() == true)
            {
                await LoadAsync();
            }
        }

        private async Task HapusAsync()
        {
            if (SelectedRow == null) return;
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Konfirmasi Hapus",
                $"Yakin ingin menghapus Nomor '{SelectedRow.NomorSurat}'?");
            if (!confirmed) return;

            try
            {
                await _repository.DeleteAsync(SelectedRow.IdBaris);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus arsip surat");
                await _messageService.ShowErrorAsync("Gagal menghapus data: " + ex.Message);
            }
        }

        private async Task CetakAsync()
        {
            if (string.IsNullOrEmpty(SelectedTahun) || !int.TryParse(SelectedTahun, out int year)) return;

            try
            {
                var desaData = await _desaRepository.GetInfoDesaAsync() ?? new DesaData();
                var dataToPrint = _semua
                    .Where(s => s.JenisSurat.Equals(_jenisSurat, StringComparison.OrdinalIgnoreCase) &&
                                s.TanggalSurat.Year == year)
                    .ToList();
                if (dataToPrint.Count == 0)
                {
                    await _messageService.ShowWarningAsync("Tidak ada data untuk dicetak.");
                    return;
                }

                var generator = new SuratKeluarMasukGenerator(desaData);
                using var stream = new MemoryStream();
                generator.GenerateAllSuratPdf(stream, dataToPrint, _jenisSurat, year);

                string tempPath = Path.Combine(Path.GetTempPath(), $"Arsip_{_jenisSurat}_{year}.pdf");
                await File.WriteAllBytesAsync(tempPath, stream.ToArray());

                var preview = _previewFactory($"Arsip {_jenisSurat} {year}", tempPath);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencetak PDF arsip surat");
                await _messageService.ShowErrorAsync("Gagal mencetak PDF: " + ex.Message);
            }
        }
    }
}