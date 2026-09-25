using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Jenis sub-bagian catatan rilis; dipakai untuk penanda warna pada kartu
    /// (mis. "Fitur Baru" hijau, "Perbaikan Bug" oranye, sisanya aksen tema).
    /// </summary>
    public enum GayaRilisCatatan
    {
        FiturBaru,
        Peningkatan,
        Perbaikan,
        Pembaruan,
        Umum
    }

    /// <summary>
    /// Entri sub-bagian dalam satu versi catatan rilis.
    /// </summary>
    public class CatatanRilisSubSection : ObservableObject
    {
        public string Heading { get; }
        public List<string> Points { get; }
        public GayaRilisCatatan Jenis { get; }
        public string LabelJenis { get; }

        public CatatanRilisSubSection(string heading, List<string> points, GayaRilisCatatan? jenis = null)
        {
            Heading = heading;
            Points = points;
            Jenis = jenis ?? TentukanJenis(heading);
            LabelJenis = LabelUntuk(Jenis);
        }

        /// <summary>Tentukan jenis sub-bagian dari awalan judulnya.</summary>
        private static GayaRilisCatatan TentukanJenis(string heading)
        {
            var teks = heading ?? string.Empty;
            if (teks.StartsWith("Fitur Baru", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.FiturBaru;
            if (teks.StartsWith("Peningkatan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Peningkatan;
            if (teks.StartsWith("Perbaikan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Perbaikan;
            if (teks.StartsWith("Pembaruan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Pembaruan;
            return GayaRilisCatatan.Umum;
        }

        private static string LabelUntuk(GayaRilisCatatan jenis) => jenis switch
        {
            GayaRilisCatatan.FiturBaru => "FITUR BARU",
            GayaRilisCatatan.Peningkatan => "PENINGKATAN",
            GayaRilisCatatan.Perbaikan => "PERBAIKAN",
            GayaRilisCatatan.Pembaruan => "PEMBARUAN",
            _ => "LAINNYA"
        };
    }

    /// <summary>
    /// Satu blok versi pada halaman catatan rilis.
    /// </summary>
    public class CatatanRilisEntry : ObservableObject
    {
        public string VersionText { get; }
        public bool IsLatest { get; }
        public List<CatatanRilisSubSection> Sections { get; }

        public CatatanRilisEntry(string versionText, bool isLatest, List<CatatanRilisSubSection> sections)
        {
            VersionText = versionText;
            IsLatest = isLatest;
            Sections = sections;
        }
    }

    /// <summary>
    /// ViewModel halaman Catatan Rilis — padanan <c>CatatanRilis</c> (WinForms).
    /// Data disalin 1:1 dari <c>Views/CatatanRilis.cs (TampilkanCatatanRilis)</c>.
    /// </summary>
    public class CatatanRilisViewModel : ObservableObject
    {
        private readonly ILogger<CatatanRilisViewModel> _logger;

        public string Title => "CATATAN RILIS";
        public string Subtitle => "Pembaruan dan perbaikan aplikasi";
        /// <summary>
        /// Identitas aplikasi pada statusbar halaman ("Surat Desa V.x.y.z  •  ...") —
        /// disusun otomatis dari atribut assembly (csproj) oleh <see cref="IdentitasAplikasi"/>,
        /// sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string VersionFooter => IdentitasAplikasi.FooterVersi;

        /// <summary>
        /// Jumlah kartu versi yang ditampilkan di halaman. Catatan rilis dipakai sebagai
        /// riwayat, jadi versi lama tetap tersimpan — hanya jumlah kartu yang ditampilkan
        /// yang dibatasi supaya halaman ini tetap ringkas dan mudah dibaca.
        /// </summary>
        public const int MaksKartu = 3;

        /// <summary>Kartu versi yang tampil di halaman (terbaru di atas, maksimal <see cref="MaksKartu"/>).</summary>
        public List<CatatanRilisEntry> Entries { get; }

        /// <summary>Seluruh riwayat versi aplikasi — tiga kartu terbaru diambil dari sini.</summary>
        public IReadOnlyList<CatatanRilisEntry> SemuaVersi { get; }

        public CatatanRilisViewModel(ILogger<CatatanRilisViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SemuaVersi = RiwayatVersi();

            // Hanya kartu versi terbaru yang ditampilkan (terbaru di atas). Versi lama
            // tetap tersimpan di SemuaVersi/RiwayatVersi, hanya tidak ditampilkan kartunya.
            Entries = SemuaVersi.Take(MaksKartu).ToList();

            _logger.LogInformation(
                "Catatan rilis dimuat: {Total} versi, {Tampil} kartu ditampilkan (batas {Maks})",
                SemuaVersi.Count, Entries.Count, MaksKartu);
        }

        /// <summary>
        /// Seluruh riwayat versi aplikasi — sumber tunggal untuk kartu-kartu di halaman
        /// Catatan Rilis (tiga versi terbaru yang diambil dari daftar ini).
        /// </summary>
        public static IReadOnlyList<CatatanRilisEntry> RiwayatVersi()
            => _riwayatVersi ??= SusunRiwayatVersi();

        private static IReadOnlyList<CatatanRilisEntry>? _riwayatVersi;

        /// <summary>Menyusun riwayat versi (terbaru di atas) beserta rincian tiap versinya.</summary>
        private static List<CatatanRilisEntry> SusunRiwayatVersi()
        {
            return new List<CatatanRilisEntry>
            {
                new CatatanRilisEntry(
                    "Versi 2.5.3 (19 September 2026)",
                    true,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Mode Diam-Diam Startup (Aplikasi Terbuka Lebih Cepat)", new List<string>
                        {
                            "Pengaturan Aplikasi kini menyediakan saklar 'Mode diam-diam saat aplikasi dibuka' (aktif secara bawaan): pekerjaan latar yang tidak penting bagi pengguna — pemeriksaan pembaruan online serta pembersihan PDF sementara, hasil ekspor lama, dan draft kedaluwarsa — ditunda beberapa menit setelah aplikasi dibuka.",
                            "Jeda bisa diatur 1-60 menit (bawaan 5 menit) lewat pengaturan 'Jeda mode diam-diam'. Semakin besar jeda, semakin ringan pembukaan aplikasi; seluruh pekerjaan tetap berjalan otomatis setelah jeda berlalu.",
                            "Rotasi log error tetap berjalan sejak dini karena kecil dan diperlukan agar log harian tetap rapi — satu-satunya pekerjaan pembersihan yang tidak ikut ditunda.",
                            "Mode ini bisa dimatikan untuk kembali ke perilaku lama (semua pembersihan dijalankan langsung sebelum jendela login tampil)."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.2 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Pasang Pembaruan Kecil Otomatis Saat Aplikasi Ditutup", new List<string>
                        {
                            "Pengaturan Aplikasi kini menyediakan saklar 'Pasang pembaruan kecil otomatis saat aplikasi ditutup': ketika aplikasi ditutup, pembaruan kecil (tambalan) diunduh, diverifikasi SHA-256, dan dipasang otomatis tanpa menanya — aplikasi dibuka kembali sudah memakai versi terbaru.",
                            "Hanya pembaruan KECIL yang mengikuti alur otomatis ini. Pembaruan besar (installer penuh) tidak pernah dipasang senyap-senyap dan selalu diminta persetujuan lewat halaman Pembaruan.",
                            "Batas ukuran otomatis dapat diatur (1-500 MB, bawaan 25 MB): paket yang lebih besar dari batas tidak pernah diunduh senyap-senyap, melainkan tetap ditawarkan lewat notifikasi untuk dipasang manual.",
                            "Pemasangan otomatis tidak pernah menunda atau menggagalkan penutupan aplikasi: seluruh kegagalan (internet lambat, verifikasi gagal, dan sebagainya) cukup dilewati, dan setiap pemasangan otomatis tercatat pada Riwayat Pembaruan.",
                            "Indikator kecil di status bar (cakram berputar) tampil selama pemasangan otomatis berlangsung dan menyebut fase yang sedang dikerjakan: memeriksa pembaruan, mengunduh pembaruan, hingga 'Siap dipasang — menutup aplikasi', sehingga pengguna selalu tahu aplikasi sedang memperbarui diri sebelum tertutup."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.1 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Riwayat Pembaruan", new List<string>
                        {
                            "Halaman Pembaruan kini menyimpan dan menampilkan riwayat pembaruan yang pernah dicoba dipasang: versinya, jenisnya (pembaruan kecil atau installer penuh), tanggal & jam, jumlah berkas yang diganti, dan hasilnya (berhasil/gagal beserta alasannya) — terbaru dulu.",
                            "Riwayat tercatat otomatis pada setiap pemasangan pembaruan kecil (hasil skrip penerap), kegagalan sebelum pemasangan berjalan (mis. unduhan atau verifikasi SHA-256 gagal), maupun installer penuh yang dijalankan dari halaman Pembaruan.",
                            "Riwayat disimpan di profil pengguna (di luar folder aplikasi), sehingga tetap ada setelah aplikasi diperbarui dan tidak pernah ikut tertimpa pembaruan.",
                            "Riwayat dibatasi 50 entri terakhir agar tetap ringan, dan berkas yang rusak dianggap kosong — tidak pernah mengganggu aplikasi."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.0 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Pembaruan Kecil Tanpa Installer (Tambalan)", new List<string>
                        {
                            "Aplikasi kini dapat menerima perbaikan kecil tanpa mengunduh installer penuh: hanya berkas yang benar-benar berubah yang diunduh (biasanya beberapa ratus KB sampai beberapa MB), diverifikasi sidik jari SHA-256-nya, lalu diganti otomatis setelah aplikasi ditutup dan dibuka kembali.",
                            "Saat aplikasi dibuka, pembaruan diperiksa di latar belakang dan notifikasi lonceng menyebut dengan jelas apa yang diperbaiki, berapa berkas yang berubah dan ukurannya, serta apakah pembaruan itu cukup dipasang sebagai pembaruan kecil atau memerlukan installer penuh.",
                            "Halaman Pembaruan kini menampilkan daftar perbaikan/penambahan yang dibawa pembaruan beserta jenisnya, dengan tombol Perbarui Sekarang untuk pembaruan kecil (tanpa installer) dan tombol Unduh Installer untuk pembaruan besar.",
                            "Perubahan besar (mis. dependensi atau runtime baru, perubahan basis data) tetap dikirim sebagai installer penuh; pengguna yang tertinggal beberapa versi juga diarahkan memakai installer penuh agar seluruh berkas pasti cocok.",
                            "Data pengguna tidak pernah ikut diganti pembaruan: database, templat, hasil PDF, log, appsettings.json, pengaturan penomoran surat, dan pengaturan cetak selalu dilewati.",
                            "Hasil pemasangan dilaporkan lewat notifikasi saat aplikasi dibuka lagi, baik berhasil maupun gagal (beserta langkah berikutnya)."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Penerbitan rilis kini membuat paket tambalan otomatis dari rilis sebelumnya (patch.json + patch-<versi>.zip) sehingga pembaruan kecil bisa diterbitkan tanpa langkah manual tambahan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.4.4 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Surat Template Tercatat di Register Surat", new List<string>
                        {
                            "Setiap surat yang dicetak dari menu Template Surat kini otomatis tercatat di Register Surat: nomor & tanggal surat, keterangan ringkas isian, serta salinan lengkap susunan suratnya ikut disimpan sebagai arsip.",
                            "Surat template dapat dicari di Register Surat lewat nomor surat, nama penerima, NIK, keperluan, maupun isi surat lainnya — pencarian register kini menjangkau seluruh isi surat, bukan hanya kolom-kolom register.",
                            "Baris register surat template menampilkan nama penerima, alamat, tempat & tanggal lahir, dan jenis kelamin yang dibaca dari isian suratnya sendiri, tanpa memaksa isian itu menjadi data kependudukan.",
                            "Surat template bisa dibuka lagi dari Register Surat lewat Edit untuk diperbaiki: nomor lamanya dipertahankan, isian dimuat kembali, dan surat berikutnya tetap memakai nomor urut selanjutnya (memperbaiki surat tidak menghabiskan nomor baru).",
                            "Cetak ulang dari Register Surat tetap menghasilkan surat yang sama persis walaupun template aslinya sudah disunting atau dihapus, karena definisi surat disimpan bersama suratnya.",
                            "Nomor surat yang sudah dipakai surat lain ditolak sebelum surat dicetak, disertai penjelasan langkah perbaikannya, supaya tidak ada dua surat bernomor sama di register.",
                            "Buku Register Surat (cetak daftar) ikut menampilkan nama, alamat, dan identitas penerima surat template serta menyebut nama templatenya pada kolom jenis surat."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Pencarian di daftar Register Surat kini juga memeriksa isi surat, sehingga surat yang datanya tidak berada di kolom register (mis. surat buatan sendiri) tetap dapat ditemukan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.4.3 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Contoh Template Surat Siap Pakai", new List<string>
                        {
                            "Halaman Template Surat kini menyediakan enam contoh surat siap pakai yang bisa langsung diisi dan dicetak, dijadikan dasar template baru, atau sekadar dilihat bentuknya: Surat Pengantar RT/RW, Surat Izin Keramaian, Surat Keterangan Penghasilan, Surat Keterangan Belum Menikah, Surat Undangan Rapat, dan Surat Pengumuman Warga.",
                            "Tombol Contoh Siap Pakai (jumlah) pada bilah alat membuka galeri contoh: daftar contoh di sisi kiri, rincian susunan surat di sisi kanan (awalan & contoh nomor, kop, tata letak, penandatangan, dan seluruh kolom isiannya).",
                            "Setiap contoh dapat dipasang ke daftar Template Surat lewat Pasang ke Daftar Saya (langsung bisa diisi, disunting, dan dihapus seperti template buatan sendiri), dijadikan titik awal wizard lewat Jadikan Contoh Baru, atau dicetak sebagai contoh lewat Pratinjau PDF.",
                            "Tombol Pasang Semua yang Belum Ada memasang seluruh contoh sekaligus, dan contoh yang sudah ada ditandai serta tidak dapat dipasang dua kali sehingga tidak muncul salinan ganda.",
                            "Saat daftar Template Surat masih benar-benar kosong, seluruh contoh dipasang otomatis sekali supaya halaman tidak kosong; contoh yang sengaja dihapus pengguna tidak akan muncul kembali sendiri.",
                            "Setiap contoh sudah lengkap susunannya — kop desa, judul, nomor surat otomatis dengan awalannya sendiri (470, 471, 472, 473, 474, dan 503), teks pembuka, kolom isian identitas warga beserta bagian khusus suratnya (keperluan, rincian acara, penghasilan, jadwal undangan), tanda tangan Kepala Desa, dan catatan penutup.",
                            "Contoh disusun hanya dari fitur yang sudah ada, jadi dapat disunting penuh di wizard (mis. mengganti awalan nomor, menambah kolom, atau mengubah tanda tangan)."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Penyimpanan Template Surat pernah menganggap tabelnya sudah siap untuk semua database karena penandanya berlaku satu proses, sehingga tabel tidak dibuat ketika aplikasi memakai berkas database lain (impor/pencadangan atau pengujian). Penanda kini berlaku per sambungan database."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.4.2 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Template Surat: Membuat Jenis Surat Sendiri", new List<string>
                        {
                            "Menu baru Template Surat dipakai bila jenis surat yang dibutuhkan belum tersedia di menu Buat Surat: pengguna menyusun sendiri bentuk suratnya lewat wizard lima langkah, lalu memakainya seperti surat bawaan — isi kolomnya, nomor terisi otomatis, dan hasilnya langsung dicetak menjadi PDF.",
                            "Langkah 1 Identitas (nama & keterangan template), Langkah 2 Elemen Surat, Langkah 3 Teks Surat, Langkah 4 Kolom Isian, Langkah 5 Tata Letak & Kaki, dan Langkah 6 Pratinjau — dengan daftar langkah di sisi kiri yang bisa diklik kembali kapan saja.",
                            "Elemen surat dipilih dengan kotak centang: pakai kop desa atau tidak, judul dan baris tambahannya, nomor surat beserta awalan dan polanya, serta baris tempat & tanggal.",
                            "Teks Surat: tambahkan blok teks kosong untuk diketik sendiri, atau sisipkan kalimat baku surat keterangan desa; setiap blok bisa diatur perataannya (kiri/tengah/kanan/rata kiri-kanan), ditebalkan, dimiringkan, dan diurutkan ulang.",
                            "Kolom Isian: tentukan sendiri label kolom (NIK, nama, dan lainnya) beserta tipenya — Teks, NIK (diperiksa 16 angka), Angka, Tanggal (diisi 17-08-2026, dicetak 17 Agustus 2026), Paragraf, atau Pilihan (dropdown berisi daftar yang Anda tentukan); kolom boleh ditandai wajib diisi dan diberi nilai awal. Tersedia tombol Tambah Kolom Umum yang mengisikan kolom identitas warga yang paling sering dipakai.",
                            "Tata letak & kaki surat: kolom isian dapat dicetak sebagai grid (tabel bergaris) atau baris 'Label : Isi', ditambah blok tanda tangan Kepala Desa/Sekretaris Desa dan teks kaki tambahan.",
                            "Langkah Pratinjau membuat contoh surat dengan isi contoh; bila masih kurang, tombol Perbaiki/Kembali membawa ke langkah mana pun untuk melengkapi, lalu Segarkan Pratinjau menampilkan hasil terbarunya.",
                            "Setelah pratinjau disetujui, Simpan & Isi Surat menyimpan template sekaligus membuka formulir pengisian yang hanya memuat kolom yang sudah dipilih.",
                            "Nomor surat memakai penghitung sendiri per template (mis. 471/001/Ds/2026) dan melanjut otomatis ke 471/002 pada surat berikutnya; urutan mulai dari 1 lagi setiap tahun. Nomor tetap bisa diubah manual bila kantor desa memakai nomor lain.",
                            "Halaman daftar Template Surat menyediakan pencarian cepat, kartu ringkasan (total template, tampil saat ini, punya kolom isian, memakai kop desa), tombol Buat Template Baru, Edit, Duplikat, Hapus, Pratinjau, serta Isi Surat; daftar menampilkan judul, contoh nomor berikutnya, jumlah kolom, dan ringkasan susunan surat.",
                            "Template disimpan di database aplikasi, jadi ikut aman pada pencadangan/impor database dan tidak hilang saat aplikasi diperbarui."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Pemeriksaan pola nomor surat menolak pola yang benar seperti {awalan}/{urut:000}/Ds/{tahun} karena hanya menerima tulisan {urut} tanpa penanda lebar angka; kini keduanya diterima, sehingga pratinjau dan penyimpanan template dapat berjalan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.4.1 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Lampiran Berkas pada Buku Agenda Surat Masuk/Keluar", new List<string>
                        {
                            "Buku agenda Surat Masuk dan Surat Keluar sekarang dapat menyimpan berkas lampiran pada tiap baris, sama seperti buku SK/Perdes/Perkades: surat pindai cukup dilampirkan sekali, lalu bisa dibuka kembali kapan saja dari daftar.",
                            "Berbeda dengan arsip Keputusan (yang menerima Word/PDF), agenda surat masuk/keluar hanya menyimpan berkas PDF dan gambar (*.pdf, *.jpg, *.jpeg, *.png, *.bmp, *.gif, *.webp, *.tif, *.tiff) — berkas Word/Excel ditolak dengan pesan penjelas, baik di formulir maupun di penyimpanan arsip.",
                            "Pada formulir input/edit tersedia tombol Pilih Berkas, Buka, dan Hapus beserta nama berkas dan label format (PDF/JPG/…); berkas tersalin ke folder ArsipSuratFiles baru setelah data disimpan dan dihapus bersama barisnya bila baris agenda dihapus.",
                            "Daftar agenda mendapat kolom Lampiran (label format + nama berkas + tombol Buka) serta penanda jumlah baris berlampiran pada bilah alat, sehingga surat yang sudah dipindai mudah ditemukan.",
                            "Berkas lampiran lama otomatis dibersihkan saat diganti atau dibuang, dan aplikasi menolak nama berkas yang mengarah ke luar folder arsip."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Tampilan buku agenda Surat Masuk/Keluar disamakan dengan buku SK/Peraturan: kepala halaman dengan tombol MASUK/KELUAR, empat kartu ringkasan (total surat, tampil saat ini, tahun terbaru, dan jumlah berlampiran), kotak pencarian cepat, filter tahun beserta pilihan 'Semua Tahun', daftar bergaya modern, serta pesan 'tidak ada data' ketika pencarian tidak menemukan apa pun.",
                            "Buku agenda kini punya pencarian cepat (nomor, asal/tujuan, perihal, isi ringkas, keterangan, dan tanggal) dan baris dapat dibuka dengan klik dua kali seperti di buku Keputusan.",
                            "Cetak PDF buku agenda dapat mencetak satu tahun tertentu maupun seluruh tahun sekaligus lewat pilihan 'Semua Tahun'.",
                            "Seluruh teks halaman Pengaturan Aplikasi (judul bagian, label, penjelasan, dan pesan status) disusun ulang agar lebih ringkas dan konsisten, termasuk istilah teknis seperti Access Token, Verify Token Webhook, serta nama tombol pengelolaan template Sheet.",
                            "Kolom FileLampiran pada tabel ArsipSurat ditambahkan otomatis ke database yang sudah ada, jadi data agenda dan arsip lama tetap utuh tanpa perlu tindakan khusus."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Halaman Pengaturan Aplikasi gagal terbuka — muncul dialog 'Error tidak tertangani' — ketika bagian Penomoran Surat ditampilkan. Penyebabnya baris kode jenis, keterangan urutan, dan contoh nomor memakai pengikatan teks yang secara bawaan mengikat dua arah (Run.Text) ke properti hanya-baca.",
                            "Seluruh pengikatan Run.Text dan ProgressBar/Slider.Value kini tegas satu arah, dan pengujian otomatis memeriksa semua berkas XAML agar halaman tidak lagi gagal dibuka karena sebab yang sama."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.4.0 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Pengaturan Aplikasi Bernavigasi", new List<string>
                        {
                            "Halaman Pengaturan Aplikasi kini memakai navigasi bagian di sisi kiri: Umum & Preferensi, Penomoran Surat, Jam Layanan WhatsApp, Gateway WhatsApp, Kredensial Google, Formulir & Sheet, dan Informasi.",
                            "Hanya satu bagian yang ditampilkan pada satu waktu, sehingga pengaturan tidak lagi berupa deretan kartu panjang yang harus dicari dengan menggulir; setiap bagian punya judul dan penjelasan singkat sendiri.",
                            "Klik chip status WhatsApp/Sheet di statusbar tetap membawa langsung ke bagian yang dituju — sekarang bagiannya sekaligus terpilih, bukan hanya digulirkan."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Pengaturan Penomoran Surat", new List<string>
                        {
                            "Awalan (kode klasifikasi) nomor surat dapat diganti sendiri per jenis surat di Pengaturan Aplikasi → Penomoran Surat, misalnya SKD 470 menjadi 471, tanpa menunggu pembaruan aplikasi.",
                            "Setiap baris menampilkan jenis surat, kodenya, keterangan apakah nomornya berbagi satu urutan dengan SKD atau berurutan sendiri, contoh nomor yang akan tercetak (mis. 471/001/Ds/2026), serta tombol kembalikan ke bawaan.",
                            "Perubahan disimpan sekali klik dan langsung berlaku untuk nomor surat berikutnya tanpa menutup aplikasi; nomor surat yang sudah terbit tidak ikut berubah.",
                            "Awalan yang tidak sah ditolak dengan pesan jelas (hanya huruf, angka, titik, tanda hubung, dan garis bawah — tanda '/' tidak boleh karena dipakai memisah segmen nomor), dan awalan yang dikosongkan berarti kembali ke bawaan.",
                            "Aplikasi memperingatkan bila satu awalan dipakai beberapa jenis surat yang nomor urutnya dihitung sendiri-sendiri sehingga nomor berpotensi kembar (mis. 474.2 pada kenal lahir/ahli waris dan surat numpang nikah).",
                            "Penyesuaian disimpan pada profil pengguna, terpisah dari berkas bawaan aplikasi, sehingga tidak hilang saat aplikasi diperbarui; berkas lama dicadangkan lebih dulu setiap kali disimpan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.3.0 (19 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Paket Pernikahan NTCR (N1–N6)", new List<string>
                        {
                            "Alur sekali isi: cukup sekali mengisi data satu pasangan calon pengantin, aplikasi menyimpan seluruh blanko yang dicentang sekaligus (bawaan N1–N5, dengan tombol cepat Paket standar dan Semua) lalu menghasilkan satu berkas PDF gabungan — satu blanko per halaman, urut N1 → N6, siap cetak tanpa menyusun berkas satu per satu.",
                            "Setiap blanko dalam paket tetap menjadi surat tersendiri: punya nomor surat, status, dan keperluan masing-masing, muncul di Register NTCR, serta tetap dapat dibuka, diedit, atau dicetak ulang satu per satu.",
                            "Kepala blanko tercetak persis lembar aslinya: LAMPIRAN IV/VI/VII/VIII/IX/X, KEPUTUSAN DIREKTUR JENDERAL BIMBINGAN MASYARAKAT ISLAM NOMOR 473 TAHUN 2020, TENTANG PETUNJUK TEKNIS PELAKSANAAN PENCATATAN PERNIKAHAN, plus label Model N1–N6 di sudut kanan atas.",
                            "Kerapatan cetak menyesuaikan sendiri: dokumen dirender ulang (normal → rapat → paling rapat) sampai jumlah halaman tepat sama dengan jumlah blanko, sehingga tidak ada blanko yang melimpah ke halaman sisa dan blanko terpanjang pun tetap muat satu halaman.",
                            "Form paket hanya menampilkan blok isian yang relevan dengan blanko yang dicentang (identitas orang tua, pejabat desa, kolom isbat, rencana akad, dan seterusnya), sehingga isian tidak menumpuk."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Surat Numpang Nikah (N8)", new List<string>
                        {
                            "Surat keterangan numpang nikah baru di menu NTCR: satu halaman berkop desa, memuat identitas calon suami, calon istri, serta desa/kelurahan, kecamatan, dan kabupaten/kota tempat akan melangsungkan akad nikah.",
                            "Nomor surat mengikuti template: 474.2/xxx/Ds/tahun untuk numpang nikah, sedangkan blanko NTCR N1–N6 memakai 474.3/xxx/Ds/tahun.",
                            "Nomor surat terisi otomatis di kotak Nomor Surat begitu form dibuka (baik form paket maupun surat tunggal), jadi nomor tidak perlu dicari manual.",
                            "N8 bukan blanko Kepdirjen sehingga tidak memakai blok LAMPIRAN dan tidak ikut dalam paket N1–N6; identitas calon istri yang tidak tercetak pada surat ini (NIK, agama, pekerjaan) juga tidak diwajibkan."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Register NTCR Terpisah", new List<string>
                        {
                            "Register NTCR kini punya halaman, judul, kolom, dan daftar filter sendiri — terpisah penuh dari Register Surat desa umum.",
                            "Kolom khas Register NTCR: blanko (N1–N8), calon suami/pemohon, tempat & tanggal lahir, alamat calon suami, calon istri, alamat calon istri, serta tujuan surat (KUA/PPN, Pengadilan Agama, atau tempat numpang nikah).",
                            "Register Surat desa umum tidak lagi memuat kolom calon mempelai sama sekali dan daftar jenis suratnya bersih dari blanko NTCR.",
                            "Mengedit surat dari Register NTCR kembali ke Register NTCR (bukan pindah ke Register Surat), dan ekspor serta cetak register memakai nama berkas RegisterNtcr."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Blanko N2: daftar “surat-surat yang diperlukan untuk diperiksa” dicetak persis template — butir 1–6 baku, sedangkan butir 7 & 8 dibiarkan titik-titik untuk diisi tangan petugas KUA.",
                            "Blanko N3 (permohonan pencatatan isbat) kini longgar: seluruh isian boleh dikosongkan karena blanko ini jarang dipakai; bagian yang kosong tercetak titik-titik seperti template dan diisi manual.",
                            "Daftar lampiran baku N2 & N3 dipusatkan menjadi satu sumber data sehingga isi blanko konsisten di semua tempat.",
                            "Penamaan blanko NTCR (kode, judul, kegunaan, nomor lampiran, dan keikutsertaan dalam paket) terpusat di satu katalog yang dipakai bersama oleh generator PDF, form isian, menu, dan register."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Memperbaiki instalasi baru yang daftar jenis suratnya tidak pernah terisi, sehingga penomoran surat dan blanko NTCR tidak berfungsi pada pemasangan baru.",
                            "Menghapus blanko N7 (penolakan kehendak nikah/rujuk) dari aplikasi karena diterbitkan KUA, bukan kantor desa — sisa datanya dibersihkan otomatis dengan pencadangan database lebih dulu, dan pembersihan dibatalkan bila pencadangan gagal."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.2.0 (18 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Keamanan & Pembaruan", new List<string>
                        {
                            "Kredensial Google (Client ID & Client Secret) kini dapat diisi langsung dari halaman Pengaturan Aplikasi — tanpa menyunting berkas konfigurasi. Kredensial disimpan terenkripsi di komputer (DPAPI, terikat akun Windows) dan tidak ikut terdistribusi bersama aplikasi.",
                            "Pembaruan aplikasi melalui GitHub Releases: aplikasi memeriksa rilis terbaru, mengunduh pemasangnya, lalu memverifikasi sidik jari SHA-256 sebelum dijalankan — pemasangan hanya dilanjutkan bila berkas benar-benar utuh dan sesuai.",
                            "Tombol Uji pada kredensial Google memastikan Client ID/Secret benar dengan mencoba masuk dan menampilkan email akun yang berhasil terhubung."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Berkas kunci layanan Google (sudesapp.json) tidak lagi disertakan dalam paket distribusi — kunci tidak pernah tersimpan di folder pemasangan pengguna.",
                            "Kredensial Google dipindah ke penyimpanan aman per-pengguna sehingga pembaruan aplikasi tidak menimpa atau membocorkan kunci.",
                            "Klien Google Drive/Sheets otomatis di-reset saat akun diganti atau dihapus — mencegah data antar-akun tercampur dalam satu sesi.",
                            "Pesan panduan penyiapan Google diperbarui agar lebih jelas ketika kredensial belum diatur."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Memperbaiki klien Google Sheets yang masih memakai akun lama setelah beralih ke akun Google lain.",
                            "Memperbaiki pengenalan ID berkas pada tautan Google Drive format baru sehingga unduhan template tidak gagal.",
                            "Memperbaiki validasi tautan Drive yang sebelumnya sempat memunculkan pesan keliru saat mengunduh lampiran."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.1.2 (16 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Layanan Surat Online (WhatsApp)", new List<string>
                        {
                            "Mode tautan Google Form/Sheet: pesan apa pun dari warga otomatis dibalas tautan Google Form (dengan Token penghubung) untuk diisi sendiri — setelah form tersimpan, jawaban dari Google Sheet masuk ke database, PDF surat dibuat otomatis, dan tautan unduh PDF dikirim balik ke WhatsApp warga; status pemrosesan ditulis di kolom Status pada Google Sheet.",
                            "Pipeline otomatis ujung-ke-ujung: permintaan surat via WhatsApp diproses sendiri tanpa operator — surat dibuat lewat jalur yang sama dengan input form biasa (tercatat di register), PDF di-generate dengan generator resmi dari data yang tersimpan, lalu dikirim balik ke nomor WhatsApp pemohon.",
                            "Status permintaan yang jujur: SELESAI bila PDF terkirim ke warga; bila gagal di tengah jalan, permintaan otomatis dikembalikan ke PERLU_PERBAIKAN dengan catatan penyebab — warga mendapat balasan penjelasan, operator melihatnya di panel, dan tidak ada permintaan yang hilang diam-diam.",
                            "Notifikasi lonceng kini terhubung ke peristiwa layanan online: permintaan baru masuk, hasil proses otomatis, dan status kirim PDF.",
                            "Jam & hari layanan otomatis: pemrosesan dapat dibatasi pada jam tertentu dan hari kerja tertentu (default Senin–Jumat). Mendukung rentang lintas tengah malam. Di luar jam/hari layanan, permintaan warga tetap tercatat dan mendapat balasan berisi kapan pemrosesan dilanjutkan (± durasi).",
                            "Catch-up otomatis: begitu jam & hari layanan dibuka kembali, seluruh permintaan yang tertunda diproses berurutan tanpa campur tangan operator — perhitungannya sadar kalender (permintaan Jumat sore diproses Senin pagi, bukan Sabtu).",
                            "Gateway WhatsApp RESMI via WhatsApp Cloud API (Meta): konfigurasi access token & Phone Number ID di Pengaturan Aplikasi; pengiriman berupa teks/tautan lewat Graph API dengan konversi nomor otomatis. Tombol Uji koneksi memverifikasi token & nomor secara langsung.",
                            "Halaman Panduan WhatsApp in-app (menu BANTUAN): langkah daftar Meta for Developers, pengambilan token & Phone Number ID, setup webhook pesan masuk (domain HTTPS sendiri atau tunnel gratis cloudflared), dan rencana uji alur penuh — lengkap dengan ilustrasi visual."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Surat selesai kini dikirim sebagai tautan unduh Google Drive (folder “Surat Online”) — warga cukup klik untuk mengunduh PDF; aplikasi tidak lagi mengirim berkas apa pun lewat WhatsApp, hanya teks/tautan.",
                            "Listener webhook pesan masuk Cloud API kini otomatis berjalan saat aplikasi dibuka — tidak perlu lagi setup terpisah.",
                            "Panel Layanan Online menampilkan kolom Sumber (WhatsApp / Google Sheet) dan status Google Sheet dapat diuji langsung dari Pengaturan Aplikasi.",
                            "Indikator status gateway WhatsApp di statusbar: WA Terhubung (Cloud API, hijau) atau WA Token belum diisi (oranye) — diperbarui otomatis tiap 10 detik beserta tooltip penjelasan dan arah perbaikan.",
                            "Pergantian gateway dan token langsung berlaku tanpa restart aplikasi (WaGatewaySelector meneruskan setiap panggilan ke gateway aktif).",
                            "Anti surat ganda: klik manual Setujui & Buat Surat pada permintaan yang sedang/sudah diproses otomatis kini ditolak dengan pesan jelas — kedua jalur saling mengunci.",
                            "Resolver generator PDF dirapikan menjadi satu sumber (SuratPdfHelper) — menghapus duplikasi yang sebelumnya disalin ke dua tempat.",
                            "Pengaman hari layanan: bila seluruh hari tidak dicentang, otomatis diperlakukan sebagai setiap hari agar layanan tidak pernah mati total karena salah konfigurasi."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug — Layanan Online", new List<string>
                        {
                            "Permintaan tidak lagi terjebak selamanya di status DIPROSES bila pembuatan surat gagal — sekarang di-rollback ke PERLU_PERBAIKAN dengan balasan otomatis ke warga.",
                            "Deteksi jenis surat diperbaiki: baris pertama pesan kini diprioritaskan sehingga kata kunci di isi field (mis. alamat 'dekat SKCK') tidak lagi salah menentukan jenis surat.",
                            "Race condition refresh daftar permintaan diperbaiki — refresh yang terpicu bersamaan (ganti filter + permintaan baru + klik Segarkan) kini tergabung tanpa menulis ganda ke daftar.",
                            "WaEngine.Dispose aman dipanggil berkali-kali dan CancellationTokenSource kini dibebaskan dengan benar."
                        }),
                        new CatatanRilisSubSection("Pembaruan Lanjutan — UI Layanan Online, Notifikasi & Tentang", new List<string>
                        {
                            "Menu 'Permintaan Online' berganti nama menjadi 'Layanan Online' — mencerminkan perannya sebagai pusat layanan surat online, bukan sekadar daftar permintaan.",
                            "Panel Layanan Online ditulis ulang: tabel permintaan kini mengisi seluruh sisa tinggi halaman (tidak lagi terkurung tinggi tetap yang merusak tata letak), kolom teks panjang terpotong rapi dengan tooltip, dan panel Detail Permintaan baru menampilkan pesan asli pemohon, catatan operator, surat yang terhubung, dan waktu diproses.",
                            "Status berwarna langsung terbaca di tabel (BARU biru, PERLU_PERBAIKAN oranye, DITOLAK merah, SELESAI hijau) dan baris ter-update di tempat saat refresh — pilihan baris tidak lagi hilang dan status terbaru selalu terlihat.",
                            "Tombol Tolak kini meminta konfirmasi dan menolak permintaan yang sudah SELESAI; Tandai Dibaca konsisten bekerja pada baris terpilih; seluruh tombol diberi tooltip penjelasan. Simulasi pesan & riwayat kirim menjadi bagian terlipat agar halaman ringkas.",
                            "Notifikasi dipercepat: dialog modal per permintaan masuk dihapus (cukup lonceng + badge), thread latar tidak pernah menunggu UI (BeginInvoke), animasi lonceng dibangun sekali lalu diputar ulang, hitungan belum-baca dihitung inkremental, dan daftar notifikasi tervirtualisasi — hanya item terlihat yang dirender.",
                            "Teks notifikasi kini multi-line tanpa ScrollViewer (pesan dipecah per baris), dan setiap hasil pipeline otomatis ikut dinotifikasikan: surat terkirim ke warga, gagal kirim PDF, PDF gagal dibuat, hingga proses otomatis gagal — tidak ada hasil yang lewat diam-diam.",
                            "Badge menu Layanan Online diperbarui tanpa query database pada setiap permintaan masuk — sinkronisasi ke database hanya saat aplikasi start dan saat panel dibuka.",
                            "Halaman Tentang Aplikasi diremajakan: judul berubah dari \"TENTANG APLIKASI\" menjadi \"TENTANG SUITAPPSU DESA\", deskripsi diperbarui mencerminkan layanan online WhatsApp & backup Google Drive, dan bagian Ikon Aplikasi dihapus karena tidak digunakan lagi."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.1.1 (16 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Pengaturan Formulir", new List<string>
                        {
                            "Unduh formulir resmi satu klik: tombol Unduh dari Sumber mengambil seluruh daftar formulir dari infosadaradmindukkarawang.id (20 formulir: F-1.01 s.d. Surat Kuasa Pengasuhan Anak) dan menyimpannya sebagai template PDF di folder Templates.",
                            "Pintar membaca link Google Drive: halaman sumber memakai tautan berbagi Drive — aplikasi mengonversinya ke unduhan langsung sehingga formulir benar-benar terunduh sebagai PDF (divalidasi header %PDF sebelum dipakai).",
                            "Penamaan template cerdas mengikuti kode formulir: F-1.02.pdf, F-1.03.pdf, dst.; kode yang dipakai beberapa formulir diberi deskriptor (F-2.01_AKTA_KELAHIRAN.pdf, F-2.01_AKTA_KEMATIAN.pdf, dan seterusnya) sehingga tidak saling menimpa dan kompatibel dengan template lama.",
                            "Tawaran unduh otomatis: bila jumlah template kurang dari 10 saat halaman Pengaturan Formulir dibuka, muncul popup tawaran mengunduh formulir yang belum ada — cukup sekali per sesi, tidak mengganggu berulang.",
                            "Backup otomatis formulir ke Google Drive (pengaturan baru di Pengaturan Aplikasi): template tersinkron ke folder SuDesApp-Formulir di Drive setiap ada unduhan baru atau template ditambah/dihapus; berjalan hanya bila akun Google terhubung, tanpa duplikat.",
                            "Google Drive sebagai sumber utama: bila user login Drive, formulir yang belum ada diambil lebih dulu dari folder SuDesApp-Formulir (cadangan sebelumnya) — web sumber hanya diakses bila tidak tersedia di Drive."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Pengaturan Formulir", new List<string>
                        {
                            "Statusbar baru di atas tombol dengan tinggi tetap: menampilkan progres unduh per formulir di kiri dan jumlah template (mis. \"15 formulir\") di kanan — teks panjang tidak lagi menggeser tombol (sebelumnya status ditampilkan ganda dan merusak tata letak).",
                            "Dialog hasil unduhan kini transparan: menampilkan path folder tujuan lengkap dan rincian per formulir (✓ berhasil / – dilewati beserta alasannya), sehingga masalah unduh mudah diketahui.",
                            "Penulisan file atomik: PDF ditulis ke berkas sementara, divalidasi, baru di-rename ke nama final — berkas setengah jadi tidak akan pernah tampil sebagai template.",
                            "Endpoint unduhan Drive diperbarui ke drive.usercontent.google.com (confirm=t) yang lebih andal terhadap halaman perantara Google.",
                            "Tombol Unduh Terpilih dihapus karena tidak terpakai; baris tombol kini ringkas: Unduh dari Sumber, Segarkan, Tambah, Hapus."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Memperbaiki error build CS1022 (kurung berlebih) pada FormulirViewModel.",
                            "Memperbaiki popup tawaran unduh yang tidak pernah muncul karena dipicu saat halaman masih dalam status loading."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.1.0 (15 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru", new List<string>
                        {
                            "Menu NTCR (N1-N4): Surat Pengantar Nikah, Surat Keterangan Untuk Nikah, Surat Persetujuan Calon Mempelai, dan Surat Keterangan Orang Tua untuk kelengkapan pendaftaran pernikahan.",
                            "Form input khusus NTCR dengan data calon suami/istri serta nama orang tua kedua belah pihak.",
                            "Dukungan penuh buat, edit, register dengan nomor surat otomatis, dan cetak PDF (474.1/001/Ds/2026) untuk setiap jenis NTCR."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Basis data otomatis diperbarui tanpa kehilangan data lama: tabel NTCR dibuat saat aplikasi dimulai."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Buku SK / Peraturan", new List<string>
                        {
                            "Lampiran PDF pada Buku SK/Perdes/Perkades: formulir kini menerima berkas PDF selain Word — isian diisi manual dengan pengingat, berkas ikut tersalin ke arsip dan dapat dibuka dari daftar.",
                            "Pencarian cepat di Buku SK/Peraturan: cari nomor, tentang, keterangan, atau tahun secara langsung, lengkap dengan status 'Menampilkan X dari Y data'.",
                            "Pindah arsip otomatis: bila jenis berkas tidak sesuai dengan formulir (mis. Perdes dipilih di form SK), aplikasi menawarkan memindahkan formulir beserta datanya ke arsip jenis yang benar tanpa memilih ulang berkas."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Buku SK / Peraturan", new List<string>
                        {
                            "Arsip keputusan dan buku agenda surat masuk/keluar dimigrasi ke database SQLite (tabel ArsipKeputusan & ArsipSurat): lebih tangguh, cepat, dan bebas berkas terkunci. Data Excel ArsipKeputusan.xlsx dan ArsipSurat.xlsx lama diimpor otomatis sekali saat pertama dijalankan, dan masih dapat diimpor ulang dari Excel kapan pun.",
                            "Backup otomatis arsip sebelum setiap perubahan data (disimpan 30 hari) dan penyimpanan diulang otomatis bila berkas/database sempat terkunci.",
                            "Deteksi jenis dokumen diperbaiki: Surat Keputusan, Peraturan Desa, dan Keputusan Kepala Desa kini tidak lagi tertukar — kop/judul dokumen menjadi patokan.",
                            "Validasi nomor ganda per jenis arsip, ID arsip tahan celah, pembacaan tanggal lebih toleran (dd-MM-yyyy, dd/MM/yyyy, dsb.), dan nomor urut tabel PDF mulai dari 1 di setiap halaman.",
                            "Menghapus data kini ikut membersihkan berkas lampiran terkait agar tidak menumpuk file yatim; label kolom menjadi 'File Lampiran' (Word/PDF).",
                            "Perbaikan crash saat membuka form input keputusan (sisa resource tema lama) dan tombol Cetak yang tidak merespons pada filter 'Semua Tahun' (kini mencetak seluruh tahun)."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Google Drive", new List<string>
                        {
                            "Pengaturan Aplikasi (menu baru): pusat preferensi aplikasi dengan tombol geser ON/OFF yang tersimpan langsung — login otomatis Google, backup otomatis ke Drive saat ditutup, riwayat aktivitas, pembersihan PDF sementara, dan pembersihan hasil ekspor lama. Menu PENGATURAN juga disusun ulang: Pengaturan Aplikasi, Pengaturan Surat, Pengaturan Formulir, Ubah Kata Sandi, Login dengan Google, Alat Perbaikan (dulu Alat Canggih), dan Pencadangan Database. Pengaturan login & keamanan dari halaman Pengaturan Surat dipindah ke sini.",
                            "Alat Perbaikan kini halaman terpadu di menu utama (bukan jendela terpisah): optimasi & backup database, ekspor register, uji skrip, cek dispatcher WhatsApp, dan pemeriksaan layar tetap dalam satu tempat.",
                            "Riwayat Aktivitas (menu baru): setiap pembuatan, pengeditan, perubahan status, dan penghapusan surat maupun arsip keputusan/agenda tercatat otomatis beserta pengguna yang melakukan (email Google atau admin), waktu, dan detail — dengan pencarian dan tombol bersihkan riwayat.",
                            "Integrasi Google Drive: login dengan akun Google (pemilih akun selalu tampil, tombol Batal dan batas waktu 3 menit berfungsi andal) serta tampilan berkas Drive di dalam aplikasi.",
                            "Login awal dengan Google: bila ada akun Google yang sudah pernah login, layar login otomatis menampilkan progres \"Masuk sebagai {email}\" lengkap dengan foto profil — tanpa perlu mengetik username/password (setara login admin). Bila tidak ada, form username/password biasa ditampilkan, dengan tombol alternatif \"Masuk dengan Akun Google\".",
                            "Pengaturan login di halaman Setelan (LOGIN DAN KEAMANAN): aktifkan \"Selalu tampilkan form username/password lebih dulu\" untuk mematikan login otomatis Google — form manual selalu muncul lebih dulu, akun Google tetap bisa dipakai lewat tombol alternatif.",
                            "Backup data ke Google Drive: database, zip lampiran arsip keputusan (ArsipKeputusanFiles), dan zip template surat diunggah bersamaan ke folder SuDesApp-Backup — otomatis maksimal 1x sehari saat aplikasi ditutup, atau manual lewat tombol di halaman Google Drive.",
                            "Cadangan lama otomatis dipangkas (maks. 30 berkas per kelompok), berkas sedang terkunci dilewati tanpa menggagalkan backup, dan tidak ada duplikat meski backup diulang.",
                            "Pemulihan 1-klik dari Google Drive: pilih berkas cadangan .db di halaman Drive, klik Pulihkan Database — berkas diunduh, divalidasi, dan diimpor dengan konfirmasi; database lama otomatis dicadangkan dan data baru aktif setelah aplikasi dijalankan ulang.",
                            "Keandalan impor/ekspor database diperkuat: penulisan WAL dipastikan selesai dan koneksi pool dibersihkan sebelum berkas database diganti — mencegah kerusakan indeks akibat proses impor."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.0.0 (14 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru", new List<string>
                        {
                            "Migrasi total antarmuka ke WPF modern dengan 6 tema: Light, Dark, Biru Office, Green, Pink, dan Slate.",
                            "Alat Canggih: optimasi & backup database, ekspor register, serta uji skrip PowerShell / Python / Node.js dengan log langsung.",
                            "Pemeriksaan Resolusi Layar: deteksi resolusi & skala DPI otomatis, ukuran jendela menyesuaikan semua monitor (laptop 1366×768 hingga 4K), plus chip resolusi di status bar.",
                            "Permohonan Rekening Koran kini tersedia di aplikasi baru lengkap dengan PDF otomatis.",
                            "Ekspor PDF (Save As) langsung dari pratinjau dokumen maupun register surat."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Register surat: sortir dengan klik header kolom, baris zebra, scrollbar modern yang terlihat jelas, dan paginasi lebih baik.",
                            "Klik kanan pada baris surat: Ubah Status, Pratinjau, Cetak, Edit, dan Ekspor PDF.",
                            "Edit surat yang sudah dibuat langsung dari register — data lama dimuat ulang ke form."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Memperbaiki crash saat membuka register surat (akses database bersamaan).",
                            "Memperbaiki scrollbar yang tidak terlihat atau macet pada beberapa halaman.",
                            "Pratinjau PDF kini menampilkan pesan kesalahan yang jelas bila pembuatan dokumen gagal."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 1.4.0 (11 September 2026)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru", new List<string>
                        {
                            "Tampilan modern untuk form Ganti Kata Sandi.",
                            "Label jenis surat ditampilkan pada pratinjau dan register surat."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Perombakan total tampilan form Domisili Instansi sehingga dapat dibuka dan disimpan dengan benar.",
                            "Perbaikan tombol 'Lihat sandi' pada halaman masuk.",
                            "Dialog Tentang Aplikasi dan Catatan Rilis kini mengikuti tema terang/gelap."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 1.3.3.0 (8 Juni 2025)",
                    false,
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru", new List<string>
                        {
                            "Mengganti total library PDF dari iText7 ke QuestPDF agar aplikasi lebih ringan dan performa cetak lebih cepat.",
                            "Menambahkan menu 'Catatan Rilis' untuk melihat riwayat pembaruan langsung di aplikasi."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Optimalisasi fitur Surat Masuk, Surat Keluar, SK, PERDES, dan PERKADES, termasuk perbaikan pada proses penyimpanan dan pencetakan.",
                            "Menyesuaikan ukuran halaman default untuk semua surat menjadi standar A4 agar mudah dicetak.",
                            "Tampilan tabel pada semua laporan PDF diubah menjadi lebih modern dan mudah dibaca.",
                            "Performa aplikasi secara umum ditingkatkan, terutama saat membuka dan mencetak laporan.",
                            "Menyempurnakan detail tata letak pada beberapa jenis surat dan memperbaiki format alamat.",
                            "Logika penomoran halaman pada laporan kini lebih cerdas, halaman akan selalu terisi penuh."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Mengatasi berbagai masalah kecil pada proses ekspor PDF untuk meningkatkan stabilitas dan kualitas hasil.",
                            "Memperbaiki proses pengecekan pembaruan aplikasi agar lebih andal.",
                            "Memperbaiki masalah format tanggal yang tidak konsisten di seluruh aplikasi.",
                            "Memperbaiki bug kritis yang menyebabkan data baru kadang tidak tampil di tabel setelah disimpan.",
                            "Mengatasi error saat pembuatan laporan PDF multi-halaman."
                        })
                    })
            };
        }
    }
}
