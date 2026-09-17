using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Formulir input/edit satu data buku SK / Peraturan (SK, Perdes, Perkades).
    /// Padanan InputKeputusanPeraturan (WinForms): validasi + simpan/ubah ke Excel.
    ///
    /// Fitur tambahan: tombol "Pilih File Lampiran" (Word/PDF). Dokumen Word (.docx/.doc)
    /// dibaca untuk mengisi otomatis Nomor/Tanggal/Tentang/Keterangan, lalu berkas (Word/PDF)
    /// disalin ke folder penyimpanan (ArsipKeputusanFiles) pada saat Simpan agar bisa
    /// dilihat, diedit, atau dicetak kembali.
    ///
    /// PDF: pemilihan berkas .pdf juga didukung, tetapi TIDAK dibaca isinya —
    /// pengguna cukup diberi tahu (messagebox) untuk mengisi formulir manual,
    /// lalu berkas tetap disalin ke arsip pada saat Simpan.
    /// </summary>
    public class InputKeputusanViewModel : ObservableObject
    {
        private readonly IArsipKeputusanRepository _repository;
        private readonly IWordFileReader _wordReader;
        private readonly FileService _fileService;
        private readonly IMessageService _messageService;
        private readonly ILogger<InputKeputusanViewModel> _logger;

        private string _jenisKeputusan = "SK";
        /// <summary>Jenis arsip saat formulir dibuka; dipakai untuk mendeteksi perpindahan jenis.</summary>
        private string _jenisKeputusanAwal = "SK";
        private DataKeputusan? _editData;
        private string _title = "Input Surat Keputusan";
        private bool _isBusy;
        private bool _isLoadingWord;

        private string _nomor = string.Empty;
        private string _tanggal = string.Empty;
        private string _tentang = string.Empty;
        private string _keterangan = string.Empty;

        /// <summary>Path file Word yang dipilih (sumber). Disalin saat Simpan.</summary>
        private string _wordFilePath = string.Empty;
        /// <summary>Path file PDF yang dipilih (sumber). Tidak dibaca isinya; disalin saat Simpan.</summary>
        private string _pdfFilePath = string.Empty;
        /// <summary>Nama file Word/PDF yang sudah tersimpan (dari kolom FileWord), untuk keperluan Lihat/Ubah.</summary>
        private string? _storedFileName;

        public event Action? RequestClose;

        public InputKeputusanViewModel(
            IArsipKeputusanRepository repository,
            IWordFileReader wordReader,
            FileService fileService,
            IMessageService messageService,
            ILogger<InputKeputusanViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _wordReader = wordReader ?? throw new ArgumentNullException(nameof(wordReader));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            PickWordFileCommand = new AsyncRelayCommand(PickFileAsync);
            FillFromWordCommand = new AsyncRelayCommand(FillFromWordAsync, () => HasWordFile);
            OpenWordFileCommand = new AsyncRelayCommand(OpenAttachedFileAsync, () => HasStoredOrPickedFile);
        }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public bool IsLoadingWord
        {
            get => _isLoadingWord;
            private set => SetProperty(ref _isLoadingWord, value);
        }

        public string Nomor { get => _nomor; set => SetProperty(ref _nomor, value); }
        public string Tanggal { get => _tanggal; set => SetProperty(ref _tanggal, value); }
        public string Tentang { get => _tentang; set => SetProperty(ref _tentang, value); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value); }

        /// <summary>Nama file Word/PDF yang dipilih, atau kosong.</summary>
        public string WordFileDisplay => string.IsNullOrWhiteSpace(_wordFilePath)
            ? (string.IsNullOrWhiteSpace(_pdfFilePath)
                ? (!string.IsNullOrWhiteSpace(_storedFileName) ? _storedFileName : "Tidak ada berkas")
                : Path.GetFileName(_pdfFilePath))
            : Path.GetFileName(_wordFilePath);

        /// <summary>Ada berkas Word (baru dipilih atau tersimpan) yang bisa dibaca isinya.</summary>
        public bool HasWordFile => !string.IsNullOrWhiteSpace(_wordFilePath) ||
                                   (!string.IsNullOrWhiteSpace(_storedFileName) &&
                                    string.Equals(Path.GetExtension(_storedFileName), ".pdf", StringComparison.OrdinalIgnoreCase) == false);

        /// <summary>Ada berkas apa pun (Word/PDF) baru dipilih atau tersimpan.</summary>
        public bool HasStoredOrPickedFile => !string.IsNullOrWhiteSpace(_wordFilePath) ||
                                             !string.IsNullOrWhiteSpace(_pdfFilePath) ||
                                             !string.IsNullOrWhiteSpace(_storedFileName);

        public bool HasNewWordFile => !string.IsNullOrWhiteSpace(_wordFilePath);

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public AsyncRelayCommand PickWordFileCommand { get; }
        public AsyncRelayCommand FillFromWordCommand { get; }
        public AsyncRelayCommand OpenWordFileCommand { get; }

        public void Initialize(string jenisKeputusan, DataKeputusan? editData)
        {
            _jenisKeputusan = (jenisKeputusan ?? "SK").ToUpperInvariant();
            _jenisKeputusanAwal = _jenisKeputusan;
            _editData = editData;
            bool isEdit = _editData != null;
            Title = (isEdit ? "Edit " : "Input ") + LabelJenis(_jenisKeputusan);

            if (isEdit)
            {
                Nomor = _editData!.Nomor;
                Tanggal = _editData.Tanggal.ToString("dd-MM-yyyy");
                Tentang = _editData.Tentang;
                Keterangan = _editData.Keterangan;
                _storedFileName = _editData.FileWord;
            }
            else
            {
                Nomor = string.Empty;
                Tanggal = string.Empty;
                Tentang = string.Empty;
                Keterangan = string.Empty;
                _storedFileName = null;
            }

            _wordFilePath = string.Empty;
            _pdfFilePath = string.Empty;
            OnPropertyChanged(nameof(WordFileDisplay));
            OnPropertyChanged(nameof(HasWordFile));
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
            OnPropertyChanged(nameof(HasNewWordFile));
            ((AsyncRelayCommand)FillFromWordCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)OpenWordFileCommand).RaiseCanExecuteChanged();
        }

        private string _jenis_keputusan => _jenisKeputusan;

        private static string LabelJenis(string jenis) => jenis switch
        {
            "PERDES" => "Peraturan Desa",
            "PERKADES" => "Peraturan Kepala Desa",
            _ => "Surat Keputusan"
        };

        /// <summary>
        /// Memilih berkas lampiran. Word (.docx/.doc) dibaca untuk isi otomatis;
        /// PDF hanya dilampirkan — isinya tidak dibaca, pengguna diingatkan
        /// mengisi formulir secara manual.
        /// </summary>
        private async Task PickFileAsync()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Pilih berkas Word atau PDF",
                    Filter = "Dokumen Word / PDF|*.docx;*.doc;*.pdf|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|PDF (.pdf)|*.pdf"
                };
                if (dlg.ShowDialog() != true) return;

                if (string.Equals(Path.GetExtension(dlg.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // PDF: lampirkan saja, jangan dibaca isinya.
                    _pdfFilePath = dlg.FileName;
                    _wordFilePath = string.Empty;
                    UpdateFileState();

                    await _messageService.ShowInfoAsync(
                        "Berkas PDF dipilih.\n\nIsi formulir (Nomor, Tanggal, Tentang, Keterangan) diisi manual ya — aplikasi tidak membaca isi berkas PDF.\n\nBerkas akan tersalin ke arsip saat data disimpan.");
                    return;
                }

                // Word: lampirkan lalu isi otomatis.
                _wordFilePath = dlg.FileName;
                _pdfFilePath = string.Empty;
                UpdateFileState();

                // Auto-isi otomatis setelah berkas dipilih.
                await FillFromWordAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memilih berkas lampiran");
                await _messageService.ShowErrorAsync("Gagal membuka berkas: " + ex.Message);
            }
        }

        private void UpdateFileState()
        {
            OnPropertyChanged(nameof(WordFileDisplay));
            OnPropertyChanged(nameof(HasWordFile));
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
            OnPropertyChanged(nameof(HasNewWordFile));
            ((AsyncRelayCommand)FillFromWordCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)OpenWordFileCommand).RaiseCanExecuteChanged();
        }

        private async Task FillFromWordAsync()
        {
            if (!HasWordFile) return;
            IsLoadingWord = true;
            try
            {
                var content = await _wordReader.ReadAsync(_wordFilePath);
                var fields = WordTextParser.ExtractKeputusanFields(content.FullText, content.Paragraphs);

                // Deteksi jenis dokumen dari isi berkas
                var detectedType = WordTextParser.DetectDocumentType(content.FullText);
                if (!string.Equals(detectedType, _jenisKeputusan, StringComparison.OrdinalIgnoreCase) && detectedType != "Unknown")
                {
                    // Tawarkan opsi memindahkan formulir (dan data yang sedang diedit)
                    // ke arsip jenis yang benar, tanpa memilih ulang berkas.
                    bool pindah = await _messageService.ShowConfirmationAsync(
                        "Jenis Berkas Tidak Sesuai",
                        $"Berkas terdeteksi {LabelJenis(detectedType)}, sedangkan formulir ini untuk {LabelJenis(_jenisKeputusan)}.\n\n" +
                        $"Pindahkan formulir ke arsip {LabelJenis(detectedType)} dan lanjutkan dengan berkas ini?\n\n" +
                        $"Pilih \"Tidak\" untuk membatalkan pemilihan berkas.");

                    if (pindah)
                    {
                        // Pindahkan formulir ke arsip jenis yang benar; berkas tetap dipakai.
                        _jenisKeputusan = detectedType.ToUpperInvariant();
                        Title = (_editData != null ? "Edit " : "Input ") + LabelJenis(_jenisKeputusan);
                        // Lanjut ke pengisian otomatis di bawah dengan jenis yang baru.
                    }
                    else
                    {
                        var msg = $"Pemilihan berkas dibatalkan.\n\n" +
                                  $"Silakan gunakan menu yang sesuai:\n" +
                                  $"- Surat Keputusan (SK)  -> Jenis \"SK\"\n" +
                                  $"- Peraturan Desa        -> Jenis \"Perdes\"\n" +
                                  $"- Keputusan Kepala Desa -> Jenis \"Perkades\"\n\n" +
                                  $"Atau pilih ulang berkas untuk melanjutkan di formulir ini.";
                        await _messageService.ShowWarningAsync(msg);

                        // Batalkan pemilihan file - bersihkan path dan UI
                        _wordFilePath = string.Empty;
                        UpdateFileState();
                        return; // Hentikan pengisian otomatis
                    }
                }

                bool changed = false;
                if (!string.IsNullOrWhiteSpace(fields.Nomor) && string.IsNullOrWhiteSpace(Nomor))
                {
                    Nomor = fields.Nomor; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.TanggalDdMmYyyy) && string.IsNullOrWhiteSpace(Tanggal))
                {
                    Tanggal = fields.TanggalDdMmYyyy; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.Tentang) && string.IsNullOrWhiteSpace(Tentang))
                {
                    Tentang = fields.Tentang; changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fields.Keterangan) && string.IsNullOrWhiteSpace(Keterangan))
                {
                    Keterangan = fields.Keterangan; changed = true;
                }

                if (changed)
                    await _messageService.ShowInfoAsync("Formulir diisi otomatis dari berkas Word. Periksa dan koreksi jika diperlukan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca/mengisi dari berkas Word");
                await _messageService.ShowErrorAsync("Tidak dapat membaca berkas Word. Pastikan berkas tidak rusak dan coba gunakan format .docx.\n\n" + ex.Message);
            }
            finally
            {
                IsLoadingWord = false;
            }
        }

        /// <summary>Membuka berkas lampiran: yang baru dipilih (Word/PDF) atau yang tersimpan di arsip.</summary>
        private async Task OpenAttachedFileAsync()
        {
            string? target = null;
            if (!string.IsNullOrWhiteSpace(_wordFilePath)) target = _wordFilePath;
            else if (!string.IsNullOrWhiteSpace(_pdfFilePath)) target = _pdfFilePath;
            else if (!string.IsNullOrWhiteSpace(_storedFileName)) target = _repository.ResolveWordFullPath(_storedFileName);

            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            {
                await _messageService.ShowWarningAsync("Berkas tidak ditemukan.");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka berkas: {Path}", target);
                await _messageService.ShowErrorAsync("Gagal membuka berkas: " + ex.Message);
            }
        }

        private async Task SaveAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            try
            {
                var errors = Validate();
                if (errors.Count > 0)
                {
                    await _messageService.ShowWarningAsync(
                        "Data belum lengkap atau format salah:\n\n" + string.Join("\n", errors));
                    return;
                }

                // Validasi nomor duplikat untuk jenis arsip yang sama (SK/PERDES/PERKADES),
                // kecuali data yang sedang diedit sendiri.
                string nomorBaru = Nomor.Trim();
                var semua = await _repository.GetAllAsync();
                bool duplikat = semua.Any(k =>
                    k.JenisKeputusan.Equals(_jenisKeputusan, StringComparison.OrdinalIgnoreCase) &&
                    k.Nomor.Equals(nomorBaru, StringComparison.OrdinalIgnoreCase) &&
                    k.IdBarisExcel != (_editData?.IdBarisExcel ?? 0));
                if (duplikat)
                {
                    await _messageService.ShowWarningAsync(
                        $"Nomor '{nomorBaru}' sudah terdaftar untuk {LabelJenis(_jenisKeputusan)}.\n\n" +
                        "Gunakan nomor yang berbeda, atau ubah data lama melalui tombol Edit di daftar.");
                    return;
                }

                var item = new DataKeputusan
                {
                    JenisKeputusan = _jenis_keputusan,
                    Nomor = Nomor.Trim(),
                    Tanggal = ParseDate(Tanggal)!.Value,
                    Tentang = Tentang.Trim(),
                    Keterangan = Keterangan.Trim(),
                    FileWord = _editData?.FileWord
                };

                if (_editData != null)
                {
                    item.IdBarisExcel = _editData.IdBarisExcel;
                    await _repository.UpdateAsync(item);
                    _logger.LogInformation("Arsip keputusan diedit: Id={Id}", item.IdBarisExcel);
                }
                else
                {
                    await _repository.AddAsync(item);
                    _logger.LogInformation("Arsip keputusan ditambahkan: Nomor={Nomor} Jenis={Jenis}", item.Nomor, item.JenisKeputusan);
                }

                // Copy Word/PDF file BEFORE closing dialog so FileWord is saved in Excel
                // before grid refresh (LoadAsync) reads it
                if (HasNewAttachment)
                {
                    try
                    {
                        await SaveAttachmentAsync(item.IdBarisExcel, item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyalin berkas lampiran (data sudah tersimpan)");
                        await _messageService.ShowErrorAsync("Data tersimpan, tapi berkas gagal disalin:\n\n" + ex.Message);
                    }
                }

                // Beri tahu bila data dipindah ke arsip jenis lain (formulir dipindah otomatis).
                if (!item.JenisKeputusan.Equals(_jenisKeputusanAwal, StringComparison.OrdinalIgnoreCase))
                {
                    await _messageService.ShowInfoAsync(
                        $"Data tersimpan di arsip {LabelJenis(item.JenisKeputusan)}.\n\n" +
                        $"Buka jenis tersebut di Buku SK / Peraturan untuk melihat datanya.");
                }

                // Now close dialog - grid refresh will see updated FileWord
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan arsip keputusan");
                await _messageService.ShowErrorAsync($"Gagal menyimpan data: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        }

        /// <summary>Ada berkas baru (Word/PDF) yang belum tersalin ke arsip.</summary>
        private bool HasNewAttachment => !string.IsNullOrWhiteSpace(_wordFilePath) || !string.IsNullOrWhiteSpace(_pdfFilePath);

        /// <summary>Menyalin berkas Word/PDF ke folder penyimpanan dan memperbarui kolom FileWord.</summary>
        private async Task SaveAttachmentAsync(int id, DataKeputusan item)
        {
            try
            {
                string sourcePath = !string.IsNullOrWhiteSpace(_wordFilePath) ? _wordFilePath : _pdfFilePath;
                var ext = Path.GetExtension(sourcePath) ?? ".docx";
                var (fileName, destPath) = _repository.ResolveWordStorage(id, item.JenisKeputusan, item.Nomor, ext);

                // Hapus versi lama jika ada dan berbeda dari yang baru.
                var oldPath = string.IsNullOrWhiteSpace(item.FileWord) ? null : _repository.ResolveWordFullPath(item.FileWord);
                if (!string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(oldPath))
                {
                    try { File.Delete(oldPath); } catch { /* ignore */ }
                }

                _fileService.EnsureDirectoryExists(Path.GetDirectoryName(destPath) ?? destPath);
                await _fileService.CopyFileAsync(sourcePath, destPath, overwrite: true);

                item.FileWord = fileName;
                await _repository.UpdateAsync(item); // persist kolom FileWord
                _storedFileName = fileName;
                _wordFilePath = string.Empty;
                _pdfFilePath = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin berkas lampiran ke penyimpanan");
                await _messageService.ShowErrorAsync("Berkas terpilih gagal disalin; data tetap tersimpan.\n\n" + ex.Message);
            }
        }

        private List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Nomor)) errors.Add("- Nomor wajib diisi.");
            if (string.IsNullOrWhiteSpace(Tentang)) errors.Add("- Kolom 'Tentang' wajib diisi.");
            if (string.IsNullOrWhiteSpace(Keterangan)) errors.Add("- Keterangan wajib diisi.");

            if (string.IsNullOrWhiteSpace(Tanggal)) errors.Add("- Tanggal wajib diisi.");
            else if (ParseDate(Tanggal) == null) errors.Add("- Format tanggal tidak valid (DD-MM-YYYY).");

            return errors;
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParseExact(value, "dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) ? date : null;
        }

        private string _jenis { get; } = ""; // placeholder supaya nama properti tetap konsisten
    }
}
