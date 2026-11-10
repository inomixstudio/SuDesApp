using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
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
    public partial class InputKeputusanViewModel : ObservableObject, IJudulHalaman
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
        /// <summary>Nama PDF sumber saat formulir dibuka lewat Salin — naskahnya ikut tersalin.</summary>
        private string? _naskahSalinan;

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


    }
}
