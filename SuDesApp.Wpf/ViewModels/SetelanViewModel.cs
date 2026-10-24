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
    public class SetelanViewModel : ObservableObject
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
            PerbaruiPratinjau();
            _ = LoadAsync();
        }

        // =================================================================
        // Navigasi bagian halaman
        // =================================================================

        /// <summary>Daftar bagian yang ditampilkan pada navigasi kiri.</summary>
        public ObservableCollection<BagianSetelanItemViewModel> Bagian { get; }

        public RelayCommand<BagianSetelanItemViewModel> PilihBagianCommand { get; }

        /// <summary>Nomor bagian yang sedang dibuka (0 = Data Desa).</summary>
        public int BagianAktif => Bagian.Count == 0
            ? 0
            : System.Math.Max(0, Bagian.ToList().FindIndex(b => b.Aktif));

        public bool TampilDesa => Bagian.Count == 0 || Bagian[0].Aktif;
        public bool TampilPejabatDesa => Bagian.Count > 1 && Bagian[1].Aktif;
        public bool TampilPejabatKecamatan => Bagian.Count > 2 && Bagian[2].Aktif;
        public bool TampilCetak => Bagian.Count > 3 && Bagian[3].Aktif;

        private void PilihBagian(BagianSetelanItemViewModel? bagian)
        {
            if (bagian == null) return;
            TampilkanBagian(bagian.Nomor - 1);
        }

        /// <summary>Buka satu bagian dan perbarui penanda bagian aktif.</summary>
        public void TampilkanBagian(int nomor)
        {
            if (Bagian.Count == 0) return;

            int tujuan = System.Math.Clamp(nomor, 0, Bagian.Count - 1);
            for (int i = 0; i < Bagian.Count; i++)
            {
                Bagian[i].Aktif = i == tujuan;
            }

            OnPropertyChanged(nameof(BagianAktif));
            OnPropertyChanged(nameof(TampilDesa));
            OnPropertyChanged(nameof(TampilPejabatDesa));
            OnPropertyChanged(nameof(TampilPejabatKecamatan));
            OnPropertyChanged(nameof(TampilCetak));
        }

        // =================================================================
        // Utas halaman. Isian dan koleksi di halaman ini dimiliki utas UI; hanya
        // pekerjaan murni (baca database, cocokkan ±90 ribu baris wilayah) yang
        // boleh berjalan di utas lain. Setiap perpindahan kembali ke utas
        // halaman lewat dua metode di bawah, jadi tidak ada tempat lain yang
        // diam-diam mengubah keadaan terikat dari utas kolam.
        // =================================================================

        /// <summary>
        /// Jalankan perubahan keadaan terikat XAML di utas halaman. Bila sudah
        /// berada di utas itu (keadaan normal di aplikasi), aksinya dijalankan
        /// langsung tanpa perpindahan.
        /// </summary>
        private Task JalankanDiUtasHalamanAsync(Action aksi)
        {
            var dispatcher = _dispatcherHalaman ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                aksi();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(aksi).Task;
        }

        /// <inheritdoc cref="JalankanDiUtasHalamanAsync(Action)"/>
        private Task JalankanDiUtasHalamanAsync(Func<Task> aksi)
        {
            var dispatcher = _dispatcherHalaman ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                return aksi();
            }

            return dispatcher.InvokeAsync(aksi).Task.Unwrap();
        }

        // =================================================================
        // Pengaturan cetak (ukuran kertas & logo kop). Disimpan langsung ke
        // preferensi aplikasi, jadi tidak ikut tombol "Simpan" data desa.
        // =================================================================

        private UkuranKertasSurat _ukuranKertas = UkuranKertasSurat.A4;
        private string _jalurLogo = string.Empty;

        /// <summary>Ukuran kertas A4 (bawaan aplikasi).</summary>
        public bool UkuranKertasA4
        {
            get => _ukuranKertas == UkuranKertasSurat.A4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.A4); }
        }

        /// <summary>Ukuran kertas F4/Folio (8,5 x 13 inci).</summary>
        public bool UkuranKertasF4
        {
            get => _ukuranKertas == UkuranKertasSurat.F4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.F4); }
        }

        /// <summary>Jalur gambar logo kop yang sedang dipakai (untuk pratinjau).</summary>
        public string JalurLogo
        {
            get => _jalurLogo;
            private set
            {
                if (SetProperty(ref _jalurLogo, value))
                {
                    OnPropertyChanged(nameof(AdaLogo));
                    OnPropertyChanged(nameof(KeteranganLogo));
                }
            }
        }

        public bool AdaLogo => !string.IsNullOrWhiteSpace(_jalurLogo);

        /// <summary>Keterangan asal logo: bawaan aplikasi atau pilihan pengguna.</summary>
        public string KeteranganLogo
        {
            get
            {
                if (!AdaLogo)
                {
                    return "Belum ada gambar logo — kop surat akan dicetak tanpa logo.";
                }

                return !string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo())
                    ? $"Logo pilihan Anda: {System.IO.Path.GetFileName(_jalurLogo)}"
                    : "Logo bawaan aplikasi.";
            }
        }

        // =================================================================
        // Sumber logo kop: bawaan / unggahan sendiri / lambang kabupaten.
        // Pilihan bersifat OPSIONAL — logo desa yang diunggah tetap yang
        // utama; lambang kabupaten hanya dipakai bila pengguna memilihnya,
        // dan pilihan otomatis hanya terjadi bila gambarnya benar-benar
        // tersedia (lihat LogoKabupaten).
        // =================================================================

        /// <summary>Benar bila sumber logo sedang "logo yang saya unggah".</summary>
        public bool SumberLogoUnggahan
        {
            get => PengaturanCetak.GetSumberLogo() == PengaturanCetak.SumberLogo.Unggahan;
            set { if (value) TerapkanSumberLogo(PengaturanCetak.SumberLogo.Unggahan); }
        }

        /// <summary>Benar bila sumber logo sedang "lambang kabupaten terpilih".</summary>
        public bool SumberLogoKabupaten
        {
            get => PengaturanCetak.GetSumberLogo() == PengaturanCetak.SumberLogo.LambangKabupaten;
            set { if (value) TerapkanSumberLogo(PengaturanCetak.SumberLogo.LambangKabupaten); }
        }

        /// <summary>
        /// Lambang kabupaten yang tersedia untuk dipilih. Hanya berisi entri bila
        /// berkasnya benar-benar ada, jadi daftar tidak pernah menawarkan lambang
        /// yang tidak bisa ditampilkan. Nilai null bila kabupaten terpilih belum
        /// punya lambang.
        /// </summary>
        public IReadOnlyList<LambangKabupatenItem> DaftarLambangKabupaten { get; private set; } = Array.Empty<LambangKabupatenItem>();

        /// <summary>Lambang kabupaten yang sedang dipilih (null bila tidak ada).</summary>
        public LambangKabupatenItem? LambangKabupatenTerpilih
        {
            get
            {
                string kode = PengaturanCetak.GetKodeLogoKabupaten();
                return DaftarLambangKabupaten.FirstOrDefault(l => string.Equals(l.Kode, kode, StringComparison.OrdinalIgnoreCase));
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                PengaturanCetak.SetKodeLogoKabupaten(value.Kode);
                TerapkanSumberLogo(PengaturanCetak.SumberLogo.LambangKabupaten);
                OnPropertyChanged(nameof(LambangKabupatenTerpilih));
            }
        }

        /// <summary>Kalimat penjelas untuk pilihan lambang kabupaten saat ini.</summary>
        public string InfoLambangKabupaten
        {
            get
            {
                string nama = _pemilih.KabupatenTerpilih?.NamaLengkap ?? "kabupaten/kota terpilih";
                if (DaftarLambangKabupaten.Count == 0)
                {
                    return $"Belum ada berkas lambang untuk {nama}. Taruh berkasnya (nama = kode kabupaten, mis. 32.15.png) " +
                           "di folder LogosKabupaten, lalu buka lagi halaman ini.";
                }

                return DaftarLambangKabupaten.Count == 1
                    ? $"1 lambang tersedia untuk {nama}."
                    : $"{DaftarLambangKabupaten.Count} lambang tersedia untuk {nama}.";
            }
        }

        /// <summary>Pindahkan sumber logo dan segarkan pratinjau.</summary>
        private void TerapkanSumberLogo(PengaturanCetak.SumberLogo sumber)
        {
            PengaturanCetak.SetSumberLogo(sumber);
            OnPropertyChanged(nameof(SumberLogoUnggahan));
            OnPropertyChanged(nameof(SumberLogoKabupaten));
            SegarkanLogoEfektif();
        }

        /// <summary>Segarkan jalur logo yang ditampilkan pratinjau sesuai sumber terkini.</summary>
        private void SegarkanLogoEfektif()
        {
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            PerbaruiPratinjau();
        }

        /// <summary>
        /// Bangun daftar lambang kabupaten yang tersedia untuk kabupaten terpilih,
        /// lalu — HANYA bila ada lambang — pilih otomatis sebagai sumber logo.
        /// Otomatis ini aman karena tidak mengambil alih logo desa yang diunggah
        /// (pengguna bisa kembali kapan saja lewat pilihan sumber), dan hanya terjadi
        /// bila berkas lambangnya benar-benar ada.
        /// </summary>
        private void SegarkanLambangKabupaten(bool pilihOtomatis)
        {
            var kab = _pemilih.KabupatenTerpilih;
            var semua = LogoKabupaten.SemuaTersedia();

            DaftarLambangKabupaten = kab == null
                ? Array.Empty<LambangKabupatenItem>()
                : semua
                    .Where(kvp => string.Equals(kvp.Key.Trim(), kab.Kode.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => new LambangKabupatenItem(kvp.Key, kvp.Value, kab.NamaLengkap))
                    .ToList();

            OnPropertyChanged(nameof(DaftarLambangKabupaten));
            OnPropertyChanged(nameof(InfoLambangKabupaten));
            OnPropertyChanged(nameof(LambangKabupatenTerpilih));

            if (pilihOtomatis && DaftarLambangKabupaten.Count > 0 && BolehPilihOtomatisLambang())
            {
                LambangKabupatenTerpilih = DaftarLambangKabupaten[0];
            }
        }

        /// <summary>
        /// Pemilihan lambang kabupaten otomatis boleh terjadi bila:
        ///   • pengguna sudah memakai lambang kabupaten (ikut berganti), atau
        ///   • pengguna belum pernah mengunggah logo desa sendiri.
        /// Logo desa yang diunggah selalu menang — lambang kabupaten bersifat
        /// alternatif, jadi pilihan otomatis tidak boleh menimpa keputusan eksplisit.
        ///
        /// Saat halaman dibuka (pilihOtomatis=false saat load, lihat
        /// SiapkanPilihanWilayahAsync), auto-select tidak terjadi lewat sini —
        /// dilakukan terpisah di akhir pemuatan wilayah supaya konsisten.
        /// </summary>
        private static bool BolehPilihOtomatisLambang()
        {
            var sumber = PengaturanCetak.GetSumberLogo();
            if (sumber == PengaturanCetak.SumberLogo.LambangKabupaten)
            {
                // Sudah memakai lambang: boleh ikut berganti mengikuti kabupaten baru.
                return true;
            }

            // Bawaan / unggahan: otomatis hanya bila belum pernah mengunggah logo.
            return string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo());
        }

        /// <summary>Satu entri lambang kabupaten yang tersedia untuk dipilih di UI.</summary>
        public sealed record LambangKabupatenItem(string Kode, string Jalur, string Nama);

        public AsyncRelayCommand GantiLogoCommand { get; }
        public AsyncRelayCommand PakaiLogoBawaanCommand { get; }

        /// <summary>Simpan pilihan ukuran kertas; langsung berlaku untuk PDF berikutnya.</summary>
        private void TerapkanUkuranKertas(UkuranKertasSurat ukuran)
        {
            if (_ukuranKertas == ukuran)
            {
                return;
            }

            _ukuranKertas = ukuran;
            PengaturanCetak.SetUkuranKertas(ukuran);
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            _logger.LogInformation("Ukuran kertas PDF diubah menjadi {Ukuran}", ukuran);

            // Skala pratinjau dihitung dari lebar kertas, jadi pratinjau ikut disegarkan.
            PerbaruiPratinjau();
        }

        /// <summary>Muat pengaturan cetak yang tersimpan.</summary>
        private void MuatPengaturanCetak()
        {
            _ukuranKertas = PengaturanCetak.GetUkuranKertas();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
            OnPropertyChanged(nameof(SumberLogoUnggahan));
            OnPropertyChanged(nameof(SumberLogoKabupaten));
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            PerbaruiPratinjau();
        }

        /// <summary>Pilih gambar logo baru dari komputer, lalu salin ke folder data aplikasi.</summary>
        private async Task GantiLogoAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih gambar logo kop surat",
                Filter = "Gambar (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string? tersimpan = PengaturanCetak.SimpanLogoPengguna(dialog.FileName);
            if (tersimpan == null)
            {
                await _messageService.ShowErrorAsync("Gambar logo gagal dipakai. Pastikan berkasnya masih ada dan bisa dibaca.");
                return;
            }

            JalurLogo = tersimpan;
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            PerbaruiPratinjau();
            _logger.LogInformation("Logo kop surat diganti: {Jalur}", tersimpan);
            await _messageService.ShowInfoAsync("Logo kop surat berhasil diganti dan langsung dipakai pada surat berikutnya.");
        }

        /// <summary>Kembalikan logo kop ke logo bawaan aplikasi.</summary>
        private async Task PakaiLogoBawaanAsync()
        {
            PengaturanCetak.HapusLogoPengguna();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));

            // Pratinjau langsung memakai logo bawaan lagi.
            PerbaruiPratinjau();
            await _messageService.ShowInfoAsync("Logo kop surat dikembalikan ke logo bawaan aplikasi.");
        }

        public string NamaDesa { get => _namaDesa; set { if (SetProperty(ref _namaDesa, value)) OnFieldChanged(); } }
        public string Kecamatan { get => _kecamatan; set { if (SetProperty(ref _kecamatan, value)) OnFieldChanged(); } }
        public string Kabupaten { get => _kabupaten; set { if (SetProperty(ref _kabupaten, value)) OnFieldChanged(); } }

        // =================================================================
        // Jenis wilayah tingkat kabupaten/kota. Ini yang menentukan kata pada
        // baris pertama kop: “PEMERINTAH KOTA BANDUNG” atau “PEMERINTAH
        // KABUPATEN BANDUNG”. Pada mode pilih, nilainya mengikuti pilihan dari
        // daftar wilayah (Kabupaten Bandung dan Kota Bandung bernama sama, jadi
        // hanya pilihan pengguna yang bisa memastikan). Pada mode manual, pengguna
        // memilihnya lewat dua pilihan di bawah kolom Kabupaten.
        // =================================================================

        /// <summary>Benar bila wilayah desa berada di sebuah kabupaten.</summary>
        public bool WilayahKabupaten
        {
            get => _jenisKabupaten == _konstJenisKabupaten;
            set { if (value) TerapkanJenisKabupaten(_konstJenisKabupaten); }
        }

        /// <summary>Benar bila wilayah desa berada di sebuah kota.</summary>
        public bool WilayahKota
        {
            get => _jenisKabupaten == _konstJenisKota;
            set { if (value) TerapkanJenisKabupaten(_konstJenisKota); }
        }

        /// <summary>Keterangan kata yang akan dicetak pada baris pertama kop surat.</summary>
        public string InfoJenisWilayah => _jenisKabupaten switch
        {
            _konstJenisKota => "Kop dicetak \"PEMERINTAH KOTA …\".",
            _konstJenisKabupaten => "Kop dicetak \"PEMERINTAH KABUPATEN …\".",
            _ => "Jenis wilayah belum ditentukan — kop dicetak \"PEMERINTAH KABUPATEN …\" " +
                 "seperti sebelumnya. Pilih salah satu supaya penulisannya pasti."
        };

        /// <summary>Ubah jenis wilayah sebagai tindakan pengguna (ikut menandai perubahan belum disimpan).</summary>
        private void TerapkanJenisKabupaten(string jenis)
        {
            if (string.Equals(_jenisKabupaten, jenis, StringComparison.Ordinal))
            {
                return;
            }

            _jenisKabupaten = jenis;
            SegarkanJenisKabupaten();
            OnFieldChanged();
            _logger.LogInformation("Jenis wilayah kabupaten/kota diubah menjadi {Jenis}", jenis);
        }

        /// <summary>
        /// Ambil jenis wilayah dari kabupaten/kota yang sedang dipilih pada jenjang.
        /// Dipakai saat pilihan wilayah dikenali dari data tersimpan maupun saat
        /// pengguna memilih dari daftar — supaya kop surat tidak perlu menebak.
        /// </summary>
        /// <returns>True bila jenis wilayahnya berubah.</returns>
        private bool SinkronkanJenisKabupatenDariPilihan()
        {
            var kabupaten = _pemilih.KabupatenTerpilih;
            if (kabupaten == null)
            {
                // Belum ada pilihan tingkat kabupaten/kota: nilai tersimpan tidak
                // dihapus supaya isian manual tidak kehilangan keterangannya.
                return false;
            }

            string jenis = kabupaten.Jenis == WilayahJenis.Kota ? _konstJenisKota : _konstJenisKabupaten;
            if (string.Equals(_jenisKabupaten, jenis, StringComparison.Ordinal))
            {
                return false;
            }

            _jenisKabupaten = jenis;
            SegarkanJenisKabupaten();

            // Jenis wilayah menentukan isi kop dan apakah data masih berbeda dari yang
            // tersimpan — data desa lama belum menyimpan kolom ini, jadi setelah
            // dikenali dari pilihan wilayah tombol Simpan perlu ikut menyala.
            OnFieldChanged();
            return true;
        }

        private void SegarkanJenisKabupaten()
        {
            OnPropertyChanged(nameof(WilayahKabupaten));
            OnPropertyChanged(nameof(WilayahKota));
            OnPropertyChanged(nameof(InfoJenisWilayah));
        }

        // =================================================================
        // Jenis lembaga (Desa/Kelurahan) dan NIP pejabat. Menentukan kata “DESA”
        // atau “KELURAHAN” pada kop dan badan surat, jabatan penandatangan
        // (“Kepala Desa …” / “Lurah …”), serta apakah NIP tercetak di bawah nama
        // penandatangan: hanya Lurah (kelurahan) yang ASN (
        // Kepala Desa non-ASN tidak memakai NIP).
        // Pada mode pilih, jenisnya mengikuti wilayah yang dipilih dari daftar
        // (WilayahJenis.Desa/Kelurahan); pada mode manual, keduanya dipilih pakai
        // dua pilihan di bawah kolom Nama Desa.
        // =================================================================

        /// <summary>Benar bila lembaga berjenis Desa (nilai bawaan data lama).</summary>
        public bool LembagaDesa
        {
            get => !LembagaKelurahan;
            set { if (value) TerapkanJenisLembaga(_konstLembagaDesa); }
        }

        /// <summary>Benar bila lembaga berjenis Kelurahan — penandatangan Lurah + NIP.</summary>
        public bool LembagaKelurahan
        {
            get => _jenisLembaga == _konstLembagaKelurahan;
            set { if (value) TerapkanJenisLembaga(_konstLembagaKelurahan); }
        }

        /// <summary>Keterangan kata yang tercetak pada kop dan badan surat.</summary>
        public string InfoJenisLembaga => LembagaKelurahan
            ? "Surat mencetak \"KELURAHAN …\"; penandatangan Lurah dan NIP ikut tercetak di bawah nama."
            : "Surat mencetak \"PEMERINTAH DESA …\"; penandatangan Kepala Desa (non-ASN, tanpa NIP).";

        /// <summary>Label kolom pejabat penandatangan: “Kepala Desa *” atau “Lurah *”.</summary>
        public string LabelPejabatKepala => LembagaKelurahan ? "Lurah *" : "Kepala Desa *";

        /// <summary>Keterangan kolom pejabat penandatangan pada bagian Pejabat Desa.</summary>
        public string InfoPejabatKepala => LembagaKelurahan
            ? "Nama Lurah pada tanda tangan surat; kolom NIP di bawahnya ikut tercetak."
            : "Nama Kepala Desa pada tanda tangan surat.";

        /// <summary>
        /// NIP pejabat penandatangan — hanya tercetak untuk Kelurahan (Lurah).
        /// Untuk Desa, kolomnya boleh dikosongkan: Kepala Desa bukan ASN.
        /// </summary>
        public string NipKepalaDesa { get => _nipKepalaDesa; set { if (SetProperty(ref _nipKepalaDesa, value)) OnFieldChanged(); } }

        /// <summary>Ubah jenis lembaga sebagai tindakan pengguna (ikut menyalakan tombol Simpan).</summary>
        private void TerapkanJenisLembaga(string jenis)
        {
            if (string.Equals(_jenisLembaga, jenis, StringComparison.Ordinal))
            {
                return;
            }

            _jenisLembaga = jenis;
            SegarkanJenisLembaga();
            OnFieldChanged();
            _logger.LogInformation("Jenis lembaga diubah menjadi {Jenis}", jenis);
        }

        /// <summary>
        /// Ambil jenis lembaga dari desa/kelurahan yang sedang dipilih pada jenjang
        /// wilayah — sama seperti jenis kabupaten/kota, supaya tidak perlu minta
        /// pengguna memilih ulang yang sudah jelas dari daftar.
        /// </summary>
        /// <returns>True bila jenis lembaganya berubah.</returns>
        private bool SinkronkanJenisLembagaDariPilihan()
        {
            var desa = _pemilih.DesaTerpilih;
            if (desa == null)
            {
                // Belum ada pilihan tingkat desa/kelurahan: nilai tersimpan tidak
                // dihapus supaya isian manual tidak kehilangan keterangannya.
                return false;
            }

            string jenis = desa.Jenis == WilayahJenis.Kelurahan ? _konstLembagaKelurahan : _konstLembagaDesa;
            if (string.Equals(_jenisLembaga, jenis, StringComparison.Ordinal))
            {
                return false;
            }

            _jenisLembaga = jenis;
            SegarkanJenisLembaga();
            OnFieldChanged();
            return true;
        }

        private void SegarkanJenisLembaga()
        {
            OnPropertyChanged(nameof(LembagaDesa));
            OnPropertyChanged(nameof(LembagaKelurahan));
            OnPropertyChanged(nameof(InfoJenisLembaga));
            OnPropertyChanged(nameof(LabelPejabatKepala));
            OnPropertyChanged(nameof(InfoPejabatKepala));
        }
        public string Alamat { get => _alamat; set { if (SetProperty(ref _alamat, value)) OnFieldChanged(); } }
        public string Kodepos { get => _kodepos; set { if (SetProperty(ref _kodepos, value)) OnFieldChanged(); } }

        // =================================================================
        // Pilihan wilayah berjenjang: provinsi → kabupaten/kota → kecamatan →
        // desa/kelurahan, lalu kode pos saran. Pengguna cukup memilih, dan nama
        // wilayah pada kop surat tidak lagi bergantung pada ketikan bebas.
        // =================================================================

        // Daftar pilihan sengaja berupa daftar tetap yang diganti utuh setiap kali
        // berubah, BUKAN koleksi yang isinya dikosongkan lalu diisi ulang: ComboBox
        // mengikat ItemsSource ke daftar ini, dan mengubah isi koleksi dari dalam
        // peristiwa pilihan ComboBox sendiri membuat WPF menyegarkan tampilannya di
        // tengah perubahan — itu menggagalkan aplikasi (lihat SegarkanDaftarWilayah).
        private IReadOnlyList<WilayahDaerah> _daftarProvinsi = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarKabupaten = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarKecamatan = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarDesa = Array.Empty<WilayahDaerah>();

        /// <summary>Provinsi yang bisa dipilih; Jawa Barat paling atas.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarProvinsi
        {
            get => _daftarProvinsi;
            private set => SetProperty(ref _daftarProvinsi, value);
        }

        /// <summary>Kabupaten/kota pada provinsi terpilih — seluruh Jawa Barat saat halaman dibuka.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarKabupaten
        {
            get => _daftarKabupaten;
            private set => SetProperty(ref _daftarKabupaten, value);
        }

        /// <summary>Kecamatan pada kabupaten/kota terpilih; kosong bila belum ada pilihan.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarKecamatan
        {
            get => _daftarKecamatan;
            private set => SetProperty(ref _daftarKecamatan, value);
        }

        /// <summary>Desa/kelurahan pada kecamatan terpilih; kosong bila belum ada pilihan.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarDesa
        {
            get => _daftarDesa;
            private set => SetProperty(ref _daftarDesa, value);
        }

        /// <summary>Penahan pengulangan: pengikatan yang menulis balik pilihan kosong
        /// tidak boleh memicu penyegaran kedua di tengah penyegaran pertama.</summary>
        private bool _sedangSegarkanWilayah;

        /// <summary>Permintaan penyegaran yang datang saat penyegaran berjalan.</summary>
        private bool _segarkanWilayahDiulang;

        /// <summary>Benar bila berkas data wilayah bawaan aplikasi terbaca.</summary>
        public bool WilayahTersedia => _pemilih.AdaData;

        private bool _modePilihWilayah = true;

        /// <summary>Benar bila wilayah dipilih dari daftar (bukan diketik manual).</summary>
        public bool ModePilihWilayah
        {
            get => _modePilihWilayah;
            private set
            {
                if (SetProperty(ref _modePilihWilayah, value))
                {
                    OnPropertyChanged(nameof(ModeIsiManual));
                }
            }
        }

        /// <summary>
        /// Saklar «isi manual» untuk desa yang wilayahnya tidak ada di daftar, atau
        /// data lama yang tidak dikenali data wilayah. Saat dicentang, isian nama
        /// desa/kecamatan/kabupaten kembali bisa diketik bebas seperti sebelumnya.
        /// </summary>
        public bool ModeIsiManual
        {
            get => !_modePilihWilayah;
            set => UbahModeWilayah(pakaiManual: value);
        }

        public WilayahDaerah? ProvinsiTerpilih
        {
            get => _pemilih.ProvinsiTerpilih;
            set
            {
                if (_pemilih.PilihProvinsi(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? KabupatenTerpilih
        {
            get => _pemilih.KabupatenTerpilih;
            set
            {
                if (_pemilih.PilihKabupaten(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? KecamatanTerpilih
        {
            get => _pemilih.KecamatanTerpilih;
            set
            {
                if (_pemilih.PilihKecamatan(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? DesaTerpilih
        {
            get => _pemilih.DesaTerpilih;
            set
            {
                if (_pemilih.PilihDesa(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        /// <summary>Keterangan asal data wilayah — acuan dan tanggal sumbernya.</summary>
        public string InfoDataWilayah => !_pemilih.AdaData
            ? _dataWilayah.PesanMasalah
            : $"Data wilayah {_dataWilayah.Info.Acuan}: {_dataWilayah.Info.JumlahBaris.ToString("N0", CultureInfo.GetCultureInfo("id-ID"))} wilayah " +
              $"(sumber diperbarui {_dataWilayah.Info.SumberDiperbarui}).";

        private string _catatanPilihanWilayah = string.Empty;

        /// <summary>Keterangan keadaan pilihan: dikenali, ambigu, atau perlu dipilih.</summary>
        public string CatatanPilihanWilayah
        {
            get => _catatanPilihanWilayah;
            private set
            {
                if (SetProperty(ref _catatanPilihanWilayah, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaCatatanPilihanWilayah));
                }
            }
        }

        public bool AdaCatatanPilihanWilayah => _catatanPilihanWilayah.Length > 0;

        /// <summary>Ringkasan rantai wilayah yang sudah lengkap (desa sampai provinsi).</summary>
        public string RingkasanPilihanWilayah
        {
            get
            {
                var rantai = _pemilih.Rantai;
                if (rantai == null)
                {
                    return string.Empty;
                }

                string kodepos = _pemilih.KodeposSaran.Length > 0
                    ? $" Kode pos saran: {_pemilih.KodeposSaran}."
                    : string.Empty;

                return $"{rantai.Desa.NamaLengkap}, {rantai.Kecamatan.NamaLengkap}, " +
                       $"{rantai.Kabupaten.NamaLengkap}, Provinsi {rantai.Provinsi.Nama}.{kodepos}";
            }
        }

        public bool AdaRingkasanPilihanWilayah => _pemilih.Rantai != null;

        /// <summary>Keterangan kolom kode pos: hasil saran data wilayah atau isian bebas.</summary>
        public string InfoKodepos => _pemilih.DesaTerpilih is { Berkodepos: true } desa &&
                                     string.Equals(Kodepos?.Trim(), desa.Kodepos, StringComparison.Ordinal)
            ? $"Kode pos {desa.Kodepos} terisi otomatis dari data wilayah untuk {desa.NamaLengkap} — " +
              "periksa dan ubah bila kantor desa memakai kode pos lain."
            : "5 digit angka — boleh dikosongkan.";

        /// <summary>
        /// Siapkan pilihan wilayah dari data desa yang tersimpan. Dipanggil setiap
        /// kali halaman dibuka: data lama berupa teks bebas dicocokkan dengan data
        /// wilayah bawaan, dan pemuatannya dijalankan di thread latar (± 90 ribu
        /// baris) supaya jendela tidak tersendat.
        /// </summary>
        private async Task SiapkanPilihanWilayahAsync(DesaData snapshot)
        {
            // Penyusunan hasil (membaca ± 90 ribu baris data wilayah) berjalan di
            // utas latar supaya jendela tidak tersendat. Penerapannya ke halaman
            // SELALU di utas halaman — lihat JalankanDiUtasHalamanAsync.
            var hasil = await Task.Run(() =>
            {
                _ = _pemilih.Provinsi;   // memicu pemuatan berkas data wilayah
                return _pemilih.AdaData
                    ? _pemilih.MuatDariNama(snapshot.NamaDesa, snapshot.Kecamatan, snapshot.Kabupaten)
                    : new HasilPencocokanWilayah(StatusPencocokanWilayah.TanpaData, null, 0);
            }).ConfigureAwait(false);

            await JalankanDiUtasHalamanAsync(() => TerapkanHasilPencocokanWilayah(hasil, snapshot));
        }

        /// <summary>
        /// Pasang hasil pencocokan wilayah ke isian halaman: mode pilih/manual,
        /// catatan keadaan, pilihan berjenjang, dan lambang kabupaten. Seluruh
        /// perubahan keadaan yang diikat XAML ada di sini, jadi inilah satu-satunya
        /// tempat yang mengubah tampilan pilihan wilayah.
        /// </summary>
        private void TerapkanHasilPencocokanWilayah(HasilPencocokanWilayah hasil, DesaData snapshot)
        {
            // Bila pencocokan nama ambigu (mis. Kabupaten vs Kota Bandung) tetapi
            // database menyimpan kode Kemendagri kabupaten, gunakan kode tersebut
            // untuk memilih kabupaten yang benar secara deterministik. Cari di data
            // wilayahnya sendiri, BUKAN pada `_pemilih.Kabupaten`: hasil Ambigu
            // tidak pernah memilih apa pun, jadi daftar tingkat bawah _pemilih masih
            // kosong saat halaman pertama dibuka dan pencarian di sana selalu gagal.
            if (hasil.Status == StatusPencocokanWilayah.Ambigu &&
                !string.IsNullOrWhiteSpace(snapshot.KodeKabupaten))
            {
                var kab = _dataWilayah.Ambil(snapshot.KodeKabupaten.Trim());
                if (kab is { Jenis: WilayahJenis.Kabupaten or WilayahJenis.Kota } && _pemilih.PilihKabupaten(kab))
                {
                    // Pilihan kecamatan/desa ikut dicoba lagi dari nama tersimpan.
                    var kec = _pemilih.Kecamatan.FirstOrDefault(k =>
                        WilayahPencocokan.Normalisasi(k.Nama) ==
                        WilayahPencocokan.Normalisasi(snapshot.Kecamatan));
                    if (kec != null) _pemilih.PilihKecamatan(kec);
                    var desa = _pemilih.Desa.FirstOrDefault(d =>
                        WilayahPencocokan.Normalisasi(d.Nama) ==
                        WilayahPencocokan.Normalisasi(snapshot.NamaDesa));
                    if (desa != null) _pemilih.PilihDesa(desa);

                    hasil = new HasilPencocokanWilayah(
                        StatusPencocokanWilayah.Ditemukan, _pemilih.Rantai, 1);
                }
            }

            SegarkanDaftarWilayah();

            switch (hasil.Status)
            {
                case StatusPencocokanWilayah.Ditemukan:
                    ModePilihWilayah = true;

                    // Wilayah yang tersimpan dikenali: jenis kabupaten/kotanya ikut
                    // disiapkan supaya kop surat memakai kata yang benar. Data desa lama
                    // belum menyimpan kolom ini, jadi barulah tersimpan setelah pengguna
                    // menekan Simpan — karena itu keadaannya diberitahukan di sini.
                    bool jenisBaru = SinkronkanJenisKabupatenDariPilihan();
                    CatatanPilihanWilayah = jenisBaru
                        ? "Wilayah desa yang tersimpan dikenali dari data wilayah bawaan — pilihan dan penulisan kop " +
                          "sudah disiapkan. Tekan Simpan agar jenis wilayahnya (Kabupaten/Kota) ikut tersimpan."
                        : "Wilayah desa yang tersimpan dikenali dari data wilayah bawaan — pilihan sudah disiapkan.";
                    break;

                case StatusPencocokanWilayah.Ambigu:
                    // Nama kembar (mis. Kabupaten dan Kota Bandung) tidak boleh dipilih
                    // otomatis: salah pilih di sini ikut tercetak pada semua surat.
                    ModePilihWilayah = true;
                    CatatanPilihanWilayah =
                        $"Ada {hasil.JumlahKandidat} wilayah bernama sama, jadi pilihan belum bisa ditentukan otomatis. " +
                        "Pilih kabupaten/kota dan kecamatan yang benar dari daftar.";
                    break;

                case StatusPencocokanWilayah.TidakDitemukan:
                    bool masihContoh =
                        DesaContoh.NilaiContoh(snapshot.NamaDesa, DesaContoh.NamaDesa, wajib: true) ||
                        DesaContoh.NilaiContoh(snapshot.Kecamatan, DesaContoh.Kecamatan, wajib: true) ||
                        DesaContoh.NilaiContoh(snapshot.Kabupaten, DesaContoh.Kabupaten, wajib: true);

                    if (masihContoh)
                    {
                        ModePilihWilayah = true;
                        CatatanPilihanWilayah =
                            "Pilih kabupaten/kota di Jawa Barat, lalu kecamatan, lalu desa/kelurahan dari daftar — " +
                            "nama wilayah dan kode pos terisi sendiri. Untuk provinsi lain, ubah dulu pilihan Provinsi.";
                    }
                    else
                    {
                        // Data desa nyata tetapi tidak ada di data wilayah (mis. wilayah hasil
                        // pemekaran setelah data dirilis). Isian lama tidak boleh diubah diam-diam.
                        ModeIsiManual = true;
                        CatatanPilihanWilayah =
                            "Data desa yang tersimpan tidak ditemukan di data wilayah bawaan, jadi isian manual dipakai. " +
                            "Hilangkan centang «Isi manual» untuk mencari wilayahnya pada daftar.";
                    }

                    break;

                default:
                    ModeIsiManual = true;
                    break;
            }

            // Aplikasi ini dipakai kantor desa di Jawa Barat: bila belum ada pilihan
            // sama sekali, provinsi Jawa Barat sudah ditetapkan sehingga seluruh
            // kabupaten/kota Jawa Barat langsung tersedia pada kotak Kabupaten/Kota —
            // pengguna tidak perlu memilih provinsi lebih dulu. Provinsi lain tetap
            // bisa dipilih, dan pilihan itu tidak pernah menimpa pilihan yang ada.
            if (ModePilihWilayah && _pemilih.PilihProvinsiDiutamakan())
            {
                SegarkanDaftarWilayah();
            }

            // Sinkronkan kode logo kabupaten dari kode yang tersimpan di database.
            // Bila pengguna sebelumnya memakai lambang kabupaten dan kode tersimpan
            // valid (kabupaten terpilih), pastikan kodeLogoKabupaten preference ikut
            // konsisten — supaya JalurLogoEfektif() menemukan berkas yang benar.
            var kabTerpilih = _pemilih.KabupatenTerpilih;
            if (kabTerpilih != null &&
                !string.IsNullOrWhiteSpace(snapshot.KodeKabupaten))
            {
                PengaturanCetak.SetKodeLogoKabupaten(kabTerpilih.Kode);
            }

            // Daftar lambang kabupaten ikut disiapkan. Pilih-otomatis diaktifkan
            // saat halaman dibuka supaya pengguna di Jawa Barat langsung mendapat
            // lambang kabupatennya tanpa harus memilih manual — kecuali bila sudah
            // punya logo unggahan sendiri (dijaga oleh BolehPilihOtomatisLambang).
            SegarkanLambangKabupaten(pilihOtomatis: true);
        }

        /// <summary>Pindah antara memilih dari daftar dan mengisi wilayah manual.</summary>
        private void UbahModeWilayah(bool pakaiManual)
        {
            if (pakaiManual == ModeIsiManual)
            {
                return;
            }

            ModePilihWilayah = !pakaiManual;

            if (pakaiManual)
            {
                CatatanPilihanWilayah =
                    "Isian manual dipakai: nama desa, kecamatan, dan kabupaten diketik sendiri. " +
                    "Hilangkan centang ini untuk memilih dari daftar wilayah.";
                return;
            }

            if (!_pemilih.AdaData)
            {
                ModeIsiManual = true;
                return;
            }

            // Kembali ke daftar: kenali dulu isian yang sedang ada supaya pengguna
            // tidak kehilangan data hanya karena berpindah mode.
            var hasil = _pemilih.MuatDariNama(NamaDesa, Kecamatan, Kabupaten);

            // Bila isian tidak dikenali, daftar dibuka dari Jawa Barat supaya
            // kabupaten/kota Jawa Barat langsung bisa dipilih tanpa memilih provinsi dulu.
            _pemilih.PilihProvinsiDiutamakan();
            SinkronkanJenisKabupatenDariPilihan();
            // Jenis lembaga juga: wilayah yang dikenali bawaannya jelas
            // kelurahan atau desa — kop dan jabatan penandatangan mengikutinya.
            SinkronkanJenisLembagaDariPilihan();
            SegarkanDaftarWilayah();
            CatatanPilihanWilayah = hasil.Status switch
            {
                StatusPencocokanWilayah.Ditemukan =>
                    "Isian yang ada dikenali dari data wilayah — pilihan sudah disiapkan.",
                StatusPencocokanWilayah.Ambigu =>
                    $"Ada {hasil.JumlahKandidat} wilayah bernama sama, jadi pilihan belum ditentukan otomatis. " +
                    "Pilih kabupaten/kota dan kecamatan yang benar dari daftar.",
                _ =>
                    "Isian yang ada tidak cocok dengan data wilayah; pilih dari daftar untuk menggantinya, " +
                    "atau centang «Isi manual» untuk mempertahankannya."
            };
        }

        /// <summary>
        /// Salin pilihan berjenjang ke isian teks yang dipakai kop surat. Hanya
        /// dipanggil saat pengguna memilih dari daftar: tingkat yang belum dipilih
        /// dikosongkan supaya surat tidak memakai campuran wilayah yang tidak pernah ada.
        /// </summary>
        private void SinkronkanIsianDariPilihan()
        {
            Kabupaten = _pemilih.KabupatenTerpilih?.Nama ?? string.Empty;
            Kecamatan = _pemilih.KecamatanTerpilih?.Nama ?? string.Empty;
            NamaDesa = _pemilih.DesaTerpilih?.Nama ?? string.Empty;

            if (_pemilih.DesaTerpilih is { Berkodepos: true } desa)
            {
                Kodepos = desa.Kodepos;
            }

            // Jenis wilayah mengikuti kabupaten/kota yang dipilih, sehingga kop surat
            // mencetak “PEMERINTAH KOTA …” atau “PEMERINTAH KABUPATEN …” dengan benar.
            SinkronkanJenisKabupatenDariPilihan();
            // Jenis lembaga juga: wilayah yang dikenali bawaannya jelas
            // kelurahan atau desa — kop dan jabatan penandatangan mengikutinya.
            SinkronkanJenisLembagaDariPilihan();

            // Jenis lembaga ikut pilihan desa/kelurahan: Kepala Desa untuk Desa,
            // Lurah + NIP untuk Kelurahan.
            SinkronkanJenisLembagaDariPilihan();

            // Daftar lambang kabupaten ikut diperbarui. Pemilihan otomatisnya hanya
            // terjadi bila berkas lambangnya benar-benar ada — dan tidak pernah
            // mengambil alih logo desa yang sudah diunggah pengguna.
            SegarkanLambangKabupaten(pilihOtomatis: true);

            SegarkanDaftarWilayah();
        }

        /// <summary>
        /// Segarkan daftar pilihan dan seluruh keterangan turunannya. Daftar diganti
        /// sebagai satu kesatuan, bukan dikosongkan lalu diisi ulang.
        /// </summary>
        private void SegarkanDaftarWilayah()
        {
            if (_sedangSegarkanWilayah)
            {
                _segarkanWilayahDiulang = true;
                return;
            }

            _sedangSegarkanWilayah = true;
            try
            {
                // Permintaan yang datang di tengah penyegaran (dari pengikatan yang
                // menulis balik pilihan kosong) dijalankan pada putaran berikutnya,
                // bukan diabaikan: keadaan pilihan sudah berubah dan daftarnya harus
                // ikut menyesuaikan. Batasnya kecil karena keadaan ini mengendap cepat.
                int putaran = 0;
                do
                {
                    _segarkanWilayahDiulang = false;
                    TerapkanDaftarWilayah();
                }
                while (_segarkanWilayahDiulang && ++putaran < 4);
            }
            finally
            {
                _sedangSegarkanWilayah = false;
            }
        }

        /// <summary>Salin daftar pilihan terkini ke halaman beserta keterangannya.</summary>
        private void TerapkanDaftarWilayah()
        {
            DaftarProvinsi = _pemilih.Provinsi;
            DaftarKabupaten = _pemilih.Kabupaten;
            DaftarKecamatan = _pemilih.Kecamatan;
            DaftarDesa = _pemilih.Desa;

            OnPropertyChanged(nameof(WilayahTersedia));
            OnPropertyChanged(nameof(InfoDataWilayah));
            OnPropertyChanged(nameof(ProvinsiTerpilih));
            OnPropertyChanged(nameof(KabupatenTerpilih));
            OnPropertyChanged(nameof(KecamatanTerpilih));
            OnPropertyChanged(nameof(DesaTerpilih));
            OnPropertyChanged(nameof(RingkasanPilihanWilayah));
            OnPropertyChanged(nameof(AdaRingkasanPilihanWilayah));
            OnPropertyChanged(nameof(InfoKodepos));
        }

        /// <summary>
        /// Surel (email) kantor desa — <b>opsional</b>, boleh dikosongkan. Bila diisi,
        /// alamatnya ikut dicetak di baris alamat kop surat dengan warna biru.
        /// </summary>
        public string Email { get => _email; set { if (SetProperty(ref _email, value)) OnFieldChanged(); } }
        public string KepalaDesa { get => _kepalaDesa; set { if (SetProperty(ref _kepalaDesa, value)) OnFieldChanged(); } }
        public string Sekdes { get => _sekdes; set { if (SetProperty(ref _sekdes, value)) OnFieldChanged(); } }
        public string NamaCamat { get => _namaCamat; set { if (SetProperty(ref _namaCamat, value)) OnFieldChanged(); } }
        public string NipCamat { get => _nipCamat; set { if (SetProperty(ref _nipCamat, value)) OnFieldChanged(); } }
        public string GolCamat { get => _golCamat; set { if (SetProperty(ref _golCamat, value)) OnFieldChanged(); } }

        public bool IsLoading
        {
            get => _isLoading;
            private set { if (SetProperty(ref _isLoading, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            } }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            private set { if (SetProperty(ref _hasChanges, value)) SaveCommand.RaiseCanExecuteChanged(); }
        }

        private string _statusPesan = "Siap";

        /// <summary>Pesan singkat untuk statusbar bawah halaman (bukan dialog).</summary>
        public string StatusPesan
        {
            get => _statusPesan;
            private set => SetProperty(ref _statusPesan, value);
        }

        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand CancelCommand { get; }

        /// <summary>Nilai form sekarang, siap dipakai untuk pemeriksaan maupun disimpan.</summary>
        private DesaData DariForm() => new()
        {
            NamaDesa = NamaDesa?.Trim(),
            Kecamatan = Kecamatan?.Trim(),
            Kabupaten = Kabupaten?.Trim(),
            JenisKabupaten = _jenisKabupaten,
            KodeKabupaten = _pemilih.KabupatenTerpilih?.Kode,
            JenisLembaga = LembagaKelurahan ? _konstLembagaKelurahan : _konstLembagaDesa,
            Alamat = Alamat?.Trim(),
            Kodepos = Kodepos?.Trim(),
            Email = Email?.Trim(),
            KepalaDesa = KepalaDesa?.Trim(),
            SekretarisDesa = Sekdes?.Trim(),
            NamaCamat = NamaCamat?.Trim(),
            NipCamat = NipCamat?.Trim(),
            GolCamat = GolCamat?.Trim(),
            // NIP pejabat (Lurah); dikosongkan untuk Desa karena Kepala Desa non-ASN.
            NipKepalaDesa = LembagaKelurahan ? NipKepalaDesa?.Trim() : string.Empty
        };

        // =================================================================
        // Pratinjau kop surat di layar (mengikuti isian, belum perlu disimpan)
        // =================================================================

        /// <summary>
        /// Lebar "kertas" pratinjau di layar (px). Dipakai sebagai acuan skala: ukuran
        /// huruf kop di PDF (point) dikalikan lebar ini dibagi lebar halaman, sehingga
        /// perbandingan besar-kecil huruf pada kop tetap tergambar apa adanya.
        /// </summary>
        public const double LebarKertasPratinjau = 600.0;

        private bool _pratinjauTerbuka = true;
        private string _pratinjauJalurLogo = string.Empty;
        private bool _pratinjauAdaLogo;
        private double _pratinjauLebarLogo = KopSurat.LebarLogo;
        private string _pratinjauInfoKertas = string.Empty;
        private string _pratinjauInfoCamat = string.Empty;
        private string _pratinjauInfoSurel = string.Empty;
        private string _pratinjauCatatanBersih = string.Empty;
        private string _pratinjauStatus = string.Empty;
        private bool _pratinjauSesuaiTersimpan = true;
        private int _pratinjauKolomPejabat = 2;

        /// <summary>Baris kop pada pratinjau, sudah diskalakan untuk layar.</summary>
        public ObservableCollection<PratinjauKopBarisViewModel> PratinjauKop { get; } = new();

        /// <summary>Blok tanda tangan pejabat pada pratinjau (Kepala Desa, Sekretaris Desa, Camat).</summary>
        public ObservableCollection<PratinjauPejabatViewModel> PratinjauPejabat { get; } = new();

        /// <summary>Jumlah kolom blok pejabat: dua, atau tiga bila Pejabat Kecamatan diisi.</summary>
        public int PratinjauKolomPejabat
        {
            get => _pratinjauKolomPejabat;
            private set => SetProperty(ref _pratinjauKolomPejabat, value);
        }

        /// <summary>Lebar kertas pratinjau (px) — acuan skala yang sama dengan perhitungan huruf.</summary>
        public double PratinjauLebarKertas => LebarKertasPratinjau;

        /// <summary>Gambar logo yang dipakai kop (kosong bila tidak ada gambar sama sekali).</summary>
        public string PratinjauJalurLogo
        {
            get => _pratinjauJalurLogo;
            private set => SetProperty(ref _pratinjauJalurLogo, value ?? string.Empty);
        }

        public bool PratinjauAdaLogo
        {
            get => _pratinjauAdaLogo;
            private set => SetProperty(ref _pratinjauAdaLogo, value);
        }

        /// <summary>
        /// Lebar kotak logo pada pratinjau (px). Kolomnya tetap disediakan walau tanpa
        /// gambar, sama seperti PDF yang selalu menyediakan kotak logo selebar 60 point.
        /// </summary>
        public double PratinjauLebarLogo
        {
            get => _pratinjauLebarLogo;
            private set => SetProperty(ref _pratinjauLebarLogo, value);
        }

        /// <summary>Keterangan surel kop: dipakai/dikosongkan, dan warnanya saat dicetak.</summary>
        public string PratinjauInfoSurel
        {
            get => _pratinjauInfoSurel;
            private set => SetProperty(ref _pratinjauInfoSurel, value ?? string.Empty);
        }

        /// <summary>Keterangan ukuran kertas dan asal logo untuk pratinjau.</summary>
        public string PratinjauInfoKertas
        {
            get => _pratinjauInfoKertas;
            private set => SetProperty(ref _pratinjauInfoKertas, value ?? string.Empty);
        }

        /// <summary>Keterangan blok camat: dipakai pada surat, atau dikosongkan.</summary>
        public string PratinjauInfoCamat
        {
            get => _pratinjauInfoCamat;
            private set => SetProperty(ref _pratinjauInfoCamat, value ?? string.Empty);
        }

        /// <summary>Catatan bila kata pengulang ("Desa", "Kecamatan") otomatis dibuang dari kop.</summary>
        public string PratinjauCatatanBersih
        {
            get => _pratinjauCatatanBersih;
            private set
            {
                if (SetProperty(ref _pratinjauCatatanBersih, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaPratinjauCatatanBersih));
                }
            }
        }

        public bool AdaPratinjauCatatanBersih => _pratinjauCatatanBersih.Length > 0;

        /// <summary>Keadaan pratinjau: sudah sesuai data tersimpan atau masih isian di layar.</summary>
        public string PratinjauStatus
        {
            get => _pratinjauStatus;
            private set => SetProperty(ref _pratinjauStatus, value ?? string.Empty);
        }

        /// <summary>True bila isian sama dengan data tersimpan (lencana pratinjau berwarna aman).</summary>
        public bool PratinjauSesuaiTersimpan
        {
            get => _pratinjauSesuaiTersimpan;
            private set => SetProperty(ref _pratinjauSesuaiTersimpan, value);
        }

        /// <summary>Pratinjau bisa disembunyikan agar isian tetap lega di layar pendek.</summary>
        public bool PratinjauTerbuka
        {
            get => _pratinjauTerbuka;
            private set
            {
                if (SetProperty(ref _pratinjauTerbuka, value))
                {
                    OnPropertyChanged(nameof(PratinjauToggleTeks));
                }
            }
        }

        public string PratinjauToggleTeks => _pratinjauTerbuka ? "Sembunyikan pratinjau" : "Tampilkan pratinjau";

        public RelayCommand PratinjauToggleCommand { get; }

        private void TogglePratinjau() => PratinjauTerbuka = !PratinjauTerbuka;

        /// <summary>
        /// Susun ulang seluruh isi pratinjau dari isian yang sedang di layar. Dipanggil
        /// setiap kali isian berubah supaya pengguna melihat akibatnya seketika —
        /// sebelum data desa disimpan.
        /// </summary>
        public void PerbaruiPratinjau()
        {
            var desa = DariForm();
            var kop = PratinjauKopSurat.Susun(desa);

            var (lebarHalaman, _) = PengaturanCetak.Dimensi(_ukuranKertas);
            double skala = LebarKertasPratinjau / System.Math.Max(1.0, lebarHalaman);

            PratinjauKop.Clear();
            foreach (var baris in kop.Baris)
            {
                PratinjauKop.Add(new PratinjauKopBarisViewModel(
                    baris.Teks,
                    System.Math.Round(baris.FontSize * skala, 1),
                    baris.Tebal,
                    baris.Surel));
            }

            PratinjauPejabat.Clear();
            PratinjauPejabat.Add(BlokPejabat(kop.KepalaDesa));
            PratinjauPejabat.Add(BlokPejabat(kop.SekretarisDesa));
            if (kop.Camat != null)
            {
                PratinjauPejabat.Add(BlokPejabat(kop.Camat));
            }

            PratinjauKolomPejabat = kop.Camat == null ? 2 : 3;
            PratinjauLebarLogo = System.Math.Round(kop.LebarLogo * skala, 1);

            string? logo = PengaturanCetak.JalurLogoEfektif();
            PratinjauJalurLogo = logo ?? string.Empty;
            PratinjauAdaLogo = logo != null;

            string asalLogo = logo == null
                ? "tanpa gambar logo"
                : string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo())
                    ? "logo bawaan aplikasi"
                    : $"logo Anda ({System.IO.Path.GetFileName(logo)})";
            PratinjauInfoKertas = $"Kertas {PengaturanCetak.LabelUkuranKertas(_ukuranKertas)} · {asalLogo}";

            PratinjauInfoCamat = kop.Camat == null
                ? "Pejabat Kecamatan dikosongkan — blok camat tidak dicetak pada surat."
                : "Pejabat Kecamatan terisi — blok camat ikut dicetak pada surat yang memintanya.";

            // Surel bersifat opsional: keterangannya menjelaskan kedua keadaan, bukan
            // menyuruh pengguna mengisinya.
            PratinjauInfoSurel = kop.Surel.Length == 0
                ? "Email desa belum diisi — kop dicetak tanpa email (kolom ini opsional)."
                : $"Email {kop.Surel} dicetak berwarna biru pada baris sendiri di bawah nama kabupaten; " +
                  "kosongkan kolomnya bila kantor desa tidak memakai email surat.";

            PratinjauCatatanBersih = CatatanBersih(desa);
            PratinjauSesuaiTersimpan = !HasChanges;
            PratinjauStatus = HasChanges
                ? "Masih isian di layar — belum disimpan"
                : "Sesuai data yang sudah disimpan";
        }

        private static PratinjauPejabatViewModel BlokPejabat(BlokPejabatPratinjau blok)
            => new(blok.Judul, blok.Jabatan, blok.Nama, string.Join("\n", blok.BarisTambahan), blok.Terisi);

        /// <summary>
        /// Catatan bila pengguna mengetik kata pengulang di depan nama wilayah
        /// (mis. "Kecamatan Tempuran"): kop tetap dicetak sekali saja. Pemeriksaannya
        /// memakai aturan pembersih yang sama dengan generator PDF.
        /// </summary>
        private static string CatatanBersih(DesaData desa)
        {
            var kolom = new List<string>();
            if (AdaKataPengulang(desa.NamaDesa, "Desa")) kolom.Add("Nama Desa");
            if (AdaKataPengulang(desa.Kecamatan, "Kecamatan", "Kec.")) kolom.Add("Kecamatan");
            if (AdaKataPengulang(desa.Kabupaten, "Kabupaten", "Kab.")) kolom.Add("Kabupaten");

            return kolom.Count == 0
                ? string.Empty
                : $"Kata pengulang pada {string.Join(", ", kolom)} otomatis dibuang di kop dan tanda tangan " +
                  "(mis. kolom Kecamatan tetap dicetak sebagai “KECAMATAN ...”, tanpa kata ganda).";
        }

        private static bool AdaKataPengulang(string? nilai, string kataPengulang, string? singkatan = null)
        {
            string asli = (nilai ?? string.Empty).Trim();
            string bersih = KopSurat.NamaWilayah(asli, kataPengulang, singkatan);
            return bersih.Length > 0 && !string.Equals(bersih, asli, StringComparison.Ordinal);
        }

        // =================================================================
        // Peringatan data contoh
        // =================================================================

        private string _pesanPeringatanContoh = string.Empty;

        /// <summary>Benar bila masih ada kolom wajib yang berisi contoh bawaan aplikasi.</summary>
        public bool AdaPeringatanContoh => _pesanPeringatanContoh.Length > 0;

        /// <summary>Pesan yang meminta pengguna mengganti data contoh dengan data desanya.</summary>
        public string PesanPeringatanContoh
        {
            get => _pesanPeringatanContoh;
            private set
            {
                if (SetProperty(ref _pesanPeringatanContoh, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaPeringatanContoh));
                }
            }
        }

        /// <summary>Keterangan Pejabat Kecamatan: sengaja tidak diisi contoh dan boleh dikosongkan.</summary>
        public string InfoPejabatKecamatan =>
            "Kolom Pejabat Kecamatan boleh dikosongkan — tidak semua surat membutuhkannya, dan " +
            "aplikasi tidak mengisinya dengan data contoh. Isi hanya bila kantor desa memakai nama camat " +
            "pada surat (mis. surat yang disetujui/diketahui camat).";

        /// <summary>
        /// Keterangan kolom email: opsional, dan bila diisi alamatnya dicetak biru pada
        /// baris alamat kop surat.
        /// </summary>
        public string InfoEmail =>
            "Opsional — boleh dikosongkan. Bila diisi, email dicetak pada baris alamat kop surat " +
            "dengan warna biru (mis. desa@sumberjaya.id).";

        /// <summary>Benar bila salah satu data Pejabat Kecamatan sudah diisi pengguna.</summary>
        public bool AdaPejabatKecamatan =>
            !string.IsNullOrWhiteSpace(_namaCamat) ||
            !string.IsNullOrWhiteSpace(_nipCamat) ||
            !string.IsNullOrWhiteSpace(_golCamat);

        /// <summary>
        /// Perbarui peringatan data contoh dan penanda tiap bagian pada navigasi.
        /// Dipanggil setiap kali isian berubah supaya pengguna langsung melihat
        /// mana yang sudah beres dan mana yang masih contoh.
        /// </summary>
        private void PerbaruiPeringatanContoh()
        {
            var fieldContoh = DesaContoh.FieldContoh(DariForm());

            PesanPeringatanContoh = fieldContoh.Count == 0
                ? string.Empty
                : "Data desa masih memakai contoh bawaan aplikasi: " + string.Join(", ", fieldContoh) +
                  ". Ganti dengan data desa Anda yang sebenarnya supaya surat yang dicetak tidak memakai " +
                  "data contoh.";

            PerbaruiLencanaBagian(fieldContoh);
            OnPropertyChanged(nameof(AdaPejabatKecamatan));

            // Pratinjau ikut isian terbaru: nama desa, pejabat, dan logo terlihat
            // seketika di layar, belum perlu disimpan dulu.
            PerbaruiPratinjau();
        }

        /// <summary>Ringkasan keadaan tiap bagian pada navigasi kiri.</summary>
        private void PerbaruiLencanaBagian(System.Collections.Generic.List<string> fieldContoh)
        {
            if (Bagian.Count < 4) return;

            var kelompokDesa = new[] { "Nama Desa", "Kecamatan", "Kabupaten", "Alamat Desa" };
            var kelompokPejabat = new[] { "Kepala Desa", "Sekretaris Desa" };

            int desa = fieldContoh.Count(f => kelompokDesa.Contains(f));
            int pejabat = fieldContoh.Count(f => kelompokPejabat.Contains(f));

            Lencana(Bagian[0], desa, "Kolom masih contoh", "Lengkap");
            Lencana(Bagian[1], pejabat, "Kolom masih contoh", "Lengkap");

            Bagian[2].Lencana = AdaPejabatKecamatan ? "Terisi (opsional)" : "Dikosongkan";
            Bagian[2].LencanaPeringatan = false;

            string kertas = _ukuranKertas == UkuranKertasSurat.F4 ? "Kertas F4 / Folio" : "Kertas A4";
            string logo = !AdaLogo
                ? "tanpa logo"
                : string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo()) ? "logo bawaan" : "logo Anda";
            Bagian[3].Lencana = $"{kertas} · {logo}";
            Bagian[3].LencanaPeringatan = false;
        }

        private static void Lencana(
            BagianSetelanItemViewModel bagian, int jumlahContoh, string pesanContoh, string pesanLengkap)
        {
            if (jumlahContoh > 0)
            {
                bagian.Lencana = $"{jumlahContoh} {pesanContoh}";
                bagian.LencanaPeringatan = true;
            }
            else
            {
                bagian.Lencana = pesanLengkap;
                bagian.LencanaPeringatan = false;
            }
        }

        /// <summary>Hitung ulang HasChanges dengan membandingkan nilai form terhadap snapshot tersimpan.</summary>
        private void OnFieldChanged()
        {
            HasChanges = HasDataChanged();
            PerbaruiPeringatanContoh();
        }

        /// <summary>Padanan HasDataChanged WinForms: bandingkan (trim + ignore case) terhadap snapshot.</summary>
        private bool HasDataChanged()
        {
            return !string.Equals(_namaDesa?.Trim(), _snapshot.NamaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kecamatan?.Trim(), _snapshot.Kecamatan, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kabupaten?.Trim(), _snapshot.Kabupaten, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_jenisKabupaten, _snapshot.JenisKabupaten, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_alamat?.Trim(), _snapshot.Alamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kodepos?.Trim(), _snapshot.Kodepos, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_email?.Trim(), _snapshot.Email, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kepalaDesa?.Trim(), _snapshot.KepalaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_sekdes?.Trim(), _snapshot.SekretarisDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_namaCamat?.Trim(), _snapshot.NamaCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipCamat?.Trim(), _snapshot.NipCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_golCamat?.Trim(), _snapshot.GolCamat, StringComparison.OrdinalIgnoreCase) ||
                   // Jenis lembaga dan NIP pejabat ikut mengikatan tombol Simpan.
                   !string.Equals(_jenisLembaga, _snapshot.JenisLembaga, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipKepalaDesa?.Trim(), _snapshot.NipKepalaDesa, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadAsync()
        {
            try
            {
                IsLoading = true;

                // Baca database di utas pemanggil (tidak menahan UI lebih dari
                // sekali), lalu SELURUH penerapan ke halaman dijalankan di utas
                // halaman — isian, tombol Simpan, dan pratinjau adalah keadaan
                // milik halaman itu.
                var settings = await _settingsManager.GetSettingsAsync();

                // Nomor surat yang diatur per wilayah mengikuti kode kabupaten/kota
                // yang benar-benar tersimpan pada data desa. Nama yang diketik manual
                // tidak cukup untuk memilih aturan hukum secara deterministik.
                SuDesApp.Configuration.PenomoranOverrideStore.GunakanDaerah(settings?.KodeKabupaten);
                if (!string.IsNullOrWhiteSpace(settings?.KodeKabupaten) && _jenisSuratConfig != null)
                    await _jenisSuratConfig.RefreshConfigAsync();

                await JalankanDiUtasHalamanAsync(async () =>
                {
                    TerapkanDataDesa(settings ?? new DesaData());

                    // Kenali wilayah desa yang tersimpan dari data wilayah bawaan supaya
                    // kunjungan berikutnya cukup memilih dari daftar.
                    await SiapkanPilihanWilayahAsync(_snapshot);

                    // Bagian yang masih berisi contoh langsung dibuka supaya pengguna
                    // tahu apa yang harus diganti.
                    if (DesaContoh.MasihContoh(_snapshot))
                    {
                        var fieldContoh = DesaContoh.FieldContoh(_snapshot);
                        bool pejabatDulu = fieldContoh.Contains("Kepala Desa") || fieldContoh.Contains("Sekretaris Desa");
                        bool desaMasihKosong = fieldContoh.Exists(f =>
                            f is "Nama Desa" or "Kecamatan" or "Kabupaten" or "Alamat Desa");
                        TampilkanBagian(desaMasihKosong || !pejabatDulu ? 0 : 1);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal memuat pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Pasang data desa tersimpan ke seluruh isian halaman beserta lencana dan
        /// pratinjaunya. Dipanggil hanya dari utas halaman (lihat
        /// <see cref="JalankanDiUtasHalamanAsync(Action)"/>).
        /// </summary>
        private void TerapkanDataDesa(DesaData data)
        {
            _snapshot = data;

            _namaDesa = _snapshot.NamaDesa ?? string.Empty;
            _kecamatan = _snapshot.Kecamatan ?? string.Empty;
            _kabupaten = _snapshot.Kabupaten ?? string.Empty;
            _jenisKabupaten = _snapshot.JenisKabupaten ?? string.Empty;
            _alamat = _snapshot.Alamat ?? string.Empty;
            _kodepos = _snapshot.Kodepos ?? string.Empty;
            _email = _snapshot.Email ?? string.Empty;
            _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
            _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
            _namaCamat = _snapshot.NamaCamat ?? string.Empty;
            _nipCamat = _snapshot.NipCamat ?? string.Empty;
            _golCamat = _snapshot.GolCamat ?? string.Empty;
            _nipKepalaDesa = _snapshot.NipKepalaDesa ?? string.Empty;

            OnPropertyChanged(nameof(NamaDesa));
            OnPropertyChanged(nameof(Kecamatan));
            OnPropertyChanged(nameof(Kabupaten));
            SegarkanJenisKabupaten();
            SegarkanJenisLembaga();
            OnPropertyChanged(nameof(NipKepalaDesa));
            OnPropertyChanged(nameof(Alamat));
            OnPropertyChanged(nameof(Kodepos));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(KepalaDesa));
            OnPropertyChanged(nameof(Sekdes));
            OnPropertyChanged(nameof(NipKepalaDesa));
            OnPropertyChanged(nameof(NamaCamat));
            OnPropertyChanged(nameof(NipCamat));
            OnPropertyChanged(nameof(GolCamat));
            MuatPengaturanCetak();
            HasChanges = false;
            PerbaruiPeringatanContoh();
        }

        /// <summary>
        /// Batal: buang seluruh perubahan yang belum disimpan, kembalikan form
        /// ke nilai tersimpan terakhir (snapshot) — tanpa menyentuh database —
        /// lalu tutup halaman kembali ke tampilan default.
        /// </summary>
        private async Task CancelAsync()
        {
            try
            {
                IsLoading = true;

                _namaDesa = _snapshot.NamaDesa ?? string.Empty;
                _kecamatan = _snapshot.Kecamatan ?? string.Empty;
                _kabupaten = _snapshot.Kabupaten ?? string.Empty;
                _jenisKabupaten = _snapshot.JenisKabupaten ?? string.Empty;
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _email = _snapshot.Email ?? string.Empty;
            _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
            _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
            _namaCamat = _snapshot.NamaCamat ?? string.Empty;
            _nipCamat = _snapshot.NipCamat ?? string.Empty;
            _golCamat = _snapshot.GolCamat ?? string.Empty;
            _nipKepalaDesa = _snapshot.NipKepalaDesa ?? string.Empty;

            OnPropertyChanged(nameof(NamaDesa));
            OnPropertyChanged(nameof(Kecamatan));
            OnPropertyChanged(nameof(Kabupaten));
            SegarkanJenisKabupaten();
            SegarkanJenisLembaga();
            OnPropertyChanged(nameof(NipKepalaDesa));
                await SiapkanPilihanWilayahAsync(_snapshot);

                // Halaman ditutup dari utas halaman, sama seperti semua perubahan
                // keadaan lain di kelas ini.
                await JalankanDiUtasHalamanAsync(() => _navigation.ShowDefault());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membatalkan perubahan pengaturan desa");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveAsync()
        {
            if (!ValidateRequiredFields())
            {
                return;
            }

            try
            {
                IsLoading = true;
                var desaData = DariForm();
                await _settingsManager.SaveSettingsAsync(desaData);
                var kodeDaerahSebelum = _snapshot.KodeKabupaten;
                SuDesApp.Configuration.PenomoranOverrideStore.GunakanDaerah(desaData.KodeKabupaten);
                if (!string.Equals(kodeDaerahSebelum, desaData.KodeKabupaten, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(desaData.KodeKabupaten) && _jenisSuratConfig != null)
                    await _jenisSuratConfig.RefreshConfigAsync();

                // Penerapan setelah simpan mengubah lencana, pratinjau, dan statusbar:
                // keadaan halaman, jadi dijalankan di utas halaman.
                await JalankanDiUtasHalamanAsync(() =>
                {
                    _snapshot = desaData;
                    HasChanges = false;
                    PerbaruiPeringatanContoh();
                    SettingsSaved?.Invoke();
                    _logger.LogInformation("Pengaturan desa berhasil disimpan");

                    // Konfirmasi cukup lewat statusbar halaman ini — dialog yang muncul
                    // setiap kali Simpan terlalu mengganggu alur kerja.
                    StatusPesan = $"Pengaturan desa tersimpan — {DateTime.Now:HH:mm:ss}";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal menyimpan pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Padanan ValidateRequiredFields WinForms: field wajib terisi, kodepos 5
        /// digit (opsional), NIP camat minimal 9 digit (opsional). Mengembalikan
        /// true bila valid; bila tidak, menampilkan peringatan berisi daftar kesalahan.
        /// </summary>
        private bool ValidateRequiredFields()
        {
            var errorMessages = new List<string>();

            ValidateField(NamaDesa, "Nama Desa", errorMessages);
            ValidateField(Alamat, "Alamat Desa", errorMessages);
            ValidateField(Kecamatan, "Kecamatan", errorMessages);
            ValidateField(Kabupaten, "Kabupaten", errorMessages);
            ValidateField(KepalaDesa, "Kepala Desa", errorMessages);
            ValidateField(Sekdes, "Sekretaris Desa", errorMessages);

            if (!string.IsNullOrWhiteSpace(Kodepos) && !_kodeposRegex.IsMatch(Kodepos.Trim()))
            {
                errorMessages.Add("Kodepos harus terdiri dari 5 digit angka");
            }

            // Surel opsional: hanya diperiksa bila kolomnya memang diisi, supaya
            // kekeliruan ketik tidak terlanjur tercetak di kop semua surat.
            if (!string.IsNullOrWhiteSpace(Email) && !_emailRegex.IsMatch(Email.Trim()))
            {
                errorMessages.Add("Email desa tidak valid — contoh yang benar: desa@sumberjaya.id");
            }

            if (!string.IsNullOrWhiteSpace(NipCamat) && NipCamat.Trim().Length < 9)
            {
                errorMessages.Add("NIP Camat minimal 9 digit");
            }

            // NIP pejabat desa/kelurahan: wajib cukup panjang HANYA bila diisi, dan
            // bermanfaat saat lembaganya kelurahan (Lurah, ASN) — NIP yang tercetak.
            if (!string.IsNullOrWhiteSpace(NipKepalaDesa) && NipKepalaDesa.Trim().Length < 9)
            {
                errorMessages.Add("NIP pejabat minimal 9 digit");
            }

            if (errorMessages.Count > 0)
            {
                var errorMessage = "Perbaiki data berikut:\n\u2022 " + string.Join("\n\u2022 ", errorMessages);
                _ = _messageService.ShowWarningAsync(errorMessage);
                return false;
            }

            return true;
        }

        private static void ValidateField(string? fieldValue, string fieldName, List<string> errorMessages)
        {
            if (!Validator.ValidateRequired(fieldValue!, fieldName, out var msg))
            {
                errorMessages.Add(msg);
            }
        }
    }
}
