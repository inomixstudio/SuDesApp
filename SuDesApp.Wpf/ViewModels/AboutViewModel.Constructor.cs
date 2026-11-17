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
    public partial class AboutViewModel : ObservableObject
    {
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
                "Aplikasi menyediakan pembuatan surat otomatis berformat PDF dari jenis surat " +
                "bawaan maupun jenis yang dibuat sendiri, termasuk blanko persyaratan " +
                "pernikahan (NTCR N1–N6) yang diisi sekali lalu dicetak sebagai satu berkas " +
                "gabungan siap cetak, surat keterangan numpang nikah (N8), serta jenis surat " +
                "khusus seperti Keterangan Penghasilan dengan formulir isian dan format " +
                "penomorannya sendiri. Template Surat memungkinkan penyusunan jenis surat " +
                "baru lengkap dengan contoh siap pakai yang dapat dipasang sekali klik.\n\n" +
                "Manajemen formulir administrasi kependudukan, register surat desa dan " +
                "register NTCR yang terpisah, arsip dan agenda digital (buku agenda surat " +
                "masuk/keluar dengan lampiran PDF atau gambar), serta buku SK/Perdes/Perkades " +
                "beserta lampirannya. Halaman Laporan Penduduk menyusun tabel statistik — " +
                "kelompok usia, pendidikan, pekerjaan, agama, status perkawinan, dan sebagainya " +
                "— yang dipilih sebelum dicetak ke PDF atau diekspor Excel dari angka yang " +
                "sama. API Desa menyediakan layanan HTTP lokal bagi sistem lain untuk membaca " +
                "statistik agregat dan data perangkat desa, serta memverifikasi keaslian " +
                "surat terbit melalui kode verifikasinya. Surat juga dapat dibuat di lapangan " +
                "dari aplikasi Android milik desa: hasilnya diantarkan lewat antrean aman " +
                "Supabase, diberi nomor resmi satu kali oleh desktop saat diimpor, dan masuk " +
                "register yang sama tanpa mengetik ulang — sinkron otomatis berjalan di latar " +
                "belakang tanpa perlu membuka halaman Sinkron Android.\n\n" +
                "Pencadangan data otomatis ke Google Drive serta ekspor ke Excel, CSV, dan " +
                "JSON. Koneksi akun Google aktif otomatis dari installer tanpa isian " +
                "kredensial, dan berkas basis data desa.db tetap berada di komputer desa " +
                "serta tidak dikirim ke server mana pun. Halaman Cadangkan & Pulihkan " +
                "menyediakan cadangan sekali klik, riwayat cadangan bertanggal, dan " +
                "pemulihan dengan salinan keamanan otomatis sebelum database diganti.\n\n" +
                "Pembaruan diperiksa dan diunduh dari GitHub Releases di latar belakang, " +
                "dengan verifikasi SHA-256 bila rilis menyertakan sidik jarinya. Setelah " +
                "unduhan selesai, lonceng notifikasi menawarkan pemasangan sekarang atau " +
                "saat aplikasi ditutup. Perbaikan kecil dipasang sebagai tambalan yang hanya " +
                "mengganti berkas yang berubah sehingga data dan pengaturan pengguna tidak " +
                "tersentuh; perubahan besar memakai installer penuh yang ditandatangani " +
                "Authenticode pada rilis resmi. Pemasangan berlangsung tanpa halaman " +
                "konfirmasi, hanya dengan jendela kemajuan.\n\n" +
                "SuDesApp berlangganan bulanan atau tahunan; langganan mengatur masa aktif " +
                "aplikasi semata. Seluruh data surat, warga, dan formulir tersimpan di " +
                "komputer desa masing-masing dan tidak dikirim ke server mana pun. Pembayaran " +
                "dilakukan lewat QRIS statis atau transfer bank ke rekening pengelola, lalu " +
                "dibukukan administrator di Pusat Administrasi. Status langganan diverifikasi dari token bertanda " +
                "tangan ECDSA P-256 sehingga dapat diperiksa sepenuhnya saat koneksi " +
                "terputus. Akun pusat dapat membuka Pusat Administrasi dari halaman " +
                "Langganan untuk mengelola masa aktif desa; pada mode lokal, server lisensi " +
                "menyala bersama aplikasi dan berhenti saat aplikasi ditutup. Koneksi " +
                "terputus tidak menghambat pekerjaan desa: tersedia masa tenggang 7 hari " +
                "setelah jatuh tempo. Bila langganan tetap berakhir, aplikasi menjadi " +
                "baca-saja — data masih dapat dibaca, dicari, dan dicadangkan, sementara " +
                "pembuatan surat baru ditolak sampai langganan diperpanjang.";

            // Setiap butir membawa nama kelompoknya di depan; urutan tampil kelompok
            // ditentukan UrutanKelompokFitur, bukan urutan butir di daftar ini.
            var fitur = new List<(string Grup, string Teks)>
            {
                ("Surat & cetak", "Pembuatan surat resmi desa dengan PDF otomatis dari template terstandar"),
                ("Surat & cetak", "Mesin PDF sendiri (SudesPdf.Core): menyusun, membaca, menggambar, dan mengekstrak teks surat tanpa pustaka PDF pihak ketiga"),
                ("Surat & cetak", "Blanko persyaratan pernikahan NTCR (N1–N6): data satu pasangan diisi sekali, seluruh blanko tersimpan sekaligus, dan tercetak sebagai satu PDF gabungan siap cetak"),
                ("Surat & cetak", "Surat keterangan numpang nikah (N8) beserta register NTCR yang terpisah dari register surat desa umum"),
                ("Surat & cetak", "Template Surat: menyusun sendiri jenis surat baru lewat wizard (kop, judul, nomor, teks bebas, kolom isian dengan grid, tanda tangan, dan teks kaki), lengkap dengan pratinjau dan penomoran otomatis per template"),
                ("Surat & cetak", "Surat dari Template Surat tercatat otomatis di Register Surat dan dapat dicari berdasarkan nomor, nama penerima, NIK, atau isi surat; dapat dibuka kembali untuk diperbaiki dan dicetak ulang"),
                ("Surat & cetak", "Contoh Template Surat siap pakai (pengantar RT/RW, domisili usaha, izin keramaian, keterangan penghasilan, belum menikah, undangan rapat, surat kuasa, surat tugas, dan pengumuman warga) yang dapat dipasang sekali klik, dijadikan dasar template baru, atau dicetak sebagai contoh"),
                ("Surat & cetak", "Daftar hadir rapat dicetak lengkap dengan kop surat desa, dan isian terakhir tetap tersimpan selama aplikasi berjalan"),
                ("Data & arsip", "Manajemen formulir administrasi kependudukan — bawaan, tambahan sendiri, maupun unduhan resmi"),
                ("Data & arsip", "Manajemen data kependudukan dan arsip digital register surat"),
                ("Data & arsip", "Buku agenda surat masuk dan keluar dengan lampiran berkas PDF atau gambar pada tiap surat"),
                ("Data & arsip", "Buku SK, Perdes, dan Perkades beserta lampiran Word/PDF"),
                ("Data & arsip", "Halaman Indeks Desa & IDM: skor IDM, status desa, IKS/IKE/IKL, tren antar tahun, dan seluruh indikator per dimensi; dapat dicetak atau disimpan sebagai PDF"),
                ("Data & arsip", "Arsip dokumen APBDes per tahun anggaran dan per tahapan (Rancangan, Awal, Perubahan, Pertanggungjawaban/LPJ, DPA/DPPA/DPAL, Laporan Realisasi, dan Lainnya): isi dokumen dapat dibaca, disunting, dan disimpan kembali, sementara angka resmi tetap dibuat di Siskeudes"),
                ("Data & arsip", "Tutup Buku Tahunan: memeriksa keutuhan penomoran satu tahun, menutup atau membuka kembali buku, lalu mencetak arsip register"),
                ("Keamanan & pencadangan", "Pencadangan database dan formulir otomatis ke Google Drive"),
                ("Keamanan & pencadangan", "Koneksi akun Google aktif otomatis dari installer; kredensial OAuth tidak diisi manual dan tidak ditulis sebagai teks biasa di appsettings.json"),
                ("Keamanan & pencadangan", "Kata sandi pengguna disimpan sebagai PBKDF2 bersalt, percobaan masuk dibatasi, dan akun pusat dipisahkan dari akun desa"),
                ("Keamanan & pencadangan", "Basis data desa.db tersimpan sebagai SQLite lokal; basis data versi lama yang terenkripsi tetap dapat dibuka bila berkas kuncinya tersedia, dan lokasinya dapat dipindahkan lewat Pengaturan Aplikasi → Database Desa"),
                ("Layanan digital", "API Desa: layanan HTTP lokal dengan kunci API ber-cakupan (Agregat/Penuh) untuk membaca statistik dan data perangkat, serta memverifikasi surat melalui kode verifikasinya"),
                ("Layanan digital", "Sinkron Android: surat yang dibuat di aplikasi Android milik desa masuk register desktop secara otomatis — antaran lewat antrean aman Supabase (maks. 10 surat per desa), diberi nomor resmi satu kali oleh desktop sehingga tidak mungkin dobel, lalu diimpor tanpa klik bila sinkron otomatis aktif"),
                ("Layanan digital", "Pemasangan perangkat Android sekali pindai: QR atau kode 4 angka berumur 10 menit yang ditukar menjadi akun perangkat, dengan kop surat, pola nomor, dan skema formulir yang selalu diperbarui dari desktop"),
                ("Pembaruan & antarmuka", "Pembaruan otomatis: rilis diperiksa dan diunduh di latar belakang, lalu lonceng menawarkan Instal Sekarang atau Instal Saat Keluar. Perbaikan kecil dipasang sebagai tambalan (hanya berkas yang berubah), perubahan besar memakai installer penuh; keduanya diverifikasi SHA-256 bila rilis menyertakan sidik jarinya, dan installer/executable rilis resmi ditandatangani Authenticode"),
                ("Data & arsip", "Ekspor data ke Excel, CSV, dan JSON"),
                ("Pembaruan & antarmuka", "Pengaturan penomoran surat: awalan nomor tiap jenis surat dapat diubah sendiri (mis. SKD 470 → 471) dan langsung berlaku tanpa menutup aplikasi"),
                ("Pembaruan & antarmuka", $"{TemaPilihanTeks()} — termasuk Kontras Tinggi (hitam-kuning, WCAG 2.1 AAA untuk pengguna low vision) — plus mode Otomatis yang mengikuti mode gelap Windows"),
                ("Data & arsip", "Manajemen status surat dan riwayat aktivitas terpusat"),
                ("Perangkat desa & verifikasi", "Verifikasi surat: keaslian surat terbit dapat diperiksa dari kode verifikasinya"),
                ("Perangkat desa & verifikasi", "Lampiran tabel daftar nama pada SK perangkat desa: satu SK untuk banyak orang sekaligus, kolom data pribadi diambil dari data Warga lewat NIK dan dikelompokkan per unit (mis. Posyandu)"),
                ("Perangkat desa & verifikasi", "Data perangkat desa per kelompok jabatan lengkap dengan SK bawaan per kelompok"),
                ("Langganan", "Langganan bulanan Rp150.000, 6 bulan Rp750.000, atau tahunan Rp1.200.000, dengan trial 30 hari; dikelola dari menu Langganan, pembayaran lewat QRIS statis atau transfer yang dibukukan administrator"),
                ("Langganan", "Status langganan tetap dapat diperiksa saat koneksi terputus: token bertanda tangan ECDSA P-256 diverifikasi secara lokal, dengan masa tenggang 7 hari setelah jatuh tempo"),
                ("Langganan", "Pusat Administrasi untuk akun pusat: masa aktif, tagihan, pengumuman, dan audit desa dikelola lewat halaman server lisensi yang dibuka dari menu Langganan. Pada mode lokal, server lisensi menyala bersama aplikasi dan berhenti saat aplikasi ditutup"),
                ("Langganan", "Bila langganan berakhir, aplikasi menjadi baca-saja: data masih dapat dibaca, dicari, diekspor, dan dicadangkan, sementara pembuatan dan pencetakan surat baru ditolak sampai langganan diperpanjang"),
                ("Pembaruan & antarmuka", "Dokumentasi bawaan aplikasi dapat dibuka tanpa koneksi internet"),
                ("Pembaruan & antarmuka", "Panduan awal interaktif dengan pelacakan kemajuan dan kiat pemakaian aplikasi"),
                ("Data & arsip", "Form Tambah Warga: Dusun dan RT/RW diisi sekali, desa, kecamatan, dan kabupaten mengikuti Pengaturan Desa, serta alamat ringkas disusun otomatis")
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
                "surat resmi ber-PDF, register dan agenda digital, hingga pembaruan dan " +
                "dukungan teknis bagi desa pengguna. Kritik, saran, dan laporan gangguan " +
                "aplikasi ditangani melalui saluran resminya; informasi terbaru tersedia di " +
                "website sumberjaya.freedev.app.";

            Teknologi = new List<TeknologiItem>
            {
                new("\U0001F5A5", "WPF (.NET 11)", "Desktop Windows", "https://dotnet.microsoft.com/apps/wpf", "Fondasi aplikasi"),
                new("\u2699", "MVVM + Layanan", "DI & logging .NET", "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection", "Fondasi aplikasi"),
                new("\U0001F5C4", "SQLite", "Basis data lokal", "https://sqlite.org", "Data & dokumen"),
                new("\u26A1", "Dapper", "Akses data", "https://github.com/DapperLib/Dapper", "Data & dokumen"),
                new("\U0001F4C4", "SudesPdf.Core", "Mesin PDF: tulis, baca & render", string.Empty, "Data & dokumen"),
                new("\U0001F4D1", "SudesPdf", "Pratinjau, cetak & teks PDF", string.Empty, "Data & dokumen"),
                new("\U0001F4CA", "SudesExPlus", "Excel: tulis & baca .xlsx", string.Empty, "Data & dokumen"),
                new("\U0001F4C1", "Google Drive API", "Cadangan & arsip", "https://developers.google.com/drive", "Layanan Google"),
                new("\U0001F4E6", "Newtonsoft.Json", "Serialisasi", "https://www.newtonsoft.com/json", "Data & dokumen"),
                new("\U0001F510", "DPAPI", "Kredensial aman", "https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata", "Keamanan"),
                // Kelompok Sinkron Android: antrean surat dari perangkat desa di
                // lapangan — koneksi langsung PostgreSQL melalui pooler Supabase.
                new("\U0001F310", "Supabase", "Antrean & auth perangkat Android", "https://supabase.com", "Sinkron Android"),
                new("\U0001F5DB", "Npgsql", "Driver PostgreSQL", "https://www.npgsql.org", "Sinkron Android"),
                new("\U0001F4F1", "QRCoder", "Kode QR pasang perangkat & kaki surat", "https://github.com/codebude/QRCoder", "Sinkron Android"),
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
                    "Dapper — akses data yang ringan untuk seluruh data desa, surat, dan arsip.",
                    "SudesPdf.Core dan SudesPdf — mesin PDF yang ditulis sendiri untuk aplikasi ini: penyusunan, pembacaan, render, ekstraksi teks, pratinjau, dan cetak. Pratinjau surat, laporan, blanko, dan register tidak bergantung pada pustaka PDF pihak ketiga.",
                    "SQLite — basis data desa.db yang menampung seluruh data desa; tanpa SQLite aplikasi tidak dapat berjalan.",
                    "Pustaka klien Google API (Drive) dan Newtonsoft.Json — integrasi login Google dan pencadangan Drive.",
                    "QRCoder untuk kode QR pada kaki surat dan pemasangan perangkat Android, Npgsql dan Supabase untuk antrean sinkron Android, Inno Setup untuk pemasangan, dan GitHub Actions untuk merilis pembaruan otomatis."
                }),
                new("\U0001F9EA", "Penguji dan pemberi masukan", new List<string>
                {
                    "Operator dan staf administrasi desa yang membuka aplikasi setiap hari, menguji setiap halaman baru, dan melaporkan temuan — banyak perbaikan pada Catatan Rilis lahir dari masukan mereka.",
                    "Penguji awal yang memastikan aplikasi tetap berjalan di beragam komputer kantor desa, dari layar 1366×768 hingga 4K dan dari tema terang hingga kontras tinggi."
                }),
                new("\U0001F3E0", "Pengguna dan mitra", new List<string>
                {
                    "Pemerintah Desa Sumberjaya dan seluruh warganya, yang menjadikan SuDesApp alat kerja sehari-hari.",
                    "Sistem dan mitra eksternal yang terhubung lewat API Desa — dashboard kecamatan, website desa, dan instansi yang memeriksa keaslian surat.",
                    "Situs INSAN Karawang (infosadaradmindukkarawang.id) — sumber pengunduhan formulir administrasi kependudukan dan blanko surat resmi Kabupaten Karawang secara gratis."
                }),
            };

            Bagian = new List<AboutBagianViewModel>
            {
                new("DetailAplikasi", "\u2139", "Detail aplikasi", "Identitas resmi aplikasi dan keadaan instalasi pada komputer ini."),
                new("Perusahaan", "\U0001F3E2", "Perusahaan", "Sumberjaya Dev, unit pengembangan perangkat lunak di bawah naungan PT Duta Publik."),
                new("Persetujuan", "\U0001F512", "Persetujuan dokumen", "Versi Kebijakan Privasi dan Syarat & Ketentuan yang disetujui di komputer ini."),
                new("DetailLengkap", "\U0001F4C4", "Detail lengkap", "Deskripsi lengkap SuDesApp dari metadata rilis."),
                new("Fitur", "\u2B50", "Fitur utama", "Fitur utama yang tersedia di SuDesApp."),
                new("Teknologi", "\U0001F9E9", "Teknologi", "Teknologi di balik SuDesApp; klik chip untuk membuka dokumentasinya."),
                new("TerimaKasih", "\U0001F64F", "Terima kasih", "Para pihak yang menopang pengembangan SuDesApp."),
                new("Dukungan", "\u2764\uFE0F", "Dukungan", "Kritik, saran, dan saluran kontak dengan pengembang."),
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


    }
}
