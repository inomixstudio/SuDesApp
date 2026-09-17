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

        public FormulirTemplate(string name)
        {
            _name = name;
        }

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
            NotificationService? notifications = null)
        {
            _appConfig = appConfig;
            _fileService = fileService;
            _messageService = messageService;
            _formulirMenuService = formulirMenuService;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _driveService = driveService;
            _notifications = notifications;

            Templates = new ObservableCollection<FormulirTemplate>();
            AddCommand = new AsyncRelayCommand(AddTemplateAsync);
            DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync);
            RefreshCommand = new AsyncRelayCommand(LoadTemplatesAsync);
            DownloadCommand = new AsyncRelayCommand(DownloadFromSourceAsync, () => !IsLoading);
            _ = LoadTemplatesAsync();
        }

        private readonly FormulirMenuService _formulirMenuService;

        public ObservableCollection<FormulirTemplate> Templates { get; }

        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
        public bool HasTemplates => Templates.Count > 0;
        public bool IsEmpty => Templates.Count == 0;

        /// <summary>Jumlah template formulir yang tampil, untuk statusbar.</summary>
        public int TemplateCount => Templates.Count;

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

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

        public async Task LoadTemplatesAsync()
        {
            try
            {
                IsLoading = true;
                Templates.Clear();

                var templateFolder = _appConfig.TemplateFolder;
                if (!Directory.Exists(templateFolder))
                {
                    Directory.CreateDirectory(templateFolder);
                }

                var pdfFiles = await Task.Run(() => Directory.GetFiles(templateFolder, "*.pdf")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

                foreach (var file in pdfFiles)
                {
                    Templates.Add(new FormulirTemplate(file));
                }

                OnPropertyChanged(nameof(HasTemplates));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(TemplateCount));
                _logger.LogInformation("Memuat {Count} template PDF", pdfFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar template");
            }
            finally
            {
                IsLoading = false;

                // Tawarkan unduhan bila daftar template belum lengkap
                // (sekali per sesi aplikasi). Harus setelah loading selesai.
                _ = MaybePromptDownloadAsync(Templates.Count);
            }
        }

        /// <summary>
        /// Popup tawaran unduh formulir: muncul otomatis bila jumlah template
        /// yang tampil kurang dari DownloadPromptThreshold. Hanya sekali per
        /// sesi agar tidak mengganggu setiap kali halaman dibuka.
        /// </summary>
        private async Task MaybePromptDownloadAsync(int templateCount)
        {
            if (_downloadPromptShown || templateCount >= DownloadPromptThreshold)
            {
                return;
            }

            _downloadPromptShown = true;
            try
            {
                var accepted = await _messageService.ShowConfirmationAsync(
                    "Formulir belum lengkap",
                    $"Baru ada {templateCount} template formulir di aplikasi ini.\n\n" +
                    "Unduh formulir resmi dari sumber (infosadaradmindukkarawang.id) sekarang?\n" +
                    "Formulir yang sudah ada di folder Templates akan dilewati.");

                if (accepted && !IsLoading)
                {
                    await DownloadFromSourceAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menampilkan popup tawaran unduh formulir");
            }
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
                await _messageService.ShowErrorAsync("File yang dipilih bukan PDF yang valid.");
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

                // Backup otomatis ke Drive (bila diaktifkan di Pengaturan Aplikasi).
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin template: {Path}", destinationPath);
                await _messageService.ShowErrorAsync("Gagal menyalin template PDF.");
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

                // Pastikan sisa template tetap tercadangkan di Drive.
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus template");
                await _messageService.ShowErrorAsync("Gagal menghapus template.");
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
                    await _messageService.ShowErrorAsync(
                        "Gagal mengambil daftar formulir dari sumber resmi.\n\n" + result.ErrorMessage);
                    StatusText = "Gagal mengambil daftar formulir.";
                    return;
                }

                if (result.Entries.Count == 0)
                {
                    await _messageService.ShowInfoAsync(
                        "Daftar formulir kosong atau tidak ditemukan di halaman sumber.\n\n" +
                        "Formulir yang ada di folder Templates tetap tersedia untuk digunakan.");
                    StatusText = "Tidak ada formulir yang ditemukan di halaman sumber.";
                    return;
                }

                StatusText = $"Ditemukan {result.Entries.Count} formulir. Memulai unduh...";
                await _messageService.ShowInfoAsync(
                    $"Ditemukan {result.Entries.Count} formulir di sumber resmi.\n\n" +
                    "Aplikasi akan mengunduh formulir yang belum ada di folder Templates.\n" +
                    "Formulir diambil lebih dulu dari Google Drive (bila Anda login);\n" +
                    "bila tidak tersedia di Drive, diunduh dari sumber resmi.\n\n" +
                    "Program akan langsung berjalan. Tunggu sampai semua formulir selesai diunduh.");

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

                await _messageService.ShowInfoAsync(
                    $"Unduhan formulir selesai.\n\n" +
                    $"Berhasil mengunduh: {downloaded}\n" +
                    $"  • Dari Google Drive: {fromDrive}\n" +
                    $"  • Dari sumber resmi: {downloaded - fromDrive}\n" +
                    $"Dilewati/gagal: {skipped}\n\n" +
                    $"Folder tujuan:\n{templateFolder}\n\n" +
                    "Rincian:\n" + string.Join("\n", fileResults));
            }
            catch (OperationCanceledException)
            {
                StatusText = "Unduhan dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh formulir dari sumber resmi");
                StatusText = "Unduhan gagal.";
                await _messageService.ShowErrorAsync(
                    "Gagal mengunduh formulir dari sumber resmi.\n\n" + ex.Message);
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
