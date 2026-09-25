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
        public string? FileLampiran => Data.FileLampiran;

        /// <summary>Ada berkas lampiran (PDF/gambar) tersimpan untuk baris ini.</summary>
        public bool PunyaLampiran => !string.IsNullOrWhiteSpace(Data.FileLampiran);

        /// <summary>Label pendek jenis berkas lampiran (PDF/JPG/...) untuk chip, atau kosong.</summary>
        public string EkstensiLampiran => LampiranArsipSurat.Label(Data.FileLampiran);

        /// <summary>Teks gabungan untuk pencarian cepat (nomor, asal/tujuan, perihal, isi, keterangan, tanggal).</summary>
        public string SearchText => string.Join(" ", NomorSurat, AsalTujuan, Perihal, IsiRingkas, Keterangan,
            TanggalSurat, TanggalTerimaKirim, Data.TanggalSurat.Year.ToString());
    }

    /// <summary>
    /// Halaman buku agenda Surat Masuk/Keluar — padanan SuratKeluarMasuk (WinForms):
    /// data dari arsip (tabel ArsipSurat di database SQLite), filter tahun,
    /// pencarian cepat, Tambah/Edit/Hapus, lampiran PDF/gambar, dan cetak PDF
    /// via generator. Tata letak halaman mengikuti buku Keputusan/Peraturan
    /// (kepala halaman, kartu ringkasan, bilah alat, dan daftar bergaya modern).
    /// </summary>
    public class AgendaSuratViewModel : ObservableObject
    {
        /// <summary>Pilihan tahun untuk menampilkan seluruh data lintas tahun.</summary>
        public const string OpsiSemuaTahun = "Semua Tahun";

        private readonly IArsipSuratRepository _repository;
        private readonly IDesaRepository _desaRepository;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly Func<string, SuratKeluarMasukData?, SuratKeluarMasukData?, InputAgendaViewModel> _inputFactory;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly ILogger<AgendaSuratViewModel> _logger;

        private string _jenisSurat = "MASUK";
        private string? _selectedTahun;
        private AgendaRow? _selectedRow;
        private bool _isLoading;
        private string _searchText = string.Empty;
        private string _statusInfo = string.Empty;
        private int _totalJenisCount;
        private int _tampilCount;
        private int _jumlahLampiran;
        private int _tahunTerbaru;
        private System.Collections.Generic.List<SuratKeluarMasukData> _semua = new();

        /// <summary>Baris arsip untuk jenis yang sedang aktif — dihitung sekali per muat.</summary>
        private System.Collections.Generic.List<SuratKeluarMasukData> _jenisRows = new();

        public AgendaSuratViewModel(
            IArsipSuratRepository repository,
            IDesaRepository desaRepository,
            NavigationService navigation,
            IMessageService messageService,
            Func<string, SuratKeluarMasukData?, SuratKeluarMasukData?, InputAgendaViewModel> inputFactory,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            ILogger<AgendaSuratViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SetMasukCommand = new AsyncRelayCommand(() => SetJenisAsync("MASUK"));
            SetKeluarCommand = new AsyncRelayCommand(() => SetJenisAsync("KELUAR"));
            TambahCommand = new AsyncRelayCommand(AddAsync);
            SalinCommand = new AsyncRelayCommand(SalinAsync, () => SelectedRow != null);
            EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedRow != null);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => SelectedRow != null);
            CetakCommand = new AsyncRelayCommand(CetakAsync, () => SelectedTahun != null);
            SegarkanCommand = new AsyncRelayCommand(() => LoadAsync());
            BukaLampiranCommand = new AsyncRelayCommand<AgendaRow?>(BukaLampiranAsync);
            BersihkanPencarianCommand = new RelayCommand(() => SearchText = string.Empty);
            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        public string JenisSurat => _jenisSurat;
        public string HeaderTitle => $"BUKU AGENDA SURAT {_jenisSurat}";
        public bool IsMasuk => _jenisSurat == "MASUK";
        public bool IsKeluar => _jenisSurat == "KELUAR";
        public string TahunLabel => _jenisSurat == "MASUK" ? "Tahun Masuk" : "Tahun Keluar";

        /// <summary>Judul kartu ringkasan total (mis. "TOTAL SURAT MASUK").</summary>
        public string TotalLabel => $"TOTAL SURAT {_jenisSurat}";
        public string HeaderSubtitle => _jenisSurat == "MASUK"
            ? "Buku agenda surat masuk — klik dua kali baris untuk mengedit."
            : "Buku agenda surat keluar — klik dua kali baris untuk mengedit.";

        public ObservableCollection<string> TahunOptions { get; } = new();

        /// <summary>
        /// Baris yang tampil di grid. Diganti sebagai satu koleksi utuh setiap kali
        /// filter berubah: DataGrid cukup sekali menyusun ulang (dan tetap
        /// tervirtualisasi), bukan menerima ribuan notifikasi baris satu per satu.
        /// </summary>
        public ObservableCollection<AgendaRow> Rows { get; private set; } = new();

        /// <summary>Total data jenis surat ini (tanpa filter tahun/pencarian).</summary>
        public int TotalJenisCount
        {
            get => _totalJenisCount;
            private set => SetProperty(ref _totalJenisCount, value);
        }

        /// <summary>Jumlah baris yang sedang tampil setelah filter.</summary>
        public int TampilCount
        {
            get => _tampilCount;
            private set => SetProperty(ref _tampilCount, value);
        }

        /// <summary>Jumlah baris tampil yang memiliki berkas lampiran.</summary>
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

        /// <summary>Kata kunci pencarian (nomor, asal/tujuan, perihal, isi ringkas, keterangan).</summary>
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

        /// <summary>Info jumlah baris yang tampil vs total data jenis ini.</summary>
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

        public AgendaRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    ((AsyncRelayCommand)EditCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)HapusCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)SalinCommand).RaiseCanExecuteChanged();
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
        public AsyncRelayCommand SalinCommand { get; }
        public AsyncRelayCommand EditCommand { get; }
        public AsyncRelayCommand HapusCommand { get; }
        public AsyncRelayCommand CetakCommand { get; }
        public AsyncRelayCommand SegarkanCommand { get; }
        public AsyncRelayCommand<AgendaRow?> BukaLampiranCommand { get; }
        public RelayCommand BersihkanPencarianCommand { get; }
        public RelayCommand BatalCommand { get; }

        /// <summary>Inisialisasi awal (dipanggil factory navigasi).</summary>
        public async Task SetJenisAsync(string jenisSurat)
        {
            string jenis = (jenisSurat ?? "MASUK").ToUpperInvariant();
            if (_jenisSurat == jenis)
            {
                await LoadAsync(muatUlangData: false);
                return;
            }

            _jenisSurat = jenis;
            SearchText = string.Empty;
            OnPropertyChanged(nameof(JenisSurat));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(IsMasuk));
            OnPropertyChanged(nameof(IsKeluar));
            OnPropertyChanged(nameof(TahunLabel));
            OnPropertyChanged(nameof(TotalLabel));

            // Surat masuk & keluar berada di satu tabel: berpindah tab tidak perlu
            // membaca ulang seluruh arsip, cukup memakai data yang sudah dimuat.
            await LoadAsync(muatUlangData: false);
        }

        /// <param name="muatUlangData">
        /// True untuk membaca ulang seluruh arsip dari database (muat pertama,
        /// tombol Segarkan, dan setelah simpan/hapus). False untuk memakai data
        /// yang sudah ada di memori — mis. saat berpindah tab Masuk/Keluar.
        /// </param>
        public async Task LoadAsync(bool muatUlangData = true)
        {
            try
            {
                IsLoading = true;

                if (muatUlangData || _semua.Count == 0)
                {
                    _semua = await _repository.GetAllAsync()
                        ?? new System.Collections.Generic.List<SuratKeluarMasukData>();
                }

                // Sekali jalan saja: baris untuk jenis aktif, lalu dipakai ulang oleh
                // FilterRows saat tahun/pencarian berubah (tidak difilter dua kali).
                _jenisRows = _semua
                    .Where(s => s.JenisSurat.Equals(_jenisSurat, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var years = _jenisRows
                    .Select(s => s.TanggalSurat.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .Select(y => y.ToString())
                    .ToList();

                TahunOptions.Clear();
                TahunOptions.Add(OpsiSemuaTahun);
                foreach (var year in years) TahunOptions.Add(year);

                SelectedTahun = OpsiSemuaTahun;
                TotalJenisCount = _jenisRows.Count;
                TahunTerbaru = years.Count > 0 && int.TryParse(years[0], out int lastYear) ? lastYear : 0;
                FilterRows();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat arsip surat {Jenis}", _jenisSurat);
                await _messageService.ShowErrorAsync("Gagal memuat buku agenda: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasRows));
            }
        }

        private void FilterRows()
        {
            bool isAllYears = SelectedTahun == OpsiSemuaTahun;
            int.TryParse(SelectedTahun, out int selectedYear);

            // Pencarian cepat: cocokkan di nomor/asal tujuan/perihal/isi/keterangan/tanggal.
            string? q = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            var jenisRows = _jenisRows;

            var filtered = jenisRows
                .Where(s => (isAllYears || selectedYear == 0 || s.TanggalSurat.Year == selectedYear) &&
                            (q == null ||
                             s.NomorSurat.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             s.AsalTujuan.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             s.Perihal.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             s.IsiRingkas.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             s.Keterangan.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             s.TanggalSurat.ToString("dd-MM-yyyy").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             (s.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy").Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                             s.TanggalSurat.Year.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(s => s.TanggalSurat)
                .ToList();

            var baris = new System.Collections.Generic.List<AgendaRow>(filtered.Count);
            int no = 1;
            foreach (var item in filtered)
            {
                baris.Add(new AgendaRow(item, no++));
            }
            Rows = new ObservableCollection<AgendaRow>(baris);
            OnPropertyChanged(nameof(Rows));

            TampilCount = filtered.Count;
            JumlahLampiran = filtered.Count(s => !string.IsNullOrWhiteSpace(s.FileLampiran));
            OnPropertyChanged(nameof(HasRows));
            StatusInfo = filtered.Count == jenisRows.Count
                ? $"Menampilkan {filtered.Count} data."
                : $"Menampilkan {filtered.Count} dari {jenisRows.Count} data.";
        }

        /// <summary>Membuka berkas lampiran milik baris yang diklik (bukan bergantung SelectedRow).</summary>
        private async Task BukaLampiranAsync(AgendaRow? row)
        {
            row ??= SelectedRow;
            if (row == null || !row.PunyaLampiran) return;
            try
            {
                var fullPath = _repository.ResolveLampiranFullPath(row.FileLampiran);
                if (fullPath == null)
                {
                    await _messageService.ShowWarningAsync("Berkas lampiran tidak ditemukan di penyimpanan.");
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka berkas lampiran agenda surat");
                await _messageService.ShowErrorAsync("Gagal membuka berkas lampiran: " + ex.Message);
            }
        }

        private Task AddAsync() => BukaFormulirAsync(null, null);

        /// <summary>
        /// Buka formulir input/edit agenda sebagai halaman di area konten utama.
        /// Setelah tersimpan (atau dibatalkan) halaman kembali ke daftar agenda.
        /// </summary>
        private Task BukaFormulirAsync(SuratKeluarMasukData? editData, SuratKeluarMasukData? prefill)
        {
            try
            {
                var vm = _inputFactory(_jenisSurat, editData, prefill);
                vm.Tersimpan += () => _ = LoadAsync();
                vm.RequestClose += () => _navigation.Navigate(this);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka formulir input agenda {Jenis}", _jenisSurat);
                return _messageService.ShowErrorAsync("Gagal membuka formulir input: " + ex.Message);
            }

            return Task.CompletedTask;
        }

        private async Task SalinAsync()
        {
            if (SelectedRow == null) return;
            try
            {
                var salinan = new SuratKeluarMasukData
                {
                    JenisSurat = _jenisSurat,
                    NomorSurat = SelectedRow!.Data.NomorSurat,
                    TanggalSurat = SelectedRow.Data.TanggalSurat,
                    TanggalDiterimaDikirim = SelectedRow.Data.TanggalDiterimaDikirim,
                    AsalTujuan = SelectedRow.Data.AsalTujuan,
                    Perihal = SelectedRow.Data.Perihal,
                    IsiRingkas = SelectedRow.Data.IsiRingkas,
                    Keterangan = SelectedRow.Data.Keterangan,
                    FileLampiran = null
                };

                await BukaFormulirAsync(null, salinan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat salinan agenda {Jenis}", _jenisSurat);
                await _messageService.ShowErrorAsync("Gagal membuat salinan: " + ex.Message);
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
                _logger.LogError(ex, "Gagal membuka formulir edit agenda {Jenis}", _jenisSurat);
                await _messageService.ShowErrorAsync("Gagal membuka formulir edit: " + ex.Message);
            }
        }

        private async Task HapusAsync()
        {
            if (SelectedRow == null) return;
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Konfirmasi Hapus",
                SelectedRow.PunyaLampiran
                    ? $"Yakin ingin menghapus Nomor '{SelectedRow.NomorSurat}' beserta berkas lampirannya?"
                    : $"Yakin ingin menghapus Nomor '{SelectedRow.NomorSurat}'?");
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
            if (string.IsNullOrEmpty(SelectedTahun)) return;

            try
            {
                bool semuaTahun = SelectedTahun == OpsiSemuaTahun;
                int.TryParse(SelectedTahun, out int year);
                string labelTahun = semuaTahun ? "Semua Tahun" : year.ToString();

                var desaData = await _desaRepository.GetInfoDesaAsync() ?? new DesaData();
                var dataToPrint = _semua
                    .Where(s => s.JenisSurat.Equals(_jenisSurat, StringComparison.OrdinalIgnoreCase) &&
                                (semuaTahun || s.TanggalSurat.Year == year))
                    .ToList();
                if (dataToPrint.Count == 0)
                {
                    await _messageService.ShowWarningAsync("Tidak ada data untuk dicetak.");
                    return;
                }

                var generator = new SuratKeluarMasukGenerator(desaData);
                using var stream = new MemoryStream();
                generator.GenerateAllSuratPdf(stream, dataToPrint, _jenisSurat, semuaTahun ? null : year);

                string tempPath = Path.Combine(Path.GetTempPath(), $"Arsip_{_jenisSurat}_{labelTahun.Replace(' ', '_')}.pdf");
                await File.WriteAllBytesAsync(tempPath, stream.ToArray());

                var preview = _previewFactory($"Arsip {_jenisSurat} {labelTahun}", tempPath);
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
