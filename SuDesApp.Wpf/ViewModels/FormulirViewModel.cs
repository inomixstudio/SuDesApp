using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Baris template dengan checkbox (padanan CheckedListBox pada dialog
    /// Formulir WinForms): Hapus bekerja pada item yang dicentang.
    /// </summary>
    public class FormulirTemplate : ObservableObject
    {
        private string _name;
        private bool _isChecked;

        public FormulirTemplate(string name, string? kode = null, long ukuranByte = 0, DateTime? diperbarui = null)
        {
            _name = name;
            KodeFormulir = kode ?? string.Empty;
            UkuranByte = ukuranByte;
            Diperbarui = diperbarui;

            var bagian = new List<string>();
            if (KodeFormulir.Length > 0) bagian.Add(KodeFormulir);
            if (UkuranByte > 0) bagian.Add(FormatUkuran(UkuranByte));
            if (Diperbarui.HasValue) bagian.Add(Diperbarui.Value.ToString("dd MMM yyyy"));
            RingkasanTeks = string.Join("  •  ", bagian);

            BukaCommand = new RelayCommand(() => SaatDibuka?.Invoke(this));
        }

        /// <summary>Dipanggil saat tombol Buka pada baris ini ditekan. Diisi oleh view model.</summary>
        internal Action<FormulirTemplate>? SaatDibuka { get; set; }

        /// <summary>Buka pratinjau PDF formulir ini di dalam aplikasi.</summary>
        public ICommand BukaCommand { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>Kode formulir dari nama berkas (mis. "F-1.01"); kosong bila tidak ber-kode.</summary>
        public string KodeFormulir { get; }

        public bool AdaKode => KodeFormulir.Length > 0;

        public long UkuranByte { get; }

        public DateTime? Diperbarui { get; }

        /// <summary>Baris keterangan singkat: kode • ukuran • tanggal berkas terakhir diubah.</summary>
        public string RingkasanTeks { get; }

        /// <summary>Nama berkas PDF-nya, dipakai sebagai tip bantuan pada baris.</summary>
        public string NamaBerkas => Name + ".pdf";

        internal static string FormatUkuran(long byteCount)
        {
            if (byteCount <= 0) return "-";
            if (byteCount < 1024) return $"{byteCount} B";
            if (byteCount < 1024 * 1024) return $"{Math.Max(1, byteCount / 1024)} KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }
    }

    /// <summary>
    /// Representasi satu baris formulir dari halaman jenis-formulir remote
    /// (infosadaradmindukkarawang.id/insankarawang/page/jenis-formulir).
    /// </summary>
    public class FormulirSourceEntry
    {
        public string NamaFormulir { get; set; } = string.Empty;
        public string LinkDownload { get; set; } = string.Empty;

        /// <summary>
        /// Nama dasar file (tanpa ekstensi) untuk template ini, ditetapkan saat
        /// parsing. Contoh: "F-1.01" atau "F-2.01_AKTA_KELAHIRAN".
        /// </summary>
        public string BaseFileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hasil parsing halaman jenis formulir.
    /// </summary>
    public class FormulirSourceResult
    {
        public bool Ok { get; set; }
        public IReadOnlyList<FormulirSourceEntry> Entries { get; set; } = Array.Empty<FormulirSourceEntry>();
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// ViewModel halaman "Pengaturan Formulir": kelola template PDF (tambah,
    /// hapus, perbarui) di folder Templates, plus unduh template dari sumber
    /// resmi (infosadaradmindukkarawang.id) dan, bila tersedia, sinkronkan
    /// ke Google Drive. Padanan dialog Formulir WinForms.
    /// </summary>
    public class FormulirViewModel : ObservableObject
    {
        private readonly AppConfig _appConfig;
        private readonly FileService _fileService;
        private readonly IMessageService _messageService;
        private readonly ILogger<FormulirViewModel> _logger;
        private readonly HttpClient _httpClient;
        private readonly GoogleDriveService? _driveService;
        private readonly NotificationService? _notifications;
        private readonly NavigationService? _navigation;
        private readonly Func<string, FormulirPdfViewModel>? _pdfPreviewFactory;

        /// <summary>Nama folder khusus template formulir di Google Drive.</summary>
        private const string DriveFormulirFolderName = "SuDesApp-Formulir";

        private bool _isLoading;
        private string _statusText = string.Empty;

        /// <summary>
        /// Popup tawaran unduh muncul bila jumlah template yang tampil lebih
        /// kecil dari nilai ini (daftar lengkap dari sumber resmi berisi 20).
        /// </summary>
        private const int DownloadPromptThreshold = 10;
        private bool _downloadPromptShown;

        public FormulirViewModel(
            AppConfig appConfig,
            FileService fileService,
            IMessageService messageService,
            FormulirMenuService formulirMenuService,
            ILogger<FormulirViewModel> logger,
            HttpClient? httpClient = null,
            GoogleDriveService? driveService = null,
            NotificationService? notifications = null,
            NavigationService? navigation = null,
            Func<string, FormulirPdfViewModel>? pdfPreviewFactory = null)
        {
            _appConfig = appConfig;
            _fileService = fileService;
            _messageService = messageService;
            _formulirMenuService = formulirMenuService;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _driveService = driveService;
            _notifications = notifications;
            _navigation = navigation;
            _pdfPreviewFactory = pdfPreviewFactory;

            Templates = new ObservableCollection<FormulirTemplate>();
            TemplatesTampil = new ObservableCollection<FormulirTemplate>();
            // Tombol Hapus hanya berguna bila ada baris yang dicentang.
            DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => AdaTerpilih);
            HasilUnduhanRincian = new ObservableCollection<string>();

            AddCommand = new AsyncRelayCommand(AddTemplateAsync);
            RefreshCommand = new AsyncRelayCommand(LoadTemplatesAsync);
            DownloadCommand = new AsyncRelayCommand(DownloadFromSourceAsync, () => !IsLoading);
            TogglePilihSemuaCommand = new RelayCommand(TogglePilihSemua);
            BukaFolderCommand = new RelayCommand(BukaFolderTemplate);
            TerimaTawaranUnduhCommand = new AsyncRelayCommand(AcceptDownloadOfferAsync);
            TutupTawaranUnduhCommand = new RelayCommand(TutupTawaranUnduh);
            TutupHasilUnduhanCommand = new RelayCommand(TutupHasilUnduhan);
            _ = LoadTemplatesAsync();
        }

        private readonly FormulirMenuService _formulirMenuService;

        /// <summary>Seluruh berkas template PDF di folder Templates (urutan abjad).</summary>
        public ObservableCollection<FormulirTemplate> Templates { get; }

        /// <summary>Baris yang benar-benar ditampilkan — hasil penyaringan kotak pencarian.</summary>
        public ObservableCollection<FormulirTemplate> TemplatesTampil { get; }

        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
        public bool HasTemplates => Templates.Count > 0;
        public bool IsEmpty => Templates.Count == 0;

        /// <summary>Jumlah template formulir yang tersedia, untuk statusbar.</summary>
        public int TemplateCount => Templates.Count;

        /// <summary>Jumlah baris yang tampil setelah disaring pencarian.</summary>
        public int JumlahTampil => TemplatesTampil.Count;

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // =====================================================================
        // Pencarian & pemilihan
        // =====================================================================

        private string _searchText = string.Empty;

        /// <summary>Kotak pencarian: menyaring nama berkas maupun kode formulir (mis. "F-1").</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    RefreshTampilan();
                }
            }
        }

        public bool AdaPencarian => _searchText.Trim().Length > 0;

        /// <summary>Benar bila pencarian aktif dan tidak ada baris yang cocok.</summary>
        public bool KosongKarenaPencarian => TemplatesTampil.Count == 0 && Templates.Count > 0;

        /// <summary>Benar bila folder Templates memang belum berisi PDF sama sekali.</summary>
        public bool KosongTanpaFormulir => Templates.Count == 0;

        public string PesanKosong => KosongTanpaFormulir
            ? "Belum ada template formulir di folder Templates. Gunakan tombol Tambah untuk memilih berkas PDF, " +
              "atau Unduh dari Sumber untuk mengambil formulir resmi."
            : $"Tidak ada formulir yang cocok dengan pencarian \u201c{_searchText.Trim()}\u201d.";

        public int JumlahTerpilih => Templates.Count(t => t.IsChecked);

        public bool AdaTerpilih => Templates.Count(t => t.IsChecked) > 0;

        /// <summary>Ringkasan pilihan untuk statusbar: "3 formulir dipilih" / "Belum ada yang dipilih".</summary>
        public string TeksTerpilih
        {
            get
            {
                int jumlah = Templates.Count(t => t.IsChecked);
                return jumlah == 0 ? "Belum ada yang dipilih" : $"{jumlah} formulir dipilih";
            }
        }

        /// <summary>Benar bila seluruh baris yang tampil sudah dicentang.</summary>
        public bool SemuaTampilTerpilih => TemplatesTampil.Count > 0 && TemplatesTampil.All(t => t.IsChecked);

        /// <summary>Label tombol pilih-semua yang berubah sesuai keadaan centang saat ini.</summary>
        public string TeksPilihSemua => SemuaTampilTerpilih ? "Kosongkan pilihan" : "Pilih semua tampil";

        public ICommand TogglePilihSemuaCommand { get; }

        /// <summary>Buka folder Templates di Windows Explorer (agar berkas mudah diperiksa/disalin).</summary>
        public ICommand BukaFolderCommand { get; }

        // =====================================================================
        // Ringkasan folder & sumber (kartu informasi halaman)
        // =====================================================================

        public string FolderTemplates => _appConfig.TemplateFolder;

        public string NamaFolderTemplates
        {
            get
            {
                var bersih = _appConfig.TemplateFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var nama = Path.GetFileName(bersih);
                return string.IsNullOrWhiteSpace(nama) ? "Templates" : nama;
            }
        }

        /// <summary>Jumlah formulir yang tersimpan beserta ukuran totalnya, untuk kartu ringkasan.</summary>
        public string RingkasanJumlah => Templates.Count == 0
            ? "Belum ada berkas"
            : $"{Templates.Count} formulir  •  {FormulirTemplate.FormatUkuran(Templates.Sum(t => t.UkuranByte))}";

        /// <summary>Alamat halaman sumber formulir resmi yang dipakai tombol Unduh dari Sumber.</summary>
        public string SumberFormulirTeks => SourceUrl.Host;

        /// <summary>Benar bila cadangan otomatis ke Google Drive ikut aktif (perlu login Drive).</summary>
        public bool CadanganDriveAktif => IsDriveReady && AppPreferenceStore.IsAutoBackupFormulirDriveEnabled();

        /// <summary>Keterangan keadaan cadangan Drive untuk kartu informasi — selalu bisa dibaca pengguna.</summary>
        public string KeteranganCadanganDrive
        {
            get
            {
                if (!AppPreferenceStore.IsAutoBackupFormulirDriveEnabled())
                {
                    return "Cadangan otomatis ke Google Drive sedang dimatikan di Pengaturan Aplikasi. " +
                           "Formulir tetap aman di folder Templates komputer ini.";
                }

                return IsDriveReady
                    ? "Cadangan otomatis ke Google Drive aktif. Formulir baru ikut diunggah ke folder " +
                      $"\u201c{DriveFormulirFolderName}\u201d supaya bisa dipakai ulang di komputer lain."
                    : "Belum masuk Google Drive, jadi cadangan otomatis belum berjalan — formulir tetap " +
                      "tersimpan di folder Templates. Masuk lewat menu Google (Login dengan Google) bila ingin " +
                      "cadangan otomatis dan unduhan dari Drive.";
            }
        }

        /// <summary>
        /// Drive dipakai hanya bila OAuth dikonfigurasi DAN token tersimpan
        /// (user sudah login). Tanpa token, Drive sengaja tidak disentuh agar
        /// proses unduh tidak memunculkan jendela browser login.
        /// </summary>
        private bool IsDriveReady =>
            _driveService != null && _driveService.IsOAuthEnabled && _driveService.HasStoredToken();

        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand DownloadCommand { get; }

        // =====================================================================
        // Kartu informasi inline (tawaran unduh & hasil unduhan)
        // =====================================================================

        private bool _adaTawaranUnduh;
        private string _tawaranUnduhTeks = string.Empty;

        /// <summary>Benar bila kartu tawaran "formulir belum lengkap" perlu ditampilkan.</summary>
        public bool AdaTawaranUnduh
        {
            get => _adaTawaranUnduh;
            private set => SetProperty(ref _adaTawaranUnduh, value);
        }

        public string TawaranUnduhTeks
        {
            get => _tawaranUnduhTeks;
            private set => SetProperty(ref _tawaranUnduhTeks, value ?? string.Empty);
        }

        public ICommand TerimaTawaranUnduhCommand { get; }
        public ICommand TutupTawaranUnduhCommand { get; }

        private bool _adaHasilUnduhan;
        private string _hasilUnduhanJudul = string.Empty;
        private string _hasilUnduhanRingkas = string.Empty;
        private bool _hasilUnduhanBerhasil = true;

        /// <summary>Benar bila kartu hasil unduhan terakhir perlu ditampilkan.</summary>
        public bool AdaHasilUnduhan
        {
            get => _adaHasilUnduhan;
            private set => SetProperty(ref _adaHasilUnduhan, value);
        }

        public string HasilUnduhanJudul
        {
            get => _hasilUnduhanJudul;
            private set => SetProperty(ref _hasilUnduhanJudul, value ?? string.Empty);
        }

        /// <summary>Ringkasan hasil: jumlah berhasil, dari Drive, dan yang dilewati.</summary>
        public string HasilUnduhanRingkas
        {
            get => _hasilUnduhanRingkas;
            private set => SetProperty(ref _hasilUnduhanRingkas, value ?? string.Empty);
        }

        /// <summary>True bila hasil terakhir berhasil (warna kartu hijau), false bila gagal (merah).</summary>
        public bool HasilUnduhanBerhasil
        {
            get => _hasilUnduhanBerhasil;
            private set => SetProperty(ref _hasilUnduhanBerhasil, value);
        }

        /// <summary>Rincian per formulir (satu baris per berkas), ditampilkan di kartu hasil.</summary>
        public ObservableCollection<string> HasilUnduhanRincian { get; }

        public ICommand TutupHasilUnduhanCommand { get; }

        /// <summary>Tutup kartu hasil unduhan (tombol ✕) — riwayatnya tetap ada di lonceng.</summary>
        private void TutupHasilUnduhan()
        {
            AdaHasilUnduhan = false;
            HasilUnduhanRincian.Clear();
        }

        private void TampilkanHasilUnduhan(
            string judul, string ringkas, IEnumerable<string> rincian, bool berhasil)
        {
            HasilUnduhanJudul = judul;
            HasilUnduhanRingkas = ringkas;
            HasilUnduhanBerhasil = berhasil;
            HasilUnduhanRincian.Clear();

            foreach (var baris in rincian)
            {
                HasilUnduhanRincian.Add(baris);
            }

            AdaHasilUnduhan = true;
        }

        /// <summary>
        /// Sembunyikan tawaran unduh dan jalankan unduhannya (dipakai tombol pada
        /// kartu tawaran). Pengguna yang menyetujui langsung melihat prosesnya
        /// berjalan di halaman ini — tanpa dialog popup.
        /// </summary>
        private async Task AcceptDownloadOfferAsync()
        {
            TutupTawaranUnduh();
            if (!IsLoading)
            {
                await DownloadFromSourceAsync();
            }
        }

        private void TutupTawaranUnduh()
        {
            AdaTawaranUnduh = false;
        }

        // =====================================================================
        // Pencarian, pemilihan, dan folder
        // =====================================================================

        /// <summary>Susun ulang daftar tampil menurut kotak pencarian dan hitung ulang penanda.</summary>
        private void RefreshTampilan()
        {
            var kata = _searchText.Trim();

            var tampil = string.IsNullOrEmpty(kata)
                ? Templates.ToList()
                : Templates
                    .Where(t => t.Name.Contains(kata, StringComparison.OrdinalIgnoreCase)
                             || t.KodeFormulir.Contains(kata, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            TemplatesTampil.Clear();
            foreach (var item in tampil)
            {
                TemplatesTampil.Add(item);
            }

            OnPropertyChanged(nameof(JumlahTampil));
            OnPropertyChanged(nameof(AdaPencarian));
            OnPropertyChanged(nameof(KosongKarenaPencarian));
            OnPropertyChanged(nameof(KosongTanpaFormulir));
            OnPropertyChanged(nameof(PesanKosong));
            PerbaruiPenandaPilihan();
        }

        /// <summary>Hitung ulang jumlah terpilih, label tombol, dan keaktifan tombol Hapus.</summary>
        private void PerbaruiPenandaPilihan()
        {
            OnPropertyChanged(nameof(JumlahTerpilih));
            OnPropertyChanged(nameof(AdaTerpilih));
            OnPropertyChanged(nameof(TeksTerpilih));
            OnPropertyChanged(nameof(SemuaTampilTerpilih));
            OnPropertyChanged(nameof(TeksPilihSemua));
            (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Centang/hapus centang seluruh baris yang tampil.</summary>
        private void TogglePilihSemua()
        {
            bool jadikanTerpilih = !SemuaTampilTerpilih;
            foreach (var item in TemplatesTampil)
            {
                item.IsChecked = jadikanTerpilih;
            }

            PerbaruiPenandaPilihan();
        }

        /// <summary>
        /// Buka pratinjau PDF satu formulir di dalam aplikasi (bukan pembaca PDF
        /// bawaan Windows). Bila berkasnya sudah dipindahkan/dihapus, keadaannya
        /// cukup dilaporkan di statusbar halaman — tanpa dialog.
        /// </summary>
        public void BukaFormulir(FormulirTemplate? formulir)
        {
            if (formulir == null) return;

            try
            {
                var jalur = FindTemplateFile(_appConfig.TemplateFolder, formulir.Name);
                if (jalur == null || !File.Exists(jalur))
                {
                    StatusText = $"Berkas {formulir.NamaBerkas} tidak ditemukan di folder Templates.";
                    return;
                }

                if (_navigation is null || _pdfPreviewFactory is null)
                {
                    StatusText = $"Pratinjau {formulir.NamaBerkas} tidak tersedia di halaman ini.";
                    return;
                }

                var pratinjau = _pdfPreviewFactory(formulir.Name);
                _navigation.Navigate(new Views.PdfPreviewView { DataContext = pratinjau });
                StatusText = $"Membuka pratinjau {formulir.NamaBerkas}...";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka pratinjau formulir {Nama}", formulir.NamaBerkas);
                StatusText = $"Berkas {formulir.NamaBerkas} tidak bisa dibuka dari sini.";
            }
        }

        /// <summary>Buka folder Templates di Windows Explorer.</summary>
        private void BukaFolderTemplate()
        {
            try
            {
                var folder = _appConfig.TemplateFolder;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                StatusText = $"Folder Templates dibuka: {folder}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder Templates");
                StatusText = "Folder Templates tidak dapat dibuka dari sini.";
            }
        }

        public async Task LoadTemplatesAsync()
        {
            try
            {
                IsLoading = true;
                StatusText = "Membaca folder Templates...";
                Templates.Clear();

                var templateFolder = _appConfig.TemplateFolder;
                if (!Directory.Exists(templateFolder))
                {
                    Directory.CreateDirectory(templateFolder);
                }

                // Nama, ukuran, dan tanggal ubah berkas dibaca sekaligus supaya
                // baris daftar bisa menampilkan keterangan yang berguna.
                var berkas = await Task.Run(() => Directory.GetFiles(templateFolder, "*.pdf")
                    .Select(f => new FileInfo(f))
                    .Where(fi => !string.IsNullOrWhiteSpace(fi.Name))
                    .GroupBy(fi => Path.GetFileNameWithoutExtension(fi.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(fi => fi.LastWriteTimeUtc).First())
                    .OrderBy(fi => Path.GetFileNameWithoutExtension(fi.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(fi => (
                        Nama: Path.GetFileNameWithoutExtension(fi.Name),
                        Ukuran: fi.Length,
                        Diubah: fi.LastWriteTime))
                    .ToList());

                foreach (var file in berkas)
                {
                    var item = new FormulirTemplate(
                        file.Nama,
                        GetFormCode(file.Nama),
                        file.Ukuran,
                        file.Diubah);

                    // Tombol Buka pada baris membuka pratinjau PDF-nya di dalam aplikasi.
                    item.SaatDibuka = BukaFormulir;

                    // Perubahan centang langsung memperbarui ringkasan pilihan & tombol Hapus.
                    item.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(FormulirTemplate.IsChecked))
                        {
                            PerbaruiPenandaPilihan();
                        }
                    };

                    Templates.Add(item);
                }

                OnPropertyChanged(nameof(HasTemplates));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(TemplateCount));
                OnPropertyChanged(nameof(RingkasanJumlah));
                OnPropertyChanged(nameof(KosongTanpaFormulir));
                _logger.LogInformation("Memuat {Count} template PDF", Templates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar template");
            }
            finally
            {
                IsLoading = false;
                RefreshTampilan();

                StatusText = Templates.Count == 0
                    ? "Folder Templates masih kosong."
                    : $"{Templates.Count} formulir siap dipakai.";

                // Tawarkan unduhan bila daftar template belum lengkap
                // (sekali per sesi aplikasi) — lewat kartu di halaman, bukan popup.
                TawarkanUnduhBilaBelumLengkap();
            }
        }

        /// <summary>
        /// Tawaran unduh formulir bila jumlah template masih di bawah
        /// <see cref="DownloadPromptThreshold"/>. Ditampilkan sebagai kartu di
        /// dalam halaman (bukan popup) dan hanya sekali per sesi aplikasi supaya
        /// tidak mengganggu setiap kali halaman dibuka.
        /// </summary>
        private void TawarkanUnduhBilaBelumLengkap()
        {
            if (_downloadPromptShown || Templates.Count >= DownloadPromptThreshold)
            {
                return;
            }

            _downloadPromptShown = true;
            TawaranUnduhTeks =
                $"Baru ada {Templates.Count} dari {DownloadPromptThreshold} formulir yang biasa dipakai. " +
                "Formulir resmi dapat diunduh langsung dari " + SumberFormulirTeks + " — formulir yang sudah ada " +
                "di folder Templates tidak akan diunduh ulang.";
            AdaTawaranUnduh = true;
        }

        /// <summary>Tambah template PDF baru (padanan formOpenFileDialog pada dialog Formulir).</summary>
        private async Task AddTemplateAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih file template formulir (PDF)",
                Filter = "PDF Files (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string sourcePath = dialog.FileName;
            if (!_fileService.IsValidPdf(sourcePath))
            {
                StatusText = "Berkas yang dipilih bukan PDF yang valid.";
                TampilkanHasilUnduhan(
                    "Berkas bukan PDF",
                    $"\u201c{Path.GetFileName(sourcePath)}\u201d tidak bisa dipakai sebagai template formulir.",
                    new[] { "Pilih berkas PDF yang bisa dibuka di pembaca PDF, lalu coba lagi." },
                    berhasil: false);
                return;
            }

            var templateFolder = _appConfig.TemplateFolder;
            if (!Directory.Exists(templateFolder))
            {
                Directory.CreateDirectory(templateFolder);
            }

            // Normalisasi nama seperti WinForms: spasi -> underscore, huruf besar.
            string baseName = Path.GetFileNameWithoutExtension(sourcePath)
                .Replace(" ", "_")
                .ToUpperInvariant();
            string destinationPath = Path.Combine(templateFolder, $"{baseName}.pdf");

            if (_fileService.FileExists(destinationPath))
            {
                bool overwrite = await _messageService.ShowConfirmationAsync(
                    "Template sudah ada",
                    $"{baseName}.pdf sudah ada di folder Templates. Timpa file lama?");
                if (!overwrite)
                {
                    return;
                }
            }

            try
            {
                await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite: true));
                _logger.LogInformation("Template ditambahkan: {Path}", destinationPath);
                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();
                StatusText = $"Template {baseName}.pdf ditambahkan.";

                // Backup otomatis ke Drive (bila diaktifkan di Pengaturan Aplikasi).
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin template: {Path}", destinationPath);

                // Kesalahan ditampilkan sebagai kartu di halaman, bukan dialog.
                TampilkanHasilUnduhan(
                    "Gagal menyalin template",
                    $"Template {baseName}.pdf tidak bisa disalin ke folder Templates.",
                    new[] { ex.Message }, berhasil: false);
                StatusText = "Gagal menyalin template PDF.";
            }
        }

        /// <summary>Hapus semua template yang dicentang (padanan TombolHapus dialog Formulir).</summary>
        private async Task DeleteSelectedAsync()
        {
            var checkedItems = Templates.Where(t => t.IsChecked).ToList();
            if (checkedItems.Count == 0)
            {
                return;
            }

            // Penghapusan berkas tetap meminta konfirmasi — berisiko menghilangkan
            // berkas pengguna, jadi layak dikonfirmasi lebih dulu.
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Hapus template",
                checkedItems.Count == 1
                    ? $"Hapus template \"{checkedItems[0].Name}\" dari folder Templates?"
                    : $"Hapus {checkedItems.Count} template terpilih dari folder Templates?");
            if (!confirmed)
            {
                return;
            }

            var templateFolder = _appConfig.TemplateFolder;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in checkedItems)
                    {
                        string? filePath = FindTemplateFile(templateFolder, item.Name);
                        if (filePath != null && File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            _logger.LogInformation("Template dihapus: {Path}", filePath);
                        }
                    }
                });

                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();
                StatusText = checkedItems.Count == 1
                    ? $"Template {checkedItems[0].Name}.pdf dihapus."
                    : $"{checkedItems.Count} template dihapus dari folder Templates.";

                // Pastikan sisa template tetap tercadangkan di Drive.
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus template");
                StatusText = "Gagal menghapus template.";
                TampilkanHasilUnduhan(
                    "Gagal menghapus template",
                    $"{checkedItems.Count} berkas tidak bisa dihapus dari folder Templates.",
                    new[] { ex.Message }, berhasil: false);
            }
        }

        /// <summary>
        /// Temukan file template PDF untuk nama base tertentu, toleran terhadap
        /// perbedaan huruf besar/kecil pada nama file di folder Templates.
        /// </summary>
        private static string? FindTemplateFile(string templateFolder, string baseName)
        {
            string exact = Path.Combine(templateFolder, $"{baseName}.pdf");
            if (File.Exists(exact))
            {
                return exact;
            }

            return Directory.GetFiles(templateFolder, "*.pdf")
                .FirstOrDefault(f => string.Equals(
                    Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Normalisasi nama file dari judul formulir asing:
        /// huruf besar, spasi -> underscore, karakter tidak diizinkan diganti _.
        /// </summary>
        public static string NormalizeTemplateName(string title)
        {
            var s = title.Trim();
            s = new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            s = Regex.Replace(s, @"\s+", "_");
            if (string.IsNullOrWhiteSpace(s)) s = "FORMULIR";
            return s.ToUpperInvariant();
        }

        /// <summary>
        /// Ambil kode formulir dari judul (mis. "Formulir F-1.02, digunakan
        /// untuk ..." → "F-1.02"). Null bila judul tidak memuat kode.
        /// </summary>
        public static string? GetFormCode(string title)
        {
            var m = Regex.Match(title.Trim(), @"^(?:formulir\s+)?(?<kode>F-\d+(?:\.\d+)?)\b",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["kode"].Value.ToUpperInvariant() : null;
        }

        /// <summary>
        /// Nama dasar file template dari judul formulir halaman sumber.
        /// - Judul dengan kode yang hanya dipakai satu formulir memakai kode
        ///   polos: "F-1.01, Formulir Biodata Keluarga" → "F-1.01".
        /// - Judul dengan kode yang dipakai beberapa formulir (mis. lima varian
        ///   "F-2.01 ...") diberi akhiran deskriptor: "F-2.01_AKTA_KELAHIRAN".
        /// - Judul tanpa kode dipotong di koma pertama lalu dinormalisasi.
        /// </summary>
        public static string BuildTemplateBaseName(string title, bool codeIsShared)
        {
            var m = Regex.Match(title.Trim(), @"^(?:formulir\s+)?(?<kode>F-\d+(?:\.\d+)?)\b[\s,:-]*(?<sisa>.*)$",
                RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                // Tanpa kode: potong di koma pertama agar tidak memuat keterangan.
                var shortTitle = title.Split(',')[0].Trim();
                return NormalizeTemplateName(shortTitle);
            }

            var kode = m.Groups["kode"].Value.ToUpperInvariant();
            if (!codeIsShared)
            {
                return kode;
            }

            // Deskriptor: teks sebelum koma pertama (tanpa tanda kurung),
            // maksimal 4 kata.
            var sisa = m.Groups["sisa"].Value.Split(',')[0]
                .Replace("(", " ").Replace(")", " ").Trim();
            var kata = sisa.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();
            if (kata.Length == 0)
            {
                return kode;
            }

            return $"{kode}_{NormalizeTemplateName(string.Join(' ', kata))}";
        }

        /// <summary>
        /// Unduhan formulir dari sumber resmi (infosadaradmindukkarawang.id).
        /// Alur:
        /// 1. Ambil halaman jenis-formulir.
        /// 2. Parse daftar nama + link.
        /// 3. Untuk tiap formulir yang belum ada di Templates, unduh filenya —
        ///    prioritas dari folder SuDesApp-Formulir di Google Drive (bila user
        ///    login), fallback ke sumber resmi.
        /// 4. Template baru diunggah ke folder SuDesApp-Formulir di Drive agar
        ///    instalasi berikutnya bisa mengambil dari Drive, bukan web lagi.
        /// </summary>
        private async Task DownloadFromSourceAsync()
        {
            if (IsLoading) return;

            // Unduhan manual: sembunyikan popup otomatis untuk sesi ini agar
            // tidak muncul lagi tepat setelah proses selesai.
            _downloadPromptShown = true;

            IsLoading = true;
            StatusText = "Mengambil daftar formulir dari sumber resmi...";
            try
            {
                var result = await FetchFormulirSourceListAsync(CancellationToken.None);
                if (!result.Ok)
                {
                    StatusText = "Gagal mengambil daftar formulir.";
                    TampilkanHasilUnduhan(
                        "Gagal mengambil daftar formulir",
                        $"Daftar formulir dari {SumberFormulirTeks} belum bisa dibaca.",
                        new[]
                        {
                            result.ErrorMessage ?? "Penyebabnya tidak diketahui.",
                            "Formulir yang sudah ada di folder Templates tetap bisa dipakai seperti biasa."
                        },
                        berhasil: false);
                    return;
                }

                if (result.Entries.Count == 0)
                {
                    StatusText = "Tidak ada formulir yang ditemukan di halaman sumber.";
                    TampilkanHasilUnduhan(
                        "Daftar formulir kosong",
                        $"Halaman sumber {SumberFormulirTeks} tidak memuat tautan formulir saat diperiksa.",
                        new[] { "Formulir yang ada di folder Templates tetap bisa dipakai seperti biasa." },
                        berhasil: false);
                    return;
                }

                // Kemajuan proses terlihat di statusbar halaman; rinciannya
                // ditampilkan sebagai kartu di halaman, bukan dialog popup.
                StatusText = $"Ditemukan {result.Entries.Count} formulir. Memulai unduh...";
                TampilkanHasilUnduhan(
                    "Mengunduh formulir resmi",
                    $"{result.Entries.Count} formulir ditemukan di {SumberFormulirTeks}. " +
                    "Formulir yang sudah ada di folder Templates akan dilewati.",
                    new[]
                    {
                        CadanganDriveAktif
                            ? $"Formulir diambil dari Google Drive lebih dulu, lalu dari sumber resmi bila belum ada."
                            : "Google Drive belum aktif — formulir diunduh langsung dari sumber resmi."
                    },
                    berhasil: true);

                var templateFolder = _appConfig.TemplateFolder;
                if (!Directory.Exists(templateFolder))
                {
                    Directory.CreateDirectory(templateFolder);
                }

                var driveFiles = await TryGetDriveTemplateFilesAsync(CancellationToken.None);

                int downloaded = 0;
                int skipped = 0;
                int fromDrive = 0;
                var fileResults = new List<string>();
                foreach (var entry in result.Entries)
                {
                    StatusText = $"Memproses: {entry.NamaFormulir}...";
                    var (downloadedPath, fromDriveItem, note) = await DownloadOneTemplateAsync(entry, templateFolder, driveFiles, CancellationToken.None);
                    if (downloadedPath != null)
                    {
                        downloaded++;
                        if (fromDriveItem) fromDrive++;
                        fileResults.Add($"✓ {entry.BaseFileName} ({(fromDriveItem ? "Drive" : "sumber")})");
                    }
                    else
                    {
                        skipped++;
                        fileResults.Add($"– {entry.BaseFileName}: {note}");
                    }
                }

                StatusText = $"Selesai. Berhasil {downloaded}, dilewati {skipped}.";

                // Notifikasi lonceng status bar.
                if (downloaded > 0)
                {
                    _notifications?.Success(
                        "Unduh Formulir",
                        $"{downloaded} formulir baru tersimpan di folder Templates" +
                        (fromDrive > 0 ? $" ({fromDrive} dari Google Drive)." : "."));
                }
                else
                {
                    _notifications?.Info(
                        "Unduh Formulir",
                        $"Tidak ada formulir baru — {skipped} sudah ada di Templates.");
                }
                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();

                if (downloaded > 0)
                {
                    await TryUploadTemplatesToDriveAsync(templateFolder);
                }

                StatusText = downloaded > 0
                    ? $"Unduhan selesai — {downloaded} formulir baru siap dipakai."
                    : $"Unduhan selesai — tidak ada formulir baru ({skipped} sudah tersedia).";

                TampilkanHasilUnduhan(
                    downloaded > 0 ? "Unduhan formulir selesai" : "Semua formulir sudah tersedia",
                    $"Berhasil {downloaded}  •  dari Drive {fromDrive}  •  dari sumber {downloaded - fromDrive}  •  " +
                    $"dilewati/gagal {skipped}",
                    fileResults
                        .Append("Folder tujuan: " + templateFolder)
                        .ToList(),
                    berhasil: downloaded > 0 && skipped == 0);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Unduhan dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh formulir dari sumber resmi");
                StatusText = "Unduhan gagal.";
                TampilkanHasilUnduhan(
                    "Unduhan gagal",
                    $"Proses unduh formulir dari {SumberFormulirTeks} terhenti.",
                    new[]
                    {
                        ex.Message,
                        "Periksa koneksi internet lalu tekan Unduh dari Sumber lagi — formulir yang sudah terunduh tidak diulang."
                    },
                    berhasil: false);
            }
            finally
            {
                IsLoading = false;
            }
        }


        /// <summary>
        /// Ambil daftar formulir dari halaman jenis-formulir resmi.
        /// </summary>
        private async Task<FormulirSourceResult> FetchFormulirSourceListAsync(CancellationToken ct)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(SourceUrl, ct);
                var entries = ParseFormulirEntries(html);
                return new FormulirSourceResult
                {
                    Ok = true,
                    Entries = entries,
                };
            }
            catch (OperationCanceledException)
            {
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = "Unduhan daftar dibatalkan."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gagal menghubungi sumber formulir");
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = $"Tidak dapat menghubungi server sumber formulir.\n\nKode kesalahan: {ex.StatusCode}\n\n" +
                        "Pastikan koneksi internet aktif dan server sumber formulir dapat diakses.\n\n" +
                        "Formulir yang ada di folder Templates tetap tersedia."
                };
            }
            catch (Exception ex) when (ex is TaskCanceledException)
            {
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = "Waktu unduh daftar habis. Server sumber formulir mungkin lambat.\n\n" +
                        "Coba lagi atau gunakan formulir yang sudah ada di Templates."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil daftar formulir");
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = $"Gagal mengambil daftar formulir.\n\nDetail: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parse nama dan link formulir dari halaman HTML sumber.
        /// Mendukung dua pola link:
        /// 1. Link Google Drive share (drive.google.com/file/d/ID/...) — pola yang
        ///    dipakai halaman jenis-formulir; nama formulir diambil dari teks
        ///    paragraf di dalam anchor.
        /// 2. Link file dokumen langsung (.pdf/.doc/.docx).
        /// </summary>
        private static IReadOnlyList<FormulirSourceEntry> ParseFormulirEntries(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return Array.Empty<FormulirSourceEntry>();
            }

            var results = new List<FormulirSourceEntry>();

            var anchorPattern = new Regex(
                @"<a\s[^>]*href\s*=\s*""(?<url>[^""]+)""[^>]*>(?<inner>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

            foreach (Match m in anchorPattern.Matches(html))
            {
                var url = m.Groups["url"].Value.Trim();
                if (string.IsNullOrEmpty(url)) continue;

                // Teks nama formulir: gabungkan teks polos di dalam anchor.
                var innerText = Regex.Replace(m.Groups["inner"].Value, @"<script.*?</script>|<style.*?</style>", " ",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                innerText = Regex.Replace(innerText, @"<[^>]+>", " ");
                innerText = System.Net.WebUtility.HtmlDecode(innerText);
                innerText = Regex.Replace(innerText, @"\s+", " ").Trim();

                // Pola 1: Google Drive share link.
                var driveMatch = Regex.Match(url, @"drive\.google\.com/(?:file/d/|open\?id=)(?<id>[-\w]{10,})",
                    RegexOptions.IgnoreCase);
                if (driveMatch.Success)
                {
                    var driveId = driveMatch.Groups["id"].Value;
                    var name = FirstMeaningfulLine(innerText);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "FORMULIR_" + driveId;
                    }

                    results.Add(new FormulirSourceEntry
                    {
                        NamaFormulir = name,
                        // URL unduh langsung untuk file publik; endpoint
                        // usercontent + confirm=t menghindari halaman perantara
                        // (virus-scan notice) sehingga selalu mengembalikan PDF.
                        LinkDownload = $"https://drive.usercontent.google.com/download?id={driveId}&export=download&confirm=t",
                    });
                    continue;
                }

                // Pola 2: link dokumen langsung (.pdf/.doc/.docx).
                var lowerUrl = url.ToLowerInvariant();
                if (lowerUrl.EndsWith(".pdf") || lowerUrl.EndsWith(".doc") || lowerUrl.EndsWith(".docx"))
                {
                    var name = FirstMeaningfulLine(innerText);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = Path.GetFileNameWithoutExtension(url);
                    }
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    results.Add(new FormulirSourceEntry
                    {
                        NamaFormulir = name,
                        LinkDownload = url,
                    });
                }
            }

            // Tetapkan nama dasar file: kode yang dipakai satu formulir saja
            // memakai kode polos; kode bersama diberi akhiran deskriptor.
            var codeCounts = results
                .Select(e => GetFormCode(e.NamaFormulir))
                .Where(c => c != null)
                .GroupBy(c => c!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var e in results)
            {
                var code = GetFormCode(e.NamaFormulir);
                var shared = code != null && codeCounts[code] > 1;
                e.BaseFileName = BuildTemplateBaseName(e.NamaFormulir, shared);
            }

            return results
                .GroupBy(e => e.BaseFileName + "|" + e.LinkDownload, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(e => e.BaseFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Ambil baris teks bermakna pertama dari teks anchor (buang teks
        /// navigasi generik seperti "Home", "Download", dsb.).
        /// </summary>
        private static string? FirstMeaningfulLine(string text)
        {
            var generik = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "home", "download", "unduh", "beranda", "menu", "login", "daftar"
            };

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Length < 3 || generik.Contains(t)) continue;
                if (t.Length > 200) t = t.Substring(0, 200).Trim();
                return t;
            }
            return null;
        }

        private static readonly Uri SourceUrl = new("https://infosadaradmindukkarawang.id/insankarawang/page/jenis-formulir");

        /// <summary>
        /// Unduh satu template dari entri sumber.
        /// Prioritas: Google Drive (cadangan backup sebelumnya) → sumber resmi.
        /// Penulisan file atomik: tulis ke .tmp, validasi, baru di-rename.
        /// Kembalikan (path, dariDrive, catatan); path null berarti gagal/dilewati.
        /// </summary>
        private async Task<(string? Path, bool FromDrive, string Note)> DownloadOneTemplateAsync(
            FormulirSourceEntry entry, string templateFolder,
            IReadOnlyDictionary<string, DriveItem>? driveFiles, CancellationToken ct)
        {
            var fileName = entry.BaseFileName + ".pdf";
            var destPath = Path.Combine(templateFolder, fileName);

            if (_fileService.FileExists(destPath))
            {
                _logger.LogInformation("Template sudah ada, dilewati: {Name}", fileName);
                return (null, false, "sudah ada");
            }

            // 1) Coba dari Google Drive dulu (folder SuDesApp-Formulir hasil
            //    backup otomatis). Bila tersedia, web sumber tidak perlu diakses.
            if (driveFiles != null && driveFiles.TryGetValue(entry.BaseFileName, out var driveItem) && !driveItem.IsFolder)
            {
                StatusText = $"Mengunduh dari Google Drive: {entry.NamaFormulir}...";
                try
                {
                    var tmpPath = destPath + ".tmp";
                    await _driveService!.DownloadToFileAsync(driveItem.Id, tmpPath, null, ct);
                    if (_fileService.IsValidPdf(tmpPath))
                    {
                        File.Move(tmpPath, destPath, overwrite: true);
                        _logger.LogInformation("Template diunduh dari Google Drive: {Name} (ID: {Id})", fileName, driveItem.Id);
                        return (destPath, true, "");
                    }

                    File.Delete(tmpPath);
                    _logger.LogWarning("File dari Google Drive bukan PDF valid: {Name}", driveItem.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal mengunduh dari Google Drive: {Name}; fallback ke sumber resmi.", fileName);
                    if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp");
                }
            }

            // 2) Fallback: unduh dari sumber resmi.

            // Jika link relatif, gabungkan dengan base URL
            var downloadUrl = entry.LinkDownload;
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absoluteUrl))
            {
                absoluteUrl = new Uri(SourceUrl, downloadUrl);
            }

            StatusText = $"Mengunduh: {entry.NamaFormulir}...";
            var tmp = destPath + ".tmp";
            try
            {
                using var response = await _httpClient.GetAsync(absoluteUrl, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await stream.CopyToAsync(fileStream, ct);
                await fileStream.FlushAsync(ct);
                fileStream.Close();

                // Validasi hasil unduhan berupa PDF SEBELUM dipakai.
                if (!_fileService.IsValidPdf(tmp))
                {
                    var head = ReadFileHead(tmp);
                    File.Delete(tmp);
                    _logger.LogWarning("File yang diunduh bukan PDF valid ({Head}): {Url}", head, absoluteUrl);
                    return (null, false, $"bukan PDF valid (awalan: {head})");
                }

                File.Move(tmp, destPath, overwrite: true);
                _logger.LogInformation("Template diunduh dari sumber: {Name} <- {Url}", fileName, absoluteUrl);
                return (destPath, false, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh template: {Name} <- {Url}", entry.NamaFormulir, absoluteUrl);
                if (File.Exists(tmp)) File.Delete(tmp);
                return (null, false, ex.Message);
            }
        }

        /// <summary>Baca 16 byte pertama file untuk diagnosa konten unduhan.</summary>
        private static string ReadFileHead(string path)
        {
            try
            {
                var bytes = new byte[16];
                using var fs = File.OpenRead(path);
                var n = fs.Read(bytes, 0, bytes.Length);
                return System.Text.Encoding.ASCII.GetString(bytes, 0, n)
                    .Replace("\r", " ").Replace("\n", " ");
            }
            catch
            {
                return "(tidak terbaca)";
            }
        }

        /// <summary>
        /// Ambil indeks file template dari folder SuDesApp-Formulir di Google
        /// Drive (kunci: nama file tanpa ekstensi, huruf besar). Kembalikan null
        /// bila user belum login Drive atau terjadi kesalahan — pemanggil lalu
        /// fallback ke sumber resmi.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, DriveItem>?> TryGetDriveTemplateFilesAsync(CancellationToken ct)
        {
            if (!IsDriveReady)
            {
                return null;
            }

            try
            {
                var folderId = await _driveService!.FindOrCreateFolderAsync(DriveFormulirFolderName, null, ct);
                var items = await _driveService.ListItemsAsync(folderId, ct);

                var map = new Dictionary<string, DriveItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    if (item.IsFolder) continue;

                    var key = Path.GetFileNameWithoutExtension(item.Name);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        map[key.ToUpperInvariant()] = item;
                    }
                }

                _logger.LogInformation("Indeks template Drive: {Count} file di folder {Folder}", map.Count, DriveFormulirFolderName);
                return map;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil daftar template dari Google Drive; fallback ke sumber resmi.");
                return null;
            }
        }

        /// <summary>
        /// Jika user login Google Drive, unggah template yang belum ada ke folder
        /// SuDesApp-Formulir di Drive sebagai cadangan — sehingga instalasi lain
        /// (atau komputer ini di kemudian hari) bisa mengambil dari Drive tanpa
        /// menyentuh web sumber lagi.
        /// </summary>
        private async Task TryUploadTemplatesToDriveAsync(string templateFolder)
        {
            if (!IsDriveReady || !AppPreferenceStore.IsAutoBackupFormulirDriveEnabled())
            {
                return;
            }

            try
            {
                StatusText = "Menyinkronkan template ke Google Drive...";
                var targetFolderId = await _driveService!.FindOrCreateFolderAsync(DriveFormulirFolderName, null, CancellationToken.None);

                // Hindari duplikat: lewati file yang sudah ada di folder Drive.
                var existing = await _driveService.ListItemsAsync(targetFolderId, CancellationToken.None);
                var existingNames = existing.Where(i => !i.IsFolder)
                    .Select(i => i.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(templateFolder, "*.pdf"))
                {
                    var name = Path.GetFileName(file);
                    if (existingNames.Contains(name)) continue;

                    try
                    {
                        await _driveService.UploadFileAsync(file, targetFolderId, fileName: name, ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal mengunggah template ke Drive: {File}", name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengunggah template ke Google Drive");
            }
        }
    }

    internal class FormulirSourceEntryComparer : IEqualityComparer<FormulirSourceEntry>
    {
        public bool Equals(FormulirSourceEntry? x, FormulirSourceEntry? y)
        {
            if (x is null || y is null) return false;
            return string.Equals(x.BaseFileName, y.BaseFileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.LinkDownload, y.LinkDownload, StringComparison.Ordinal);
        }

        public int GetHashCode(FormulirSourceEntry obj)
        {
            return HashCode.Combine(obj.BaseFileName, obj.LinkDownload);
        }
    }
}
