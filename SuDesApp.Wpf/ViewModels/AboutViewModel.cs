using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel untuk halaman "Tentang Aplikasi" — padanan AboutBox1 (WinForms).
    /// Halaman dibuat ringkas: hanya ringkasan yang dirender saat dibuka;
    /// detail lengkap, fitur, dan teknologi dibuka per bagian (lazy) sehingga
    /// membuka halaman tetap terasa ringan.
    /// </summary>
    public partial class AboutViewModel : ObservableObject
    {
        private readonly ILogger<AboutViewModel> _logger;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();

        // Identitas resmi dipinjam dari IdentitasAplikasi — sumber kebenaran yang
        // sama dengan status bar, footer Catatan Rilis, dan jendela login, supaya
        // versi di halaman ini tidak pernah berbeda dari status bar.
        public string AssemblyTitle => IdentitasAplikasi.Nama;
        public string AssemblyVersion => IdentitasAplikasi.VersiLengkap;
        public string VersiTampil => IdentitasAplikasi.VersiDenganPrefiks;
        public string VersiBuild => IdentitasAplikasi.VersiLengkap;
        public string AssemblyProduct => IdentitasAplikasi.Nama;
        public string AssemblyCopyright => IdentitasAplikasi.HakCipta;
        public string AssemblyCompany => IdentitasAplikasi.Pengembang;

        private readonly AppConfig? _appConfig;

        /// <summary>Tautan repositori dari konfigurasi — sumber yang sama dengan pemeriksa pembaruan.</summary>
        public string RepoUrl { get; }

        /// <summary>Tautan dukungan/donasi dari konfigurasi; kosong bila tidak diisi.</summary>
        public string UrlDukungan { get; }

        /// <summary>Website resmi pengembang (Sumberjaya Dev); kosong bila tidak diisi.</summary>
        public string UrlWebsite { get; }

        /// <summary>Surel pengembang untuk kritik dan saran (satu kotak masuk untuk keduanya).</summary>
        public string EmailPengembang { get; }

        /// <summary>Teks repositori siap tampil (tanpa skema).</summary>
        public string RepoTampil { get; }

        /// <summary>Teks website siap tampil.</summary>
        public string WebsiteTampil { get; }

        /// <summary>Teks tautan dukungan siap tampil.</summary>
        public string DukunganTampil { get; }

        /// <summary>Teks surel pengembang siap tampil.</summary>
        public string EmailPengembangTampil { get; }

        /// <summary>
        /// Halaman resmi tempat mengunduh formulir administrasi kependudukan —
        /// INSAN Karawang. Sengaja dipisahkan dari <see cref="UrlWebsite"/>:
        /// website pengembang tidak memuat formulir, sedangkan tautan ini adalah
        /// sumber formulir yang dipakai warga Kabupaten Karawang.
        /// </summary>
        public string UrlFormulirKarawang { get; }

        /// <summary>Teks tautan formulir Karawang siap tampil (tanpa skema).</summary>
        public string FormulirKarawangTampil { get; }

        /// <summary>
        /// Contoh sumber formulir kabupaten lain. Formulir adminduk mengikuti
        /// Permendagri 109/2019 sehingga isinya sama di seluruh Indonesia; tautan
        /// ini berguna bagi desa yang berbatasan dengan kabupaten lain.
        /// </summary>
        public IReadOnlyList<SumberFormulirKabupaten> FormulirKabupatenLain { get; }

        // ===== Bagian "Perusahaan": SuDesApp — Sumberjaya Dev — PT Duta Publik =====

        /// <summary>
        /// Nama merek pengembang tanpa titik penutup: atribut Company di csproj menulis
        /// "Sumberjaya Dev." — bentuk resmi untuk hak cipta — sedangkan judul kartu
        /// perusahaan dibaca lebih rapi tanpa titik.
        /// </summary>
        public string MerekPengembang => IdentitasAplikasi.Pengembang.TrimEnd('.', ' ');

        /// <summary>Perusahaan induk yang menaungi pengembang aplikasi.</summary>
        public string PerusahaanInduk { get; }

        /// <summary>Badan hukum perusahaan induk (nama lengkap, bukan nama merek).</summary>
        public string BadanHukumInduk { get; }

        /// <summary>Bidang usaha perusahaan induk.</summary>
        public string BidangInduk { get; }

        /// <summary>Moto perusahaan induk seperti tertulis pada logonya.</summary>
        public string MotoInduk { get; }

        /// <summary>Penjelasan peran Sumberjaya Dev di dalam grup perusahaan.</summary>
        public string DeskripsiPengembang { get; }

        /// <summary>Penjelasan perusahaan induk dan kaitannya dengan aplikasi ini.</summary>
        public string DeskripsiInduk { get; }

        /// <summary>Tautan situs perusahaan induk.</summary>
        public string UrlDutaPublik { get; }

        /// <summary>Teks situs perusahaan induk siap tampil.</summary>
        public string DutaPublikTampil { get; }

        /// <summary>Logo penuh Sumberjaya Dev (markah + nama + moto).</summary>
        public BitmapSource? LogoSumberjayaDev { get; private set; }

        /// <summary>Monogram Sumberjaya Dev untuk piring kecil di bagan naungan.</summary>
        public BitmapSource? IkonSumberjayaDev { get; private set; }

        /// <summary>Logo penuh dutapublik.com (PT Duta Publik).</summary>
        public BitmapSource? LogoDutaPublik { get; private set; }

        /// <summary>Monogram Duta Publik untuk piring kecil di bagan naungan.</summary>
        public BitmapSource? IkonDutaPublik { get; private set; }

        /// <summary>Penanda logo perusahaan sudah dimuat (sekali saja).</summary>
        private bool _logoPerusahaanDimuat;

        /// <summary>Kunci bagian yang memuat logo perusahaan.</summary>
        private const string KunciPerusahaan = "Perusahaan";

        /// <summary>Kunci bagian yang memuat persetujuan dokumen hukum.</summary>
        private const string KunciPersetujuan = "Persetujuan";

        private IReadOnlyList<PersetujuanDokumenTampil> _persetujuan = Array.Empty<PersetujuanDokumenTampil>();

        /// <summary>
        /// Versi Kebijakan Privasi dan Syarat &amp; Ketentuan yang disetujui di
        /// komputer ini, beserta waktunya. Dimuat saat bagiannya dibuka — pola
        /// lazy yang sama dengan logo perusahaan.
        /// </summary>
        public IReadOnlyList<PersetujuanDokumenTampil> Persetujuan
        {
            get => _persetujuan;
            private set
            {
                if (SetProperty(ref _persetujuan, value))
                {
                    OnPropertyChanged(nameof(RingkasanPersetujuan));
                }
            }
        }

        /// <summary>Ringkasan satu baris untuk kepala kartu persetujuan.</summary>
        public string RingkasanPersetujuan
        {
            get
            {
                var tercatat = Persetujuan.Where(p => p.SudahDisetujui).ToList();
                if (tercatat.Count == 0)
                {
                    return "Belum ada persetujuan tercatat di komputer ini.";
                }

                var terakhir = tercatat.Max(p => p.WaktuDisetujui);
                return tercatat.Count == Persetujuan.Count
                    ? $"Kedua dokumen disetujui — terakhir {PersetujuanDokumen.WaktuTampil(terakhir)}."
                    : $"{tercatat.Count} dari {Persetujuan.Count} dokumen disetujui di komputer ini.";
            }
        }

        /// <summary>Deskripsi lengkap aplikasi (disusun di konstruktor) — tampil di bagian "Detail Lengkap".</summary>
        public string AssemblyDescription { get; }

        /// <summary>Deskripsi lengkap aplikasi — tampil di bagian "Detail Lengkap".</summary>
        public string DeskripsiLengkap => AssemblyDescription;

        /// <summary>
        /// Satu kalimat identitas untuk hero halaman — versi ringkas dari
        /// <see cref="DeskripsiSingkat"/>. Hero hanya butuh dua baris; uraian
        /// lengkapnya tetap ada di bagian "Detail lengkap".
        /// </summary>
        public string Tagline { get; } =
            "Administrasi dan surat-menyurat desa dalam satu aplikasi — surat resmi ber-PDF, " +
            "register dan agenda digital, sampai pencadangan otomatis; datanya tetap di komputer desa.";

        /// <summary>Deskripsi satu kalimat untuk halaman utama.</summary>
        public string DeskripsiSingkat { get; } =
            "Aplikasi administrasi dan surat-menyurat desa: pembuatan surat resmi ber-PDF, " +
            "register & agenda digital, pencadangan otomatis ke Google Drive, hingga ekspor " +
            "Excel/CSV/JSON — semuanya tetap tersimpan " +
            "di komputer desa sendiri. Aplikasi ini berlangganan (bulanan atau tahunan) untuk " +
            "menopang pengembangannya tetap jalan.";

        /// <summary>
        /// Seluruh fitur utama dalam satu daftar datar — dipakai untuk hitungan
        /// ringkas di header. Isinya diturunkan dari <see cref="FiturKelompok"/>,
        /// jadi menambah fitur cukup di satu tempat.
        /// </summary>
        public IReadOnlyList<string> Features { get; }

        /// <summary>
        /// Fitur utama yang dikelompokkan menurut pekerjaan sehari-hari kantor
        /// desa (surat &amp; cetak, data &amp; arsip, dan seterusnya) supaya daftar
        /// yang panjang tetap terbaca per bagian, bukan sebagai satu dinding teks.
        /// </summary>
        public IReadOnlyList<FiturGrup> FiturKelompok { get; }

        /// <summary>Jumlah fitur utama — untuk ringkasan di halaman utama.</summary>
        public int JumlahFitur => Features.Count;

        /// <summary>Jumlah kelompok fitur — untuk lencana pada bagian "Fitur utama".</summary>
        public int JumlahKelompokFitur => FiturKelompok.Count;

        /// <summary>
        /// Urutan tampil kelompok fitur. Daftar ini sekaligus menjadi daftar
        /// kelompok yang sah: kelompok yang tidak punya butir tidak ditampilkan.
        /// </summary>
        private static readonly string[] UrutanKelompokFitur =
        {
            "Surat & cetak",
            "Data & arsip",
            "Perangkat desa & verifikasi",
            "Layanan digital",
            "Keamanan & pencadangan",
            "Pembaruan & antarmuka",
            "Langganan",
        };

        /// <summary>Ikon kelompok fitur — satu huruf gambar per kelompok.</summary>
        private static string IkonKelompokFitur(string judul) => judul switch
        {
            "Surat & cetak" => "\U0001F4E8",
            "Data & arsip" => "\U0001F5C2",
            "Perangkat desa & verifikasi" => "\U0001FAAA",
            "Layanan digital" => "\U0001F4E1",
            "Keamanan & pencadangan" => "\U0001F512",
            "Pembaruan & antarmuka" => "\U0001F5A5",
            "Langganan" => "\U0001F4B3",
            _ => "\u2022",
        };

        /// <summary>Kalimat pembuka tiap kelompok fitur.</summary>
        private static string KeteranganKelompokFitur(string judul) => judul switch
        {
            "Surat & cetak" =>
                "Pembuatan surat resmi, blanko NTCR, dan template surat sendiri — dari isian sampai PDF siap cetak.",
            "Data & arsip" =>
                "Formulir, data kependudukan, agenda, buku SK/Perdes, serta arsip IDM dan APBDes — tercatat sekali, terbaca kapan saja.",
            "Perangkat desa & verifikasi" =>
                "Struktur perangkat desa, SK untuk banyak nama sekaligus, dan pemeriksaan keaslian surat.",
            "Layanan digital" =>
                "API Desa untuk sistem lain: angka agregat, data perangkat desa, dan pemeriksaan keaslian surat.",
            "Keamanan & pencadangan" =>
                "Database tersimpan lokal, kredensial terlindungi, dan cadangan otomatis ke Google Drive.",
            "Pembaruan & antarmuka" =>
                "Pembaruan diunduh di latar belakang, penomoran yang bisa diatur, " +
                $"{JumlahTemaPilihan} tema, dan dokumentasi offline.",

            "Langganan" =>
                "Masa aktif, pembayaran, dan mode baca-saja bila langganan berakhir.",
            _ => string.Empty,
        };

        /// <summary>
        /// Jumlah tema yang bisa dipilih pengguna (tanpa mode Otomatis). Dihitung
        /// dari katalog tema, bukan ditulis tangan, supaya teks Tentang tidak
        /// ketinggalan saat tema baru ditambahkan.
        /// </summary>
        private static int JumlahTemaPilihan => ThemeService.GetAvailableThemes()
            .Count(t => !string.Equals(t, ThemeService.Auto, StringComparison.OrdinalIgnoreCase));

        /// <summary>Daftar nama tema siap tampil, mis. "11 tema pilihan — Terang, Gelap, …".</summary>
        private static string TemaPilihanTeks()
        {
            var nama = ThemeService.GetAvailableThemes()
                .Where(t => !string.Equals(t, ThemeService.Auto, StringComparison.OrdinalIgnoreCase))
                .Select(ThemeService.GetDisplayName)
                .ToList();
            return $"{nama.Count} tema pilihan — {string.Join(", ", nama)}";
        }

        /// <summary>Jumlah teknologi — untuk ringkasan di halaman utama.</summary>
        public int JumlahTeknologi => Teknologi.Count;

        private AboutBagianViewModel _bagianAktif = null!;

        /// <summary>Daftar bagian yang bisa dibuka lewat chip navigasi.</summary>
        public IReadOnlyList<AboutBagianViewModel> Bagian { get; private set; } = null!;

        /// <summary>Bagian yang sedang ditampilkan — menentukan template konten.</summary>
        public AboutBagianViewModel BagianAktif
        {
            get => _bagianAktif;
            private set => SetProperty(ref _bagianAktif, value);
        }

        /// <summary>Dipanggil chip navigasi (code-behind) saat bagian dipilih.</summary>
        public void PilihBagian(AboutBagianViewModel bagian)
        {
            if (ReferenceEquals(bagian, BagianAktif))
            {
                return;
            }

            // Logo perusahaan baru diambil saat bagiannya benar-benar dibuka — pola
            // yang sama dengan bagian lain: hanya konten terpilih yang disiapkan.
            if (bagian.Kunci == KunciPerusahaan)
            {
                MuatLogoPerusahaan();
            }

            if (bagian.Kunci == KunciPersetujuan)
            {
                MuatPersetujuan();
            }

            foreach (var item in Bagian)
            {
                item.IsTerpilih = ReferenceEquals(item, bagian);
            }

            BagianAktif = bagian;
        }

        /// <summary>Daftar teknologi untuk bagian "Teknologi" (ikon + nama + tautan).</summary>
        public IReadOnlyList<TeknologiItem> Teknologi { get; }

        /// <summary>
        /// Teknologi yang dikelompokkan (fondasi, data &amp; dokumen, layanan Google,
        /// keamanan) supaya chip tidak tampil sebagai satu kumpulan tanpa urutan.
        /// </summary>
        public IReadOnlyList<TeknologiGrup> TeknologiKelompok { get; }

        /// <summary>
        /// Daftar pihak yang berjasa pada bagian "Thanks to" (kelompok + butir) —
        /// data-driven agar nama/pihak baru mudah ditambahkan tanpa menyentuh tampilan.
        /// </summary>
        public IReadOnlyList<UcapanTerimaKasihGrup> UcapanTerimaKasih { get; }

        /// <summary>Lokasi data aplikasi di komputer ini (dihitung sekali, murah).</summary>
        public string LokasiData { get; }

        /// <summary>
        /// Lokasi berkas milik pengguna di luar folder aplikasi — preferensi,
        /// kunci API, kredensial & token Google (DPAPI), dan riwayat pembaruan.
        /// Dipisah dari <see cref="LokasiData"/> agar pengguna tidak mengira
        /// mencadangkan folder aplikasi ikut menyelamatkan seluruh datanya.
        /// </summary>
        public string LokasiPreferensi { get; }

        /// <summary>Versi runtime .NET yang sedang dipakai.</summary>
        public string RuntimeVersi { get; } =
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        /// <summary>Durasi aplikasi berjalan (diperbarui oleh timer di code-behind).</summary>
        public string Uptime
        {
            get
            {
                var t = _uptime.Elapsed;
                return t.TotalHours >= 1
                    ? $"{(int)t.TotalHours} jam {t.Minutes} menit"
                    : t.TotalMinutes >= 1
                        ? $"{t.Minutes} menit {t.Seconds} detik"
                        : $"{t.Seconds} detik";
            }
        }

        /// <summary>Dipanggil code-behind untuk menyegarkan teks uptime.</summary>
        public void RefreshUptime() => OnPropertyChanged(nameof(Uptime));

    }
}
