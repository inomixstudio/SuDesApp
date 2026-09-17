using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel halaman Ekspor/Impor Database — padanan ExImdb (WinForms).
    /// Satu VM dipakai untuk dua mode (impor/ekspor); MainWindowViewModel
    /// memanggil SetMode sebelum navigasi.
    /// </summary>
    public class ExImdbViewModel : ObservableObject
    {
        private readonly DatabaseImportExportService _service;
        private readonly IMessageService _messageService;
        private readonly ILogger<ExImdbViewModel> _logger;

        private bool _isImportMode;
        private string _selectedPath = string.Empty;
        private bool _isBusy;
        private string _headerTitle = "EXPORT DATABASE";
        private string _subtitle = "Buat salinan cadangan database";
        private string _infoText = "Pilih lokasi untuk menyimpan database:";
        private string _actionLabel = "Export";

        public ExImdbViewModel(
            DatabaseImportExportService service,
            IMessageService messageService,
            ILogger<ExImdbViewModel> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            BrowseCommand = new RelayCommand(Browse);
            ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsBusy);
        }

        /// <summary>Atur mode halaman sesuai pilihan menu navigasi.</summary>
        public void SetMode(bool isImport)
        {
            _isImportMode = isImport;
            if (isImport)
            {
                HeaderTitle = "IMPORT DATABASE";
                Subtitle = "Pulihkan database dari file eksternal";
                InfoText = "Pilih file database untuk diimpor:";
                ActionLabel = "Import";
            }
            else
            {
                HeaderTitle = "EXPORT DATABASE";
                Subtitle = "Buat salinan cadangan database";
                InfoText = "Pilih lokasi untuk menyimpan database:";
                ActionLabel = "Export";
            }
        }

        public string HeaderTitle { get => _headerTitle; private set => SetProperty(ref _headerTitle, value); }
        public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
        public string InfoText { get => _infoText; private set => SetProperty(ref _infoText, value); }
        public string ActionLabel { get => _actionLabel; private set => SetProperty(ref _actionLabel, value); }
        public string SelectedPath { get => _selectedPath; set => SetProperty(ref _selectedPath, value); }
        public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

        public ICommand BrowseCommand { get; }
        public ICommand ExecuteCommand { get; }

        private void Browse()
        {
            string currentDbPath;
            try
            {
                currentDbPath = _service.GetDatabasePath();
            }
            catch
            {
                currentDbPath = string.Empty;
            }

            string initialDir = !string.IsNullOrWhiteSpace(currentDbPath) && File.Exists(currentDbPath)
                ? Path.GetDirectoryName(currentDbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (_isImportMode)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pilih File Database untuk Diimpor",
                    Filter = "SQLite Database Files (*.db)|*.db|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false,
                    InitialDirectory = initialDir
                };
                if (dialog.ShowDialog() == true)
                {
                    SelectedPath = dialog.FileName;
                }
            }
            else
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Simpan Database",
                    Filter = "SQLite Database Files (*.db)|*.db|All Files (*.*)|*.*",
                    FileName = $"desa_export_{DateTime.Now:yyyyMMddHHmmss}.db",
                    OverwritePrompt = true,
                    InitialDirectory = initialDir
                };
                if (dialog.ShowDialog() == true)
                {
                    SelectedPath = dialog.FileName;
                }
            }
        }

        private async Task ExecuteAsync()
        {
            if (IsBusy)
            {
                return;
            }

            string path = SelectedPath?.Trim() ?? string.Empty;
            if (!ValidatePath(path))
            {
                return;
            }

            IsBusy = true;
            try
            {
                if (_isImportMode)
                {
                    _logger.LogInformation("Memulai impor database dari: {Path}", path);
                    await _service.ImportDatabaseAsync(path);
                    await _messageService.ShowInfoAsync("Database berhasil diimpor!");
                }
                else
                {
                    _logger.LogInformation("Memulai ekspor database ke: {Path}", path);
                    await _service.ExportDatabaseAsync(path);
                    await _messageService.ShowInfoAsync("Database berhasil diekspor!");
                }
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "File tidak ditemukan: {Path}", path);
                await _messageService.ShowErrorAsync($"File tidak ditemukan: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operasi tidak valid: {Path}", path);
                await _messageService.ShowErrorAsync($"Operasi gagal: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kesalahan tak terduga: {Path}", path);
                await _messageService.ShowErrorAsync($"Terjadi kesalahan: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _ = _messageService.ShowErrorAsync("Path file tidak boleh kosong.");
                return false;
            }
            if (_isImportMode && !File.Exists(path))
            {
                _ = _messageService.ShowErrorAsync("File tidak ditemukan pada path yang dipilih.");
                return false;
            }
            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                _ = _messageService.ShowErrorAsync("File harus berekstensi .db.");
                return false;
            }
            return true;
        }
    }
}