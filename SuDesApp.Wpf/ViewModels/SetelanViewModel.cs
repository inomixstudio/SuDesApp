using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel untuk halaman Pengaturan Surat (padanan SetelanForm WinForms).
    /// Susunannya memakai navigasi bagian di sisi kiri (Data Desa, Pejabat Desa,
    /// Pejabat Kecamatan, Cetak &amp; Logo) sehingga isian tidak perlu digulir panjang.
    ///
    /// Data desa bawaan aplikasi masih berupa contoh ("Nama Desa" dan sejenisnya),
    /// jadi selama masih contoh halaman ini menampilkan peringatan dan bagian yang
    /// perlu diisi ditandai pada navigasinya. Memuat, memvalidasi (field wajib,
    /// kodepos 5 digit, NIP camat >= 9 digit bila diisi), lalu menyimpan data desa.
    ///
    /// Di atas isian ada pratinjau kop surat yang mengikuti isian secara langsung
    /// (nama desa, pejabat, dan logo) sehingga pengguna melihat hasilnya sebelum
    /// menekan Simpan. Teksnya disusun <see cref="PratinjauKopSurat"/> dari sumber
    /// yang sama dengan generator PDF, jadi tidak bisa berbeda dari hasil cetak.
    /// </summary>
    public partial class SetelanViewModel : ObservableObject
    {
        private static readonly Regex _kodeposRegex = new(@"^\d{5}$", RegexOptions.Compiled);

        /// <summary>
        /// Nilai jenis wilayah yang disimpan bersama data desa. Sama dengan
        /// <see cref="WilayahDaerah.Awalan"/> untuk tingkat kabupaten/kota, dan
        /// dibaca generator surat lewat <see cref="KopSurat.LabelKabupaten"/>.
        /// </summary>
        private const string _konstJenisKabupaten = "Kabupaten";
        private const string _konstJenisKota = "Kota";

        /// <summary>
        /// Nilai jenis lembaga yang disimpan bersama data desa: «Desa» atau
        /// «Kelurahan». Menentukan jabatan penandatangan («Kepala Desa …»/
        /// «Lurah …»), kata “PEMERINTAH DESA/KELURAHAN” pada kop, serta apakah
        /// NIP pejabat tercetak di bawah namanya — dibaca generator lewat
        /// <see cref="KopSurat.KataLembaga"/>.
        /// </summary>
        private const string _konstLembagaDesa = "Desa";
        private const string _konstLembagaKelurahan = "Kelurahan";

        /// <summary>
        /// Pemeriksaan ringan untuk email desa (opsional): harus memuat satu "@" dengan
        /// teks di kedua sisinya. Sengaja tidak ketat supaya alamat kantor desa yang
        /// tidak lazim tetap bisa disimpan.
        /// </summary>
        private static readonly Regex _emailRegex = new(@"^[^\s@]+@[^\s@]+$", RegexOptions.Compiled);

        /// <summary>Dipicu setelah pengaturan berhasil disimpan (mis. refresh status bar utama).</summary>
        public static event Action? SettingsSaved;

        private readonly SettingsManager _settingsManager;
        private readonly IMessageService _messageService;
        private readonly ILogger<SetelanViewModel> _logger;
        private readonly NavigationService _navigation;

        /// <summary>Data wilayah bawaan aplikasi (provinsi s.d. desa/kelurahan + kode pos).</summary>
        private readonly WilayahDataStore _dataWilayah;

        /// <summary>Aturan pilihan berjenjang — dipisah dari UI supaya bisa diuji langsung.</summary>
        private readonly WilayahPemilih _pemilih;
        private readonly SuDesApp.Configuration.IJenisSuratConfigLoader? _jenisSuratConfig;

        /// <summary>Nilai tersimpan terakhir — pembanding untuk HasChanges (paritas HasDataChanged WinForms).</summary>
        private DesaData _snapshot = new();

        /// <summary>
        /// Dispatcher utas halaman — utas yang MEMILIKI isian dan koleksi yang
        /// diikat XAML. Diambil saat konstruksi supaya tiap perubahan keadaan
        /// terikat dijalankan di utas yang sama, tidak bergantung pada
        /// SynchronizationContext yang kebetulan sedang terpasang (kelanjutan
        /// <c>await</c> tanpa konteks berjalan di utas kolam, dan WPF menolak
        /// perubahan koleksi terikat dari utas lain).
        /// </summary>
        private readonly Dispatcher? _dispatcherHalaman = Dispatcher.FromThread(Thread.CurrentThread);

        private string _namaDesa = string.Empty;
        private string _kecamatan = string.Empty;
        private string _kabupaten = string.Empty;

        /// <summary>Jenis wilayah tingkat kabupaten/kota: “Kabupaten”, “Kota”, atau kosong bila belum diketahui.</summary>
        private string _jenisKabupaten = string.Empty;

        /// <summary>Jenis lembaga pemerintahan: “Desa”, “Kelurahan”, atau kosong (dianggap “Desa”).</summary>
        private string _jenisLembaga = string.Empty;

        private string _alamat = string.Empty;
        private string _kodepos = string.Empty;
        private string _email = string.Empty;
        private string _kepalaDesa = string.Empty;
        private string _sekdes = string.Empty;
        private string _namaCamat = string.Empty;
        private string _nipCamat = string.Empty;
        private string _golCamat = string.Empty;
        private string _nipKepalaDesa = string.Empty;
        private bool _isLoading;
        private bool _hasChanges;

        public SetelanViewModel(
            SettingsManager settingsManager,
            IMessageService messageService,
            ILogger<SetelanViewModel> logger,
            NavigationService navigation,
            WilayahDataStore dataWilayah,
            SuDesApp.Configuration.IJenisSuratConfigLoader? jenisSuratConfig = null)
        {
            _settingsManager = settingsManager;
            _messageService = messageService;
            _logger = logger;
            _navigation = navigation;
            _dataWilayah = dataWilayah;
            _jenisSuratConfig = jenisSuratConfig;
            _pemilih = new WilayahPemilih(dataWilayah);
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasChanges && !IsLoading);
            // Batal jalan bersama muat awal akan memulai pencocokan wilayah kedua
            // di data pemilih yang sama — kundainya digerbangi seperti tombol Simpan.
            CancelCommand = new AsyncRelayCommand(CancelAsync, () => !IsLoading);
            GantiLogoCommand = new AsyncRelayCommand(GantiLogoAsync);
            PakaiLogoBawaanCommand = new AsyncRelayCommand(PakaiLogoBawaanAsync);
            PilihBagianCommand = new RelayCommand<BagianSetelanItemViewModel>(PilihBagian, b => b != null);
            PratinjauToggleCommand = new RelayCommand(TogglePratinjau);

            Bagian = new ObservableCollection<BagianSetelanItemViewModel>
            {
                new(1, "Data Desa", "Nama, kecamatan, kabupaten, alamat, dan kodepos"),
                new(2, "Pejabat Desa", "Nama Kepala Desa & Sekretaris Desa pada tanda tangan"),
                new(3, "Pejabat Kecamatan", "Nama, NIP, dan golongan camat — opsional"),
                new(4, "Cetak & Logo", "Ukuran kertas PDF dan logo kop surat")
            };

            TampilkanBagian(0);
            // Pratinjau awal disusun setelah LoadAsync siap (bukan dari form kosong).
            // LoadAsync dipanggil eksplisit oleh navigasi lewat DibukaAsync().
        }

        private bool _sudahDimuatPermintaan;
        private string? _kodeKabupatenTerakhir;

        /// <summary>
        /// Dipanggil navigasi setiap halaman dibuka..Idempotent: hanya memuat
        /// sekali per sesi; kunjungan berikutnya langsung memakai isian yang ada.
        /// </summary>
        public async Task DibukaAsync()
        {
            if (_sudahDimuatPermintaan) return;
            _sudahDimuatPermintaan = true;
            await LoadAsync();
        }


    }
}
