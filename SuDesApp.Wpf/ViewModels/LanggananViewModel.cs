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
    public partial class LanggananViewModel : ObservableObject
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
            TempelKodeCommand = new RelayCommand(TempelKodeDariClipboard, () => !LanggananSibuk);
            HapusKodeCommand = new RelayCommand(HapusKode, () => !LanggananSibuk && !string.IsNullOrWhiteSpace(KodeAktivasi));
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
            HubungkanCommand = new AsyncRelayCommand(
                () => JalankanSibukAsync(HubungkanAsync), () => !LanggananSibuk);
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
        public string HeaderTitle => "AKTIVASI LISENSI";

        /// <summary>Penjelasan singkat di bawah judul header.</summary>
        public string HeaderSubtitle =>
            "Aktivasi lisensi desa melalui kode aktivasi manual. Data desa tetap tersimpan di komputer ini — " +
            "yang diurus di halaman ini hanya aktivasi lisensinya.";

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
