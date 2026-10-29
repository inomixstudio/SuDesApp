using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman Langganan — berdiri sendiri di navigasi menu utama, bukan lagi
    /// salah satu bagian di dalam Pengaturan Aplikasi.
    ///
    /// Alasan dipisah: status lisensi adalah hal yang paling sering dicari
    /// pengguna (kapan berakhir, bagaimana memperpanjang), sementara Pengaturan
    /// Aplikasi berisi setelan teknis yang jarang disentuh. Menaruh urusan
    /// pembayaran di dalamnya membuat pengguna harus tahu lebih dulu bahwa ada
    /// bagian "Langganan" di sana.
    ///
    /// PEMBAGIAN TUGAS
    /// ---------------
    /// Kelas ini memegang keadaan yang diikat XAML dan mengatur urutan tindakan
    /// pengguna. Semua KEBIJAKAN ada di Core, satu pemilik per urusan:
    ///   • status token & alamat server   → <see cref="LisensiManager"/>
    ///   • status → kalimat/nada/petak KPI → <see cref="RingkasanLangganan"/>
    ///   • katalog harga & tagihan         → <see cref="LanggananKlien"/>
    ///   • tampilan harga kartu paket      → <see cref="KatalogLangganan"/>
    ///   • fitur mana yang terkunci        → <see cref="PenjagaLisensi"/>
    /// Halaman ini tidak menghitung harga, tidak memutuskan kelayakan, dan tidak
    /// menyusun sendiri kalimat status — semuanya disalin dari hasil Core.
    ///
    /// Di dalam kelas ini, dua penjagaan dipusatkan supaya tidak disalin ke
    /// setiap perintah: <c>JalankanSibukAsync</c> (satu operasi sibuk pada satu
    /// waktu) dan <c>Beritahu</c> (nada + kalimat banner selalu bersama).
    ///
    /// Catatan keamanan: kode aktivasi berfungsi sebagai identitas desa (bukan
    /// lagi rahasia), sedangkan uang — nominal dan durasi — selalu ditentukan
    /// server. Kartu paket menampilkan harga dari katalog server (GET /paket),
    /// dan saat tagihan dibuat server MENEGUHKAN ulang angka itu dari katalognya
    /// sendiri — klien tidak pernah menentukan nominal.
    /// </summary>
    public class LanggananViewModel : ObservableObject
    {
        private readonly ILogger<LanggananViewModel> _logger;
        private readonly SettingsManager? _pengaturanDesa;
        private readonly LisensiManager _lisensi;
        private readonly PenjagaLisensi _penjaga;
        private readonly LanggananKlien _klien;
        private readonly SambunganServerLisensi _sambungan;

        public LanggananViewModel(
            ILogger<LanggananViewModel> logger,
            LisensiManager lisensi,
            PenjagaLisensi penjaga,
            LanggananKlien klien,
            SambunganServerLisensi sambungan,
            SettingsManager? pengaturanDesa = null,
            ServerLisensiLokal? serverLokal = null,
            NavigationService? navigasi = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lisensi = lisensi ?? throw new ArgumentNullException(nameof(lisensi));
            _penjaga = penjaga ?? throw new ArgumentNullException(nameof(penjaga));
            _klien = klien ?? throw new ArgumentNullException(nameof(klien));

            _sambungan = sambungan ?? throw new ArgumentNullException(nameof(sambungan));
            // Server lokal & navigasi boleh null pada uji: halaman tetap berfungsi
            // sebagai klien server lisensi jarak jauh.
            _serverLokal = serverLokal;
            _navigasi = navigasi;
            // Profil desa lokal: sumber nama desa untuk jalur BERLANGGANAN AWAL
            // (desa belum punya kode aktivasi — nama desa dikirim ke server dan
            // kode baru diterbitkan otomatis setelah tagihan dibuat). Boleh null
            // pada uji: AmbilNamaDesaProfilAsync mundur ke string kosong.
            _pengaturanDesa = pengaturanDesa;

            // Server yang menyala belakangan harus membuat chip pulih tanpa
            // membuka ulang halaman. Langganannya lemah: halaman ini transient
            // sedangkan perekam sambungan singleton (lihat LanggananSambunganLemah).
            LanggananSambunganLemah.Daftar(this, _sambungan);

            _statusSesaat = new StatusSesaat(p => StatusMessage = p, StatusSiap);

            // Kartu paket dibangun dari katalog bawaan Core (kode, label, durasi
            // cadangan, catatan cadangan) dan langsung ditandai: sebelum angka
            // server tiba, tahunan yang ditandai "PALING HEMAT".
            PilihanPaket = KatalogLangganan.Bawaan.Kartu.Select(k => new PaketOpsi(k)).ToList();
            TerapkanKatalog(KatalogLangganan.Bawaan);

            // Kode aktivasi tersimpan dimuat; status dari cache offline
            // ditampilkan seketika (cek server hanya lewat tombol Cek Status).
            _kodeAktivasi = AppPreferenceStore.GetKodeAktivasi();
            CekLanggananCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(CekLanggananAsync), () => !LanggananSibuk);
            SimpanLanggananCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(SimpanLaluCekAsync), () => !LanggananSibuk);
            BayarLanggananCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(BayarLanggananAsync), () => !LanggananSibuk);
            MulaiTrialCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(MulaiTrialAsync), () => !LanggananSibuk);
            // QRIS manual: perintahnya sinkron (tidak menghubungi server) —
            // pembayarannya diverifikasi admin setelah dana masuk.
            TampilkanQrisCommand = new RelayCommand(AlihTampilQris);
            SalinRincianQrisCommand = new RelayCommand(SalinRincianQris);
            BukaWaAdminCommand = new RelayCommand(BukaWaAdmin);
            SimpanServerCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(SimpanServerAsync), () => !LanggananSibuk);
            BukaPusatAdministrasiCommand = new AsyncRelayCommand(BukaPusatAdministrasiAsync);
            BukaKunciServerCommand = new RelayCommand(BukaKunciServer,
                () => _alamatTerkunciSesi && !_lisensi.UrlServerTerkunci);
            _urlServer = _lisensi.UrlServer;

            // Keadaan awal chip: alamat kosong → belum diatur; ada alamat tetapi
            // belum ada panggilan dari halaman ini → belum diuji. Panggilan
            // katalog di latar belakang (beberapa baris di bawah) akan segera
            // menggantinya dengan hasil sungguhan.
            _keadaanServer = string.IsNullOrWhiteSpace(_urlServer)
                ? KeadaanServer.BelumDiatur
                : KeadaanServer.BelumDiuji;

            // Baca cache tanpa jaringan supaya status tampil seketika. Nilai
            // yang ditampilkan sudah diverifikasi tanda tangannya oleh
            // LisensiManager, jadi cache yang diedit manual tidak akan
            // menampilkan status palsu.
            TampilkanStatusLangganan(_lisensi.BacaCachePublik());

            // Angka dari salinan katalog terakhir ditampilkan lebih dulu supaya
            // kartu tidak kosong saat server lisensi sedang tidak bisa
            // dijangkau; angka resmi menyusul dari server di latar belakang
            // (tidak menahan tampilan; kegagalan jaringan wajar dan tidak pernah
            // ditampilkan sebagai galat).
            TampilkanKatalogTersimpan();
            _ = MuatKatalogAsync();
        }

        /// <summary>Judul halaman pada header.</summary>
        public string HeaderTitle => "LANGGANAN";

        /// <summary>Penjelasan singkat di bawah judul header.</summary>
        public string HeaderSubtitle =>
            "Status masa aktif lisensi desa dan perpanjangannya. Data desa tetap tersimpan di komputer ini — " +
            "yang diurus di halaman ini hanya masa aktif aplikasinya.";

        // =======================================================================
        // Kode aktivasi & status langganan.
        // =======================================================================

        private string _kodeAktivasi = string.Empty;

        /// <summary>Kode aktivasi yang diketik pengguna (tersimpan saat Simpan).</summary>
        public string KodeAktivasi
        {
            get => _kodeAktivasi;
            set
            {
                if (SetProperty(ref _kodeAktivasi, value))
                {
                    // Kode ikut ke rincian QRIS (referensi pembayaran manual).
                    OnPropertyChanged(nameof(QrisReferensi));
                }
            }
        }

        private string _langgananStatus = "Belum diperiksa";

        /// <summary>Status langganan yang terbaca (Tidak aktif / Aktif s.d. …).</summary>
        public string LanggananStatus
        {
            get => _langgananStatus;
            private set => SetProperty(ref _langgananStatus, value);
        }

        private string _langgananInfo = string.Empty;

        /// <summary>Nama desa + sumber status (cache offline atau cek server).</summary>
        public string LanggananInfo
        {
            get => _langgananInfo;
            private set => SetProperty(ref _langgananInfo, value);
        }

        private int _langgananTone;

        /// <summary>Nada kotak status langganan: 1 = aktif (hijau), 2 = tidak aktif (kuning), 3 = galat (merah), 0 = netral.</summary>
        public int LanggananTone
        {
            get => _langgananTone;
            private set => SetProperty(ref _langgananTone, value);
        }

        // =======================================================================
        // Ringkasan status untuk kartu status (lencana header + petak KPI).
        // Semua turunan dari status terakhir yang sudah diverifikasi tanda
        // tangannya — tidak ada angka yang dihitung ulang di sisi tampilan.
        // =======================================================================

        /// <summary>
        /// Ringkasan status terakhir yang sudah diverifikasi tanda tangannya —
        /// satu-satunya sumber kalimat banner dan angka petak KPI di kartu Status
        /// Langganan. Penyusunannya ada di Core (<see cref="RingkasanLangganan"/>),
        /// jadi halaman ini tidak menyimpan aturan lisensi sendiri dan tanggal di
        /// banner tidak bisa berbeda dari tanggal di petak.
        /// </summary>
        private RingkasanLangganan _ringkasan = RingkasanLangganan.BelumDiperiksa;

        /// <summary>
        /// Label keadaan untuk lencana di header. Trial server memakai token sah,
        /// tetapi tetap tampil "Trial" (bukan "Full Service") karena desa belum
        /// membayar apa pun — aturannya di <see cref="PenjagaLisensi.KeadaanTampil"/>.
        /// </summary>
        public string KeadaanLabel => PenjagaLisensi.LabelKeadaan(KeadaanTampil);

        /// <summary>Nada lencana keadaan: 1 aktif (hijau), 2 Demo/Trial (kuning), 3 baca-saja (merah).</summary>
        public int KeadaanTone => PenjagaLisensi.NadaKeadaan(KeadaanTampil);

        /// <summary>Keadaan yang ditampilkan (trial server dibaca sebagai Trial).</summary>
        private SuDesApp.Services.KeadaanLisensi KeadaanTampil =>
            PenjagaLisensi.KeadaanTampil(_penjaga.Keadaan, _penjaga.Status?.Paket);

        /// <summary>True bila aplikasi sedang baca-saja karena langganan benar-benar berakhir.</summary>
        public bool BacaSaja => _penjaga.BacaSaja;

        /// <summary>Pesan baca-saja — sama persis dengan yang dipakai gerbang fitur.</summary>
        public string PesanBacaSaja => PenjagaLisensi.PesanAksesDikunci;

        /// <summary>
        /// Nama paket aktif dalam bahasa manusia ("Tahunan", "6 Bulan", "Bulanan",
        /// "Trial 30 hari"). Pemetaan kode → nama ada di Core, tempat kode "trial"
        /// — yang tidak punya kartu karena gratis — tetap terbaca sebagai nama
        /// paket, bukan kode mentah.
        /// </summary>
        public string PaketNama => _ringkasan.PaketNama;

        /// <summary>
        /// Batas SAH efektif dalam waktu lokal (tanggal bayar ATAU tanggal cek
        /// terakhir + masa tenggang), atau "—" bila belum diketahui.
        ///
        /// Diambil dari <see cref="RingkasanLangganan"/> supaya petak ini tidak
        /// bisa memakai tanggal yang berbeda dari kalimat banner di atasnya.
        /// </summary>
        public string BerlakuSampaiTampil => _ringkasan.SahSampaiTampil;

        /// <summary>Sisa hari sebelum perlu menyambung internet lagi, atau "—".</summary>
        public string SisaHariTampil => _ringkasan.SisaHariTampil;

        /// <summary>Waktu pemeriksaan server terakhir (waktu lokal), atau "—".</summary>
        public string TerakhirDiperiksaTampil => _ringkasan.TerakhirDiperiksaTampil;

        /// <summary>
        /// Tombol trial tampil hanya bila langganan TIDAK sedang aktif — yang
        /// sudah Full Service tidak perlu trial, dan server pun akan menolak.
        /// Sebelum ada status terbaca, tombol tampil.
        /// </summary>
        public bool TampilTombolTrial => _ringkasan.TampilTombolTrial && !_penjaga.TrialPernahDimulai;

        /// <summary>
        /// True selama trial berjalan (lokal maupun server). Kartu Status memakai
        /// ini untuk memasang tombol "Bayar / Berlangganan" langsung di samping
        /// Cek Status — supaya desa yang sedang uji coba tidak perlu mencari
        /// tombol bayar di kartu paket di bawahnya. Perintahnya tetap
        /// <see cref="BayarLanggananCommand"/> yang sama, jadi aturan pembayaran
        /// (termasuk penjagaan kode aktivasi) tinggal satu salinan.
        /// </summary>
        public bool SedangTrial => _ringkasan.SelamaTrial;

        private bool _langgananSibuk;

        /// <summary>Penanda sedang cek/membuat tagihan (menonaktifkan tombol).</summary>
        public bool LanggananSibuk
        {
            get => _langgananSibuk;
            private set
            {
                if (SetProperty(ref _langgananSibuk, value))
                {
                    CekLanggananCommand.RaiseCanExecuteChanged();
                    SimpanLanggananCommand.RaiseCanExecuteChanged();
                    BayarLanggananCommand.RaiseCanExecuteChanged();
                    MulaiTrialCommand.RaiseCanExecuteChanged();
                    SimpanServerCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncRelayCommand CekLanggananCommand { get; }
        public AsyncRelayCommand SimpanLanggananCommand { get; }
        public AsyncRelayCommand BayarLanggananCommand { get; }
        public AsyncRelayCommand MulaiTrialCommand { get; }
        public AsyncRelayCommand SimpanServerCommand { get; }
        public RelayCommand BukaKunciServerCommand { get; }

        // =======================================================================
        // Server lisensi — alamat dipakai semua panggilan status, katalog, dan
        // tagihan. Yang disimpan di sini hanya teks yang sedang diketik; pemilik
        // alamat yang dipakai panggilan adalah LisensiManager.
        // =======================================================================

        private string _urlServer = string.Empty;

        /// <summary>
        /// Kunci sesi: dipasang setelah server benar-benar menjawab, supaya
        /// alamat yang sudah terbukti tidak tergeser tanpa sengaja (mis. karena
        /// mengetik di kotak alamat). Pengguna tetap bisa membukanya lewat
        /// tombol Ubah alamat; kunci konfigurasi rilis tidak bisa dibuka.
        /// </summary>
        private bool _alamatTerkunciSesi;

        /// <summary>
        /// Keadaan sambungan ke server lisensi menurut panggilan terakhir yang
        /// BENAR-BENAR terjadi — bukan tebakan dari bentuk alamatnya.
        /// </summary>
        public enum KeadaanServer
        {
            /// <summary>Alamat belum diisi: belum ada yang bisa dihubungi.</summary>
            BelumDiatur,

            /// <summary>Alamat sudah diisi, tetapi belum ada panggilan yang selesai dari halaman ini.</summary>
            BelumDiuji,

            /// <summary>Server menjawab dan katalognya terbaca.</summary>
            Terhubung,

            /// <summary>Panggilan terakhir tidak sampai ke server, atau jawabannya tidak bisa dipakai.</summary>
            TidakTerjangkau
        }

        private KeadaanServer _keadaanServer = KeadaanServer.BelumDiuji;

        /// <summary>
        /// URL server lisensi yang diisi pengguna (tersimpan sebagai preferensi,
        /// awalnya diambil dari <c>LisensiManager.UrlServer</c>).
        /// </summary>
        public string UrlServer
        {
            get => _urlServer;
            set
            {
                // Kotak alamat sudah read-only saat terkunci, tetapi penjagaan
                // ini tetap perlu: perubahan bisa datang dari binding, kode, atau
                // test. Alamat yang terkunci tidak boleh berganti diam-diam.
                if (UrlServerTerkunci && !string.Equals(_urlServer, value, StringComparison.Ordinal))
                {
                    OnPropertyChanged(nameof(UrlServer));
                    return;
                }

                if (SetProperty(ref _urlServer, value))
                {
                    // Alamat yang diketik berbeda dari alamat yang terakhir diuji:
                    // keadaan lama tidak lagi menggambarkan isi kotak ini.
                    TetapkanKeadaanServer(string.IsNullOrWhiteSpace(value)
                        ? KeadaanServer.BelumDiatur
                        : KeadaanServer.BelumDiuji);
                }
            }
        }

        /// <summary>
        /// True bila alamat server tidak boleh diubah dari halaman ini: karena
        /// ditanam di konfigurasi rilis, atau karena koneksi ke alamat ini sudah
        /// terbukti pada sesi berjalan.
        /// </summary>
        public bool UrlServerTerkunci => _lisensi.UrlServerTerkunci || _alamatTerkunciSesi;

        /// <summary>Kebalikan <see cref="UrlServerTerkunci"/> untuk mengikat kontrol input.</summary>
        public bool UrlServerDapatDiubah => !UrlServerTerkunci;

        /// <summary>Tombol Ubah alamat hanya masuk akal untuk kunci sesi.</summary>
        public bool TampilBukaKunciServer => _alamatTerkunciSesi && !_lisensi.UrlServerTerkunci;

        /// <summary>Keterangan mengapa kotak alamat tidak bisa diubah.</summary>
        public string CatatanAlamatServer => !UrlServerTerkunci
            ? string.Empty
            : _lisensi.UrlServerTerkunci
                ? "Alamat server dikunci oleh konfigurasi aplikasi. Perubahan alamat hanya bisa lewat rilis baru."
                : "Alamat server dikunci karena koneksi sudah terverifikasi. Tekan Ubah alamat bila memang perlu menggantinya.";

        /// <summary>
        /// Buka kunci sesi secara sadar. Kunci dari konfigurasi rilis tidak bisa
        /// dibuka dari aplikasi.
        /// </summary>
        private void BukaKunciServer()
        {
            if (_lisensi.UrlServerTerkunci) return;
            _alamatTerkunciSesi = false;
            OnPropertyChanged(nameof(UrlServerTerkunci));
            OnPropertyChanged(nameof(UrlServerDapatDiubah));
            OnPropertyChanged(nameof(TampilBukaKunciServer));
            OnPropertyChanged(nameof(CatatanAlamatServer));
            BukaKunciServerCommand.RaiseCanExecuteChanged();
            StatusMessage = "Alamat server dibuka — ganti alamat lalu tekan Simpan & Uji.";
        }

        /// <summary>
        /// True bila chip dan pesan peringatan harus tampil — yaitu selama server
        /// BELUM terbukti menjawab (alamat kosong, belum diuji, atau tidak
        /// terjangkau).
        ///
        /// Nilainya datang dari hasil panggilan sungguhan, bukan dari kata pada
        /// alamat: alamat <c>localhost</c> yang servernya benar-benar menjawab
        /// (server uji di komputer sendiri) dihitung TERHUBUNG. Sebelumnya setiap
        /// alamat yang memuat "localhost" selalu dilabeli "Belum diatur" walau
        /// status dan pembayaran berjalan normal dari alamat itu.
        /// </summary>
        public bool ServerBelumTersambung => _keadaanServer != KeadaanServer.Terhubung;

        /// <summary>Teks chip status server di kartu Server Lisensi.</summary>
        public string ServerChipTeks => _keadaanServer switch
        {
            KeadaanServer.Terhubung => "Terhubung",
            KeadaanServer.TidakTerjangkau => "Belum terjangkau",
            KeadaanServer.BelumDiuji => "Belum diuji",
            _ => "Belum diatur"
        };

        /// <summary>
        /// Pesan pengarahan di bawah kolom alamat server. Mengikuti keadaan
        /// sambungan supaya tidak lagi menyalahkan alamat yang sebenarnya bekerja
        /// (dulu pesannya selalu berbunyi "Cek Status dan pembayaran akan gagal"
        /// selama alamatnya memuat "localhost").
        /// </summary>
        public string PesanServer => _keadaanServer switch
        {
            KeadaanServer.Terhubung =>
                "Server menjawab di alamat ini — Cek Status dan pembayaran memakai alamat yang sama.",
            KeadaanServer.TidakTerjangkau =>
                "Server tidak menjawab di alamat ini. Periksa alamatnya, koneksi internet ke server, " +
                "atau apakah servernya sedang berjalan — Cek Status dan pembayaran akan gagal sampai " +
                "server terjangkau.",
            KeadaanServer.BelumDiuji =>
                "Alamat sudah tersimpan, tetapi belum pernah dibuktikan dari halaman ini. " +
                "Tekan Cek Status atau Simpan & Uji untuk memeriksanya.",
            _ => "Server belum diatur — isi alamat hasil pemasangan server lisensi, lalu tekan Simpan & Uji."
        };

        /// <summary>
        /// Simpan keadaan sambungan baru beserta seluruh teks yang bergantung
        /// padanya, supaya chip, warna, dan pesannya tidak bisa menyimpang.
        /// </summary>
        private void TetapkanKeadaanServer(KeadaanServer baru)
        {
            if (_keadaanServer == baru) return;

            bool terkunciSebelumnya = UrlServerTerkunci;
            _keadaanServer = baru;

            // Koneksi yang sudah terbukti mengunci alamat untuk sesi ini. Ini
            // bukan hukuman: tombol Ubah alamat tetap tersedia, tetapi alamat
            // tidak bisa tergeser karena salah ketik saat halaman dibuka lagi.
            if (baru == KeadaanServer.Terhubung && !_lisensi.UrlServerTerkunci)
            {
                _alamatTerkunciSesi = true;
            }

            OnPropertyChanged(nameof(ServerBelumTersambung));
            OnPropertyChanged(nameof(ServerChipTeks));
            OnPropertyChanged(nameof(PesanServer));

            if (terkunciSebelumnya != UrlServerTerkunci)
            {
                OnPropertyChanged(nameof(UrlServerTerkunci));
                OnPropertyChanged(nameof(UrlServerDapatDiubah));
                OnPropertyChanged(nameof(TampilBukaKunciServer));
                OnPropertyChanged(nameof(CatatanAlamatServer));
                BukaKunciServerCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Denyut sambungan melaporkan server kembali menjawab: muat ulang
        /// katalog (paksa) supaya chip berpindah ke "Terhubung" tanpa menunggu
        /// halaman dibuka ulang. Dipanggil dari helper langganan lemah — bisa
        /// dari utas latar, jadi perubahan keadaan tampilan diarahkan ke
        /// Dispatcher halaman.
        /// </summary>
        internal void SambunganServerBerubah()
        {
            if (_sambungan.Sekarang != KeadaanSambunganServer.Terhubung) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (dispatcher.CheckAccess())
            {
                SegarkanKarenaDenyut();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(SegarkanKarenaDenyut));
            }
        }

        /// <summary>
        /// Penjaga agar event beruntun tidak menjalankan dua pemuatan katalog
        /// sekaligus. Hanya disentuh dari utas halaman.
        /// </summary>
        private bool _menyegarkanKarenaDenyut;

        private void SegarkanKarenaDenyut()
        {
            if (_keadaanServer == KeadaanServer.Terhubung) return;
            if (_menyegarkanKarenaDenyut) return;

            _menyegarkanKarenaDenyut = true;
            _ = SegarkanKarenaDenyutAsync();
        }

        private async Task SegarkanKarenaDenyutAsync()
        {
            try
            {
                await MuatKatalogAsync(paksa: true);
            }
            catch (Exception ex)
            {
                // MuatKatalogAsync sudah menangani kegagalan jaringan (wajar,
                // halaman tetap berguna offline); ini jaring pengaman supaya
                // penanda tidak macet bila ada galat tak terduga.
                _logger.LogWarning(ex, "Penyegaran katalog karena denyut sambungan gagal");
            }
            finally
            {
                _menyegarkanKarenaDenyut = false;
            }
        }

        // =======================================================================
        // Server lisensi lokal (pusat administrasi di komputer ini).
        // =======================================================================
        // Halaman ini KENDALI yang terlihat operator; prosesnya milik
        // ServerLisensiLokal (Core) dan siklus hidupnya milik aplikasi
        // (dinyalakan saat dibuka, dimatikan saat keluar). Pilihan "nyalakan
        // bersama aplikasi" disimpan sebagai preferensi — App membaca nilai yang
        // sama pada pembukaan berikutnya, jadi tidak ada dua sumber kebenaran.
        //
        // Pusat Administrasi dibuka dari sini pada alamat yang SAMA dengan alamat
        // langganan: itulah yang membuat langganan yang dikelola operator
        // (aktivasi, perpanjangan, tandai lunas) adalah langganan yang diperiksa
        // halaman ini — bukan dua server yang berbeda.

        private readonly ServerLisensiLokal? _serverLokal;
        private readonly NavigationService? _navigasi;

        /// <summary>
        /// True bila blok server lokal ditampilkan: hanya pada build yang
        /// alamatnya boleh diganti (server uji lokal). Rilis yang terkunci pada
        /// server produksi tidak menjalankan server lisensi di komputer desa.
        /// </summary>
        public bool TampilServerLokal => _serverLokal is not null && !_lisensi.UrlServerTerkunci;

        /// <summary>
        /// Pilihan "jalankan server lisensi di komputer ini". Disimpan sebagai
        /// preferensi supaya pilihan ini berlaku juga pada pembukaan berikutnya
        /// (App yang menyalakannya), lalu dikerjakan sekarang juga.
        /// </summary>
        public bool ServerLokalAktif
        {
            get => AppPreferenceStore.IsServerLisensiLokalAktif();
            set
            {
                if (value == AppPreferenceStore.IsServerLisensiLokalAktif()) return;

                AppPreferenceStore.SetServerLisensiLokalAktif(value);
                OnPropertyChanged();
                SegarkanStatusServerLokal();

                if (value) _ = NyalakanServerLokalAsync();
                else HentikanServerLokal();
            }
        }

        /// <summary>True selama proses server yang dinyalakan aplikasi masih hidup.</summary>
        public bool ServerLokalBerjalan => _serverLokal?.Berjalan == true;

        /// <summary>Kalimat status server lokal di bawah kotak centangnya.</summary>
        public string ServerLokalStatus
        {
            get
            {
                if (_serverLokal is null) return "Server lokal tidak tersedia di aplikasi ini.";
                if (_serverLokal.Berjalan) return "Berjalan di " + _serverLokal.Alamat;

                return AppPreferenceStore.IsServerLisensiLokalAktif()
                    ? "Belum berjalan — periksa pesan kegagalan di bawah."
                    : "Tidak berjalan.";
            }
        }

        /// <summary>Di mana data langganan server lokal disimpan — dan bahwa data itu menetap.</summary>
        public string ServerLokalCatatan =>
            "Server ini yang melayani aktivasi dan perpanjangan di halaman ini, dan Pusat Administrasi " +
            "membuka alamat yang sama. Data langganan tersimpan di " +
            OpsiServerLokal.FolderDataBawaan() + " sehingga tetap ada setelah server dimatikan.";

        /// <summary>
        /// Tombol Pusat Administrasi hanya untuk sesi akun pusat — aturan yang
        /// sama dengan menu sidebar (docs/policy-peran-pengguna.md). Halaman
        /// admin sendiri tetap meminta kredensial operator server.
        /// </summary>
        public bool TampilPusatAdministrasi => _navigasi is not null && Utilities.AkunPusat.SesiIniPusat;

        public AsyncRelayCommand BukaPusatAdministrasiCommand { get; }

        private async Task NyalakanServerLokalAsync()
        {
            if (_serverLokal is null) return;

            StatusMessage = "Menyalakan server lisensi lokal…";
            try
            {
                if (await _serverLokal.NyalakanDanPakaiAsync(_lisensi))
                {
                    // Alamat server berubah ke server yang baru menyala: kartu
                    // paket dan chip status harus berbicara tentang server itu,
                    // bukan angka dari alamat sebelumnya.
                    UrlServer = _lisensi.UrlServer;
                    _angkaDariSumberIni = false;
                    await MuatKatalogAsync(paksa: true);

                    Beritahu(2, "Server lisensi lokal berjalan di " + _serverLokal.Alamat +
                        " — alamat ini yang dipakai halaman Langganan dan Pusat Administrasi.");
                    StatusMessage = "Server lisensi lokal berjalan.";
                }
                else
                {
                    // Gagal menyala: pilihan dikembalikan supaya kotak centang
                    // tidak menjanjikan server yang tidak ada.
                    AppPreferenceStore.SetServerLisensiLokalAktif(false);
                    OnPropertyChanged(nameof(ServerLokalAktif));
                    Beritahu(3, _serverLokal.GalatTerakhir ?? "Server lisensi lokal tidak bisa dinyalakan.");
                    StatusMessage = "Server lisensi lokal gagal dinyalakan.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalakan server lisensi lokal dari halaman Langganan");
                AppPreferenceStore.SetServerLisensiLokalAktif(false);
                OnPropertyChanged(nameof(ServerLokalAktif));
                Beritahu(3, "Server lisensi lokal gagal dinyalakan: " + ex.Message);
                StatusMessage = "Server lisensi lokal gagal dinyalakan.";
            }
            finally
            {
                SegarkanStatusServerLokal();
            }
        }

        /// <summary>
        /// Matikan server lokal sekarang juga. Pilihan "nyalakan bersama
        /// aplikasi" dibiarkan seperti yang dipilih operator, sehingga server
        /// menyala lagi pada pembukaan berikutnya bila memang diinginkan.
        /// </summary>
        private void HentikanServerLokal()
        {
            try
            {
                _serverLokal?.Matikan();
                Beritahu(2, "Server lisensi lokal dimatikan. Server menyala lagi saat aplikasi dibuka bila pilihannya tetap aktif.");
                StatusMessage = "Server lisensi lokal dimatikan.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mematikan server lisensi lokal dari halaman Langganan");
                Beritahu(3, "Server lisensi lokal tidak bisa dimatikan: " + ex.Message);
            }
            finally
            {
                SegarkanStatusServerLokal();
            }
        }

        private void SegarkanStatusServerLokal()
        {
            OnPropertyChanged(nameof(ServerLokalBerjalan));
            OnPropertyChanged(nameof(ServerLokalStatus));
            OnPropertyChanged(nameof(ServerLokalCatatan));
        }

        /// <summary>
        /// Buka pusat administrasi DI DALAM aplikasi, pada alamat server yang
        /// sama dengan alamat langganan. Penjagaan akun pusat diulang di sini
        /// (bukan hanya lewat visibilitas tombol) supaya perintah tidak bisa
        /// dijalankan dari sesi akun desa.
        /// </summary>
        private async Task BukaPusatAdministrasiAsync()
        {
            if (_navigasi is null || !Utilities.AkunPusat.SesiIniPusat)
            {
                Beritahu(3, "Halaman pusat administrasi hanya tersedia untuk akun pusat.");
                return;
            }

            string alamat = _lisensi.AlamatPusatAdministrasi();
            if (alamat.Length == 0)
            {
                Beritahu(3, "Alamat server lisensi belum diatur, jadi pusat administrasi belum bisa dibuka.");
                return;
            }

            try
            {
                var halaman = new SuDesApp.Wpf.Views.PusatAdministrasiView();
                _navigasi.Navigate(halaman);
                await halaman.MuatAsync(alamat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pusat administrasi {Alamat}", alamat);
                Beritahu(3, "Gagal membuka pusat administrasi: " + ex.Message);
            }
        }

        // =======================================================================
        // Pilihan paket (bulanan / 6 bulan / tahunan).
        // =======================================================================

        private string _paketTerpilih = "bulanan";

        /// <summary>
        /// Kartu paket di halaman. Daftar kodenya tetap (lihat
        /// <see cref="KatalogLangganan.Bawaan"/>) supaya kartu selalu ada isi,
        /// dan ANGKA harga serta masanya diisi dari katalog server begitu server
        /// terjangkau — sebelum itu kartu menampilkan keterangan tanpa rupiah,
        /// persis seperti perilaku lama.
        /// </summary>
        public IReadOnlyList<PaketOpsi> PilihanPaket { get; }

        /// <summary>Paket terpilih (diobserver lewat <see cref="PaketTerpilihOpsi"/>).</summary>
        public PaketOpsi? PaketTerpilihOpsi
        {
            get => PilihanPaket.FirstOrDefault(p => p.Kode == _paketTerpilih) ?? PilihanPaket[0];
            set
            {
                if (value == null) return;
                _paketTerpilih = value.Kode;
                OnPropertyChanged(nameof(PaketTerpilihOpsi));
                SegarkanQris();
            }
        }

        /// <summary>
        /// Paket yang akan dibeli: "bulanan", "enambulan", atau "tahunan".
        /// Nilainya ikut ke server saat membuat tagihan; harga TIDAK dibaca dari
        /// sini — server yang menentukan, supaya angka yang tampil dan yang
        /// ditagih tidak bisa berbeda.
        /// </summary>
        public string PaketTerpilih => _paketTerpilih;

        // =======================================================================
        // Pembayaran QRIS manual (QRIS statis merchant).
        // =======================================================================
        // Jalur ini TIDAK lewat gateway: pembeli memindai QRIS yang ditampilkan
        // di layar ini, lalu operator menandai lunas di pusat administer setelah
        // dana masuk. Karena itu halaman tidak pernah mengklaim langganan aktif
        // sendiri — kalimatnya selalu "aktif setelah admin menandai lunas".
        //
        // Gambar QR ditanam di rakitan aplikasi (terdaftar <Resource> di csproj),
        // bukan berkas di folder instalasi: gambar yang bisa ditukar dari disk
        // berarti pembayaran desa bisa dialihkan ke rekening lain tanpa jejak.

        /// <summary>
        /// Jalur resource QRIS di rakitan aplikasi (dipakai pemuat gambar dan uji).
        /// Isinya HANYA kode QR-nya — potongan dari poster QRIS milik merchant,
        /// sudah hitam-putih dan berzona-tenang sendiri — bukan poster lengkapnya.
        /// </summary>
        public const string NamaResourceQris = "Resources/Qris-Pembayaran.png";

        private bool _tampilQris;

        /// <summary>True saat panel QRIS terbuka di kartu paket.</summary>
        public bool TampilQris
        {
            get => _tampilQris;
            private set => SetProperty(ref _tampilQris, value);
        }

        private BitmapSource? _qrisGambar;

        /// <summary>
        /// Gambar QRIS, dimuat saat panel PERTAMA kali dibuka (bukan saat halaman
        /// dibuka) supaya pengguna yang tidak pernah membayar manual tidak perlu
        /// mendekode gambar ini. Null = gagal dimuat; panel tetap menampilkan
        /// nominal dan rinciannya.
        /// </summary>
        public BitmapSource? QrisGambar
        {
            get => _qrisGambar;
            private set => SetProperty(ref _qrisGambar, value);
        }

        /// <summary>
        /// Nomor admin dalam format internasional tanpa tanda, untuk tautan
        /// <c>wa.me</c>: 0851 1166 9951 → 6285111669951. Nomor sengaja TIDAK
        /// ditampilkan sebagai teks di halaman; pengguna menghubungi admin
        /// lewat tombol WhatsApp yang sudah ada.
        /// </summary>
        public const string NomorWaAdminWa = "6285111669951";

        /// <summary>
        /// NMID merchant yang WAJIB ada di dalam payload QRIS yang dibundel
        /// (tag 51, sub-tag 02).
        ///
        /// Konstanta ini yang dipegang uji <c>QrisPembayaranTests</c>: uji itu
        /// men-dekode gambar QRIS sungguhan lalu membandingkan NMID di dalamnya
        /// dengan nilai ini. Jadi mengganti berkas dengan QR merchant lain tidak
        /// bisa lolos diam-diam — uji gagal, bukan pembayaran desa yang pindah
        /// ke rekening lain.
        /// </summary>
        public const string QrisNmid = "ID1026608792580";

        /// <summary>
        /// Nama merchant di payload (tag 59) — inilah yang tampil di aplikasi
        /// bank/dompet digital pembeli, sedangkan "ARIEINO - FAMILY CLOTHING"
        /// hanya judul di poster yang tidak ikut ditampilkan lagi.
        /// </summary>
        public const string QrisNama = "arieino";

        /// <summary>
        /// Identitas penerima yang harus dicocokkan pembeli di aplikasi banknya.
        ///
        /// Disusun dari <see cref="QrisNama"/> dan <see cref="QrisNmid"/> supaya
        /// teks di layar tidak bisa menyimpang dari isi QR yang benar-benar
        /// dibundel: mengganti salah satunya tanpa yang lain membuat uji NMID
        /// gagal, bukan membuat nama penerima berbohong. WAJIB diperbarui
        /// bersamaan bila gambar <see cref="NamaResourceQris"/> diganti.
        /// </summary>
        public string QrisPenerima => $"{QrisNama} (NMID {QrisNmid}, Karawang)";

        /// <summary>Nominal menurut kartu paket terpilih (angka server bila sudah ada).</summary>
        public string QrisNominal => PaketTerpilihOpsi?.HargaTampil ?? "Harga dari server";

        /// <summary>
        /// Baris rincian yang dikirim pembeli ke admin sebagai bukti: kode
        /// aktivasi, paket, nominal, dan nama penerima. Kode aktivasi ikut karena
        /// QRIS statis tidak membawa nomor tagihan seperti gateway — itulah
        /// satu-satunya penanda desa yang bisa dicocokkan admin.
        /// </summary>
        public string QrisReferensi =>
            $"{(string.IsNullOrWhiteSpace(KodeAktivasi) ? "(kode aktivasi belum diisi)" : KodeAktivasi.Trim())} · " +
            $"{PaketTerpilihOpsi?.Label ?? "-"} · {QrisNominal} · {QrisPenerima}";

        /// <summary>Buka/tutup panel QRIS.</summary>
        public RelayCommand TampilkanQrisCommand { get; }

        /// <summary>Salin rincian pembayaran ke papan klip.</summary>
        public RelayCommand SalinRincianQrisCommand { get; }

        /// <summary>Buka WhatsApp admin (wa.me) dengan pesan bukti pembayaran terisi.</summary>
        public RelayCommand BukaWaAdminCommand { get; }

        /// <summary>
        /// Susun tautan <c>wa.me</c> untuk satu pesan. Statis supaya bisa diuji
        /// tanpa membangun ViewModel: nomor tujuan dan encoding pesan adalah
        /// satu aturan yang tidak boleh berbeda antara tombol dan uji.
        /// </summary>
        internal static string BangunTautanWa(string pesan) =>
            $"https://wa.me/{NomorWaAdminWa}?text={Uri.EscapeDataString(pesan)}";

        /// <summary>
        /// Pesan bukti pembayaran manual yang dikirim desa ke admin: identitas
        /// desa (kode aktivasi), paket, nominal, dan nama penerima — persis
        /// seperti <see cref="QrisReferensi"/> yang bisa disalin. Dua jalur
        /// mengirim rincian yang sama supaya admin tidak menerima dua versi.
        /// </summary>
        internal string PesanWaAdmin =>
            "Halo admin, saya ingin mengaktifkan/memperpanjang langganan SuDesApp.\n" +
            $"Rincian pembayaran: {QrisReferensi}.\n" +
            "Bukti pembayaran saya lampirkan. Terima kasih.";

        /// <summary>
        /// Buka WhatsApp admin di aplikasi/peramban bawaan dengan pesan yang
        /// sudah terisi. Kegagalan membuka (WhatsApp Desktop tidak ada dan
        /// peramban ditolak) tetap menyertakan tautan manual di banner, jadi
        /// pengguna tidak kehilangan jalan menghubungi admin.
        /// </summary>
        private void BukaWaAdmin()
        {
            var tautan = BangunTautanWa(PesanWaAdmin);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tautan,
                    UseShellExecute = true
                });
                Beritahu(2, "WhatsApp admin dibuka — kirim rincian dan bukti pembayaran. " +
                    "Langganan aktif setelah admin menandai lunas.");
                StatusMessage = "WhatsApp admin dibuka.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka WhatsApp admin {Tautan}", tautan);
                Beritahu(2, $"Buka manual: {tautan}. " +
                    "Sertakan rincian pembayaran berikut: " + QrisReferensi);
                StatusMessage = "WhatsApp tidak bisa dibuka otomatis — buka tautan manual di banner.";
            }
        }

        /// <summary>Umumkan ulang nominal &amp; rincian setelah paket/katalog berubah.</summary>
        private void SegarkanQris()
        {
            OnPropertyChanged(nameof(QrisNominal));
            OnPropertyChanged(nameof(QrisReferensi));

            // Panel sedang terbuka: kalimat pengarahnya memuat nominal, jadi ia
            // harus ikut berubah. Tanpa ini, desa yang mengganti paket setelah
            // panel terbuka masih membaca nominal LAMA di statusbar — dan
            // nominal itu yang berisiko ditransfer, karena QR-nya sama
            // (QRIS statis tidak membawa nominal).
            if (TampilQris)
            {
                TampilkanPesanQris();
            }
        }

        /// <summary>
        /// Satu-satunya tempat kalimat pengarah QRIS disusun — dipakai saat panel
        /// dibuka dan setiap kali nominalnya berubah, supaya keduanya tidak bisa
        /// berbeda. Kode aktivasi yang kosong ikut diingatkan di kalimat yang sama
        /// (bukan sebagai pesan terpisah), supaya pengingat itu tidak membuang
        /// petunjuk scan/nominal/WhatsApp di bawahnya.
        /// </summary>
        private void TampilkanPesanQris() => Beritahu(2,
            (string.IsNullOrWhiteSpace(KodeAktivasi)
                ? "Isi kode aktivasi terlebih dahulu — kode itulah yang dipakai admin untuk mencocokkan pembayaran dengan desa ini. "
                : "") +
            $"Scan QRIS di layar dengan aplikasi bank/e-wallet, masukkan nominal {QrisNominal}, " +
            "lalu hubungi admin via WhatsApp — tombol di panel. " +
            "Langganan aktif setelah admin menandai lunas.");

        /// <summary>
        /// Satu kartu paket di halaman. Isinya proyeksi dari
        /// <see cref="PaketLanggananKlien"/> milik Core: kelas ini hanya membuatnya
        /// dapat diikat XAML, sedangkan aturan harga dan teksnya ada di Core.
        /// </summary>
        public sealed class PaketOpsi : ObservableObject
        {
            private PaketLanggananKlien _paket;
            private string _catatan;
            private bool _disorot;
            private bool _sedangAktif;

            public PaketOpsi(PaketLanggananKlien paket)
            {
                _paket = paket ?? throw new ArgumentNullException(nameof(paket));
                _catatan = paket.CatatanCadangan;
            }

            /// <summary>Kode yang dikirim ke server ("bulanan" / "enambulan" / "tahunan").</summary>
            public string Kode => _paket.Kode;

            /// <summary>Teks yang dilihat pengguna.</summary>
            public string Label => _paket.Label;

            public override string ToString() => Label;

            /// <summary>Angka resmi server ("Rp100.000") atau teks jujur tanpa angka.</summary>
            public string HargaTampil => _paket.HargaTampil;

            /// <summary>
            /// Harga per hari dari angka resmi server, mis. "≈ Rp3.288/hari" —
            /// pembanding jujur antar paket yang berbeda durasi. Kosong sampai
            /// harga dan hari dari server sama-sama diketahui.
            /// </summary>
            public string HargaPerHariTampil => _paket.HargaPerHariTampil;

            /// <summary>True bila harga per hari bisa ditampilkan (angka server lengkap).</summary>
            public bool AdaHargaPerHari => _paket.AdaHargaPerHari;

            /// <summary>
            /// Masa aktif — angka dari server bila sudah ada; sebelum itu bawaan
            /// yang sama dengan katalog server, supaya tampilan tidak berubah
            /// setelah angka resmi datang.
            /// </summary>
            public string Durasi => _paket.DurasiTampil;

            /// <summary>Kalimat pembanding — dihitung dari harga asli server.</summary>
            public string Catatan
            {
                get => _catatan;
                private set => SetProperty(ref _catatan, value);
            }

            /// <summary>
            /// True untuk paket paling hemat (harga per hari terendah dari angka
            /// server). Ditandai "PALING HEMAT" di kartu supaya pengguna tidak
            /// perlu menghitung sendiri mana yang paling untung.
            /// </summary>
            public bool Disorot
            {
                get => _disorot;
                private set => SetProperty(ref _disorot, value);
            }

            /// <summary>True bila paket ini yang sedang aktif pada langganan berjalan.</summary>
            public bool SedangAktif
            {
                get => _sedangAktif;
                set => SetProperty(ref _sedangAktif, value);
            }

            /// <summary>Terapkan hasil katalog Core (angka, catatan, penanda hemat) ke kartu ini.</summary>
            internal void TerapkanKatalog(PaketLanggananKlien paket, string catatan, bool disorot)
            {
                bool angkaBerubah = _paket.Harga != paket.Harga || _paket.Hari != paket.Hari;
                _paket = paket;
                Catatan = catatan;
                Disorot = disorot;

                if (angkaBerubah)
                {
                    OnPropertyChanged(nameof(HargaTampil));
                    OnPropertyChanged(nameof(HargaPerHariTampil));
                    OnPropertyChanged(nameof(AdaHargaPerHari));
                    OnPropertyChanged(nameof(Durasi));
                }
            }
        }

        // =======================================================================
        // Katalog harga dari server (GET /paket) — penyusunannya ada di
        // LanggananKlien; halaman ini hanya menerapkan hasilnya.
        // =======================================================================

        private string _keteranganHarga =
            "Harga resmi ditetapkan server. Bila kartu belum menampilkan angka, server belum terjangkau — " +
            "harga tetap dikonfirmasi server saat tagihan dibuat.";

        /// <summary>
        /// Kalimat di atas kartu paket: berubah setelah katalog server termuat,
        /// supaya pengguna tahu apakah angka di kartu sudah angka resmi.
        /// </summary>
        public string KeteranganHarga
        {
            get => _keteranganHarga;
            private set => SetProperty(ref _keteranganHarga, value);
        }

        /// <summary>
        /// Benar bila angka di kartu berasal dari sumber yang sedang dipakai
        /// (katalog server atau salinan resminya). Dipakai hanya untuk memilih
        /// keterangan: setelah server diganti, angka lama di kartu tidak boleh
        /// membuat pesan "server belum terjangkau" tampak tidak perlu.
        /// </summary>
        private bool _angkaDariSumberIni;

        /// <summary>
        /// Muat katalog harga dari server lalu terapkan ke kartu. Kegagalan
        /// jaringan di sini wajar (halaman masih berguna offline), jadi tidak
        /// pernah ditampilkan sebagai galat.
        /// </summary>
        private async Task MuatKatalogAsync(bool paksa = false)
        {
            HasilKatalog hasil;
            try
            {
                hasil = await _klien.MuatDariServerAsync(paksa);
            }
            catch (Exception ex)
            {
                // Konstruktor halaman memanggil ini tanpa ditunggu
                // (fire-and-forget), jadi pengecualian yang lolos akan hilang
                // sebagai unobserved exception tanpa jejak di layar. Kegagalan
                // jaringan di sini wajar — cukup dicatat, lalu keadaan server
                // ditandai seperti kegagalan biasa.
                _logger.LogWarning(ex, "Pemuatan katalog harga paket gagal");
                TetapkanKeadaanServer(KeadaanServer.TidakTerjangkau);
                return;
            }

            PerbaruiKeadaanServer(hasil);

            if (hasil.Katalog != null)
            {
                TerapkanKatalog(hasil.Katalog);
                _angkaDariSumberIni = hasil.Katalog.AdaHarga;
                KeteranganHarga =
                    "Harga di bawah diambil langsung dari katalog server — angka yang sama persis dengan " +
                    "yang akan ditagih saat pembayaran.";
                return;
            }

            // Bila kartu SUDAH punya angka (dari salinan tersimpan), jangan
            // menimpanya dengan pesan "server belum terjangkau" — angka itu
            // lebih berguna daripada keterangan kegagalan.
            if (hasil.Hasil == HasilMuatKatalog.Gagal && !_angkaDariSumberIni)
            {
                KeteranganHarga =
                    "Server lisensi belum bisa dijangkau, jadi kartu belum menampilkan angka. " +
                    "Harga tetap dikonfirmasi server saat tagihan dibuat.";
            }
        }

        /// <summary>
        /// Terjemahkan hasil panggilan katalog menjadi keadaan chip server.
        /// Hanya panggilan yang BENAR-BENAR terjadi yang mengubah keadaan:
        /// percobaan yang dilewati karena jeda coba ulang (setelah gagal) tidak
        /// mengubah apa pun, supaya chip tidak berkedip "tidak terjangkau"
        /// hanya karena permintaannya sengaja ditahan.
        /// </summary>
        private void PerbaruiKeadaanServer(HasilKatalog hasil)
        {
            switch (hasil.Hasil)
            {
                case HasilMuatKatalog.Server:
                    TetapkanKeadaanServer(KeadaanServer.Terhubung);
                    break;

                case HasilMuatKatalog.Gagal:
                    TetapkanKeadaanServer(KeadaanServer.TidakTerjangkau);
                    break;

                case HasilMuatKatalog.Kosong:
                    // Server menjawab tetapi katalognya tidak bisa dipakai — atau
                    // memang belum ada alamat untuk dihubungi. Keduanya bukan
                    // "terhubung", tetapi jangan juga disebut "belum diatur"
                    // bila alamatnya sudah diisi.
                    TetapkanKeadaanServer(string.IsNullOrWhiteSpace(_urlServer)
                        ? KeadaanServer.BelumDiatur
                        : KeadaanServer.TidakTerjangkau);
                    break;

                // HasilMuatKatalog.Dilewati: tidak ada panggilan → keadaan terakhir tetap.
            }
        }

        /// <summary>
        /// Tampilkan angka dari salinan katalog terakhir (tanpa jaringan) supaya
        /// kartu sudah berisi sejak halaman dibuka walau server lisensi
        /// belum/tidak bisa dihubungi.
        /// </summary>
        private void TampilkanKatalogTersimpan()
        {
            var salinan = _klien.MuatKatalogTersimpan();
            if (salinan is null) return;

            TerapkanKatalog(salinan);
            _angkaDariSumberIni = salinan.AdaHarga;
            KeteranganHarga =
                "Harga di bawah adalah angka terakhir yang dikonfirmasi server. " +
                "Angka yang benar-benar ditagih tetap ditentukan server saat pembayaran.";
        }

        /// <summary>
        /// Salin angka &amp; kebijakan katalog (catatan kartu dan penanda "paling
        /// hemat") ke kartu di layar. Aturannya ada di
        /// <see cref="KatalogLangganan"/>, jadi halaman ini tidak punya salinan
        /// aturan harga yang bisa menyimpang.
        /// </summary>
        private void TerapkanKatalog(KatalogLangganan katalog)
        {
            foreach (var opsi in PilihanPaket)
            {
                var kartu = katalog.Cari(opsi.Kode);
                if (kartu is null) continue;

                opsi.TerapkanKatalog(kartu, katalog.Catatan(kartu), katalog.KodeDisorot == opsi.Kode);
            }

            // Nominal QRIS mengikuti kartu paket terpilih, jadi ia ikut berubah
            // saat angka resmi server (atau salinan tersimpan) tiba.
            SegarkanQris();
        }

        // =======================================================================
        // Tindakan.
        // =======================================================================

        /// <summary>
        /// Jalankan satu tindakan halaman sebagai SATU operasi sibuk: klik beruntun
        /// diabaikan, tombol lain nonaktif selama berjalan, dan penanda sibuk selalu
        /// dilepas walau tindakannya gagal.
        ///
        /// Dulu potongan penjagaan ini disalin di setiap perintah, dan "Simpan &amp; Uji"
        /// sempat melewatkannya: dua klik beruntun menjalankan dua pemuatan katalog +
        /// dua cek status yang saling menimpa, sementara tombol Bayar/Trial tetap bisa
        /// ditekan di tengah pengujian alamat. Satu tempat ini yang memiliki aturannya.
        /// </summary>
        private async Task JalankanSibukAsync(Func<Task> tindakan)
        {
            if (LanggananSibuk) return;
            LanggananSibuk = true;
            try
            {
                await tindakan();
            }
            finally
            {
                LanggananSibuk = false;
            }
        }

        /// <summary>
        /// Satu-satunya cara menulis banner untuk pesan sementara (galat simpan,
        /// petunjuk QRIS, hasil panduan): nada dan kalimat selalu berubah bersama,
        /// jadi tidak ada banner merah yang berbunyi seolah berhasil.
        /// </summary>
        private void Beritahu(int nada, string keterangan)
        {
            LanggananTone = nada;
            LanggananInfo = keterangan;
        }

        private async Task CekLanggananAsync()
        {
            try
            {
                _lisensi.KodeAktivasi = KodeAktivasi?.Trim();
                StatusMessage = "Memeriksa langganan ke server…";
                var hasil = await _lisensi.CekAsync();
                // Terapkan dulu ke penjaga fitur, lalu tampilkan — supaya lencana
                // keadaan (Demo / Full Service / Baca-saja) yang dihitung dari
                // penjaga ikut mencerminkan hasil yang baru saja diterima,
                // bukan status dari cek sebelumnya.
                _penjaga.Terapkan(hasil);
                TampilkanStatusLangganan(hasil);

                // Aktivasi (trial atau kode langganan yang sah) juga menjadi titik
                // pencatatan persetujuan — jaring pengaman bila preferensi pengguna
                // terhapus setelah login sehingga dialog awal tidak sempat muncul.
                // Catatan yang sudah ada tidak pernah ditimpa: waktu persetujuan
                // pertama tetap utuh sebagai bukti.
                if (hasil.Aktif)
                {
                    CatatPersetujuanSaatAktivasi();
                }

                // Cek Status SELALU memuat ulang katalog harga (melewati jeda
                // kegagalan): server baru saja terbukti terjangkau, dan pengguna
                // sengaja menekan tombol ini.
                await MuatKatalogAsync(paksa: true);
                StatusMessage = "Status langganan diperbarui.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cek langganan gagal");
                Beritahu(3, "Gagal menghubungi server lisensi: " + ex.Message);
                StatusMessage = "Cek langganan gagal.";
            }
        }

        /// <summary>
        /// Catat persetujuan dokumen yang belum punya catatan sama sekali.
        /// Kegagalan hanya dicatat ke log — aktivasi langganan tetap berhasil,
        /// dan dialog persetujuan akan muncul lagi saat aplikasi dibuka.
        /// </summary>
        private void CatatPersetujuanSaatAktivasi()
        {
            var hasil = PersetujuanDokumenService.CatatBilaBelumAda(DateTime.Now);
            if (!hasil.Berhasil)
            {
                _logger.LogWarning("Persetujuan dokumen saat aktivasi belum tercatat: {Pesan}", hasil.Pesan);
            }
        }

        /// <summary>
        /// Isi tombol "Simpan &amp; Cek": kode disimpan sebagai preferensi, lalu
        /// statusnya langsung diperiksa — keduanya satu operasi sibuk.
        /// </summary>
        private async Task SimpanLaluCekAsync()
        {
            AppPreferenceStore.SetKodeAktivasi(KodeAktivasi?.Trim() ?? string.Empty);
            _statusSesaat.Tampilkan("Tersimpan — kode aktivasi");
            await CekLanggananAsync();
        }

        /// <summary>
        /// Simpan alamat server lisensi (preferensi bertahan antarjalankan),
        /// terapkan seketika ke LisensiManager, lalu buktikan koneksinya dengan
        /// langsung mengecek status bila kode aktivasi sudah terisi.
        /// </summary>
        private async Task SimpanServerAsync()
        {
            var url = (UrlServer ?? string.Empty).Trim().TrimEnd('/');
            if (url.Length == 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Beritahu(3, "Alamat server belum sah — isi alamat lengkap yang diawali http:// atau https://.");
                StatusMessage = "Alamat server tidak disimpan.";
                return;
            }

            // Penjagaan kedua di luar binding read-only: jangan pernah menulis
            // preferensi yang berbeda dari alamat produksi yang sudah dikunci.
            if (_lisensi.UrlServerTerkunci
                && !string.Equals(url, PenentuAlamatLisensi.Rapikan(_lisensi.UrlServer), StringComparison.Ordinal))
            {
                Beritahu(3, "Alamat server dikunci oleh konfigurasi aplikasi dan tidak bisa diganti dari halaman ini.");
                StatusMessage = "Alamat server tidak disimpan.";
                UrlServer = _lisensi.UrlServer;
                return;
            }

            // Alamat yang dikunci konfigurasi tidak ditulis ke preferensi:
            // kalau build berikutnya kembali ke loopback, preferensi lama tidak
            // boleh menghidupkan lagi alamat produksi yang sudah usang.
            if (!_lisensi.UrlServerTerkunci)
            {
                AppPreferenceStore.SetUrlServerLisensi(url);
            }
            _lisensi.UrlServer = url;
            UrlServer = url;
            _statusSesaat.Tampilkan("Tersimpan — server lisensi");

            // Alamat server baru = katalog harga baru; muat ulang kartu paket
            // sekarang juga (paksa, lewati jeda kegagalan) supaya angka di layar
            // tidak tetap berasal dari server lama.
            _angkaDariSumberIni = false;
            await MuatKatalogAsync(paksa: true);
            StatusMessage = "Alamat server disimpan — katalog harga diuji ulang.";

            // "Simpan & Uji" juga membuktikan langganan memakai alamat baru.
            // Pemanggilnya (JalankanSibukAsync) menahan penanda sibuk sampai di
            // sini, jadi tidak ada tombol lain yang menyela di tengah pengujian.
            if (!string.IsNullOrWhiteSpace(KodeAktivasi))
            {
                await CekLanggananAsync();
            }
        }

        /// <summary>
        /// Mulai masa uji coba 30 hari dari halaman ini — TANPA memaksa kode
        /// aktivasi, sama seperti popup saat aplikasi dibuka.
        ///
        /// Aturannya satu dan dipakai bersama (<see cref="PenjagaLisensi.MulaiPercobaanAsync"/>):
        /// bila kode sudah diisi dan server terjangkau, trial dicatat SERVER
        /// (sekali per desa, token bertanda tangan); bila kode belum ada atau
        /// server tidak terjangkau, trial LOKAL yang dipakai supaya desa tetap
        /// bisa mencoba saat offline. Server yang menjawab MENOLAK tidak diberi
        /// cadangan lokal — dua jalur trial tidak boleh dijumlahkan.
        /// </summary>
        private async Task MulaiTrialAsync()
        {
            // Trial lokal yang sudah dipakai menutup trial server juga: dua jalur
            // trial yang sama-sama 30 hari tidak boleh menjadi 60 hari.
            if (_penjaga.TrialPernahDimulai)
            {
                Beritahu(2, "Masa uji coba 30 hari sudah pernah dipakai di komputer ini. " +
                    "Selesaikan pembayaran lisensi untuk melanjutkan.");
                StatusMessage = "Trial tidak diaktifkan.";
                return;
            }

            try
            {
                // Kode yang sudah diketik (belum disimpan) ikut dipakai supaya
                // trial server bisa dipilih saat server memang terjangkau.
                if (!string.IsNullOrWhiteSpace(KodeAktivasi))
                {
                    _lisensi.KodeAktivasi = KodeAktivasi!.Trim();
                }

                StatusMessage = "Menyiapkan masa uji coba 30 hari…";

                var percobaan = await _penjaga.MulaiPercobaanAsync();
                var (judul, pesan) = PenjagaLisensi.JelaskanPercobaan(percobaan);
                if (percobaan.BerlakuSampai is DateTime sampai)
                {
                    pesan += $" Berlaku sampai {sampai.ToLocalTime():dd MMM yyyy}.";
                }

                bool ditolak = percobaan.Asal
                    is AsalPercobaan.DitolakServer or AsalPercobaan.SudahPernahTrial;

                // Tampilkan status terbaru: trial server sudah membawa token,
                // trial lokal belum — banner memakai ringkasan trial (lihat
                // TampilkanStatusLangganan) supaya tidak terbaca "Tidak aktif"
                // padahal menu sudah terbuka.
                var status = _penjaga.Status ?? await _lisensi.CekAsync();
                _penjaga.Terapkan(status);
                TampilkanStatusLangganan(status);

                // Banner di atas sudah menampilkan keadaan (trial/aktif). Yang
                // hanya terbaca di sini: ALASAN penolakan server, dan tanggal
                // berakhir hasil percobaan.
                if (ditolak)
                {
                    Beritahu(2, judul + " — " + pesan);
                }

                StatusMessage = ditolak
                    ? "Trial tidak diaktifkan."
                    : percobaan.BerlakuSampai is DateTime berakhir
                        ? $"{judul} s.d. {berakhir.ToLocalTime():dd MMM yyyy}."
                        : judul + ".";

                if (status.Aktif)
                {
                    CatatPersetujuanSaatAktivasi();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Aktifkan trial gagal");
                Beritahu(3, "Gagal menyiapkan trial: " + ex.Message);
                StatusMessage = "Trial gagal diaktifkan.";
            }
        }

        /// <summary>
        /// Buat tagihan pembayaran. Dua jalur, sama seperti aturan server:
        /// <list type="number">
        /// <item>kode aktivasi terisi = TERPANJANG — desa yang sudah punya kode
        /// memperpanjang masa aktifnya;</item>
        /// <item>kode kosong = BERLANGGANAN AWAL — nama desa diambil dari profil
        /// desa lokal, dan server memberi kode aktivasi baru; kode itu disimpan
        /// ke preferensi seketika supaya tidak hilang, lalu layak untuk
        /// aktivasi setelah pembayaran lunas.</item>
        /// </list>
        /// Paket ikut dikirim; nominal TIDAK — server yang menentukan harga
        /// supaya tidak bisa dimanipulasi dari sisi klien.
        /// </summary>
        private async Task BayarLanggananAsync()
        {
            string? namaDesa = null;
            if (string.IsNullOrWhiteSpace(KodeAktivasi))
            {
                // Berlangganan awal: tanpa kode, nama desa wajib. Sumbernya
                // profil desa lokal (Pengaturan aplikasi), bukan ketikan ulang.
                namaDesa = (await AmbilNamaDesaProfilAsync()).Trim();
                if (namaDesa.Length == 0)
                {
                    Beritahu(2,
                        "Desa belum punya kode aktivasi. Isi nama desa di Pengaturan Aplikasi " +
                        "terlebih dahulu, lalu tekan Bayar lagi — kode aktivasi akan diterbitkan otomatis " +
                        "setelah tagihan dibuat.");
                    return;
                }
            }

            try
            {
                var hasil = await _klien.BuatTagihanAsync(PaketTerpilih, KodeAktivasi, namaDesa);
                if (hasil.Tagihan is null)
                {
                    Beritahu(3, $"Gagal membuat tagihan (HTTP {hasil.StatusHttp}).");
                    return;
                }

                // Kode baru dari berlangganan awal: simpan SEKETIKA dan tampil di
                // halaman ini (kotak Kode Aktivasi yang sama), supaya desa tidak
                // kehilangan aksesnya sebelum sempat membuat catatan.
                if (!string.IsNullOrWhiteSpace(hasil.KodeAktivasi) &&
                    !string.Equals(hasil.KodeAktivasi, KodeAktivasi, StringComparison.Ordinal))
                {
                    KodeAktivasi = hasil.KodeAktivasi;
                    AppPreferenceStore.SetKodeAktivasi(hasil.KodeAktivasi);
                    Beritahu(2,
                        $"KODE AKTIVASI BARU: {hasil.KodeAktivasi} — telah disimpan otomatis. " +
                        "Aktif setelah pembayaran lunas; tekan Cek Status setelahnya.");
                }

                var tagihan = hasil.Tagihan;

                // Tampilkan angka dari server: yang dicetak di sini adalah yang
                // benar-benar akan ditagihkan, bukan perkiraan lokal.
                var ringkas = tagihan.RingkasanTampil;
                if (!string.IsNullOrWhiteSpace(tagihan.LinkPembayaran) &&
                    Uri.TryCreate(tagihan.LinkPembayaran, UriKind.Absolute, out var uri))
                {
                    bool terbuka = CobaBukaPeramban(uri);
                    Beritahu(2, terbuka
                        ? $"Tagihan {ringkas} ({tagihan.OrderId}) dibuat — selesaikan pembayaran QRIS/VA di peramban, lalu tekan Cek Status."
                        : $"Tagihan {ringkas} ({tagihan.OrderId}) SUDAH dibuat di server, tetapi peramban tidak bisa dibuka otomatis. " +
                          $"Buka link ini di peramban lalu tekan Cek Status: {uri}");
                    StatusMessage = terbuka
                        ? "Tagihan dibuat — lanjutkan pembayaran di peramban."
                        : "Tagihan dibuat; peramban tidak bisa dibuka otomatis.";
                }
                else
                {
                    Beritahu(2, $"Tagihan {ringkas} ({tagihan.OrderId}) dibuat. Link pembayaran belum tersedia — hubungi admin.");
                    StatusMessage = "Tagihan dibuat tanpa link pembayaran.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Buat tagihan langganan gagal");
                Beritahu(3, "Gagal membuat tagihan: " + ex.Message);
                StatusMessage = "Gagal membuat tagihan.";
            }
        }

        /// <summary>
        /// Nama desa dari profil lokal untuk jalur berlangganan awal. Profil
        /// desa kosong dianggap wajar (instalasi baru yang belum diisi), jadi
        /// pemanggil menyaungkan kalimat panduan bila hasilnya kosong.
        /// </summary>
        private async Task<string> AmbilNamaDesaProfilAsync()
        {
            if (_pengaturanDesa is null)
            {
                return string.Empty;
            }

            try
            {
                var desa = await _pengaturanDesa.GetSettingsAsync().ConfigureAwait(false);
                return desa?.NamaDesa?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Profil desa tidak bisa dibaca untuk berlangganan awal");
                return string.Empty;
            }
        }

        /// <summary>
        /// Buka link pembayaran di peramban bawaan. Dipisah supaya kegagalannya
        /// tidak pernah terbaca sebagai "gagal membuat tagihan": saat ini terjadi
        /// tagihannya SUDAH tercatat di server, dan pesan yang salah membuat desa
        /// membuat tagihan kedua — lalu mungkin membayar dua kali.
        /// </summary>
        private bool CobaBukaPeramban(Uri alamat)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = alamat.ToString(),
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka peramban untuk link pembayaran {Alamat}", alamat);
                return false;
            }
        }

        /// <summary>
        /// Buka/tutup panel QRIS. Panel SELALU terbuka saat tombol ditekan — inilah
        /// isi tombolnya, "tampilkan QR"; dulu kode kosong hanya memunculkan
        /// peringatan dan QR tak pernah tampil. Kode aktivasi tetap diingatkan
        /// saat kosong (di kalimat yang sama, bukan menimpa — satu penulis
        /// banner), karena QRIS statis tidak membawa nomor tagihan: kode itulah
        /// satu-satunya penanda desa yang bisa dicocokkan admin.
        /// </summary>
        private void AlihTampilQris()
        {
            TampilQris = !TampilQris;
            if (!TampilQris) return;

            // Dimuat saat panel pertama dibuka saja: pengguna yang membayar lewat
            // tagihan otomatis tidak perlu mendekode gambar apa pun.
            QrisGambar ??= MuatGambarQris();

            TampilkanPesanQris();
        }

        /// <summary>
        /// Salin rincian ke papan klip supaya pengguna bisa mengirimkannya ke admin
        /// tanpa mengetik ulang. Papan klip bisa ditolak proses lain — itu bukan
        /// kegagalan halaman, jadi cukup dicatat di log.
        /// </summary>
        private void SalinRincianQris()
        {
            try
            {
                System.Windows.Clipboard.SetText(QrisReferensi);
                Beritahu(2, $"Rincian disalin — kirim ke admin via WhatsApp: {QrisReferensi}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin rincian QRIS ke papan klip");
                Beritahu(2, $"Rincian: {QrisReferensi}");
            }
        }

        /// <summary>
        /// Muat gambar QRIS dari resource rakitan. Alamat pertama memakai nama
        /// rakitan eksplisit (pola yang sama dengan AboutViewModel):
        /// <c>pack://application:,,,/Resources/…</c> menunjuk rakitan yang SEDANG
        /// berjalan — pada test host itu rakitan uji, bukan SuDesApp — sehingga
        /// tanpa nama rakitan gambarnya gagal dimuat di luar aplikasi.
        /// </summary>
        private BitmapSource? MuatGambarQris()
        {
            string[] alamat =
            {
                $"pack://application:,,,/SuDesApp;component/{NamaResourceQris}",
                $"pack://application:,,,/{NamaResourceQris}",
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
                    _logger.LogDebug(ex, "QRIS tidak termuat dari {Alamat}", satu);
                }
            }

            _logger.LogWarning("Gambar QRIS tidak ditemukan di resource aplikasi ({Berkas}).", NamaResourceQris);
            return null;
        }

        /// <summary>
        /// Terapkan satu status lisensi ke tampilan halaman: kalimat dan nada
        /// banner, petak ringkasan, tombol trial, serta penanda paket aktif.
        ///
        /// Kalimat dan angkanya datang dari <see cref="RingkasanLangganan"/>
        /// (Core) — halaman ini tidak menyusun aturan lisensi sendiri, hanya
        /// menyalin hasilnya. Selain teks, hanya dua hal yang diputuskan di sini:
        /// panel QRIS ditutup saat langganan aktif, dan kartu paket aktif ditandai.
        /// </summary>
        private void TampilkanStatusLangganan(StatusLisensi hasil)
        {
            // Trial lokal belum punya token: banner langganan biasa akan berbunyi
            // "Tidak aktif" padahal menu vital sudah terbuka. Ringkasan trial
            // menjelaskan sisa harinya dan tetap menawarkan pembayaran.
            _ringkasan = _penjaga.Keadaan == KeadaanLisensi.Trial
                ? RingkasanLangganan.DariTrial(_penjaga.TrialBerakhirUtc, _penjaga.SisaHariTrial)
                : RingkasanLangganan.Dari(hasil, KodeAktivasi);
            LanggananStatus = _ringkasan.Judul;
            Beritahu(_ringkasan.Nada, _ringkasan.Keterangan);
            OnPropertyChanged(nameof(SedangTrial));

            // Sudah aktif = tidak ada lagi yang perlu dibayar di layar ini; panel
            // QRIS yang masih terbuka dari percobaan sebelumnya hanya
            // membingungkan (nominalnya pun sudah tidak relevan).
            if (hasil.Aktif)
            {
                TampilQris = false;
            }

            // Tandai paket yang sedang aktif pada kartu pilihan paket supaya
            // pengguna tahu paket mana yang sedang dipakai sebelum memperpanjang.
            foreach (var opsi in PilihanPaket)
            {
                opsi.SedangAktif = hasil.Aktif && !string.IsNullOrWhiteSpace(hasil.Paket) &&
                    string.Equals(opsi.Kode, hasil.Paket, StringComparison.OrdinalIgnoreCase);
            }

            // Segarkan turunan status yang dipakai lencana header, tombol trial, dan petak KPI.
            OnPropertyChanged(nameof(KeadaanLabel));
            OnPropertyChanged(nameof(KeadaanTone));
            OnPropertyChanged(nameof(BacaSaja));
            OnPropertyChanged(nameof(TampilTombolTrial));
            OnPropertyChanged(nameof(PaketNama));
            OnPropertyChanged(nameof(BerlakuSampaiTampil));
            OnPropertyChanged(nameof(SisaHariTampil));
            OnPropertyChanged(nameof(TerakhirDiperiksaTampil));
        }

        // =======================================================================
        // Statusbar halaman.
        // =======================================================================

        /// <summary>Pesan tetap saat tidak ada aksi yang baru dikerjakan.</summary>
        private const string StatusSiap = "Status lisensi dapat diperiksa ulang kapan saja lewat tombol Cek Status.";

        private string _statusMessage = StatusSiap;
        private readonly StatusSesaat _statusSesaat;

        /// <summary>Pesan pada statusbar halaman (berubah sesaat setelah aksi).</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }
    }

