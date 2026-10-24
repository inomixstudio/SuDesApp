using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman Data Perangkat Desa: daftar tetap jabatan perangkat desa dari
    /// Kepala Desa sampai Kadus — tiap jabatan selalu punya baris, dan
    /// tabelnya hanya tampilan: isian data diri dilakukan lewat form Tambah
    /// Perangkat. Jabatan yang boleh dipegang lebih dari satu orang (KADUS
    /// per dusun) tampil sebagai satu baris per orang dengan kolom wilayahnya.
    /// Halaman ini juga memuat bagian RT/RW serta kelompok lembaga lain — BPD,
    /// LINMAS, Posyandu, Kader KPM, Kader Stunting, Staf Desa, Operator Desa,
    /// PKK, LPM, dan Karang Taruna — masing-masing dipilih lewat tombol di
    /// bilah alat dan memakai tabel, form, maupun panel SK yang sama. Tersedia
    /// pula ekspor Excel dan cetak PDF
    /// (pratinjau PDFium); pencarian, filter jabatan/wilayah, dan tombol Muat
    /// Ulang sengaja tidak dipakai — saringan status berlaku langsung dari
    /// salinan data yang dibaca sekali saat halaman dibuka.
    ///
    /// Setiap baris — termasuk jabatan yang belum ada orangnya — punya tombol
    /// SK di kolom AKSI yang membuka panel Surat Keputusan; panel itu juga
    /// menjadi tempat mengarsipkan berkas SK Bupati untuk Kepala Desa.
    ///
    /// Di bawah tabel perangkat desa ada bagian <b>RT/RW</b> (Ketua RT dan
    /// Ketua RW) dengan tabel, tombol, dan form isian yang sama persis, sebab
    /// keduanya memang satu kelompok pekerjaan pengisian data warga yang
    /// ditetapkan lewat SK Kepala Desa.
    ///
    /// Dua aturan yang dijaga halaman ini: satu jabatan pada satu wilayah
    /// hanya boleh satu orang (validasi ada di <c>PerangkatDesaService</c>),
    /// dan jabatan yang belum punya orang tetap tampil sebagai baris kosong
    /// penanda. Ringkasan keterisian dibaca dari pesan status di atas tabel —
    /// halaman ini sengaja tidak memakai kartu KPI.
    /// </summary>
    /// <summary>Bagian halaman Perangkat Desa yang sedang ditampilkan.</summary>
    public enum BagianPerangkat
    {
        PerangkatDesa,
        RtRw,
        Bpd,
        Linmas,
        Posyandu,
        KaderKpm,
        KaderStunting,
        StafDesa,
        OperatorDesa,
        Pkk,
        Lpm,
        KarangTaruna
    }

    public class PerangkatDesaViewModel : ObservableObject
    {
        private readonly IPerangkatDesaService _layanan;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepo;
        private readonly AppConfig _appConfig;
        private readonly StrukturPerangkatGenerator _strukturGenerator;
        private readonly FormulirPerangkatGenerator _formulirGenerator;
        private readonly SkPerangkatGenerator _skGenerator;
        private readonly SkPerangkatLampiranService _lampiranService;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<PerangkatDesaViewModel> _logger;

        private string _statusText = string.Empty;
        private string _formGalat = string.Empty;
        private bool _isBusy;
        private bool _isFormTerbuka;
        private bool _isModeUbah;
        private int _formId;

        /// <summary>
        /// Jumlah orang pada jabatan resmi, tanpa saringan apa pun. Dipakai
        /// membedakan "database memang kosong" dari "filter tidak cocok",
        /// sebab kelompok lain di luar daftar jabatan resmi tidak dihitung
        /// halaman ini.
        /// </summary>
        private int _jumlahOrangResmi;

        /// <summary>Jumlah orang RT/RW; bagian RT/RW menampilkannya di kepala bagian.</summary>
        private int _jumlahOrangRtRw;

        private int _jumlahOrangBpd;
        private int _jumlahOrangLinmas;
        private int _jumlahOrangPosyandu;
        private int _jumlahOrangKaderKpm;
        private int _jumlahOrangKaderStunting;
        private int _jumlahOrangStaf;
        private int _jumlahOrangOperator;
        private int _jumlahOrangPkk;
        private int _jumlahOrangLpm;
        private int _jumlahOrangKarangTaruna;

        private BagianPerangkat _bagianAktif = BagianPerangkat.PerangkatDesa;

        /// <summary>
        /// Salinan seluruh data perangkat tanpa saringan apa pun. Dibaca sekali
        /// saat halaman dimuat; filter status diterapkan di memori terhadap
        /// salinan ini, sehingga halaman tidak lagi memakai pencarian maupun
        /// filter jabatan/wilayah ke database.
        /// </summary>
        private List<PerangkatDesa> _semua = new();

        /// <summary>
        /// Nama berkas SK Bupati yang sudah terarsip untuk baris yang sedang
        /// diedit. Disimpan terpisah karena form data perangkat tidak punya
        /// isian untuk berkas. Tanpa ini, simpan dari form menulis BerkasSK
        /// kosong sehingga nama berkas arsip hilang dari data walau berkas
        /// PDF-nya sendiri masih ada di folder.
        /// </summary>
        private string? _formBerkasSk;

        /// <summary>
        /// Jalur berkas PDF SK Bupati yang baru dipilih operator lewat form.
        /// Berkasnya belum disalin ke folder arsip sebab baris baru belum punya
        /// ID; penyalinan dilakukan tepat setelah data tersimpan.
        /// </summary>
        private string? _formBerkasSkSumber;

        // isian form
        private string _formNama = string.Empty;
        private string _cariWarga = string.Empty;
        private ObservableCollection<WargaData> _hasilWarga = new();
        private string _formJabatan = string.Empty;
        private string _formNip = string.Empty;
        private string _formNik = string.Empty;
        private string _formJenisKelamin = string.Empty;
        private string _formTempatLahir = string.Empty;
        private string _formTanggalLahir = string.Empty;
        private string _formAgama = string.Empty;
        private string _formPendidikan = string.Empty;
        private string _formTahunLulus = string.Empty;
        private string _formNoIjasah = string.Empty;
        private string _formAlamat = string.Empty;
        private string _formDusun = string.Empty;
        private string _formRt = string.Empty;
        private string _formRw = string.Empty;
        private string _formUnit = string.Empty;
        private string _formNomorHp = string.Empty;
        private string _formWhatsApp = string.Empty;
        private string _formNomorRekening = string.Empty;
        private string _formNomorBpjs = string.Empty;
        private string _formNomorSk = string.Empty;
        private string _formTanggalSk = string.Empty;
        private string _formNomorRekomendasi = string.Empty;
        private string _formTanggalRekomendasi = string.Empty;
        private string _formMasaJabatanMulai = string.Empty;
        private string _formMasaJabatanSelesai = string.Empty;
        private string _formStatus = StatusPerangkat.Aktif;
        private string _formCatatan = string.Empty;

        // panel SK per jabatan
        private bool _isPanelSkTerbuka;
        private string _skJabatan = string.Empty;

        /// <summary>
        /// Jabatan yang isian panelnya terakhir kali dimuat. Dipakai untuk
        /// membedakan "panel dibuka untuk jabatan lain" (isian disiapkan ulang)
        /// dari "jabatan yang sama di-refresh" (isian operator dipertahankan) —
        /// lihat MuatIsianSk.
        /// </summary>
        private string _skJabatanSebelumnya = string.Empty;
        private string _skJenis = nameof(SkJenisPerangkat.Pengangkatan);
        private string _skNomor = string.Empty;
        private string _skTanggal = string.Empty;
        private string _skNama = string.Empty;
        private string _skNik = string.Empty;
        private string _skWilayah = string.Empty;
        private string _skMulai = string.Empty;
        private string _skSelesai = string.Empty;
        private string _skAlasan = string.Empty;
        private string _skGalat = string.Empty;
        private int _skIdPerangkat;
        private bool _skContoh;
        private string? _skBerkasSk;

        // SK bersama: daftar nama pada lampiran disusun dari orang-orang pada
        // jabatan terpilih, lalu kolom pribadinya dilengkapi dari Data Warga
        // lewat NIK oleh SkPerangkatLampiranService.
        private bool _skBersama;
        private bool _skLampiranMemuat;
        private bool _skLampiranCadangan;
        private int _skLampiranVersi;

        public PerangkatDesaViewModel(
            IPerangkatDesaService layanan,
            IWargaRepository wargaRepository,
            IDesaRepository desaRepo,
            AppConfig appConfig,
            StrukturPerangkatGenerator strukturGenerator,
            FormulirPerangkatGenerator formulirGenerator,
            SkPerangkatGenerator skGenerator,
            SkPerangkatLampiranService lampiranService,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<PerangkatDesaViewModel> logger)
        {
            _layanan = layanan ?? throw new ArgumentNullException(nameof(layanan));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _desaRepo = desaRepo ?? throw new ArgumentNullException(nameof(desaRepo));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _strukturGenerator = strukturGenerator ?? throw new ArgumentNullException(nameof(strukturGenerator));
            _formulirGenerator = formulirGenerator ?? throw new ArgumentNullException(nameof(formulirGenerator));
            _skGenerator = skGenerator ?? throw new ArgumentNullException(nameof(skGenerator));
            _lampiranService = lampiranService ?? throw new ArgumentNullException(nameof(lampiranService));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            TambahCommand = new AsyncRelayCommand(BukaFormTambahAsync, () => !IsBusy);
            // Pemilih bagian halaman. Kedua tombol tetap aktif — yang membedakan
            // bagian yang sedang tampil adalah warnanya di XAML — sedangkan
            // PilihBagian sendiri tidak melakukan apa-apa bila bagiannya sama.
            TampilkanPerangkatDesaCommand = new RelayCommand(
                () => PilihBagian(BagianPerangkat.PerangkatDesa), () => !IsBusy);
            TampilkanRtRwCommand = new RelayCommand(
                () => PilihBagian(BagianPerangkat.RtRw), () => !IsBusy);
            TampilkanBpdCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.Bpd), () => !IsBusy);
            TampilkanLinmasCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.Linmas), () => !IsBusy);
            TampilkanPosyanduCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.Posyandu), () => !IsBusy);
            TampilkanKaderKpmCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.KaderKpm), () => !IsBusy);
            TampilkanKaderStuntingCommand = new RelayCommand(
                () => PilihBagian(BagianPerangkat.KaderStunting), () => !IsBusy);
            TampilkanStafDesaCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.StafDesa), () => !IsBusy);
            TampilkanOperatorDesaCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.OperatorDesa), () => !IsBusy);
            TampilkanPkkCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.Pkk), () => !IsBusy);
            TampilkanLpmCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.Lpm), () => !IsBusy);
            TampilkanKarangTarunaCommand = new RelayCommand(() => PilihBagian(BagianPerangkat.KarangTaruna), () => !IsBusy);
            UbahCommand = new AsyncRelayCommand<BarisPerangkatViewModel>(
                BukaFormDariBarisAsync, b => b != null && !IsBusy);
            HapusCommand = new AsyncRelayCommand<BarisPerangkatViewModel>(
                HapusAsync, b => b != null && b.AdaData && !IsBusy);
            BatalFormCommand = new RelayCommand(TutupForm);
            SimpanCommand = new AsyncRelayCommand(SimpanAsync,
                () => !IsBusy && !string.IsNullOrWhiteSpace(FormNama) && !string.IsNullOrWhiteSpace(FormJabatan));
            LampirkanSkFormCommand = new AsyncRelayCommand(LampirkanSkFormAsync, () => !IsBusy);
            BukaSkFormCommand = new RelayCommand(BukaSkForm, () => FormSkAdaBerkas);
            EksporExcelCommand = new AsyncRelayCommand(EksporExcelAsync, () => !IsBusy);
            CetakPdfCommand = new AsyncRelayCommand(CetakPdfAsync, () => !IsBusy);
            CetakFormulirCommand = new AsyncRelayCommand(CetakFormulirAsync, () => !IsBusy);

            SkCommand = new AsyncRelayCommand<BarisPerangkatViewModel>(
                BukaPanelSkAsync, b => b != null && !IsBusy);
            TutupPanelSkCommand = new RelayCommand(TutupPanelSk);
            PratinjauSkCommand = new AsyncRelayCommand(PratinjauSkAsync,
                () => !IsBusy && !SkDariBupati
                      && (!SkBersama || SkLampiran.Count > 0 || !SkBisaBersama));
            ArsipkanSkCommand = new AsyncRelayCommand(ArsipkanSkAsync,
                () => !IsBusy && _skIdPerangkat > 0);
            BukaBerkasSkCommand = new RelayCommand(BukaBerkasSk, () => SkAdaBerkas);

            // SK bersama: daftar nama lampiran selalu diturunkan dari data yang
            // sudah ada di halaman, jadi tombolnya hanya memuat ulang — tidak ada
            // yang perlu disimpan terpisah.
            SegarkanLampiranCommand = new AsyncRelayCommand(
                MuatLampiranBersamaAsync, () => !IsBusy && SkBersama);

            SusunBarisBagian();
        }

        // ---------- pemilih bagian ----------

        /// <summary>
        /// Tombol pemilih bagian halaman, disusun sebagai data supaya XAML cukup
        /// menyalinnya dengan ItemsControl: satu baris data memuat nama, ikon,
        /// perintah, dan keterangan tooltip satu bagian. Dulu ketiganya ditulis
        /// ulang per tombol di XAML, jadi menambah bagian menuntut tiga suntingan.
        /// </summary>
        public sealed class BarisBagian
        {
            internal BarisBagian(BagianPerangkat bagian, string judul, string ikon, string keterangan)
            {
                Bagian = bagian;
                Judul = judul;
                Ikon = ikon;
                Keterangan = keterangan;
            }

            /// <summary>Bagian yang dibuka tombol ini; juga penanda tombol aktif.</summary>
            public BagianPerangkat Bagian { get; }
            public string Judul { get; }
            public string Ikon { get; }
            public string Keterangan { get; }

            /// <summary>Nama anggota enum — dibandingkan gaya tombol bagian
            /// dengan <see cref="NamaBagianAktifEnum"/> untuk menyalakan tombol aktif.</summary>
            public string NamaBagian => Bagian.ToString();

            /// <summary>Perintah pindah bagian; diisi sekali oleh view model induk.</summary>
            public System.Windows.Input.ICommand? Perintah { get; internal set; }
        }

        /// <summary>Tombol bagian kelompok aparatur pemerintahan desa.</summary>
        public IReadOnlyList<BarisBagian> BarisBagianPemerintahan { get; private set; } = Array.Empty<BarisBagian>();

        /// <summary>Tombol bagian lembaga &amp; kader mitra desa.</summary>
        public IReadOnlyList<BarisBagian> BarisBagianLembaga { get; private set; } = Array.Empty<BarisBagian>();

        private void SusunBarisBagian()
        {
            BarisBagian B(BagianPerangkat bagian, string judul, string ikon, string keterangan)
            {
                var baris = new BarisBagian(bagian, judul, ikon, keterangan)
                {
                    Perintah = bagian switch
                    {
                        BagianPerangkat.PerangkatDesa => TampilkanPerangkatDesaCommand,
                        BagianPerangkat.RtRw => TampilkanRtRwCommand,
                        BagianPerangkat.Bpd => TampilkanBpdCommand,
                        BagianPerangkat.Linmas => TampilkanLinmasCommand,
                        BagianPerangkat.Posyandu => TampilkanPosyanduCommand,
                        BagianPerangkat.KaderKpm => TampilkanKaderKpmCommand,
                        BagianPerangkat.KaderStunting => TampilkanKaderStuntingCommand,
                        BagianPerangkat.StafDesa => TampilkanStafDesaCommand,
                        BagianPerangkat.OperatorDesa => TampilkanOperatorDesaCommand,
                        BagianPerangkat.Pkk => TampilkanPkkCommand,
                        BagianPerangkat.Lpm => TampilkanLpmCommand,
                        _ => TampilkanKarangTarunaCommand
                    }
                };
                return baris;
            }

            // Baris atas: aparatur pemerintahan desa (SK perorangan). Baris
            // bawah: lembaga & kader mitra desa (SK bersama/pengangkatan).
            BarisBagianPemerintahan = new[]
            {
                B(BagianPerangkat.PerangkatDesa, "Perangkat Desa", IkonMenu.Orang,
                    "Tampilkan bagian Perangkat Desa"),
                B(BagianPerangkat.RtRw, "RT/RW", IkonMenu.Warga,
                    "Tampilkan bagian RT/RW"),
                B(BagianPerangkat.Bpd, "BPD", IkonMenu.Peraturan,
                    "Tampilkan BPD"),
                B(BagianPerangkat.StafDesa, "Staf Desa", IkonMenu.Dokumen,
                    "Tampilkan Staf Desa"),
                B(BagianPerangkat.OperatorDesa, "Operator Desa", IkonMenu.Api,
                    "Tampilkan Operator Desa"),
            };

            BarisBagianLembaga = new[]
            {
                B(BagianPerangkat.Linmas, "LINMAS", IkonMenu.Linmas,
                    "Tampilkan LINMAS"),
                B(BagianPerangkat.Posyandu, "Posyandu", IkonMenu.Posyandu,
                    "Tampilkan Posyandu"),
                B(BagianPerangkat.KaderKpm, "Kader KPM", IkonMenu.Pengguna,
                    "Tampilkan Kader KPM"),
                B(BagianPerangkat.KaderStunting, "Kader Stunting", IkonMenu.KaderStunting,
                    "Tampilkan Kader Stunting"),
                B(BagianPerangkat.Pkk, "PKK", IkonMenu.Pkk,
                    "Tampilkan PKK"),
                B(BagianPerangkat.Lpm, "LPM", IkonMenu.Lpm,
                    "Tampilkan LPM — LPM dan LPMD satu lembaga, jadi data LPMD lama tampil di sini"),
                B(BagianPerangkat.KarangTaruna, "Karang Taruna", IkonMenu.KarangTaruna,
                    "Tampilkan Karang Taruna"),
            };
        }

        /// <summary>Ikon chip judul halaman: ikon bagian yang sedang tampil, supaya
        /// kepala halaman ikut berganti tanpa satu properti per bagian di XAML.</summary>
        public string IkonBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => IkonMenu.Orang,
            BagianPerangkat.RtRw => IkonMenu.Warga,
            BagianPerangkat.Bpd => IkonMenu.Peraturan,
            BagianPerangkat.Linmas => IkonMenu.Linmas,
            BagianPerangkat.Posyandu => IkonMenu.Posyandu,
            BagianPerangkat.KaderKpm => IkonMenu.Pengguna,
            BagianPerangkat.KaderStunting => IkonMenu.KaderStunting,
            BagianPerangkat.StafDesa => IkonMenu.Dokumen,
            BagianPerangkat.OperatorDesa => IkonMenu.Api,
            BagianPerangkat.Pkk => IkonMenu.Pkk,
            BagianPerangkat.Lpm => IkonMenu.Lpm,
            _ => IkonMenu.KarangTaruna
        };

        // ---------- header ----------

        public string HeaderTitle => "DATA PERANGKAT DESA";

        public string HeaderSubtitle =>
            "Struktur perangkat desa dari Kepala Desa sampai Kadus: satu baris per orang "
            + "yang memegang jabatan. Bagian Perangkat Desa, RT/RW, BPD, LINMAS, Posyandu, "
            + "Kader KPM, Kader Stunting, Staf Desa, Operator Desa, PKK, LPM, dan Karang "
            + "Taruna dipilih "
            + "lewat tombol di bilah alat; halaman terbuka pada bagian Perangkat Desa.";

        // ---------- daftar ----------

        /// <summary>
        /// Baris tabel, selalu berblok per jabatan resmi (Kepala Desa → Kadus).
        /// Jabatan tanpa orang tampil sebagai baris kosong siap isi.
        /// </summary>
        public ObservableCollection<BarisPerangkatViewModel> Items { get; } = new();

        public ObservableCollection<BarisPerangkatViewModel> ItemsRtRw { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsBpd { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsLinmas { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsPosyandu { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKaderKpm { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKaderStunting { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsStafDesa { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsOperatorDesa { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsPkk { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsLpm { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKarangTaruna { get; } = new();

        /// <summary>
        /// Nama bagian aktif sebagai teks (mis. "Linmas") — dibandingkan di XAML
        /// dengan penanda <c>u:BagianAktif.Bagian</c> pada tombol pemilih bagian,
        /// supaya tombol bagian yang sedang tampil bisa ditandai tanpa satu
        /// properti <c>Tampilkan…</c> per bagian.
        /// </summary>
        public string NamaBagianAktifEnum => _bagianAktif.ToString();
        public bool TampilkanRtRw => _bagianAktif == BagianPerangkat.RtRw;
        public bool TampilkanPerangkatDesa => _bagianAktif == BagianPerangkat.PerangkatDesa;

        /// <summary>Benar bila bagian lembaga/kelompok lain (di luar Perangkat Desa dan RT/RW) sedang tampil.</summary>
        public bool TampilkanBagianStandar =>
            _bagianAktif != BagianPerangkat.PerangkatDesa && _bagianAktif != BagianPerangkat.RtRw;

        /// <summary>Tabel baris bagian yang sedang tampil (untuk bagian selain Perangkat Desa &amp; RT/RW).</summary>
        public ObservableCollection<BarisPerangkatViewModel> ItemsAktif => _bagianAktif switch
        {
            BagianPerangkat.Bpd => ItemsBpd,
            BagianPerangkat.Linmas => ItemsLinmas,
            BagianPerangkat.Posyandu => ItemsPosyandu,
            BagianPerangkat.KaderKpm => ItemsKaderKpm,
            BagianPerangkat.KaderStunting => ItemsKaderStunting,
            BagianPerangkat.StafDesa => ItemsStafDesa,
            BagianPerangkat.OperatorDesa => ItemsOperatorDesa,
            BagianPerangkat.Pkk => ItemsPkk,
            BagianPerangkat.Lpm => ItemsLpm,
            BagianPerangkat.KarangTaruna => ItemsKarangTaruna,
            _ => new ObservableCollection<BarisPerangkatViewModel>()
        };

        public string JudulBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.Bpd => "BPD",
            BagianPerangkat.Linmas => "LINMAS",
            BagianPerangkat.Posyandu => "POSYANDU",
            BagianPerangkat.KaderKpm => "KADER KPM",
            BagianPerangkat.KaderStunting => "KADER STUNTING",
            BagianPerangkat.StafDesa => "STAF DESA",
            BagianPerangkat.OperatorDesa => "OPERATOR DESA",
            BagianPerangkat.Pkk => "PKK",
            BagianPerangkat.Lpm => "LPM",
            BagianPerangkat.KarangTaruna => "KARANG TARUNA",
            _ => string.Empty
        };

        /// <summary>Nama bagian untuk pesan, huruf kecil semua (mis. "perangkat desa",
        /// "rt/rw", "bpd").</summary>
        private string NamaBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "perangkat desa",
            BagianPerangkat.RtRw => "RT/RW",
            _ => JudulBagianAktif.ToLowerInvariant()
        };

        /// <summary>Nama bagian untuk judul (mis. "Perangkat Desa", "RT/RW", "BPD").</summary>
        private string NamaBagianTampil => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "Perangkat Desa",
            BagianPerangkat.RtRw => "RT/RW",
            _ => JudulBagianAktif
        };

        /// <summary>Judul dokumen cetak/ekspor bagian yang sedang tampil. Tiap bagian
        /// dicetak terpisah sebab SK tiap kelompok punya cara penerbitan berbeda,
        /// jadi daftar satu bagian tidak dicampur dengan bagian lain.</summary>
        private string JudulDokumenAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "DAFTAR PERANGKAT DESA",
            BagianPerangkat.RtRw => "DAFTAR KETUA RT DAN KETUA RW",
            _ => $"DAFTAR {JudulBagianAktif}"
        };

        /// <summary>Lampiran nama berkas per bagian (kosong untuk Perangkat Desa),
        /// memakai nama enum supaya selalu aman untuk nama berkas.</summary>
        private string SufiksNamaBerkas => _bagianAktif == BagianPerangkat.PerangkatDesa
            ? string.Empty
            : "_" + _bagianAktif;

        /// <summary>Teks tombol tambah — diseragamkan "Tambah Orang" di seluruh
        /// halaman (bilah alat maupun panel keadaan kosong), supaya ajakan yang
        /// sama terlihat di semua bagian.</summary>
        public string LabelTambahAktif => "Tambah Orang";

        /// <summary>Ajakan menambah data pada panel keadaan kosong.</summary>
        public string PesanKosongAksi =>
            "Tekan “Tambah Orang” untuk menambahkan orang pada jabatan yang kosong.";

        public string StatusBagianAktif
        {
            get
            {
                var items = ItemsAktif;
                if (items.Count == 0) return "Belum ada data.";
                int orang = items.Count(b => b.AdaData);
                int kosong = items.Count(b => b.IsBaru);
                string pesan = orang == 0
                    ? "Belum ada orang yang tampil"
                    : $"{orang} orang ditampilkan";
                if (kosong > 0) pesan += $", {kosong} jabatan belum ada orangnya";
                return pesan + ".";
            }
        }

        /// <summary>
        /// Ringkasan bagian RT/RW, tampil di kepala bagian: jumlah orang yang
        /// tampil beserta jabatan yang masih kosong.
        /// </summary>
        public string StatusRtRw
        {
            get
            {
                if (ItemsRtRw.Count == 0) return "Belum ada data RT/RW.";

                int orang = ItemsRtRw.Count(b => b.AdaData);
                int kosong = ItemsRtRw.Count(b => b.IsBaru);
                string pesan = orang == 0
                    ? "Belum ada orang RT/RW yang tampil"
                    : $"{orang} orang RT/RW ditampilkan";
                if (kosong > 0) pesan += $", {kosong} jabatan belum ada orangnya";
                return pesan + ".";
            }
        }

        /// <summary>Benar bila tidak ada satu pun orang yang ditampilkan halaman ini —
        /// jabatan perangkat desa maupun RT/RW — dipakai untuk membedakan
        /// "belum ada data" dari "filter tidak cocok".</summary>
        public bool DatabaseKosong =>
            _jumlahOrangResmi == 0 && _jumlahOrangRtRw == 0 && _jumlahOrangBpd == 0 &&
            _jumlahOrangLinmas == 0 && _jumlahOrangPosyandu == 0 && _jumlahOrangKaderKpm == 0 &&
            _jumlahOrangKaderStunting == 0 && _jumlahOrangStaf == 0 && _jumlahOrangOperator == 0 &&
            _jumlahOrangPkk == 0 && _jumlahOrangLpm == 0 &&
            _jumlahOrangKarangTaruna == 0;

        /// <summary>Tabel bagian yang sedang tampil — ukuran isi inilah yang
        /// menentukan apakah pesan keadaan kosong boleh diperlihatkan.</summary>
        private ObservableCollection<BarisPerangkatViewModel> TabelAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => Items,
            BagianPerangkat.RtRw => ItemsRtRw,
            _ => ItemsAktif
        };

        /// <summary>Benar bila tabel bagian yang sedang tampil tidak memuat satu pun
        /// orang — baris penanda "belum terisi" tidak dihitung — dan database memang
        /// belum punya data. Tanpa syarat itu, tabel penanda memang tampilan yang
        /// diharapkan, jadi pesan di tengah tampilan sengaja tidak ditampilkan.</summary>
        public bool TampilkanKosong => TabelAktif.All(b => !b.AdaData) && DatabaseKosong;

        /// <summary>Tabel kosong karena database memang belum punya data perangkat desa.</summary>
        public bool KosongKarenaBelumAdaData => TampilkanKosong && DatabaseKosong;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    SegarkanPerintah();
                }
            }
        }

        /// <summary>Jabatan untuk form: seluruh jabatan yang tampil di halaman ini —
        /// perangkat desa resmi, RT/RW, dan kelompok lain (BPD, Linmas, Posyandu,
        /// Kader KPM, Kader Stunting, Staf Desa, Operator Desa, PKK, LPM, Karang Taruna) — supaya
        /// orang yang disimpan selalu tampil di salah satu tabel.</summary>
        public static readonly IReadOnlyList<string> DaftarJabatanBpd = new[]
        {
            JabatanPerangkat.KetuaBPD,
            JabatanPerangkat.WakilKetuaBPD,
            JabatanPerangkat.SekretarisBPD,
            JabatanPerangkat.AnggotaBPD
        };
        public static readonly IReadOnlyList<string> DaftarJabatanLinmas = new[] { JabatanPerangkat.Linmas };
        public static readonly IReadOnlyList<string> DaftarJabatanPosyandu = new[] { JabatanPerangkat.KaderPosyandu };
        public static readonly IReadOnlyList<string> DaftarJabatanPkk = new[] { JabatanPerangkat.KetuaPKK, JabatanPerangkat.PengurusPKK };
        public static readonly IReadOnlyList<string> DaftarJabatanLpm = new[] { JabatanPerangkat.KetuaLPM, JabatanPerangkat.PengurusLPM };
        public static readonly IReadOnlyList<string> DaftarJabatanKaderStunting = new[] { JabatanPerangkat.KaderStunting };
        public static readonly IReadOnlyList<string> DaftarJabatanKarangTaruna = new[]
        {
            JabatanPerangkat.KetuaKarangTaruna,
            JabatanPerangkat.WakilKetuaKarangTaruna,
            JabatanPerangkat.SekretarisKarangTaruna,
            JabatanPerangkat.BendaharaKarangTaruna,
            JabatanPerangkat.PengurusKarangTaruna
        };

        public IReadOnlyList<string> DaftarJabatan { get; } =
            JabatanPerangkat.Resmi
                .Concat(new[] { JabatanPerangkat.KetuaRT, JabatanPerangkat.KetuaRW })
                .Concat(DaftarJabatanBpd)
                .Concat(DaftarJabatanLinmas)
                .Concat(DaftarJabatanPosyandu)
                .Concat(new[] { JabatanPerangkat.KaderKPM })
                .Concat(DaftarJabatanKaderStunting)
                .Concat(new[] { JabatanPerangkat.Staf, JabatanPerangkat.OperatorDesa })
                .Concat(DaftarJabatanPkk)
                .Concat(DaftarJabatanLpm)
                .Concat(DaftarJabatanKarangTaruna)
                .ToList();

        /// <summary>Jabatan bagian RT/RW, urut RT lalu RW.</summary>
        public IReadOnlyList<string> DaftarJabatanRtRw { get; } =
            new[] { JabatanPerangkat.KetuaRT, JabatanPerangkat.KetuaRW };

        /// <summary>Daftar jabatan satu bagian halaman ini — sumber pilihan form,
        /// panel SK, dan tombol Tambah tiap bagian. Dibatasi per bagian supaya
        /// pilihan tidak pernah melompat ke bagian lain, sebab tiap kelompok
        /// punya cara penerbitan SK yang berbeda.</summary>
        public IReadOnlyList<string> JabatanBagian(BagianPerangkat bagian) => bagian switch
        {
            BagianPerangkat.PerangkatDesa => JabatanPerangkat.Resmi,
            BagianPerangkat.RtRw => DaftarJabatanRtRw,
            BagianPerangkat.Bpd => DaftarJabatanBpd,
            BagianPerangkat.Linmas => DaftarJabatanLinmas,
            BagianPerangkat.Posyandu => DaftarJabatanPosyandu,
            BagianPerangkat.KaderKpm => new[] { JabatanPerangkat.KaderKPM },
            BagianPerangkat.KaderStunting => DaftarJabatanKaderStunting,
            BagianPerangkat.StafDesa => new[] { JabatanPerangkat.Staf },
            BagianPerangkat.OperatorDesa => new[] { JabatanPerangkat.OperatorDesa },
            BagianPerangkat.Pkk => DaftarJabatanPkk,
            BagianPerangkat.Lpm => DaftarJabatanLpm,
            BagianPerangkat.KarangTaruna => DaftarJabatanKarangTaruna,
            _ => DaftarJabatan
        };

        /// <summary>Daftar jabatan bagian yang memuat satu jabatan — dipakai form yang
        /// dibuka dari baris, supaya pilihan jabatannya tidak keluar dari baris itu.</summary>
        private IReadOnlyList<string> JabatanBagianJabatan(string jabatan)
        {
            foreach (BagianPerangkat bagian in Enum.GetValues<BagianPerangkat>())
            {
                var daftar = JabatanBagian(bagian);
                if (daftar.Contains(jabatan, StringComparer.Ordinal)) return daftar;
            }
            return DaftarJabatan;
        }

        private IReadOnlyList<string>? _daftarJabatanForm;

        /// <summary>Pilihan jabatan pada form — dibatasi bagian tempat form dibuka,
        /// sehingga orang yang disimpan selalu jatuh ke bagian itu.</summary>
        public IReadOnlyList<string> DaftarJabatanForm => _daftarJabatanForm ?? DaftarJabatan;

        private void AturDaftarJabatanForm(IReadOnlyList<string> daftar)
        {
            _daftarJabatanForm = daftar;
            OnPropertyChanged(nameof(DaftarJabatanForm));
        }

        public IReadOnlyList<string> PilihanStatusForm { get; } = StatusPerangkat.Semua;

        public IReadOnlyList<string> PilihanJenisKelamin { get; } = new[] { "Laki-laki", "Perempuan" };

        public IReadOnlyList<string> PilihanPendidikan { get; } = new[]
        {
            "SD", "SMP/MTs", "SMA/MA/SMK", "D1/D2/D3", "S1", "S2", "S3"
        };

        /// <summary>Opsi agama — daftar yang sama dengan form Data Warga.</summary>
        public IReadOnlyList<string> PilihanAgama { get; } = SuratConstants.ValidAgama;

        // ---------- form ----------

        public bool IsFormTerbuka
        {
            get => _isFormTerbuka;
            private set => SetProperty(ref _isFormTerbuka, value);
        }

        public string JudulForm => IsModeUbah
            ? $"Ubah Data {NamaBagianTampil}"
            : $"Tambah {NamaBagianTampil}";

        public bool IsModeUbah
        {
            get => _isModeUbah;
            private set
            {
                if (SetProperty(ref _isModeUbah, value))
                    OnPropertyChanged(nameof(JudulForm));
            }
        }

        /// <summary>Isian form yang bermasalah, tampil di bawah form.</summary>
        public string FormGalat
        {
            get => _formGalat;
            private set => SetProperty(ref _formGalat, value);
        }

        public string FormNama
        {
            get => _formNama;
            set
            {
                if (SetProperty(ref _formNama, value))
                {
                    BersihkanGalat();
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string CariWarga
        {
            get => _cariWarga;
            set
            {
                if (SetProperty(ref _cariWarga, value ?? string.Empty))
                    _ = CariWargaAsync(_cariWarga);
            }
        }

        public ObservableCollection<WargaData> HasilWarga
        {
            get => _hasilWarga;
            private set => SetProperty(ref _hasilWarga, value);
        }

        private async Task CariWargaAsync(string kata)
        {
            if (kata.Trim().Length < 2)
            {
                HasilWarga.Clear();
                return;
            }

            string pencarian = kata.Trim();
            try
            {
                var wargaDitemukan = await _wargaRepository.CariWargaUntukPerangkatAsync(pencarian, 20)
                    .ConfigureAwait(true);
                if (!string.Equals(pencarian, _cariWarga.Trim(), StringComparison.Ordinal)) return;
                HasilWarga = new ObservableCollection<WargaData>(wargaDitemukan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pencarian warga untuk perangkat desa gagal");
                HasilWarga.Clear();
            }
        }

        public void PilihWarga(WargaData? warga)
        {
            if (warga is null) return;
            FormNama = warga.Nama ?? string.Empty;
            FormNik = warga.NIK ?? string.Empty;
            FormJenisKelamin = PerangkatDesa.JenisKelaminRingkas(warga.JenisKelamin);
            FormTempatLahir = warga.TempatLahir ?? string.Empty;
            FormTanggalLahir = TanggalIsian.KeTeks(DateTime.TryParse(warga.TanggalLahir, out var tgl) ? tgl : null);
            FormAgama = warga.Agama ?? string.Empty;
            FormPendidikan = warga.Pendidikan ?? string.Empty;
            FormAlamat = warga.AlamatLengkap ?? warga.Alamat ?? string.Empty;
            FormDusun = warga.Dusun ?? string.Empty;
            FormRt = warga.RT ?? string.Empty;
            FormRw = warga.RW ?? string.Empty;
            FormNomorHp = warga.NomorHP ?? string.Empty;
            CariWarga = string.Empty;
            HasilWarga.Clear();
        }

        public string FormJabatan
        {
            get => _formJabatan;
            set
            {
                if (SetProperty(ref _formJabatan, value))
                {
                    BersihkanGalat();
                    SegarkanFormSk();
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Hilangkan pesan galat begitu operator mulai memperbaiki isian, supaya
        /// tidak terbawa dari percobaan sebelumnya.
        /// </summary>
        private void BersihkanGalat()
        {
            if (!string.IsNullOrEmpty(_formGalat)) FormGalat = string.Empty;
        }

        public string FormNip
        {
            get => _formNip;
            set => SetProperty(ref _formNip, value);
        }

        public string FormNik
        {
            get => _formNik;
            set => SetProperty(ref _formNik, value);
        }

        public string FormJenisKelamin
        {
            get => _formJenisKelamin;
            set => SetProperty(ref _formJenisKelamin, value);
        }

        public string FormTempatLahir
        {
            get => _formTempatLahir;
            set => SetProperty(ref _formTempatLahir, value);
        }

        /// <summary>
        /// Tanggal lahir sebagai teks bebas (isian TextBox biasa, bukan pemilih
        /// tanggal): diisi dan dibaca dengan pola dd-MM-yyyy. Teks selain pola
        /// itu ditolak saat disimpan dengan pesan yang jelas, sebab hanya tanggal
        /// yang sah boleh masuk ke kolom tanggal database.
        /// </summary>
        public string FormTanggalLahir
        {
            get => _formTanggalLahir;
            set => SetProperty(ref _formTanggalLahir, value ?? string.Empty);
        }

        public string FormPendidikan
        {
            get => _formPendidikan;
            set => SetProperty(ref _formPendidikan, value);
        }

        /// <summary>Agama (kolom AGAMA pada berkas data perangkat contoh).</summary>
        public string FormAgama
        {
            get => _formAgama;
            set => SetProperty(ref _formAgama, value ?? string.Empty);
        }

        /// <summary>Tahun lulus pendidikan terakhir (teks, tanda "-" aman).</summary>
        public string FormTahunLulus
        {
            get => _formTahunLulus;
            set => SetProperty(ref _formTahunLulus, value ?? string.Empty);
        }

        /// <summary>Nomor ijazah pendidikan terakhir.</summary>
        public string FormNoIjasah
        {
            get => _formNoIjasah;
            set => SetProperty(ref _formNoIjasah, value ?? string.Empty);
        }

        /// <summary>
        /// Alamat lama tetap dibawa saat data diubah supaya baris lama tidak
        /// kehilangan alamatnya, tetapi form sudah tidak menyediakan isiannya —
        /// keterangan wilayah cukup diisi lewat Dusun/RT/RW.
        /// </summary>
        public string FormAlamat
        {
            get => _formAlamat;
            set => SetProperty(ref _formAlamat, value);
        }

        public string FormDusun
        {
            get => _formDusun;
            set => SetProperty(ref _formDusun, value);
        }

        public string FormRt
        {
            get => _formRt;
            set => SetProperty(ref _formRt, value);
        }

        public string FormRw
        {
            get => _formRw;
            set => SetProperty(ref _formRw, value);
        }

        /// <summary>Unit kerja baris (mis. dusun untuk KADUS), tersimpan pada data
        /// walau halaman ini tidak lagi memakainya untuk pengelompokan SK.</summary>
        public string FormUnit
        {
            get => _formUnit;
            set => SetProperty(ref _formUnit, value ?? string.Empty);
        }

        public string FormNomorHp
        {
            get => _formNomorHp;
            set => SetProperty(ref _formNomorHp, value);
        }

        public string FormWhatsApp
        {
            get => _formWhatsApp;
            set => SetProperty(ref _formWhatsApp, value);
        }

        /// <summary>Nomor rekening bank perangkat desa (opsional).</summary>
        public string FormNomorRekening
        {
            get => _formNomorRekening;
            set => SetProperty(ref _formNomorRekening, value ?? string.Empty);
        }

        /// <summary>Nomor kepesertaan BPJS perangkat desa (opsional).</summary>
        public string FormNomorBpjs
        {
            get => _formNomorBpjs;
            set => SetProperty(ref _formNomorBpjs, value ?? string.Empty);
        }

        public string FormNomorSk
        {
            get => _formNomorSk;
            set => SetProperty(ref _formNomorSk, value);
        }

        public string FormTanggalSk
        {
            get => _formTanggalSk;
            set => SetProperty(ref _formTanggalSk, value ?? string.Empty);
        }

        /// <summary>Nomor rekomendasi pengangkatan (rekomendasi Kecamatan).</summary>
        public string FormNomorRekomendasi
        {
            get => _formNomorRekomendasi;
            set => SetProperty(ref _formNomorRekomendasi, value ?? string.Empty);
        }

        /// <summary>Tanggal rekomendasi pengangkatan (teks dd-MM-yyyy).</summary>
        public string FormTanggalRekomendasi
        {
            get => _formTanggalRekomendasi;
            set => SetProperty(ref _formTanggalRekomendasi, value ?? string.Empty);
        }

        public string FormMasaJabatanMulai
        {
            get => _formMasaJabatanMulai;
            set => SetProperty(ref _formMasaJabatanMulai, value ?? string.Empty);
        }

        public string FormMasaJabatanSelesai
        {
            get => _formMasaJabatanSelesai;
            set => SetProperty(ref _formMasaJabatanSelesai, value ?? string.Empty);
        }

        public string FormStatus
        {
            get => _formStatus;
            set => SetProperty(ref _formStatus, value);
        }

        public string FormCatatan
        {
            get => _formCatatan;
            set => SetProperty(ref _formCatatan, value);
        }

        // ---------- SK pada form ----------

        /// <summary>
        /// Benar bila jabatan terpilih mempunyai SK yang diterbitkan Bupati
        /// (Kepala Desa dan anggota BPD): berkas PDF-nya wajib dilampirkan saat
        /// menyimpan, sebab aplikasi tidak menerbitkan SK itu.
        /// </summary>
        public bool FormSkWajib =>
            SkJabatanAksiKatalog.SumberJabatan(FormJabatan) == SumberSkPerangkat.Bupati;

        /// <summary>
        /// Benar bila jabatan terpilih mempunyai SK yang diterbitkan Kepala Desa:
        /// SK jadi dibuat otomatis dari data form saat menyimpan, jadi form tidak
        /// menampilkan pilihan berkas lampiran sama sekali.
        /// </summary>
        public bool FormSkOtomatis =>
            !string.IsNullOrWhiteSpace(FormJabatan) && !FormSkWajib;

        /// <summary>Penjelasan pembuatan SK otomatis (info pada form).</summary>
        public string FormSkPesanOtomatis
        {
            get
            {
                string jabatan = FormJabatan;
                string kelompok = JabatanPerangkat.Kelompok(jabatan);
                if (!SkPerangkatKatalog.AdaUntuk(jabatan, kelompok))
                {
                    return $"Kelompok jabatan {NamaJabatanTampil(jabatan)} belum punya template SK, "
                         + "jadi SK tidak bisa dibuat otomatis saat menyimpan.";
                }

                string jabatanTampil = NamaJabatanTampil(jabatan);
                if (SkPerangkatKatalog.LazimBersama(kelompok))
                {
                    string kelompokTeks;
                    if (string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal))
                    {
                        kelompokTeks = " Anggota dikelompokkan per Posyandu mengikuti dusunnya — "
                            + "jumlah posyandu mengikuti jumlah Kepala Dusun.";
                    }
                    else if (SkPerangkatKatalog.Untuk(jabatan, kelompok)?.BisaPenetapan == true)
                    {
                        kelompokTeks = " SK kelompok ini berbentuk Penetapan, sesuai contoh SK desanya.";
                    }
                    else
                    {
                        kelompokTeks = string.Empty;
                    }

                    return $"SK {jabatanTampil} tidak perlu dilampirkan: satu SK bersama untuk seluruh "
                         + "anggota dengan lampiran daftar nama dibuat otomatis dari data form ini "
                         + "(nomor dan tanggal SK) tepat saat data disimpan, lalu diarsipkan."
                         + kelompokTeks;
                }

                return $"SK {jabatanTampil} tidak perlu dilampirkan: aplikasi membuatnya "
                     + "otomatis dalam bentuk SK jadi dari data form ini (nomor dan tanggal SK, nama, "
                     + "NIK, wilayah, serta masa jabatan) tepat saat data disimpan, lalu mengarsipkannya.";
            }
        }

        /// <summary>Nama berkas SK pada form: pilihan baru, arsip lama, atau belum ada.</summary>
        public string FormSkNamaBerkas
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_formBerkasSkSumber))
                    return Path.GetFileName(_formBerkasSkSumber) + "   (belum diarsipkan)";
                if (BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null)
                    return _formBerkasSk!;
                return "Belum ada berkas — wajib dilampirkan sebelum data disimpan.";
            }
        }

        /// <summary>Benar bila berkas SK bisa dibuka dari form (pilihan baru atau arsip lama).</summary>
        public bool FormSkAdaBerkas =>
            (!string.IsNullOrWhiteSpace(_formBerkasSkSumber) && File.Exists(_formBerkasSkSumber))
            || BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null;

        // ---------- perintah ----------

        public AsyncRelayCommand TambahCommand { get; }

        /// <summary>Tampilkan bagian Perangkat Desa (bawaan halaman ini).</summary>
        public RelayCommand TampilkanPerangkatDesaCommand { get; }

        public RelayCommand TampilkanRtRwCommand { get; }
        public RelayCommand TampilkanBpdCommand { get; }
        public RelayCommand TampilkanLinmasCommand { get; }
        public RelayCommand TampilkanPosyanduCommand { get; }
        public RelayCommand TampilkanKaderKpmCommand { get; }
        public RelayCommand TampilkanKaderStuntingCommand { get; }
        public RelayCommand TampilkanStafDesaCommand { get; }
        public RelayCommand TampilkanOperatorDesaCommand { get; }
        public RelayCommand TampilkanPkkCommand { get; }
        public RelayCommand TampilkanLpmCommand { get; }
        public RelayCommand TampilkanKarangTarunaCommand { get; }

        /// <summary>Buka form lengkap dari satu baris tabel (baris kosong = mode tambah
        /// dengan jabatannya sudah terpilih).</summary>
        public AsyncRelayCommand<BarisPerangkatViewModel> UbahCommand { get; }

        public AsyncRelayCommand<BarisPerangkatViewModel> HapusCommand { get; }
        public RelayCommand BatalFormCommand { get; }
        public AsyncRelayCommand SimpanCommand { get; }

        /// <summary>Pilih berkas PDF SK Bupati untuk lampiran wajib pada form.</summary>
        public AsyncRelayCommand LampirkanSkFormCommand { get; }

        /// <summary>Buka berkas SK form (pilihan baru atau arsip) pada pratinjau PDF.</summary>
        public RelayCommand BukaSkFormCommand { get; }

        /// <summary>Simpan daftar yang sedang tampil ke berkas Excel (.xlsx).</summary>
        public AsyncRelayCommand EksporExcelCommand { get; }

        /// <summary>Cetak daftar ke PDF lalu tampilkan pada pratinjau PDFium.</summary>
        public AsyncRelayCommand CetakPdfCommand { get; }

        /// <summary>Cetak formulir pendataan kosong (18 kolom DATA PERANGKAT) untuk
        /// mengumpulkan data perangkat baru, lalu tampilkan pada pratinjau PDFium.</summary>
        public AsyncRelayCommand CetakFormulirCommand { get; }

        /// <summary>Buka panel SK untuk satu baris, termasuk baris jabatan yang belum ada orangnya.</summary>
        public AsyncRelayCommand<BarisPerangkatViewModel> SkCommand { get; }

        /// <summary>Tutup panel SK tanpa mengubah data.</summary>
        public RelayCommand TutupPanelSkCommand { get; }

        /// <summary>Buat SK jabatan terpilih lalu tampilkan pratinjaunya di PDFium.</summary>
        public AsyncRelayCommand PratinjauSkCommand { get; }

        /// <summary>Salin berkas PDF SK ke folder arsip untuk orang pada jabatan terpilih.</summary>
        public AsyncRelayCommand ArsipkanSkCommand { get; }

        /// <summary>Buka berkas SK terarsip pada pratinjau PDFium.</summary>
        public RelayCommand BukaBerkasSkCommand { get; }

        /// <summary>Muat ulang daftar nama lampiran SK bersama dari data jabatan terpilih.</summary>
        public AsyncRelayCommand SegarkanLampiranCommand { get; }

        // ---------- panel SK per jabatan (properti) ----------

        /// <summary>Panel SK tampil sebagai overlay di atas tabel.</summary>
        public bool IsPanelSkTerbuka
        {
            get => _isPanelSkTerbuka;
            private set => SetProperty(ref _isPanelSkTerbuka, value);
        }

        public string JudulPanelSk => string.IsNullOrWhiteSpace(_skJabatan)
            ? "SURAT KEPUTUSAN"
            : $"SURAT KEPUTUSAN — {NamaJabatanTampil(_skJabatan)}";

        /// <summary>Jabatan yang bisa dipilih di panel SK — dibatasi bagian tempat
        /// panel dibuka (lihat <see cref="JabatanBagian"/>), sebab tiap kelompok
        /// punya cara penerbitan SK berbeda. Tanpa daftar ini, panel yang dibuka
        /// dari baris bagian lain menampilkan kotak jabatan kosong.</summary>
        public IReadOnlyList<string> PilihanJabatanSk => _daftarSkJabatan ?? DaftarJabatan;

        private IReadOnlyList<string>? _daftarSkJabatan;

        /// <summary>Jenis SK; nilainya nama <see cref="SkJenisPerangkat"/> supaya mudah di-bind.</summary>
        public IReadOnlyList<FilterPilihan> PilihanJenisSk { get; } = new[]
        {
            new FilterPilihan(nameof(SkJenisPerangkat.Pengangkatan), "Pengangkatan"),
            new FilterPilihan(nameof(SkJenisPerangkat.Pemberhentian), "Pemberhentian"),
            new FilterPilihan(nameof(SkJenisPerangkat.Penetapan), "Penetapan (bila kelompok mendukung)")
        };

        /// <summary>Ganti jabatan dari panel; isian disesuaikan dengan data jabatan itu.</summary>
        public string SkJabatan
        {
            get => _skJabatan;
            set
            {
                if (!SetProperty(ref _skJabatan, value ?? string.Empty)) return;
                MuatIsianSk(value);
            }
        }

        public string SkJenis
        {
            get => _skJenis;
            set => SetProperty(ref _skJenis, value ?? nameof(SkJenisPerangkat.Pengangkatan));
        }

        public string SkNomor
        {
            get => _skNomor;
            set => SetProperty(ref _skNomor, value ?? string.Empty);
        }

        public string SkTanggal
        {
            get => _skTanggal;
            set => SetProperty(ref _skTanggal, value ?? string.Empty);
        }

        public string SkNama
        {
            get => _skNama;
            set => SetProperty(ref _skNama, value ?? string.Empty);
        }

        public string SkNik
        {
            get => _skNik;
            set => SetProperty(ref _skNik, value ?? string.Empty);
        }

        public string SkWilayah
        {
            get => _skWilayah;
            set => SetProperty(ref _skWilayah, value ?? string.Empty);
        }

        public string SkMulai
        {
            get => _skMulai;
            set => SetProperty(ref _skMulai, value ?? string.Empty);
        }

        public string SkSelesai
        {
            get => _skSelesai;
            set => SetProperty(ref _skSelesai, value ?? string.Empty);
        }

        public string SkAlasan
        {
            get => _skAlasan;
            set => SetProperty(ref _skAlasan, value ?? string.Empty);
        }

        /// <summary>Isian panel yang bermasalah, tampil di dalam panel.</summary>
        public string SkGalat
        {
            get => _skGalat;
            private set => SetProperty(ref _skGalat, value ?? string.Empty);
        }

        /// <summary>Benar bila jabatan ini belum punya orang, sehingga SK dicetak sebagai contoh.</summary>
        public bool SkContoh
        {
            get => _skContoh;
            private set
            {
                if (SetProperty(ref _skContoh, value))
                    OnPropertyChanged(nameof(SkCatatanMode));
            }
        }

        public string SkCatatanMode => SkContoh
            ? "Jabatan ini belum ada orangnya. SK dicetak sebagai contoh: nama dan tanggal dibiarkan "
              + "bergaris titik-titik, dan isian boleh diketik untuk dicoba."
            : "Isian di bawah sudah diambil dari data orang pada jabatan ini; ubah bila perlu sebelum dicetak.";

        /// <summary>SK jabatan ini diterbitkan Bupati (Kepala Desa), jadi aplikasi tidak menerbitkan dokumennya.</summary>
        public bool SkDariBupati => SkJabatanAksiKatalog.SumberJabatan(_skJabatan) == SumberSkPerangkat.Bupati;

        /// <summary>Sudah ada berkas SK terarsip untuk orang ini.</summary>
        public bool SkAdaBerkas => !string.IsNullOrWhiteSpace(_skBerkasSk);

        /// <summary>Judul bagian pengarsipan berkas SK, menyesuaikan penerbit SK-nya.</summary>
        public string SkArsipJudul => SkDariBupati ? "Berkas SK Bupati terarsip" : "Berkas SK tersimpan";

        /// <summary>Teks tombol pengarsipan berkas SK.</summary>
        public string SkArsipTombol => SkDariBupati ? "Arsipkan SK Bupati" : "Arsipkan Berkas SK";

        /// <summary>
        /// Keterangan pengarsipan: SK Bupati tidak diterbitkan aplikasi, sedangkan
        /// SK jabatan lain boleh dilekati berkas bertanda tangan yang sudah ada.
        /// </summary>
        public string SkArsipKeterangan => SkDariBupati
            ? $"SK {NamaJabatanTampil(_skJabatan)} diterbitkan Bupati, bukan oleh Kepala Desa, sehingga aplikasi tidak "
              + "menerbitkan dokumennya. Arsipkan berkas PDF SK-nya supaya nomor dan dokumennya tersimpan "
              + "bersama data ini."
            : "SK jabatan ini diterbitkan Kepala Desa dan dibuat otomatis dalam bentuk SK jadi saat data "
              + "orang disimpan; berkasnya tersimpan di sini dan bisa dibuka lewat tombol di bawah. "
              + "Dokumen juga tetap bisa dicetak ulang dari isian panel ini.";

        public string SkNamaBerkas => string.IsNullOrWhiteSpace(_skBerkasSk)
            ? "Belum ada berkas arsip."
            : _skBerkasSk!;

        /// <summary>Folder arsip berkas SK — ditampilkan sebagai tooltip supaya operator tahu lokasinya.</summary>
        public string SkLokasiArsip => BerkasSkPerangkat.LokasiFolder;

        /// <summary>Angka ringkas jabatan terpilih, mis. "3 orang — 2 aktif, 1 menunggu SK".</summary>
        public string SkKeteranganJabatan
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_skJabatan)) return string.Empty;
                var kartu = SkJabatanAksiKatalog
                    .Susun(_semua, new[] { JabatanPerangkat.Normalisasi(_skJabatan) })
                    .FirstOrDefault();
                return kartu?.Ringkas ?? string.Empty;
            }
        }

        /// <summary>
        /// Catatan template SK jabatan terpilih: contoh SK yang dipakai aplikasi
        /// untuk jabatan ini beserta sumbernya, supaya operator tahu SK-nya
        /// memang disusun sesuai jabatannya.
        /// </summary>
        /// <summary>
        /// Kelompok jabatan terpilih punya template SK bawaan sehingga dokumennya
        /// bisa dibuat. Bila false, panel jangan menampilkan daftar dasar hukum
        /// sebab tidak ada template yang dirujuk — diganti penjelasan keterbatasannya.
        /// </summary>
        public bool SkAdaTemplate =>
            !string.IsNullOrWhiteSpace(_skJabatan)
            && SkPerangkatKatalog.AdaUntuk(_skJabatan, JabatanPerangkat.Kelompok(_skJabatan));

        public string SkCatatanTemplate
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_skJabatan)) return string.Empty;

                if (!SkAdaTemplate)
                {
                    string kelompok = JabatanPerangkat.Kelompok(_skJabatan);
                    return $"Kelompok {kelompok} belum punya template SK bawaan, jadi SK "
                         + $"{NamaJabatanTampil(_skJabatan)} belum bisa dicetak dari panel ini. "
                         + "Berkas SK bertanda tangan tetap bisa disimpan lewat bagian Berkas SK.";
                }

                return $"Template SK {NamaJabatanTampil(_skJabatan)} disusun mengikuti format naskah "
                     + "dinas (Peraturan Bupati tentang Tata Naskah Dinas Desa) beserta UU, PP, dan "
                     + "Permendagri pengangkatan/pemberhentian perangkat desa yang berlaku, dirinci "
                     + "menurut tugas pokok jabatannya.";
            }
        }

        /// <summary>Dasar hukum template jabatan terpilih, ditampilkan di panel SK.</summary>
        public string SkDasarHukum
        {
            get
            {
                var template = SkPerangkatKatalog.Untuk(_skJabatan, JabatanPerangkat.Kelompok(_skJabatan));
                return template?.DasarHukumTeks ?? string.Empty;
            }
        }

        // ---------- SK bersama (lampiran daftar nama) ----------

        /// <summary>
        /// Benar bila kelompok jabatan terpilih memang diterbitkan sebagai satu SK
        /// bersama (Linmas, Posyandu, Kader KPM — lihat
        /// <see cref="SkPerangkatKatalog.LazimBersama"/>), bukan SK per orang.
        /// Panel menyalakan mode bersama otomatis untuk kelompok ini.
        /// </summary>
        public bool SkWajibBersama =>
            SkPerangkatKatalog.LazimBersama(JabatanPerangkat.Kelompok(_skJabatan));

        /// <summary>Benar bila operator boleh mengubah mode SK bersama.</summary>
        public bool SkBersamaDiubah => !SkWajibBersama;

        /// <summary>
        /// Mode SK bersama: satu SK dan satu nomor untuk semua orang pada jabatan
        /// terpilih, dengan nama-namanya dicetak pada halaman Lampiran seperti SK
        /// Linmas/Posyandu. Dinyalakan operator dari panel — kecuali kelompok yang
        /// SK-nya memang selalu bersama, yang dinyalakan otomatis dan tidak bisa
        /// dimatikan; daftar namanya dimuat saat mode menyala dan dimuat ulang
        /// lewat tombolnya.
        /// </summary>
        public bool SkBersama
        {
            get => _skBersama;
            set
            {
                // Kelompok SK bersama (Linmas, Posyandu, Kader KPM) tidak boleh
                // keluar dari mode bersama: dokumennya memang satu untuk seluruh
                // anggota, bukan SK per orang.
                if (!value && SkWajibBersama)
                {
                    OnPropertyChanged(nameof(SkBersama));
                    return;
                }

                if (!SetProperty(ref _skBersama, value)) return;

                if (value)
                {
                    // Pemuatannya sengaja tidak ditunggui setter; panel menampilkan
                    // keadaan "memuat" sampai daftarnya siap.
                    _ = MuatLampiranBersamaAsync();
                }
                else
                {
                    // Versi dinaikkan supaya hasil pemuatan yang masih berjalan
                    // tidak lagi dipakai setelah mode dimatikan.
                    _skLampiranVersi++;
                    _skLampiranCadangan = false;
                    SkLampiranMemuat = false;
                    SkLampiran.Clear();
                    SegarkanTurunanLampiran();
                }
            }
        }

        /// <summary>Nama-nama yang ikut pada lampiran SK bersama, urut seperti akan tercetak.</summary>
        public ObservableCollection<LampiranBarisViewModel> SkLampiran { get; } = new();

        /// <summary>Daftar nama lampiran sedang dibaca/dilengkapi dari Data Warga.</summary>
        public bool SkLampiranMemuat
        {
            get => _skLampiranMemuat;
            private set
            {
                if (SetProperty(ref _skLampiranMemuat, value))
                    OnPropertyChanged(nameof(SkPesanLampiran));
            }
        }

        public int SkJumlahLampiran => SkLampiran.Count;

        /// <summary>Benar bila jabatan terpilih punya orang yang bisa masuk ke lampiran.</summary>
        public bool SkBisaBersama => CalonLampiran(_skJabatan).Orang.Count > 0;

        /// <summary>Penjelasan mode SK bersama, tampil di panel di bawah tombolnya.</summary>
        public string SkCatatanBersama
        {
            get
            {
                const string penjelasan =
                    "Satu SK dan satu nomor untuk semua orang pada jabatan ini; nama mereka tidak dicetak di badan SK, "
                    + "melainkan pada halaman Lampiran daftar nama seperti SK Linmas/Posyandu. Kolom pribadi (pendidikan, "
                    + "pekerjaan, agama, golongan darah, status kawin, alamat) diambil dari Data Warga lewat NIK.";

                if (!SkWajibBersama) return penjelasan;

                return $"Kelompok {NamaJabatanTampil(_skJabatan)} pada contoh SK desanya diterbitkan sebagai satu SK "
                    + "bersama, jadi pilihan ini menyala tetap dan tidak bisa dimatikan. " + penjelasan;
            }
        }

        /// <summary>Keterangan asal-usul nama pada lampiran (termasuk pemakaian cadangan).</summary>
        public string SkLampiranCatatan => !SkBersama
            ? string.Empty
            : _skLampiranCadangan
                ? "Jabatan ini tidak punya orang berstatus AKTIF atau MENUNGGU SK, jadi seluruh orang pada jabatan "
                  + "ini dimasukkan — hapus baris yang tidak perlu."
                : SkJumlahLampiran > 0
                    ? $"Nama diambil dari data jabatan {NamaJabatanTampil(_skJabatan)}. Peran dan unitnya boleh "
                      + "disunting, dan baris yang tidak ikut dihapus."
                    : string.Empty;

        /// <summary>Ringkas jumlah nama pada lampiran, tampil di atas daftar.</summary>
        public string SkLampiranRingkasan => SkJumlahLampiran == 0
            ? string.Empty
            : $"{SkJumlahLampiran} nama akan tercetak pada lampiran daftar nama.";

        /// <summary>Pesan keadaan daftar lampiran: memuat, kosong, atau jabatan tanpa orang.</summary>
        public string SkPesanLampiran
        {
            get
            {
                if (!SkBersama) return string.Empty;
                if (SkLampiranMemuat) return "Memuat data pribadi dari Data Warga…";
                if (SkLampiran.Count > 0) return string.Empty;
                return SkBisaBersama
                    ? "Daftar nama masih kosong. Tekan Muat Ulang Daftar untuk mengisi nama dari data jabatan ini."
                    : $"Jabatan {NamaJabatanTampil(_skJabatan)} belum punya orang, jadi lampirannya belum bisa "
                      + "disusun. Isi datanya lebih dulu lewat tombol Tambah Orang.";
            }
        }

        /// <summary>Teks tombol pratinjau, menyesuaikan mode yang sedang dipakai.</summary>
        public string SkTombolPratinjau => SkBersama ? "Pratinjau SK Bersama" : "Pratinjau SK";

        // ---------- aksi ----------

        // ---------- tanggal teks dd-mm-yyyy ----------
        // Aturan bacanya dipusatkan di <see cref="TanggalIsian"/> supaya seluruh
        // form yang tanggalnya diketik memakai pola dan pesan galat yang sama.

        private static string TanggalKeTeks(DateTime? tanggal) => TanggalIsian.KeTeks(tanggal);

        private static bool BacaTanggal(string? teks, string label, out DateTime? tanggal, out string pesan) =>
            TanggalIsian.Baca(teks, label, out tanggal, out pesan);

        /// <summary>
        /// Periksa seluruh isian tanggal form sekaligus, supaya semua kesalahan
        /// ditampilkan bersamaan. Mengembalikan false disertai pesan gabungan bila
        /// ada yang tidak sah.
        /// </summary>
        private bool BacaTanggalForm(out DateTime? tanggalLahir, out DateTime? tanggalSk,
            out DateTime? tanggalRekomendasi, out DateTime? mulai, out DateTime? selesai, out string pesan)
        {
            var kekurangan = new List<string>();

            if (!BacaTanggal(_formTanggalLahir, "Tanggal Lahir", out tanggalLahir, out string p1))
                kekurangan.Add(p1);
            if (!BacaTanggal(_formTanggalSk, "Tanggal SK", out tanggalSk, out string p2))
                kekurangan.Add(p2);
            if (!BacaTanggal(_formTanggalRekomendasi, "Tgl. Rekomendasi", out tanggalRekomendasi, out string p3))
                kekurangan.Add(p3);
            if (!BacaTanggal(_formMasaJabatanMulai, "Masa Jabatan Mulai", out mulai, out string p4))
                kekurangan.Add(p4);
            if (!BacaTanggal(_formMasaJabatanSelesai, "Masa Jabatan Selesai", out selesai, out string p5))
                kekurangan.Add(p5);

            pesan = string.Join(Environment.NewLine, kekurangan);
            return kekurangan.Count == 0;
        }

        /// <summary>
        /// Muat daftar. Dipanggil setelah halaman tampil, jadi
        /// tidak perlu menunggu pekerjaan berat sebelum menavigasi.
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;

                // Sekali baca: seluruh data dipanggil ke memori, lalu penyusunan
                // baris dijalankan di sini. Halaman ini tidak memakai pencarian
                // maupun filter; seluruh data selalu tampil apa adanya.
                _semua = await _layanan.AmbilSemuaAsync().ConfigureAwait(true);

                HitungJumlah(_semua);
                OnPropertyChanged(nameof(DatabaseKosong));
                SusunBaris();
                OnPropertyChanged(nameof(StatusBagianAktif));
                OnPropertyChanged(nameof(TampilkanKosong));
                OnPropertyChanged(nameof(KosongKarenaBelumAdaData));

                StatusText = SusunStatusText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data perangkat desa");
                StatusText = "Gagal memuat data perangkat desa.";
                await _messageService.ShowErrorAsync("Gagal memuat data perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Hitung jumlah orang tiap kelompok dari salinan data yang baru dibaca.
        /// Angka ini dipakai <see cref="DatabaseKosong"/> untuk membedakan
        /// "database memang kosong" dari "bagian yang tampil belum ada isinya" —
        /// bukan untuk kartu ringkasan, sebab halaman ini tidak memakai kartu KPI.
        /// </summary>
        private void HitungJumlah(IReadOnlyList<PerangkatDesa> semua)
        {
            _jumlahOrangResmi = semua.Count(p => JabatanPerangkat.AdalahResmi(p.Jabatan));
            _jumlahOrangRtRw = HitungKelompok(semua, "RT/RW");
            _jumlahOrangBpd = HitungKelompok(semua, "BPD");
            _jumlahOrangLinmas = HitungKelompok(semua, "LINMAS");
            _jumlahOrangPosyandu = HitungKelompok(semua, "POSYANDU");
            _jumlahOrangKaderKpm = HitungKelompok(semua, "KADER KPM");
            _jumlahOrangKaderStunting = HitungKelompok(semua, "KADER STUNTING");
            _jumlahOrangStaf = HitungKelompok(semua, "STAF DESA");
            _jumlahOrangOperator = HitungKelompok(semua, "OPERATOR DESA");
            _jumlahOrangPkk = HitungKelompok(semua, "PKK");
            _jumlahOrangLpm = HitungKelompok(semua, "LPM");
            _jumlahOrangKarangTaruna = HitungKelompok(semua, "KARANG TARUNA");
        }

        private static int HitungKelompok(IReadOnlyList<PerangkatDesa> semua, string kelompok) =>
            semua.Count(p => JabatanPerangkat.Kelompok(p.Jabatan) == kelompok);

        /// <summary>
        /// Susun baris seluruh tabel halaman: satu blok per jabatan menurut
        /// bagiannya — perangkat desa urut Kepala Desa sampai Kadus, bagian lain
        /// menurut daftar jabatnya — dan di dalam bloknya orang yang masih
        /// memegang jabatan didahulukan. Jabatan tanpa orang tetap mendapat
        /// baris kosong siap isi.
        /// </summary>
        private void SusunBaris()
        {
            var resmi = _semua
                .Where(p => JabatanPerangkat.AdalahResmi(p.Jabatan))
                .ToList();
            var rtrw = _semua
                .Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "RT/RW")
                .ToList();
            var bpd = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "BPD").ToList();
            var linmas = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "LINMAS").ToList();
            var posyandu = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "POSYANDU").ToList();
            var kaderkpm = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KADER KPM").ToList();
            var kaderstunting = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KADER STUNTING").ToList();
            var staf = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "STAF DESA").ToList();
            var op = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "OPERATOR DESA").ToList();
            var pkk = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "PKK").ToList();
            var lpm = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "LPM").ToList();
            var karang = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KARANG TARUNA").ToList();

            IsiTabel(Items, JabatanPerangkat.Resmi, resmi);
            IsiTabel(ItemsRtRw, DaftarJabatanRtRw, rtrw);
            IsiTabel(ItemsBpd, DaftarJabatanBpd, bpd);
            IsiTabel(ItemsLinmas, DaftarJabatanLinmas, linmas);
            IsiTabel(ItemsPosyandu, DaftarJabatanPosyandu, posyandu);
            IsiTabel(ItemsKaderKpm, new[] { JabatanPerangkat.KaderKPM }, kaderkpm);
            IsiTabel(ItemsKaderStunting, new[] { JabatanPerangkat.KaderStunting }, kaderstunting);
            IsiTabel(ItemsStafDesa, new[] { JabatanPerangkat.Staf }, staf);
            IsiTabel(ItemsOperatorDesa, new[] { JabatanPerangkat.OperatorDesa }, op);
            IsiTabel(ItemsPkk, DaftarJabatanPkk, pkk);
            IsiTabel(ItemsLpm, DaftarJabatanLpm, lpm);
            IsiTabel(ItemsKarangTaruna, DaftarJabatanKarangTaruna, karang);

            OnPropertyChanged(nameof(StatusRtRw));
        }

        /// <summary>
        /// Isi satu tabel halaman ini: satu blok per jabatan menurut
        /// <paramref name="urutan"/>, orang yang masih memegang jabatan
        /// didahulukan, dan jabatan tanpa orang tetap mendapat baris kosong siap
        /// isi. Dipakai tabel perangkat desa maupun tabel bagian RT/RW.
        ///
        /// Penghuni sebenarnya menentukan baris penanda: jabatan yang belum ada
        /// orangnya tetap mendapat baris kosong siap isi.
        /// </summary>
        private void IsiTabel(
            ObservableCollection<BarisPerangkatViewModel> tabel,
            IReadOnlyList<string> urutan,
            IReadOnlyList<PerangkatDesa> orang)
        {
            var semuaPerJabatan = Kelompokkan(orang);

            tabel.Clear();
            foreach (var jabatan in urutan)
            {
                if (semuaPerJabatan.TryGetValue(jabatan, out var isi))
                {
                    foreach (var p in isi) tabel.Add(new BarisPerangkatViewModel(p));
                }

                if (!semuaPerJabatan.ContainsKey(jabatan))
                {
                    tabel.Add(new BarisPerangkatViewModel(jabatan));
                }
            }

            // Jabatan di luar daftar bagian tetap ditampilkan sebagai barisnya
            // sendiri. IsiTabel mencari memakai nama konstanta, sedangkan kamus
            // dikunci oleh JabatanTampil orang itu. Nilai warisan yang tidak punya
            // padanan di JabatanPerangkat.NamaLama (mis. "KEPALA DUSUN" untuk
            // KADUS) tidak akan pernah cocok, sehingga orang tersebut HILANG dari
            // tabel, kartu angka, ekspor Excel, dan cetak PDF — padahal barisnya
            // ada di database. Baris sisa di bawah menutup kebocoran itu.
            var sudahTampil = new HashSet<string>(urutan, StringComparer.Ordinal);
            foreach (var sisa in semuaPerJabatan.Keys
                .Where(k => !sudahTampil.Contains(k))
                .OrderBy(k => k, StringComparer.CurrentCulture))
            {
                foreach (var p in semuaPerJabatan[sisa])
                {
                    tabel.Add(new BarisPerangkatViewModel(p));
                }
            }
        }

        /// <summary>Kelompokkan orang per jabatan; urut: yang masih memegang
        /// jabatan didahulukan, lalu menurut nama. Dipakai kedua tabel halaman ini.</summary>
        private static Dictionary<string, List<PerangkatDesa>> Kelompokkan(IEnumerable<PerangkatDesa> orang) =>
            orang
                .GroupBy(p => p.JabatanTampil, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => p.MasihMemegangJabatan)
                          .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                          .ToList(),
                    StringComparer.Ordinal);

        /// <summary>
        /// Pesan daftar sesuai keadaan sebenarnya: jumlah orang yang tampil dan
        /// jabatan yang belum ada orangnya pada bagian yang sedang tampil.
        /// </summary>
        private string SusunStatusText()
        {
            if (DatabaseKosong)
            {
                return "Belum ada data perangkat desa maupun kelompok lain. "
                     + $"Tekan {LabelTambahAktif} untuk mulai mengisi bagian ini.";
            }

            // Pesan mengikuti bagian yang sedang tampil, sebab tiap bagian
            // dipilih lewat tombol dan tidak diperlihatkan bersamaan.
            var tabel = TabelAktif;
            string namaBagian = NamaBagianAktif;

            int orang = tabel.Count(b => b.AdaData);
            int kosong = tabel.Count(b => b.IsBaru);
            string pesan = orang == 0
                ? $"Belum ada {namaBagian} yang tampil"
                : $"{orang} orang {namaBagian} ditampilkan";
            if (kosong > 0)
            {
                pesan += $", {kosong} jabatan belum ada orangnya";
            }

            return pesan + ".";
        }

        /// <summary>
        /// Pindah bagian halaman. Semua tabel sudah ada di memori.
        /// </summary>
        private void PilihBagian(BagianPerangkat bagian)
        {
            if (_bagianAktif == bagian) return;

            _bagianAktif = bagian;
            OnPropertyChanged(nameof(NamaBagianAktifEnum));
            OnPropertyChanged(nameof(IkonBagianAktif));
            OnPropertyChanged(nameof(TampilkanRtRw));
            OnPropertyChanged(nameof(TampilkanPerangkatDesa));
            OnPropertyChanged(nameof(TampilkanBagianStandar));
            OnPropertyChanged(nameof(ItemsAktif));
            OnPropertyChanged(nameof(JudulBagianAktif));
            OnPropertyChanged(nameof(StatusBagianAktif));
            OnPropertyChanged(nameof(TampilkanKosong));
            OnPropertyChanged(nameof(KosongKarenaBelumAdaData));
            OnPropertyChanged(nameof(JudulForm));

            // Jumlah kelompok tidak berubah saat bagian ditukar — angkanya hanya
            // perlu dihitung sekali di LoadAsync lewat HitungJumlah.
            StatusText = SusunStatusText();
            SegarkanPerintah();
        }

        // ---------- baris tabel ----------

        private async Task HapusAsync(BarisPerangkatViewModel? baris)
        {
            var data = baris?.Sumber;
            if (data is null) return;

            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Hapus Data Perangkat",
                $"Hapus data \"{data.Nama}\" ({data.JabatanTampil})?\n\n"
                + "Data surat yang sudah pernah dibuat tidak ikut berubah.");

            if (!lanjut) return;

            try
            {
                IsBusy = true;
                bool terhapus = await _layanan.HapusAsync(data.ID).ConfigureAwait(true);
                if (!terhapus)
                {
                    await _messageService.ShowWarningAsync("Data itu sudah tidak ada. Daftar akan dimuat ulang.");
                    await LoadAsync().ConfigureAwait(true);
                    return;
                }

                _activityLog.Log("PERANGKAT", $"{data.Nama} ({data.JabatanTampil})", "Hapus", $"ID {data.ID}");

                // Berkas SK terarsip ikut dibersihkan agar tidak tertinggal sebagai
                // berkas yatim di folder setelah baris datanya hilang. Hapus aman
                // dipanggil walau berkasnya sudah tidak ada atau sedang dibuka.
                BerkasSkPerangkat.Hapus(data.BerkasSK);

                await LoadAsync().ConfigureAwait(true);
                StatusText = $"Data {data.Nama} dihapus.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus perangkat desa #{ID}", data.ID);
                await _messageService.ShowErrorAsync("Gagal menghapus data perangkat.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---------- ekspor & cetak ----------

        /// <summary>
        /// Simpan baris berisi orang pada **bagian yang sedang tampil** ke berkas
        /// Excel. Kolomnya mengikuti tampilan layar; tiap bagian diekspor terpisah
        /// sebab SK tiap kelompok punya cara penerbitan berbeda.
        /// </summary>
        private async Task EksporExcelAsync()
        {
            // Bagian kosong diperiksa lebih dulu, sebelum operator diminta memilih
            // nama berkas untuk data yang tidak akan ditulis.
            var baris = TabelAktif.Where(b => b.AdaData).ToList();
            if (baris.Count == 0)
            {
                await _messageService.ShowInfoAsync(
                    $"Tidak ada data {NamaBagianAktif} yang bisa diekspor, jadi berkas tidak dibuat.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = $"Ekspor Data {NamaBagianTampil}",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx",
                FileName = $"Perangkat_Desa{SufiksNamaBerkas}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                await TulisExcelAsync(baris, sfd.FileName, JudulDokumenAktif, desa?.NamaDesa ?? "-")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{baris.Count} baris {NamaBagianAktif}", "Ekspor Excel", sfd.FileName);
                StatusText = $"{baris.Count} baris {NamaBagianAktif} diekspor ke {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync(
                    $"Berhasil mengekspor {baris.Count} baris data {NamaBagianAktif}.\n\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor data perangkat desa");
                await _messageService.ShowErrorAsync("Gagal mengekspor data perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Kolom ekspor mengikuti berkas "DATA PERANGKAT" contoh pemakai persis —
        /// 18 kolom dengan urutan yang sama; nilai kosong dicetak "-" seperti
        /// berkasnya, dan kolom NIK/rekening ditulis sebagai teks.
        /// </summary>
        private static readonly string[] HeaderEkspor =
        {
            "DESA", "NAMA PERANGKAT", "JABATAN", "JENIS KELAMIN", "TEMPAT LAHIR",
            "TGL. LAHIR", "NIK", "AGAMA", "NO. TELPON", "NOMOR REKOMENDASI",
            "TGL. REKOMENDASI", "NO SK PENGANGKATAN", "TGL SK PENGANGKATAN",
            "PENDIDIKAN", "TAHUN LULUS", "NO. IJASAH", "NOREK BJB", "BPJS KES"
        };

        /// <summary>
        /// Nomor berurutan per jabatan berbeda, meniru kolom JABATAN pada berkas
        /// contoh (lima Kepala Dusun sama-sama bernomor "8.", dst.).
        /// </summary>
        private static string JabatanBernomor(string jabatan, Dictionary<string, int> nomor)
        {
            if (!nomor.TryGetValue(jabatan, out int n))
            {
                n = nomor.Count + 1;
                nomor[jabatan] = n;
            }

            return $"{n}. {jabatan}";
        }

        private static async Task TulisExcelAsync(
            IReadOnlyList<BarisPerangkatViewModel> baris, string path, string judulLembar, string namaDesa)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add(judulLembar);

            for (int col = 0; col < HeaderEkspor.Length; col++)
            {
                var cell = ws.Cells[1, col + 1];
                cell.Value = HeaderEkspor[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(37, 99, 235));
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }
            ws.Row(1).Height = 26;

            int rowIdx = 2;
            var nomorJabatan = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in baris)
            {
                ws.Cells[rowIdx, 1].Value = namaDesa;
                ws.Cells[rowIdx, 2].Value = b.Nama;
                ws.Cells[rowIdx, 3].Value = JabatanBernomor(b.JabatanTampil, nomorJabatan);
                ws.Cells[rowIdx, 4].Value = b.JenisKelamin;
                ws.Cells[rowIdx, 5].Value = b.TempatLahir;
                ws.Cells[rowIdx, 6].Value = b.TanggalLahir;
                // NIK sebagai teks supaya angka awal nol tidak hilang di Excel.
                ws.Cells[rowIdx, 7].Value = string.IsNullOrWhiteSpace(b.NIK) ? "-" : b.NIK;
                ws.Cells[rowIdx, 8].Value = b.Agama;
                ws.Cells[rowIdx, 9].Value = string.IsNullOrWhiteSpace(b.NomorHP) ? "-" : b.NomorHP;
                ws.Cells[rowIdx, 10].Value = b.NomorRekomendasi;
                ws.Cells[rowIdx, 11].Value = b.TanggalRekomendasi;
                ws.Cells[rowIdx, 12].Value = b.NomorSk;
                ws.Cells[rowIdx, 13].Value = b.TanggalPengangkatan;
                ws.Cells[rowIdx, 14].Value = b.Pendidikan;
                ws.Cells[rowIdx, 15].Value = b.TahunLulus;
                ws.Cells[rowIdx, 16].Value = b.NoIjasah;
                ws.Cells[rowIdx, 17].Value = b.NomorRekening;
                ws.Cells[rowIdx, 18].Value = b.NomorBpjs;

                if (rowIdx % 2 == 1)
                {
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.BackgroundColor
                        .SetColor(Color.FromArgb(248, 250, 252));
                }

                rowIdx++;
            }

            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].AutoFitColumns(10, 45);
            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            await package.SaveAsAsync(new FileInfo(path));
        }

        /// <summary>
        /// Cetak daftar **bagian yang sedang tampil** (sama persis dengan layar,
        /// termasuk baris jabatan yang belum terisi) ke PDF, lalu tampilkan pada
        /// pratinjau PDFium. Tiap bagian dicetak terpisah sebab SK tiap kelompok
        /// punya cara penerbitan berbeda.
        /// </summary>
        private async Task CetakPdfAsync()
        {
            try
            {
                IsBusy = true;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                var data = SusunDataCetak(desa);

                using var stream = new MemoryStream();
                _strukturGenerator.GeneratePdf(stream, data);
                byte[] isi = stream.ToArray();
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat PDF atau PDF kosong.");
                    return;
                }

                // Pratinjau ditulis ke folder TempPDF supaya ikut dibersihkan
                // TempPdfCleanup seperti keluaran lain; PDFium membutuhkan
                // jalur penuh — nama berkas saja membuat dokumen gagal dimuat.
                string folder = _appConfig.TempPdfFolder;
                Directory.CreateDirectory(folder);
                string jalur = Path.Combine(folder,
                    $"PerangkatDesa{SufiksNamaBerkas}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(jalur, isi).ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{data.JumlahOrang} orang {NamaBagianAktif}", "Cetak PDF", jalur);
                StatusText = $"Daftar {NamaBagianTampil} ({data.JumlahOrang} orang) dibuka di pratinjau PDF.";

                _navigation.Navigate(_previewFactory($"Daftar {NamaBagianTampil}", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF daftar perangkat desa");
                await _messageService.ShowErrorAsync("Gagal membuat PDF daftar perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Cetak formulir pendataan kosong: blanko 18 kolom berkas "DATA PERANGKAT"
        /// (DESA s.d. BPJS KES) yang dipakai menarik data perangkat baru di lapangan,
        /// lalu buka hasilnya di pratinjau PDFium agar bisa dicetak/diperbanyak.
        /// </summary>
        private async Task CetakFormulirAsync()
        {
            try
            {
                IsBusy = true;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                var data = new FormulirPerangkatData
                {
                    Desa = desa,
                    TanggalCetak = DateTime.Now
                };

                using var stream = new MemoryStream();
                _formulirGenerator.GeneratePdf(stream, data);
                byte[] isi = stream.ToArray();
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat formulir atau PDF kosong.");
                    return;
                }

                // Sama seperti cetak daftar: berkas ditulis ke TempPDF supaya ikut
                // dibersihkan TempPdfCleanup, lalu dibuka di pratinjau PDFium.
                string folder = _appConfig.TempPdfFolder;
                Directory.CreateDirectory(folder);
                string jalur = Path.Combine(folder,
                    $"FormulirPerangkat_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(jalur, isi).ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", "Formulir pendataan (18 kolom DATA PERANGKAT)",
                    "Cetak Formulir", jalur);
                StatusText = "Formulir pendataan perangkat desa dibuka di pratinjau PDF.";

                _navigation.Navigate(_previewFactory("Formulir Pendataan Perangkat Desa", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat formulir pendataan perangkat desa");
                await _messageService.ShowErrorAsync(
                    "Gagal membuat formulir pendataan perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Baris cetak dari **bagian yang sedang tampil**, mengikuti kolom berkas
        /// "DATA PERANGKAT" contoh: hanya orang yang tercatat (baris jabatan kosong
        /// tidak dicetak sebagai baris — jumlahnya tampil pada ringkasan di atas).
        /// </summary>
        private StrukturPerangkatData SusunDataCetak(DesaData desa)
        {
            var baris = TabelAktif.Where(b => b.AdaData).ToList();
            var nomorJabatan = new Dictionary<string, int>(StringComparer.Ordinal);

            return new StrukturPerangkatData
            {
                Desa = desa,
                Judul = JudulDokumenAktif,
                TanggalCetak = DateTime.Now,
                Baris = baris.Select(b => new StrukturPerangkatBaris
                {
                    Jabatan = JabatanBernomor(b.JabatanTampil, nomorJabatan),
                    Nama = b.Nama,
                    JenisKelamin = b.JenisKelamin,
                    TempatLahir = b.TempatLahir,
                    TanggalLahir = b.TanggalLahir,
                    Nik = string.IsNullOrWhiteSpace(b.NIK) ? "-" : b.NIK,
                    Agama = b.Agama,
                    NoTelp = string.IsNullOrWhiteSpace(b.NomorHP) ? "-" : b.NomorHP,
                    NomorRekomendasi = b.NomorRekomendasi,
                    TanggalRekomendasi = b.TanggalRekomendasi,
                    NomorSk = b.NomorSk,
                    TanggalSk = b.TanggalPengangkatan,
                    Pendidikan = b.Pendidikan,
                    TahunLulus = b.TahunLulus,
                    NoIjasah = b.NoIjasah,
                    NoRekBjb = b.NomorRekening,
                    BpjsKes = b.NomorBpjs
                }).ToList(),
                JumlahOrang = baris.Count,
                JumlahJabatanKosong = TabelAktif.Count(b => b.IsBaru)
            };
        }

        // ---------- panel surat keputusan per jabatan ----------

        /// <summary>
        /// Buka panel SK untuk satu baris. Baris kosong pun membuka panel: SK
        /// dicetak sebagai contoh (nama dan tanggal bergaris titik-titik) dengan
        /// jabatan baris itu sudah terpilih, sehingga operator bisa melihat
        /// bentuk dokumennya lebih dulu.
        /// </summary>
        private async Task BukaPanelSkAsync(BarisPerangkatViewModel? baris)
        {
            if (baris is null) return;
            await Task.Yield();

            IsFormTerbuka = false;
            SkGalat = string.Empty;
            // Pilihan jabatan panel dibatasi bagian baris ini, supaya SK yang
            // disusun tetap milik kelompok yang bersangkutan.
            _daftarSkJabatan = JabatanBagian(_bagianAktif);
            OnPropertyChanged(nameof(PilihanJabatanSk));
            MuatIsianSk(baris.JabatanTampil);
            IsPanelSkTerbuka = true;
        }

        private void TutupPanelSk()
        {
            IsPanelSkTerbuka = false;
            SkGalat = string.Empty;
        }

        /// <summary>
        /// Isi panel dari data jabatan terpilih: orang yang masih memegang
        /// jabatan didahulukan, lalu nomor/tanggal SK dan masa jabatannya
        /// dibawa serta supaya tidak perlu diketik ulang. Jabatan tanpa orang
        /// membuka mode contoh.
        /// </summary>
        private void MuatIsianSk(string? jabatan)
        {
            string normal = JabatanPerangkat.Normalisasi(jabatan);
            if (!string.Equals(_skJabatan, normal, StringComparison.Ordinal))
            {
                _skJabatan = normal;
                OnPropertyChanged(nameof(SkJabatan));
            }

            // Panel dibuka/dipindah jabatan: mode SK bersama dimulai ulang supaya
            // daftar nama jabatan lama tidak terbawa ke jabatan yang baru.
            SkBersama = false;
            var orang = _semua
                .Where(p => string.Equals(p.JabatanTampil, normal, StringComparison.Ordinal))
                .OrderByDescending(p => p.MasihMemegangJabatan)
                .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                .FirstOrDefault();

            _skIdPerangkat = orang?.ID ?? 0;
            _skBerkasSk = orang?.BerkasSK;
            SkContoh = orang is null;
            SkNomor = orang?.NomorSK ?? string.Empty;
            SkTanggal = TanggalKeTeks(orang?.TanggalSK);
            SkMulai = TanggalKeTeks(orang?.MasaJabatanMulai);
            SkSelesai = TanggalKeTeks(orang?.MasaJabatanSelesai);
            SkNama = orang?.Nama ?? string.Empty;
            SkNik = orang?.NIK ?? string.Empty;
            SkWilayah = orang is null || orang.WilayahRingkas == "-" ? string.Empty : orang.WilayahRingkas;

            // Pindah JABATAN memang memulai isian dari orang jabatan itu. Tetapi
            // bila jabatannya SAMA (panel dibuka ulang, atau ComboBox memilih
            // item yang sama), isian operator jangan dibuang: dulu SkAlasan
            // dikosongkan dan SkJenis dipaksa kembali ke Pemberhentian /
            // Pengangkatan, sehingga alasan pemberhentian yang sudah diketik
            // hilang dan pilihan "Penetapan" (KPM) terbalik sendiri tanpa
            // operator menyentuhnya.
            bool pindahJabatan = !string.Equals(_skJabatanSebelumnya, normal, StringComparison.Ordinal);
            _skJabatanSebelumnya = normal;

            if (pindahJabatan)
            {
                SkAlasan = string.Empty;
            }

            SkJenis = pindahJabatan
                ? (orang is { StatusTampil: StatusPerangkat.Berhenti }
                    ? nameof(SkJenisPerangkat.Pemberhentian)
                    : nameof(SkJenisPerangkat.Pengangkatan))
                : SkJenis;

            // Kelompok yang SK-nya selalu bersama (Linmas, Posyandu, Kader KPM)
            // langsung memakai mode bersama — SK per orang tidak pernah dibuat
            // untuk kelompok ini.
            SkBersama = SkWajibBersama;

            SegarkanPanelSk();
        }

        /// <summary>Segarkan seluruh turunan yang berubah bersama isian panel.</summary>
        private void SegarkanPanelSk()
        {
            OnPropertyChanged(nameof(JudulPanelSk));
            OnPropertyChanged(nameof(SkDariBupati));
            OnPropertyChanged(nameof(SkAdaBerkas));
            OnPropertyChanged(nameof(SkNamaBerkas));
            OnPropertyChanged(nameof(SkKeteranganJabatan));
            OnPropertyChanged(nameof(SkAdaTemplate));
            OnPropertyChanged(nameof(SkCatatanTemplate));
            OnPropertyChanged(nameof(SkDasarHukum));
            OnPropertyChanged(nameof(SkArsipJudul));
            OnPropertyChanged(nameof(SkArsipTombol));
            OnPropertyChanged(nameof(SkArsipKeterangan));
            OnPropertyChanged(nameof(SkLokasiArsip));
            OnPropertyChanged(nameof(SkBisaBersama));
            OnPropertyChanged(nameof(SkCatatanBersama));
            OnPropertyChanged(nameof(SkLampiranCatatan));
            OnPropertyChanged(nameof(SkLampiranRingkasan));
            OnPropertyChanged(nameof(SkPesanLampiran));
            OnPropertyChanged(nameof(SkTombolPratinjau));
            OnPropertyChanged(nameof(SkWajibBersama));
            OnPropertyChanged(nameof(SkBersamaDiubah));
            SegarkanPerintah();
        }

        /// <summary>
        /// Calon baris lampiran untuk satu jabatan: orang yang masih memegang
        /// jabatan (AKTIF/MENUNGGU SK). Jabatan yang tidak punya pemegang sama
        /// sekali memakai seluruh orang pada jabatan itu — ditandai
        /// <c>Cadangan</c> supaya panel menjelaskannya — agar operator tidak
        /// buntu saat datanya sudah lama.
        /// </summary>
        private (List<PerangkatDesa> Orang, bool Cadangan) CalonLampiran(string jabatan)
        {
            if (string.IsNullOrWhiteSpace(jabatan))
            {
                return (new List<PerangkatDesa>(), false);
            }

            var semua = _semua
                .Where(p => string.Equals(p.JabatanTampil, jabatan, StringComparison.Ordinal))
                .ToList();
            var pemegang = semua.Where(p => p.MasihMemegangJabatan).ToList();
            bool cadangan = pemegang.Count == 0 && semua.Count > 0;

            var orang = (cadangan ? semua : pemegang)
                .OrderByDescending(p => p.MasihMemegangJabatan)
                .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                .ToList();
            return (orang, cadangan);
        }

        /// <summary>
        /// Susun daftar nama lampiran SK bersama untuk jabatan terpilih: orangnya
        /// diambil dari data halaman (bukan dari database), sedangkan kolom pribadi
        /// dilengkapi <see cref="SkPerangkatLampiranService"/> lewat NIK sekali baca.
        /// </summary>
        private async Task MuatLampiranBersamaAsync()
        {
            int versi = ++_skLampiranVersi;
            string jabatan = _skJabatan;
            var calon = CalonLampiran(jabatan);
            _skLampiranCadangan = calon.Cadangan;

            SkLampiran.Clear();
            SegarkanTurunanLampiran();
            if (calon.Orang.Count == 0) return;

            try
            {
                SkLampiranMemuat = true;
                string kelompok = JabatanPerangkat.Kelompok(jabatan);
                var sumber = calon.Orang
                    .Select(o => new LampiranSumber
                    {
                        Orang = o,
                        Peran = PeranLampiran(kelompok),
                        // Unit disusun sama dengan SK jadi saat menyimpan, supaya
                        // blok lampiran di panel dan di dokumen selalu sama.
                        Unit = UnitLampiran(kelompok, o)
                    })
                    .ToList();

                var baris = await _lampiranService.SusunAsync(sumber).ConfigureAwait(true);

                // Hasil dibuang bila operator sudah mematikan mode bersama atau
                // berpindah jabatan saat data warga masih dibaca.
                if (versi != _skLampiranVersi || !SkBersama
                    || !string.Equals(_skJabatan, jabatan, StringComparison.Ordinal))
                {
                    return;
                }

                // SusunAsync mempertahankan urutan sumbernya, jadi status orangnya
                // bisa dipasangkan pada baris yang sama untuk ditampilkan di panel.
                for (int i = 0; i < baris.Count && i < sumber.Count; i++)
                {
                    SkLampiran.Add(new LampiranBarisViewModel(
                        baris[i], HapusBarisLampiran, sumber[i].Orang.StatusTampil));
                }

                _activityLog.Log("PERANGKAT", jabatan, "Daftar lampiran SK bersama",
                    $"{SkLampiran.Count} nama");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun lampiran SK bersama {Jabatan}", jabatan);
                SkGalat = "Gagal menyusun daftar nama lampiran. Penyebabnya: " + ex.Message;
            }
            finally
            {
                // Hanya pemuatan terbaru yang boleh menutup penanda "memuat";
                // pemuatan lama yang kalah balapan tidak menyentuhnya.
                if (versi == _skLampiranVersi)
                {
                    SkLampiranMemuat = false;
                    SegarkanTurunanLampiran();
                }
            }
        }

        /// <summary>Buang satu baris dari lampiran tanpa mengubah data perangkatnya.</summary>
        private void HapusBarisLampiran(LampiranBarisViewModel baris)
        {
            if (baris is null) return;
            SkLampiran.Remove(baris);
            SegarkanTurunanLampiran();
        }

        /// <summary>Segarkan seluruh turunan daftar lampiran SK bersama.</summary>
        private void SegarkanTurunanLampiran()
        {
            OnPropertyChanged(nameof(SkJumlahLampiran));
            OnPropertyChanged(nameof(SkBisaBersama));
            OnPropertyChanged(nameof(SkLampiranRingkasan));
            OnPropertyChanged(nameof(SkLampiranCatatan));
            OnPropertyChanged(nameof(SkPesanLampiran));
            OnPropertyChanged(nameof(SkTombolPratinjau));
            SegarkanPerintah();
        }

        /// <summary>
        /// Cetak SK jabatan terpilih ke PDF lalu tampilkan di pratinjau PDFium.
        /// Isian yang kosong (atau baris tanpa orang) dicetak bergaris titik-titik
        /// oleh generator, jadi dokumen tetap terbaca.
        /// </summary>
        private async Task PratinjauSkAsync()
        {
            try
            {
                IsBusy = true;
                SkGalat = string.Empty;

                string kelompok = JabatanPerangkat.Kelompok(_skJabatan);
                if (!SkPerangkatKatalog.Ada(kelompok))
                {
                    SkGalat = $"Kelompok {kelompok} belum punya template SK bawaan, jadi dokumen belum bisa dibuat.";
                    return;
                }

                // Isian tanggal panel berupa teks; yang tidak sah ditolak di sini
                // supaya dokumen tidak tercetak dengan tanggal salah.
                if (!BacaTanggal(_skTanggal, "Tanggal SK", out DateTime? tanggalSk, out string pesanTanggal)
                    || !BacaTanggal(_skMulai, "Masa Jabatan Mulai", out DateTime? mulai, out pesanTanggal)
                    || !BacaTanggal(_skSelesai, "Masa Jabatan Selesai", out DateTime? selesai, out pesanTanggal))
                {
                    SkGalat = pesanTanggal;
                    return;
                }

                // SK bersama mencetak nama-namanya di halaman lampiran.
                bool bersama = SkBersama;
                var lampiran = bersama
                    ? SkLampiran.Select(b => b.KeBaris()).ToList()
                    : new List<BarisLampiranSk>();
                if (bersama && lampiran.Count == 0 && SkBisaBersama)
                {
                    SkGalat = "Belum ada nama pada lampiran SK bersama, jadi dokumen belum bisa dibuat. "
                            + "Muat ulang daftar namanya terlebih dahulu.";
                    return;
                }

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);

                var isi = new SkPerangkatIsi
                {
                    Jenis = JenisSkTerpilih(),
                    Kelompok = kelompok,
                    Nomor = SkNomor,
                    Tanggal = tanggalSk ?? DateTime.Today,
                    // Pada SK bersama, yang mencetak nama adalah lampirannya.
                    Nama = bersama ? string.Empty : SkNama,
                    NIK = bersama ? string.Empty : SkNik,
                    Jabatan = _skJabatan,
                    Wilayah = bersama ? string.Empty : SkWilayah,
                    Mulai = mulai,
                    Selesai = selesai,
                    // Pratinjau SK juga wajib punya alasan supaya dokumen yang
                    // dilihat operator sama sahnya dengan yang terbit otomatis.
                    Alasan = JenisSkTerpilih() == SkJenisPerangkat.Pemberhentian
                        ? (string.IsNullOrWhiteSpace(SkAlasan)
                            ? SkPerangkatKatalog.AlasanPemberhentianKosong
                            : "karena " + SkAlasan.Trim().TrimEnd('.'))
                        : SkAlasan,
                    Lampiran = lampiran,
                    PenandaDiktum = SkPerangkatKatalog.PenandaDiktum(kelompok),
                    Memperhatikan = SkPerangkatKatalog.Memperhatikan(kelompok),
                    Contoh = SkContoh
                };

                using var stream = new MemoryStream();
                _skGenerator.GenerateSkPdf(stream, desa, isi);
                byte[] pdf = stream.ToArray();
                if (pdf.Length == 0)
                {
                    SkGalat = "Gagal membuat PDF atau PDF kosong.";
                    return;
                }

                // Pratinjau ditulis ke TempPDF supaya ikut dibersihkan
                // TempPdfCleanup seperti keluaran lain; PDFium butuh jalur penuh.
                string folder = _appConfig.TempPdfFolder;
                Directory.CreateDirectory(folder);
                string awalan = bersama ? "SK_Bersama" : "SK";
                string jalur = Path.Combine(folder,
                    $"{awalan}_{_skJabatan.Replace(' ', '_')}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(jalur, pdf).ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{_skJabatan}",
                    bersama ? "Pratinjau SK bersama" : "Pratinjau SK", jalur);
                StatusText = bersama
                    ? $"SK bersama {_skJabatan} ({lampiran.Count} nama pada lampiran) dibuka di pratinjau PDF."
                    : $"SK {_skJabatan} dibuka di pratinjau PDF.";
                _navigation.Navigate(_previewFactory(
                    bersama
                        ? $"SK Bersama {NamaJabatanTampil(_skJabatan)}"
                        : $"SK {NamaJabatanTampil(_skJabatan)}", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat SK {Jabatan}", _skJabatan);
                SkGalat = "Gagal membuat SK. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private SkJenisPerangkat JenisSkTerpilih() =>
            Enum.TryParse<SkJenisPerangkat>(_skJenis, out var jenis)
                ? jenis
                : SkJenisPerangkat.Pengangkatan;

        /// <summary>
        /// Salin berkas PDF SK dari luar ke folder arsip, lalu simpan namanya di
        /// data orang yang sedang ditampilkan panel: SK Bupati untuk Kepala Desa,
        /// atau SK bertanda tangan Kepala Desa untuk jabatan lain.
        /// </summary>
        private async Task ArsipkanSkAsync()
        {
            var orang = _semua.FirstOrDefault(p => p.ID == _skIdPerangkat);
            if (orang is null)
            {
                SkGalat = "Baris ini belum punya orang, jadi berkas SK belum bisa diarsipkan. "
                        + "Isi dulu datanya lewat form, lalu arsipkan berkas SK-nya.";
                return;
            }

            var ofd = new OpenFileDialog
            {
                Title = $"Pilih berkas PDF SK untuk {orang.JabatanTampil}",
                Filter = "Berkas PDF (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog() != true) return;

            string? namaBaru = null;
            try
            {
                IsBusy = true;
                SkGalat = string.Empty;

                namaBaru = BerkasSkPerangkat.Arsipkan(orang.ID, ofd.FileName);
                orang.BerkasSK = namaBaru;
                var hasil = await _layanan.SimpanAsync(orang, modeUbah: true).ConfigureAwait(true);
                if (!hasil.Berhasil)
                {
                    BerkasSkPerangkat.Hapus(namaBaru);
                    SkGalat = hasil.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, hasil.Kekurangan)
                        : hasil.Pesan ?? "Berkas SK belum bisa disimpan.";
                    return;
                }

                _skBerkasSk = namaBaru;
                _activityLog.Log("PERANGKAT", $"{orang.Nama} ({orang.JabatanTampil})",
                    "Arsip SK", namaBaru);
                await LoadAsync().ConfigureAwait(true);
                SegarkanPanelSk();
                StatusText = $"Berkas SK {orang.JabatanTampil} diarsipkan.";
                await _messageService.ShowInfoAsync($"Berkas SK berhasil diarsipkan.\n\n{namaBaru}");
            }
            catch (Exception ex)
            {
                if (namaBaru != null) BerkasSkPerangkat.Hapus(namaBaru);
                _logger.LogError(ex, "Gagal mengarsipkan berkas SK Bupati");
                SkGalat = "Gagal mengarsipkan berkas SK. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Tampilkan berkas SK Bupati yang sudah diarsipkan pada pratinjau PDFium.</summary>
        private void BukaBerkasSk()
        {
            string? jalur = BerkasSkPerangkat.JalurLengkap(_skBerkasSk);
            if (jalur is null)
            {
                SkGalat = "Berkas SK tidak ditemukan di folder arsip. Lampirkan ulang berkasnya.";
                return;
            }

            _navigation.Navigate(_previewFactory($"SK {NamaJabatanTampil(_skJabatan)}", jalur));
        }

        /// <summary>"KAUR UMUM DAN TATA USAHA" → "Kaur Umum Dan Tata Usaha" untuk judul panel.</summary>
        private static string NamaJabatanTampil(string jabatan) =>
            string.Join(" ", JabatanPerangkat.Normalisasi(jabatan)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(kata => char.ToUpperInvariant(kata[0]) + kata[1..].ToLowerInvariant()));

        // ---------- form tambah/ubah ----------

        private Task BukaFormTambahAsync() => BukaFormTambah(JabatanPerangkat.KepalaDesa);

        /// <summary>
        /// Buka form data diri mode tambah pada bagian yang sedang tampil,
        /// dengan jabatan awal bagian itu. Daftar jabatan form dibatasi
        /// bagiannya, jadi tambah orang tidak pernah menyatu dengan
        /// jabatan bagian lain.
        /// </summary>
        private async Task BukaFormTambah(string jabatanAwal)
        {
            await Task.Yield();

            // Form data diri dan panel SK sama-sama overlay; jangan bertumpuk.
            IsPanelSkTerbuka = false;
            KosongkanForm();
            IsModeUbah = false;

            var daftar = JabatanBagian(_bagianAktif);
            AturDaftarJabatanForm(daftar);
            // Jabatan awal bisa berasal dari pemanggil lain (mis. tombol Tambah
            // Perangkat pada bagian lain lewat panel kosong); bila tidak termasuk
            // bagian ini, pakai jabatan pertama bagian itu.
            FormJabatan = daftar.Contains(jabatanAwal, StringComparer.Ordinal)
                ? jabatanAwal
                : daftar.FirstOrDefault() ?? jabatanAwal;
            FormStatus = StatusPerangkat.Aktif;
            FormGalat = string.Empty;
            IsFormTerbuka = true;
        }

        /// <summary>
        /// Buka form dari satu baris: baris kosong berarti tambah baru dengan
        /// jabatannya sudah terpilih, baris berisi orang berarti ubah data
        /// orang itu.
        /// </summary>
        private async Task BukaFormDariBarisAsync(BarisPerangkatViewModel? baris)
        {
            if (baris is null) return;
            await Task.Yield();

            IsPanelSkTerbuka = false;
            // Pilihan jabatan dibatasi bagian baris ini, baik untuk menambah
            // (baris kosong) maupun mengubah orang yang sudah ada.
            AturDaftarJabatanForm(JabatanBagianJabatan(baris.JabatanTampil));

            if (baris.Sumber is null)
            {
                KosongkanForm();
                IsModeUbah = false;
                FormJabatan = baris.JabatanTampil;
                FormStatus = StatusPerangkat.Aktif;
                FormGalat = string.Empty;
                IsFormTerbuka = true;
                return;
            }

            BukaFormUbah(baris.Sumber);
        }

        private void BukaFormUbah(PerangkatDesa data)
        {
            _formId = data.ID;
            _formBerkasSk = data.BerkasSK;
            _formBerkasSkSumber = null;
            IsModeUbah = true;
            FormNama = data.Nama;
            FormJabatan = data.JabatanTampil;
            FormNip = data.NIP ?? string.Empty;
            FormNik = data.NIK ?? string.Empty;
            FormJenisKelamin = PerangkatDesa.JenisKelaminRingkas(data.JenisKelamin);
            FormTempatLahir = data.TempatLahir ?? string.Empty;
            FormTanggalLahir = TanggalKeTeks(data.TanggalLahir);
            FormPendidikan = data.Pendidikan ?? string.Empty;
            FormTahunLulus = data.TahunLulus ?? string.Empty;
            FormNoIjasah = data.NoIjasah ?? string.Empty;
            FormAgama = data.Agama ?? string.Empty;
            FormAlamat = data.Alamat ?? string.Empty;
            FormDusun = data.Dusun ?? string.Empty;
            FormRt = data.RT ?? string.Empty;
            FormRw = data.RW ?? string.Empty;
            FormUnit = data.Unit ?? string.Empty;
            FormNomorHp = data.NomorHP ?? string.Empty;
            FormWhatsApp = data.WhatsApp ?? string.Empty;
            FormNomorRekening = data.NomorRekening ?? string.Empty;
            FormNomorBpjs = data.NomorBPJS ?? string.Empty;
            FormNomorSk = data.NomorSK ?? string.Empty;
            FormTanggalSk = TanggalKeTeks(data.TanggalSK);
            FormNomorRekomendasi = data.NomorRekomendasi ?? string.Empty;
            FormTanggalRekomendasi = TanggalKeTeks(data.TanggalRekomendasi);
            FormMasaJabatanMulai = TanggalKeTeks(data.MasaJabatanMulai);
            FormMasaJabatanSelesai = TanggalKeTeks(data.MasaJabatanSelesai);
            FormStatus = data.StatusTampil;
            FormCatatan = data.Catatan ?? string.Empty;
            FormGalat = string.Empty;
            SegarkanFormSk();

            IsFormTerbuka = true;
        }

        private void TutupForm()
        {
            IsFormTerbuka = false;
            FormGalat = string.Empty;
            KosongkanForm();
        }

        private void KosongkanForm()
        {
            _formId = 0;
            _formBerkasSk = null;
            _formBerkasSkSumber = null;
            FormNama = string.Empty;
            CariWarga = string.Empty;
            HasilWarga.Clear();
            FormJabatan = string.Empty;
            FormNip = string.Empty;
            FormNik = string.Empty;
            FormJenisKelamin = string.Empty;
            FormTempatLahir = string.Empty;
            FormTanggalLahir = string.Empty;
            FormPendidikan = string.Empty;
            FormTahunLulus = string.Empty;
            FormNoIjasah = string.Empty;
            FormAgama = string.Empty;
            FormAlamat = string.Empty;
            FormDusun = string.Empty;
            FormRt = string.Empty;
            FormRw = string.Empty;
            FormUnit = string.Empty;
            FormNomorHp = string.Empty;
            FormWhatsApp = string.Empty;
            FormNomorRekening = string.Empty;
            FormNomorBpjs = string.Empty;
            FormNomorSk = string.Empty;
            FormTanggalSk = string.Empty;
            FormNomorRekomendasi = string.Empty;
            FormTanggalRekomendasi = string.Empty;
            FormMasaJabatanMulai = string.Empty;
            FormMasaJabatanSelesai = string.Empty;
            FormStatus = StatusPerangkat.Aktif;
            FormCatatan = string.Empty;
            SegarkanFormSk();
        }

        private async Task SimpanAsync()
        {
            // SK Bupati (Kepala Desa & anggota BPD) harus sudah dilampirkan;
            // SK jabatan lain dibuat aplikasi dari data form saat disimpan.
            bool skDariBupati = FormSkWajib;
            bool arsipLamaAda = BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null;

            if (skDariBupati)
            {
                if (_formBerkasSkSumber is null && !arsipLamaAda)
                {
                    FormGalat = "Jabatan ini mempunyai SK yang diterbitkan Bupati, jadi berkas PDF SK-nya "
                        + "wajib dilampirkan sebelum data disimpan. Tekan “Lampirkan SK” lalu pilih berkasnya.";
                    return;
                }
                if (_formBerkasSkSumber is not null && !File.Exists(_formBerkasSkSumber))
                {
                    FormGalat = "Berkas SK yang dipilih tidak ditemukan lagi. Pilih ulang berkasnya.";
                    return;
                }
            }

            // Seluruh isian tanggal form berupa teks; dibaca sekaligus di sini
            // supaya semua kesalahan format tampil bersamaan sebelum data disusun.
            if (!BacaTanggalForm(out DateTime? tanggalLahir, out DateTime? tanggalSk,
                    out DateTime? tanggalRekomendasi, out DateTime? masaJabatanMulai,
                    out DateTime? masaJabatanSelesai, out string pesanTanggal))
            {
                FormGalat = pesanTanggal;
                return;
            }

            var data = new PerangkatDesa
            {
                ID = _formId,
                Nama = FormNama,
                Jabatan = FormJabatan,
                NIP = FormNip,
                NIK = FormNik,
                JenisKelamin = FormJenisKelamin,
                TempatLahir = FormTempatLahir,
                TanggalLahir = tanggalLahir,
                Agama = FormAgama,
                Pendidikan = FormPendidikan,
                TahunLulus = FormTahunLulus,
                NoIjasah = FormNoIjasah,
                Alamat = FormAlamat,
                Dusun = FormDusun,
                RT = FormRt,
                RW = FormRw,
                Unit = FormUnit,
                NomorHP = FormNomorHp,
                WhatsApp = FormWhatsApp,
                NomorSK = FormNomorSk,
                TanggalSK = tanggalSk,
                NomorRekomendasi = FormNomorRekomendasi,
                TanggalRekomendasi = tanggalRekomendasi,
                NomorRekening = FormNomorRekening,
                NomorBPJS = FormNomorBpjs,
                // Form tidak memuat berkas, jadi nama berkas arsip dibawa dari
                // baris yang dibuka. Tanpa ini, setiap penyimpanan form akan
                // mengosongkan BerkasSK dan memutus tautan ke PDF SK tersimpan.
                BerkasSK = _formBerkasSk,
                MasaJabatanMulai = masaJabatanMulai,
                MasaJabatanSelesai = masaJabatanSelesai,
                Status = FormStatus,
                Catatan = FormCatatan,
                DiperbaruiOleh = SessionContext.Display
            };

            bool modeUbah = IsModeUbah;
            try
            {
                IsBusy = true;
                FormGalat = string.Empty;

                var hasil = await _layanan.SimpanAsync(data, modeUbah).ConfigureAwait(true);
                if (!hasil.Berhasil)
                {
                    FormGalat = hasil.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, hasil.Kekurangan)
                        : hasil.Pesan ?? "Data belum bisa disimpan.";
                    return;
                }

                _activityLog.Log("PERANGKAT", $"{hasil.Data!.Nama} ({hasil.Data.JabatanTampil})",
                    modeUbah ? "Ubah" : "Tambah",
                    modeUbah ? $"ID {hasil.Data.ID} diperbarui" : $"ID {hasil.Data.ID} ditambahkan");

                bool skDibuat = false;

                // SK Bupati (Kepala Desa & anggota BPD): salin berkas terpilih
                // ke folder arsip lalu tulis namanya pada baris yang tersimpan.
                if (skDariBupati && _formBerkasSkSumber is not null)
                {
                    string? namaArsip = null;
                    try
                    {
                        namaArsip = BerkasSkPerangkat.Arsipkan(data.ID, _formBerkasSkSumber);
                        data.BerkasSK = namaArsip;
                        var ulang = await _layanan.SimpanAsync(data, modeUbah: true).ConfigureAwait(true);
                        if (!ulang.Berhasil)
                        {
                            throw new InvalidOperationException(ulang.Kekurangan.Count > 0
                                ? string.Join(Environment.NewLine, ulang.Kekurangan)
                                : ulang.Pesan ?? "nama berkas SK belum tertulis pada data.");
                        }

                        _formBerkasSk = namaArsip;
                        _formBerkasSkSumber = null;
                        _activityLog.Log("PERANGKAT", $"{data.Nama} ({data.JabatanTampil})",
                            "Lampirkan SK Bupati", namaArsip);
                    }
                    catch (Exception ex)
                    {
                        if (namaArsip is not null)
                        {
                            BerkasSkPerangkat.Hapus(namaArsip);
                            data.BerkasSK = _formBerkasSk;
                        }

                        _logger.LogError(ex, "Gagal mengarsipkan berkas SK Bupati {Nama}", data.Nama);
                        // Datanya sudah tersimpan; lanjutkan sebagai mode ubah supaya
                        // tekan Simpan berikutnya memperbarui baris ini, bukan ganda.
                        _formId = data.ID;
                        IsModeUbah = true;
                        await LoadAsync().ConfigureAwait(true);
                        FormGalat = "Data tersimpan, tetapi berkas SK Bupati belum terarsipkan: " + ex.Message
                            + " Tekan Simpan sekali lagi untuk mengarsipkan berkasnya.";
                        return;
                    }
                }
                // SK jabatan lain: buat SK jadi dari data form (bukan contoh)
                // lalu simpan PDF-nya sebagai berkas arsip baris ini.
                else if (!skDariBupati)
                {
                    try
                    {
                        skDibuat = await BuatSkJadiAsync(data).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal membuat SK jadi {Nama} ({Jabatan})",
                            data.Nama, data.JabatanTampil);
                        _formId = data.ID;
                        IsModeUbah = true;
                        await LoadAsync().ConfigureAwait(true);
                        FormGalat = "Data tersimpan, tetapi SK jadi belum berhasil dibuat: " + ex.Message
                            + " Bila berkas SK sedang dibuka di pratinjau, tutup dulu lalu tekan Simpan sekali lagi.";
                        return;
                    }
                }

                TutupForm();
                await LoadAsync().ConfigureAwait(true);
                StatusText = modeUbah
                    ? $"Data {hasil.Data.Nama} diperbarui."
                    : $"{hasil.Data.Nama} ditambahkan sebagai {hasil.Data.JabatanTampil}.";
                if (skDibuat)
                {
                    StatusText += " SK jadi dibuat dan diarsipkan.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan perangkat desa {Nama}", data.Nama);
                FormGalat = "Data belum tersimpan. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---------- SK otomatis & lampiran SK Bupati pada form ----------

        /// <summary>
        /// Pilih berkas PDF SK Bupati pada form. Berkasnya belum disalin ke
        /// folder arsip sebab baris baru belum punya ID — penyalinan dilakukan
        /// oleh <see cref="SimpanAsync"/> tepat setelah data tersimpan.
        /// </summary>
        private async Task LampirkanSkFormAsync()
        {
            await Task.Yield();

            var ofd = new OpenFileDialog
            {
                Title = "Pilih berkas PDF SK Bupati",
                Filter = "Berkas PDF (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog() != true) return;

            _formBerkasSkSumber = ofd.FileName;
            BersihkanGalat();
            SegarkanFormSk();
        }

        /// <summary>Buka berkas SK pada form (pilihan baru atau arsip lama) di pratinjau PDFium.</summary>
        private void BukaSkForm()
        {
            string? jalur = !string.IsNullOrWhiteSpace(_formBerkasSkSumber) && File.Exists(_formBerkasSkSumber)
                ? _formBerkasSkSumber
                : BerkasSkPerangkat.JalurLengkap(_formBerkasSk);

            if (jalur is null)
            {
                FormGalat = "Berkas SK tidak ditemukan. Pilih ulang berkasnya.";
                return;
            }

            _navigation.Navigate(_previewFactory($"SK {NamaJabatanTampil(FormJabatan)}", jalur));
        }

        /// <summary>Segarkan turunan form yang bergantung pada pilihan berkas SK.</summary>
        private void SegarkanFormSk()
        {
            OnPropertyChanged(nameof(FormSkWajib));
            OnPropertyChanged(nameof(FormSkOtomatis));
            OnPropertyChanged(nameof(FormSkPesanOtomatis));
            OnPropertyChanged(nameof(FormSkNamaBerkas));
            OnPropertyChanged(nameof(FormSkAdaBerkas));
            BukaSkFormCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Buat SK jadi (bukan contoh) untuk baris yang baru tersimpan: isian
        /// memakai data orang asli dari form — nomor &amp; tanggal SK, nama, NIK,
        /// wilayah, dan masa jabatan — sedangkan tanda tangannya memakai nama
        /// Kepala Desa dari data desa (Contoh = false). PDF-nya ditulis ke folder
        /// arsip dengan nama tetap per baris lalu namanya ditulis pada kolom
        /// BerkasSK. Mengembalikan true bila SK memang dibuat.
        /// </summary>
        private async Task<bool> BuatSkJadiAsync(PerangkatDesa data)
        {
            string jabatan = data.JabatanTampil;
            string kelompok = JabatanPerangkat.Kelompok(jabatan);

            // Kelompok tanpa template (mis. LAINNYA) tidak bisa dibuatkan SK;
            // datanya tetap tersimpan tanpa berkas SK.
            if (!SkPerangkatKatalog.AdaUntuk(jabatan, kelompok)) return false;

            // SK bersama (LINMAS & POSYANDU): satu SK untuk seluruh anggota
            // jabatan ini dengan lampiran daftar nama — bukan SK per orang,
            // sama seperti contoh SK Linmas dan SK Posyandu desa.
            bool bersama = SkPerangkatKatalog.LazimBersama(kelompok);
            var peserta = bersama
                ? PesertaSkBersama(data, jabatan)
                : new List<PerangkatDesa> { data };
            var lampiran = bersama
                ? await SusunLampiranSkBersamaAsync(kelompok, peserta).ConfigureAwait(true)
                : (IReadOnlyList<BarisLampiranSk>)Array.Empty<BarisLampiranSk>();

            var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
            var template = SkPerangkatKatalog.Untuk(jabatan, kelompok);
            var isi = new SkPerangkatIsi
            {
                // Pemberhentian mengikuti status baris; kelompok yang pada
                // contoh SK-nya berbentuk penetapan (KPM) memakai Penetapan
                // — bukan pengangkatan.
                Jenis = data.StatusTampil == StatusPerangkat.Berhenti
                    ? SkJenisPerangkat.Pemberhentian
                    : template?.BisaPenetapan == true
                        ? SkJenisPerangkat.Penetapan
                        : SkJenisPerangkat.Pengangkatan,
                Kelompok = kelompok,
                Nomor = data.NomorSK ?? string.Empty,
                Tanggal = data.TanggalSK ?? DateTime.Today,
                // Pada SK bersama nama & NIK dicetak dari halaman lampiran.
                Nama = bersama ? string.Empty : data.Nama,
                NIK = bersama ? string.Empty : data.NIK ?? string.Empty,
                Jabatan = jabatan,
                Wilayah = data.WilayahRingkas == "-" ? string.Empty : data.WilayahRingkas,
                Mulai = data.MasaJabatanMulai,
                Selesai = data.MasaJabatanSelesai,

                // Alasan pemberhentian SELALU terisi. Jalur ini (simpan form)
                // tidak melewati panel SK sama sekali, jadi tanpa ini setiap SK
                // pemberhentian otomatis tercetak tanpa dasar — "… dari jabatan
                // KADUS di Desa XYZ, terhitung sejak tanggal …" — yang tidak sah.
                Alasan = data.StatusTampil == StatusPerangkat.Berhenti
                    ? SkPerangkatKatalog.KalimatAlasanPemberhentian(data.Catatan)
                    : string.Empty,

                Lampiran = lampiran,
                PenandaDiktum = SkPerangkatKatalog.PenandaDiktum(kelompok),
                Memperhatikan = SkPerangkatKatalog.Memperhatikan(kelompok),
                Contoh = false
            };

            using var stream = new MemoryStream();
            _skGenerator.GenerateSkPdf(stream, desa, isi);
            byte[] pdf = stream.ToArray();
            if (pdf.Length == 0)
            {
                throw new InvalidOperationException(
                    "PDF SK kosong. Periksa data desa pada Pengaturan, lalu coba lagi.");
            }

            // Satu berkas dipakai bersama: SK bersama ditautkan ke seluruh
            // anggota, SK biasa hanya ke baris ini.
            string namaBaru = BerkasSkPerangkat.SimpanHasilOtomatis(data.ID, pdf);
            await TulisBerkasSkAsync(peserta, namaBaru).ConfigureAwait(true);

            _activityLog.Log("PERANGKAT",
                bersama ? $"{jabatan} — {peserta.Count} anggota" : $"{data.Nama} ({jabatan})",
                "SK jadi dibuat otomatis", namaBaru);
            return true;
        }

        /// <summary>
        /// Seluruh anggota satu jabatan untuk SK bersama: orang yang masih
        /// memegang jabatan didahulukan (bila tidak ada, seluruh orangnya dipakai
        /// — sama dengan aturan lampiran panel), dan baris yang baru disimpan
        /// selalu ikut memakai data terbarunya.
        /// </summary>
        private List<PerangkatDesa> PesertaSkBersama(PerangkatDesa data, string jabatan)
        {
            var (orang, _) = CalonLampiran(jabatan);
            int indeks = orang.FindIndex(p => p.ID == data.ID);
            if (indeks >= 0)
            {
                orang[indeks] = data;
            }
            else
            {
                orang.Add(data);
            }
            return orang;
        }

        /// <summary>
        /// Susun baris lampiran SK bersama, lengkap dengan data warga lewat NIK.
        /// Pengelompokan unit: LINMAS satu daftar tanpa judul (atau mengikuti
        /// isian Unit operator); POSYANDU dikelompokkan per posyandu — isian Unit
        /// didahulukan (mis. "POSYANDU SAKURA I"), dan bila kosong diturunkan
        /// dari dusun orangnya sebab jumlah posyandu mengikuti jumlah Kepala Dusun.
        /// </summary>
        private async Task<IReadOnlyList<BarisLampiranSk>> SusunLampiranSkBersamaAsync(
            string kelompok, List<PerangkatDesa> peserta)
        {
            var sumber = peserta
                .Select(p => new LampiranSumber
                {
                    Orang = p,
                    Peran = PeranLampiran(kelompok),
                    Unit = UnitLampiran(kelompok, p)
                })
                // Urutan sumber menentukan urutan blok unit pada lampiran:
                // urut unit (tanpa unit dulu), lalu yang masih menjabat, lalu nama.
                .OrderBy(s => s.Unit, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(s => s.Orang.MasihMemegangJabatan)
                .ThenBy(s => s.Orang.Nama, StringComparer.CurrentCulture)
                .ToList();

            return await _lampiranService.SusunAsync(sumber).ConfigureAwait(true);
        }

        /// <summary>
        /// Peran awal baris lampiran: Posyandu memakai "Anggota" sebab contoh
        /// SK-nya memuat kolom Jabatan berisi Ketua/Bendahara/Anggota — operator
        /// mengganti baris yang memegang peran. Kelompok lain kosong supaya kolom
        /// Jabatan tidak muncul saat seluruh barisnya seragam.
        /// </summary>
        private static string PeranLampiran(string kelompok) =>
            string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal) ? "Anggota" : string.Empty;

        /// <summary>Unit lampiran satu orang pada SK bersama.</summary>
        private static string UnitLampiran(string kelompok, PerangkatDesa orang)
        {
            string unit = (orang.Unit ?? string.Empty).Trim();

            if (string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal))
            {
                if (unit.Length > 0) return unit;

                // Posyandu mengikuti dusun: jumlah posyandu = jumlah Kepala Dusun,
                // jadi tanpa isian Unit, bloknya diturunkan dari dusun orangnya
                // (mis. POSYANDU KALENJAYA).
                string dusun = (orang.Dusun ?? string.Empty).Trim();
                return dusun.Length > 0 ? "POSYANDU " + dusun : string.Empty;
            }

            // LINMAS (dan KPM): satu daftar tanpa judul blok, sama seperti contoh
            // SK-nya; kelompok lain tetap memakai isian unitnya.
            if (string.Equals(kelompok, "LINMAS", StringComparison.Ordinal)
                || string.Equals(kelompok, "KADER KPM", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return unit;
        }

        /// <summary>
        /// Tulis nama berkas SK pada kolom BerkasSK seluruh peserta SK. Berkas
        /// otomatis lama dibuang setelah semua baris berpindah supaya tidak ada
        /// baris yang menunjuk berkas terhapus; berkas unggahan operator tidak
        /// pernah dihapus (bisa jadi SK bertanda tangan asli) — hanya tautannya
        /// yang berganti.
        /// </summary>
        private async Task TulisBerkasSkAsync(List<PerangkatDesa> peserta, string namaBaru)
        {
            var lama = peserta
                .Select(p => p.BerkasSK)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var orang in peserta)
            {
                if (string.Equals(orang.BerkasSK ?? string.Empty, namaBaru,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orang.BerkasSK = namaBaru;
                orang.DiperbaruiOleh = SessionContext.Display;
                var ulang = await _layanan.SimpanAsync(orang, modeUbah: true).ConfigureAwait(true);
                if (!ulang.Berhasil)
                {
                    throw new InvalidOperationException(ulang.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, ulang.Kekurangan)
                        : ulang.Pesan ?? "nama berkas SK belum tertulis pada data.");
                }
            }

            foreach (var nama in lama)
            {
                if (!string.Equals(nama, namaBaru, StringComparison.OrdinalIgnoreCase)
                    && BerkasSkPerangkat.AdalahHasilOtomatis(nama))
                {
                    BerkasSkPerangkat.Hapus(nama);
                }
            }
        }

        private void SegarkanPerintah()
        {
            TambahCommand.RaiseCanExecuteChanged();
            UbahCommand.RaiseCanExecuteChanged();
            HapusCommand.RaiseCanExecuteChanged();
            SimpanCommand.RaiseCanExecuteChanged();
            EksporExcelCommand.RaiseCanExecuteChanged();
            CetakPdfCommand.RaiseCanExecuteChanged();
            CetakFormulirCommand.RaiseCanExecuteChanged();
            SkCommand.RaiseCanExecuteChanged();
            PratinjauSkCommand.RaiseCanExecuteChanged();
            ArsipkanSkCommand.RaiseCanExecuteChanged();
            BukaBerkasSkCommand.RaiseCanExecuteChanged();
            SegarkanLampiranCommand.RaiseCanExecuteChanged();
            TampilkanPerangkatDesaCommand.RaiseCanExecuteChanged();
            TampilkanRtRwCommand.RaiseCanExecuteChanged();
            TampilkanBpdCommand.RaiseCanExecuteChanged();
            TampilkanLinmasCommand.RaiseCanExecuteChanged();
            TampilkanPosyanduCommand.RaiseCanExecuteChanged();
            TampilkanKaderKpmCommand.RaiseCanExecuteChanged();
            TampilkanKaderStuntingCommand.RaiseCanExecuteChanged();
            TampilkanStafDesaCommand.RaiseCanExecuteChanged();
            TampilkanOperatorDesaCommand.RaiseCanExecuteChanged();
            TampilkanPkkCommand.RaiseCanExecuteChanged();
            TampilkanLpmCommand.RaiseCanExecuteChanged();
            TampilkanKarangTarunaCommand.RaiseCanExecuteChanged();
        }
    }

}

