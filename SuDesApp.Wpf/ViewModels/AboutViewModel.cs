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
    public class AboutViewModel : ObservableObject
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
            "register & agenda digital, layanan permintaan surat via WhatsApp, pencadangan " +
            "otomatis ke Google Drive, hingga ekspor Excel/CSV/JSON — semuanya tetap tersimpan " +
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
                "Formulir, data kependudukan, agenda, dan buku SK/Perdes — tercatat sekali, terbaca kapan saja.",
            "Perangkat desa & verifikasi" =>
                "Struktur perangkat desa, SK untuk banyak nama sekaligus, dan pemeriksaan keaslian surat.",
            "Layanan digital" =>
                "Permintaan surat lewat website desa dan API Desa untuk sistem lain yang terhubung.",
            "Keamanan & pencadangan" =>
                "Database terenkripsi penuh, kredensial terlindungi, dan cadangan otomatis ke Google Drive.",
            "Pembaruan & antarmuka" =>
                "Pembaruan bertanda tangan SHA-256, penomoran yang bisa diatur, " +
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
        /// kunci database (DPAPI), kunci API, token Google, dan riwayat pembaruan.
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

        public AboutViewModel(
            ILogger<AboutViewModel> logger,
            AppConfig? appConfig = null,
            GoogleDriveService? driveService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appConfig = appConfig;
            // Paragraf dipisah "\n\n" — ditampilkan apa adanya oleh TextBlock di
            // BagianDetailLengkapView sehingga deskripsi terbaca per blok, bukan
            // satu dinding teks panjang.
            AssemblyDescription =
                "SuDesApp adalah aplikasi administrasi dan surat-menyurat desa yang membantu " +
                "pemerintah desa menyusun surat resmi secara cepat, akurat, dan konsisten. " +
                "Dilengkapi pembuatan surat otomatis berformat PDF — termasuk blanko persyaratan " +
                "pernikahan (NTCR N1–N6) yang dapat dibuat sekali isi lalu dicetak sebagai satu " +
                "berkas gabungan siap cetak — surat keterangan numpang nikah, dan Template Surat " +
                "untuk menyusun sendiri jenis surat yang belum tersedia, lengkap dengan contoh " +
                "siap pakai yang tinggal dipasang.\n\n" +
                "Layanan permintaan surat daring via WhatsApp (WhatsApp Cloud API + Google " +
                "Form/Sheet), manajemen formulir administrasi kependudukan, register surat desa " +
                "dan register NTCR yang terpisah, arsip dan agenda digital (buku agenda surat " +
                "masuk/keluar dengan lampiran PDF/gambar), serta buku SK/Perdes/Perkades dengan " +
                "lampiran. API Desa membuka layanan HTTP lokal bagi sistem lain untuk membaca " +
                "angka agregat, mengirim permintaan surat ke antrean operator, dan memverifikasi " +
                "keaslian surat terbit lewat kode verifikasinya.\n\n" +
                "Pencadangan data otomatis ke Google Drive serta ekspor ke Excel, CSV, dan JSON. " +
                "Kredensial Google disimpan terenkripsi di komputer, dan berkas database desa.db " +
                "dijaga penuh oleh enkripsi SQLCipher dengan kuncinya tersimpan aman via DPAPI " +
                "Windows — data warga tidak pernah tersimpan sebagai berkas SQLite biasa.\n\n" +
                "Pembaruan aplikasi diunduh dari GitHub Releases dengan verifikasi SHA-256: " +
                "perbaikan kecil datang sebagai pembaruan tambalan yang hanya mengganti berkas " +
                "yang berubah (tanpa installer, data dan pengaturan pengguna tidak tersentuh), " +
                "sedangkan perubahan besar memakai installer penuh.\n\n" +
                "SuDesApp berlangganan bulanan atau tahunan. Yang diurus langganan hanyalah masa " +
                "aktif aplikasinya — semua data surat, warga, dan formulir tetap tersimpan dan " +
                "terenkripsi di komputer desa masing-masing, tidak pernah dikirim ke server. " +
                "Pembayaran QRIS/VA melalui Midtrans Snap (juga bisa dari halaman web untuk " +
                "perangkat lain), dan status langganan diverifikasi dari token bertanda tangan " +
                "ECDSA P-256 sehingga tetap bisa diperiksa penuh saat internet sedang mati. " +
                "Internet putus tidak langsung memblokir kerja desa: masih ada masa tenggang 7 " +
                "hari setelah jatuh tempo, dan bila langganan memang berakhir aplikasi menjadi " +
                "baca-saja — data tetap bisa dibaca, dicari, dan dicadangkan, tetapi membuat " +
                "surat baru ditolak sampai langganan diperpanjang.";

            // Setiap butir membawa nama kelompoknya di depan; urutan tampil kelompok
            // ditentukan UrutanKelompokFitur, bukan urutan butir di daftar ini.
            var fitur = new List<(string Grup, string Teks)>
            {
                ("Surat & cetak", "Pembuatan surat resmi desa dengan PDF otomatis dari template terstandar"),
                ("Surat & cetak", "Blanko persyaratan pernikahan NTCR (N1–N6): sekali isi data satu pasangan, seluruh blanko tersimpan sekaligus dan tercetak dalam satu PDF gabungan siap cetak"),
                ("Surat & cetak", "Surat keterangan numpang nikah (N8) beserta register NTCR yang terpisah dari register surat desa umum"),
                ("Surat & cetak", "Template Surat: menyusun sendiri jenis surat baru lewat wizard (kop, judul, nomor, teks bebas, kolom isian dengan grid, tanda tangan, dan teks kaki), lengkap dengan pratinjau dan penomoran otomatis per template"),
                ("Surat & cetak", "Surat dari Template Surat tercatat otomatis di Register Surat — dapat dicari (nomor, nama penerima, NIK, atau isi surat), dibuka lagi untuk diperbaiki, dan dicetak ulang kapan saja"),
                ("Surat & cetak", "Contoh Template Surat siap pakai (pengantar RT/RW, domisili usaha, izin keramaian, keterangan penghasilan, belum menikah, undangan rapat, surat kuasa, surat tugas, dan pengumuman warga) yang dapat dipasang sekali klik, dijadikan dasar template baru, atau dicetak sebagai contoh"),
                ("Data & arsip", "Manajemen formulir administrasi kependudukan (\u2265 20 formulir resmi desa)"),
                ("Data & arsip", "Manajemen data kependudukan dan arsip digital register surat"),
                ("Data & arsip", "Buku agenda surat masuk dan keluar dengan lampiran berkas PDF atau gambar pada tiap surat"),
                ("Data & arsip", "Buku SK, Perdes, dan Perkades beserta lampiran Word/PDF"),
                ("Layanan digital", "Layanan permintaan surat daring lewat website desa maupun API Desa yang diproses operator"),
                ("Keamanan & pencadangan", "Pencadangan database dan formulir otomatis ke Google Drive"),
                ("Keamanan & pencadangan", "Kredensial Google tersimpan terenkripsi di komputer tanpa berkas kunci pada instalasi"),
                ("Keamanan & pencadangan", "Basis data desa.db terenkripsi penuh SQLCipher dengan kunci dijaga DPAPI Windows; lokasi berkas database dapat dipindahkan lewat Pengaturan Aplikasi → Database Desa"),
                ("Layanan digital", "API Desa: layanan HTTP lokal dengan kunci API ber-cakupan (Agregat/Permintaan/Penuh) untuk membaca statistik & data perangkat, menerima permintaan surat dari sistem luar, dan memverifikasi surat lewat kode verifikasinya"),
                ("Pembaruan & antarmuka", "Pembaruan aplikasi dari GitHub Releases: perbaikan kecil dipasang sebagai pembaruan tambalan (hanya berkas yang berubah, tanpa installer) dan diperiksa otomatis saat aplikasi dibuka, sedangkan perubahan besar memakai installer penuh — seluruhnya dengan verifikasi SHA-256"),
                ("Data & arsip", "Ekspor data ke Excel, CSV, dan JSON"),
                ("Pembaruan & antarmuka", "Pengaturan aplikasi bernavigasi antar bagian, termasuk pengaturan penomoran surat: awalan nomor tiap jenis surat dapat diganti sendiri (mis. SKD 470 → 471) dan langsung berlaku tanpa menutup aplikasi"),
                ("Pembaruan & antarmuka", $"{TemaPilihanTeks()} — termasuk Kontras Tinggi (hitam-kuning, WCAG 2.1 AAA untuk pengguna low vision) — plus mode Otomatis yang mengikuti mode gelap Windows"),
                ("Data & arsip", "Manajemen status surat dan riwayat aktivitas terpusat"),
                ("Perangkat desa & verifikasi", "Verifikasi surat: keaslian surat terbit dapat diperiksa dari kode verifikasinya"),
                ("Perangkat desa & verifikasi", "Lampiran tabel daftar nama pada SK perangkat desa: satu SK untuk banyak orang sekaligus, kolom data pribadi diambil dari data Warga lewat NIK dan dikelompokkan per unit (mis. Posyandu)"),
                ("Perangkat desa & verifikasi", "Data perangkat desa per kelompok jabatan lengkap dengan SK bawaan per kelompok"),
                ("Langganan", "Langganan aplikasi (mulai Rp150.000 per bulan; pilihan 6 bulan Rp750.000 dan tahunan Rp1.200.000 lebih hemat per hari, plus trial 14 hari) lewat menu Langganan yang berdiri sendiri di navigasi utama — pembayaran QRIS/VA, dan jumlah hari yang tersisa selalu terbaca jelas"),
                ("Langganan", "Status langganan dapat diperiksa penuh saat internet mati: token bertanda tangan ECDSA P-256 diverifikasi lokal, dengan masa tenggang 7 hari setelah jatuh tempo sehingga internet putus tidak memblokir kerja desa"),
                ("Langganan", "Bila langganan benar-benar berakhir, aplikasi menjadi baca-saja — data tetap bisa dibaca, dicari, diekspor, dan dicadangkan; membuat surat baru dan mencetak ditolak sampai langganan diperpanjang"),
                ("Pembaruan & antarmuka", "Dokumentasi bawaan aplikasi dapat dibuka tanpa koneksi internet")
            };

            Features = fitur.Select(f => f.Teks).ToList();
            FiturKelompok = UrutanKelompokFitur
                .Select(judul => new FiturGrup(
                    judul,
                    IkonKelompokFitur(judul),
                    KeteranganKelompokFitur(judul),
                    fitur.Where(f => f.Grup == judul).Select(f => f.Teks).ToList()))
                .Where(grup => grup.Butir.Count > 0)
                .ToList();

            // Tautan dari konfigurasi (AppConfig) — sumber yang sama dengan
            // pemeriksa pembaruan, sehingga repo di halaman ini dan repo unduhan
            // pembaruan tidak mungkin menunjuk tempat berbeda.
            var repo = _appConfig?.GithubRepo;
            RepoUrl = string.IsNullOrWhiteSpace(repo)
                ? string.Empty
                : "https://github.com/" + repo;
            UrlDukungan = "https://saweria.co/arieino";
            UrlWebsite = "https://sumberjaya.freedev.app";
            EmailPengembang = "mailto:sumberjayadev@gmail.com";

            // Sumber formulir tetap INSAN Karawang — alamatnya tidak berubah,
            // hanya keterangannya yang kini menegaskan cakupannya (Kab. Karawang).
            // Sumber utama tetap INSAN Karawang; daftar daerah lain diambil dari
            // katalog yang sama dengan halaman Pengaturan Formulir, supaya kedua
            // halaman tidak pernah menampilkan tautan yang berbeda.
            UrlFormulirKarawang = "https://infosadaradmindukkarawang.id";

            var sumberLain = KatalogFormulirDaerah.Semua
                .Where(s => !s.UnduhanOtomatis)
                .Select(s => new SumberFormulirKabupaten(s.Nama, s.TautanTampil, s.Url))
                .ToList();
            sumberLain.Add(new SumberFormulirKabupaten(
                "Formulir standar nasional (Permendagri 109/2019)", "peraturan.bpk.go.id",
                "https://peraturan.bpk.go.id/Details/138575/permendagri-no-109-tahun-2019"));
            FormulirKabupatenLain = sumberLain;

            // Teks siap tampil (tanpa skema) untuk kartu tautan.
            RepoTampil = string.IsNullOrWhiteSpace(repo) ? "-" : repo;
            WebsiteTampil = "sumberjaya.freedev.app";
            FormulirKarawangTampil = "infosadaradmindukkarawang.id";
            DukunganTampil = "saweria.co/arieino";
            EmailPengembangTampil = "sumberjayadev@gmail.com";

            // Bagian "Perusahaan": SuDesApp dikembangkan Sumberjaya Dev, unit
            // pengembangan perangkat lunak di bawah naungan PT Duta Publik.
            PerusahaanInduk = "PT Duta Publik";
            BadanHukumInduk = "PT Duta Publik Dwitama";
            BidangInduk = "Media pers dan penerbitan daring — dutapublik.com";
            MotoInduk = "\u201CTajam, Faktual, Terpercaya\u201D";
            UrlDutaPublik = "https://dutapublik.com";
            DutaPublikTampil = "dutapublik.com";

            DeskripsiInduk =
                "PT Duta Publik adalah perusahaan induk yang menaungi Sumberjaya Dev, " +
                "termasuk seluruh pengembangan aplikasi ini. Melalui badan hukum PT Duta " +
                "Publik Dwitama, perusahaan ini bergerak di bidang media pers dan " +
                "penerbitan daring — salah satunya dutapublik.com dengan moto " +
                "\u201cTajam, Faktual, Terpercaya\u201d — serta menaungi unit teknologi " +
                "yang membangun perangkat lunaknya sendiri.";

            DeskripsiPengembang =
                "Sumberjaya Dev adalah unit pengembangan perangkat lunak di bawah naungan " +
                "PT Duta Publik. Di sinilah SuDesApp dirancang, dibangun, dan dirawat — dari " +
                "surat resmi ber-PDF, register dan agenda digital, layanan permintaan surat " +
                "via WhatsApp, sampai pembaruan dan dukungan teknis bagi desa yang memakainya. " +
                "Kritik, saran, dan laporan gangguan aplikasi ini juga bermuara di Sumberjaya Dev.";

            Teknologi = new List<TeknologiItem>
            {
                new("\U0001F5A5", "WPF (.NET 10)", "Desktop Windows", "https://dotnet.microsoft.com/apps/wpf", "Fondasi aplikasi"),
                new("\u2699", "MVVM + Layanan", "DI & logging .NET", "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection", "Fondasi aplikasi"),
                new("\U0001F5C4", "SQLite + SQLCipher", "Database terenkripsi", "https://www.zetetic.net/sqlcipher/", "Data & dokumen"),
                new("\u26A1", "Dapper", "Akses data", "https://github.com/DapperLib/Dapper", "Data & dokumen"),
                new("\U0001F4C4", "QuestPDF", "Pembuatan PDF", "https://www.questpdf.com", "Data & dokumen"),
                new("\U0001F4D1", "PdfiumViewer", "Pratinjau & cetak PDF", "https://github.com/pvginkel/PdfiumViewer", "Data & dokumen"),
                new("\U0001F4CA", "EPPlus", "Excel", "https://epplussoftware.com", "Data & dokumen"),
                new("\U0001F4C1", "Google Drive API", "Cadangan & arsip", "https://developers.google.com/drive", "Layanan Google"),
                new("\U0001F4E6", "Newtonsoft.Json", "Serialisasi", "https://www.newtonsoft.com/json", "Data & dokumen"),
                new("\U0001F510", "DPAPI", "Kredensial aman", "https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata", "Keamanan"),
            };

            // Kelompok mengikuti urutan kemunculan pertama, jadi urutan daftar di atas
            // sudah menentukan urutan bagian pada halaman Teknologi.
            TeknologiKelompok = Teknologi
                .GroupBy(item => item.Grup)
                .Select(grup => new TeknologiGrup(grup.Key, grup.ToList()))
                .ToList();

            // Ucapan terima kasih — data-driven agar nama/pihak baru mudah ditambahkan
            // tanpa menyentuh tampilan.
            UcapanTerimaKasih = new List<UcapanTerimaKasihGrup>
            {
                new("\U0001F9E9", "Komunitas & pencipta perangkat lunak open-source", new List<string>
                {
                    "Microsoft — .NET dan WPF yang menjadi fondasi aplikasi ini, beserta Microsoft.Extensions (dependency injection, logging, konfigurasi) yang dipakai di seluruh lapisan kode.",
                    "QuestPDF — generator PDF untuk seluruh surat, blanko NTCR, laporan, dan register; cepat, konsisten, dan tanpa lisensi berbayar.",
                    "Dapper dan EPPlus — akses data yang ringan serta ekspor Excel yang andal untuk register, laporan, dan arsip.",
                    "PdfiumViewer dan PDFium — pratinjau serta cetak PDF di dalam aplikasi, termasuk halaman pratinjau yang menyimpan hasil langsung ke berkas.",
                    "SQLite dan SQLCipher (Zetetic) — basis data desa.db yang terenkripsi penuh; tanpa keduanya data warga tidak dapat dijaga seperti sekarang.",
                    "Pustaka klien Google API (Drive, Sheets, Forms) dan Newtonsoft.Json — integrasi login Google, pencadangan Drive, serta layanan Form/Sheet.",
                    "QRCoder untuk QR pada kaki surat, Inno Setup untuk pemasang, dan GitHub Actions yang merilis pembaruan secara otomatis."
                }),
                new("\U0001F9EA", "Penguji dan pemberi masukan", new List<string>
                {
                    "Operator dan staf administrasi desa yang membuka aplikasi setiap hari, menguji tiap halaman baru, dan melaporkan temuan — banyak perbaikan pada Catatan Rilis lahir dari masukan mereka.",
                    "Penguji awal yang memastikan aplikasi tetap berjalan di komputer kantor desa bermacam-macam, dari layar 1366×768 hingga 4K dan dari tema terang hingga kontras tinggi."
                }),
                new("\U0001F3E0", "Pengguna dan mitra", new List<string>
                {
                    "Pemerintah Desa Sumberjaya dan seluruh warganya — pengguna yang menjadikan SuDesApp alat kerja nyata setiap hari.",
                    "Sistem dan mitra eksternal yang terhubung lewat API Desa serta layanan permintaan surat WhatsApp, sehingga warga dapat terlayani tanpa antre panjang.",
                    "Situs INSAN Karawang (infosadaradmindukkarawang.id) — tempat mengunduh formulir administrasi kependudukan dan blanko surat resmi khusus Kabupaten Karawang secara gratis; terima kasih atas ketersediaan formulir bagi seluruh desa.",
                    "Donatur yang ikut menopang pengembangan lewat tautan Dukungan — terima kasih atas dukungannya."
                }),
            };

            Bagian = new List<AboutBagianViewModel>
            {
                new("DetailAplikasi", "\u2139", "Detail aplikasi", "Identitas resmi aplikasi dan informasi instalasi pada komputer ini."),
                new("Perusahaan", "\U0001F3E2", "Perusahaan", "Sumberjaya Dev, unit pengembangan perangkat lunak di bawah naungan PT Duta Publik."),
                new("Persetujuan", "\U0001F512", "Persetujuan dokumen", "Versi Kebijakan Privasi dan Syarat & Ketentuan yang disetujui di komputer ini."),
                new("DetailLengkap", "\U0001F4C4", "Detail lengkap", "Deskripsi lengkap SuDesApp dari metadata rilis."),
                new("Fitur", "\u2B50", "Fitur utama", "Fitur utama yang tersedia di SuDesApp."),
                new("Teknologi", "\U0001F9E9", "Teknologi", "Teknologi di balik SuDesApp — klik chip untuk membuka dokumentasinya."),
                new("TerimaKasih", "\U0001F64F", "Thanks to", "Terima kasih kepada mereka yang menopang terbangunnya SuDesApp."),
                new("Dukungan", "\u2764\uFE0F", "Dukungan", "Dukungan pengembangan, kritik, dan saran."),
            };
            _bagianAktif = Bagian[0];
            Bagian[0].IsTerpilih = true;

            try
            {
                LokasiData = AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                LokasiData = "-";
            }

            try
            {
                LokasiPreferensi = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SuDesApp");
            }
            catch
            {
                LokasiPreferensi = "-";
            }

            OpenUrlCommand = new RelayCommand<string>(OpenUrl);

            LoadAppLogo();
            if (driveService != null) _ = LoadGoogleAccountAsync(driveService);
        }

        /// <summary>Logo aplikasi (dari resource assembly) — tampil di header halaman.</summary>
        public BitmapSource? AppLogo { get; private set; }

        /// <summary>Foto profil akun Google (null bila belum login / tanpa foto).</summary>
        public BitmapSource? GooglePhoto { get; private set; }

        private string _googleName = string.Empty;

        /// <summary>Nama tampilan akun Google yang sedang terhubung.</summary>
        public string GoogleName
        {
            get => _googleName;
            private set
            {
                if (SetProperty(ref _googleName, value))
                {
                    OnPropertyChanged(nameof(GoogleInitial));
                }
            }
        }

        private string _googleEmail = string.Empty;

        /// <summary>Email akun Google yang sedang terhubung.</summary>
        public string GoogleEmail
        {
            get => _googleEmail;
            private set
            {
                if (SetProperty(ref _googleEmail, value))
                {
                    OnPropertyChanged(nameof(GoogleInitial));
                }
            }
        }

        /// <summary>Inisial untuk lingkaran cadangan bila foto profil tidak tersedia.</summary>
        public string GoogleInitial
        {
            get
            {
                var s = string.IsNullOrWhiteSpace(GoogleName) ? GoogleEmail : GoogleName;
                var first = s.Trim().Length > 0 ? s.Trim()[0] : '?';
                return char.ToUpperInvariant(first).ToString();
            }
        }

        private bool _hasGoogleAccount;

        /// <summary>Aktif bila ada akun Google terhubung — menampilkan kartu akun.</summary>
        public bool HasGoogleAccount
        {
            get => _hasGoogleAccount;
            private set => SetProperty(ref _hasGoogleAccount, value);
        }

        private bool _hasGooglePhoto;

        /// <summary>Aktif bila foto profil tersedia — menyembunyikan lingkaran inisial.</summary>
        public bool HasGooglePhoto
        {
            get => _hasGooglePhoto;
            private set => SetProperty(ref _hasGooglePhoto, value);
        }

        /// <summary>Muat logo aplikasi dari resource — gagal diabaikan (dekoratif).</summary>
        private void LoadAppLogo()
        {
            AppLogo = MuatResource("AppIcon-256.png");
            OnPropertyChanged(nameof(AppLogo));
        }

        /// <summary>
        /// Logo Sumberjaya Dev dan PT Duta Publik — dipanggil sekali, saat bagian
        /// Perusahaan dibuka. Gagal memuat tidak menghentikan halaman: kartu
        /// perusahaan tetap tampil lengkap dengan teksnya.
        /// </summary>
        private void MuatLogoPerusahaan()
        {
            if (_logoPerusahaanDimuat)
            {
                return;
            }

            _logoPerusahaanDimuat = true;
            LogoSumberjayaDev = MuatResource("Logo-SumberjayaDev.png");
            IkonSumberjayaDev = MuatResource("Ikon-SumberjayaDev.png");
            LogoDutaPublik = MuatResource("Logo-DutaPublik.png");
            IkonDutaPublik = MuatResource("Ikon-DutaPublik.png");

            OnPropertyChanged(nameof(LogoSumberjayaDev));
            OnPropertyChanged(nameof(IkonSumberjayaDev));
            OnPropertyChanged(nameof(LogoDutaPublik));
            OnPropertyChanged(nameof(IkonDutaPublik));
        }

        /// <summary>
        /// Versi dokumen hukum yang disetujui pada instalasi ini. Versi terpasang
        /// ikut ditampilkan supaya pengguna bisa membandingkan: bila dokumennya
        /// sudah diperbarui, kartu ini menandainya sebelum dialog berikutnya muncul.
        /// </summary>
        private void MuatPersetujuan()
        {
            try
            {
                Persetujuan = PersetujuanDokumenService.MuatStatus()
                    .Select(status => new PersetujuanDokumenTampil(status))
                    .ToList();
            }
            catch (Exception ex)
            {
                // Bagian ini hanya keterangan; kegagalan membaca folder dokumen
                // tidak boleh membuat halaman Tentang gagal dibuka.
                _logger.LogWarning(ex, "Gagal memuat status persetujuan dokumen");
                Persetujuan = Array.Empty<PersetujuanDokumenTampil>();
            }
        }

        /// <summary>
        /// Gambar dari folder Resources rakitan aplikasi, atau null bila tidak ada.
        ///
        /// Alamat pertama memakai nama rakitan eksplisit (pola sama dengan AppStatusBar):
        /// alamat "pack://application:,,,/Resources/..." menunjuk rakitan yang sedang
        /// berjalan — pada test host itu rakitan uji, bukan SuDesApp — sehingga tanpa
        /// nama rakitan gambarnya gagal dimuat di luar aplikasi. Alamat kedua adalah
        /// cadangan gaya lama.
        /// </summary>
        private BitmapSource? MuatResource(string namaBerkas)
        {
            string[] alamat =
            {
                $"pack://application:,,,/SuDesApp;component/Resources/{namaBerkas}",
                $"pack://application:,,,/Resources/{namaBerkas}",
            };

            foreach (var satu in alamat)
            {
                try
                {
                    var gambar = new BitmapImage();
                    gambar.BeginInit();
                    gambar.CacheOption = BitmapCacheOption.OnLoad;
                    gambar.UriSource = new Uri(satu, UriKind.Absolute);
                    gambar.EndInit();
                    gambar.Freeze();
                    return gambar;
                }
                catch (Exception ex)
                {
                    // Coba alamat berikutnya; gambar hanya pelengkap halaman.
                    _logger.LogDebug(ex, "Gambar {Berkas} tidak termuat dari {Alamat}", namaBerkas, satu);
                }
            }

            _logger.LogWarning("Gambar {Berkas} tidak ditemukan di resource aplikasi", namaBerkas);
            return null;
        }

        /// <summary>
        /// Ambil profil akun Google (nama, email, foto) di background — dekoratif,
        /// jadi gagal jaringan diabaikan senyap tanpa dialog. Foto di-freeze agar
        /// aman dipakai lintas thread.
        /// </summary>
        private async System.Threading.Tasks.Task LoadGoogleAccountAsync(GoogleDriveService driveService)
        {
            try
            {
                if (!driveService.IsOAuthEnabled || !driveService.HasStoredToken()) return;

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                var info = await driveService.GetAccountInfoAsync(cts.Token);
                if (info == null || string.IsNullOrWhiteSpace(info.Email)) return;

                BitmapSource? photo = null;
                if (info.PhotoBytes is { Length: > 0 })
                {
                    try
                    {
                        var img = new BitmapImage();
                        using var ms = new MemoryStream(info.PhotoBytes);
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                        img.Freeze();
                        photo = img;
                    }
                    catch (Exception exPhoto)
                    {
                        _logger.LogWarning(exPhoto, "Gagal membaca foto profil Google");
                    }
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    ApplyGoogleAccount(info.DisplayName, info.Email, photo);
                }
                else
                {
                    await dispatcher.BeginInvoke(new Action(() => ApplyGoogleAccount(info.DisplayName, info.Email, photo)));
                }
            }
            catch (Exception ex)
            {
                // Dekoratif: tidak mengganggu pengguna.
                _logger.LogInformation(ex, "Profil Google tidak termuat untuk halaman Tentang");
            }
        }

        private void ApplyGoogleAccount(string displayName, string email, BitmapSource? photo)
        {
            GoogleName = string.IsNullOrWhiteSpace(displayName) ? email : displayName;
            GoogleEmail = email;
            GooglePhoto = photo;
            HasGooglePhoto = photo != null;
            HasGoogleAccount = true;
            OnPropertyChanged(nameof(GooglePhoto));
        }

        public ICommand OpenUrlCommand { get; }

        private void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal buka URL: {Url}", url);
            }
        }
    }

    /// <summary>Satu item chip teknologi pada bagian "Teknologi".</summary>
    public class TeknologiItem
    {
        public TeknologiItem(string ikon, string nama, string keterangan, string url, string grup = "")
        {
            Ikon = ikon;
            Nama = nama;
            Keterangan = keterangan;
            Url = url;
            Grup = grup;
        }

        public string Ikon { get; }
        public string Nama { get; }
        public string Keterangan { get; }
        public string Url { get; }

        /// <summary>Nama kelompok (mis. "Data &amp; dokumen") — judul chip saat ditampilkan.</summary>
        public string Grup { get; }
    }

    /// <summary>Satu kelompok chip teknologi pada bagian "Teknologi".</summary>
    public class TeknologiGrup
    {
        public TeknologiGrup(string judul, List<TeknologiItem> item)
        {
            Judul = judul;
            Item = item;
        }

        public string Judul { get; }

        public List<TeknologiItem> Item { get; }
    }

    /// <summary>Satu kelompok fitur pada bagian "Fitur utama".</summary>
    public class FiturGrup
    {
        public FiturGrup(string judul, string ikon, string keterangan, List<string> butir)
        {
            Judul = judul;
            Ikon = ikon;
            Keterangan = keterangan;
            Butir = butir;
        }

        public string Judul { get; }

        public string Ikon { get; }

        /// <summary>Kalimat pembuka kelompok — menjelaskan isi kelompoknya.</summary>
        public string Keterangan { get; }

        public List<string> Butir { get; }
    }

    /// <summary>Satu kelompok ucapan terima kasih pada bagian "Thanks to".</summary>
    public class UcapanTerimaKasihGrup
    {
        public UcapanTerimaKasihGrup(string ikon, string judul, List<string> butir)
        {
            Ikon = ikon;
            Judul = judul;
            Butir = butir;
        }

        public string Ikon { get; }
        public string Judul { get; }
        public List<string> Butir { get; }
    }

    /// <summary>Satu baris persetujuan dokumen pada bagian "Persetujuan dokumen".</summary>
    public sealed class PersetujuanDokumenTampil
    {
        public PersetujuanDokumenTampil(StatusPersetujuan status)
        {
            Judul = status.Dokumen.Judul;
            SudahDisetujui = status.Tercatat is not null;
            PerluPembaruan = status.VersiBerubah;

            if (status.Tercatat is { } tercatat)
            {
                WaktuDisetujui = tercatat.DisetujuiPada;
                VersiDisetujui = $"Versi {tercatat.Versi}";
                DisetujuiPadaTampil = PersetujuanDokumen.WaktuTampil(tercatat.DisetujuiPada);
            }

            VersiTerpasang = status.VersiTerpasang is null
                ? "Versi dokumen tidak terbaca dari folder aplikasi."
                : $"Versi terpasang: {status.VersiTerpasang.Versi}"
                  + (string.IsNullOrWhiteSpace(status.VersiTerpasang.TanggalBerlaku)
                      ? string.Empty
                      : $" · berlaku sejak {status.VersiTerpasang.TanggalBerlaku}");
        }

        public string Judul { get; }

        /// <summary>Versi yang benar-benar disetujui pengguna, mis. "Versi 1.0".</summary>
        public string VersiDisetujui { get; } = "Belum disetujui";

        /// <summary>Waktu persetujuan siap tampil; kosong bila belum pernah.</summary>
        public string DisetujuiPadaTampil { get; } = "Belum pernah disetujui di komputer ini.";

        /// <summary>
        /// Waktu persetujuan untuk perhitungan ringkasan. Dokumen yang belum
        /// disetujui memakai nilai minimum agar tidak pernah terpilih sebagai
        /// "persetujuan terakhir".
        /// </summary>
        public DateTime WaktuDisetujui { get; } = DateTime.MinValue;

        /// <summary>Versi dokumen yang terpasang saat halaman ini dibuka.</summary>
        public string VersiTerpasang { get; }

        public bool SudahDisetujui { get; }

        /// <summary>Dokumen di komputer ini sudah lebih baru dari yang disetujui.</summary>
        public bool PerluPembaruan { get; }

        public string StatusTampil => SudahDisetujui ? "Disetujui" : "Belum disetujui";

        public string Catatan => PerluPembaruan
            ? "Dokumen di komputer ini sudah diperbarui — persetujuan versi baru akan diminta saat aplikasi dibuka berikutnya."
            : string.Empty;
    }

    /// <summary>
    /// Satu tautan sumber formulir kabupaten lain pada bagian "Thanks to".
    /// Formulir adminduk berlaku nasional (Permendagri 109/2019), jadi tautan
    /// ini hanya memudahkan desa yang berbatasan dengan kabupaten lain.
    /// </summary>
    public class SumberFormulirKabupaten
    {
        public SumberFormulirKabupaten(string nama, string tautanTampil, string url)
        {
            Nama = nama;
            TautanTampil = tautanTampil;
            Url = url;
        }

        /// <summary>Nama daerah, mis. "Kabupaten Bandung".</summary>
        public string Nama { get; }

        /// <summary>Teks alamat siap tampil (tanpa skema).</summary>
        public string TautanTampil { get; }

        /// <summary>Alamat lengkap yang dibuka saat kartu diklik.</summary>
        public string Url { get; }
    }

    /// <summary>Satu entri navigasi bagian pada halaman Tentang Aplikasi.</summary>
    public class AboutBagianViewModel : ObservableObject
    {
        private bool _isTerpilih;

        public AboutBagianViewModel(string kunci, string ikon, string label, string deskripsiStatus)
        {
            Kunci = kunci;
            Ikon = ikon;
            Label = label;
            DeskripsiStatus = deskripsiStatus;
        }

        /// <summary>Kunci bagian — menentukan template konten mana yang dirender.</summary>
        public string Kunci { get; }

        public string Ikon { get; }

        public string Label { get; }

        /// <summary>Keterangan singkat bagian — tampil di statusbar halaman.</summary>
        public string DeskripsiStatus { get; }

        public bool IsTerpilih
        {
            get => _isTerpilih;
            set => SetProperty(ref _isTerpilih, value);
        }
    }
}
