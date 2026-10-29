using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Formulir input/edit satu data buku SK / Peraturan (SK, Perdes, Perkades).
    /// Padanan InputKeputusanPeraturan (WinForms): validasi + simpan/ubah ke Excel.
    ///
    /// Untuk PERDES, dokumen tidak lagi berasal dari berkas Word: naskah disusun
    /// per halaman di formulir ini, lalu dirender generator PDF SudesPdf saat
    /// pratinjau dan saat disimpan. Sumber naskah ikut disimpan di samping PDF
    /// arsip supaya bisa dibuka dan disunting lagi.
    ///
    /// SK/PERKADES tetap mendukung lampiran Word/PDF. Berkas Word dibaca untuk
    /// mengisi Nomor/Tanggal/Tentang/Keterangan; .doc lama dikonversi otomatis.
    /// </summary>
    public class InputKeputusanViewModel : ObservableObject, IJudulHalaman
    {
        private readonly IArsipKeputusanRepository _repository;
        private readonly IDesaRepository _desaRepository;
        private readonly IWordFileReader _wordReader;
        private readonly FileService _fileService;
        private readonly IMessageService _messageService;
        private readonly Func<string, string, Action?, PdfPreviewViewModel> _previewFactory;
        private readonly string _folderPdfSementara;
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

        /// <summary>Halaman naskah PERDES yang sedang disunting.</summary>
        public ObservableCollection<NaskahHalamanViewModel> HalamanNaskah { get; } = new();
        private NaskahHalamanViewModel? _halamanTerpilih;
        private string _infoNaskah = string.Empty;

        public event Action? RequestClose;

        /// <summary>Data berhasil disimpan — halaman induk perlu memuat ulang daftarnya.</summary>
        public event Action? Tersimpan;

        public InputKeputusanViewModel(
            IArsipKeputusanRepository repository,
            IDesaRepository desaRepository,
            IWordFileReader wordReader,
            FileService fileService,
            IMessageService messageService,
            Func<string, string, Action?, PdfPreviewViewModel> previewFactory,
            AppConfig appConfig,
            ILogger<InputKeputusanViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _wordReader = wordReader ?? throw new ArgumentNullException(nameof(wordReader));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _folderPdfSementara = (appConfig ?? throw new ArgumentNullException(nameof(appConfig))).TempPdfFolder;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            PickWordFileCommand = new AsyncRelayCommand(PickFileAsync);
            OpenWordFileCommand = new AsyncRelayCommand(OpenAttachedFileAsync, () => HasStoredOrPickedFile);
            TambahHalamanCommand = new RelayCommand(TambahHalaman);
            HapusHalamanCommand = new RelayCommand(HapusHalaman, () => _halamanTerpilih != null);
            NaikkanHalamanCommand = new RelayCommand(
                () => GeserHalaman(-1),
                () => _halamanTerpilih != null && HalamanNaskah.IndexOf(_halamanTerpilih) > 0);
            TurunkanHalamanCommand = new RelayCommand(
                () => GeserHalaman(1),
                () => _halamanTerpilih != null && HalamanNaskah.IndexOf(_halamanTerpilih) < HalamanNaskah.Count - 1);
            PratinjauNaskahCommand = new AsyncRelayCommand(PratinjauNaskahAsync, () => HasNaskah);
        }

        public string Title
        {
            get => _title;
            private set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(JudulHalaman));
                }
            }
        }

        /// <summary>Judul halaman ini di title bar jendela (lihat <see cref="IJudulHalaman"/>).</summary>
        public string JudulHalaman => Title;

        public bool IsLoadingWord
        {
            get => _isLoadingWord;
            private set => SetProperty(ref _isLoadingWord, value);
        }

        public string Nomor { get => _nomor; set => SetProperty(ref _nomor, value); }
        public string Tanggal { get => _tanggal; set => SetProperty(ref _tanggal, value); }
        public string Tentang { get => _tentang; set => SetProperty(ref _tentang, value); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value); }

        /// <summary>PERDES memakai naskah berhalaman, bukan lampiran Word.</summary>
        public bool IsPerdes => _jenisKeputusan == "PERDES";

        public bool IsBukanPerdes => !IsPerdes;

        /// <summary>Halaman naskah yang sedang dibuka di kotak penyunting.</summary>
        public NaskahHalamanViewModel? HalamanTerpilih
        {
            get => _halamanTerpilih;
            set
            {
                if (!SetProperty(ref _halamanTerpilih, value)) return;
                ((RelayCommand)HapusHalamanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)NaikkanHalamanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)TurunkanHalamanCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(AdaHalamanTerpilih));
            }
        }

        public bool AdaHalamanTerpilih => _halamanTerpilih != null;

        /// <summary>Keterangan singkat keadaan naskah di formulir.</summary>
        public string InfoNaskah
        {
            get => _infoNaskah;
            private set => SetProperty(ref _infoNaskah, value);
        }

        /// <summary>Ada halaman naskah yang berisi — syarat pratinjau dan penerbitan PDF.</summary>
        public bool HasNaskah => HalamanNaskah.Any(h => !string.IsNullOrWhiteSpace(h.Isi));

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

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public AsyncRelayCommand PickWordFileCommand { get; }
        public AsyncRelayCommand OpenWordFileCommand { get; }
        public RelayCommand TambahHalamanCommand { get; }
        public RelayCommand HapusHalamanCommand { get; }
        public RelayCommand NaikkanHalamanCommand { get; }
        public RelayCommand TurunkanHalamanCommand { get; }
        public AsyncRelayCommand PratinjauNaskahCommand { get; }

        public void Initialize(string jenisKeputusan, DataKeputusan? editData, DataKeputusan? prefill = null)
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
            else if (prefill != null)
            {
                Nomor = prefill.Nomor;
                Tanggal = prefill.Tanggal.ToString("dd-MM-yyyy");
                Tentang = prefill.Tentang;
                Keterangan = prefill.Keterangan;
                _storedFileName = null;
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
            OnPropertyChanged(nameof(IsPerdes));
            OnPropertyChanged(nameof(IsBukanPerdes));
            OnPropertyChanged(nameof(WordFileDisplay));
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
            ((AsyncRelayCommand)OpenWordFileCommand).RaiseCanExecuteChanged();
            MuatNaskahAwal();
        }

        /// <summary>
        /// Muat naskah contoh PERDES sebagai isi halaman. Nama desa pada teks
        /// contoh diganti identitas Pengaturan Surat; nomor dan tanggal tetap
        /// diisi saat pratinjau/simpan dari kolom formulir.
        /// </summary>
        public async Task GunakanNaskahContohAsync(string namaBerkas)
        {
            if (string.IsNullOrWhiteSpace(namaBerkas)) return;
            var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true);
            var naskah = NaskahPerdesContoh.Muat(namaBerkas, desa);
            IsiHalamanDariModel(naskah.Halaman);
            InfoNaskah = $"{naskah.Halaman.Count} halaman naskah contoh dimuat. Sunting isi halaman lalu periksa pratinjau PDF.";
        }

        private void MuatNaskahAwal()
        {
            HalamanNaskah.Clear();
            HalamanTerpilih = null;
            InfoNaskah = string.Empty;

            if (!IsPerdes)
            {
                UpdateNaskahState();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_storedFileName))
            {
                string? pdfPath = _repository.ResolveWordFullPath(_storedFileName);
                if (pdfPath != null)
                {
                    try
                    {
                        var naskah = NaskahPerdesStore.Muat(pdfPath);
                        if (naskah != null)
                        {
                            IsiHalamanDariModel(naskah.Halaman);
                            InfoNaskah = $"{naskah.Halaman.Count} halaman naskah tersimpan dimuat. Sunting lalu simpan untuk menerbitkan PDF baru.";
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Naskah sampingan tidak dapat dibaca untuk {Berkas}", _storedFileName);
                        InfoNaskah = "Naskah tersimpan tidak dapat dibaca. Isi halaman baru untuk menggantikan PDF lama.";
                        UpdateNaskahState();
                        return;
                    }
                }
            }

            InfoNaskah = _storedFileName != null
                ? "Naskah lama tidak tersedia untuk disunting; PDF lama tetap tersimpan sampai Anda mengisi halaman naskah."
                : "Belum ada halaman. Tambahkan halaman atau pilih salah satu contoh Perdes di daftar.";
            UpdateNaskahState();
        }

        private void IsiHalamanDariModel(IEnumerable<NaskahPerdesHalaman> halaman)
        {
            foreach (var lama in HalamanNaskah)
            {
                lama.PropertyChanged -= Halaman_PropertyChanged;
            }
            HalamanNaskah.Clear();
            foreach (var item in halaman)
            {
                var halamanBaru = new NaskahHalamanViewModel(item);
                halamanBaru.PropertyChanged += Halaman_PropertyChanged;
                HalamanNaskah.Add(halamanBaru);
            }

            HalamanTerpilih = HalamanNaskah.FirstOrDefault();
            UpdateNaskahState();
        }

        private void UpdateNaskahState()
        {
            OnPropertyChanged(nameof(HasNaskah));
            ((AsyncRelayCommand)PratinjauNaskahCommand).RaiseCanExecuteChanged();
            ((RelayCommand)HapusHalamanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)NaikkanHalamanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)TurunkanHalamanCommand).RaiseCanExecuteChanged();
        }

        private void TambahHalaman()
        {
            var halaman = new NaskahHalamanViewModel(new NaskahPerdesHalaman
            {
                Judul = $"Halaman {HalamanNaskah.Count + 1}",
                Isi = string.Empty
            });
            halaman.PropertyChanged += Halaman_PropertyChanged;
            HalamanNaskah.Add(halaman);
            HalamanTerpilih = halaman;
            InfoNaskah = "Halaman baru ditambahkan. Isi teks naskah pada kotak di sebelah kanan.";
            UpdateNaskahState();
        }

        private void HapusHalaman()
        {
            if (_halamanTerpilih == null) return;
            int indeks = HalamanNaskah.IndexOf(_halamanTerpilih);
            _halamanTerpilih.PropertyChanged -= Halaman_PropertyChanged;
            HalamanNaskah.Remove(_halamanTerpilih);
            HalamanTerpilih = HalamanNaskah.Count == 0
                ? null
                : HalamanNaskah[Math.Clamp(indeks, 0, HalamanNaskah.Count - 1)];
            InfoNaskah = HalamanNaskah.Count == 0
                ? "Belum ada halaman naskah."
                : "Halaman dihapus.";
            UpdateNaskahState();
        }

        private void GeserHalaman(int arah)
        {
            if (_halamanTerpilih == null) return;
            int indeks = HalamanNaskah.IndexOf(_halamanTerpilih);
            int tujuan = indeks + arah;
            if (tujuan < 0 || tujuan >= HalamanNaskah.Count) return;
            HalamanNaskah.Move(indeks, tujuan);
            HalamanTerpilih = _halamanTerpilih;
            UpdateNaskahState();
        }

        private void Halaman_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NaskahHalamanViewModel.Isi))
            {
                UpdateNaskahState();
            }
        }

        private NaskahPerdes AmbilNaskah() => new()
        {
            Judul = Tentang,
            Halaman = HalamanNaskah.Select(h => h.KeModel()).ToList()
        };

        /// <summary>
        /// Pratinjau naskah yang sedang disunting di jendela terpisah, sehingga
        /// isian formulir dan halaman naskah tidak hilang saat pratinjau ditutup.
        /// </summary>
        private async Task PratinjauNaskahAsync()
        {
            if (!HasNaskah)
            {
                await _messageService.ShowInfoAsync("Isi minimal satu halaman naskah sebelum membuka pratinjau.");
                return;
            }

            try
            {
                var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true) ?? new DesaData();
                DateTime tanggal = ParseDate(Tanggal) ?? DateTime.Today;
                byte[] pdf = new NaskahPerdesGenerator(desa, AmbilNaskah(), Nomor, tanggal).Buat();

                Directory.CreateDirectory(_folderPdfSementara);
                string path = Path.Combine(_folderPdfSementara,
                    $"Pratinjau_Perdes_{DateTime.Now:yyyyMMddHHmmssfff}.pdf");
                await File.WriteAllBytesAsync(path, pdf).ConfigureAwait(true);

                TampilkanPratinjau(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau naskah Perdes");
                await _messageService.ShowErrorAsync("Pratinjau naskah gagal dibuat: " + ex.Message);
            }
        }

        private void TampilkanPratinjau(string path)
        {
            Window? jendela = null;
            var preview = _previewFactory(
                $"Pratinjau Naskah {LabelJenis(_jenisKeputusan)}", path, () => jendela?.Close());
            jendela = new Window
            {
                Title = preview.Title,
                Content = new SuDesApp.Wpf.Views.PdfPreviewView { DataContext = preview },
                Owner = Application.Current?.MainWindow,
                Width = 1100,
                Height = 860,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            jendela.ShowDialog();
        }


        private static string LabelJenis(string jenis) => jenis switch
        {
            "PERDES" => "Peraturan Desa",
            "PERKADES" => "Peraturan Kepala Desa",
            _ => "Surat Keputusan"
        };

        /// <summary>
        /// Memilih berkas lampiran. Word (.docx/.doc) dibaca untuk isi otomatis — berkas
        /// .doc lama dikonversi otomatis lebih dulu; PDF hanya dilampirkan — isinya tidak
        /// dibaca, pengguna diingatkan mengisi formulir secara manual.
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
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
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

                // PERDES tidak lagi menerima lampiran Word: naskahnya disusun
                // sebagai halaman dan diterbitkan generator PDF. Berkas seperti
                // ini dibatalkan supaya tidak tersimpan sebagai lampiran PERDES.
                if (string.Equals(detectedType, "PERDES", StringComparison.OrdinalIgnoreCase) && !IsPerdes)
                {
                    _wordFilePath = string.Empty;
                    UpdateFileState();
                    await _messageService.ShowWarningAsync(
                        "Berkas ini terdeteksi Peraturan Desa.\n\n" +
                        "Peraturan Desa disusun sebagai halaman naskah di menu PERDES, bukan sebagai lampiran Word. " +
                        "Buka tab PERDES lalu sunting halaman naskahnya.");
                    return;
                }

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
                        OnPropertyChanged(nameof(IsPerdes));
                        OnPropertyChanged(nameof(IsBukanPerdes));
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
                await _messageService.ShowErrorAsync(
                    "Tidak dapat membaca berkas Word. Pastikan berkas tidak rusak dan tidak sedang dibuka di aplikasi lain.\n\n" +
                    "Berkas .doc (Word lama) dikonversi otomatis memakai Microsoft Word; pastikan Microsoft Word terpasang di komputer ini.\n\n" +
                    ex.Message);
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

                // PERDES: PDF diterbitkan dari naskah halaman SEBELUM data disimpan.
                // Bila generator gagal, tidak ada baris arsip setengah jadi.
                byte[]? pdfNaskah = null;
                if (IsPerdes && HasNaskah)
                {
                    try
                    {
                        var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true) ?? new DesaData();
                        pdfNaskah = new NaskahPerdesGenerator(
                            desa, AmbilNaskah(), Nomor, ParseDate(Tanggal)!.Value).Buat();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal membuat PDF naskah Perdes");
                        await _messageService.ShowErrorAsync(
                            "PDF naskah gagal dibuat, data belum disimpan:\n\n" + ex.Message);
                        return;
                    }
                }

                var item = new DataKeputusan
                {
                    JenisKeputusan = _jenisKeputusan,
                    Nomor = Nomor.Trim(),
                    Tanggal = ParseDate(Tanggal)!.Value,
                    Tentang = Tentang.Trim(),
                    Keterangan = Keterangan.Trim(),
                    FileWord = _editData?.FileWord
                };
                string? berkasLama = item.FileWord;

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

                // Simpan berkas SEBELUM halaman ditutup agar FileWord sudah terbaru
                // saat daftar induk memuat ulang.
                if (pdfNaskah != null)
                {
                    try
                    {
                        await SaveNaskahPdfAsync(item, pdfNaskah, berkasLama);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyimpan PDF naskah Perdes (data sudah tersimpan)");
                        await _messageService.ShowErrorAsync(
                            "Data tersimpan, tetapi PDF naskah gagal ditulis:\n\n" + ex.Message);
                    }
                }
                else if (HasNewAttachment)
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

                // Halaman ditutup agar daftar induk memuat ulang dan melihat FileWord terbaru.
                Tersimpan?.Invoke();
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
                    try { File.Delete(oldPath); } catch { /* berkas lama terkunci — versi baru tetap disalin */ }
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

        /// <summary>
        /// Tulis PDF naskah ke arsip, simpan sumber naskah di sampingnya, lalu
        /// perbarui kolom FileWord. Berkas lama (termasuk Word) dibersihkan bila
        /// jalurnya berbeda.
        /// </summary>
        private async Task SaveNaskahPdfAsync(DataKeputusan item, byte[] pdf, string? berkasLama)
        {
            var (fileName, destPath) = _repository.ResolveWordStorage(
                item.IdBarisExcel, item.JenisKeputusan, item.Nomor, ".pdf");
            var oldPath = string.IsNullOrWhiteSpace(berkasLama)
                ? null
                : _repository.ResolveWordFullPath(berkasLama);

            _fileService.EnsureDirectoryExists(Path.GetDirectoryName(destPath) ?? destPath);
            await File.WriteAllBytesAsync(destPath, pdf).ConfigureAwait(true);

            item.FileWord = fileName;
            await _repository.UpdateAsync(item); // persist kolom FileWord
            NaskahPerdesStore.Simpan(destPath, AmbilNaskah());
            _storedFileName = fileName;

            if (oldPath != null && !string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(oldPath);
                    NaskahPerdesStore.Hapus(oldPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Berkas Perdes lama tidak dapat dihapus: {Path}", oldPath);
                }
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

    }
}
