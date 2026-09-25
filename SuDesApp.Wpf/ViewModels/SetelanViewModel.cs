using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
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
        private static readonly Regex KodeposRegex = new(@"^\d{5}$", RegexOptions.Compiled);

        /// <summary>
        /// Pemeriksaan ringan untuk email desa (opsional): harus memuat satu "@" dengan
        /// teks di kedua sisinya. Sengaja tidak ketat supaya alamat kantor desa yang
        /// tidak lazim tetap bisa disimpan.
        /// </summary>
        private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+$", RegexOptions.Compiled);

        /// <summary>Dipicu setelah pengaturan berhasil disimpan (mis. refresh status bar utama).</summary>
        public static event Action? SettingsSaved;

        private readonly SettingsManager _settingsManager;
        private readonly IMessageService _messageService;
        private readonly ILogger<SetelanViewModel> _logger;
        private readonly NavigationService _navigation;

        /// <summary>Nilai tersimpan terakhir — pembanding untuk HasChanges (paritas HasDataChanged WinForms).</summary>
        private DesaData _snapshot = new();

        private string _namaDesa = string.Empty;
        private string _kecamatan = string.Empty;
        private string _kabupaten = string.Empty;
        private string _alamat = string.Empty;
        private string _kodepos = string.Empty;
        private string _email = string.Empty;
        private string _kepalaDesa = string.Empty;
        private string _sekdes = string.Empty;
        private string _namaCamat = string.Empty;
        private string _nipCamat = string.Empty;
        private string _golCamat = string.Empty;
        private bool _isLoading;
        private bool _hasChanges;

        public SetelanViewModel(SettingsManager settingsManager, IMessageService messageService, ILogger<SetelanViewModel> logger, NavigationService navigation)
        {
            _settingsManager = settingsManager;
            _messageService = messageService;
            _logger = logger;
            _navigation = navigation;
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasChanges && !IsLoading);
            CancelCommand = new AsyncRelayCommand(CancelAsync);
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
        public string Alamat { get => _alamat; set { if (SetProperty(ref _alamat, value)) OnFieldChanged(); } }
        public string Kodepos { get => _kodepos; set { if (SetProperty(ref _kodepos, value)) OnFieldChanged(); } }

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
            private set { if (SetProperty(ref _isLoading, value)) SaveCommand.RaiseCanExecuteChanged(); }
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
            Alamat = Alamat?.Trim(),
            Kodepos = Kodepos?.Trim(),
            Email = Email?.Trim(),
            KepalaDesa = KepalaDesa?.Trim(),
            SekretarisDesa = Sekdes?.Trim(),
            NamaCamat = NamaCamat?.Trim(),
            NipCamat = NipCamat?.Trim(),
            GolCamat = GolCamat?.Trim()
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
                   !string.Equals(_alamat?.Trim(), _snapshot.Alamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kodepos?.Trim(), _snapshot.Kodepos, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_email?.Trim(), _snapshot.Email, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kepalaDesa?.Trim(), _snapshot.KepalaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_sekdes?.Trim(), _snapshot.SekretarisDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_namaCamat?.Trim(), _snapshot.NamaCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipCamat?.Trim(), _snapshot.NipCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_golCamat?.Trim(), _snapshot.GolCamat, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                var settings = await _settingsManager.GetSettingsAsync();
                _snapshot = settings ?? new DesaData();

                _namaDesa = _snapshot.NamaDesa ?? string.Empty;
                _kecamatan = _snapshot.Kecamatan ?? string.Empty;
                _kabupaten = _snapshot.Kabupaten ?? string.Empty;
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _email = _snapshot.Email ?? string.Empty;
                _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
                _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
                _namaCamat = _snapshot.NamaCamat ?? string.Empty;
                _nipCamat = _snapshot.NipCamat ?? string.Empty;
                _golCamat = _snapshot.GolCamat ?? string.Empty;

                OnPropertyChanged(nameof(NamaDesa));
                OnPropertyChanged(nameof(Kecamatan));
                OnPropertyChanged(nameof(Kabupaten));
                OnPropertyChanged(nameof(Alamat));
                OnPropertyChanged(nameof(Kodepos));
                OnPropertyChanged(nameof(Email));
                OnPropertyChanged(nameof(KepalaDesa));
                OnPropertyChanged(nameof(Sekdes));
                OnPropertyChanged(nameof(NamaCamat));
                OnPropertyChanged(nameof(NipCamat));
                OnPropertyChanged(nameof(GolCamat));
                MuatPengaturanCetak();
                HasChanges = false;
                PerbaruiPeringatanContoh();

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
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _email = _snapshot.Email ?? string.Empty;
                _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
                _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
                _namaCamat = _snapshot.NamaCamat ?? string.Empty;
                _nipCamat = _snapshot.NipCamat ?? string.Empty;
                _golCamat = _snapshot.GolCamat ?? string.Empty;

                OnPropertyChanged(nameof(NamaDesa));
                OnPropertyChanged(nameof(Kecamatan));
                OnPropertyChanged(nameof(Kabupaten));
                OnPropertyChanged(nameof(Alamat));
                OnPropertyChanged(nameof(Kodepos));
                OnPropertyChanged(nameof(Email));
                OnPropertyChanged(nameof(KepalaDesa));
                OnPropertyChanged(nameof(Sekdes));
                OnPropertyChanged(nameof(NamaCamat));
                OnPropertyChanged(nameof(NipCamat));
                OnPropertyChanged(nameof(GolCamat));
                HasChanges = false;
                PerbaruiPeringatanContoh();
                _navigation.ShowDefault();
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
                _snapshot = desaData;
                HasChanges = false;
                PerbaruiPeringatanContoh();
                SettingsSaved?.Invoke();
                _logger.LogInformation("Pengaturan desa berhasil disimpan");

                // Konfirmasi cukup lewat statusbar halaman ini — dialog yang muncul
                // setiap kali Simpan terlalu mengganggu alur kerja.
                StatusPesan = $"Pengaturan desa tersimpan — {DateTime.Now:HH:mm:ss}";
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

            if (!string.IsNullOrWhiteSpace(Kodepos) && !KodeposRegex.IsMatch(Kodepos.Trim()))
            {
                errorMessages.Add("Kodepos harus terdiri dari 5 digit angka");
            }

            // Surel opsional: hanya diperiksa bila kolomnya memang diisi, supaya
            // kekeliruan ketik tidak terlanjur tercetak di kop semua surat.
            if (!string.IsNullOrWhiteSpace(Email) && !EmailRegex.IsMatch(Email.Trim()))
            {
                errorMessages.Add("Email desa tidak valid — contoh yang benar: desa@sumberjaya.id");
            }

            if (!string.IsNullOrWhiteSpace(NipCamat) && NipCamat.Trim().Length < 9)
            {
                errorMessages.Add("NIP Camat minimal 9 digit");
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