/// <summary>
/// Langganan lemah halaman Langganan ke <see cref="SambunganServerLisensi.Berubah"/>.
///
/// Kenapa tidak langsung <c>sambungan.Berubah += ...</c>: halaman memakai
/// ViewModel transient, sedangkan perekam sambungan adalah singleton — langganan
/// kuat akan menahan setiap halaman yang pernah dibuka sampai aplikasi ditutup.
/// Kenapa bukan WeakEventManager generik WPF: event <c>Berubah</c> bertipe
/// <c>Action</c>, bukan pola <c>EventHandler&lt;TEventArgs&gt;</c> yang didukung
/// kelas itu. Helper ini memegang halaman secara lemah dan melepas langganannya
/// sendiri pada event berikutnya setelah halaman dibuang.
/// </summary>
internal sealed class LanggananSambunganLemah
{
    private readonly WeakReference<LanggananViewModel> _halaman;
    private readonly SambunganServerLisensi _sumber;

    private LanggananSambunganLemah(LanggananViewModel halaman, SambunganServerLisensi sumber)
    {
        _halaman = new WeakReference<LanggananViewModel>(halaman);
        _sumber = sumber;
        _sumber.Berubah += Tangani;
    }

    /// <summary>Daftarkan langganan lemah; umurnya mengikuti halaman.</summary>
    public static void Daftar(LanggananViewModel halaman, SambunganServerLisensi sumber) =>
        _ = new LanggananSambunganLemah(halaman, sumber);

    private void Tangani()
    {
        if (_halaman.TryGetTarget(out var halaman))
        {
            halaman.SambunganServerBerubah();
            return;
        }

        // Halaman sudah dibuang: jangan biarkan singleton menahan apa pun.
        _sumber.Berubah -= Tangani;
    }
}
}
