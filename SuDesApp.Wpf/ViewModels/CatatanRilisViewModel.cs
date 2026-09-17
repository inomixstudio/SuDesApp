using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Entri sub-bagian dalam satu versi catatan rilis.
    /// </summary>
    public class CatatanRilisSubSection : ObservableObject
    {
        public string Heading { get; }
        public List<string> Points { get; }

        public CatatanRilisSubSection(string heading, List<string> points)
        {
            Heading = heading;
            Points = points;
        }
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
        public string VersionFooter => "Surat Desa V.2.1.2  \u2022  Sumberjaya Dev.";

        public List<CatatanRilisEntry> Entries { get; }

        public CatatanRilisViewModel(ILogger<CatatanRilisViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Entries = new List<CatatanRilisEntry>
            {
                new CatatanRilisEntry(
                    "Versi 2.1.2 (16 September 2026)",
                    true,
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
                            "Migrasi total antarmuka ke WPF modern dengan 7 tema: Emerald, Light, Dark, Blue, Green, Pink, dan Slate.",
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

            _logger.LogInformation("Catatan rilis dimuat: {Count} versi", Entries.Count);
        }
    }
}
