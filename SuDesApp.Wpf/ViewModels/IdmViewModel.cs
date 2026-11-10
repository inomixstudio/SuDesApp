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
    public partial class IdmViewModel : ObservableObject
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
        private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;

        private bool _sedangMemuat;
        private CancellationTokenSource? _ctsMuat;
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
            Func<string, string, Action?, DokumenBacaViewModel>? dokumenFactory = null,
            SuDesApp.Services.PenjagaLisensi? penjaga = null)
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
            _penjaga = penjaga;

            Indikator = new ObservableCollection<BarisTampil>();
            Riwayat = new ObservableCollection<RiwayatTampil>();

            // Catatan sertifikat dibaca sekali di awal: peringatan dini harus
            // terlihat begitu halaman dibuka, bahkan sebelum tombol Muat ulang
            // disentuh (pemantau latar sudah menulisnya tiap interval).
            _sertifikatCatatan = _sertifikatStore?.Muat();

            MuatCommand = new AsyncRelayCommand(MuatAsync, () => !SedangMemuat);
            BatalCommand = new RelayCommand(BatalkanMuat, () => SedangMemuat);
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


    }
}
