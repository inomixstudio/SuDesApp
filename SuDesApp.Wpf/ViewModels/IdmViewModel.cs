using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public sealed record DimensiIndeksDesa2025(string Nama, int Skor);

    /// <summary>
    /// Satu sumber resmi pemerintah yang dipakai halaman IDM. Alamatnya
    /// ditampilkan utuh dan dapat diklik, sehingga operator bisa langsung
    /// membuka dokumennya tanpa mencari alamatnya sendiri.
    /// </summary>
    public sealed record SumberResmiIdmTampil(string Judul, string Keterangan, string Alamat);

    /// <summary>
    /// Halaman Indeks Desa Membangun (IDM).
    ///
    /// Halaman ini TIDAK mengisi atau menghitung apa pun: seluruh angka — skor
    /// IKS/IKE/IKL, IDM, status, target, dan rincian 55 indikator — dibaca dari
    /// <c>idm.kemendesa.go.id</c> untuk desa yang dipilih pada Pengaturan Surat.
    /// Dengan begitu yang tampil di aplikasi sama persis dengan yang tampil di
    /// situs pusat, dan tidak ada risiko angka versi aplikasi berbeda dari versi
    /// kementerian.
    /// </summary>
    public class IdmViewModel : ObservableObject
    {
        /// <summary>
        /// Tahun yang dicoba mundur dari tahun berjalan. Situs pusat menerbitkan
        /// IDM setahun sekali, dan data terbaru bisa tertinggal beberapa tahun
        /// (seri IKS/IKE/IKL berhenti di 2024 saat Indeks Desa 2025 diterbitkan);
        /// jendela ini harus cukup lebar untuk melewati tahun-tahun kosong itu.
        /// </summary>
        private const int TahunDicoba = 8;

        /// <summary>
        /// Banyaknya tahun yang disimpan: tahun terbaru dipakai untuk rincian
        /// indikator, sisanya mengisi daftar "tahun lain" dan grafik tren.
        /// Lima tahun membuat tren terbaca tanpa membanjiri portal pusat dengan
        /// permintaan (satu permintaan per tahun yang dicoba).
        /// </summary>
        private const int TahunDitampilkan = 5;

        /// <summary>
        /// Halaman IDM baru saja menampilkan data (segar maupun cadangan) —
        /// status bar utama menyegarkan status IDMnya lewat event ini, tanpa polling.
        /// </summary>
        public static event Action? DataDiperbarui;

        private readonly SettingsManager _pengaturan;
        private readonly WilayahDataStore _wilayah;
        private readonly IIdmKemendesaKlien _klien;
        private readonly IIndeksDesa2025Klien? _indeks2025Klien;
        private readonly ILogger<IdmViewModel> _logger;
        private readonly Func<DateTime> _jam;
        private readonly IIdmSimpananStore? _simpanan;
        private readonly IIdmPemantauanStore? _pemantauanStore;
        private readonly IIdmSertifikatStore? _sertifikatStore;
        private readonly NavigationService? _navigasi;
        private readonly Func<string, string, PdfPreviewViewModel>? _previewFactory;
        private readonly Func<string, string, Action?, DokumenBacaViewModel>? _dokumenFactory;
        private readonly AppConfig? _appConfig;
        private readonly IMessageService? _pesan;

        private bool _sedangMemuat;
        private string _pesanStatus = string.Empty;
        private string _pesanGalat = string.Empty;
        private string _peringatanSumber = string.Empty;
        private string _pesanCadangan = string.Empty;
        private string _kodeDesa = string.Empty;
        private string _namaDesa = string.Empty;
        private string _kecamatan = string.Empty;
        private string _kabupaten = string.Empty;
        private string _provinsi = string.Empty;
        private DataIdm? _data;
        private int _tahunDipilih;
        private IdmPemantauan? _pemantauan;
        private IdmSertifikatCatatan? _sertifikatCatatan;
        private IndeksDesa2025? _indeks2025;
        private bool _indeks2025BerhasilDiperiksa;

        /// <param name="navigasi">
        /// Halaman tujuan pratinjau PDF. Kosong = pencetakan tidak tersedia
        /// (dipakai uji yang hanya memeriksa tampilan).
        /// </param>
        /// <param name="previewFactory">Pembuat halaman pratinjau PDF (sama dengan halaman lain).</param>
        /// <param name="appConfig">Folder PDF sementara agar ikut dibersihkan TempPdfCleanup.</param>
        /// <param name="pesan">Dialog galat; kosong = galat hanya tampil di halaman.</param>
        public IdmViewModel(
            SettingsManager pengaturan,
            WilayahDataStore wilayah,
            IIdmKemendesaKlien klien,
            ILogger<IdmViewModel>? logger = null,
            Func<DateTime>? jam = null,
            IIdmSimpananStore? simpanan = null,
            NavigationService? navigasi = null,
            Func<string, string, PdfPreviewViewModel>? previewFactory = null,
            AppConfig? appConfig = null,
            IMessageService? pesan = null,
            IIdmPemantauanStore? pemantauan = null,
            IIdmSertifikatStore? sertifikat = null,
            IIndeksDesa2025Klien? indeksDesa2025 = null,
            Func<string, string, Action?, DokumenBacaViewModel>? dokumenFactory = null)
        {
            _pengaturan = pengaturan ?? throw new ArgumentNullException(nameof(pengaturan));
            _wilayah = wilayah ?? throw new ArgumentNullException(nameof(wilayah));
            _klien = klien ?? throw new ArgumentNullException(nameof(klien));
            _indeks2025Klien = indeksDesa2025;
            _logger = logger ?? NullLogger<IdmViewModel>.Instance;
            _jam = jam ?? (() => DateTime.Now);
            _simpanan = simpanan; // null = tanpa cadangan berkas (dipakai uji)
            _pemantauanStore = pemantauan; // null = tanpa catatan pemeriksaan (dipakai uji)
            _sertifikatStore = sertifikat; // null = tanpa catatan sertifikat (dipakai uji)
            _navigasi = navigasi;
            _previewFactory = previewFactory;
            _dokumenFactory = dokumenFactory;
            _appConfig = appConfig;
            _pesan = pesan;

            Indikator = new ObservableCollection<BarisTampil>();
            Riwayat = new ObservableCollection<RiwayatTampil>();

            // Catatan sertifikat dibaca sekali di awal: peringatan dini harus
            // terlihat begitu halaman dibuka, bahkan sebelum tombol Muat ulang
            // disentuh (pemantau latar sudah menulisnya tiap interval).
            _sertifikatCatatan = _sertifikatStore?.Muat();

            MuatCommand = new AsyncRelayCommand(MuatAsync, () => !SedangMemuat);
            BukaSitusCommand = new RelayCommand(BukaSitus);
            BukaDokumentasiIndeksDesaCommand = new RelayCommand(BukaDokumentasiIndeksDesa);
            BukaTerbitanCommand = new RelayCommand(BukaTerbitan, () => AdaTerbitanBaru);
            BukaTautanCommand = new RelayCommand<SumberResmiIdmTampil>(BukaTautan);
            PilihTahunCommand = new RelayCommand<int>(PilihTahun);
            CetakCommand = new AsyncRelayCommand(BuatPdfAsync, () => AdaDataIdm && !SedangMemuat);
        }

        /// <summary>
        /// Sumber resmi pemerintah yang dipakai halaman ini. Daftar ini sama
        /// dengan isi dokumen <c>indeks-desa-2025.md</c>, jadi angka yang tampil
        /// di halaman dan rujukan yang dibaca selalu menuju dokumen yang sama.
        ///
        /// Daftar disimpan statis: rujukan ini tidak berubah menurut keadaan
        /// desa, dan tetap bisa dibaca (mis. oleh uji) tanpa membuka halaman.
        /// </summary>
        public static IReadOnlyList<SumberResmiIdmTampil> DaftarSumberResmi2025 { get; } =
            new List<SumberResmiIdmTampil>
            {
                new(
                    "Dataset Indeks Desa 2025 — Satu Data Kemendesa",
                    "Berkas CSV resmi per desa; angka pada halaman ini dibaca dari sana.",
                    "https://satudata.kemendesa.go.id/dataset/9054899a-b8d9-495e-a830-be60c4295c5b/resource/71d8caca-9e3e-41b1-8888-3518991e27b9/download/indeks-desa.csv"),
                new(
                    "Permendesa PDTT Nomor 9 Tahun 2024 tentang Indeks Desa",
                    "Dasar pengukuran Indeks Desa — naskah peraturan di JDIH Kementerian Desa dan PDT.",
                    "https://jdih.kemendesa.go.id/public/documents/c32a892e-dd06-425a-ab76-207959840829Salinan_Peraturan_Menteri_Desa%2C_PDT%2C_dan_Transmigrasi_Nomor_9_Tahun_2024_tentang_Indeks_Desa_%282%29.pdf"),
                new(
                    "Kepmendesa PDT Nomor 343 Tahun 2025 — Status Kemajuan & Kemandirian Desa",
                    "Penetapan status desa tahun 2025 di JDIH Kementerian Desa dan PDT.",
                    "https://jdih.kemendesa.go.id/public/documents/a72ca269-9ca6-41ee-b306-c4bee4db2a8dKepmenDesPDT_343_Tahun_2025_ttg_Status_Kemajuan_dan_Kemandirian_Desa_2025.pdf"),
                new(
                    "Portal IDM — idm.kemendesa.go.id",
                    "Situs pusat Indeks Desa Membangun; seri IDM lama (IKS/IKE/IKL) sampai 2024.",
                    "https://idm.kemendesa.go.id/")
            };

        /// <summary>Daftar sumber resmi yang tampil pada kartu "Sumber resmi 2025".</summary>
        public IReadOnlyList<SumberResmiIdmTampil> SumberResmi2025 => DaftarSumberResmi2025;

        public string HeaderTitle => "INDEKS DESA & IDM";

        public string HeaderSubtitle =>
            "Data Indeks Desa 2025 dan riwayat IDM untuk desa yang dipilih di Pengaturan Surat. "
            + "Indeks Desa memakai enam dimensi, terpisah dari seri IDM lama.";

        // ===================== Keadaan pemuatan =====================

        public bool SedangMemuat
        {
            get => _sedangMemuat;
            private set
            {
                if (SetProperty(ref _sedangMemuat, value))
                {
                    OnPropertyChanged(nameof(TidakSedangMemuat));
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                    OnPropertyChanged(nameof(JudulKeadaan));
                    OnPropertyChanged(nameof(IsiKeadaan));
                    ((AsyncRelayCommand)MuatCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)CetakCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool TidakSedangMemuat => !_sedangMemuat;

        /// <summary>Pesan proses ("Menghubungi situs pusat…"), bukan galat.</summary>
        public string PesanStatus
        {
            get => _pesanStatus;
            private set
            {
                if (SetProperty(ref _pesanStatus, value)) OnPropertyChanged(nameof(AdaPesanStatus));
            }
        }

        public bool AdaPesanStatus => _pesanStatus.Length > 0;

        /// <summary>Alasan halaman belum bisa menampilkan data (desa belum dipilih, situs mati, …).</summary>
        public string PesanGalat
        {
            get => _pesanGalat;
            private set
            {
                if (SetProperty(ref _pesanGalat, value))
                {
                    OnPropertyChanged(nameof(AdaGalat));
                    OnPropertyChanged(nameof(AdaGalatDiToolbar));
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                    OnPropertyChanged(nameof(JudulKeadaan));
                    OnPropertyChanged(nameof(IsiKeadaan));
                }
            }
        }

        public bool AdaGalat => _pesanGalat.Length > 0;

        /// <summary>
        /// Situs pusat tidak bisa dihubungi tapi masih ada data terakhir yang
        /// berhasil dimuat: pesan ini menyertakan tanggal pengambilannya supaya
        /// pengguna tahu angka di layar bukan angka hari ini.
        /// </summary>
        public string PesanCadangan
        {
            get => _pesanCadangan;
            private set
            {
                if (SetProperty(ref _pesanCadangan, value)) OnPropertyChanged(nameof(AdaPesanCadangan));
            }
        }

        public bool AdaPesanCadangan => _pesanCadangan.Length > 0;

        /// <summary>
        /// Tidak ada isi yang bisa ditampilkan dan tidak sedang memuat: tempat
        /// yang seharusnya berisi kartu-kartu digantikan SATU kartu keadaan.
        /// Kartu itu menangani dua sebab sekaligus — belum pernah dimuat dan
        /// gagal dimuat — supaya halaman tidak pernah berakhir sebagai layar
        /// kosong dengan satu baris galat di toolbar.
        /// </summary>
        public bool TampilkanKartuKeadaan => !AdaData && !_sedangMemuat;

        /// <summary>Judul kartu keadaan — membedakan "belum dimuat" dan "gagal".</summary>
        public string JudulKeadaan => AdaGalat
            ? "Data pusat belum bisa dimuat"
            : "Data IDM belum dimuat";

        /// <summary>
        /// Isi kartu keadaan: alasan kegagalan dari situs/pengaturan, atau
        /// petunjuk singkat saat halaman baru dibuka.
        /// </summary>
        public string IsiKeadaan => AdaGalat ? _pesanGalat : PesanMenunggu;

        /// <summary>
        /// Baris galat di toolbar hanya dipakai saat halaman SUDAH berisi data —
        /// mis. muat ulang gagal tetapi angka lama tetap tampil. Tanpa data,
        /// pesannya sudah dibawa kartu keadaan sehingga tidak tampil dua kali.
        /// </summary>
        public bool AdaGalatDiToolbar => AdaGalat && AdaData;

        /// <summary>
        /// Teks petunjuk saat halaman belum pernah memuat data. Ditulis sebagai
        /// kalimat yang menyebut tombolnya sendiri dan sumber identitas desa,
        /// karena dua hal itulah yang paling sering jadi sebab halaman kosong.
        /// </summary>
        public string PesanMenunggu =>
            "Data diambil dari idm.kemendesa.go.id memakai nama desa pada Pengaturan Surat "
            + "(memerlukan sambungan internet).";

        // ===================== Dialog progres (gaya splash) =====================

        /// <summary>
        /// Deret langkah pemuatan dari situs pusat — dipakai dialog progres
        /// bergaya SplashStartupWindow: langkah yang sedang berjalan berdenyut,
        /// langkah selesai tercentang. Ditambahkan bertahap oleh
        /// <see cref="MuatAsync"/> persis seperti <c>SplashStartupWindow.Tahap</c>.
        /// </summary>
        public ObservableCollection<TahapMemuatTampil> TahapanMemuat { get; } = new();

        /// <summary>
        /// Mul satu langkah baru: langkah sebelumnya ditandai selesai lalu
        /// langkah ini menjadi yang aktif — pola yang sama dengan alur tahap
        /// splash (centang berjalan berikutnya begitu langkah lanjut dimulai).
        /// </summary>
        private void TambahTahapMemuat(string teks)
        {
            SelesaikanTahapMemuat();

            // Garis penghubung hanya digambar DI ANTARA titik: titik terakhir
            // tidak boleh menyisakan ekor garis yang menggantung ke kanan.
            foreach (var lama in TahapanMemuat)
            {
                lama.Terakhir = false;
            }

            TahapanMemuat.Add(new TahapMemuatTampil
            {
                Teks = teks,
                Status = TahapMemuatTampil.AktifStatus,
                Terakhir = true,
            });
            OnPropertyChanged(nameof(TahapAktifTeks));
        }

        /// <summary>
        /// Teks langkah yang sedang berjalan — ditampilkan di bawah deret ceklis
        /// dialog progres (pola splash). Langkah yang terakhir ditambahkan selalu
        /// yang aktif, dan teksnya tetap yang terakhir setelah semua selesai.
        /// </summary>
        public string TahapAktifTeks =>
            TahapanMemuat.Count > 0 ? TahapanMemuat[^1].Teks : string.Empty;

        /// <summary>Tandai langkah aktif terakhir sebagai selesai (tercentang).</summary>
        private void SelesaikanTahapMemuat()
        {
            foreach (var tahap in TahapanMemuat)
            {
                if (tahap.Status == TahapMemuatTampil.AktifStatus)
                {
                    tahap.Status = TahapMemuatTampil.SelesaiStatus;
                }
            }
        }

        /// <summary>Pemberitahuan bahwa data terpaksa diambil tanpa enkripsi.</summary>
        public string PeringatanSumber
        {
            get => _peringatanSumber;
            private set
            {
                if (SetProperty(ref _peringatanSumber, value)) OnPropertyChanged(nameof(AdaPeringatanSumber));
            }
        }

        public bool AdaPeringatanSumber => _peringatanSumber.Length > 0;

        /// <summary>
        /// Peringatan dini masa berlaku sertifikat situs pusat, dihitung dari
        /// catatan pemeriksaan terakhir (ditulis pemantau latar dan pemeriksa
        /// sertifikat) — tampil bahkan sebelum halaman memuat data, supaya
        /// pengguna tahu sertifikat mau habis jauh sebelum halaman menjadi
        /// kosong. Kosong bila sertifikat sehat atau belum pernah diperiksa.
        /// </summary>
        public string PesanSertifikat
        {
            get
            {
                var catatan = _sertifikatCatatan;
                if (catatan?.BerlakuSampai is not { } berlaku) return string.Empty;

                var status = IdmSertifikatStatus.Nilai(berlaku, _jam());
                return IdmSertifikatStatus.Pesan(status, berlaku, _jam());
            }
        }

        public bool AdaPesanSertifikat => PesanSertifikat.Length > 0;

        // ===================== Data pusat =====================

        /// <summary>
        /// Data tahun yang rinciannya sedang tampil — satu-satunya pembacaan
        /// "tahun aktif" di halaman ini. Null bila belum ada data atau tidak ada
        /// tahun yang dipilih (mis. desa ini hanya punya penilaian Indeks Desa 2025).
        /// </summary>
        private HasilIdm? Hasil => _data?.Seri.Cari(_tahunDipilih);

        public bool AdaData => Hasil != null || _indeks2025 != null;

        public bool AdaDataIdm => Hasil != null;

        public bool AdaIndeksDesa2025 => _indeks2025 != null;

        public string SkorTotalIndeksDesa2025 => _indeks2025?.TotalSkor.ToString(CultureInfo.InvariantCulture) ?? "—";

        public string NilaiIndeksDesa2025 => _indeks2025 == null
            ? "—"
            : _indeks2025.NilaiIndeks.ToString("0.00", CultureInfo.GetCultureInfo("id-ID")) + "%";

        public string StatusIndeksDesa2025 => _indeks2025?.Status ?? "—";

        /// <summary>
        /// Judul bagian riwayat IDM lama pada ringkasan. Tampil hanya ketika
        /// penilaian 2025 (Indeks Desa) sudah ada, supaya keping IKS/IKE/IKL di
        /// bawahnya tidak terbaca sebagai penilaian tahun 2025.
        /// </summary>
        public string JudulSeriLamaTampil => Hasil == null
            ? "RIWAYAT IDM LAMA"
            : "RIWAYAT IDM LAMA · S/D " + TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Catatan pada kartu tren: seri Indeks Desa 2025 tidak digabungkan ke
        /// grafik IDM lama, jadi penilaian 2025 disebut terpisah. Kosong bila
        /// dataset 2025 tidak memuat desa ini.
        /// </summary>
        public string CatatanTren2025Tampil => _indeks2025 == null
            ? string.Empty
            : "Penilaian 2025 memakai Indeks Desa (enam dimensi) dan ditampilkan pada ringkasan — seri IDM lama"
              + (Hasil == null
                  ? " tidak tersedia untuk desa ini."
                  : " berakhir di tahun " + TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture) + ".");

        /// <summary>Tanggal perubahan dataset menurut header Last-Modified dari sumber.</summary>
        public string TanggalPembaruanSumberIndeksDesa2025 => _indeks2025?.TanggalPembaruanSumber is { } tanggal
            ? "Pembaruan sumber: " + tanggal.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("id-ID"))
            : "Tanggal pembaruan sumber tidak tersedia.";

        public IReadOnlyList<DimensiIndeksDesa2025> DimensiIndeksDesa2025 => _indeks2025 is not { } data
            ? Array.Empty<DimensiIndeksDesa2025>()
            : new[]
            {
                new DimensiIndeksDesa2025("Layanan dasar", data.LayananDasar),
                new DimensiIndeksDesa2025("Sosial", data.Sosial),
                new DimensiIndeksDesa2025("Ekonomi", data.Ekonomi),
                new DimensiIndeksDesa2025("Lingkungan", data.Lingkungan),
                new DimensiIndeksDesa2025("Aksesibilitas", data.Aksesibilitas),
                new DimensiIndeksDesa2025("Tata kelola pemerintahan", data.TataKelola),
            };

        /// <summary>Skor 13 sub-dimensi sebagaimana diterbitkan dataset resmi 2025.</summary>
        public IReadOnlyList<SubDimensiIndeksDesa2025> RincianSubDimensiIndeksDesa2025 =>
            _indeks2025?.SubDimensi ?? System.Array.Empty<SubDimensiIndeksDesa2025>();

        public string SumberIndeksDesa2025 => "Sumber: Dataset Indeks Desa 2025 — Kementerian Desa PDT (Satu Data Indonesia).";

        public string NamaDesaTampil => Hasil?.Identitas?.Desa is { Length: > 0 } nama
            ? nama
            : _namaDesa.Length > 0 ? _namaDesa : "—";

        /// <summary>Kecamatan, kabupaten, provinsi — dari situs pusat bila ada.</summary>
        public string WilayahTampil
        {
            get
            {
                if (Hasil?.Identitas is { } id)
                {
                    var bagian = new[] { id.Kecamatan, id.Kabupaten, id.Provinsi }
                        .Where(x => !string.IsNullOrWhiteSpace(x));
                    return string.Join(" · ", bagian);
                }

                var lokal = new[] { _kecamatan, _kabupaten, _provinsi }
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                return string.Join(" · ", lokal);
            }
        }

        public string KodeDesaTampil => string.IsNullOrEmpty(_kodeDesa)
            ? "—"
            : "Kode desa " + _kodeDesa;

        /// <summary>
        /// Tahun penilaian yang rinciannya sedang tampil. Mengikuti chip tahun yang
        /// dipilih; setelah pemuatan, tahun terbaru yang terpilih. Bila desa ini
        /// juga punya penilaian Indeks Desa 2025, tahun seri lama ditandai supaya
        /// tidak terbaca sebagai penilaian 2025 (kartu Indeks Desa terpisah).
        /// </summary>
        public string TahunTampil
        {
            get
            {
                if (Hasil is not { } hasil)
                {
                    return _indeks2025 != null ? "Penilaian tahun 2025 · Indeks Desa" : "—";
                }

                string teks = "Penilaian tahun "
                              + hasil.Ringkasan.Tahun.ToString(CultureInfo.InvariantCulture);
                return _indeks2025 != null ? teks + " · seri IDM lama" : teks;
            }
        }

        /// <summary>Banyaknya tahun seri IDM yang berhasil dimuat pada sesi ini.</summary>
        public int JumlahTahunDimuat => _data?.Seri.Tahun.Count ?? 0;

        /// <summary>
        /// Tahun TERAKHIR data IDM yang datanya benar-benar terbit untuk desa ini
        /// — bukan tahun berjalan. Dipakai sebagai penanda "tahun terakhir" pada
        /// ringkasan tahun yang dimuat.
        /// </summary>
        public string TahunTerakhirIdmTampil => Hasil == null
            ? IdmTeks.Kosong
            : TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Kalimat yang menyebut berapa tahun IDM yang dimuat, rentangnya, dan
        /// tahun terakhir yang rinciannya sedang tampil. Halaman mengambil beberapa
        /// tahun sekaligus (tahun terbaru untuk rincian, sisanya untuk tren), jadi
        /// tanpa kalimat ini pembaca tidak tahu bahwa tahun-tahun lain ikut dimuat
        /// dan tahun mana yang sedang dibaca.
        /// </summary>
        public string RingkasanTahunDimuat
        {
            get
            {
                if (Hasil == null || _data is not { } data) return string.Empty;

                return data.Seri.RingkasanTahun();
            }
        }

        /// <summary>True bila ada daftar tahun untuk ditampilkan di bawah ringkasan.</summary>
        public bool AdaRiwayatTahun => Riwayat.Count > 0;

        /// <summary>
        /// Indikator "data terbaru yang terbit": dari catatan pemeriksaan portal
        /// terakhir (halaman atau pemantau latar), dan bila belum ada catatan
        /// dipakai tahun terbit terbaru pada deret yang dimuat. Patokannya selalu
        /// tahun TERBIT terakhir, bukan tahun yang sedang dibuka: memilih tahun
        /// lama bukan tanda halaman memakai data cadangan. Bila catatan justru
        /// menunjukkan tahun yang lebih baru daripada yang dimuat (mis. data
        /// cadangan), kalimatnya mengajak muat ulang.
        /// </summary>
        public string IndikatorTerbitTampil
        {
            get
            {
                int dimuat = TahunTerbitTerbaru;
                int? terbit = _pemantauan?.TahunTerbit ?? (dimuat > 0 ? dimuat : null);
                if (terbit == null) return string.Empty;

                // Penilaian 2025 tampil lewat Indeks Desa → label bagian ini
                // disebut "seri IDM lama" supaya tidak terbaca sebagai anggapan
                // bahwa 2024 adalah penilaian terbaru yang ada.
                string teks = (_indeks2025 != null
                        ? "Data terbaru seri IDM lama: "
                        : "Data terbaru yang terbit: ")
                               + terbit.Value.ToString(CultureInfo.InvariantCulture);
                if (dimuat > 0 && terbit.Value > dimuat)
                {
                    teks += " — halaman ini masih memuat tahun "
                            + dimuat.ToString(CultureInfo.InvariantCulture)
                            + " (data cadangan), muat ulang untuk memperbarui";
                }

                return teks;
            }
        }

        /// <summary>Tanggal pemeriksaan portal: catatan pemantauan, atau waktu data dimuat.</summary>
        public string PemeriksaanTampil
        {
            get
            {
                DateTime? kapan = _pemantauan?.WaktuPeriksa ?? _data?.WaktuMuat;
                return kapan == null
                    ? string.Empty
                    : "Diperiksa: " + kapan.Value.ToString(
                        "dd/MM/yyyy HH.mm", CultureInfo.GetCultureInfo("id-ID"));
            }
        }

        /// <summary>Indikator tampil hanya bila ada data untuk dijelaskan.</summary>
        public bool AdaIndikatorTerbit => Hasil != null && IndikatorTerbitTampil.Length > 0;

        /// <summary>Bagian tanggal pemeriksaan hanya tampil bila waktunya diketahui.</summary>
        public bool AdaPemeriksaan => PemeriksaanTampil.Length > 0;

        /// <summary>
        /// Tahun-tahun setelah tahun data yang tampil sampai tahun berjalan
        /// belum diterbitkan pusat (permintaan dimulai dari tahun berjalan dan
        /// mundur per tahun), jadi yang tampil adalah penilaian tahun sebelumnya
        /// — beri tahu SEMUA tahun yang kosong supaya pengguna tidak mengira
        /// hanya tahun berjalan yang tertinggal. Juga berlaku untuk cadangan
        /// offline yang membawa tahun lebih lama. Pengecualiannya tahun 2025:
        /// bila dataset Indeks Desa 2025 memuat desa ini, tahun itu sudah tampil
        /// lewat seri baru sehingga tidak dihitung sebagai tahun yang kosong
        /// (lihat <see cref="PesanTahun"/>).
        /// </summary>
        public bool AdaPesanTahun => Hasil != null && TahunTerbitTerbaru < _jam().Year;

        public string PesanTahun
        {
            get
            {
                if (!AdaPesanTahun) return string.Empty;

                // Patokannya tahun TERBIT terbaru, bukan tahun yang sedang dibuka:
                // memilih tahun lama tidak boleh membuat tahun-tahun sesudahnya
                // terbaca sebagai "belum diterbitkan".
                int tampil = TahunTerbitTerbaru;
                var belum = new List<string>();
                for (int tahun = tampil + 1; tahun <= _jam().Year; tahun++)
                {
                    // Penilaian 2025 sudah tampil lewat dataset resmi Indeks Desa
                    // → tahun itu jangan disebut "belum diterbitkan pusat".
                    if (_indeks2025 != null && tahun == 2025) continue;
                    belum.Add(tahun.ToString(CultureInfo.InvariantCulture));
                }

                if (_indeks2025 != null)
                {
                    return "Seri IDM lama (IKS/IKE/IKL) terakhir terbit tahun "
                           + tampil.ToString(CultureInfo.InvariantCulture)
                           + "; penilaian 2025 tampil sebagai Indeks Desa enam dimensi"
                           + (belum.Count == 0
                               ? "."
                               : ". Tahun " + DaftarTahun(belum) + " belum diterbitkan pusat.");
                }

                return "Tahun " + DaftarTahun(belum)
                    + " belum diterbitkan pusat — menampilkan penilaian tahun "
                    + tampil.ToString(CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>"2025" / "2025 dan 2026" / "2025, 2026, dan 2027".</summary>
        private static string DaftarTahun(IReadOnlyList<string> tahun) => tahun.Count switch
        {
            0 => string.Empty,
            1 => tahun[0],
            _ => string.Join(", ", tahun.Take(tahun.Count - 1)) + " dan " + tahun[^1],
        };

        /// <summary>
        /// Terbitan resmi pusat yang lebih baru dari tahun data yang tampil
        /// (mis. Kepmendesa 2025 ketika portal baru memuat 2024). Null bila tidak ada.
        /// Angkanya TIDAK dipakai sebagai pengganti skor — hanya tautan dan keterangan.
        /// </summary>
        public TerbitanIdmResmi? TerbitanBaru => Hasil is { } hasil
            ? IdmTerbitanResmi.LebihBaruDari(hasil.Ringkasan.Tahun, _jam().Year)
            : null;

        /// <summary>Benar bila ada terbitan resmi pusat untuk tahun setelah tahun data.</summary>
        public bool AdaTerbitanBaru => TerbitanBaru != null;

        /// <summary>Judul dokumen resmi yang ditampilkan di kartu kabar data.</summary>
        public string JudulTerbitanBaru => TerbitanBaru?.Judul ?? string.Empty;

        /// <summary>Penjelasan tahun resmi vs tahun yang tampil, tanpa angka karangan.</summary>
        public string KeteranganTerbitanBaru
        {
            get
            {
                if (Hasil == null) return string.Empty;
                if (_indeks2025 != null)
                    return "Kepmendesa PDT menetapkan status desa tahun 2025. Penilaian Indeks Desa enam dimensi tersedia di bagian atas; riwayat IDM lama tetap ditampilkan terpisah.";
                if (!_indeks2025BerhasilDiperiksa)
                    return "Kepmendesa PDT menetapkan status desa tahun 2025. Dataset Indeks Desa nasional belum berhasil dimuat; muat ulang saat tersambung ke internet.";
                return "Kepmendesa PDT menetapkan status desa tahun 2025. Dataset nasional telah diperiksa, tetapi tidak memuat kode desa ini.";
            }
        }

        public string SkorTampil => Hasil is { } hasil
            ? IdmTeks.Angka(hasil.Ringkasan.Skor, "0.0000")
            : IdmTeks.Kosong;

        public string StatusTampil => Hasil is { } hasil
            ? IdmTeks.Status(hasil.Ringkasan.Status)
            : IdmTeks.Kosong;

        public string IksTampil => IdmTeks.Angka(Hasil?.Ringkasan.Iks);

        public string IkeTampil => IdmTeks.Angka(Hasil?.Ringkasan.Ike);

        public string IklTampil => IdmTeks.Angka(Hasil?.Ringkasan.Ikl);

        public string TargetTampil => Hasil is { } hasil
            ? IdmTeks.Status(hasil.Ringkasan.TargetStatus)
            : IdmTeks.Kosong;

        public string SkorMinimalTampil => IdmTeks.Angka(Hasil?.Ringkasan.SkorMinimal);

        /// <summary>
        /// Kalimat jarak ke status berikutnya — hanya memakai angka dari pusat.
        /// Teksnya dipusatkan di <see cref="IdmTeks.PesanTarget"/> supaya PDF
        /// mencetak kalimat yang persis sama.
        /// </summary>
        public string PesanTarget => Hasil is { } hasil ? IdmTeks.PesanTarget(hasil.Ringkasan) : string.Empty;

        public string SumberTampil => IdmTeks.Sumber(_data?.WaktuMuat);

        /// <summary>Rincian indikator apa adanya dari pusat, termasuk baris subtotal.</summary>
        public ObservableCollection<BarisTampil> Indikator { get; }

        /// <summary>Tahun-tahun lain yang datanya diterbitkan pusat (terbaru dulu).</summary>
        public ObservableCollection<RiwayatTampil> Riwayat { get; }

        /// <summary>
        /// Deret grafik tren: satu titik per tahun terbit, urut menaik. Angkanya
        /// berasal dari daftar tahun yang SAMA dengan chip "Tahun lain" di atas
        /// — skor IDM dan IKS/IKE/IKL apa adanya dari pusat, tanpa menghitung
        /// ulang — supaya grafik tidak pernah menampilkan angka berbeda dari
        /// baris ringkasan.
        /// </summary>
        public IReadOnlyList<TitikTren> DeretTren { get; private set; } = Array.Empty<TitikTren>();

        public ICommand MuatCommand { get; }

        public ICommand BukaSitusCommand { get; }

        public ICommand BukaDokumentasiIndeksDesaCommand { get; }

        /// <summary>Buka halaman resmi terbitan pusat (JDIH Kemendesa PDT).</summary>
        public ICommand BukaTerbitanCommand { get; }

        /// <summary>
        /// Tampilkan rincian indikator untuk satu tahun yang sudah dimuat.
        /// Parameter berisi tahun penilaian; tahun yang tidak ada pada daftar
        /// diabaikan sehingga chip tahun lain tidak pernah menampilkan data kosong.
        /// </summary>
        public ICommand PilihTahunCommand { get; }

        /// <summary>
        /// Tahun penilaian yang rinciannya sedang tampil. Sama dengan tahun
        /// terbaru setelah pemuatan, lalu berubah bila pengguna memilih chip
        /// tahun lain.
        /// </summary>
        public int TahunDipilih => _tahunDipilih;

        /// <summary>
        /// Ganti tahun yang ditampilkan tanpa memuat ulang dari pusat: seluruh
        /// tahun sudah ada di memori, jadi cukup pindahkan pilihan lalu bangun
        /// ulang tampilan yang bergantung padanya. Data yang dimuat (kode desa,
        /// waktu, seluruh tahun) tidak ikut berubah. Tahun yang tidak dimuat
        /// diabaikan sehingga chip tidak pernah membuka rincian kosong.
        /// </summary>
        private void PilihTahun(int tahun)
        {
            // Tahun yang sudah terbuka dan tahun yang tidak dimuat diabaikan,
            // sehingga chip tidak pernah membangun ulang rincian tanpa sebab.
            if (tahun == _tahunDipilih) return;
            if (_data?.Seri.Cari(tahun) is null) return;

            _tahunDipilih = tahun;
            TampilkanPilihan();
        }

        /// <summary>
        /// Bangun ulang seluruh tampilan yang bergantung pada tahun terpilih:
        /// tabel indikator dan penanda chip. Angka ringkasan ikut disegarkan
        /// lewat <see cref="Segarkan"/>.
        /// </summary>
        private void TampilkanPilihan()
        {
            IsiIndikator();
            BangunRiwayat();
            Segarkan();
        }

        /// <summary>
        /// Buka tautan sumber resmi (dataset, peraturan, portal pusat) di peramban
        /// bawaan. Dipakai kartu "Sumber resmi 2025" pada halaman ini.
        /// </summary>
        public ICommand BukaTautanCommand { get; }

        /// <summary>
        /// Cetak atau simpan ringkasan + rincian indikator sebagai PDF — untuk
        /// dilampirkan pada laporan desa. Tersedia hanya setelah data pusat tampil;
        /// hasilnya dibuka di halaman pratinjau PDF yang sama dengan dokumen lain,
        /// sehingga tombol Cetak dan Ekspor di sana langsung bisa dipakai.
        /// </summary>
        public ICommand CetakCommand { get; }

        /// <summary>
        /// Keterangan tombol Cetak / PDF, termasuk saat tombolnya NONAKTIF.
        /// Tombol nonaktif tidak menampilkan tooltip biasa (lihat
        /// ToolTipService.ShowOnDisabled di IdmView), jadi tanpa teks ini pengguna
        /// desa yang hanya punya penilaian Indeks Desa 2025 melihat tombol mati
        /// tanpa tahu sebabnya — laporan IDM memang hanya memuat rincian indikator
        /// seri IDM lama.
        /// </summary>
        public string TooltipCetak
        {
            get
            {
                if (Hasil != null)
                {
                    return "Cetak atau simpan ringkasan penilaian dan rincian indikator sebagai PDF "
                           + "untuk dilampirkan pada laporan desa";
                }

                if (_indeks2025 != null)
                {
                    return "Desa ini hanya punya penilaian Indeks Desa 2025 (enam dimensi), "
                           + "sedangkan laporan PDF memuat ringkasan dan rincian indikator seri IDM lama "
                           + "— belum ada rincian indikator yang bisa dicetak.";
                }

                return "Muat data dari situs pusat terlebih dahulu untuk bisa mencetak laporan";
            }
        }

        // ===================== Pemuatan =====================

        /// <summary>
        /// Ambil data pusat untuk desa pada Pengaturan Surat. Aman dipanggil
        /// berkali-kali (tombol Muat ulang); tidak pernah melempar.
        /// </summary>
        public async Task MuatAsync()
        {
            if (SedangMemuat) return;

            SedangMemuat = true;
            PesanGalat = string.Empty;
            PeringatanSumber = string.Empty;
            PesanStatus = "Menghubungi situs pusat…";

            // Dialog progres gaya splash: deret langkah dibangun bertahap,
            // persis pola SplashStartupWindow.Tahap (aktif → tercentang).
            TahapanMemuat.Clear();
            TambahTahapMemuat("Membaca pengaturan desa & mencocokkan wilayah");

            try
            {
                var desa = await _pengaturan.GetSettingsAsync();
                _namaDesa = desa?.NamaDesa?.Trim() ?? string.Empty;
                _kecamatan = desa?.Kecamatan?.Trim() ?? string.Empty;
                _kabupaten = desa?.Kabupaten?.Trim() ?? string.Empty;
                _provinsi = string.Empty;

                if (_namaDesa.Length == 0 || _kecamatan.Length == 0 || _kabupaten.Length == 0)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = "Data desa belum diisi. Buka Pengaturan Surat dan lengkapi nama desa, "
                                 + "kecamatan, dan kabupaten — setelah itu halaman ini menampilkan data pusat.";
                    return;
                }

                var pencocokan = WilayahPencocokan.CariRantai(
                    _wilayah, _namaDesa, _kecamatan, _kabupaten);

                if (pencocokan.Status != StatusPencocokanWilayah.Ditemukan || pencocokan.Rantai == null)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = PesanPencocokan(pencocokan);
                    return;
                }

                _provinsi = pencocokan.Rantai.Provinsi.Nama;
                _kodeDesa = KodeAngka(pencocokan.Rantai.Desa.Kode);
                if (_kodeDesa.Length != 10)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = "Kode desa \"" + pencocokan.Rantai.Desa.Kode
                                 + "\" pada data wilayah tidak berbentuk 10 digit, jadi tidak bisa dipakai "
                                 + "mencari data IDM.";
                    return;
                }

                _indeks2025 = null;
                _indeks2025BerhasilDiperiksa = false;
                if (_indeks2025Klien != null)
                {
                    TambahTahapMemuat("Mencari penilaian Indeks Desa 2025 berdasarkan kode desa");
                    try
                    {
                        _indeks2025 = await _indeks2025Klien.MuatAsync(_kodeDesa);
                        _indeks2025BerhasilDiperiksa = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Dataset Indeks Desa 2025 tidak dapat dimuat");
                    }
                }

                var hasil = new List<HasilIdm>();
                bool lewatHttp = false;
                bool jaringanGagal = false;
                int gagalBeruntun = 0;
                int tahunAwal = _jam().Year;

                TambahTahapMemuat("Menghubungi situs IDM Kemendesa");

                // Ambil sampai lima tahun BERBEDA. Situs pusat kadang menjawab tahun
                // yang SAMA untuk permintaan tahun lain (parameter tahun diabaikan),
                // jadi batasnya dihitung dari deret unik — aturan yang sama dengan
                // yang memasang data di halaman ini (IdmSeriTahun).
                for (int i = 0; i < TahunDicoba
                     && IdmSeriTahun.Dari(hasil).Tahun.Count < TahunDitampilkan; i++)
                {
                    TambahTahapMemuat($"Mengunduh data IDM tahun {tahunAwal - i}");

                    var muatan = await _klien.MuatAsync(_kodeDesa, tahunAwal - i);
                    lewatHttp |= muatan.LewatHttp;

                    if (muatan.Status == StatusMuatIdm.Gagal)
                    {
                        jaringanGagal = true;
                        gagalBeruntun++;
                        if (gagalBeruntun >= 2)
                        {
                            PesanGalat = muatan.Pesan;
                            break;
                        }

                        continue;
                    }

                    gagalBeruntun = 0;
                    if (muatan.Status == StatusMuatIdm.Berhasil && muatan.Hasil != null)
                    {
                        // Jawaban kembar tetap dikumpulkan; IdmSeriTahun yang
                        // membuangnya saat dipasang, jadi aturan "satu tahun sekali"
                        // hanya ada di satu tempat.
                        hasil.Add(muatan.Hasil);
                    }
                }

                if (hasil.Count == 0)
                {
                    PesanStatus = string.Empty;

                    if (_indeks2025 != null)
                    {
                        KosongkanKoleksi();
                        _tahunDipilih = 0;
                        Segarkan();
                        PesanGalat = string.Empty;
                        PesanCadangan = "Data Indeks Desa 2025 tersedia. Rincian dan tren IDM lama tidak tersedia untuk desa ini.";
                        DataDiperbarui?.Invoke();
                        return;
                    }

                    // Jaringan gagal → tampilkan cadangan (data terakhir yang
                    // berhasil dimuat, lengkap dengan tanggalnya). Jawaban situs
                    // "tidak ada data" tidak dianggap putus-jaringan sehingga
                    // halaman tetap dikosongkan sesuai fakta dari pusat.
                    if (jaringanGagal)
                    {
                        string alasan = PesanGalat.Length > 0 ? PesanGalat : AlasanJaringan;
                        if (TampilkanCadangan(alasan))
                        {
                            _logger.LogInformation(
                                "Situs pusat gagal dihubungi — halaman IDM memakai cadangan untuk desa {Kode}.",
                                _kodeDesa);
                            return;
                        }

                        PesanGalat = alasan;
                        Kosongkan();
                        return;
                    }

                    Kosongkan();
                    if (PesanGalat.Length == 0)
                    {
                        PesanGalat = "Situs pusat tidak memuat data IDM untuk " + _namaDesa
                                     + " (kode " + _kodeDesa + "). Desa ini mungkin belum masuk penilaian IDM — "
                                     + "kelurahan umumnya tidak dinilai — atau datanya belum diterbitkan.";
                    }

                    return;
                }

                TambahTahapMemuat("Menyusun tampilan halaman");
                Tampilkan(hasil, lewatHttp);
                SelesaikanTahapMemuat();
                PesanStatus = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data IDM dari situs pusat");
                PesanStatus = string.Empty;

                if (!TampilkanCadangan("Gagal memuat data IDM: " + ex.Message))
                {
                    Kosongkan();
                    PesanGalat = "Gagal memuat data IDM: " + ex.Message;
                }
            }
            finally
            {
                SedangMemuat = false;
            }
        }

        /// <summary>
        /// Pasang satu set hasil sebagai data yang tampil: pilih tahun terbaru
        /// sebagai rincian, simpan seluruh daftar (untuk cadangan dan cetak),
        /// lalu isi koleksi indikator/riwayat/tren. Dipakai bersama oleh pemuatan
        /// langsung dan pemakaian cadangan supaya keduanya tidak menyimpang.
        /// </summary>
        private void PasangData(IReadOnlyList<HasilIdm> semua, string kodeDesa, DateTime? waktuMuat, bool lewatHttp)
        {
            _data = new DataIdm(kodeDesa, waktuMuat, lewatHttp, IdmSeriTahun.Dari(semua));
            _tahunDipilih = _data.Seri.Terbaru;

            BangunTren();
            TampilkanPilihan();
        }

        private void Tampilkan(List<HasilIdm> hasil, bool lewatHttp)
        {
            PasangData(hasil, _kodeDesa, _jam(), lewatHttp);
            PesanCadangan = string.Empty;

            PeringatanSumber = lewatHttp
                ? "Sertifikat situs pusat sedang bermasalah, jadi data diambil lewat HTTP tanpa enkripsi. "
                  + "Angkanya tetap dari situs resmi; muat ulang saat sertifikatnya sudah diperbaiki."
                : string.Empty;

            // Cadangan hasil yang baru saja BERHASIL dimuat — dipakai lagi saat
            // internet mati, termasuk setelah aplikasi dibuka ulang.
            try
            {
                _simpanan?.Simpan(new IdmSimpanan
                {
                    KodeDesa = _kodeDesa,
                    WaktuMuat = _data?.WaktuMuat,
                    LewatHttp = lewatHttp,
                    Hasil = _data!.Seri.Tahun.ToList(),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cadangan IDM gagal ditulis — tampilan tidak terpengaruh");
            }

            // Catatan pemeriksaan portal: memuat halaman ini berarti portal baru
            // saja dijawab, jadi indikator "data terbaru terbit" ikut diperbarui.
            try
            {
                _pemantauanStore?.Simpan(new IdmPemantauan
                {
                    WaktuPeriksa = _data?.WaktuMuat,
                    TahunTerbit = _data!.Seri.Terbaru,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catatan pemeriksaan IDM gagal ditulis — tampilan tidak terpengaruh");
            }

            Segarkan();
            DataDiperbarui?.Invoke();
        }

        /// <summary>
        /// Isi ulang chip tahun dari deret yang dimuat. Chip adalah tampilan dari
        /// (<see cref="_data"/>, <see cref="_tahunDipilih"/>), bukan tempat
        /// menyimpan keadaan sendiri: setiap pemilihan tahun membangun ulang daftar
        /// ini sehingga penanda "sedang tampil" tidak bisa menyimpang dari rincian
        /// indikator yang terlihat.
        /// </summary>
        private void BangunRiwayat()
        {
            Riwayat.Clear();
            if (_data is not { } data) return;

            foreach (var h in data.Seri.Tahun)
            {
                int tahun = h.Ringkasan.Tahun;
                Riwayat.Add(new RiwayatTampil(
                    tahun,
                    IdmTeks.Angka(h.Ringkasan.Skor, "0.0000"),
                    IdmTeks.Status(h.Ringkasan.Status),
                    Ditampilkan: tahun == _tahunDipilih,
                    Terbaru: tahun == data.Seri.Terbaru));
            }
        }

        /// <summary>
        /// Deret grafik tren: seluruh tahun yang dimuat, urut menaik (kiri → kanan).
        /// Tidak bergantung pada tahun yang sedang dipilih — grafik menampilkan
        /// seluruh riwayat, sedangkan tabel indikator hanya satu tahun.
        /// </summary>
        private void BangunTren()
        {
            var tahun = _data?.Seri.Tahun ?? Array.Empty<HasilIdm>();

            DeretTren = tahun
                .Select(h => new TitikTren(
                    h.Ringkasan.Tahun,
                    h.Ringkasan.Skor,
                    h.Ringkasan.Iks,
                    h.Ringkasan.Ike,
                    h.Ringkasan.Ikl))
                .OrderBy(t => t.Tahun)
                .ToList();
        }

        /// <summary>
        /// Ganti isi tabel indikator dengan baris milik tahun yang sedang dipilih.
        /// Dipakai saat data baru dipasang maupun saat pengguna memilih tahun lain
        /// pada daftar chip — keduanya harus memperlihatkan indikator tahun yang
        /// sama dengan angka ringkasan di atas tabel.
        /// </summary>
        private void IsiIndikator()
        {
            Indikator.Clear();
            if (Hasil is not { } hasil) return;

            foreach (var baris in hasil.Baris)
            {
                Indikator.Add(BarisTampil.Dari(baris));
            }
        }

        /// <summary>
        /// Tahun terakhir yang datanya benar-benar terbit untuk desa ini — satu
        /// sumber untuk semua keterangan tentang "tahun terbit", supaya kalimat
        /// itu tidak ikut berubah ketika pengguna membuka tahun yang lebih lama.
        /// </summary>
        private int TahunTerbitTerbaru => _data?.Seri.Terbaru ?? 0;

        /// <summary>Kalimat bawaan bila situs pusat tidak terjangkau tanpa pesan rinci.</summary>
        private const string AlasanJaringan =
            "Situs pusat tidak terjangkau. Periksa sambungan internet lalu muat ulang.";

        /// <summary>
        /// Tampilkan cadangan data terakhir yang BERHASIL dimuat saat situs pusat
        /// tidak bisa dihubungi. Sumbernya berurutan: data di memori (masa sesi
        /// berjalan) lalu cadangan berkas — keduanya harus untuk kode desa yang
        /// sama, supaya cadangan desa lain tidak pernah tampil.
        /// Berhasil = halaman terisi + peringatan bertanggal; gagal = tidak ada
        /// cadangan sehingga pemanggil mengosongkan halaman seperti biasa.
        /// </summary>
        private bool TampilkanCadangan(string alasan)
        {
            IdmSimpanan? sumber = null;

            if (_data is { Seri.Tahun.Count: > 0 } memori
                && memori.KodeDesa.Length > 0
                && string.Equals(memori.KodeDesa, _kodeDesa, StringComparison.Ordinal))
            {
                sumber = new IdmSimpanan
                {
                    KodeDesa = memori.KodeDesa,
                    WaktuMuat = memori.WaktuMuat,
                    LewatHttp = memori.LewatHttp,
                    Hasil = memori.Seri.Tahun.ToList(),
                };
            }
            else if (_simpanan != null)
            {
                var berkas = _simpanan.Muat();
                if (berkas != null
                    && berkas.Hasil is { Count: > 0 }
                    && berkas.KodeDesa.Length > 0
                    && string.Equals(berkas.KodeDesa, _kodeDesa, StringComparison.Ordinal))
                {
                    sumber = berkas;
                }
            }

            if (sumber == null) return false;

            PasangData(sumber.Hasil, sumber.KodeDesa, sumber.WaktuMuat, sumber.LewatHttp);

            PeringatanSumber = sumber.LewatHttp
                ? "Sertifikat situs pusat sedang bermasalah, jadi data cadangan ini sempat diambil lewat HTTP tanpa enkripsi."
                : string.Empty;

            PesanGalat = string.Empty;
            var kapan = sumber.WaktuMuat == null
                ? "waktu pengambilannya tidak tercatat"
                : sumber.WaktuMuat.Value.ToString("dd MMMM yyyy pukul HH.mm", new CultureInfo("id-ID"));
            PesanCadangan = alasan.TrimEnd('.')
                            + ". Menampilkan data terakhir yang berhasil dimuat " + kapan
                            + " — muat ulang saat internet sudah kembali.";

            Segarkan();
            return true;
        }

        /// <summary>Apakah data terakhir diambil tanpa enkripsi (dipakai uji/Diagnostik).</summary>
        public bool LewatHttp => _data?.LewatHttp ?? false;

        /// <summary>
        /// Kosongkan data milik desa yang SEDANG dipilih, termasuk penilaian
        /// Indeks Desa 2025. Dipakai saat pengaturan desa tidak lengkap atau
        /// wilayahnya tidak bisa dicocokkan: tanpa ini, kartu Indeks Desa 2025
        /// milik desa sebelumnya tetap tampil padahal halaman sudah menyatakan
        /// desa belum bisa dikenali. Jalur "situs pusat gagal" TIDAK memakai
        /// metode ini karena data 2025 yang baru dimuat tetap sah ditampilkan.
        /// </summary>
        private void KosongkanKonteks()
        {
            _indeks2025 = null;
            _indeks2025BerhasilDiperiksa = false;
            _kodeDesa = string.Empty;
            Kosongkan();
        }

        private void Kosongkan()
        {
            _data = null;
            _tahunDipilih = 0;
            PesanCadangan = string.Empty;
            KosongkanKoleksi();
            Segarkan();
        }

        private void KosongkanKoleksi()
        {
            Indikator.Clear();
            Riwayat.Clear();
            DeretTren = Array.Empty<TitikTren>();
        }

        private void Segarkan()
        {
            // Baca ulang catatan pemeriksaan: pemantau latar bisa saja baru saja
            // menulisnya (mis. menemukan tahun yang lebih baru dari data tampil).
            _pemantauan = _pemantauanStore?.Muat();

            // Catatan sertifikat juga dibaca ulang — pemantau latar memeriksanya
            // tiap interval tanpa membuka halaman ini.
            _sertifikatCatatan = _sertifikatStore?.Muat();

            foreach (var nama in new[]
                     {
                         nameof(AdaData), nameof(NamaDesaTampil), nameof(WilayahTampil),
                         nameof(AdaDataIdm), nameof(AdaIndeksDesa2025), nameof(SkorTotalIndeksDesa2025),
                         nameof(NilaiIndeksDesa2025), nameof(StatusIndeksDesa2025),
                         nameof(JudulSeriLamaTampil), nameof(CatatanTren2025Tampil),
                         nameof(TanggalPembaruanSumberIndeksDesa2025),
                         nameof(DimensiIndeksDesa2025), nameof(RincianSubDimensiIndeksDesa2025),
                         nameof(SumberIndeksDesa2025),
                         nameof(KodeDesaTampil), nameof(TahunTampil), nameof(SkorTampil),
                         nameof(TahunDipilih),
                         nameof(JumlahTahunDimuat), nameof(TahunTerakhirIdmTampil),
                         nameof(RingkasanTahunDimuat), nameof(AdaRiwayatTahun),
                         nameof(StatusTampil), nameof(IksTampil), nameof(IkeTampil),
                         nameof(IklTampil), nameof(TargetTampil), nameof(SkorMinimalTampil),
                         nameof(PesanTarget), nameof(SumberTampil),
                         nameof(TooltipCetak), nameof(PesanSertifikat), nameof(AdaPesanSertifikat),
                         nameof(TampilkanKartuKeadaan), nameof(JudulKeadaan), nameof(IsiKeadaan),
                         nameof(AdaGalatDiToolbar), nameof(PesanMenunggu),
                         nameof(AdaPesanTahun), nameof(PesanTahun), nameof(DeretTren),
                         nameof(TerbitanBaru), nameof(AdaTerbitanBaru),
                         nameof(JudulTerbitanBaru), nameof(KeteranganTerbitanBaru),
                         nameof(IndikatorTerbitTampil), nameof(PemeriksaanTampil),
                         nameof(AdaIndikatorTerbit), nameof(AdaPemeriksaan)
                     })
            {
                OnPropertyChanged(nama);
            }

            // Tombol Cetak hanya aktif saat ada data yang bisa dicetak.
            ((AsyncRelayCommand)CetakCommand).RaiseCanExecuteChanged();
            (BukaTerbitanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ===================== Cetak / ekspor PDF =====================

        /// <summary>
        /// Susun laporan IDM dari data yang SEDANG tampil (termasuk data cadangan
        /// offline), render PDF, lalu buka pratinjau. Tidak mengambil data baru:
        /// yang dicetak persis yang dilihat pengguna di layar.
        /// </summary>
        public async Task BuatPdfAsync()
        {
            if (Hasil is not { } hasil) return;

            if (_navigasi == null || _previewFactory == null)
            {
                PesanGalat = "Pencetakan PDF belum tersedia pada halaman ini.";
                return;
            }

            PesanStatus = "Menyiapkan PDF…";
            try
            {
                var desa = await AmbilDesaAsync();
                var data = LaporanIdmBuilder.Susun(
                    hasil, _data?.Seri.Tahun, desa, _data?.WaktuMuat, _jam());

                using var penampung = new MemoryStream();
                new LaporanIdmGenerator().GenerateLaporanPdf(penampung, data);

                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, penampung.ToArray(), data.NamaBerkas)
                    .ConfigureAwait(true);

                string judul = "Laporan IDM — " + data.NamaDesa
                               + (data.Tahun > 0 ? " (" + data.Tahun + ")" : string.Empty);
                _navigasi.Navigate(_previewFactory(judul, jalur));
                PesanStatus = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF laporan IDM");
                PesanStatus = string.Empty;
                PesanGalat = "Gagal membuat PDF laporan IDM: " + ex.Message;

                if (_pesan != null)
                {
                    await _pesan.ShowErrorAsync(PesanGalat);
                }
            }
        }

        /// <summary>Data desa untuk kop dan blok tanda tangan; gagal baca tidak memblokir cetak.</summary>
        private async Task<DesaData> AmbilDesaAsync()
        {
            try
            {
                return await _pengaturan.GetSettingsAsync() ?? new DesaData();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca data desa untuk laporan IDM; laporan tetap dibuat.");
                return new DesaData();
            }
        }

        private void BukaSitus()
        {
            try
            {
                Process.Start(new ProcessStartInfo(IdmKemendesaKlien.AlamatSitus) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka situs IDM pusat");
                PesanGalat = "Tidak bisa membuka peramban: " + ex.Message;
            }
        }

        private void BukaDokumentasiIndeksDesa()
        {
            const string berkas = "indeks-desa-2025.md";
            var jalur = BerkasDokumentasi.Cari(berkas);
            if (jalur is null)
            {
                PesanGalat = "Dokumentasi sumber Indeks Desa 2025 tidak ditemukan di folder docs aplikasi.";
                return;
            }

            if (_navigasi is null || _dokumenFactory is null)
            {
                PesanGalat = "Pembaca dokumentasi tidak tersedia pada halaman ini.";
                return;
            }

            try
            {
                _navigasi.Navigate(_dokumenFactory(
                    "Sumber resmi Indeks Desa 2025",
                    jalur,
                    () => _navigasi.Navigate(this)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka dokumentasi sumber Indeks Desa 2025");
                PesanGalat = "Dokumentasi sumber Indeks Desa 2025 tidak bisa dibuka: " + ex.Message;
            }
        }

        /// <summary>Buka halaman resmi terbitan pusat (bukan tautan dataset daerah).</summary>
        private void BukaTerbitan()
        {
            var terbitan = TerbitanBaru;
            if (terbitan == null) return;

            try
            {
                Process.Start(new ProcessStartInfo(terbitan.Alamat) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka terbitan resmi IDM {Alamat}", terbitan.Alamat);
                PesanGalat = "Tidak bisa membuka peramban: " + ex.Message;
            }
        }

        /// <summary>
        /// Buka satu tautan sumber resmi di peramban bawaan. Kegagalan
        /// dilaporkan di halaman, bukan dialog: pesan cukup satu baris.
        /// </summary>
        private void BukaTautan(SumberResmiIdmTampil? sumber)
        {
            if (sumber is null) return;

            var galat = TautanWeb.Buka(sumber.Alamat);
            if (galat is null)
            {
                PesanStatus = "Tautan resmi dibuka di peramban: " + sumber.Judul;
                _logger.LogInformation("Tautan resmi IDM dibuka: {Alamat}", sumber.Alamat);
                return;
            }

            PesanGalat = "Tidak bisa membuka peramban: " + galat;
            _logger.LogWarning("Gagal membuka tautan resmi IDM {Alamat}: {Galat}", sumber.Alamat, galat);
        }

        private static string PesanPencocokan(HasilPencocokanWilayah hasil) => hasil.Status switch
        {
            StatusPencocokanWilayah.TanpaData =>
                "Data wilayah bawaan tidak terbaca, jadi kode desa tidak bisa ditentukan. "
                + "Periksa pemasangan aplikasi (folder Data/Wilayah).",

            StatusPencocokanWilayah.Ambigu =>
                "Nama desa pada Pengaturan Surat cocok dengan " + hasil.JumlahKandidat
                + " wilayah berbeda. Pilih desa dari daftar wilayah (bukan diketik) supaya data IDM "
                + "yang tampil benar-benar milik desa ini.",

            _ =>
                "Desa pada Pengaturan Surat belum dikenali dari data wilayah bawaan. "
                + "Buka Pengaturan Surat lalu pilih provinsi → kabupaten/kota → kecamatan → desa dari daftar."
        };

        /// <summary>"32.15.02.2001" → "3215022001" (bentuk yang dipakai situs pusat).</summary>
        internal static string KodeAngka(string? kode) =>
            new((kode ?? string.Empty).Where(char.IsDigit).ToArray());

        /// <summary>Satu baris tabel yang siap ditampilkan (tanpa logika tambahan).</summary>
        public sealed class BarisTampil
        {
            public int? Nomor { get; init; }
            public string NomorTampil { get; init; } = "";
            public string Bidang { get; init; } = "";
            public string Indikator { get; init; } = "";
            public string Skor { get; init; } = "";
            public string Keterangan { get; init; } = "";
            public string Kegiatan { get; init; } = "";
            public string Pihak { get; init; } = "";
            public bool Ringkasan { get; init; }

            /// <summary>Baris subtotal resmi (IKS/IKE/IKL/IDM) ditebalkan seperti di situs pusat.</summary>
            public FontWeight Ketebalan => Ringkasan ? FontWeights.Bold : FontWeights.Normal;

            public static BarisTampil Dari(BarisIdm baris) => new()
            {
                Nomor = baris.Nomor,
                NomorTampil = baris.Nomor?.ToString(CultureInfo.InvariantCulture) ?? "",
                Bidang = baris.Bidang,
                Indikator = baris.Indikator,
                Skor = baris.SkorTampil,
                Keterangan = baris.Keterangan ?? "",
                Kegiatan = baris.Kegiatan ?? "",
                Pihak = baris.Pihak ?? "",
                Ringkasan = baris.Ringkasan
            };
        }

        /// <summary>
        /// Satu tahun pada daftar chip "tahun yang diterbitkan pusat" — nilai
        /// tampilan belaka, dibangun ulang dari (<see cref="_data"/>,
        /// <see cref="_tahunDipilih"/>). Chip ini dapat diklik: memilih satu tahun
        /// mengganti rincian indikator tanpa memuat ulang dari pusat.
        ///
        /// <see cref="Ditampilkan"/> menandai tahun yang rinciannya sedang tampil;
        /// <see cref="Terbaru"/> menandai tahun terakhir yang terbit — dua hal
        /// berbeda yang tidak boleh saling menggantikan.
        /// </summary>
        public sealed record RiwayatTampil(
            int Tahun,
            string Skor,
            string Status,
            bool Ditampilkan = false,
            bool Terbaru = false);

        /// <summary>
        /// Satu pemuatan yang berhasil: desa, waktu, jalur pengambilan, dan seluruh
        /// tahunnya. Satu-satunya tempat asal-usul data disimpan; memilih tahun lain
        /// hanya memindahkan <see cref="_tahunDipilih"/> dan tidak pernah mengubah
        /// record ini.
        /// </summary>
        private sealed record DataIdm(
            string KodeDesa,
            DateTime? WaktuMuat,
            bool LewatHttp,
            IdmSeriTahun Seri);

        /// <summary>Satu langkah pada dialog progres pemuatan (gaya splash).</summary>
        public sealed class TahapMemuatTampil : ObservableObject
        {
            /// <summary>Nilai <c>Status</c>: langkah belum dimulai.</summary>
            public const string BelumStatus = "Belum";

            /// <summary>Nilai <c>Status</c>: langkah sedang berjalan (titik berdenyut).</summary>
            public const string AktifStatus = "Aktif";

            /// <summary>Nilai <c>Status</c>: langkah selesai (lingkaran terisi + centang).</summary>
            public const string SelesaiStatus = "Selesai";

            private string _teks = string.Empty;
            private string _status = BelumStatus;
            private bool _terakhir;

            public string Teks
            {
                get => _teks;
                set => SetProperty(ref _teks, value);
            }

            /// <summary>Langkah terakhir pada deret — XAML memakai ini untuk
            /// menyembunyikan garis penghubung setelah titik terakhir.</summary>
            public bool Terakhir
            {
                get => _terakhir;
                set => SetProperty(ref _terakhir, value);
            }

            /// <summary>"Belum" / "Aktif" / "Selesai" — dipakai pemicu XAML untuk
            /// titik denyut dan centang (pola sama dengan indikator tahap splash).</summary>
            public string Status
            {
                get => _status;
                set => SetProperty(ref _status, value);
            }
        }
    }
}
