using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Baris daftar file Google Drive.</summary>
    public class DriveItemRow : ObservableObject
    {
        public DriveItemRow(DriveItem item)
        {
            Item = item;
        }

        public DriveItem Item { get; }
        public string Name => Item.Name;
        public string TypeDisplay => Item.TypeDisplay;
        public string SizeDisplay => Item.SizeDisplay;
        public string ModifiedDisplay => Item.ModifiedDisplay;
        public bool IsFolder => Item.IsFolder;
    }

    /// <summary>
    /// Halaman penjelajah Google Drive — padanan GoogleDriveForm (WinForms).
    /// Navigasi folder, unggah (file lokal/PDF surat), unduh, buka di browser,
    /// hapus, pratinjau PDF, dan ganti akun.
    /// </summary>
    public class GoogleDriveViewModel : ObservableObject, IDisposable
    {
        private readonly GoogleDriveService _driveService;
        private readonly AppConfig _config;
        private readonly IMessageService _messageService;
        private readonly ILogger<GoogleDriveViewModel> _logger;
        private readonly NavigationService _navigation;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly GoogleBackupService _backupService;
        private readonly DatabaseImportExportService _importExportService;
        private readonly NotificationService? _notifications;

        private readonly System.Collections.Generic.List<(string Id, string Name)> _folderStack = new();
        private CancellationTokenSource? _cts;

        private bool _isBusy;
        private string _statusText = string.Empty;
        private string _pathLabel = "Google Drive";
        private string? _accountEmail;
        private DriveItemRow? _selectedRow;
        private string _currentFolderId = string.Empty;
        private string _currentFolderName = "Google Drive";

        public GoogleDriveViewModel(
            GoogleDriveService driveService,
            AppConfig config,
            IMessageService messageService,
            ILogger<GoogleDriveViewModel> logger,
            NavigationService navigation,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            GoogleBackupService backupService,
            DatabaseImportExportService importExportService,
            NotificationService? notifications = null)
        {
            _driveService = driveService ?? throw new ArgumentNullException(nameof(driveService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _importExportService = importExportService ?? throw new ArgumentNullException(nameof(importExportService));
            _notifications = notifications;

            RefreshCommand = new AsyncRelayCommand(async () => await LoadFolderAsync(_currentFolderId, _currentFolderName), () => !IsBusy);
            UpCommand = new AsyncRelayCommand(GoUpAsync, () => CanGoUp && !IsBusy);
            UploadCommand = new AsyncRelayCommand(UploadAsync, () => !IsBusy);
            UploadSuratCommand = new AsyncRelayCommand(UploadSuratAsync, () => !IsBusy);
            DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => CanDownload && !IsBusy);
            OpenOnlineCommand = new AsyncRelayCommand(OpenOnlineAsync, () => SelectedRow != null && !IsBusy);
            BackupDatabaseCommand = new AsyncRelayCommand(BackupDatabaseAsync, () => !IsBusy);
            RestoreDatabaseCommand = new AsyncRelayCommand(RestoreDatabaseAsync, () => CanDownload && !IsBusy);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedRow != null && !IsBusy);
            AccountCommand = new AsyncRelayCommand(AccountAsync, () => !IsBusy);
            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        public bool IsOAuthEnabled => _driveService.IsOAuthEnabled;
        public ObservableCollection<DriveItemRow> Items { get; } = new();

        public string HeaderTitle => "GOOGLE DRIVE - ARSIP SURAT";
        public string HeaderSubtitle => _accountEmail != null ? $"Terhubung sebagai {_accountEmail}" : (_driveService.IsOAuthEnabled ? "Akun Google masing-masing (OAuth)" : "Login Google belum dikonfigurasi");

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public string PathLabel { get => _pathLabel; private set => SetProperty(ref _pathLabel, value); }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanGoUp => _folderStack.Count > 0;
        public bool CanDownload => SelectedRow != null && !SelectedRow.IsFolder;

        public DriveItemRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand UpCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand UploadSuratCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand OpenOnlineCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AccountCommand { get; }
        public ICommand BatalCommand { get; }
        public ICommand BackupDatabaseCommand { get; }
        public ICommand RestoreDatabaseCommand { get; }

        /// <summary>Konek ke akun lalu muat folder root (padanan Shown + LoadFolder).</summary>
        public async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                if (!_driveService.IsOAuthEnabled)
                {
                    StatusText = "Login Google belum dikonfigurasi.";
                    PathLabel = "Google Drive";
                    await _messageService.ShowInfoAsync(
                        "Fitur Google Drive belum aktif.\n\n" +
                        "Aplikasi perlu OAuth Client ID & Client Secret agar setiap pengguna login ke akun Google Drive miliknya sendiri (bukan akun bersama).\n\n" +
                        "Kredensial belum tersimpan di komputer ini — hubungi teknisi untuk menyimpannya " +
                        "(terenkripsi di " + GoogleDriveService.CredentialsStorePath + ") atau isi bagian GoogleDrive " +
                        "pada appsettings.json, lalu jalankan ulang aplikasi.");
                    return;
                }

                StatusText = "Menghubungkan ke akun Google... Selesaikan login di browser bila diminta.";
                var email = await _driveService.GetAccountEmailAsync(GetCts().Token);
                _accountEmail = email;
                OnPropertyChanged(nameof(HeaderSubtitle));
                StatusText = email != null ? $"Terhubung sebagai {email}." : "Terhubung ke akun Google.";

                _currentFolderId = _driveService.GetRootFolderIdOrRoot();
                _currentFolderName = _driveService.RootFolderId == "" ? "Google Drive" : "Folder Drive";
                await LoadFolderAsync(_currentFolderId, _currentFolderName, resolveRootName: _driveService.RootFolderId != "");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghubungkan Google Drive");
                StatusText = "Belum terhubung.";
                await _messageService.ShowErrorAsync($"Gagal menghubungkan Google Drive.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Cadangkan data ke folder SuDesApp-Backup di Google Drive: database
        /// (snapshot konsisten), zip folder lampiran, dan zip template surat.
        /// Setelah berhasil, daftar folder dimuat ulang agar berkas baru terlihat.
        /// </summary>
        private async Task BackupDatabaseAsync()
        {
            IsBusy = true;
            StatusText = "Membuat cadangan data (database, lampiran, template) ke Google Drive...";
            try
            {
                var uploaded = await _backupService.BackupNowAsync(GetCts().Token);
                if (uploaded.Count > 0)
                {
                    _backupService.MarkBackupDoneToday();
                }

                StatusText = uploaded.Count > 0
                    ? $"Cadangan berhasil: {uploaded.Count} berkas diunggah."
                    : "Backup tidak menghasilkan berkas — cek log.";

                if (uploaded.Count > 0)
                {
                    _notifications?.Success(
                        "Backup Google Drive",
                        $"{uploaded.Count} berkas cadangan terunggah ke folder {GoogleBackupService.BackupFolderName}.");
                }
                else
                {
                    _notifications?.Warning(
                        "Backup Google Drive",
                        "Tidak ada berkas cadangan yang terunggah — cek log untuk detail.");
                }

                await _messageService.ShowInfoAsync(
                    uploaded.Count > 0
                        ? "Backup data berhasil.\n\n" +
                          "Berkas terunggah:\n" + string.Join("\n", uploaded.Select(n => "• " + n)) + "\n\n" +
                          $"Lokasi: folder {GoogleBackupService.BackupFolderName} di Google Drive\n" +
                          "Cadangan lama otomatis dipangkas (maks. 30 berkas per kelompok)."
                        : "Tidak ada berkas cadangan yang terunggah.\n\n" +
                          "Kemungkinan database kosong dan folder lampiran/template kosong, " +
                          "atau seluruh berkas sedang terkunci dibuka di aplikasi lain.");

                await LoadFolderAsync(_currentFolderId, _currentFolderName);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Backup dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal backup data ke Google Drive");
                StatusText = "Backup gagal.";
                _notifications?.Error("Backup Google Drive", "Gagal backup data: " + ex.Message);
                await _messageService.ShowErrorAsync($"Gagal backup data.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Pemulihan 1-klik dari Google Drive: pilih berkas cadangan .db di
        /// folder SuDesApp-Backup, unduh ke berkas sementara, validasi +
        /// migrasi + impor via DatabaseImportExportService (database lama
        /// otomatis dicadangkan dulu), lalu minta aplikasi dijalankan ulang.
        /// </summary>
        private async Task RestoreDatabaseAsync()
        {
            var row = SelectedRow;
            if (row == null || row.IsFolder) return;

            // 1. Konfirmasi awal + peringatan konsekuensi
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Pulihkan Database dari Google Drive",
                $"Pulihkan database dari berkas \"{row.Item.Name}\"?\n\n" +
                "Seluruh data aplikasi saat ini akan DIGANTI dengan isi berkas cadangan." +
                " Database lama tetap dicadangkan otomatis sebelum diganti.\n\nLanjutkan?");
            if (!confirmed) return;

            IsBusy = true;
            StatusText = $"Mengunduh {row.Item.Name}...";
            string tempPath = string.Empty;
            try
            {
                // 2. Unduh ke berkas sementara
                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"SuDesApp_Restore_{DateTime.Now:yyyyMMddHHmmss}_{SanitizeFileName(row.Item.Name)}");
                await _driveService.DownloadToFileAsync(row.Item.Id, tempPath, ct: GetCts().Token);

                // 3. Validasi + migrasi + impor (database lama dicadangkan di dalamnya)
                StatusText = "Memvalidasi dan mengimpor database...";
                await _importExportService.ImportDatabaseAsync(tempPath);

                StatusText = $"Database dipulihkan dari {row.Item.Name}.";
                _logger.LogInformation("Database dipulihkan dari cadangan Drive: {Name}", row.Item.Name);

                // 4. Beri tahu pengguna — import menukar berkas database di disk,
                // jadi aplikasi perlu dijalankan ulang agar data baru termuat.
                await _messageService.ShowInfoAsync(
                    "Database berhasil dipulihkan dari cadangan Google Drive.\n\n" +
                    "Silakan tutup dan jalankan ulang aplikasi agar data baru dimuat sepenuhnya.");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pemulihan dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memulihkan database dari Drive: {Name}", row.Item.Name);
                StatusText = "Pemulihan gagal.";
                await _messageService.ShowErrorAsync($"Gagal memulihkan database.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
                try
                {
                    if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception exDel)
                {
                    _logger.LogWarning(exDel, "Gagal menghapus berkas sementara pemulihan: {Path}", tempPath);
                }
            }
        }

        /// <summary>Navigasi ke folder / buka pratinjau PDF (dipanggil dari double-click/Enter).</summary>
        public async Task OpenItemAsync(DriveItemRow? row)
        {
            if (row == null || IsBusy) return;

            if (row.IsFolder)
            {
                _folderStack.Add((_currentFolderId, _currentFolderName));
                OnPropertyChanged(nameof(CanGoUp));
                RaiseCanExecuteChanged();
                await LoadFolderAsync(row.Item.Id, row.Item.Name);
                return;
            }

            if (row.Item.IsPdf)
            {
                IsBusy = true;
                StatusText = $"Memuat {row.Item.Name}...";
                try
                {
                    var bytes = await _driveService.DownloadToBytesAsync(row.Item.Id, GetCts().Token);
                    string tempPath = Path.Combine(Path.GetTempPath(), $"Drive_{DateTime.Now:yyyyMMddHHmmss}_{SanitizeFileName(row.Item.Name)}");
                    await File.WriteAllBytesAsync(tempPath, bytes);
                    var preview = _previewFactory($"Google Drive ▸ {row.Item.Name}", tempPath);
                    _navigation.Navigate(preview);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal memuat PDF {Name}", row.Item.Name);
                    StatusText = $"Gagal memuat PDF: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
                return;
            }

            OpenInBrowser(row.Item);
        }

        public Task LoadFolderAsync(string folderId, string folderName, bool resolveRootName = false)
        {
            return LoadFolderCoreAsync(folderId, folderName, resolveRootName);
        }

        private async Task LoadFolderCoreAsync(string folderId, string folderName, bool resolveRootName)
        {
            IsBusy = true;
            SelectedRow = null;
            try
            {
                if (resolveRootName && !string.IsNullOrWhiteSpace(_driveService.RootFolderId))
                {
                    try
                    {
                        var root = await _driveService.GetItemAsync(_driveService.RootFolderId, GetCts().Token);
                        folderName = root.Name;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal ambil nama folder root");
                    }
                }

                _currentFolderId = folderId;
                _currentFolderName = folderName;
                PathLabel = folderName == "Google Drive" ? "Google Drive" : $"Google Drive ▸ {folderName}";

                StatusText = "Memuat daftar file...";
                var list = await _driveService.ListItemsAsync(folderId, GetCts().Token);

                Items.Clear();
                foreach (var item in list)
                {
                    Items.Add(new DriveItemRow(item));
                }
                StatusText = Items.Count == 0 ? "Folder kosong." : $"{Items.Count} item.";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat folder Drive {FolderId}", folderId);
                StatusText = $"Gagal memuat: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal memuat folder Google Drive.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GoUpAsync()
        {
            if (_folderStack.Count == 0) return;
            var parent = _folderStack[^1];
            _folderStack.RemoveAt(_folderStack.Count - 1);
            OnPropertyChanged(nameof(CanGoUp));
            RaiseCanExecuteChanged();
            await LoadFolderCoreAsync(parent.Id, parent.Name, resolveRootName: false);
        }

        private async Task UploadAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih file untuk diunggah ke Google Drive",
                Filter = "Semua File|*.*|Dokumen PDF|*.pdf|Word|*.doc;*.docx|Excel|*.xls;*.xlsx|Gambar|*.png;*.jpg;*.jpeg"
            };
            if (dialog.ShowDialog() != true) return;
            await UploadLocalFileAsync(dialog.FileName);
        }

        private async Task UploadSuratAsync()
        {
            var initialDir = Directory.Exists(_config.PdfOutputPath) ? _config.PdfOutputPath : AppDomain.CurrentDomain.BaseDirectory;
            var dialog = new OpenFileDialog
            {
                Title = "Pilih PDF surat untuk diunggah",
                Filter = "Dokumen PDF|*.pdf|Semua File|*.*",
                InitialDirectory = initialDir
            };
            if (dialog.ShowDialog() != true) return;
            await UploadLocalFileAsync(dialog.FileName);
        }

        private async Task UploadLocalFileAsync(string path)
        {
            if (!File.Exists(path) || IsBusy) return;

            var targetFolderId = _currentFolderId;
            var targetFolderName = _currentFolderName == "Google Drive" ? "Google Drive" : _currentFolderName;
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Upload ke Google Drive",
                $"Unggah \"{Path.GetFileName(path)}\" ke folder \"{targetFolderName}\"?");
            if (!confirmed) return;

            IsBusy = true;
            StatusText = $"Mengunggah {Path.GetFileName(path)}...";
            var progress = new Progress<double>(p => StatusText = $"Mengunggah {Path.GetFileName(path)}... {p:0}%");
            try
            {
                var uploaded = await _driveService.UploadFileAsync(path, targetFolderId, progress: progress, ct: GetCts().Token);
                if (_currentFolderId == targetFolderId)
                {
                    Items.Add(new DriveItemRow(uploaded));
                }
                StatusText = $"Berhasil diunggah: {uploaded.Name}";
                _logger.LogInformation("Upload berhasil: {Name} (ID: {Id})", uploaded.Name, uploaded.Id);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Upload dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunggah {Path}", path);
                StatusText = $"Gagal mengunggah: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal mengunggah file.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DownloadAsync()
        {
            var row = SelectedRow;
            if (row == null || row.IsFolder) return;

            var dialog = new SaveFileDialog
            {
                Title = "Simpan file dari Google Drive",
                FileName = SanitizeFileName(row.Item.Name),
                Filter = "Semua File|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            IsBusy = true;
            StatusText = $"Mengunduh {row.Item.Name}...";
            try
            {
                await _driveService.DownloadToFileAsync(row.Item.Id, dialog.FileName, ct: GetCts().Token);
                StatusText = $"Tersimpan di: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh {Name}", row.Item.Name);
                StatusText = $"Gagal mengunduh: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal mengunduh file.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private Task OpenOnlineAsync()
        {
            var row = SelectedRow;
            if (row != null) OpenInBrowser(row.Item);
            return Task.CompletedTask;
        }

        private async Task DeleteAsync()
        {
            var row = SelectedRow;
            if (row == null) return;

            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Hapus dari Drive",
                $"Hapus \"{row.Item.Name}\" dari Google Drive?\n\nTindakan ini tidak dapat dibatalkan.");
            if (!confirmed) return;

            IsBusy = true;
            StatusText = $"Menghapus {row.Item.Name}...";
            try
            {
                await _driveService.DeleteItemAsync(row.Item.Id, GetCts().Token);
                Items.Remove(row);
                SelectedRow = null;
                StatusText = $"Dihapus: {row.Item.Name}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus {Name}", row.Item.Name);
                StatusText = $"Gagal menghapus: {ex.Message}";
                await _messageService.ShowErrorAsync($"Gagal menghapus file.\n\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task AccountAsync()
        {
            if (!_driveService.IsOAuthEnabled) return;

            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Koneksi Akun Google",
                "Login ulang ke akun Google akan menghapus token tersimpan dan membuka halaman izin lagi.\n\nLanjutkan?");
            if (!confirmed) return;

            IsBusy = true;
            StatusText = "Menghapus token lama...";
            try
            {
                await _driveService.ClearTokenAsync(GetCts().Token);
                _accountEmail = null;
                OnPropertyChanged(nameof(HeaderSubtitle));
                StatusText = "Menghubungkan ke akun Google... Selesaikan login di browser bila diminta.";
                _accountEmail = await _driveService.GetAccountEmailAsync(GetCts().Token);
                OnPropertyChanged(nameof(HeaderSubtitle));
                StatusText = _accountEmail != null ? $"Terhubung sebagai {_accountEmail}." : "Terhubung ke akun Google.";
                await LoadFolderAsync(_currentFolderId, _currentFolderName, resolveRootName: _driveService.RootFolderId != "");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal login ulang Google Drive");
                StatusText = $"Gagal login ulang: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenInBrowser(DriveItem item)
        {
            var url = !string.IsNullOrWhiteSpace(item.WebViewLink)
                ? item.WebViewLink
                : _driveService.ToWebViewLink(item.Id);
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka browser untuk {Url}", url);
                StatusText = $"Gagal membuka browser: {ex.Message}";
            }
        }

        private void RaiseCanExecuteChanged()
        {
            if (RefreshCommand is AsyncRelayCommand r) r.RaiseCanExecuteChanged();
            if (UpCommand is AsyncRelayCommand u) u.RaiseCanExecuteChanged();
            if (UploadCommand is AsyncRelayCommand up) up.RaiseCanExecuteChanged();
            if (UploadSuratCommand is AsyncRelayCommand us) us.RaiseCanExecuteChanged();
            if (DownloadCommand is AsyncRelayCommand d) d.RaiseCanExecuteChanged();
            if (OpenOnlineCommand is AsyncRelayCommand o) o.RaiseCanExecuteChanged();
            if (DeleteCommand is AsyncRelayCommand del) del.RaiseCanExecuteChanged();
            if (AccountCommand is AsyncRelayCommand a) a.RaiseCanExecuteChanged();
            if (RestoreDatabaseCommand is AsyncRelayCommand rs) rs.RaiseCanExecuteChanged();
            if (BackupDatabaseCommand is AsyncRelayCommand bk) bk.RaiseCanExecuteChanged();
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(name) ? "file" : name;
        }

        private CancellationTokenSource GetCts() => _cts ??= new CancellationTokenSource();

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}