using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman Data Perangkat Desa: daftar orang dan jabatannya, dari Kepala
    /// Desa sampai Kepala Dusun, Ketua RT/RW, Linmas, dan lembaga (Posyandu,
    /// PKK, BPD).
    ///
    /// Dua aturan yang dijaga form ini: satu jabatan pada satu wilayah hanya
    /// boleh satu orang, dan jabatan inti desa (Kepala Desa, Sekretaris Desa,
    /// Ketua BPD) selalu terlihat di kartu rekapitulasi supaya kelengkapan
    /// struktur tidak terlewat.
    /// </summary>
    public class PerangkatDesaViewModel : ObservableObject
    {
        private readonly IPerangkatDesaService _layanan;
        private readonly IDesaRepository _desaRepo;
        private readonly AppConfig _appConfig;
        private readonly SkPerangkatGenerator _skGenerator;
        private readonly SkPerangkatLampiranService _lampiranService;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<PerangkatDesaViewModel> _logger;

        private string _desaNama = string.Empty;

        private string _searchText = string.Empty;
        private string _filterJabatan = string.Empty;
        private string _filterStatus = string.Empty;
        private string _filterWilayah = string.Empty;
        private string _filterKelompok = string.Empty;
        private string _statusText = string.Empty;
        private string _formGalat = string.Empty;
        private bool _isBusy;
        private bool _isFormTerbuka;
        private bool _isModeUbah;
        private int _formId;

        /// <summary>
        /// Nama berkas SK Bupati yang sudah terarsip untuk baris yang sedang
        /// diedit. Disimpan terpisah karena form data perangkat tidak punya
        /// isian untuk berkas. Tanpa ini, simpan dari form menulis BerkasSK
        /// kosong sehingga nama berkas arsip hilang dari data Though berkas
        /// PDF-nya sendiri masih ada di folder.
        /// </summary>
        private string? _formBerkasSk;

        // isian form
        private string _formNama = string.Empty;
        private string _formJabatan = string.Empty;
        private string _formNip = string.Empty;
        private string _formNik = string.Empty;
        private string _formJenisKelamin = string.Empty;
        private string _formTempatLahir = string.Empty;
        private DateTime? _formTanggalLahir;
        private string _formPendidikan = string.Empty;
        private string _formAlamat = string.Empty;
        private string _formDusun = string.Empty;
        private string _formRt = string.Empty;
        private string _formRw = string.Empty;
        private string _formUnit = string.Empty;
        private string _formNomorHp = string.Empty;
        private string _formWhatsApp = string.Empty;
        private string _formNomorSk = string.Empty;
        private DateTime? _formTanggalSk;
        private DateTime? _formMasaJabatanMulai;
        private DateTime? _formMasaJabatanSelesai;
        private string _formStatus = StatusPerangkat.Aktif;
        private string _formCatatan = string.Empty;

        private readonly List<string> _nilaiWilayah = new();

        public PerangkatDesaViewModel(
            IPerangkatDesaService layanan,
            IDesaRepository desaRepo,
            AppConfig appConfig,
            SkPerangkatGenerator skGenerator,
            SkPerangkatLampiranService lampiranService,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<PerangkatDesaViewModel> logger)
        {
            _layanan = layanan ?? throw new ArgumentNullException(nameof(layanan));
            _desaRepo = desaRepo ?? throw new ArgumentNullException(nameof(desaRepo));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _skGenerator = skGenerator ?? throw new ArgumentNullException(nameof(skGenerator));
            _lampiranService = lampiranService ?? throw new ArgumentNullException(nameof(lampiranService));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            MuatUlangCommand = new AsyncRelayCommand(() => LoadAsync(true));
            CariCommand = new AsyncRelayCommand(CariAsync);
            TambahCommand = new AsyncRelayCommand(BukaFormTambahAsync, () => !IsBusy);
            UbahCommand = new AsyncRelayCommand<PerangkatDesa>(BukaFormUbahAsync, p => p != null && !IsBusy);
            HapusCommand = new AsyncRelayCommand<PerangkatDesa>(HapusAsync, p => p != null && !IsBusy);
            BatalFormCommand = new RelayCommand(TutupForm);
            SimpanCommand = new AsyncRelayCommand(SimpanAsync,
                () => !IsBusy && !string.IsNullOrWhiteSpace(FormNama) && !string.IsNullOrWhiteSpace(FormJabatan));
            TandaiIntiKosongCommand = new AsyncRelayCommand(TampilkanJabatanIntiKosongAsync, () => !IsBusy);
            BukaPanelSkCommand = new AsyncRelayCommand<PerangkatDesa>(
                p => BukaPanelSkAsync(p), p => p != null && !IsBusy);
            BukaPanelSkContohCommand = new AsyncRelayCommand(() => BukaPanelSkAsync(null), () => !IsBusy);
            TutupPanelSkCommand = new RelayCommand(() => IsPanelSkTerbuka = false);
            CetakSkCommand = new AsyncRelayCommand(CetakSkAsync, () => !IsBusy);
            BukaPanelJabatanCommand = new AsyncRelayCommand<JabatanAksiItem>(BukaPanelJabatanAsync, p => p != null && !IsBusy);
            TampilkanSemuaCommand = new AsyncRelayCommand(TampilkanSemuaAsync, () => !IsBusy);
            PilihBerkasSkCommand = new AsyncRelayCommand(PilihBerkasSkAsync, () => !IsBusy);
            LepasBerkasSkCommand = new RelayCommand(LepasBerkasSk);
            PratinjauBerkasSkCommand = new AsyncRelayCommand<PerangkatDesa>(PratinjauBerkasSkAsync, p => p != null && !IsBusy);
            MuatAnggotaLampiranCommand = new AsyncRelayCommand(MuatAnggotaLampiranAsync, () => !IsBusy);
            TambahLampiranCommand = new AsyncRelayCommand(TambahLampiranAsync, () => !IsBusy);
            KosongkanLampiranCommand = new RelayCommand(KosongkanLampiran);
        }

        // ---------- header ----------

        public string HeaderTitle => "DATA PERANGKAT DESA";

        public string HeaderSubtitle =>
            "Siapa yang memegang jabatan di desa: Kepala Desa, sekretariat, pelaksana teknis, "
            + "Kepala Dusun, Ketua RT/RW, Linmas, Posyandu, PKK, dan BPD.";

        // ---------- daftar ----------

        public ObservableCollection<PerangkatDesa> Items { get; } = new();
        public ObservableCollection<PerangkatStatistikKartu> KartuStatistik { get; } = new();

        public bool AdanyaData => Items.Count > 0;

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
                    OnPropertyChanged(nameof(AdanyaData));
                    SegarkanPerintah();
                }
            }
        }

        // ---------- filter ----------

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value ?? string.Empty);
        }

        /// <summary>Jabatan yang difilter; kosong berarti semua.</summary>
        public string FilterJabatan
        {
            get => _filterJabatan;
            set => SetProperty(ref _filterJabatan, value ?? string.Empty);
        }

        public string FilterStatus
        {
            get => _filterStatus;
            set => SetProperty(ref _filterStatus, value ?? string.Empty);
        }

        public string FilterWilayah
        {
            get => _filterWilayah;
            set => SetProperty(ref _filterWilayah, value ?? string.Empty);
        }

        /// <summary>Kelompok jabatan yang sedang ditampilkan (mis. <c>RT/RW</c>); kosong berarti
        /// semua kelompok. Diisi lebih dulu oleh menu sidebar yang dipisah per kelompok
        /// (mis. "Data Perangkat Desa → Linmas"), lalu tetap bisa diubah dari dropdown
        /// filter di halaman ini supaya pengguna tidak terjebak pada satu kelompok.</summary>
        public string FilterKelompok
        {
            get => _filterKelompok;
            set
            {
                if (SetProperty(ref _filterKelompok, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(KelompokTerpilih));
                }
            }
        }

        /// <summary>
        /// Nama kelompok yang aktif memfilter daftar ("" → null). Dipakai panel
        /// "SK per Jabatan" untuk menandai kartu yang daftar orangnya sedang tampil.
        /// </summary>
        public string? KelompokTerpilih =>
            string.IsNullOrWhiteSpace(_filterKelompok) ? null : _filterKelompok.Trim();

        /// <summary>Semua jabatan, urut dari pimpinan ke lembaga.</summary>
        public IReadOnlyList<string> DaftarJabatan { get; } = JabatanPerangkat.Semua;

        /// <summary>
        /// Jabatan untuk dropdown filter. Diawali pilihan kosong supaya filter
        /// bisa dikembalikan ke "semua jabatan" setelah operator menyaring.
        /// </summary>
        public IReadOnlyList<string> PilihanJabatanFilter { get; } =
            new[] { string.Empty }.Concat(JabatanPerangkat.Semua).ToList();

        /// <summary>Status filter, diawali pilihan kosong (semua status).</summary>
        public IReadOnlyList<string> PilihanStatusFilter { get; } =
            new[] { string.Empty }.Concat(StatusPerangkat.Semua).ToList();

        /// <summary>
        /// Kelompok jabatan untuk dropdown filter — diawali pilihan kosong (semua
        /// kelompok), seragam dengan filter jabatan/status/wilayah di halaman ini.
        /// </summary>
        public IReadOnlyList<string> PilihanKelompokFilter { get; } =
            new[] { string.Empty }.Concat(JabatanPerangkat.UrutanKelompok).ToList();

        public IReadOnlyList<string> PilihanStatusForm { get; } = StatusPerangkat.Semua;

        public IReadOnlyList<string> PilihanJenisKelamin { get; } = new[] { "Laki-laki", "Perempuan" };

        public IReadOnlyList<string> PilihanPendidikan { get; } = new[]
        {
            "SD", "SMP/MTs", "SMA/MA/SMK", "D1/D2/D3", "S1", "S2", "S3"
        };

        /// <summary>
        /// Nilai dusun, RT, dan RW yang pernah dipakai, untuk dropdown filter.
        /// Diawali pilihan kosong supaya filter bisa dibersihkan kembali.
        /// </summary>
        public IReadOnlyList<string> DaftarWilayah =>
            new[] { string.Empty }.Concat(_nilaiWilayah).ToList();

        // ---------- form ----------

        public bool IsFormTerbuka
        {
            get => _isFormTerbuka;
            private set => SetProperty(ref _isFormTerbuka, value);
        }

        public string JudulForm => IsModeUbah ? "Ubah Data Perangkat" : "Tambah Perangkat Desa";

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

        public string FormJabatan
        {
            get => _formJabatan;
            set
            {
                if (SetProperty(ref _formJabatan, value))
                {
                    BersihkanGalat();
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

        public DateTime? FormTanggalLahir
        {
            get => _formTanggalLahir;
            set => SetProperty(ref _formTanggalLahir, value);
        }

        public string FormPendidikan
        {
            get => _formPendidikan;
            set => SetProperty(ref _formPendidikan, value);
        }

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

        /// <summary>
        /// Unit kerja di dalam kelompok jabatannya (mis. "POSYANDU SAKURA I"),
        /// dipakai mengelompokkan nama pada lampiran SK banyak orang.
        /// </summary>
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

        public string FormNomorSk
        {
            get => _formNomorSk;
            set => SetProperty(ref _formNomorSk, value);
        }

        public DateTime? FormTanggalSk
        {
            get => _formTanggalSk;
            set => SetProperty(ref _formTanggalSk, value);
        }

        public DateTime? FormMasaJabatanMulai
        {
            get => _formMasaJabatanMulai;
            set => SetProperty(ref _formMasaJabatanMulai, value);
        }

        public DateTime? FormMasaJabatanSelesai
        {
            get => _formMasaJabatanSelesai;
            set => SetProperty(ref _formMasaJabatanSelesai, value);
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

        // ---------- perintah ----------

        public AsyncRelayCommand MuatUlangCommand { get; }
        public AsyncRelayCommand CariCommand { get; }
        public AsyncRelayCommand TambahCommand { get; }
        public AsyncRelayCommand<PerangkatDesa> UbahCommand { get; }
        public AsyncRelayCommand<PerangkatDesa> HapusCommand { get; }
        public RelayCommand BatalFormCommand { get; }
        public AsyncRelayCommand SimpanCommand { get; }
        public AsyncRelayCommand TandaiIntiKosongCommand { get; }
        public AsyncRelayCommand<PerangkatDesa> BukaPanelSkCommand { get; }
        public AsyncRelayCommand BukaPanelSkContohCommand { get; }
        public RelayCommand TutupPanelSkCommand { get; }
        public AsyncRelayCommand CetakSkCommand { get; }

        /// <summary>Kartu aksi pada panel "SK per Jabatan" — klik kartu = saring daftar + buka panel SK.</summary>
        public AsyncRelayCommand<JabatanAksiItem> BukaPanelJabatanCommand { get; }

        // ---------- aksi ----------

        /// <summary>
        /// Muat daftar sesuai filter. Dipanggil setelah halaman tampil, jadi
        /// tidak perlu menunggu pekerjaan berat sebelum menavigasi.
        /// </summary>
        public async Task LoadAsync(bool tampilkanPesan)
        {
            try
            {
                IsBusy = true;
                var filter = new PerangkatDesaFilter
                {
                    Cari = SearchText,
                    Jabatan = FilterJabatan,
                    Status = FilterStatus,
                    Wilayah = FilterWilayah,
                    Kelompok = FilterKelompok
                };

                var hasil = await _layanan.AmbilSemuaAsync(filter).ConfigureAwait(true);
                var statistik = await _layanan.AmbilStatistikAsync().ConfigureAwait(true);

                // Nama desa untuk kop pratinjau panel SK (sekali per muat).
                if (_desaNama.Length == 0)
                {
                    try
                    {
                        var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                        _desaNama = desa.NamaDesa ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal memuat info desa untuk panel SK");
                    }
                }

                Items.Clear();
                foreach (var p in hasil) Items.Add(p);
                OnPropertyChanged(nameof(AdanyaData));

                SusunKartu(statistik);
                SusunPanelJabatan();
                await MuatWilayahAsync().ConfigureAwait(true);

                StatusText = hasil.Count == 0
                    ? "Belum ada perangkat desa yang sesuai filter."
                    : $"{hasil.Count} perangkat desa ditampilkan. " + (tampilkanPesan ? "" : "Filter tidak diubah.");
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

        private void SusunKartu(PerangkatDesaStatistik s)
        {
            KartuStatistik.Clear();
            KartuStatistik.Add(new PerangkatStatistikKartu
            {
                Label = "TOTAL PERANGKAT",
                Nilai = s.Total.ToString(),
                Keterangan = $"{s.Total} orang tercatat"
            });
            KartuStatistik.Add(new PerangkatStatistikKartu
            {
                Label = "SEDANG MEMEGANG JABATAN",
                Nilai = (s.Aktif + s.MenungguSK).ToString(),
                Keterangan = $"{s.Aktif} aktif, {s.MenungguSK} menunggu SK"
            });
            KartuStatistik.Add(new PerangkatStatistikKartu
            {
                Label = "JABATAN INTI",
                Nilai = $"{JabatanPerangkat.Inti.Count - s.JabatanIntiKosong.Count}/{JabatanPerangkat.Inti.Count}",
                Keterangan = s.JabatanIntiKosong.Count == 0
                    ? "Struktur inti lengkap"
                    : "Kosong: " + string.Join(", ", s.JabatanIntiKosong),
                Tekankan = s.JabatanIntiKosong.Count > 0
            });
            KartuStatistik.Add(new PerangkatStatistikKartu
            {
                Label = "SUDAH SELESAI / BERHENTI",
                Nilai = (s.Selesai + s.Berhenti).ToString(),
                Keterangan = $"{s.Selesai} selesai, {s.Berhenti} berhenti"
            });
        }

        private async Task MuatWilayahAsync()
        {
            var nilai = await _layanan.AmbilNilaiWilayahAsync().ConfigureAwait(true);
            bool sama = nilai.SequenceEqual(_nilaiWilayah, StringComparer.OrdinalIgnoreCase);
            if (sama) return;

            _nilaiWilayah.Clear();
            _nilaiWilayah.AddRange(nilai.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(DaftarWilayah));
        }

        private Task CariAsync() => LoadAsync(true);

        // ---------- panel Surat Keputusan (SK) ----------

        private bool _isPanelSkTerbuka;
        private string _skJenis = SkJenisPerangkat.Pengangkatan.ToString();
        private string _skKelompok = string.Empty;
        private string _skNomor = string.Empty;
        private string _skNama = string.Empty;
        private string _skNik = string.Empty;
        private string _skJabatan = string.Empty;
        private string _skWilayah = string.Empty;
        private string _skAlasan = string.Empty;
        private DateTime? _skTanggal = DateTime.Today;
        private DateTime? _skMulai;
        private DateTime? _skSelesai;
        private string _skDasarHukum = string.Empty;
        private string _skGalat = string.Empty;
        private bool _skContoh;
        private PerangkatDesa? _skSumber;

        /// <summary>Nama berkas SK terarsip yang sedang dipilih pada panel mode arsip (SK Bupati).</summary>
        private string? _skBerkasBaru;

        // isian lampiran daftar nama (SK banyak orang)
        private string _skLampiranNikBaru = string.Empty;
        private string _skLampiranPeranBaru = string.Empty;
        private string _skLampiranUnitBaru = string.Empty;
        private string _skLampiranPesan = string.Empty;

        /// <summary>Kelompok + dasar hukumnya untuk daftar rujukan panel SK.</summary>
        public IReadOnlyList<BarisKelompokSk> DaftarKelompokSk { get; private set; } =
            JabatanPerangkat.UrutanKelompok
                .Where(SkPerangkatKatalog.Ada)
                .Select(k => new BarisKelompokSk
                {
                    Kelompok = k,
                    DasarHukumTeks = SkPerangkatKatalog.Cari(k)!.DasarHukumTeks,
                    PunyaTemplate = true
                })
                .ToList();

        /// <summary>Kartu aksi per jabatan pada panel "SK per Jabatan" — disusun ulang setiap daftar dimuat.</summary>
        public ObservableCollection<JabatanAksiItem> KartuJabatanAksi { get; } = new();

        /// <summary>Benar bila panel "SK per Jabatan" sedang diperlihatkan (bisa ditutup).</summary>
        public bool AdanyaPanelJabatan
        {
            get => _adanyaPanelJabatan;
            private set => SetProperty(ref _adanyaPanelJabatan, value);
        }

        private bool _adanyaPanelJabatan;

        /// <summary>Jabatan yang kartunya terakhir diklik — penanda pilihan pada panel.</summary>
        public string? JabatanPanelTerpilih
        {
            get => _jabatanPanelTerpilih;
            private set => SetProperty(ref _jabatanPanelTerpilih, value);
        }

        private string? _jabatanPanelTerpilih;

        public bool IsPanelSkTerbuka
        {
            get => _isPanelSkTerbuka;
            private set => SetProperty(ref _isPanelSkTerbuka, value);
        }

        /// <summary>Judul panel, menyesuaikan mode contoh atau orang tertentu.</summary>
        public string JudulPanelSk => _skContoh ? "Contoh Surat Keputusan" : "Surat Keputusan SK";

        /// <summary>
        /// SK kelompok yang sedang dibuka diterbitkan Bupati — panel beralih ke mode
        /// arsip: operator melampirkan berkas PDF SK Bupati, bukan menerbitkan dokumen.
        /// Contoh SK (tanpa baris data) tetap mode cetak agar bentuknya tetap bisa dilihat.
        /// </summary>
        /// <summary>Mode arsip SK Bupati: baris yang dibuka termasuk kelompok yang SK-nya diterbitkan Bupati (BPD).</summary>
        public bool SkModeArsipBupati =>
            !_skContoh && _skSumber != null && SkPerangkatKatalog.DariBupati(_skSumber.Kelompok);

        /// <summary>Tampilkan kembali seluruh kelompok dan sembunyikan panel aksi jabatan.</summary>
        public AsyncRelayCommand TampilkanSemuaCommand { get; }

        /// <summary>Keterangan mode arsip pada panel (kenapa tombol cetaknya berganti).</summary>
        public string SkModeArsipKeterangan
        {
            get
            {
                if (_skSumber != null
                    && string.Equals(_skSumber.JabatanTampil, JabatanPerangkat.KepalaDesa, StringComparison.Ordinal))
                {
                    return "SK Kepala Desa diterbitkan Bupati melalui Surat Keputusan (SKD), bukan oleh Kepala Desa sendiri. "
                        + "Lampirkan berkas PDF SKD-nya di sini: nomor dan tanggalnya ikut tersimpan ke data perangkat, "
                        + "dan berkasnya bisa dibuka lagi kapan saja dari daftar.";
                }

                return "SK kelompok ini diterbitkan Bupati (SK bersama seluruh anggota), bukan oleh Kepala Desa. "
                    + "Lampirkan berkas PDF SK Bupati di sini: nomor dan tanggalnya ikut tersimpan ke data perangkat, "
                    + "dan berkasnya bisa dibuka lagi kapan saja dari daftar.";
            }
        }

        /// <summary>
        /// Benar bila SK yang sedang dibuka panelnya diterbitkan Bupati (SKD).
        /// Berlaku dua jalur: baris Kepala Desa (jabatan yang SK-nya dari Bupati) dan
        /// kelompok BPD. Mode arsip menggantikan tombol cetak pada panel.
        /// </summary>
        public bool SkDiterbitkanBupati =>
            !_skContoh && _skSumber != null
            && SkJabatanAksiKatalog.SumberJabatan(_skSumber.Jabatan) == SumberSkPerangkat.Bupati;

        /// <summary>Nama berkas yang baru dipilih di panel mode arsip (belum tentu tersimpan).</summary>
        public string? SkBerkasTerpilih
        {
            get => _skBerkasBaru ?? _skSumber?.BerkasSK;
            private set => SetProperty(ref _skBerkasBaru, value);
        }

        /// <summary>Berkas SK terarsip ada untuk baris yang dibuka — tombol pratinjau aktif.</summary>
        public bool AdaBerkasSkTerpilih => !string.IsNullOrWhiteSpace(SkBerkasTerpilih)
            && BerkasSkPerangkat.JalurLengkap(SkBerkasTerpilih) != null;

        public IReadOnlyList<string> PilihanSkJenis { get; } = new[] { "Pengangkatan", "Pemberhentian" };

        public IReadOnlyList<string> PilihanSkKelompok { get; } =
            JabatanPerangkat.UrutanKelompok.Where(SkPerangkatKatalog.Ada).ToList();

        public string SkJenis
        {
            get => _skJenis;
            set
            {
                if (SetProperty(ref _skJenis, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(SkPengangkatan));
                    OnPropertyChanged(nameof(SkDasarHukumJudul));
                }
            }
        }

        public bool SkPengangkatan =>
            string.Equals(SkJenis, SkJenisPerangkat.Pengangkatan.ToString(), StringComparison.Ordinal);

        /// <summary>Judul blok dasar hukum: SK pengangkatan memakai Mengingat dari template.</summary>
        public string SkDasarHukumJudul => SkPengangkatan ? "MENGINGAT (DASAR HUKUM)" : "CATATAN PEMBERHENTIAN";

        public string SkKelompok
        {
            get => _skKelompok;
            set
            {
                if (SetProperty(ref _skKelompok, value ?? string.Empty))
                {
                    SegarkanDasarHukum();

                    // Lampiran selalu milik satu kelompok: setelah kelompok berganti,
                    // daftar namanya tidak lagi cocok dengan judul dan dasar hukum SK.
                    if (SkLampiran.Count > 0)
                    {
                        SkLampiran.Clear();
                        SegarkanRingkasanLampiran();
                        SkLampiranPesan = "Kelompok berubah, jadi lampiran dikosongkan. "
                            + "Muat ulang anggotanya bila memang diperlukan.";
                    }
                }
            }
        }

        public string SkNomor
        {
            get => _skNomor;
            set => SetProperty(ref _skNomor, value ?? string.Empty);
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

        public string SkJabatan
        {
            get => _skJabatan;
            set => SetProperty(ref _skJabatan, value ?? string.Empty);
        }

        public string SkWilayah
        {
            get => _skWilayah;
            set => SetProperty(ref _skWilayah, value ?? string.Empty);
        }

        public string SkAlasan
        {
            get => _skAlasan;
            set => SetProperty(ref _skAlasan, value ?? string.Empty);
        }

        public DateTime? SkTanggal
        {
            get => _skTanggal;
            set => SetProperty(ref _skTanggal, value);
        }

        public DateTime? SkMulai
        {
            get => _skMulai;
            set => SetProperty(ref _skMulai, value);
        }

        public DateTime? SkSelesai
        {
            get => _skSelesai;
            set => SetProperty(ref _skSelesai, value);
        }

        /// <summary>Dasar hukum kelompok terpilih, siap baca di panel.</summary>
        public string SkDasarHukum
        {
            get => _skDasarHukum;
            private set => SetProperty(ref _skDasarHukum, value);
        }

        public string SkGalat
        {
            get => _skGalat;
            private set => SetProperty(ref _skGalat, value);
        }

        // ---------- lampiran daftar nama (SK banyak orang) ----------

        /// <summary>
        /// Baris lampiran SK. Kosong berarti SK dicetak untuk satu orang seperti
        /// biasa; berisi berarti diktum KESATU merujuk lampiran dan tabel namanya
        /// dicetak di halaman berikutnya, dikelompokkan per unit.
        /// </summary>
        public ObservableCollection<LampiranBarisViewModel> SkLampiran { get; } = new();

        public string SkLampiranNikBaru
        {
            get => _skLampiranNikBaru;
            set => SetProperty(ref _skLampiranNikBaru, value ?? string.Empty);
        }

        public string SkLampiranPeranBaru
        {
            get => _skLampiranPeranBaru;
            set => SetProperty(ref _skLampiranPeranBaru, value ?? string.Empty);
        }

        public string SkLampiranUnitBaru
        {
            get => _skLampiranUnitBaru;
            set => SetProperty(ref _skLampiranUnitBaru, value ?? string.Empty);
        }

        /// <summary>Pesan ringkas di bawah daftar lampiran (hasil muat/tambah/hapus).</summary>
        public string SkLampiranPesan
        {
            get => _skLampiranPesan;
            private set => SetProperty(ref _skLampiranPesan, value);
        }

        public bool AdaLampiranSk => SkLampiran.Count > 0;

        /// <summary>
        /// Ringkasan lampiran: berapa orang, berapa unit, dan berapa baris yang
        /// kolom pribadinya masih kosong (NIK tidak ketemu di data warga).
        /// </summary>
        public string RingkasanLampiran
        {
            get
            {
                if (SkLampiran.Count == 0)
                {
                    return "Belum ada lampiran: SK dicetak untuk satu orang (isian Nama di atas).";
                }

                var unit = SkLampiran
                    .Select(b => b.Unit.Trim())
                    .Where(u => u.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string bagianUnit = unit.Count == 0
                    ? "tanpa pengelompokan unit"
                    : $"{unit.Count} unit: {string.Join("; ", unit)}";

                int tanpaDataWarga = SkLampiran.Count(b => !b.DataWargaLengkap);

                string catatan = tanpaDataWarga == 0
                    ? string.Empty
                    : $" — {tanpaDataWarga} orang datanya belum lengkap di Data Warga";

                return $"{SkLampiran.Count} orang, {bagianUnit}{catatan}. "
                    + "Diktum KESATU akan merujuk lampiran ini, jadi isian Nama di atas tidak dipakai.";
            }
        }

        public ICommand PilihBerkasSkCommand { get; }

        public ICommand LepasBerkasSkCommand { get; }

        /// <summary>Buka pratinjau berkas SK terarsip dari baris daftar (utama: SK Bupati).</summary>
        public ICommand PratinjauBerkasSkCommand { get; }

        public ICommand MuatAnggotaLampiranCommand { get; }

        public ICommand TambahLampiranCommand { get; }

        public ICommand KosongkanLampiranCommand { get; }

        /// <summary>
        /// Isi lampiran dari seluruh anggota kelompok yang sedang dipilih (mis.
        /// semua kader Posyandu), lengkap dengan unit kerja masing-masing, lalu
        /// ambil pekerjaan/agama/golongan darah/status kawinnya dari data Warga.
        /// </summary>
        private async Task MuatAnggotaLampiranAsync()
        {
            if (!SkPerangkatKatalog.Ada(SkKelompok))
            {
                SkLampiranPesan = $"Kelompok {SkKelompok} belum punya template SK.";
                return;
            }

            try
            {
                IsBusy = true;
                var anggota = await _layanan
                    .AmbilSemuaAsync(new PerangkatDesaFilter { Kelompok = SkKelompok })
                    .ConfigureAwait(true);

                // Hanya yang masih memegang jabatan: orang yang sudah SELESAI/
                // BERHENTI tidak lagi ditetapkan dalam SK baru.
                var sumber = anggota
                    .Where(a => a.MasihMemegangJabatan)
                    .Select(a => new LampiranSumber { Orang = a, Unit = a.Unit ?? string.Empty })
                    .ToList();

                if (sumber.Count == 0)
                {
                    SkLampiranPesan = $"Tidak ada anggota kelompok {JabatanPerangkat.TampilanKelompok(SkKelompok)} "
                        + "yang masih memegang jabatan.";
                    return;
                }

                var baris = await _lampiranService.SusunAsync(sumber).ConfigureAwait(true);
                PasangLampiran(baris);
                SkLampiranPesan = $"{baris.Count} anggota kelompok "
                    + $"{JabatanPerangkat.TampilanKelompok(SkKelompok)} dimuat ke lampiran."
                    + " Isi kolom Peran bila kedudukannya berbeda (mis. Ketua, Sekretaris),"
                    + " dan kolom Unit untuk mengelompokkan baris.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat anggota kelompok {Kelompok} ke lampiran SK", SkKelompok);
                SkLampiranPesan = "Gagal memuat anggota kelompok: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Tambah satu nama ke lampiran dari NIK. Jalur ini dipakai untuk orang yang
        /// tidak tercatat di Data Perangkat Desa (mis. kader bantu), sebab seluruh
        /// kolom pribadinya diambil dari data Warga.
        /// </summary>
        private async Task TambahLampiranAsync()
        {
            string nik = SkLampiranNikBaru.Trim();
            if (nik.Length == 0)
            {
                SkLampiranPesan = "Isi NIK 16 digit warga yang mau ditambahkan.";
                return;
            }

            try
            {
                IsBusy = true;
                var baris = await _lampiranService
                    .SusunSatuAsync(nik, SkLampiranPeranBaru, SkLampiranUnitBaru)
                    .ConfigureAwait(true);

                if (baris == null)
                {
                    SkLampiranPesan = $"NIK {nik} tidak ditemukan di Data Warga. Periksa kembali "
                        + "atau tambahkan warganya lebih dulu di halaman Data Warga.";
                    return;
                }

                if (baris.NIK.Length > 0
                    && SkLampiran.Any(b => string.Equals(b.Nik, baris.NIK, StringComparison.Ordinal)))
                {
                    SkLampiranPesan = $"{baris.Nama} sudah ada di lampiran — setiap orang cukup sekali.";
                    return;
                }

                SisipLampiran(baris);
                SkLampiranNikBaru = string.Empty;
                SkLampiranPeranBaru = string.Empty;
                SkLampiranUnitBaru = string.Empty;
                SkLampiranPesan = $"{baris.Nama} ditambahkan ke lampiran.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambah NIK {NIK} ke lampiran SK", nik);
                SkLampiranPesan = "Gagal menambah nama: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void KosongkanLampiran()
        {
            if (SkLampiran.Count == 0)
            {
                SkLampiranPesan = "Lampiran memang masih kosong.";
                return;
            }

            SkLampiran.Clear();
            SkLampiranPesan = "Lampiran dikosongkan: SK kembali dicetak untuk satu orang.";
            SegarkanRingkasanLampiran();
        }

        private void HapusLampiranBaris(LampiranBarisViewModel baris)
        {
            if (SkLampiran.Remove(baris))
            {
                SkLampiranPesan = $"{baris.Nama} dihapus dari lampiran.";
                SegarkanRingkasanLampiran();
            }
        }

        private void PasangLampiran(IReadOnlyList<BarisLampiranSk> baris)
        {
            SkLampiran.Clear();
            foreach (var orang in baris)
            {
                SisipLampiran(orang);
            }
        }

        private void SisipLampiran(BarisLampiranSk orang)
        {
            var baris = new LampiranBarisViewModel(orang, HapusLampiranBaris);
            baris.PropertyChanged += (_, _) => SegarkanRingkasanLampiran();
            SkLampiran.Add(baris);
            SegarkanRingkasanLampiran();
        }

        private void SegarkanRingkasanLampiran()
        {
            OnPropertyChanged(nameof(RingkasanLampiran));
            OnPropertyChanged(nameof(AdaLampiranSk));
        }

        /// <summary>Buka panel SK untuk satu orang (pra-isi dari datanya) atau contoh kosong.
        /// Dipanggil juga dari kartu aksi jabatan, yang membawa penanda jabatan pemanggil.</summary>
        private async Task BukaPanelSkAsync(PerangkatDesa? data, JabatanAksiItem? kartuJabatan = null)
        {
            await Task.Yield();

            // Kelompok tanpa template SK (jabatan khusus desa → LAINNYA) ditolak dengan
            // penjelasan. Sebelumnya panel diam-diam jatuh ke template PERANGKAT DESA,
            // sehingga dokumen tercetak dengan judul dan dasar hukum kelompok yang salah.
            if (data is not null && !SkPerangkatKatalog.Ada(data.Kelompok))
            {
                StatusText = $"Kelompok {data.Kelompok} belum punya template SK bawaan.";
                _logger.LogInformation(
                    "SK perangkat ditolak: kelompok {Kelompok} tidak punya template (jabatan {Jabatan}).",
                    data.Kelompok, data.JabatanTampil);

                await _messageService.ShowInfoAsync(
                    $"Jabatan {data.JabatanTampil} termasuk kelompok {data.Kelompok} yang belum punya "
                    + "template SK bawaan, jadi SK-nya tidak bisa dicetak dari halaman ini.\n\n"
                    + "Template SK bawaan tersedia untuk: "
                    + string.Join(", ", PilihanSkKelompok.Select(JabatanPerangkat.TampilanKelompok)) + ".\n\n"
                    + "Untuk jabatan lain, buat SK-nya lewat menu Surat Peraturan → SK / Keputusan, "
                    + "atau perbaiki jabatan pada data perangkat ini bila kelompoknya memang keliru.");
                return;
            }

            _skSumber = data;
            _skContoh = data is null;
            _skBerkasBaru = null;
            SkGalat = string.Empty;

            // Kartu aksi jabatan menentukan kelompok & jabatan panel meskipun
            // jabatannya belum punya orang di daftar (pemasangan baru).

            // Lampiran selalu mulai kosong: SK satu orang adalah bentuk baku, dan
            // lampiran hanya dipakai bila operator memang memuatnya.
            SkLampiran.Clear();
            SegarkanRingkasanLampiran();
            SkLampiranPesan = string.Empty;
            SkLampiranNikBaru = string.Empty;
            SkLampiranPeranBaru = string.Empty;
            SkLampiranUnitBaru = string.Empty;

            if (data is null)
            {
                SkJenis = SkJenisPerangkat.Pengangkatan.ToString();
                // Pemanggil kartu jabatan menentukan kelompoknya sendiri — termasuk
                // LAINNYA, supaya pesan "belum punya template" bisa ditunjukkan.
                SkKelompok = kartuJabatan?.Kelompok ?? PilihanSkKelompok.First();
                SkNomor = string.Empty;
                SkNama = string.Empty;
                SkNik = string.Empty;
                SkJabatan = kartuJabatan?.NamaJabatan ?? string.Empty;
                SkWilayah = string.Empty;
                SkAlasan = string.Empty;
                SkTanggal = DateTime.Today;
                SkMulai = null;
                SkSelesai = null;
            }
            else
            {
                SkJenis = SkJenisPerangkat.Pengangkatan.ToString();
                // Sudah dipastikan punya template di atas, jadi kelompoknya dipakai
                // apa adanya — tidak ada lagi kejadian dokumen jatuh ke kelompok lain.
                SkKelompok = data.Kelompok;
                SkNomor = data.NomorSK ?? string.Empty;
                SkNama = data.Nama;
                SkNik = data.NIK ?? string.Empty;
                SkJabatan = data.JabatanTampil;
                SkWilayah = data.WilayahRingkas == "-" ? string.Empty : data.WilayahRingkas;
                SkAlasan = string.Empty;
                SkTanggal = data.TanggalSK ?? DateTime.Today;
                SkMulai = data.MasaJabatanMulai;
                SkSelesai = data.MasaJabatanSelesai;
            }

            OnPropertyChanged(nameof(JudulPanelSk));
            OnPropertyChanged(nameof(SkModeArsipBupati));
            OnPropertyChanged(nameof(SkDiterbitkanBupati));
            OnPropertyChanged(nameof(SkModeArsipKeterangan));
            OnPropertyChanged(nameof(SkBerkasTerpilih));
            OnPropertyChanged(nameof(AdaBerkasSkTerpilih));
            IsPanelSkTerbuka = true;

            // Kelompok yang SK-nya lazim diterbitkan bersama (Linmas, kader Posyandu)
            // langsung ditawari anggotanya — operator tinggal memeriksa daftarnya.
            if (!_skContoh && _skSumber != null && SkLampiran.Count == 0
                && SkPerangkatKatalog.LazimBersama(_skSumber.Kelompok))
            {
                await MuatAnggotaLampiranAsync().ConfigureAwait(true);
            }
        }

        private void SegarkanDasarHukum()
        {
            SkDasarHukum = SkPerangkatKatalog.Cari(SkKelompok)?.DasarHukumTeks ?? string.Empty;
        }

        /// <summary>
        /// Mode arsip SK Bupati: pilih berkas PDF dari komputer, salin ke folder arsip
        /// aplikasi, lalu catat nomor + tanggalnya ke data perangkat. Operator diminta
        /// mengisi nomor SK dulu supaya data ikut lengkap (tidak wajib, tapi diingatkan).
        /// </summary>
        private async Task PilihBerkasSkAsync()
        {
            if (_skSumber == null) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pilih berkas PDF SK Bupati",
                Filter = "PDF (*.pdf)|*.pdf",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                IsBusy = true;

                // Nama berkas yang sedang terarsip harus diingat lebih dulu: setelah
                // arsipkan, nama baru menggantikan nama lama sehingga nama lama tidak
                // bisa lagi dibaca dari data. Berkas lamanya ikut dibersihkan di
                // bawah, kalau tidak ia tertinggal sebagai berkas yatim di folder
                // arsip tanpa yang pemilikinya.
                string? namaLama = SkBerkasTerpilih;

                string nama = BerkasSkPerangkat.Arsipkan(_skSumber.ID, dlg.FileName);
                SkBerkasTerpilih = nama;
                OnPropertyChanged(nameof(AdaBerkasSkTerpilih));

                // Nomor + tanggal SK langsung dicatat ke data perangkat: status
                // MENUNGGU SK tidak lagi menggantung padahal SK Bupatinya sudah ada.
                await SimpanSkArsipAsync(nama).ConfigureAwait(true);

                // SimpanSkArsipAsync melaporkan kegagalan lewat SkGalat. Bila gagal,
                // data di database masih menunjuk berkas lama dan tidak ada yang
                // merujuk berkas baru, jadi berkas baru justru dibuang dan tampilan
                // dikembalikan ke nama lama.
                if (!string.IsNullOrEmpty(SkGalat))
                {
                    BerkasSkPerangkat.Hapus(nama);
                    SkBerkasTerpilih = namaLama;
                    OnPropertyChanged(nameof(AdaBerkasSkTerpilih));
                    return;
                }

                // Nama baru sudah tersimpan: berkas lama tidak dirujuk siapa pun lagi
                // sehingga boleh dibuang agar tidak tertinggal sebagai berkas yatim.
                if (!string.IsNullOrWhiteSpace(namaLama)
                    && !string.Equals(namaLama, nama, StringComparison.OrdinalIgnoreCase))
                {
                    BerkasSkPerangkat.Hapus(namaLama);
                }

                SkGalat = string.Empty;
                StatusText = $"Berkas SK \"{Path.GetFileName(dlg.FileName)}\" diarsipkan untuk {_skSumber.Nama}.";
                _activityLog.Log("PERANGKAT", $"{_skSumber.Nama} ({_skSumber.JabatanTampil})",
                    "Arsip SK Bupati", nama);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengarsipkan berkas SK untuk perangkat #{ID}", _skSumber.ID);
                SkGalat = "Berkas belum bisa diarsipkan: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Lepas berkas SK terarsip dari baris ini (berkas fisiknya ikut dihapus).</summary>
        private void LepasBerkasSk()
        {
            if (_skSumber == null) return;

            string? lama = SkBerkasTerpilih;
            if (string.IsNullOrWhiteSpace(lama)) return;

            BerkasSkPerangkat.Hapus(lama);
            SkBerkasTerpilih = null;
            OnPropertyChanged(nameof(AdaBerkasSkTerpilih));
            StatusText = $"Berkas SK \"{lama}\" dilepas dari {_skSumber.Nama}. Nomor SK tetap tersimpan.";
        }

        /// <summary>Buka pratinjau berkas SK terarsip milik satu baris daftar.</summary>
        private async Task PratinjauBerkasSkAsync(PerangkatDesa? data)
        {
            if (data == null) return;

            string? jalur = BerkasSkPerangkat.JalurLengkap(data.BerkasSK);
            if (jalur == null)
            {
                await _messageService.ShowInfoAsync(
                    "Baris ini belum punya berkas SK terarsip, atau berkasnya sudah dipindah dari folder:\n" +
                    BerkasSkPerangkat.LokasiFolder);
                return;
            }

            _navigation.Navigate(_previewFactory(
                $"SK {data.JabatanTampil} — {data.Nama}", jalur));
        }

        /// <summary>
        /// Simpan nomor SK (isian panel) + nama berkas terarsip ke baris yang dibuka,
        /// tanpa lewat formulir lengkap. Status MENUNGGU SK naik ke AKTIF bila berkas
        /// sudah ada dan tanggalnya terisi.
        /// </summary>
        private async Task SimpanSkArsipAsync(string namaBerkas)
        {
            var segar = await _layanan.AmbilAsync(_skSumber!.ID).ConfigureAwait(true);
            if (segar == null)
            {
                SkGalat = "Data perangkat ini sudah tidak ada; berkasnya tidak tersimpan ke baris mana pun.";
                return;
            }

            segar.NomorSK = string.IsNullOrWhiteSpace(SkNomor) ? segar.NomorSK : SkNomor.Trim();
            segar.TanggalSK = SkTanggal ?? segar.TanggalSK;
            segar.BerkasSK = namaBerkas;
            if (string.Equals(segar.Status, StatusPerangkat.MenungguSK, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(segar.NomorSK))
            {
                segar.Status = StatusPerangkat.Aktif;
            }
            segar.DiperbaruiOleh = SessionContext.Display;

            var hasil = await _layanan.SimpanAsync(segar, modeUbah: true).ConfigureAwait(true);
            if (!hasil.Berhasil)
            {
                SkGalat = hasil.Pesan ?? "Nomor SK belum bisa disimpan ke data perangkat.";
                return;
            }

            _skSumber = segar;
            await LoadAsync(tampilkanPesan: false).ConfigureAwait(true);
        }

        /// <summary>Cetak/pratinjau SK dari isian panel; isian contoh tetap diberi garis.</summary>
        private async Task CetakSkAsync()
        {
            if (string.IsNullOrWhiteSpace(SkKelompok))
            {
                SkGalat = "Pilih kelompok jabatan terlebih dahulu.";
                return;
            }
            if (!SkPerangkatKatalog.Ada(SkKelompok))
            {
                SkGalat = $"Kelompok {SkKelompok} tidak punya template SK bawaan. Pilih kelompok lain "
                    + "pada isian Kelompok, atau cetak lewat menu Surat Peraturan → SK / Keputusan.";
                return;
            }
            // Nama orang hanya wajib untuk SK satu orang; SK berlampiran mengambil
            // nama-namanya dari tabel lampiran.
            if (!_skContoh && SkLampiran.Count == 0 && string.IsNullOrWhiteSpace(SkNama))
            {
                SkGalat = "Isi nama yang ditetapkan, muat lampiran daftar nama, "
                    + "atau tutup panel dan cetak lewat tombol Contoh.";
                return;
            }
            if (!SkPengangkatan && string.IsNullOrWhiteSpace(SkAlasan))
            {
                SkGalat = "Isi alasan pemberhentian, misalnya \"karena masa jabatan telah berakhir\".";
                return;
            }

            try
            {
                IsBusy = true;
                SkGalat = string.Empty;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                var isi = new SkPerangkatIsi
                {
                    Jenis = SkPengangkatan ? SkJenisPerangkat.Pengangkatan : SkJenisPerangkat.Pemberhentian,
                    Kelompok = SkKelompok,
                    Nomor = SkNomor.Trim(),
                    Tanggal = SkTanggal ?? DateTime.Today,
                    Nama = SkNama.Trim(),
                    NIK = SkNik.Trim(),
                    Jabatan = SkJabatan.Trim(),
                    Wilayah = SkWilayah.Trim(),
                    Mulai = SkMulai,
                    Selesai = SkSelesai,
                    Alasan = SkAlasan.Trim(),
                    Lampiran = SkLampiran.Select(b => b.KeBaris()).ToList(),
                    Contoh = _skContoh
                };

                using var stream = new MemoryStream();
                _skGenerator.GenerateSkPdf(stream, desa, isi);
                var pdfBytes = stream.ToArray();
                if (pdfBytes.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal menghasilkan PDF atau PDF kosong.");
                    return;
                }

                string outputFolder = _appConfig.PdfOutputPath;
                Directory.CreateDirectory(outputFolder);
                string sandiNama = _skContoh
                    ? "Contoh"
                    : SkLampiran.Count > 0
                        ? $"{SkLampiran.Count}orang"
                        : new string(SkNama.Trim().Where(char.IsLetterOrDigit).ToArray());
                string outputPath = Path.Combine(outputFolder,
                    $"SK_{SkKelompok.Replace("/", "-")}_{sandiNama}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(outputPath, pdfBytes);

                string sasaran = _skContoh
                    ? "Contoh SK"
                    : SkLampiran.Count > 0
                        ? $"Lampiran {SkLampiran.Count} orang"
                        : SkNama;

                _activityLog.Log("PERANGKAT", sasaran, "Cetak SK", $"{SkJenis} kelompok {SkKelompok}");

                _navigation.Navigate(_previewFactory(
                    $"SK {SkJenis} — {SkKelompok}", outputPath));
                IsPanelSkTerbuka = false;
                StatusText = _skContoh
                    ? "Contoh SK siap dipratinjau."
                    : SkLampiran.Count > 0
                        ? $"SK {SkJenis} untuk {SkLampiran.Count} orang (dengan lampiran) siap dipratinjau."
                        : $"SK {SkJenis} untuk {SkNama} siap dipratinjau.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF SK kelompok {Kelompok}", SkKelompok);
                SkGalat = "SK belum bisa dicetak. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Tampilkan daftar yang jabatan intinya belum terisi.</summary>
        private async Task TampilkanJabatanIntiKosongAsync()
        {
            var statistik = await _layanan.AmbilStatistikAsync().ConfigureAwait(true);
            if (statistik.JabatanIntiKosong.Count == 0)
            {
                await _messageService.ShowInfoAsync(
                    "Struktur inti sudah lengkap: Kepala Desa, Sekretaris Desa, dan Ketua BPD semuanya terisi.");
                return;
            }

            FilterJabatan = JabatanPerangkat.Inti.First(j => j == statistik.JabatanIntiKosong[0]);
            FilterStatus = string.Empty;
            // Pemfilter jabatan inti melintasi kelompok — kelompok yang tertinggal
            // akan menyembunyikan barisnya dan kartu panel terkesan mati.
            FilterKelompok = string.Empty;
            await LoadAsync(true);
        }

        // ---------- panel SK per jabatan ----------

        /// <summary>
        /// Susun ulang kartu aksi per jabatan dari daftar orang yang baru dimuat.
        /// Angka di kartu mengikuti hasil filter: bila halaman sedang menampilkan
        /// satu kelompok, kartu kelompok lain berhitung nol — karena itu panel
        /// disembunyikan bila daftar sedang difilter kelompok lain.
        /// </summary>
        private void SusunPanelJabatan()
        {
            var kartu = SkJabatanAksiKatalog.Susun(Items);

            KartuJabatanAksi.Clear();
            foreach (var k in kartu) KartuJabatanAksi.Add(k);

            AdanyaPanelJabatan = string.IsNullOrWhiteSpace(FilterKelompok);
        }

        /// <summary>
        /// Aksi kartu jabatan: saring daftar ke jabatannya, lalu buka panel SK yang
        /// sudah terisi sesuai sumber SK jabatan tersebut — SK Kepala Desa ke mode
        /// arsip SKD (diterbitkan Bupati), sisanya ke mode cetak/pratinjau.
        /// </summary>
        private async Task BukaPanelJabatanAsync(JabatanAksiItem? kartu)
        {
            if (kartu == null) return;

            // Saring daftar ke jabatan kartu (dan buka panelnya bila tertutup).
            FilterKelompok = kartu.Kelompok;
            FilterJabatan = kartu.NamaJabatan;
            FilterStatus = string.Empty;
            JabatanPanelTerpilih = kartu.NamaJabatan;
            await LoadAsync(false).ConfigureAwait(true);

            // Pada pemasangan baru jabatan belum punya orang: bila tidak ada baris
            // yang bisa mewakili, SK-nya disusun dari form tanpa data (mode contoh)
            // dengan kelompok kartu — operator tetap mendapat dokumen siap diisi.
            var wakil = Items.FirstOrDefault(p => p.JabatanTampil == kartu.NamaJabatan);
            await BukaPanelSkAsync(wakil, kartu).ConfigureAwait(true);
        }

        /// <summary>Bersihkan filter kelompok/jabatan: seluruh perangkat tampil lagi.</summary>
        private async Task TampilkanSemuaAsync()
        {
            JabatanPanelTerpilih = null;
            FilterKelompok = string.Empty;
            FilterJabatan = string.Empty;
            FilterStatus = string.Empty;
            await LoadAsync(true).ConfigureAwait(true);
        }

        private async Task BukaFormTambahAsync()
        {
            await Task.Yield();
            KosongkanForm();
            IsModeUbah = false;
            FormJabatan = JabatanPerangkat.KepalaDesa;
            FormStatus = StatusPerangkat.Aktif;
            IsFormTerbuka = true;
        }

        private async Task BukaFormUbahAsync(PerangkatDesa? data)
        {
            if (data is null) return;
            await Task.Yield();

            _formId = data.ID;
            _formBerkasSk = data.BerkasSK;
            IsModeUbah = true;
            FormNama = data.Nama;
            FormJabatan = data.JabatanTampil;
            FormNip = data.NIP ?? string.Empty;
            FormNik = data.NIK ?? string.Empty;
            FormJenisKelamin = PerangkatDesa.JenisKelaminRingkas(data.JenisKelamin);
            FormTempatLahir = data.TempatLahir ?? string.Empty;
            FormTanggalLahir = data.TanggalLahir;
            FormPendidikan = data.Pendidikan ?? string.Empty;
            FormAlamat = data.Alamat ?? string.Empty;
            FormDusun = data.Dusun ?? string.Empty;
            FormRt = data.RT ?? string.Empty;
            FormRw = data.RW ?? string.Empty;
            FormUnit = data.Unit ?? string.Empty;
            FormNomorHp = data.NomorHP ?? string.Empty;
            FormWhatsApp = data.WhatsApp ?? string.Empty;
            FormNomorSk = data.NomorSK ?? string.Empty;
            FormTanggalSk = data.TanggalSK;
            FormMasaJabatanMulai = data.MasaJabatanMulai;
            FormMasaJabatanSelesai = data.MasaJabatanSelesai;
            FormStatus = data.StatusTampil;
            FormCatatan = data.Catatan ?? string.Empty;
            FormGalat = string.Empty;

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
            FormNama = string.Empty;
            FormJabatan = string.Empty;
            FormNip = string.Empty;
            FormNik = string.Empty;
            FormJenisKelamin = string.Empty;
            FormTempatLahir = string.Empty;
            FormTanggalLahir = null;
            FormPendidikan = string.Empty;
            FormAlamat = string.Empty;
            FormDusun = string.Empty;
            FormRt = string.Empty;
            FormRw = string.Empty;
            FormUnit = string.Empty;
            FormNomorHp = string.Empty;
            FormWhatsApp = string.Empty;
            FormNomorSk = string.Empty;
            FormTanggalSk = null;
            FormMasaJabatanMulai = null;
            FormMasaJabatanSelesai = null;
            FormStatus = StatusPerangkat.Aktif;
            FormCatatan = string.Empty;
        }

        private async Task SimpanAsync()
        {
            var data = new PerangkatDesa
            {
                ID = _formId,
                Nama = FormNama,
                Jabatan = FormJabatan,
                NIP = FormNip,
                NIK = FormNik,
                JenisKelamin = FormJenisKelamin,
                TempatLahir = FormTempatLahir,
                TanggalLahir = FormTanggalLahir,
                Pendidikan = FormPendidikan,
                Alamat = FormAlamat,
                Dusun = FormDusun,
                RT = FormRt,
                RW = FormRw,
                Unit = FormUnit,
                NomorHP = FormNomorHp,
                WhatsApp = FormWhatsApp,
                NomorSK = FormNomorSk,
                TanggalSK = FormTanggalSk,
                // Form tidak memuat berkas, jadi nama berkas arsip dibawa dari
                // baris yang dibuka. Tanpa ini, setiap penyimpanan form akan
                // mengosongkan BerkasSK dan memutus tautan ke PDF SK tersimpan.
                BerkasSK = _formBerkasSk,
                MasaJabatanMulai = FormMasaJabatanMulai,
                MasaJabatanSelesai = FormMasaJabatanSelesai,
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

                TutupForm();
                await LoadAsync(false).ConfigureAwait(true);
                StatusText = modeUbah
                    ? $"Data {hasil.Data.Nama} diperbarui."
                    : $"{hasil.Data.Nama} ditambahkan sebagai {hasil.Data.JabatanTampil}.";
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

        private async Task HapusAsync(PerangkatDesa? data)
        {
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
                    await LoadAsync(false).ConfigureAwait(true);
                    return;
                }

                _activityLog.Log("PERANGKAT", $"{data.Nama} ({data.JabatanTampil})", "Hapus", $"ID {data.ID}");

                // Berkas SK terarsip ikut dibersihkan agar tidak tertinggal sebagai
                // berkas yatim di folder setelah baris datanya hilang. Hapus aman
                // dipanggil walau berkasnya sudah tidak ada atau sedang dibuka.
                BerkasSkPerangkat.Hapus(data.BerkasSK);

                await LoadAsync(false).ConfigureAwait(true);
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

        private void SegarkanPerintah()
        {
            MuatUlangCommand.RaiseCanExecuteChanged();
            CariCommand.RaiseCanExecuteChanged();
            TambahCommand.RaiseCanExecuteChanged();
            UbahCommand.RaiseCanExecuteChanged();
            HapusCommand.RaiseCanExecuteChanged();
            SimpanCommand.RaiseCanExecuteChanged();
            TandaiIntiKosongCommand.RaiseCanExecuteChanged();
            BukaPanelSkCommand.RaiseCanExecuteChanged();
            BukaPanelSkContohCommand.RaiseCanExecuteChanged();
            CetakSkCommand.RaiseCanExecuteChanged();
            BukaPanelJabatanCommand.RaiseCanExecuteChanged();
            TampilkanSemuaCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Isi satu kartu angka di atas daftar.</summary>
    public class PerangkatStatistikKartu
    {
        public string Label { get; init; } = string.Empty;
        public string Nilai { get; init; } = string.Empty;
        public string Keterangan { get; init; } = string.Empty;
        public bool Tekankan { get; init; }
    }

    /// <summary>
    /// Baris referensi satu kelompok di panel SK: nama kelompok, dasar hukum
    /// siap baca, dan apakah kelompok ini punya template SK yang bisa dicetak.
    /// </summary>
    public class BarisKelompokSk
    {
        public string Kelompok { get; init; } = string.Empty;
        public string DasarHukumTeks { get; init; } = string.Empty;
        public bool PunyaTemplate { get; init; }
    }
}
