using System.Collections.ObjectModel;
using System.Windows.Input;
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
        public List<CatatanRilisSubSection> Sections { get; }

        /// <summary>
        /// True HANYA untuk entri paling atas — diatur otomatis oleh
        /// <see cref="CatatanRilisViewModel.SusunRiwayatVersi"/> dari posisi dalam
        /// daftar, bukan bendera manual per entri. Dulu bendera itu ditulis tangan
        /// dan 2.5.9 ikut menandai dirinya "TERBARU" sehingga dua badge tampil
        /// bersamaan.
        /// </summary>
        public bool IsLatest { get; internal set; }

        public CatatanRilisEntry(string versionText, List<CatatanRilisSubSection> sections)
        {
            VersionText = versionText;
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
        public string Subtitle => "Pembaruan, perbaikan, dan fitur baru dari waktu ke waktu";
        /// <summary>
        /// Identitas aplikasi pada statusbar halaman ("Surat Desa V.x.y.z  •  ...") —
        /// disusun otomatis dari atribut assembly (csproj) oleh <see cref="IdentitasAplikasi"/>,
        /// sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string VersionFooter => IdentitasAplikasi.FooterVersi;

        /// <summary>
        /// Jumlah kartu versi per batch. Halaman dibuka dengan satu batch (ringkas),
        /// lalu tombol "Muat versi lebih lama" menambah satu batch lagi — seluruh
        /// riwayat tetap bisa dibaca tanpa membanjiri halaman sejak awal.
        /// </summary>
        public const int MaksKartu = 3;

        /// <summary>Kartu versi yang sedang tampil (terbaru di atas). Bisa bertambah
        /// lewat <see cref="MuatVersiLamaCommand"/>.</summary>
        public ObservableCollection<CatatanRilisEntry> Entries { get; } = new();

        /// <summary>Seluruh riwayat versi aplikasi — sumber tunggal kartu-kartu di halaman.</summary>
        public IReadOnlyList<CatatanRilisEntry> SemuaVersi { get; }

        /// <summary>Tambah satu batch kartu versi lama (nonaktif saat semua sudah tampil).</summary>
        public ICommand MuatVersiLamaCommand { get; }

        /// <summary>Jumlah versi lama yang belum ditampilkan.</summary>
        public int SisaVersi => SemuaVersi.Count - Entries.Count;

        /// <summary>Masih ada versi lama yang bisa dibuka?</summary>
        public bool AdaSisaVersi => SisaVersi > 0;

        /// <summary>
        /// Ringkasan riwayat ("Menampilkan 3 dari 19 versi…"). Menggantikan baris
        /// yang dulu mencetak footer versi — teks yang sama sudah tampil di status
        /// bar di bawah halaman, jadi tidak perlu diulang dua kali.
        /// </summary>
        public string RingkasanRiwayat =>
            $"Menampilkan {Entries.Count} dari {SemuaVersi.Count} versi — yang terbaru di atas.";

        public CatatanRilisViewModel(ILogger<CatatanRilisViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SemuaVersi = RiwayatVersi();

            // Halaman dibuka dengan batch pertama saja; sisanya menunggu klik
            // "Muat versi lebih lama" agar daftar panjang tidak langsung membanjiri layar.
            foreach (var entri in SemuaVersi.Take(MaksKartu))
            {
                Entries.Add(entri);
            }

            MuatVersiLamaCommand = new RelayCommand(MuatVersiLama, () => AdaSisaVersi);

            _logger.LogInformation(
                "Catatan rilis dimuat: {Total} versi, {Tampil} kartu ditampilkan (batch {Maks})",
                SemuaVersi.Count, Entries.Count, MaksKartu);
        }

        /// <summary>Tambahkan batch versi berikutnya ke <see cref="Entries"/>.</summary>
        private void MuatVersiLama()
        {
            foreach (var entri in SemuaVersi.Skip(Entries.Count).Take(MaksKartu).ToList())
            {
                Entries.Add(entri);
            }

            OnPropertyChanged(nameof(SisaVersi));
            OnPropertyChanged(nameof(AdaSisaVersi));
            OnPropertyChanged(nameof(RingkasanRiwayat));
            ((RelayCommand)MuatVersiLamaCommand).RaiseCanExecuteChanged();
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
            var daftar = new List<CatatanRilisEntry>
            {
                new CatatanRilisEntry(
                    "Versi 2.7.1 (20 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Peningkatan — Audit Menyeluruh Seluruh Halaman Menu", new List<string>
                        {
                            "Setiap halaman yang bisa dibuka dari sidebar kini diuji dengan container aplikasi yang sebenarnya: ViewModel diambil dari DI, view pasangannya dirender, dan hasilnya diperiksa. Uji ini menangkap halaman yang tampak ada di menu tetapi gagal dibuka karena dependensi tidak terdaftar atau XAML tidak cocok dengan DataContext-nya.",
                            "Halaman placeholder sisa migrasi (TodoView) dihapus. Halaman itu tidak lagi punya menu, tidak pernah bisa dibuka, dan hanya meninggalkan jalur kode mati yang membuat daftar halaman terlihat belum selesai.",
                            "Seluruh binding tombol dan menu diperiksa terhadap properti ViewModel-nya, dan setiap handler klik dicocokkan dengan metode code-behind. Tidak ada lagi tombol yang tampil tetapi tidak melakukan apa-apa."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Memilih Tema Tidak Lagi Mengubah Judul Halaman", new List<string>
                        {
                            "Baris tema di sidebar bukan halaman, jadi kini tidak ikut dibungkus penanda halaman aktif. Sebelumnya memilih tema membuat judul jendela berubah menjadi nama tema (mis. \"Orbit\") padahal halaman yang tampil tidak berpindah, dan sorotan halaman sebelumnya ikut hilang."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Informasi Tentang Aplikasi Mengikuti Katalog Tema", new List<string>
                        {
                            "Teks Tentang Aplikasi masih menyebut \"tujuh tema\" padahal aplikasi sudah memuat sebelas tema. Jumlah dan nama tema kini dihitung dari katalog tema yang sama dengan menu Tema, jadi tidak bisa lagi ketinggalan saat tema baru ditambahkan.",
                            "Keterangan layanan digital juga diperbarui: permintaan surat daring tidak lagi hanya disebut lewat WhatsApp, tetapi juga Google Form/Sheet dan API Desa."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Petunjuk Menunjuk Menu yang Benar", new List<string>
                        {
                            "Pesan dan petunjuk di halaman SOTK Desa masih menyuruh pengguna membuka \"Pengaturan Desa\", menu yang sudah tidak ada. Semuanya kini menunjuk ke menu Pengaturan Surat yang benar, termasuk teks di layar dan dokumentasi perangkat desa."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Installer Menyalin Template dan Mengenali Aplikasi Berjalan", new List<string>
                        {
                            "Instalasi baru kini benar-benar membawa contoh template formulir. Sebelumnya skrip installer menyalin template dari folder build yang tidak pernah ada di komputer pengguna, sehingga folder Templates selalu kosong. Template yang sudah diedit desa tetap tidak pernah ditimpa saat upgrade.",
                            "Aplikasi kini membuat mutex satu-instance yang namanya sama dengan AppMutex installer. Setup bisa mengetahui aplikasi sedang berjalan sebelum menimpa berkasnya, dan membuka aplikasi dua kali tidak lagi membuat dua instance berebut database yang sama.",
                            "Metadata versi installer diisi dari versi rilis, termasuk untuk prarilis, sehingga Panel Kontrol Windows menampilkan versi yang benar."
                        }),
                        new CatatanRilisSubSection("Pembaruan — Tautan Website dan Sumber Formulir di Halaman Tentang", new List<string>
                        {
                            "Website resmi pada halaman Tentang kini menunjuk sumberjaya.freedev.app, situs pengembang aplikasi.",
                            "Kartu unduhan formulir tetap mengarah ke INSAN Karawang (infosadaradmindukkarawang.id), tetapi keterangannya kini menegaskan bahwa formulir tersebut untuk Kabupaten Karawang. Tersedia pula contoh tautan formulir daerah lain — Kabupaten Bandung, Kota Sukabumi, Kabupaten Pasaman, Kabupaten Bantul, Kabupaten Tebo, dan Kota Surakarta — serta tautan Permendagri 109/2019 sebagai formulir standar nasional.",
                            "Halaman Formulir ikut menjelaskan cakupan sumbernya, sehingga tidak ada lagi kesan bahwa tombol Unduh dari Sumber mengambil formulir untuk semua kabupaten."
                        }),
                        new CatatanRilisSubSection("Pembaruan — Pengaturan Formulir Mengikuti Kabupaten Desa", new List<string>
                        {
                            "Halaman Pengaturan Formulir kini membaca kabupaten/kota pada Pengaturan Surat → Data Desa. Kartu baru di atas daftar menampilkan desa, kabupaten, dan keadaan sumbernya: unduhan otomatis (hijau), unduh manual dari situs resmi (kuning), atau belum ada tautan resmi (netral). Perubahan data desa langsung terasa tanpa membuka ulang halaman.",
                            "Hanya Kabupaten Karawang yang bisa diunduh otomatis (INSAN Karawang). Kabupaten Bandung, Kota Sukabumi, Kabupaten Pasaman, Kabupaten Bantul, Kabupaten Tebo, dan Kota Surakarta dikenali beserta halaman resminya: tombol Buka Situs Formulir membuka halaman itu, lalu berkas PDF-nya ditambahkan lewat tombol Tambah.",
                            "Bila kabupaten terpilih belum punya tautan unduhan yang dikenal, halaman menjelaskan bahwa formulir harus ditambahkan manual lewat tombol Tambah — memakai blanko dari Dukcapil setempat — dan menyediakan tombol Atur di Pengaturan Surat bila kabupatennya belum dipilih sama sekali.",
                            "Kartu bantuan khusus kini muncul untuk keadaan itu: menyebut desa dan kabupaten yang dipilih, lalu memberi tiga langkah menambah formulir manual. Ajakan mengatur kabupaten hanya tampil bila kabupaten memang belum dipilih, tidak lagi menyesatkan desa yang kabupatennya sudah diatur tetapi belum punya tautan.",
                            "Tampilan ikut dirapikan: tiga kartu ringkasan seragam dengan halaman lain, pita warna keadaan sumber, identitas desa pada kepala halaman, judul dan jumlah pada daftar formulir, dan keadaan kosong menyebut desa/kabupaten yang sedang dipakai beserta langkah berikutnya."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Sumber Formulir Dapat Diperbarui Tanpa Rilis Baru", new List<string>
                        {
                            "Daftar kabupaten dan tautan unduhan formulir kini dibaca dari berkas sumber-formulir-daerah.json di profil pengguna (%LOCALAPPDATA%\\SuDesApp). Daftar bawaannya sendiri disimpan sebagai Bawaan\\sumber-formulir-daerah.json di folder aplikasi, bukan lagi ditanam di kode. Berkas aktif dibuat otomatis saat pertama dibuka, dan letaknya di luar folder instalasi supaya suntingan tidak tertimpa saat aplikasi diperbarui.",
                            "Berkas bawaan boleh diganti lewat tambalan pembaruan tanpa merilis ulang program; berkas aktif yang sudah disunting operator tidak ditimpa. Bila berkas bawaan hilang atau rusak, daftar darurat (Kabupaten Karawang) dipakai agar unduhan otomatis tetap berjalan.",
                            "Setelah menyunting berkas, tekan Segarkan di halaman Pengaturan Formulir — daftar langsung dibaca ulang tanpa menutup aplikasi. Lokasi berkas juga ditampilkan di kartu informasi halaman agar mudah ditemukan.",
                            "Berkas yang rusak atau kosong tidak membuat halaman gagal: daftar bawaan dipakai, alasan kegagalan dicatat di log, dan berkas operator tidak ditimpa. Entri tanpa nama/kode/URL, URL bukan http(s), serta kode ganda dilewati tanpa mengganggu entri lain."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Tombol Pratinjau pada Formulir Permohonan Rekening Koran", new List<string>
                        {
                            "Form Permohonan Rekening Koran kini punya tombol Pratinjau di samping tombol Simpan: PDF permohonan langsung disusun dan dibuka dari isian yang sedang berjalan, termasuk ketika belum semua kolom terisi — berguna untuk memeriksa bentuk surat sambil mengisi tanpa harus menyimpan dulu.",
                            "Pratinjau murni untuk dilihat: tidak dicatat ke register surat dan tidak memakai/mengurangi nomor surat. Pencatatan resmi tetap hanya lewat tombol Simpan/Cetak seperti sebelumnya, sehingga tidak ada entri register ganda atau nomor yang terbuang oleh percobaan.",
                            "Kalau penyusunan PDF gagal, pesan kesalahannya tampil jelas di dialog aplikasi tanpa menutup form, sehingga isian tidak hilang dan bisa langsung diperbaiki."
                        }),
                        new CatatanRilisSubSection("Peningkatan — SKD Umum Diberi Nama Jelas: SKD Belum Menikah (KUA)", new List<string>
                        {
                            "Jenis surat yang dulu tampil sebagai \"SKD Umum\" kini dilabeli \"SKD Belum Menikah (KUA)\" di seluruh tampilannya: menu Buat Surat, pilihan jenis pada register dan pencarian riwayat, pelabelan PDF register surat, katalog formulir permintaan WhatsApp, serta formulir Google terkait — namanya kini menyebut keperluan aslinya (surat keterangan belum menikah untuk keperluan KUA), tidak mudah disalahartikan sebagai SKD serbaguna.",
                            "Hanya nama tampilannya yang berubah; kode jenis SKD_UMUM dan data surat lama tetap sama, jadi riwayat lama, pemberkasan, dan penomoran berlanjut tanpa migrasi."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.7.0 (19 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Empat Tema Preset Baru", new List<string>
                        {
                            "Halaman Tema kini memuat sebelas tema + mode Otomatis: empat tema baru diadaptasi langsung dari preset desain populer — Orbit (ungu gelap), Ember (oranye hangat), Kanagawa (indigo gelap), dan Blossom (pink terang). Palet setiap tema dikutip dari preset aslinya, bukan dikarang ulang, sehingga nuansanya tetap setia pada preset tersebut.",
                            "Keempat tema terdaftar penuh seperti tema bawaan: muncul di menu Tema, tersimpan saat dipilih, tetap berlaku setelah aplikasi dibuka ulang, dan ikut diuji oleh uji jelajah render yang merender seluruh halaman pada kesebelas tema (± 10 ribu kendali diperiksa) untuk memastikan tidak ada bagian yang jatuh ke chrome bawaan Windows.",
                            "Berkas tema preset dihasilkan alat Tools/gen-tema-preset.py dari palet preset, jadi menambah tema adaptasi berikutnya cukup menambah satu entri palet — tidak perlu menulis ratusan baris XAML secara manual."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Pemilih Tema Visual", new List<string>
                        {
                            "Menu Tema di sidebar kini berbentuk pemilih visual dengan pratinjau warna: setiap baris tema menampilkan titik swatch warna aksen paletnya di samping nama, sehingga pengguna bisa melihat karakter tiap tema sebelum memilihnya — tidak lagi memilih dari daftar nama saja.",
                            "Tema yang sedang aktif ditandai centang di ujung kanan baris; centang berpindah seketika saat tema lain diklik, tanpa menutup dan membuka ulang menu.",
                            "Slot ikon pada baris tema digantikan titik warna, jadi tidak ada celah ikon kosong yang menggantung, dan seluruh baris tetap rata seperti item menu lain di sidebar."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Font Google Sans Ditanam", new List<string>
                        {
                            "Antarmuka kini memakai Google Sans — font yang sama dengan yang dipakai antarmuka modern. Berkas fontnya ditanam di dalam aplikasi (subset latin, lisensi SIL OFL 1.1 yang ikut disertakan) sehingga tidak bergantung pada font yang terpasang di komputer desa.",
                            "Muka Regular (400) dan Bold (700) asli ikut ditanam sebagai satu koleksi font, jadi judul dan teks tebal memakai bentuk huruf tebal yang benar — bukan tebal tiruan yang digemukkan dengan goresan.",
                            "Seluruh sebelas tema memakai keluarga huruf yang sama; bila ada karakter di luar cakupan subset latin, sistem otomatis memakai cadangan Segoe UI Variable tanpa mengubah tata letak."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Ukuran Huruf Distandarkan", new List<string>
                        {
                            "Ukuran huruf di seluruh aplikasi kini mengikuti satu skala baku yang diadaptasi dari tipografi antarmuka modern: 10 (keterangan kecil), 11 (label), 13 (teks isi kecil), 14 (teks isi dan kontrol), 15 (judul kecil), 20 (judul), 24 (angka besar), dan 28 (tampilan). Ukuran pecahan warisan lama seperti 11,5 / 12,5 / 13,5 dan ukuran acak 16–18 tidak dipakai lagi — 492 literal diseragamkan dalam satu putaran.",
                            "Skala ini dijaga uji otomatis: menulis ukuran di luar skala di XAML maupun kode akan langsung menggagalkan build, sehingga ukuran baru tidak bisa menyelinap lagi sedikit demi sedikit."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Dialog Progres Ceklis di Halaman IDM", new List<string>
                        {
                            "Saat data IDM diambil dari situs pusat Kemendesa, halaman tidak lagi hanya menampilkan tulisan \"mohon tunggu beberapa detik\". Kini muncul dialog progres bergaya splash screen: strip progres di puncak kartu dan deretan langkah yang diisi bertahap — pencocokan wilayah, kontak situs pusat, unduhan tiap tahun, lalu penyusunan tampilan.",
                            "Langkah yang sedang berjalan berdenyut; begitu langkah berikutnya dimulai, langkah sebelumnya otomatis tercentang. Progres yang jelas membuat proses yang butuh beberapa detik terasa terkendali, dan bila gagal pengguna langsung melihat pesan galat yang sebenarnya.",
                            "Dialog menutup seluruh halaman dengan tirai modal sehingga tombol di belakangnya tidak bisa diklik saat data sedang diambil — mencegah dua permintaan berjalan bersamaan — dan seluruh warnanya mengikuti tema yang aktif."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Nama Tema dan Catatan Rilis Tanpa Nama Aplikasi Lain", new List<string>
                        {
                            "Nama tema di menu Tema kini tampil polos — Orbit, Ember, Kanagawa, dan Blossom — tanpa embel-embel nama aplikasi lain di belakangnya. Pengguna desa tidak perlu lagi melihat merek produk lain saat memilih tampilan aplikasinya sendiri.",
                            "Seluruh catatan rilis versi ini ditulis ulang dengan kata-kata netral: empat tema baru disebut sebagai adaptasi preset desain populer, pemilih tema disebut pemilih tema visual, dan skala ukuran huruf disebut diadaptasi dari tipografi antarmuka modern. Tidak ada satu pun nama merek aplikasi lain yang tersisa di teks aplikasi.",
                            "Aturan ini dijaga uji otomatis yang memindai seluruh berkas sumber aplikasi (XAML, C#, dan proyek) serta nama tema yang tampil — menyebut nama merek itu lagi akan langsung menggagalkan build."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Pilihan Tahun Anggaran APBDes Otomatis", new List<string>
                        {
                            "Daftar tahun di halaman APBDES kini selalu memuat tahun berjalan menurut jam komputer dan menandainya sebagai tahun berjalan, ditambah tahun sebelum dan sesudahnya serta seluruh tahun yang sudah punya dokumen. Dulu daftar itu hanya berisi angka tanpa penanda, sehingga pengguna harus menebak tahun mana yang sedang aktif.",
                            "Daftar disusun ulang setiap arsip dibaca — saat halaman dibuka, saat Muat Ulang ditekan, dan setelah impor yang mengubah tahun dokumen — jadi aplikasi yang dibiarkan terbuka melewati pergantian tahun tetap mendapat tahun baru. Tahun yang sedang dibuka juga tidak pernah hilang dari daftar, sehingga kotak pilihan tidak tampil kosong."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Badge TERBARU Ganda dan Riwayat Rilis Lengkap", new List<string>
                        {
                            "Badge \"TERBARU\" sebelumnya bisa muncul dua kali (versi 2.6.0 dan 2.5.9 sama-sama menandai dirinya terbaru) karena penandanya ditulis manual per entri. Kini penanda itu diturunkan dari posisi: hanya kartu paling atas yang mendapat badge, sehingga tidak mungkin lagi muncul ganda.",
                            "Halaman tidak lagi mencetak teks versi dua kali (baris atas dan status bar); baris atas kini merangkum berapa versi yang sedang tampil dari seluruh riwayat.",
                            "Riwayat lengkap bisa dibaca dengan tombol \"Muat versi lebih lama\" yang menambah tiga kartu per klik — halaman tetap ringkas saat dibuka, tetapi tidak ada lagi versi lama yang tidak bisa dijangkau."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Catatan Rilis Rapi dan Tanpa Teks Terpotong", new List<string>
                        {
                            "Judul dan subjudul di kepala halaman tidak lagi tertimpa chip ikon. Sebelumnya judul menempati sisa ruang beberapa piksel saja karena chip ikon menyerobot hampir seluruh lebar kepala halaman, sehingga huruf pertama judul terdorong ke belakang ikon.",
                            "Lencana jenis bagian (FITUR BARU, PENINGKATAN, PERBAIKAN) tidak lagi terpotong. Sebelumnya lencana itu ditekan oleh judul bagian sampai hurufnya terpangkas; kini judul yang membungkus, dan lencana turun ke baris berikutnya bila jendela terlalu sempit untuk memuat keduanya berdampingan.",
                            "Lencana TERBARU pada kartu versi teratas diberi lebar minimum, jadi tulisan di dalamnya tidak pernah terpotong walau nama versi panjang.",
                            "Jarak antarbaris butir diratakan dan tinggi barisnya dipatok konsisten, sehingga teks berbaris dua atau lebih tetap terbaca penuh — termasuk huruf berdiakritik — di semua tema dan ukuran jendela.",
                            "Ringkasan riwayat, kartu versi, dan bilah status dirapatkan jarak tepinya; halaman tetap utuh mulai jendela selebar 640 px sampai layar lebar tanpa ada tulisan yang meluber keluar kartu — dijaga uji tata letak yang merender halaman pada tiga lebar jendela lalu mengukur setiap teks dengan ukuran huruf sebenarnya."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Roda Mouse Menggulir Halaman di Atas Tabel dan Daftar", new List<string>
                        {
                            "Roda mouse tidak lagi terasa mati saat kursor berada di atas tabel, daftar, atau grid: begitu penampil dalam mencapai ujungnya — atau memang tidak bisa digulir karena scrollbar-nya dimatikan — guliran otomatis diteruskan ke penampil berikutnya di atasnya, sampai halaman ikut bergulir.",
                            "Sebelumnya WPF menandai kejadian roda \"sudah ditangani\" di penampil paling dalam, sehingga halaman di luarnya tidak pernah ikut bergulir walau roda terus diputar. Kini satu penangan roda bertingkat dipasang sekali saat aplikasi mulai dan berlaku di seluruh halaman, jendela pratinjau, maupun dialog."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Tata Letak Halaman APBDes Tanpa Teks Terpotong", new List<string>
                        {
                            "Kepala halaman APBDES memakai kolom bintang, bukan deretan mendatar: sebelumnya judul dan subjudulnya meluber keluar kartu pada jendela sempit (sampai 904 px pada halaman selebar 640 px).",
                            "Deretan tombol tahun anggaran, tambah dokumen, impor, simpan, dan muat ulang kini membungkus ke baris berikutnya bila ruang tidak cukup. Ringkasan angka pindah ke barisnya sendiri dengan lebar penuh, jadi tidak lagi terhimpit sampai tinggal beberapa piksel.",
                            "Identitas dokumen, berkas sumber, dan rincian anggaran ditumpuk dalam satu kolom bergulir; sebelumnya dua kartu berdampingan menyisakan ruang terlalu sempit untuk isian di jendela minimum. Label isian diletakkan di atas kotaknya dan tombol berkas membungkus.",
                            "Uraian dan keterangan pada tabel rincian membungkus ke baris berikutnya, dan tabel bisa digulir mendatar tanpa memotong judul kolom. Judul dokumen panjang di daftar kiri juga membungkus, bukan terpotong di tepi kartu. Tahun kosong dan dokumen tanpa rincian kini punya keterangan, bukan ruang kosong.",
                            "Seluruh isian tanggal di halaman ini mengikuti bahasa Indonesia (10/07/2026, bukan placeholder Inggris). Tata letaknya dijaga uji yang merender halaman pada empat lebar jendela dan mengukur setiap teks dengan ukuran huruf sebenarnya."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.6.0 (14 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Peningkatan — Logo Kabupaten Otomatis untuk Jawa Barat", new List<string>
                        {
                            "Lambang seluruh 27 kabupaten/kota Jawa Barat kini tersedia lengkap dan otomatis: begitu pengguna memilih kabupaten di Pengaturan Surat, lambangnya terpasang sendiri tanpa unggah manual. Dua logo yang sebelumnya gagal terunduh (Kabupaten Tasikmalaya dan Kota Cimahi) kini berhasil diperoleh dari Wikimedia Commons dengan kata kunci alternatif.",
                            "Kode Kemendagri kabupaten kini disimpan permanen di database (kolom KodeKabupaten pada tabel InfoDesa). Sebelumnya hanya nama kabupaten yang tersimpan, sehingga saat aplikasi di-restart, pemulihan logo mengandalkan pencocokan nama yang ambigu — mis. \"Bandung\" bisa merujuk Kabupaten Bandung (32.04) atau Kota Bandung (32.73). Kini kode yang unik menjamin lambang yang tepat selalu dipilih.",
                            "Saat halaman Pengaturan Surat dibuka, bila nama kabupaten ambigu (ada kabupaten dan kota dengan nama sama), sistem otomatis menyelesaikan ambiguitas dengan kode Kemendagri yang tersimpan. Tidak lagi ada risiko logo salah kabupaten setelah restart."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Ukuran Font Menu dan Navigasi Diseragamkan", new List<string>
                        {
                            "Seluruh elemen menu dan navigasi kini memakai ukuran font yang sama (14pt), sebelumnya bervariasi antara 12, 13, 14, dan 15 untuk elemen yang sejenis. Item menu sidebar, child button, tab navigasi, menu konteks (klik kanan), dan navigasi kiri (Tentang, Panduan WhatsApp, Pencadangan) kini semua konsisten.",
                            "Token tunggal UkuranTeksMenu ditambahkan di ThemeStyles.xaml sebagai satu-satunya sumber ukuran font untuk elemen menu. Perubahan ukuran di masa depan cukup mengubah satu angka di satu tempat, tanpa menyentuh setiap file satu per satu.",
                            "MenuItem yang sebelumnya tidak punya FontSize eksplisit (mengandalkan inheritance dari Window yang rapuh) kini memilikinya secara eksplisit. Ikon sidebar mode ciut juga diseragamkan dari 15 ke 14 supaya tidak berubah ukuran saat sidebar dilipat/dilebarkan."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Akun Administrator Tidak Muncul di Kelola Pengguna", new List<string>
                        {
                            "Akun Administrator default (admin/admin) kini otomatis disisipkan ke tabel Pengguna saat pemasangan baru, sehingga langsung muncul di halaman Kelola Pengguna dan bisa dikelola (ganti nama, peran, kata sandi). Sebelumnya tabel Pengguna mulai kosong dan akun admin hanya ada di file Login.dat yang terpisah — akun Administrator tidak pernah tampil di daftar pengguna.",
                            "Layar login kini memakai PenggunaService.MasukAsync (PBKDF2, kunci otomatis setelah 3× gagal, peran dari database) sebagai jalur utama. Sebelumnya login memverifikasi file Login.dat dengan SHA256 yang lebih lemah dan tidak pernah menyentuh tabel Pengguna.",
                            "Instalasi lama yang masih memakai Login.dat dimigrasi otomatis: saat login pertama berhasil via Login.dat, akun dipindahkan ke tabel Pengguna dan kata sandi selanjutnya diverifikasi melalui database. Peran dan nama tampilan dari database langsung diset ke sesi, sehingga penegakan izin (bila diaktifkan) langsung memakai peran yang benar."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.9 (13 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Langganan Aplikasi (Bulanan & Tahunan)", new List<string>
                        {
                            "SuDesApp kini berlangganan, dengan menu LANGGANAN yang berdiri sendiri di navigasi utama — berdiri tepat setelah Beranda dan Login dengan Google, bukan lagi salah satu bagian di dalam Pengaturan Aplikasi. Status masa aktif dan pembayaran adalah hal yang paling sering dicari pengguna, sementara Pengaturan Aplikasi berisi setelan teknis yang jarang disentuh.",
                            "Tiga pilihan paket: Bulanan Rp100.000 (31 hari), 6 Bulan Rp550.000 (183 hari), dan Tahunan Rp1.000.000 (365 hari). Dihitung per hari, bulanan = Rp3.226, 6 bulan = Rp3.005, dan tahunan = Rp2.740 — makin lama komitmennya, makin hemat per hari, jadi membayar di muka memang lebih murah daripada membayar tiap bulan.",
                            "Pembayaran QRIS dan Virtual Account bank (BCA, BNI, BRI, dan Permata) lewat Midtrans Snap. Aplikasi membuat tagihan lalu membuka halaman pembayaran di peramban; begitu dana masuk, masa aktif bertambah otomatis tanpa pengguna perlu mengirim bukti transfer ke siapa pun.",
                            "Halaman web https://<server>/checkout untuk membayar dari telepon atau komputer lain tanpa membuka aplikasi: pilih paket, isi kode aktivasi, buat tagihan. Halaman itu ikut disajikan oleh server lisensi yang sama, lengkap dengan berkas Docker dan panduan langkah demi langkah di docs/deploy-server.md.",
                            "Pembayaran terbaru dan masa aktif yang tersisa selalu terbaca dari server, jadi angka yang dilihat pengguna persis sama dengan yang benar-benar ditagih — harga tidak bisa berbeda antara tampilan dan tagihan karena keduanya berasal dari satu sumber di server.",
                            "Data desa TIDAK ikut diunggah: database desa.db tetap terenkripsi di komputer masing-masing. Yang diurus server hanya masa aktif aplikasinya."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Lisensi Bertanda Tangan (verifikasi offline)", new List<string>
                        {
                            "Masa aktif lisensi dibuktikan dengan token bertanda tangan ECDSA P-256 yang hanya boleh diterbitkan server. Aplikasi memverifikasi tanda tangannya secara lokal, sehingga tetap bisa diverifikasi penuh saat internet mati — tanpa perlu menyimpan rahasia apa pun di komputer desa.",
                            "Masa tenggang 7 hari setelah jatuh tempo: internet putus tidak langsung memblokir kerja desa. Karena waktu konfirmasi terakhir ditulis DI DALAM token yang ditandatangani, memperpanjang tenggang dengan menyunting berkas cache lokal tidak menolong — edit apa pun membuat verifikasi gagal dan status menjadi \"tidak aktif\", bukan status palsu.",
                            "Cek status ke server dilakukan sekali saat aplikasi dibuka lalu berkala tiap 6 jam di latar belakang, tidak menahan tampilan. Kegagalan jaringan wajar dan tidak pernah ditampilkan sebagai galat.",
                            "Satu langganan dibatasi jumlah perangkatnya (bawaan 2), sehingga satu kode aktivasi tidak dipakai di banyak PC sekaligus. Komputer yang sudah terdaftar selalu bisa memeriksa ulang statusnya, dan percobaan dari komputer di luar batas tidak mengurangi kuota yang tersisa."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Mode Baca-Saja Saat Langganan Berakhir", new List<string>
                        {
                            "Bila langganan benar-benar berakhir (lewat jatuh tempo DAN masa tenggang 7 hari), aplikasi menjadi baca-saja: data tetap bisa dibaca, dicari, diekspor, dicadangkan/dipulihkan, dan surat lama bisa diedit — tetapi membuat surat baru dan mencetak ditolak, dengan pesan singkat yang mengarahkan ke menu Langganan.",
                            "Pengguna yang baru membayar tidak menunggu detak berikutnya: halaman Langganan menerapkan status penuh seketika setelah Cek Status.",
                            "Batasannya juga berlaku pada lapis dalam (penyimpanan surat), bukan hanya tombol di tampilan, sehingga jalur penyimpanan non-UI seperti paket NTCR dan template juga ikut terkunci — kunci yang hanya disembunyikan di layar bukan kunci.",
                            "Chip keadaan langganan muncul di status bar — \"Full Service • sisa N hari\" saat penuh (angka sisa hari ikut terbaca langsung di chip, bukan hanya lewat tooltip), \"Baca-saja\" saat terkunci — dan bisa diklik untuk langsung membuka halaman Langganan.",
                            "Instalasi yang belum pernah berlangganan TIDAK ikut dikunci — \"belum berlangganan\" berbeda dari \"berakhir\", supaya pengguna lama tidak mendadak kehilangan akses."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Langganan, Tentang Aplikasi, dan Kepemilikan Scope", new List<string>
                        {
                            "Menu Langganan kini benar-benar bisa disambungkan: alamat server lisensi bisa diisi dan disimpan langsung dari halaman Langganan (kartu Server Lisensi), tanpa menyunting berkas konfigurasi. Selama alamat masih localhost, halaman menampilkan peringatan bahwa server belum terhubung — alih-alih gagal diam-diam.",
                            "Halaman Langganan dirapikan ulang: kotak status dengan ukuran teks lebih menonjol, pilihan paket berupa dua kartu yang dipilih berdampingan (bukan ComboBox), tombol Bayar menempel di kartu paket, dan tombol Cek Status tetap bersama statusnya.",
                            "Warna kotak status langganan kini berubah seketika mengikuti hasil pemeriksaan — sebelumnya perubahan nada bisa tertinggal karena perubahannya tidak memberi tahu tampilan.",
                            "Halaman Tentang Aplikasi diseragamkan jadi satu identitas: baris Copyright memakai Sumberjaya Dev. seperti penerbit installer dan baris merek di status bar (sebelumnya INOMIX Studio hanya muncul di satu tempat), dua properti surel yang isinya kembar disatukan jadi satu sumber, dan alamat repositori disesuaikan dengan nama aslinya (inomixstudio/SuDesApp).",
                            "Kepemilikan scope halaman pindah ke NavigationService: tidak ada lagi slot global yang bisa menutup halaman di luar jalur navigasi, scope halaman lama baru ditutup saat penggantinya benar-benar ditampilkan, dan banner Login dengan Google tak lagi membuat scope yang tidak pernah ditutup. Seluruh panggilan CreateScope tanpa using di aplikasi kini tinggal milik NavigationService, dan aturannya dijaga 7 unit test baru.",
                            "Kode mati NavigateToView dibuang; pembukaan halaman Login Google dari banner kini memakai jalur yang sama dengan menu (ShowGoogleLoginPublicAsync), lengkap dengan dialog galat, bukan catch diam."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.8 (10 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Surat Keterangan Belum Terbit / Sudah Terbit", new List<string>
                        {
                            "Jenis surat baru di menu Buat Surat Baru, tepat setelah SKTM: Belum Terbit. Surat ini menyatakan kondisi ADMINISTRATIF (\"belum terbit\"/\"sudah terbit\") alih-alih kondisi ekonomi (\"tidak mampu\") — bentuk yang kini diminta pengembangan (rekap KUR, beasiswa, bantuan) sebagai pengganti SKTM.",
                            "Isian, tata letak, validasi, pemilihan penandatangan, dan nomor surat memakai jalur yang sama dengan SKTM, sehingga operator tidak perlu belajar form baru. Varian dipilih lewat satu kotak pilihan (Belum Terbit / Sudah Terbit) yang juga menentukan judul surat dan menyusun ulang baris keterangan bawaan.",
                            "Nomor surat tetap mengikuti format register desa yang sama (470/{nomor}/Ds/{tahun}) dan register/arsip menamainya \"Surat Ket. Belum Terbit / Sudah Terbit\", bukan kode mentah."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Tata Letak Surat Sesuai Naskah Dinas", new List<string>
                        {
                            "Blok tanda tangan kini memuat baris tanda tangan di atas nama penandatangan. Naskah dinas Perbup 74/2020 lampiran mensyaratkan susunan tempat/tanggal → jabatan → ruang tanda tangan → \"tanda tangan\" → NAMA. Dua baris terakhir itu tidak pernah dicetak, sehingga perangkat desa harus menuliskan nama penandatangan dengan tangan di setiap lembar surat. Perbaikan ini berlaku untuk SELURUH surat karena sampai pada satu penampung bersama (termasuk setiap Template Surat buatan sendiri).",
                            "Baris \"Surat keterangan ini dipergunakan untuk :\" kini dicetak. Isian keperluan SUDAH dikumpulkan setiap form Buat Surat Baru tetapi belum pernah satu pun generator mencetaknya — perangkat desa menuliskannya tangan di setiap lembar, padahal baris inilah yang membuat surat bisa diterima bank, Dukcapil, atau KUA. Surat yang sudah mencetak keperluannya sendiri (Pengantar SKCK, Ahli Waris, Garapan Sawah) tetap memakai bentuknya masing-masing supaya tidak tercetak dua kali.",
                            "Register dan arsip tidak lagi menampilkan nama jenis surat mentah: KENAL_LAHIR, AHLI_WARIS, dan IJIN_TINGGAL hanya dikembalikan sama huruf besar seperti \"KENAL LAHIR\" karena masuk cabang bawaan. Sekarang bertuliskan \"Ket. Kenal Lahir\", \"Surat Ket. Ahli Waris\", dan \"Ket. Tinggal Sementara\" seperti yang tertera di menu.",
                            "Pratinjau PDF: label 'Halaman 1 dari 1' di bawah setiap halaman dihapus. Informasi halaman sudah terbaca dari jumlah kartu halaman, sedangkan label itu memakan ruang dan selalu berbunyi 'Halaman 1 dari 1' untuk surat satu halaman — seolah-olah pesan, bukan keterangan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.7 (10 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Perbaikan — Data Warga dan Laporan Penduduk (angka resmi tidak boleh salah)", new List<string>
                        {
                            "Impor database tidak lagi membuang tujuh kolom warga. Migrasi tabel lama menyusun ulang tabel Warga tanpa NoKK, NamaAyah, NamaIbu, AlamatDetail, TanggalStatus, KeteranganWarga, dan StatusKeluarga, lalu menggunakannya sebagai ganti — nomor KK, nama orang tua, riwayat mutasi, dan kedudukan dalam keluarga hilang permanen. Kini ketujuh kolom itu ikut dipindahkan.",
                            "Tambah Warga dengan NIK yang sudah terdaftar tidak lagi menimpa warga orang lain. Dulu baris Budi yang NIK-nya dicocokkan langsung DIPERBARUI (nama, tanggal lahir, agama, alamat, RT semua berganti) sementara aplikasi melaporkan \"berhasil ditambahkan\" dan jumlah warga tidak berubah — satu warga hilang tanpa bisa dipulihkan dari aplikasi. Sekarang simpan ditolak dengan nama warga pemilik NIK itu disebut.",
                            "Tanggal lahir yang diketik mengikuti petunjuk form tidak lagi tertukar bulan dan tanggal. Pemindai menerima pola MM/dd lebih dulu sehingga \"05-06-1990\" (maksudnya 5 Juni) tersimpan 6 Mei — umur di daftar salah satu tahun dan tanggal lahir salah selamanya di setiap surat dan ekspor.",
                            "Angka Kepala Keluarga, KK laki-laki, dan KK wanita tidak lagi selalu 0. Kolom kedudukan dalam keluarga (StatusKeluarga) tidak pernah ikut dibaca saat statistik dihitung, sehingga kartu Data Warga, halaman Laporan, dan tabel di PDF resmi semuanya mencetak 0 — baris totalnya bahkan berbunyi \"Jumlah Kepala Keluarga 0 — 100,0 %\". Sekarang dihitung dari kedudukan, dan bila operator belum mengisinya, pemilik tunggal satu nomor KK dihitung sebagai Kepala Keluarga.",
                            "Angka rekap tidak lagi basi sampai satu jam. Cache statistik (yang bisa hidup 60 menit) hanya dibatalkan pada satu jalur penulisan, jadi setelah menambah/mengubah/menghapus warga kartu dan laporan masih menampilkan angka lama — warga ke-11 belum terhitung dan warga yang sudah dihapus masih terhitung. Pembatalan kini dilakukan di seluruh jalur tulis.",
                            "Laporan PDF dan Excel tidak lagi saling berselisih. Kotak angka pada PDF memakai jumlah warga SELURUH terdata (termasuk pindah & meninggal) untuk pembagian laki-laki/perempuan, sementara layar dan Excel memakai yang masih tinggal — satu laporan punya dua pembagian jenis kelamin bergantung dokumennya dicetak atau dibuka di Excel.",
                            "Mutasi dari dropdown per baris tidak lagi menghapus catatannya. Status dan tanggal berubah benar, tetapi catatan sebab pindah/meninggal milik warga selalu ditimpa string kosong — catatan itu satu-satunya rekaman alasan mutasi.",
                            "Warga tanpa RT tidak lagi hilang dari tabel tetapi tetap ikut dihitung di totalnya. Dulu tabel RT menampilkan 8 baris dengan baris total \"Jumlah 10 — 100,0 %\" dan tanpa penjelasan ke mana dua orang itu pergi; kini mereka tampil pada baris \"(tidak diisi)\", sama seperti kantong usia.",
                            "Tanggal lahir di masa depan (salah ketik 2027 untuk 1927) tidak lagi dihitung sebagai balita. Orang berusia 99 tahun masuk kantong 0-4 dan membengkakan jumlahnya; sekarang diperlakukan sebagai \"(tidak diketahui)\" seperti tanggal yang tidak terbaca.",
                            "Keterangan tabel kelompok usia tidak lagi bertentangan dengan isinya: pilihannya berbunyi \"sampai 75 tahun ke atas\" sementara kantong terakhir laporan adalah 65+.",
                            "Tabel bertajuk \"Penduduk Menurut …\" tidak lagi menyesatkan. Tabel agama, pendidikan, status perkawinan, kelompok usia, dan RT dihitung dari seluruh warga terdata sementara angka besar \"Penduduk\" hanya yang masih tinggal, sehingga jumlah baris (10 — 100,0 %) berbeda dari judul halaman (8). Judulnya kini \"Warga Menurut …\" dengan catatan cakupan.",
                            "Berbeda ekstensi nama berkas tidak lagi menghasilkan berkas salah kapak: memilih Excel lalu mengetik nama .pdf menghasilkan berkas bernama .pdf yang isinya xlsx. Isi kini mengikuti ekstensi yang benar-benar diketik.",
                            "Form Tambah Warga tidak lagi membuka kotak \"Status Keluarga\" kosong: nilai bawaan \"Kepala Keluarga\" ditetapkan lalu langsung ditimpa string kosong beberapa baris di bawahnya.",
                            "Kartu rekap tidak lagi terpotong pada jendela minimum. Sembilan kartu berbagi ±99 px sehingga keterangan seperti \"0 laki-laki, 0 wanita\" menjadi \"0 laki-l…\", dan angka \"1.234 / 56\" meluber keluar kartu. Keterangan kini boleh membungkus dan kartu punya lebar minimum."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Perangkat Desa dan Surat Keputusan", new List<string>
                        {
                            "SK pemberhentian tidak lagi terbit tanpa alasan. Jalur simpan form membuat SK secara otomatis tetapi tidak mengisi alasan sama sekali, sehingga setiap SK pemberhentian tercetak \"… dari jabatan KADUS di Desa XYZ, terhitung sejak tanggal …\" — tanpa menyebut apa pun yang melatarbelakanginya. Sekarang alasannya mengikuti catatan baris, dan bila kosong memakai \"karena telah berakhir masa jabatannya\". Pratinjau SK dari panel juga wajib menyebut alasannya.",
                            "Orang dengan jabatan warisan tidak lagi hilang dari halaman Perangkat Desa. Tabel mencari memakai nama jabatan resmi sementara kumpulan baris dikunci bentuk tampilannya; nilai seperti \"KEPALA DUSUN\" tidak punya padanan sehingga orang itu tidak muncul di tabel, kartu angka, ekspor Excel, maupun cetak PDF — padahal barisnya ada di database. Baris sisa kini tetap ditampilkan.",
                            "Panel SK tidak lagi menampilkan tulisan mentah \"{KABUPATEN}\" dan \"{DESA}\". Daftar dasar hukum ditampilkan tanpa melewati pengganti placeholder, sehingga operator melihatnya sebagai kerusakan tampilan. Kini tampil sebagai \"[nama kabupaten]\" dan \"[nama desa]\", sementara dokumen yang dicetak tetap memakai nama asli dari Pengaturan Desa.",
                            "Aturan \"satu jabatan pada satu wilayah hanya boleh satu orang\" tidak lagi bocor pada data lama. Pemeriksaannya membandingkan teks jabatan dan wilayah apa adanya, sehingga baris warisan bertulis \"KADUS \" atau RT/RW \"1\" (bukan \"01\") tidak pernah cocok dan dua Kepala Dusun bisa terbit untuk satu dusun. Perbandingan kini melalui bentuk ternormalisasi.",
                            "Pindah pilihan di panel SK tidak lagi membuang isian operator. Memilih jabatan yang sama — atau ComboBox memilih ulang item yang sama — mengosongkan kotak Alasan yang sudah diketik dan memaksa jenis SK kembali ke Pengangkatan, sehingga pilihan \"Penetapan\" (KPM) terbalik sendiri tanpa operator menyentuhnya. Isian kini hanya disiapkan ulang saat jabatannya benar-benar berganti.",
                            "Diktum SK bersama KPM diakhiri tanda titik, bukan titik koma, sehingga tetap seragam dengan diktum lain."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.6 (10 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Perbaikan — Pesan WhatsApp Warga Masuk dan Sampai ke Permintaan", new List<string>
                        {
                            "Webhook WhatsApp Cloud API sekarang benar-benar menerima permintaan dari Meta. Sebelumnya alamat jalur diuji dengan EndsWith(\"/wa-webhook\") padahal HTTP.sys mendaftarkannya dengan garis miring di akhir, sehingga SETIAP permintaan verifikasi maupun pesan masuk dibalas 404 — verifikasi langganan Meta gagal dan pesan warga tidak pernah sampai ke antrean operator.",
                            "Keperluan yang diketik warga tidak lagi dibuang. Label \"Keperluan\" dinormalkan menjadi kunci \"keterangan\" oleh pembaca format WA, sehingga nilainya dicari di kunci yang salah dan surat selalu terbit dengan teks bawaan (\"Persyaratan\") ketimbang yang diminta warga. Perbaikan sama diterapkan pada jalur Google Sheet.",
                            "Permintaan warga yang gagal diproses tidak lagi hilang diam-diam. Penanda \"sudah diterima\" dicatat sebelum pemrosesan; bila pemrosesan gagal (database terkunci, Google API mati), penanda sekarang dilepas agar percobaan ulang Meta dengan ID pesan yang sama tidak dibuang.",
                            "Kode permintaan (PMT-…-0001) tidak lagi bisa kembar. Kode berikutnya dihitung dengan nomor maksimum + 1 tanpa penguncian, sementara percakapan WhatsApp dan poller Google Sheet berjalan di thread berbeda — dua warga bisa saja mendapat kode yang sama, operator menyetujui surat warga yang salah. Alokasi kini berurutan di dalam gerbang, index UNIQUE ditambahkan pada kolom kode, dan reservasi dilepas setelah baris tersimpan.",
                            "Waktu tunggu layanan WhatsApp pada konfigurasi 24 jam sekarang dihitung dari tengah malam hari layanan, bukan dari jam buka yang dikonfigurasi — warga di hari Sabtu dulu diberi tahu \"±16 jam lagi\" padahal sarannya ±48 jam."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Data Surat dan Berkas Word", new List<string>
                        {
                            "Satu paragraf .docx yang rusak tidak lagi membuang seluruh isi surat. try/catch sebelumnya melingkupi seluruh loop pembacaan, sehingga paragraf pertama yang bermasalah memotong semua hal setelahnya (isi, tabel, tanda tangan, nomor) — dan berkas impor tetap dipakai untuk dicetak tanpa peringatan apa pun. Sekarang hanya paragraf bermasalah yang dilewati.",
                            "Tanggal pada hasil impor Word tidak lagi bergeser bulan/tahun di komputer dengan format regional luar Indonesia. Pembacaan dan penulisan tanggal kini memakai budaya invarian, sehingga \"05-06-2026\" tetap berarti 5 Juni 2026 dan tahunnya tetap Masehi di semua lokasi Windows.",
                            "Data desa tidak lagi dipotong diam-diam pada 255 karakter. Alamat panjang yang ditulis clerk biasanya terpotong di tengah kata, lalu tampil tidak lengkap pada kop setiap surat — padahal simpan terlihat sukses. Isian kini disimpan utuh dan dicatat pada log.",
                            "Baris surat yang gagal dipetakan tidak lagi hilang dari register tanpa jejak. Kegagalan pemetaan sekarang ditulis ke log beserta ID barisnya, sehingga clerk yang mencari surat \"tidak ketemu\" bisa menelusurinya.",
                            "Nama gateway WhatsApp yang dipilih kini benar-benar tersimpan. Penyetelannya selalu menulis \"cloudapi\" dan mengabaikan argumennya.",
                            "Status surat tidak lagi bisa tersimpan kosong saat menyunting surat berstatus non-Draft, yang membuat surat menghilang dari filter Aktif/Draft dan ditolak alur verifikasi."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Tampilan Antarmuka", new List<string>
                        {
                            "Panah urut (▲/▼) sekarang muncul untuk kolom Tanggal, Nama, dan Jenis Surat di Register Surat dan Register NTCR. Nama kolom database berbeda dari nama properti tampilan, sehingga perbandingan panah tidak pernah cocok untuk tiga dari tujuh kolom — pengguna mengurutkan tanpa ada penanda kolom mana yang aktif.",
                            "Tombol \"🖨️ Cetak Register (PDF)\" tidak lagi terdorong keluar jendela pada lebar minimum (900 px). Bilah alat Register Surat memakai WrapPanel seperti Register NTCR, sehingga tombol membungkus ke baris berikutnya alih-alih meluber.",
                            "Bilah aksi Agenda Surat dan Arsip Keputusan juga tidak lagi memotong tombol: tujuh tombol aksi plus kotak cari melebihi lebar konten, sehingga \"Cetak\" dan \"Batal\" jatuh di luar jendela dan tidak bisa diklik.",
                            "Kolom tabel register tidak lagi menyusut menjadi pita tipis. Kolom lebar-bintang (Alamat, Keperluan, Tujuan, Kepala) kini punya lebar minimum sehingga teks tetap terbungkus wajar; bila tabel memang lebih lebar dari jendela, batang gulir mendatar tersedia alih-alih teks terpotong.",
                            "Kepala kolom tabel register ikut membungkus saat panjang (mis. \"Tujuan (KUA / Numpang Nikah)\") alih-alih terpotong di tengah kata.",
                            "Tombol halaman \"◀ Sebelumnya\" dan \"Berikutnya ▶\" tidak lagi terpotong: satuannya MinWidth, bukan Width — teksnya tidak muat pada skala teks maupun DPI yang lebih besar.",
                            "Kartu \"Unduh Formulir Resmi\" pada halaman Tentang mengikuti tema. Latarnya dipatok putih sementara teksnya berwarna terang di tema gelap/slate, sehingga kartu tampil menyilaukan dan tulisannya nyaris tak terbaca.",
                            "Halaman Laporan tidak lagi punya \"jebakan gulir\": setiap pratinjau tabel punya ScrollViewer tinggi 260 px yang menahan roda tetikus. Sekarang seluruh kolom pratinjau mengikuti satu batang gulir luar.",
                            "Halaman Kelola Pengguna: kolom form akun punya batas bawah dan atas lebar sehingga daftar akun tetap terbaca saat jendela sempit.",
                            "Pratinjau PDF tidak lagi menyisakan tombol cetak/ekspor mati setelah satu dokumen gagal dimuat. Keadaan \"dokumen tidak ditemukan\" sekarang dikembalikan saat dokumen berikutnya berhasil dibuka."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Kelancaran Aplikasi", new List<string>
                        {
                            "Galat pada perintah (mis. \"Muat\", \"Ekspor\", \"Simpan\") tidak lagi memunculkan dialog \"Aplikasi mengalami gangguan\" seolah aplikasi rusak. Kegagalan sementara yang seharusnya ditangani sendiri — database sedang terkunci, berkas sedang dibuka di aplikasi lain, jaringan putus — sekarang dilaporkan sebagai perintah yang tidak selesai, sementara pekerjaan yang sudah tersimpan tetap aman.",
                            "Notifikasi tidak lagi membangkitkan galat lintas thread. Pembersihan kabar basi menelusuri daftar yang terikat ke UI dari thread latar sehingga daftar berubah saat ditelusuri; pencarian kini dijalankan di thread UI, dan status notifikasi tidak lagi bisa keliru.",
                            "Pembaruan kecil tidak lagi memposisikan cache PINNING salah: pembatalan cache berdasarkan awalan sempat cocok pada segmen tipe yang salah, sehingga data kedua (penomoran surat lonjakan, isian desa) bisa kosong setelah Setting berubah. Kunci kini dicocokkan tepat.",
                            "Penyimpanan Setelan Desa dan pemeriksaan tabel InfoDesa tidak lagi membocorkan handle statement SQLite; koneksi yang sama dipakai berulang kali selama aplikasi berjalan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.5 (4 Oktober 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Pembaruan — Platform .NET 10 LTS", new List<string>
                        {
                            "Aplikasi kini dibangun di atas .NET 10 LTS (2 Oktober 2026): TargetFramework seluruh proyek ke net10.0, versi SDK dikunci lewat global.json ke 10.0.401 sehingga build developer dan CI memakai SDK yang sama, alur rilis GitHub Actions ikut dipin, dan README beserta panduan rilis diperbarui. Paket rilis tetap self-contained, jadi komputer kantor desa tidak perlu memasang runtime terpisah untuk menjalankannya.",
                            "Microsoft.Data.Sqlite diperbarui dari 9.0.20 ke 10.0.12 dan paket Google API (Drive, Sheets, Forms) diaudit pada versi aman yang selaras satu sama lain. Enkripsi SQLCipher tetap terjaga: seluruh SQLitePCLRaw sengaja bertahan di lini 2.1.x dan kombinasinya diuji langsung (PRAGMA cipher_version terisi, tulis-baca database terenkripsi lulus) sebelum paket dinaikkan.",
                            "Seluruh proyek lolos build di .NET 10 tanpa satu pun peringatan maupun galat (0 warning, 0 error)."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Ucapan Terima Kasih pada Halaman Tentang", new List<string>
                        {
                            "Halaman Tentang Aplikasi kini memiliki bagian baru 'Ucapan terima kasih' (di antara Teknologi dan Dukungan): terima kasih kepada komunitas dan pencipta perangkat lunak open-source yang menopang aplikasi (Microsoft/.NET, QuestPDF, Dapper, EPPlus, PdfiumViewer, SQLite + SQLCipher, dan lainnya), para penguji dan pemberi masukan, serta pengguna dan mitra — termasuk donatur yang mendukung lewat tautan Dukungan.",
                            "Isinya disusun sebagai data di satu tempat, jadi menambahkan nama atau pihak baru cukup menyisipkan satu baris tanpa menyentuh tampilan.",
                            "Audit halaman Tentang: daftar contoh Template Surat kini menyebut semua sembilan contoh yang ada di kode — Surat Keterangan Domisili Usaha sebelumnya tidak disebut."
                        }),
                        new CatatanRilisSubSection("Perbaikan — Login Google pada Aplikasi Hasil Installer", new List<string>
                        {
                            "Penyebab kegagalan login Google setelah aplikasi dipasang dari installer ditemukan dan ditutup: kredensial klien OAuth hidup di berkas rahasia build yang sengaja tidak ikut git, sehingga paket yang dibangun tanpa berkas itu (mis. rilis dari GitHub Actions) hanya memuat placeholder — sedangkan isian kredensial di Pengaturan sudah dihapus lebih dulu, sehingga login Google tidak bisa dihidupkan sama sekali di komputer pengguna.",
                            "Bagian Kredensial Google dikembalikan pada halaman Pengaturan Aplikasi: Client ID dan Client Secret dapat diisi teknisi, disimpan terenkripsi (DPAPI, khusus pengguna Windows yang bersangkutan), lalu diuji dengan tombol Uji Koneksi yang menampilkan email akun hasil login. Kartu ini juga menampilkan sumber kredensial yang sedang dipakai dan memberi tahu apakah paket aplikasi memuat kredensial tertanam atau tidak.",
                            "Gerbang kredensial pada alur rilis: skrip baru memeriksa hasil publish dan membatalkan proses sebelum zip/installer dibuat bila kredensial OAuth tidak tertanam, sehingga installer yang tidak bisa login Google tidak lagi terbit tanpa disadari. Gerbang yang sama dipasang pada workflow rilis GitHub Actions (dengan opsi sadar untuk tetap menerbitkan bila memang disengaja), dan statusnya ikut tertulis pada catatan rilis.",
                            "Pesan panduan pada aplikasi diperbarui: ketika kredensial belum ada, aplikasi mengarahkan ke Pengaturan Aplikasi → Kredensial Google, bukan lagi menyuruh menyunting appsettings.json."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — SK Perangkat Desa dan Menu SOTK", new List<string>
                        {
                            "Blanko SK Perangkat Desa diselesaikan menyeluruh: kolom buku SK diaudit dan ditambah, formulir input beserta cetaknya dilengkapi, dan generator dokumennya kini mencakup Formulir, Struktur, dan Gambar (lambang Garuda ikut tertanam). Panel SK beserta berkas lampirannya dikelola dalam satu tempat, dan aturan jabatan dijaga otomatis.",
                            "Dua menu baru — SOTK Desa dan Perdes SOTK — menerbitkan naskah ber-lambang Garuda dengan bentuk peraturan yang masih berlaku: PP 16/2026 menggantikan PP 43/2014, dan dasar hukum SK diperbarui mengikuti aturan terkini.",
                            "Data perangkat desa dirapikan: isian Alamat dibuang karena Dusun/RT/RW sudah mewakili, filter Status/Jabatan kosong dihapus, dan penambahan orang kini memakai satu tombol 'Tambah Orang' di kiri bilah aksi.",
                            "Menu utama memakai bilah dua baris (Pemerintahan dan Lembaga) yang disusun dari data, sehingga penambahan menu berikutnya cukup satu baris data."
                        }),
                        new CatatanRilisSubSection("Peningkatan — Formulir Surat dan Tampilan Aplikasi", new List<string>
                        {
                            "Deret Dusun/Jalan, RT, dan RW pada 14 form input surat memakai label di samping kotak isian seperti baris lain (kotak RT/RW ringkas 88 px) sehingga pengisian lebih cepat dan form lebih rapat; kartu DATA REKENING dan PERIODE REKENING pada halaman Rekening Koran tampil berdampingan.",
                            "Tembusan surat keputusan dicetak setelah blok tanda tangan Kepala Desa — rata kiri setelah nama penandatangan — tidak lagi berdampingan di sel tabel, dan urutannya sudah diverifikasi pada semua jenis SK.",
                            "Jendela pembuka mengikuti tema yang dipilih (termasuk mode Otomatis yang mengikuti mode gelap Windows) dengan kartu membulat dan indikator tahap beranimasi: titik berdenyut saat tahap berjalan, lalu berubah menjadi centang saat tahapnya selesai, dengan durasi mengikuti pilihan kecepatan animasi pengguna.",
                            "Tampilan diseragamkan lewat design system: token bayangan lembut dan tebal, cincin fokus kotak isian, kepala halaman bergradien Primary→Secondary dengan tombol yang kontras di atasnya, tooltip dan dropdown seragam, serta panel keadaan kosong berchip ikon di delapan halaman data (pengganti emoji agar ikut tema).",
                            "Jendela utama dan jendela impor Word kini bersudut membulat di Windows 11 lewat preferensi DWM resmi — tanpa mematikan resize; kartu login dan dialog bertema (Catatan Surat serta dialog pesan) memakai sudut 14 px dengan bayangan lembut."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug — Keterbacaan dan Kerapian Tampilan", new List<string>
                        {
                            "Navigasi bagian pada halaman Pengaturan Aplikasi kini memakai warna keluarga sidebar sehingga teksnya terang di panel gelap: sebelumnya nyaris menyatu di tema Green, Blue, dan Pink (rasio kontras hanya 1,4–2,5:1), sekarang 6–21:1 dan lolos WCAG AA di semua tema.",
                            "Kotak isian Dusun pada form surat kini sejajar persis dengan kotak isian baris lain; sebelumnya meleset sekitar 33 px karena kolom label pada baris bersarang tidak ikut ukuran bersama.",
                            "Tombol SK, PERDES, dan PERKADES di halaman buku keputusan tidak lagi terpotong: lebarnya mengikuti panjang teks, bukan dipatok 80 px."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.4 (30 September 2026)",
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — API Desa (Layanan Data Lokal)", new List<string>
                        {
                            "Menu BANTUAN baru 'API Desa': aplikasi dapat membuka layanan HTTP lokal (http://localhost:8790/sudes/api/v1) untuk dibaca sistem lain di kantor desa — mati secara bawaan dan hanya melayani localhost.",
                            "Akses memakai kunci API (header X-Api-Key atau Bearer) dengan cakupan tambahan: kunci Agregat untuk membaca statistik kependudukan & data perangkat desa, dan kunci Permintaan untuk mengirim permintaan surat dari sistem luar ke antrean operator di panel Layanan Online.",
                            "Kunci utama (Penuh), kunci tambahan, port (1025-65535), dan pembatasan laju (60 permintaan/menit) dikelola langsung di halaman API Desa; kunci disimpan terenkripsi DPAPI dan hanya bisa dibaca akun Windows yang sama.",
                            "Endpoint verifikasi surat GET /verifikasi/{kode} terbuka tanpa kunci API: pihak luar cukup membuka alamatnya di peramban untuk memeriksa keaslian surat terbit.",
                            "Data pribadi warga tidak pernah diekspos — API hanya membuka angka agregat, data perangkat, dan permintaan surat; seluruh kegiatan tercatat di Riwayat Aktivitas."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Lokasi Berkas Database Dapat Dipilih", new List<string>
                        {
                            "Pengaturan Aplikasi → Database Desa: lokasi berkas desa.db kini dapat dipindah sendiri (mis. ke folder data desa di drive D) lewat tombol pilih berkas, tanpa menyentuh berkas konfigurasi.",
                            "Pilihan disimpan di profil pengguna dan tetap dipakai setelah aplikasi diperbarui; kembali ke lokasi bawaan cukup satu tombol, dan lokasi aktif beserta status berkasnya (terenkripsi & terkunci / siap dienkripsi / tidak terbaca) tampil di halaman.",
                            "Saat berpindah, aplikasi memeriksa berkas lebih dulu: database yang belum terenkripsi atau tidak terbaca meminta konfirmasi sebelum dipakai, dan jalur tidak sah otomatis kembali ke lokasi bawaan tanpa menggagalkan pembukaan aplikasi.",
                            "Pengaturan diterapkan setelah aplikasi ditutup lalu dibuka lagi agar seluruh sambungan database tertutup rapi — tidak ada data yang dipindah atau diubah otomatis. Tombol 'Simpan & Mulai Ulang Sekarang' mengerjakan penutupan dan pembukaan kembali itu seketika, lengkap dengan konfirmasi."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Laporan Penduduk", new List<string>
                        {
                            "Menu baru LAPORAN PENDUDUK pada kelompok DATA DESA: rekapitulasi kependudukan per tanggal laporan yang bisa dipilih isinya per tabel, lalu dicetak sebagai PDF atau disimpan sebagai Excel — angka yang tampil di layar, di PDF, dan di Excel berasal dari satu perhitungan sehingga tidak pernah berbeda.",
                            "Dialog 'Pilih isi laporan' menyediakan daftar tabel beserta tombol Pilih semua dan Hapus semua yang langsung menandai centang di layar; satu tombol Cetak PDF menyusun dokumen lalu membuka halaman pratinjau PDFium dengan tombol Cetak dan Ekspor di dalamnya — tombol Pratinjau yang terpisah sudah digabungkan ke sini.",
                            "Kartu judul 'Isi laporan' tidak lagi menimpa tombol aksi (kotak dilebarkan dengan teks terpotong rapi), dan pratinjau tidak pernah tampil kosong karena navigasinya membawa jalur berkas penuh — bukan sekadar nama berkas."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Tutup Buku Tahunan", new List<string>
                        {
                            "Menu baru TUTUP BUKU TAHUNAN: memeriksa keutuhan penomoran surat satu tahun — deret nomornya ditampilkan berikut catatan bila ada nomor yang loncat atau kembar — lalu menutup buku agar nomor tahun itu dinyatakan final. Menutup buku tidak menghapus apa pun dan selalu bisa dibuka kembali, jadi halaman ini sengaja tidak memiliki tombol hapus.",
                            "Arsip register tahunan dapat dicetak langsung dari halaman ini sebagai PDF atau Excel; tahun yang punya surat dimuat otomatis (terbaru di atas) dan status bukanya tersimpan di database. Tabel statusnya dibuat otomatis saat aplikasi dibuka — termasuk pada database lama — jadi tidak ada migrasi manual."
                        }),
                        new CatatanRilisSubSection("Fitur Baru — Verifikasi Surat", new List<string>
                        {
                            "Menu baru VERIFIKASI SURAT: memeriksa keaslian surat terbit langsung dari aplikasi dengan mengetik kode verifikasinya — aplikasi mencocokkan kode dengan data surat di register dan menampilkan hasilnya, atau peringatan bila kode tidak dikenal."
                        }),
                        new CatatanRilisSubSection("Peningkatan", new List<string>
                        {
                            "Halaman Tentang Aplikasi diselaraskan: daftar fitur dan teknologi kini menyebut enkripsi database SQLCipher dan API Desa, dan lokasi data dipecah jelas — 'Folder aplikasi' (program + database) serta 'Preferensi & kunci' (%LOCALAPPDATA%\\SuDesApp).",
                            "Pengaturan Aplikasi → Preferensi: tinggi maksimum baris tabel bertext panjang (Register Surat, Register NTCR, API Desa, Data Warga) kini dapat diatur operator — cukup tekan tombol cepat Ringkas, Normal, atau Lega, atau ketik sendiri angkanya (40-600 piksel, bawaan 200) — dan tersimpan di preferensi aplikasi. Naikkan bila alamat panjang perlu tampil lebih utuh, turunkan bila tabel ingin lebih ringkas; tabel yang sedang terbuka langsung menyesuaikan tanpa perlu dibuka ulang.",
                            "Kontras keenam tema (Terang, Gelap, Biru, Hijau, Pink, Abu-abu) disesuaikan agar memenuhi WCAG 2.1 AA: token teks 'di atas latar' untuk lencana sukses/peringatan/galat ditambahkan, warna teks status dan subjudul header disetel ulang, serta teks aksen kini memakai token teks — bukan warna isian. Akibatnya teks di atas aksen di tema Terang/Gelap/Hijau/Pink berganti dari putih (1,9-3,5:1) menjadi tinta gelap yang terbaca, dan header gradien di empat tema itu sedikit lebih gelap. Aturannya dijaga alat Tools/check-kontras-wcag.ps1 (49 pasangan × 7 tema). Putaran kedua (1 Oktober 2026) menutup celahnya: sepuluh pasangan yang dipakai kode tetapi belum pernah diperiksa ditambahkan ke gerbang, dan beberapa angka palet disetel ulang (teks sekunder tema Gelap dari #9E9E9E ke #BDBDBD, kartu galat tema Pink dari #FEE2E2 ke #FEEAEA, aksen terang tema Biru dan Pink diperjelas).",
                            "Tema ketujuh 'Kontras Tinggi' ditambahkan untuk pengguna low vision: latar hitam pekat, teks putih, dan aksen kuning menyala — dipilih dari daftar Tema seperti tema lain, tersimpan, dan tetap berlaku setelah aplikasi dibuka ulang. Ini satu-satunya tema yang menargetkan WCAG 2.1 AAA (teks normal minimal 7:1), jadi ambangnya ikut dinaikkan di alat Tools/check-kontras-wcag.ps1 — bukan sekadar AA seperti enam tema lainnya.",
                            "Menu 'Ubah Kata Sandi' di sidebar dihapus dan dipindah ke halaman Kelola Pengguna: tombol 'Ubah Kata Sandi Saya' kini berada di header halaman itu sehingga tetap tersedia untuk semua peran — bukan hanya Administrator — sementara halaman Kelola Pengguna ditata ulang dengan kotak pencarian nama/nama akun/peran, ringkasan jumlah akun aktif, lencana status akun, dan daftar akun yang lebih rapi.",
                            "Kartu 'Akun Saya' ditambahkan di halaman Kelola Pengguna: menampilkan nama lengkap, peran, status aktif/nonaktif, nama akun yang dipakai berikut cara masuknya (Google atau akun aplikasi), dan peringatan bila akun itu ditandai nonaktif. Kartu ini tampil untuk semua peran — termasuk Sekretaris Desa, Kepala Desa, dan Auditor yang tidak berhak membuka daftar akun — karena halaman ini satu-satunya tempat pemakai melihat identitas akunnya sendiri secara utuh.",
                            "Perangkat Desa kini menempati bagiannya sendiri di sidebar (PERANGKAT DESA), tepat di atas DATA DESA dan di bawah Formulir, dengan keterangan singkat yang muncul saat kursor diarahkan ke menunya. Seluruh tombol di halaman itu juga memakai ikon tema — bukan lagi emoji — sehingga warnanya ikut berganti bersama tema dan tampilannya seragam dengan halaman lain.",
                            "Seluruh tabel baca-saja kini memakai pewarnaan zebra seragam — baris bergantian warna mengikuti tema, sementara sorotan kursor dan baris terpilih tetap menonjol — sehingga daftar panjang seperti Riwayat Aktivitas dan Wa Panel lebih mudah diikuti mata."
                        }),
                        new CatatanRilisSubSection("Perbaikan Bug", new List<string>
                        {
                            "Halaman-halaman yang sempat kosong atau gagal dibuka kini tampil normal: kotak pencarian Data Warga, kepala halaman Data Perangkat Desa, serta halaman-halaman menu BANTUAN — tanpa sumber daya tema yang hilang.",
                            "Tinggi baris tabel pada Register Surat, Register NTCR, API Desa, dan pratinjau impor Data Warga kini menyesuaikan panjang teksnya secara otomatis — jumlah baris teks dihitung per baris data, jadi alamat dan keterangan sepanjang apa pun terbaca tanpa terpotong (tooltip tetap tersedia).",
                            "Kolom tabel Data Perangkat Desa diperiksa lengkap setiap aplikasi dibuka sehingga database lama tidak lagi gagal dengan galat 'no such column'.",
                            "Nomor langkah pada panduan WhatsApp tidak terlihat karena teksnya dibuat sejajar warna lencananya, dan nomor langkah wizard Pengaturan Aplikasi tercetak putih di atas lingkaran abu-abu yang belum aktif — keduanya kini memakai token teks yang sesuai sehingga terbaca di semua tema.",
                            "Teks putih kaku di atas latar berwarna (badge notifikasi, lencana TERBARU dan Draft, ikon dialog, label toggle ON, ikon panduan) diganti token tema, jadi kontrasnya ikut benar saat operator mengganti tema tanpa membuka halaman lagi.",
                            "Kepala halaman Data Perangkat Desa tidak lagi tampil tanpa latar, dan sejumlah teks yang nyaris tak terbaca diperbaiki: keterangan pada bilah status Google Drive, pesan galat di jendela Masuk serta halaman Data Warga dan Pembaruan, lencana pada halaman Pembaruan dan Dokumentasi, judul pada Ringkasan Buku Tahunan dan daftar surat hari ini, serta keterangan di Formulir Surat dan wizard Template Surat. Di Pengaturan Aplikasi, subjudul yang tercetak putih di atas kartu putih dan satu butir navigasi di kartu terang kini mengikuti token teks; pratinjau kertas di jendela impor Word memakai warna teks tetap karena latarnya memang selalu putih.",
                            "Webhook WhatsApp Cloud API tidak lagi memerlukan hak administrator: bila alamat http://+:PORT/ ditolak sistem, aplikasi otomatis mencoba localhost dan 127.0.0.1 sehingga listener tetap hidup tanpa langkah netsh — panduan WhatsApp kini menyebut netsh hanya sebagai pilihan tambahan.",
                            "Tabel Data Warga dapat digulir mendatang pada layar sempit sehingga kolom Aksi tetap terjangkau, dan tombol Terapkan kini selalu memberi pesan status — termasuk saat perubahan tidak jadi diterapkan — alih-alih diam tanpa keterangan."
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.5.3 (19 September 2026)",
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
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru — Contoh Template Surat Siap Pakai", new List<string>
                        {
                            "Halaman Template Surat kini menyediakan enam contoh surat siap pakai yang bisa langsung diisi dan dicetak, dijadikan dasar template baru, atau sekadar dilihat bentuknya: Surat Pengantar RT/RW, Surat Izin Keramaian, Surat Keterangan Penghasilan, Surat Keterangan Belum Menikah, Surat Undangan Rapat, dan Surat Pengumuman Warga. (Kini ada sembilan contoh: Domisili Usaha, Surat Kuasa, dan Surat Tugas menyusul kemudian — daftar lengkapnya disebut pada catatan 2.5.5.)",
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
                            "Halaman Tentang Aplikasi diremajakan: judul berubah dari \"TENTANG APLIKASI\" menjadi \"TENTANG SUITAPPSU DESA\", deskripsi diperbarui mencerminkan layanan online WhatsApp & backup Google Drive, dan bagian Ikon Aplikasi dihapus karena tidak digunakan lagi. (Judul halaman Tentang kini kembali menjadi \"TENTANG APLIKASI\" mengikuti tata letak halaman Tentang yang baru.)"
                        })
                    }),
                new CatatanRilisEntry(
                    "Versi 2.1.1 (16 September 2026)",
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
                    new List<CatatanRilisSubSection>
                    {
                        new CatatanRilisSubSection("Fitur Baru", new List<string>
                        {
                            "Migrasi total antarmuka ke WPF modern dengan 6 tema: Light, Dark, Biru Office, Green, Pink, dan Slate. (Kini tujuh tema plus mode Otomatis; nama tampil saat ini: Terang, Gelap, Biru, Hijau, Pink, Abu-abu, dan Kontras Tinggi — tema ketujuh menyusul di 2.5.4.)",
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

            // Badge "TERBARU" diturunkan dari posisi, bukan bendera manual: hanya
            // kartu paling atas. Ini yang mencegah dua badge tampil bersamaan.
            for (int i = 0; i < daftar.Count; i++)
            {
                daftar[i].IsLatest = i == 0;
            }

            return daftar;
        }
    }
}
