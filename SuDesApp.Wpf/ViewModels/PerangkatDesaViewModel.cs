using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
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
    /// (pratinjau SudesPdf); pencarian, filter jabatan/wilayah, dan tombol Muat
    /// Ulang sengaja tidak dipakai — saringan status berlaku langsung dari
    /// salinan data yang dibaca sekali saat halaman dibuka.
    ///
    /// Setiap baris — termasuk jabatan yang belum ada orangnya — punya tombol
    /// SK di kolom AKSI yang membuka panel Surat Keputusan; panel itu juga
    /// menjadi tempat mengarsipkan berkas SK Bupati untuk Kepala Desa.
    ///
    /// Seluruh bagian memakai satu tabel, satu kepala bagian, dan satu form:
    /// memilih bagian hanya mengganti isi tabel dan angka ringkasannya, sebab
    /// tiap kelompok memang pekerjaan pengisian yang sama dan hanya berbeda
    /// cara penerbitan SK-nya.
    ///
    /// Dua aturan yang dijaga halaman ini: satu jabatan pada satu wilayah
    /// hanya boleh satu orang (validasi ada di <c>PerangkatDesaService</c>),
    /// dan jabatan yang belum punya orang tetap tampil sebagai baris kosong
    /// penanda. Ringkasan keterisian dibaca dari kepala bagian di atas tabel:
    /// jumlah orang, jumlah jabatan, dan jabatan yang belum terisi.
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

    public partial class PerangkatDesaViewModel : ObservableObject
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
        private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;

        private string _statusText = string.Empty;
        private string _formGalat = string.Empty;
        private bool _isBusy;
        private bool _isFormTerbuka;
        private bool _isModeUbah;
        private int _formId;

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
            ILogger<PerangkatDesaViewModel> logger,
            SuDesApp.Services.PenjagaLisensi? penjaga = null)
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
            _penjaga = penjaga;

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

        /// <summary>Cetak daftar ke PDF lalu tampilkan pada pratinjau SudesPdf.</summary>
        public AsyncRelayCommand CetakPdfCommand { get; }

        /// <summary>Cetak formulir pendataan kosong (18 kolom DATA PERANGKAT) untuk
        /// mengumpulkan data perangkat baru, lalu tampilkan pada pratinjau SudesPdf.</summary>
        public AsyncRelayCommand CetakFormulirCommand { get; }

        /// <summary>Buka panel SK untuk satu baris, termasuk baris jabatan yang belum ada orangnya.</summary>
        public AsyncRelayCommand<BarisPerangkatViewModel> SkCommand { get; }

        /// <summary>Tutup panel SK tanpa mengubah data.</summary>
        public RelayCommand TutupPanelSkCommand { get; }

        /// <summary>Buat SK jabatan terpilih lalu tampilkan pratinjaunya di SudesPdf.</summary>
        public AsyncRelayCommand PratinjauSkCommand { get; }

        /// <summary>Salin berkas PDF SK ke folder arsip untuk orang pada jabatan terpilih.</summary>
        public AsyncRelayCommand ArsipkanSkCommand { get; }

        /// <summary>Buka berkas SK terarsip pada pratinjau SudesPdf.</summary>
        public RelayCommand BukaBerkasSkCommand { get; }

        /// <summary>Muat ulang daftar nama lampiran SK bersama dari data jabatan terpilih.</summary>
        public AsyncRelayCommand SegarkanLampiranCommand { get; }


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

