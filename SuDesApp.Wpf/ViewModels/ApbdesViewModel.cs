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
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;

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
    public class ApbdesViewModel : ObservableObject, IDisposable
    {
        public static readonly string[] JenisDaftar = ApbdesStore.JenisDaftar;

        private readonly IApbdesWordImpor _wordImpor;
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
        private bool _pindahDokumen;
        private string _pesan = string.Empty;
        private string _pesanGalat = string.Empty;
        private string _pesanPeringatan = string.Empty;

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
            Func<string, string, PdfPreviewViewModel>? previewFactory = null)
        {
            _wordImpor = wordImpor ?? throw new ArgumentNullException(nameof(wordImpor));
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
            + "Pertanggungjawaban, dokumen pelaksanaan, dan laporan realisasi. Angka keuangan tetap "
            + "milik dokumen Siskeudes — di sini dokumennya ditampilkan, disunting, dan disimpan.";

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
        /// Hanya PDF yang punya mesin render (PDFium); Word/Excel tetap lewat
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
        public ICommand LampirkanBerkasCommand { get; }
        public ICommand SimpanCommand { get; }
        public ICommand MuatUlangCommand { get; }
        public ICommand HapusDokumenCommand { get; }
        public ICommand HapusBerkasCommand { get; }
        public ICommand BukaBerkasCommand { get; }
        public ICommand PratinjauBerkasCommand { get; }
        public ICommand TambahPosCommand { get; }

        // ===================== Ringkasan =====================

        private void RingkasanBerubah()
        {
            foreach (var nama in new[]
                     {
                         nameof(TotalAnggaranTampil), nameof(TotalRealisasiTampil), nameof(SisaTampil),
                         nameof(PendapatanTampil), nameof(BelanjaTampil), nameof(PembiayaanTampil),
                         nameof(TidakAdaPos)
                     })
            {
                OnPropertyChanged(nama);
            }

            if (_terpilih != null)
            {
                _terpilih.PerbaruiRingkasan();
            }
        }

        /// <summary>Belum ada baris rincian — tabel menampilkan keterangan, bukan ruang kosong.</summary>
        public bool TidakAdaPos => Pos.Count == 0;

        public string TotalAnggaranTampil => Pos.Count == 0 ? "—" : Rupiah(Pos.Sum(p => p.Anggaran));

        public string TotalRealisasiTampil => Pos.Count == 0 ? "—" : Rupiah(Pos.Sum(p => p.Realisasi));

        public string SisaTampil
        {
            get
            {
                if (Pos.Count == 0) return "—";
                long sisa = Pos.Sum(p => p.Anggaran) - Pos.Sum(p => p.Realisasi);
                return Rupiah(sisa);
            }
        }

        public string PendapatanTampil => RingkasanKode("4");
        public string BelanjaTampil => RingkasanKode("5");
        public string PembiayaanTampil => RingkasanKode("6");

        /// <summary>Jumlah per kelompok kode rekening pertama (4 Pendapatan, 5 Belanja, 6 Pembiayaan).</summary>
        private string RingkasanKode(string awalan)
        {
            var nyala = Pos.Where(p => (p.Kode ?? string.Empty).StartsWith(awalan, StringComparison.Ordinal)).ToList();
            if (nyala.Count == 0) return "—";

            long anggaran = nyala.Sum(p => p.Anggaran);
            long realisasi = nyala.Sum(p => p.Realisasi);
            string persen = anggaran > 0 ? $" ({Math.Round(realisasi * 100.0 / anggaran)}%)" : "";
            return $"{Rupiah(anggaran)} — terpakai {Rupiah(realisasi)}{persen}";
        }

        /// <summary>Rupiah format ribuan Indonesia ("Rp1.234.567").</summary>
        internal static string Rupiah(long nominal) =>
            "Rp" + nominal.ToString("N0", new CultureInfo("id-ID"));

        // ===================== Muat & simpan =====================

        private void Muat()
        {
            _semua = ApbdesStore.Muat();
            SegarkanDaftar();
            SegarkanPilihanTahun();
        }

        /// <summary>
        /// Segarkan daftar tahun di ComboBox. Dipanggil setiap kali arsip dibaca
        /// atau dimuat ulang, sehingga tahun berjalan menurut jam komputer ikut
        /// ditambahkan walau aplikasi sudah lama terbuka (melewati pergantian tahun).
        /// </summary>
        private void SegarkanPilihanTahun()
        {
            OnPropertyChanged(nameof(TahunPilihan));
        }

        private void MuatUlang()
        {
            SimpanKeModel();
            Muat();
            Pesan = "Daftar dokumen dimuat ulang dari arsip komputer ini.";
            PesanGalat = string.Empty;
        }

        /// <summary>Isi daftar dokumen untuk tahun aktif dari daftar di memori.</summary>
        private void SegarkanDaftar(DokumenItem? pilih = null)
        {
            string? idPilihan = (pilih ?? _terpilih)?.Id;

            Dokumen.Clear();
            foreach (var dok in _semua.Where(d => d.Tahun == Tahun)
                         .OrderBy(d => d.Jenis == "APBDes Perubahan" ? 1 : 0)
                         .ThenBy(d => d.PerubahanKe)
                         .ThenBy(d => d.Jenis))
            {
                Dokumen.Add(new DokumenItem(dok));
            }

            var berikut = idPilihan == null
                ? Dokumen.FirstOrDefault()
                : Dokumen.FirstOrDefault(d => d.Id == idPilihan) ?? Dokumen.FirstOrDefault();

            _terpilih = null;
            DokumenTerpilih = berikut;

            OnPropertyChanged(nameof(TidakAdaDokumen));
            OnPropertyChanged(nameof(TahunPilihan));
            PerbaruiTombol();
        }

        private void Tambah()
        {
            SimpanKeModel();

            var dok = new ApbdesStore.Dokumen
            {
                Tahun = Tahun,
                Jenis = "APBDes Awal",
                PerubahanKe = 0,
                Judul = "APBDes " + Tahun.ToString(CultureInfo.InvariantCulture),
                Tanggal = _jam().ToString("yyyy-MM-dd"),
                Diubah = _jam().ToString("s"),
            };

            _semua.Add(dok);
            var item = new DokumenItem(dok);
            Dokumen.Add(item);
            DokumenTerpilih = item;

            OnPropertyChanged(nameof(TidakAdaDokumen));
            Pesan = "Dokumen baru ditambahkan. Isi atau impor berkasnya, lalu tekan Simpan.";
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;
        }

        private void Simpan()
        {
            if (!SimpanKeModel())
            {
                Pesan = string.Empty;
                return;
            }

            PesanPeringatan = string.Empty;
            PesanGalat = string.Empty;

            if (ApbdesStore.Simpan(_semua))
            {
                Pesan = $"Tersimpan di komputer ini: {_semua.Count} dokumen APBDES di arsip SuDesApp.";
                foreach (var d in Dokumen) d.PerbaruiRingkasan();
            }
            else
            {
                Pesan = string.Empty;
                PesanGalat = "Gagal menyimpan berkas APBDES — periksa disk/hak akses.";
            }
        }

        /// <summary>
        /// Pindahkan suntingan dokumen yang sedang dibuka (termasuk tabel rincian)
        /// ke model di memori. Dianggap berhasil bila ada dokumen terpilih.
        /// </summary>
        private bool SimpanKeModel()
        {
            if (_terpilih == null) return false;
            if (_pindahDokumen) return false;

            _terpilih.TulisKeModel();
            _terpilih.Model.Pos = Pos
                .Where(p => !string.IsNullOrWhiteSpace(p.Kode) || !string.IsNullOrWhiteSpace(p.Uraian)
                            || p.Anggaran != 0 || p.Realisasi != 0)
                .Select(p => new ApbdesStore.Pos
                {
                    Kode = p.Kode?.Trim() ?? string.Empty,
                    Uraian = p.Uraian?.Trim() ?? string.Empty,
                    Anggaran = p.Anggaran,
                    Realisasi = p.Realisasi,
                    Keterangan = p.Keterangan?.Trim() ?? string.Empty,
                })
                .ToList();
            _terpilih.Model.Diubah = _jam().ToString("s");
            _terpilih.PerbaruiRingkasan();
            return true;
        }

        private void IsiPosDariModel()
        {
            foreach (var item in Pos.ToList()) item.PropertyChanged -= PosBerubah;
            Pos.Clear();

            if (_terpilih != null)
            {
                foreach (var p in _terpilih.Model.Pos)
                {
                    Pos.Add(BuatPos(p));
                }
            }

            RingkasanBerubah();
        }

        /// <summary>
        /// Buat baris tabel sekaligus menyambungkannya ke ringkasan: setiap
        /// perubahan angka pada baris harus langsung terlihat di total, tanpa
        /// menunggu disimpan.
        /// </summary>
        private PosItem BuatPos(ApbdesStore.Pos pos)
        {
            var item = new PosItem(pos, HapusPos);
            item.PropertyChanged += PosBerubah;
            return item;
        }

        private void PosBerubah(object? sender, PropertyChangedEventArgs e)
        {
            if (_pindahDokumen) return;
            RingkasanBerubah();
        }

        private void TambahPos()
        {
            if (_terpilih == null) return;
            Pos.Add(BuatPos(new ApbdesStore.Pos { Kode = "5" }));
            Pesan = string.Empty;
        }

        private void HapusPos(PosItem item)
        {
            item.PropertyChanged -= PosBerubah;
            Pos.Remove(item);
        }

        private void HapusDokumen()
        {
            if (_terpilih == null) return;

            var dihapus = _terpilih;
            ApbdesStore.HapusBerkas(dihapus.Model.Berkas);
            _semua.RemoveAll(d => d.Id == dihapus.Model.Id);
            Dokumen.Remove(dihapus);

            _terpilih = null;
            DokumenTerpilih = Dokumen.FirstOrDefault();

            Pesan = "Dokumen \"" + ApbdesStore.LabelJenis(dihapus.Jenis, dihapus.PerubahanKe)
                    + "\" dihapus dari daftar — tekan Simpan supaya penghapusan tetap tersimpan.";
            PesanPeringatan = string.Empty;
        }

        // ===================== Berkas =====================

        private void HapusBerkas()
        {
            if (_terpilih == null || !_terpilih.AdaBerkas) return;

            ApbdesStore.HapusBerkas(_terpilih.Model.Berkas);
            _terpilih.Model.Berkas = string.Empty;
            _terpilih.Model.BerkasAsli = string.Empty;
            _terpilih.Model.Teks = string.Empty;
            _terpilih.PerbaruiBerkas();

            OnPropertyChanged(nameof(BerkasTampil));
            OnPropertyChanged(nameof(TeksTampil));
            OnPropertyChanged(nameof(AdaTeks));
            PerbaruiTombol();
            Pesan = "Berkas salinan dihapus — tekan Simpan supaya perubahan daftar dokumen tetap tersimpan.";
        }

        private void BukaBerkas()
        {
            if (_terpilih == null) return;

            var jalur = ApbdesStore.JalurBerkas(_terpilih.Model.Berkas);
            if (jalur == null || !File.Exists(jalur))
            {
                PesanGalat = "Berkas salinan tidak ada di arsip. Impor berkasnya lagi lewat tombol Impor dari Word.";
                return;
            }

            BukaDiAplikasiLuar(jalur);
        }

        /// <summary>
        /// Pratinjau berkas arsip DI DALAM aplikasi — ditempatkan di halaman
        /// pratinjau PDF yang sama dengan IDM, Agenda Surat, dan Daftar Hadir.
        /// Berkas diambil dari folder arsip APBDes (bukan folder PDF sementara),
        /// jadi tidak ikut terhapus pembersih dokumen temp. Word/Excel tidak
        /// dirender; tombolnya nonaktif dan tooltip mengarahkan ke Buka berkas.
        /// </summary>
        private void PratinjauBerkas()
        {
            if (_terpilih == null) return;

            var jalur = ApbdesStore.JalurBerkas(_terpilih.Model.Berkas);
            if (jalur == null || !File.Exists(jalur))
            {
                PesanGalat = "Berkas salinan tidak ada di arsip. Impor berkasnya lagi lewat tombol Impor dari Word.";
                return;
            }

            if (!BisaPratinjau)
            {
                // Command sudah nonaktif untuk format ini; penjaga ini hanya untuk
                // pemanggilan langsung (mis. dari uji) supaya pesannya tetap jelas.
                Pesan = "Pratinjau di aplikasi hanya untuk berkas PDF — berkas ini dibuka "
                        + "dengan aplikasi luar lewat tombol Buka berkas.";
                PesanGalat = string.Empty;
                return;
            }

            if (_navigasi == null || _previewFactory == null)
            {
                // Tanpa layanan navigasi (mis. halaman dirakit manual) jangan diam:
                // berkasnya tetap dibuka dengan aplikasi luar.
                BukaDiAplikasiLuar(jalur);
                return;
            }

            string judul = _terpilih.BerkasAsli.Length > 0 ? _terpilih.BerkasAsli : _terpilih.Berkas;
            Pesan = string.Empty;
            PesanGalat = string.Empty;
            _navigasi.Navigate(_previewFactory("Pratinjau " + judul, jalur));
        }

        private void BukaDiAplikasiLuar(string jalur)
        {
            try
            {
                Process.Start(new ProcessStartInfo(jalur) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka berkas APBDes {Jalur}", jalur);
                PesanGalat = "Tidak bisa membuka berkas: " + ex.Message;
            }
        }

        // ===================== Impor Word =====================

        /// <summary>
        /// Impor berkas dokumen APBDes (Word): berkasnya disalin ke arsip, teksnya
        /// ditampilkan, dan baris anggaran yang dikenali mengisi tabel rincian.
        /// Semua hasilnya masih bisa disunting sebelum disimpan.
        /// </summary>
        private async Task ImporWordAsync()
        {
            if (_terpilih == null)
            {
                PesanGalat = "Pilih dulu dokumennya (atau tekan Tambah Dokumen) supaya berkas bisa ditempelkan ke dokumen itu.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Pilih berkas dokumen APBDes (Word)",
                Filter = "Dokumen Word|*.docx;*.doc|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|Semua Berkas (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true) return;

            SedangMemuat = true;
            Pesan = string.Empty;
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;

            try
            {
                var hasil = await _wordImpor.BacaAsync(dialog.FileName);
                TerapkanImpor(dialog.FileName, hasil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengimpor berkas APBDes");
                PesanGalat = "Gagal membaca berkas: " + ex.Message;
            }
            finally
            {
                SedangMemuat = false;
            }
        }

        /// <summary>
        /// Lampirkan berkas dokumen APBDes apa adanya (PDF/Excel/Word) ke arsip:
        /// berkasnya disalin dan tidak dibaca isinya. Ini jalur utama untuk hasil
        /// cetak Siskeudes; jalur Word dipakai bila rinciannya ingin dikenali otomatis.
        /// </summary>
        private void PilihBerkasUntukDilampirkan()
        {
            if (_terpilih == null)
            {
                PesanGalat = "Pilih dulu dokumennya (atau tekan Tambah Dokumen) supaya berkas bisa ditempelkan ke dokumen itu.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Pilih berkas dokumen APBDes",
                Filter = "Dokumen Siskeudes|*.pdf;*.xlsx;*.xls;*.csv;*.docx;*.doc"
                         + "|PDF (*.pdf)|*.pdf|Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Word (*.docx;*.doc)|*.docx;*.doc"
                         + "|Semua Berkas (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true) return;

            Pesan = string.Empty;
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;

            try
            {
                LampirkanBerkas(dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal melampirkan berkas APBDes");
                PesanGalat = "Gagal melampirkan berkas: " + ex.Message;
            }
        }

        /// <summary>
        /// Salin berkas sumber ke arsip tanpa membaca isinya. Dipisah dari dialog
        /// berkas supaya bisa diuji tanpa jendela.
        /// </summary>
        internal bool LampirkanBerkas(string jalurBerkas)
        {
            var dok = _terpilih;
            if (dok == null) return false;

            string lama = dok.Model.Berkas;
            string nama = ApbdesStore.SimpanBerkas(jalurBerkas);
            dok.Model.Berkas = nama;
            dok.Model.BerkasAsli = Path.GetFileName(jalurBerkas);

            // Isi yang dibaca dari berkas lama tidak berlaku lagi untuk berkas baru.
            dok.Model.Teks = string.Empty;
            dok.PerbaruiBerkas();
            if (!string.IsNullOrEmpty(lama) && !string.Equals(lama, nama, StringComparison.OrdinalIgnoreCase))
            {
                ApbdesStore.HapusBerkas(lama);
            }

            OnPropertyChanged(nameof(BerkasTampil));
            OnPropertyChanged(nameof(TeksTampil));
            OnPropertyChanged(nameof(AdaTeks));
            PerbaruiTombol();
            Pesan = "Berkas \"" + dok.Model.BerkasAsli + "\" disimpan ke arsip. "
                    + "Tekan Simpan supaya tercatat di daftar dokumen.";
            return true;
        }

        /// <summary>
        /// Terapkan hasil pembacaan berkas ke dokumen yang sedang dibuka: berkasnya
        /// disalin ke arsip, identitas yang kosong diisi dari berkas, dan tabel
        /// rincian diganti baris anggaran hasil pembacaan.
        ///
        /// Dipisah dari pemilihan berkas (dialog) supaya bisa diuji tanpa jendela.
        /// </summary>
        internal bool TerapkanImpor(string jalurBerkas, HasilImporApbdes hasil)
        {
            var dok = _terpilih;
            if (dok == null) return false;

            string lama = dok.Model.Berkas;
            string nama = ApbdesStore.SimpanBerkas(jalurBerkas);
            dok.Model.Berkas = nama;
            dok.Model.BerkasAsli = hasil.NamaBerkas;
            dok.Model.Teks = hasil.Teks;
            dok.PerbaruiBerkas();

            // Berkas lama pada dokumen yang sama tidak perlu disimpan dua kali.
            if (!string.IsNullOrEmpty(lama) && !string.Equals(lama, nama, StringComparison.OrdinalIgnoreCase))
            {
                ApbdesStore.HapusBerkas(lama);
            }

            if (string.IsNullOrWhiteSpace(dok.Nomor) && hasil.Nomor.Length > 0) dok.Nomor = hasil.Nomor;
            if (hasil.Tahun.HasValue && hasil.Tahun.Value > 2000) dok.Tahun = hasil.Tahun.Value;
            if (!string.IsNullOrWhiteSpace(hasil.Jenis) && JenisDaftar.Contains(hasil.Jenis!))
            {
                dok.Jenis = hasil.Jenis!;
            }

            if (dok.Tanggal == null)
            {
                // Tanggal dokumen tidak selalu bisa dibaca dari berkas; diisi tanggal
                // hari ini supaya ada titik awal yang mudah diperbaiki pengguna.
                dok.Tanggal = _jam().Date;
            }

            if (hasil.Anggaran.Count > 0)
            {
                foreach (var item in Pos.ToList()) item.PropertyChanged -= PosBerubah;
                Pos.Clear();
                foreach (var baris in hasil.Anggaran)
                {
                    Pos.Add(BuatPos(new ApbdesStore.Pos
                    {
                        Kode = baris.Kode,
                        Uraian = baris.Uraian,
                        Anggaran = baris.Anggaran,
                        Realisasi = baris.Realisasi,
                    }));
                }

                RingkasanBerubah();
            }

            // Tahun dokumen bisa berubah dari berkas; daftar tahun ikut menyesuaikan.
            SegarkanPilihanTahun();
            if (dok.Tahun != Tahun)
            {
                Pesan = "Berkas \"" + hasil.NamaBerkas + "\" disimpan, tetapi tahun dokumen bergeser ke "
                        + dok.Tahun + " — pindah ke tahun itu untuk melihatnya.";
                SimpanKeModel();
                SegarkanDaftar();
            }
            else
            {
                Pesan = "Berkas \"" + hasil.NamaBerkas + "\" disimpan ke arsip: "
                        + hasil.Anggaran.Count + " baris anggaran terbaca. Periksa tabelnya, lalu tekan Simpan.";
            }

            PesanPeringatan = hasil.Peringatan.Count > 0 ? string.Join(" ", hasil.Peringatan) : string.Empty;

            _logger.LogInformation(
                "Impor APBDes {Berkas}: {Baris} baris anggaran untuk dokumen {Dokumen}.",
                hasil.NamaBerkas, hasil.Anggaran.Count, dok.Judul);

            return true;
        }

        private void PerbaruiTombol()
        {
            foreach (var perintah in new[]
                     {
                         HapusDokumenCommand, HapusBerkasCommand, BukaBerkasCommand, PratinjauBerkasCommand,
                         TambahPosCommand, LampirkanBerkasCommand
                     })
            {
                (perintah as RelayCommand)?.RaiseCanExecuteChanged();
            }

            (ImporWordCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(BisaPratinjau));
            OnPropertyChanged(nameof(KeteranganPratinjau));
            OnPropertyChanged(nameof(JudulTampilan));
        }

        // ===================== Baris tabel =====================

        /// <summary>Satu dokumen pada daftar arsip.</summary>
        public sealed class DokumenItem : ObservableObject
        {
            private string _jenis = "APBDes Awal";
            private int _perubahanKe;
            private string _judul = string.Empty;
            private string _nomor = string.Empty;
            private DateTime? _tanggal;
            private int _tahun;
            private string _keterangan = string.Empty;

            public DokumenItem(ApbdesStore.Dokumen model)
            {
                Model = model;
                _tahun = model.Tahun;
                _jenis = model.Jenis;
                _perubahanKe = model.PerubahanKe;
                _judul = model.Judul;
                _nomor = model.Nomor;
                _keterangan = model.Keterangan;
                _tanggal = DateTime.TryParseExact(model.Tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var t)
                    ? t
                    : null;
            }

            public ApbdesStore.Dokumen Model { get; }

            public string Id => Model.Id;

            public int Tahun
            {
                get => _tahun;
                set
                {
                    if (SetProperty(ref _tahun, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public string Jenis
            {
                get => _jenis;
                set
                {
                    if (SetProperty(ref _jenis, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public int PerubahanKe
            {
                get => _perubahanKe;
                set
                {
                    if (SetProperty(ref _perubahanKe, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public string Judul
            {
                get => _judul;
                set => SetProperty(ref _judul, value);
            }

            public string Nomor
            {
                get => _nomor;
                set => SetProperty(ref _nomor, value);
            }

            public DateTime? Tanggal
            {
                get => _tanggal;
                set
                {
                    if (SetProperty(ref _tanggal, value))
                    {
                        OnPropertyChanged(nameof(Label));
                        OnPropertyChanged(nameof(TanggalTampil));
                    }
                }
            }

            public string Keterangan
            {
                get => _keterangan;
                set => SetProperty(ref _keterangan, value);
            }

            public string Label => ApbdesStore.LabelJenis(_jenis, _perubahanKe) +
                                   (TanggalTampil.Length > 0 ? " · " + TanggalTampil : "");

            public string TanggalTampil => _tanggal?.ToString("dd MMM yyyy", new CultureInfo("id-ID")) ?? "";

            public string Teks => Model.Teks;

            public bool AdaBerkas => !string.IsNullOrWhiteSpace(Model.Berkas);

            public string Berkas => Model.Berkas;

            public string BerkasAsli => Model.BerkasAsli;

            /// <summary>Jumlah baris rincian pada dokumen ini (ditampilkan di daftar).</summary>
            public string PosTampil => Model.Pos.Count == 0 ? "belum ada rincian" : Model.Pos.Count + " baris";

            public string TotalTampil => Model.Pos.Count == 0
                ? "—"
                : Rupiah(Model.Pos.Sum(p => p.Anggaran));

            /// <summary>Pindahkan suntingan yang terlihat di halaman ke model memori.</summary>
            public void TulisKeModel()
            {
                Model.Tahun = _tahun;
                Model.Jenis = _jenis;
                Model.PerubahanKe = _perubahanKe;
                Model.Judul = _judul.Trim();
                Model.Nomor = _nomor.Trim();
                Model.Keterangan = _keterangan.Trim();
                Model.Tanggal = _tanggal?.ToString("yyyy-MM-dd") ?? string.Empty;
            }

            public void PerbaruiRingkasan()
            {
                OnPropertyChanged(nameof(PosTampil));
                OnPropertyChanged(nameof(TotalTampil));
            }

            public void PerbaruiBerkas()
            {
                OnPropertyChanged(nameof(AdaBerkas));
                OnPropertyChanged(nameof(Berkas));
                OnPropertyChanged(nameof(BerkasAsli));
                OnPropertyChanged(nameof(Teks));
            }
        }

        /// <summary>Satu baris rincian anggaran yang bisa disunting di halaman.</summary>
        public sealed class PosItem : ObservableObject
        {
            private string _kode;
            private string _uraian;
            private long _anggaran;
            private long _realisasi;
            private string _keterangan;

            public PosItem(ApbdesStore.Pos pos, Action<PosItem>? hapus = null)
            {
                _kode = pos.Kode;
                _uraian = pos.Uraian;
                _anggaran = pos.Anggaran;
                _realisasi = pos.Realisasi;
                _keterangan = pos.Keterangan;
                HapusCommand = new RelayCommand(() => hapus?.Invoke(this));
            }

            public string Kode
            {
                get => _kode;
                set => SetProperty(ref _kode, value);
            }

            public string Uraian
            {
                get => _uraian;
                set => SetProperty(ref _uraian, value);
            }

            public long Anggaran
            {
                get => _anggaran;
                set
                {
                    // Lebih/(Kurang), %, dan total halaman ikut berubah begitu angka diisi.
                    if (SetProperty(ref _anggaran, value)) PerbaruiHitung();
                }
            }

            public long Realisasi
            {
                get => _realisasi;
                set
                {
                    if (SetProperty(ref _realisasi, value)) PerbaruiHitung();
                }
            }

            public string Keterangan
            {
                get => _keterangan;
                set => SetProperty(ref _keterangan, value);
            }

            /// <summary>Lebih/(Kurang): selisih anggaran dengan realisasi, seperti kolom laporan resmi.</summary>
            public string LebihKurangTampil => Rupiah(_anggaran - _realisasi);

            public string PersenTampil => _anggaran > 0
                ? Math.Round(_realisasi * 100.0 / _anggaran) + "%"
                : "—";

            public ICommand HapusCommand { get; }

            private void PerbaruiHitung()
            {
                OnPropertyChanged(nameof(LebihKurangTampil));
                OnPropertyChanged(nameof(PersenTampil));
            }
        }
    }
}
