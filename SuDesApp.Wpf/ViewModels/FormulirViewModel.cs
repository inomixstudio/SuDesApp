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
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
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
    /// (infosadaradmindukkarawang.id/insankarawang/page/jenis-formulir) —
    /// halaman resmi formulir administrasi kependudukan Kabupaten Karawang.
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
    /// resmi Kabupaten Karawang (infosadaradmindukkarawang.id) dan, bila
    /// tersedia, sinkronkan ke Google Drive. Padanan dialog Formulir WinForms.
    /// </summary>
    public partial class FormulirViewModel : ObservableObject, IDisposable
    {
        private readonly AppConfig _appConfig;
        private readonly SettingsManager? _pengaturan;
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

        private readonly PenjagaLisensi? _penjaga;

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
            Func<string, FormulirPdfViewModel>? pdfPreviewFactory = null,
            PenjagaLisensi? penjaga = null,
            SettingsManager? pengaturan = null)
        {
            _appConfig = appConfig;
            _pengaturan = pengaturan;
            _fileService = fileService;
            _messageService = messageService;
            _formulirMenuService = formulirMenuService;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _driveService = driveService;
            _notifications = notifications;
            _navigation = navigation;
            _pdfPreviewFactory = pdfPreviewFactory;
            _penjaga = penjaga;

            Templates = new ObservableCollection<FormulirTemplate>();
            TemplatesTampil = new ObservableCollection<FormulirTemplate>();
            // Tombol Hapus hanya berguna bila ada baris yang dicentang.
            DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => AdaTerpilih);
            HasilUnduhanRincian = new ObservableCollection<string>();

            AddCommand = new AsyncRelayCommand(AddTemplateAsync);
            RefreshCommand = new AsyncRelayCommand(LoadTemplatesAsync);
            // Unduhan hanya masuk akal bila kabupaten desa memang didukung.
            DownloadCommand = new AsyncRelayCommand(DownloadFromSourceAsync, () => !IsLoading && UnduhanOtomatis);
            TogglePilihSemuaCommand = new RelayCommand(TogglePilihSemua);
            BukaFolderCommand = new RelayCommand(BukaFolderTemplate);
            TerimaTawaranUnduhCommand = new AsyncRelayCommand(AcceptDownloadOfferAsync);
            TutupTawaranUnduhCommand = new RelayCommand(TutupTawaranUnduh);
            TutupHasilUnduhanCommand = new RelayCommand(TutupHasilUnduhan);
            BukaSitusCommand = new RelayCommand(BukaSitusSumber);
            BukaPengaturanDesaCommand = new RelayCommand(BukaPengaturanDesa);

            // Data desa bisa berubah dari halaman Pengaturan Surat selama aplikasi
            // berjalan; halaman ini hidup sepanjang sesi, jadi sumber formulir
            // ikut disesuaikan tanpa pengguna perlu membuka ulang menunya.
            if (_pengaturan != null)
            {
                _pengaturan.PengaturanDesaTersimpan += OnPengaturanDesaTersimpan;
            }

            _ = LoadTemplatesAsync();
        }

        /// <summary>Melepas langganan data desa saat scope halaman dibuang.</summary>
        public void Dispose()
        {
            if (_pengaturan != null)
            {
                _pengaturan.PengaturanDesaTersimpan -= OnPengaturanDesaTersimpan;
            }
        }

        private readonly FormulirMenuService _formulirMenuService;

        /// <summary>Seluruh berkas template PDF di folder Templates (urutan abjad).</summary>
        public ObservableCollection<FormulirTemplate> Templates { get; }

        /// <summary>Baris yang benar-benar ditampilkan — hasil penyaringan kotak pencarian.</summary>
        public ObservableCollection<FormulirTemplate> TemplatesTampil { get; }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    // Tombol unduh ikut mati selama proses berjalan.
                    (DownloadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }
        public bool HasTemplates => Templates.Count > 0;
        public bool IsEmpty => Templates.Count == 0;

        /// <summary>Jumlah template formulir yang tersedia, untuk statusbar.</summary>
        public int TemplateCount => Templates.Count;

        /// <summary>Jumlah baris yang tampil setelah disaring pencarian.</summary>
        public int JumlahTampil => TemplatesTampil.Count;

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }


    }
}
