using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman APBDES: arsip dokumen anggaran desa per tahun dan per tahapan —
    /// Rancangan, APBDes Awal, Perubahan ke-1/ke-2/…, Pertanggungjawaban (LPJ),
    /// serta dokumen pelaksanaan dan laporan realisasi.
    ///
    /// Angka keuangan resmi tetap milik dokumen Siskeudes; halaman ini menampung
    /// dokumennya (Word/PDF/Excel), menampilkan isinya, menyediakan rincian
    /// anggaran yang bisa disunting, lalu menyimpannya. Berkas Word yang diimpor
    /// dibaca otomatis: teksnya dipandang di halaman, dan baris "kode rekening +
    /// uraian + angka" dikenali menjadi rincian anggaran.
    /// </summary>
    public partial class ApbdesViewModel : ObservableObject, IDisposable
    {
        public static readonly string[] JenisDaftar = ApbdesStore.JenisDaftar;

        private readonly IApbdesWordImpor _wordImpor;
        private readonly IDesaRepository? _desaRepo;
        private readonly IKlienApbdesPusat? _klienPusat;
        private readonly SuDesApp.Wilayah.WilayahDataStore? _wilayah;
        private readonly ILogger<ApbdesViewModel> _logger;
        private readonly Func<DateTime> _jam;
        private readonly NavigationService? _navigasi;
        private readonly Func<string, string, PdfPreviewViewModel>? _previewFactory;
        private readonly System.Windows.Threading.DispatcherTimer _tahunTimer;
        private int _tahunTerakhirTerpantau;

        private List<ApbdesStore.Dokumen> _semua = new();
        private int _tahun;
        private DokumenItem? _terpilih;
        private bool _sedangMemuat;
        private bool _sedangMuatPusat;
        private bool _pindahDokumen;
        private string _pesan = string.Empty;
        private string _pesanGalat = string.Empty;
        private string _pesanPeringatan = string.Empty;
        private string _statusPusat = string.Empty;

        /// <summary>
        /// Teks sumber yang ditulis di keterangan dokumen publikasi pusat yang
        /// ditarik otomatis, supaya operator tahu dari mana angka kolom
        /// "publikasi" datangnya.
        /// </summary>
        internal const string SumberProdeskel = "Prodeskel (prodeskel.binapemdes.kemendagri.go.id)";

        /// <param name="navigasi">
        /// Navigasi halaman. Kosong = tombol Pratinjau membuka berkas dengan
        /// aplikasi luar (sama seperti Buka berkas) alih-alih gagal diam-diam.
        /// </param>
        /// <param name="previewFactory">
        /// Pabrik halaman pratinjau PDF, pola yang sama dengan IDM dan Agenda Surat.
        /// </param>
        public ApbdesViewModel(
            IApbdesWordImpor wordImpor,
            ILogger<ApbdesViewModel>? logger = null,
            Func<DateTime>? jam = null,
            NavigationService? navigasi = null,
            Func<string, string, PdfPreviewViewModel>? previewFactory = null,
            IDesaRepository? desaRepo = null,
            IKlienApbdesPusat? klienPusat = null,
            SuDesApp.Wilayah.WilayahDataStore? wilayah = null)
        {
            _wordImpor = wordImpor ?? throw new ArgumentNullException(nameof(wordImpor));
            _desaRepo = desaRepo;
            _klienPusat = klienPusat;
            _wilayah = wilayah;
            _logger = logger ?? NullLogger<ApbdesViewModel>.Instance;
            _jam = jam ?? (() => DateTime.Now);
            _navigasi = navigasi;
            _previewFactory = previewFactory;

            Dokumen = new ObservableCollection<DokumenItem>();
            Pos = new ObservableCollection<PosItem>();
            Pos.CollectionChanged += (_, _) => RingkasanBerubah();

            _tahun = _jam().Year;
            _tahunTerakhirTerpantau = _tahun;

            TambahCommand = new RelayCommand(Tambah);
            SimpanCommand = new RelayCommand(Simpan);
            MuatUlangCommand = new RelayCommand(MuatUlang);
            HapusDokumenCommand = new RelayCommand(HapusDokumen, () => DokumenTerpilih != null);
            HapusBerkasCommand = new RelayCommand(HapusBerkas, () => DokumenTerpilih?.AdaBerkas == true);
            BukaBerkasCommand = new RelayCommand(BukaBerkas, () => DokumenTerpilih?.AdaBerkas == true);
            PratinjauBerkasCommand = new RelayCommand(PratinjauBerkas, () => BisaPratinjau);
            TambahPosCommand = new RelayCommand(TambahPos, () => DokumenTerpilih != null);
            ImporWordCommand = new AsyncRelayCommand(ImporWordAsync, () => !SedangMemuat && DokumenTerpilih != null);
            ImporPublikasiCommand = new AsyncRelayCommand(ImporPublikasiAsync, () => !SedangMemuat);
            MuatDariPusatCommand = new AsyncRelayCommand(
                MuatDariPusatAsync, () => !SedangMemuat && !SedangMuatPusat);
            LampirkanBerkasCommand = new RelayCommand(
                PilihBerkasUntukDilampirkan, () => DokumenTerpilih != null);

            Muat();

            // Periksa perubahan tahun secara berkala agar daftar tetap mengikuti
            // kalender meski halaman/aplikasi dibiarkan terbuka.
            _tahunTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _tahunTimer.Tick += PeriksaPergantianTahun;
            _tahunTimer.Start();
        }

        public string HeaderTitle => "APBDES DESA";

        public string HeaderSubtitle =>
            "Arsip dokumen anggaran desa: Rancangan, APBDes Awal, Perubahan (ke-1, ke-2, …), "
            + "Pertanggungjawaban, dokumen pelaksanaan, dan laporan realisasi — plus riwayat "
            + "perbandingan anggaran & realisasi tahun-tahun sebelumnya, termasuk anggaran "
            + "publikasi pemerintah pusat (Prodeskel) yang ditarik otomatis.";

        /// <summary>Tahun anggaran yang sedang dibuka.</summary>
        public int Tahun
        {
            get => _tahun;
            set
            {
                if (!SetProperty(ref _tahun, value)) return;
                SimpanKeModel();
                SegarkanDaftar();
                OnPropertyChanged(nameof(JudulDaftarDokumen));
                Pesan = string.Empty;
            }
        }

        /// <summary>
        /// Tahun menurut jam komputer. Dibaca ulang setiap kali daftar tahun
        /// disusun — aplikasi yang dibiarkan terbuka melewati pergantian tahun
        /// tetap mendapat tahun berjalan yang baru tanpa dibuka ulang.
        /// </summary>
        public int TahunBerjalan => _jam().Year;

        /// <summary>
        /// Pilihan tahun anggaran: tahun berjalan (selalu ada), tahun sebelum dan
        /// sesudahnya untuk penyusunan anggaran, seluruh tahun yang sudah punya
        /// dokumen, dan tahun yang sedang dibuka. Diurutkan dari tahun terbaru
        /// supaya tahun berjalan langsung terlihat di puncak daftar.
        /// </summary>
        public IReadOnlyList<PilihanTahun> TahunPilihan
        {
            get
            {
                int berjalan = TahunBerjalan;
                var tahun = new HashSet<int>(_semua.Select(d => d.Tahun))
                {
                    berjalan - 1, berjalan, berjalan + 1,
                };

                // Tahun yang sedang dibuka tidak boleh hilang dari daftar: bila
                // tidak ada, ComboBox akan menampilkan pilihan kosong.
                tahun.Add(Tahun);

                return tahun
                    .OrderByDescending(t => t)
                    .Select(t => new PilihanTahun(t))
                    .ToList();
            }
        }

        /// <summary>Judul daftar dokumen — tahun yang sedang dibuka ikut terbaca.</summary>
        public string JudulDaftarDokumen =>
            "DOKUMEN TAHUN " + Tahun.ToString(CultureInfo.InvariantCulture);

        /// <summary>Belum ada dokumen pada tahun yang sedang dibuka.</summary>
        public bool TidakAdaDokumen => Dokumen.Count == 0;

        /// <summary>Kebalikan <see cref="AdaDokumenTerpilih"/> — panel sambutan.</summary>
        public bool TidakAdaDokumenTerpilih => _terpilih == null;

        /// <summary>
        /// Satu baris pada daftar turun tahun anggaran.
        /// </summary>
        public sealed class PilihanTahun
        {
            public PilihanTahun(int nilai)
            {
                Nilai = nilai;
            }

            public int Nilai { get; }

            public string Label => Nilai.ToString(CultureInfo.InvariantCulture);
        }

        private void PeriksaPergantianTahun(object? sender, EventArgs e)
        {
            int tahunBaru = _jam().Year;
            if (tahunBaru == _tahunTerakhirTerpantau) return;

            bool mengikutiTahunTerbaru = Tahun == _tahunTerakhirTerpantau;
            _tahunTerakhirTerpantau = tahunBaru;
            SegarkanPilihanTahun();

            // Jika pengguna sedang melihat tahun yang baru saja berganti,
            // pindahkan otomatis ke tahun baru. Pilihan tahun historis tetap.
            if (mengikutiTahunTerbaru) Tahun = tahunBaru;
        }

        public void Dispose()
        {
            _tahunTimer.Stop();
            _tahunTimer.Tick -= PeriksaPergantianTahun;
        }

        /// <summary>Pilihan nomor urut perubahan (Perubahan ke-1 … ke-5).</summary>
        public IReadOnlyList<int> UrutanPerubahanPilihan { get; } = new[] { 1, 2, 3, 4, 5 };

        public ObservableCollection<DokumenItem> Dokumen { get; }

        public DokumenItem? DokumenTerpilih
        {
            get => _terpilih;
            set
            {
                if (ReferenceEquals(_terpilih, value)) return;

                // Suntingan pada dokumen sebelumnya disimpan dulu di memori, supaya
                // berpindah dokumen tidak membuang perubahan yang belum ditulis ke disk.
                SimpanKeModel();
                SetProperty(ref _terpilih, value);

                _pindahDokumen = true;
                try
                {
                    IsiPosDariModel();
                }
                finally
                {
                    _pindahDokumen = false;
                }

                Pesan = string.Empty;
                PesanGalat = string.Empty;
                OnPropertyChanged(nameof(AdaDokumenTerpilih));
                OnPropertyChanged(nameof(TidakAdaDokumenTerpilih));
                OnPropertyChanged(nameof(DokumenTampil));
                OnPropertyChanged(nameof(BerkasTampil));
                OnPropertyChanged(nameof(TeksTampil));
                OnPropertyChanged(nameof(AdaTeks));
                OnPropertyChanged(nameof(PerluUrutanPerubahan));
                RingkasanBerubah();
                PerbaruiTombol();
            }
        }

        public bool AdaDokumenTerpilih => _terpilih != null;

        /// <summary>Benar bila dokumen terpilih memakai nomor urut perubahan.</summary>
        public bool PerluUrutanPerubahan =>
            _terpilih != null &&
            _terpilih.Jenis.Equals("APBDes Perubahan", StringComparison.OrdinalIgnoreCase);

        public string JudulTampilan => _terpilih == null
            ? "Belum ada dokumen yang dipilih"
            : ApbdesStore.LabelJenis(_terpilih.Jenis, _terpilih.PerubahanKe);

        public string DokumenTampil
        {
            get
            {
                if (_terpilih == null) return string.Empty;

                var bagian = new List<string>();
                if (!string.IsNullOrWhiteSpace(_terpilih.Nomor)) bagian.Add("Nomor " + _terpilih.Nomor);
                string tanggal = _terpilih.TanggalTampil;
                if (tanggal.Length > 0) bagian.Add(tanggal);
                return string.Join(" · ", bagian);
            }
        }

        public string BerkasTampil
        {
            get
            {
                if (_terpilih == null) return "Tidak ada dokumen yang dipilih.";

                if (!_terpilih.AdaBerkas) return "Belum ada berkas sumber yang dilampirkan.";

                return string.IsNullOrWhiteSpace(_terpilih.BerkasAsli)
                    ? _terpilih.Berkas
                    : _terpilih.BerkasAsli + "  (salinan: " + _terpilih.Berkas + ")";
            }
        }

        /// <summary>
        /// Berkas terpilih bisa dipratinjau di halaman pratinjau aplikasi.
        /// Hanya PDF yang punya mesin render (SudesPdf); Word/Excel tetap lewat
        /// tombol Buka berkas karena aplikasi tidak merender format itu.
        /// </summary>
        public bool BisaPratinjau => _terpilih?.AdaBerkas == true
            && string.Equals(Path.GetExtension(_terpilih.Berkas), ".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>Keterangan tombol Pratinjau — menjelaskan batas formatnya.</summary>
        public string KeteranganPratinjau => _terpilih?.AdaBerkas != true
            ? "Lampirkan berkas dulu untuk melihat pratinjaunya."
            : BisaPratinjau
                ? "Lihat berkas PDF ini di dalam aplikasi."
                : "Pratinjau di aplikasi hanya untuk PDF. Berkas ini dibuka dengan "
                  + "aplikasi luar lewat tombol Buka berkas.";

        public bool AdaTeks => TeksTampil.Length > 0;

        public string TeksTampil => _terpilih?.Teks ?? string.Empty;

        public ObservableCollection<PosItem> Pos { get; }

        public bool SedangMemuat
        {
            get => _sedangMemuat;
            private set
            {
                if (!SetProperty(ref _sedangMemuat, value)) return;
                OnPropertyChanged(nameof(StatusMemuat));
                PerbaruiTombol();
            }
        }

        /// <summary>Keterangan singkat saat berkas sedang dibaca (halaman tetap responsif).</summary>
        public string StatusMemuat => _sedangMemuat ? "Sedang membaca berkas…" : string.Empty;

        public bool SedangMuatPusat
        {
            get => _sedangMuatPusat;
            private set
            {
                if (!SetProperty(ref _sedangMuatPusat, value)) return;
                PerbaruiTombol();
            }
        }

        /// <summary>
        /// Status terakhir penarikan anggaran publik pemerintah pusat (Prodeskel)
        /// — tampil pada kartu Riwayat, termasuk alasannya bila situs tak terjangkau
        /// atau bentuk halamannya belum dikenali.
        /// </summary>
        public string StatusPusat
        {
            get => _statusPusat;
            private set => SetProperty(ref _statusPusat, value);
        }

        public string Pesan
        {
            get => _pesan;
            private set
            {
                if (SetProperty(ref _pesan, value)) OnPropertyChanged(nameof(AdaPesan));
            }
        }

        public bool AdaPesan => _pesan.Length > 0;

        public string PesanGalat
        {
            get => _pesanGalat;
            private set
            {
                if (SetProperty(ref _pesanGalat, value)) OnPropertyChanged(nameof(AdaGalat));
            }
        }

        public bool AdaGalat => _pesanGalat.Length > 0;

        public string PesanPeringatan
        {
            get => _pesanPeringatan;
            private set
            {
                if (SetProperty(ref _pesanPeringatan, value)) OnPropertyChanged(nameof(AdaPeringatan));
            }
        }

        public bool AdaPeringatan => _pesanPeringatan.Length > 0;

        public ICommand TambahCommand { get; }
        public ICommand ImporWordCommand { get; }
        public ICommand ImporPublikasiCommand { get; }
        /// <summary>Tarik anggaran yang sudah dipublikasikan pemerintah pusat
        /// (Prodeskel Kemendagri) ke panel Riwayat — jalur utama; impor Excel tetap
        /// ada sebagai cadangan.</summary>
        public ICommand MuatDariPusatCommand { get; }
        public ICommand LampirkanBerkasCommand { get; }
        public ICommand SimpanCommand { get; }
        public ICommand MuatUlangCommand { get; }
        public ICommand HapusDokumenCommand { get; }
        public ICommand HapusBerkasCommand { get; }
        public ICommand BukaBerkasCommand { get; }
        public ICommand PratinjauBerkasCommand { get; }
        public ICommand TambahPosCommand { get; }


    }
}
