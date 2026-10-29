using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels;

/// <summary>Owns the feature sidebar, its active item, and navigation menu state.</summary>
public sealed class FeatureNavigationViewModel
{
    private readonly Func<string, Action> _resolveRoute;
    private readonly FormulirMenuService _formulirMenuService;
    private readonly ThemeService _themeService;
    private readonly IMessageService? _messageService;
    private readonly List<NavItem> _navigableItems = new();

    /// <summary>Keadaan kunci lisensi terakhir, disimpan supaya item yang dibuat belakangan (daftar formulir) ikut ditandai.</summary>
    private bool _lisensiTerkunci;
    private string _pesanTerkunci = string.Empty;

    /// <param name="messageService">
    /// Dipakai untuk melaporkan kegagalan yang tidak terlihat secara visual
    /// (mis. tema yang berlaku tetapi gagal disimpan). Opsional supaya
    /// pembuatan menu tetap bisa dilakukan tanpa layar dialog.
    /// </param>
    public FeatureNavigationViewModel(
        Func<string, Action> resolveRoute,
        FormulirMenuService formulirMenuService,
        ThemeService themeService,
        IMessageService? messageService = null)
    {
        _resolveRoute = resolveRoute ?? throw new ArgumentNullException(nameof(resolveRoute));
        _formulirMenuService = formulirMenuService ?? throw new ArgumentNullException(nameof(formulirMenuService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _messageService = messageService;
        BuildMenu();
    }

    public ObservableCollection<NavItem> MenuItems { get; } = new();
    public NavItem Beranda { get; private set; } = null!;
    public NavItem? Google { get; private set; }
    public NavItem? Formulir { get; private set; }
    public NavItem? PerangkatDesa { get; private set; }
    public NavItem? Langganan { get; private set; }

    /// <summary>Baris menu rahasia untuk sesi akun pusat; null untuk sesi lain.</summary>
    public NavItem? PusatAdministrasi { get; private set; }

    public NavItem? Pembaruan { get; private set; }
    public NavItem? PanduanAwal { get; private set; }
    public NavItem? IndeksDesa { get; private set; }
    public string JudulHalamanAktif { get; private set; } = "Beranda";

    public event Action<string>? ActivePageChanged;

    public void PilihBeranda() => TandaiAktif(Beranda);

    public void TandaiPanduanAwal() => TandaiAktif(PanduanAwal);

    public void RefreshFormulirSection()
    {
        var accordion = Formulir;
        if (accordion is null) return;

        accordion.Children.Clear();
        var templates = _formulirMenuService.GetTemplates();
        if (templates.Count == 0)
        {
            accordion.Children.Add(BuatTombol("Tambah Template", IkonMenu.Tambah, "formulir"));
        }
        else
        {
            foreach (var template in templates)
            {
                string route = "template-pdf:" + template;
                string display = template.Replace("_", " ").ToUpperInvariant();
                accordion.Children.Add(BuatTombol(display, IkonMenu.Blanko, route));
            }
        }

        PasangPenandaAktif(accordion.Children);
        // Item formulir yang dibuat setelah sidebar berdiri ikut ditandai
        // dengan keadaan lisensi yang sedang berlaku.
        KunciMenuSidebar.Terapkan(accordion.Children, _lisensiTerkunci, _pesanTerkunci);
    }

    /// <summary>
    /// Tandai menu vital di sidebar sesuai keadaan lisensi: gembok + tooltip
    /// tampil SEBELUM menu diklik, jadi pengguna tidak menemukan kunci baru
    /// setelah menekan menu. Daftar rutenya tetap milik <see cref="PenjagaLisensi"/>
    /// — di sini hanya menyalin keputusan itu ke tampilan, sekali jalan untuk
    /// seluruh pohon menu (akordeon ikut ditandai bila semua anaknya terkunci).
    /// </summary>
    public void TerapkanKunciLisensi(bool terkunci, string? pesan)
    {
        _lisensiTerkunci = terkunci;
        _pesanTerkunci = pesan ?? string.Empty;
        KunciMenuSidebar.Terapkan(MenuItems, _lisensiTerkunci, _pesanTerkunci);
    }

    public void TandaiAktif(NavItem? item)
    {
        foreach (var nav in _navigableItems) nav.IsActive = ReferenceEquals(nav, item);
        JudulHalamanAktif = string.IsNullOrWhiteSpace(item?.Title) ? "Beranda" : item!.Title;
        ActivePageChanged?.Invoke(JudulHalamanAktif);
    }

    /// <summary>
    /// Tulis server yang dituju menu Pusat Administrasi — beserta keadaan
    /// sambungannya — pada baris menu itu sendiri (baris kecil di bawah judulnya),
    /// supaya operator tahu alamat mana yang akan dibuka tanpa lebih dulu membuka
    /// halamannya, dan apakah servernya sedang bisa dihubungi.
    ///
    /// Keadaannya mengikuti panggilan SUNGGUHAN terakhir ke server (cek lisensi
    /// berkala, muat katalog harga): "Belum diuji" berarti memang belum ada
    /// percobaan sejak aplikasi dibuka — bukan "gagal" — dan "Terhubung" hanya
    /// muncul setelah server benar-benar menjawab, termasuk menjawab galat.
    /// Hanya host dan port yang tampil; jalur <c>/admin</c> dan token operator
    /// tidak pernah ikut ke sidebar. Tidak melakukan apa pun bila sesi ini bukan
    /// sesi pusat, karena baris menunya memang tidak dibuat.
    /// </summary>
    public void PerbaruiBarisPusatAdministrasi(string? alamat, KeadaanSambunganServer keadaan)
    {
        if (PusatAdministrasi is null) return;

        PusatAdministrasi.StatusText = string.IsNullOrWhiteSpace(alamat)
            ? "Server belum diatur"
            : $"Server: {HostTampil(alamat)} · {SambunganServerLisensi.Teks(keadaan)}";
    }

    /// <summary>
    /// "https://lisensi.contoh.go.id/admin" → "lisensi.contoh.go.id"; server uji
    /// menyebut port-nya ("localhost:5000") supaya tidak tertukar dengan produksi.
    /// </summary>
    private static string HostTampil(string alamat)
    {
        if (!Uri.TryCreate(alamat.Trim(), UriKind.Absolute, out var uri)) return alamat.Trim();

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private void BuildMenu()
    {
        Beranda = Tombol("Beranda", IkonMenu.Beranda, "beranda");
        Beranda.Description = "Ringkasan surat, pintasan cepat, dan aktivitas terbaru";
        MenuItems.Add(Beranda);
        Google = TambahTombol("Login dengan Google", IkonMenu.AkunGoogle, "google-login");

        Header("BUAT SURAT");
        TambahTombol("Register Surat", IkonMenu.Dokumen, "register-surat");

        var buatSurat = Accordion("Buat Surat Baru", IkonMenu.Tambah);
        foreach (var (label, template) in BuatSuratCatalog.MenuSurat)
            buatSurat.Children.Add(Tombol(label, IkonMenu.Dokumen, "surat:" + template));
        buatSurat.Children.Add(Tombol("Permohonan Rekening Koran", IkonMenu.Dokumen, "rekening-koran"));
        MenuItems.Add(buatSurat);
        var buatSendiri = TambahTombol("Buat Surat Sendiri", IkonMenu.Blanko, "template-surat");
        buatSendiri.Description = "Buat jenis surat sendiri dari nol atau dari berkas Word, lalu isi, pratinjau, dan cetak — tanpa mengubah menu surat bawaan.";
        TambahTombol("Daftar Hadir", IkonMenu.DaftarHadir, "daftar-hadir");

        var peraturan = Accordion("Surat Peraturan", IkonMenu.Peraturan);
        foreach (string jenis in new[] { "SK", "PERDES", "PERKADES" })
            peraturan.Children.Add(Tombol(jenis == "SK" ? "SK / Keputusan" : jenis, IkonMenu.Blanko, "keputusan:" + jenis));
        MenuItems.Add(peraturan);

        var agenda = Accordion("Surat Masuk/Keluar", IkonMenu.Agenda);
        agenda.Children.Add(Tombol("Surat Masuk", IkonMenu.SuratMasuk, "agenda:MASUK"));
        agenda.Children.Add(Tombol("Surat Keluar", IkonMenu.SuratKeluar, "agenda:KELUAR"));
        MenuItems.Add(agenda);

        var ntcr = Accordion("NTCR", IkonMenu.Orang);
        ntcr.Children.Add(new NavItem { Title = "Paket N1–N6 (satu pasangan)", Icon = IkonMenu.Dokumen,
            Action = Perintah("ntcr-paket"), Route = "ntcr-paket",
            Description = "Sekali isi data satu pasangan: seluruh blanko yang dicentang dibuat & dicetak dalam satu berkas PDF." });
        foreach (var blanko in NtcrKatalog.Semua)
            ntcr.Children.Add(new NavItem { Title = blanko.Judul, Icon = IkonMenu.Blanko,
                Action = Perintah("surat:" + blanko.NamaJenis), Route = "surat:" + blanko.NamaJenis,
                Description = $"{blanko.Kode} — {blanko.Keterangan}" });
        MenuItems.Add(ntcr);
        TambahTombol("Register NTCR", IkonMenu.Dokumen, "register-ntcr");
        TambahTombol("Tutup Buku Tahunan", IkonMenu.TutupBuku, "tutup-buku");
        TambahTombol("Verifikasi Surat", IkonMenu.Verifikasi, "verifikasi-surat");
        TambahTombol("Riwayat Aktivitas", IkonMenu.Riwayat, "riwayat");

        Header("FORMULIR");
        Formulir = Accordion("Formulir", IkonMenu.Blanko);
        MenuItems.Add(Formulir);
        RefreshFormulirSection();

        Header("DATA DESA");
        PerangkatDesa = TambahTombol("Perangkat Desa", IkonMenu.Orang, "perangkat-desa");
        PerangkatDesa.Description = "Struktur pemerintahan desa per jabatan: identitas, masa jabatan, arsip SK, dan penyusun SK Bupati.";
        var sotk = TambahTombol("SOTK Desa", IkonMenu.Halaman, "sotk-desa");
        sotk.Description = "Susun SOTK dan rancangan Perdes SOTK yang tersambung ke Buku Perdes.";
        var apbdes = TambahTombol("APBDES", IkonMenu.Keuangan, "apbdes");
        apbdes.Description = "Arsip dokumen APBDes: Awal, Perubahan, dan Pertanggungjawaban — impor berkas Word, sunting rincian, simpan.";
        IndeksDesa = TambahTombol("Indeks Desa (IDM)", IkonMenu.Pembangunan, "idm");
        IndeksDesa.Description = "Indeks Desa Membangun resmi Kemendesa: status, skor IKS/IKE/IKL, tren tahunan, rincian indikator, dan laporan PDF.";

        Header("DATA WARGA");
        TambahTombol("Data Warga", IkonMenu.Warga, "warga");
        TambahTombol("Laporan Penduduk", IkonMenu.Laporan, "laporan-penduduk");
        Header("PENGATURAN");
        TambahTombol("Pengaturan Aplikasi", IkonMenu.Pengaturan, "pengaturan-aplikasi");
        TambahTombol("Pengaturan Surat", IkonMenu.Pengaturan, "pengaturan-surat");
        // Pengaturan nomor surat berdiri sendiri sebagai halaman mandiri —
        // tidak lagi seksi di dalam halaman Pengaturan Aplikasi.
        var penomoranSurat = TambahTombol("Pengaturan Nomor Surat", IkonMenu.Dokumen, "penomoran-surat");
        penomoranSurat.Description = "Pola nomor penuh tiap jenis surat (awalan & urutan) pada database desa aktif.";
        TambahTombol("Pengaturan Formulir", IkonMenu.Pengaturan, "pengaturan-formulir");
        TambahTombol("Kelola Pengguna", IkonMenu.Pengguna, "kelola-pengguna");
        TambahTombol("Cadangkan & Pulihkan", IkonMenu.Cadangkan, "cadangkan-pulihkan");

        Header("LANGGANAN");
        Langganan = TambahTombol("Langganan", IkonMenu.Langganan, "langganan");
        Langganan.Description = "Status masa aktif lisensi desa, kode aktivasi, dan perpanjangan (bulanan/tahunan).";
        if (AkunPusat.SesiIniPusat)
        {
            PusatAdministrasi = TambahTombol("Pusat Administrasi", IkonMenu.PusatAdmin, "pusat-administrasi");
            PusatAdministrasi.Description = "Halaman operator server lisensi di dalam aplikasi; perlu ID dan token admin server.";
        }

        Header("BANTUAN");
        var tema = Accordion("Tema", IkonMenu.Tema, IkonMenu.Tema);
        var tersedia = ThemeService.GetAvailableThemes();
        var urutan = new[] { ThemeService.DefaultTheme }.Concat(tersedia.Where(t => t != ThemeService.DefaultTheme));
        foreach (string theme in urutan)
        {
            var swatch = new System.Windows.Media.SolidColorBrush(ThemeService.WarnaSwatch(theme));
            swatch.Freeze();
            tema.Children.Add(new NavItem
            {
                Title = ThemeService.GetDisplayName(theme), Description = ThemeService.GetDescription(theme),
                AccessibilityName = ThemeService.GetAccessibilityName(theme), TemaId = theme, Icon = "",
                SwatchBrush = swatch, IsTemaAktif = theme == _themeService.Current,
                Action = new AsyncRelayCommand(async () => { PilihTema(theme, tema.Children); await System.Threading.Tasks.Task.CompletedTask; })
            });
        }
        MenuItems.Add(tema);
        Pembaruan = TambahTombol("Pembaruan", IkonMenu.Pembaruan, "pembaruan");
        PanduanAwal = TambahTombol("Panduan Awal", IkonMenu.Panduan, "panduan-awal");
        PanduanAwal.Description = "Langkah mengisi data desa, pejabat, dan nomor surat";
        TambahTombol("Dokumentasi", IkonMenu.Dokumentasi, "dokumentasi");
        TambahTombol("API Desa", IkonMenu.Api, "api-desa");
        TambahTombol("Catatan Rilis", IkonMenu.Peraturan, "catatan-rilis");
        TambahTombol("Tentang", IkonMenu.Tentang, "tentang");
        TambahTombol("Keluar", IkonMenu.Keluar, "keluar");

        NavItem.RegisterExclusiveAccordions(MenuItems.Where(i => i.IsAccordion));
        PasangPenandaAktif(MenuItems);
    }

    private void PilihTema(string theme, IEnumerable<NavItem> kandidat)
    {
        bool tersimpan = _themeService.Apply(theme);
        foreach (var item in kandidat) item.IsTemaAktif = item.TemaId == theme;

        // Tema sudah berlaku tetapi pilihannya gagal disimpan: tanpa pesan ini
        // pengguna hanya melihat centang yang mengatakan "pilihan diterima",
        // padahal tema akan kembali seperti semula saat aplikasi dibuka lagi.
        if (!tersimpan)
        {
            _ = _messageService?.ShowWarningAsync(
                "Tema sudah berlaku untuk sesi ini, tetapi gagal disimpan — " +
                "tema akan kembali seperti semula saat aplikasi dibuka lagi.");
        }
    }        private void PasangPenandaAktif(IEnumerable<NavItem> items)
        {
            foreach (var item in items)
            {
                // Baris tema bukan halaman: membungkusnya dengan penanda halaman
                // aktif membuat judul jendela berubah menjadi nama tema (mis.
                // "Orbit") padahal halaman yang tampil tidak berpindah, dan
                // sorotan halaman sebelumnya ikut hilang.
                if (item.Action is not null && !item.IsAccordion && item.TemaId is null)
                {
                if (!_navigableItems.Contains(item)) _navigableItems.Add(item);
                if (item.Action is not PenandaAktifCommand)
                {
                    var target = item;
                    item.Action = new PenandaAktifCommand(item.Action, () => TandaiAktif(target));
                }
            }
            if (item.Children.Count > 0) PasangPenandaAktif(item.Children);
        }
    }

    private void Header(string title) => MenuItems.Add(new NavItem { Title = title, IsSectionHeader = true });

    private NavItem TambahTombol(string title, string icon, string route)
    {
        var item = Tombol(title, icon, route);
        MenuItems.Add(item);
        return item;
    }

    private NavItem BuatTombol(string title, string icon, string route) => Tombol(title, icon, route);

    private NavItem Tombol(string title, string icon, string route) => new()
    {
        Title = title, Icon = icon, Action = Perintah(route), Route = route
    };

    private ICommand Perintah(string route) => new AsyncRelayCommand(async () =>
    {
        _resolveRoute(route)();
        await System.Threading.Tasks.Task.CompletedTask;
    });

    private static NavItem Accordion(string title, string icon, string? groupIcon = null) => new()
    {
        Title = title, Icon = icon, GroupIcon = groupIcon ?? icon, IsAccordion = true, IsExpanded = false
    };
}
