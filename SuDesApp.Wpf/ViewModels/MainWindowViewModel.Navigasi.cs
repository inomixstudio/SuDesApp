using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    // Modul navigasi MainWindowViewModel; state bersama tetap di berkas utama.
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Cache instance halaman yang sering dibuka (Pengaturan Aplikasi, Pengaturan
        /// Surat, Catatan Rilis). ViewModel-nya tetap dibuat baru setiap dibuka (agar
        /// nilainya segar), tetapi pohon visual halaman tidak dibangun ulang dari nol —
        /// yang membuat perpindahan menu terasa ringan.
        /// </summary>
        private readonly Dictionary<Type, UserControl> _cacheHalaman = new();

        private DaftarHadirViewModel? _daftarHadirViewModel;
        private FormulirViewModel? _formulirViewModel;
        private object? _currentView;
        private string _halamanAktif = "Beranda";

        /// <summary>ViewModel halaman yang judulnya sedang dipantau (lihat <see cref="IJudulHalaman"/>).</summary>
        private INotifyPropertyChanged? _viewJudulDipantau;

        private SidebarMode _modeSidebar = SidebarMode.Terbuka;

        private IdmViewModel? _idmViewModel;

        private IServiceProvider BuatScopeHalaman() => _navigation.BukaHalaman();

        private IServiceProvider BuatScopeBertahan() => _navigation.BukaBertahan();

        public ObservableCollection<NavItem> MenuItems => _featureNavigation.MenuItems;

        public object? CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        /// <summary>Nama halaman yang sedang dibuka — ditampilkan di title bar jendela.</summary>
        public string HalamanAktif
        {
            get => _halamanAktif;
            private set => SetProperty(ref _halamanAktif, value);
        }

        /// <summary>
        /// Sambungkan judul title bar ke halaman yang baru tampil. Halaman yang memakai
        /// <see cref="IJudulHalaman"/> (mis. wizard Template Surat) boleh mengganti judulnya
        /// sendiri kapan saja — perubahan terpantau lewat PropertyChanged.
        /// </summary>
        private void PantauJudulHalaman(object? view)
        {
            if (_viewJudulDipantau is not null)
            {
                _viewJudulDipantau.PropertyChanged -= HalamanJudulBerubah;
                _viewJudulDipantau = null;
            }

            if (view is IJudulHalaman && view is INotifyPropertyChanged notifier)
            {
                _viewJudulDipantau = notifier;
                notifier.PropertyChanged += HalamanJudulBerubah;
            }

            TerapkanJudulHalaman();
        }

        private void HalamanJudulBerubah(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(IJudulHalaman.JudulHalaman))
            {
                TerapkanJudulHalaman();
            }
        }

        /// <summary>Tulis judul title bar: judul halaman bila ada, selain itu nama menu terakhir.</summary>
        private void TerapkanJudulHalaman()
        {
            string? judul = (CurrentView as IJudulHalaman)?.JudulHalaman;
            HalamanAktif = string.IsNullOrWhiteSpace(judul) ? _featureNavigation.JudulHalamanAktif : judul!;
        }

        /// <summary>
        /// Tampilan sidebar yang sedang dipakai: terbuka penuh atau ikon saja. Template
        /// sidebar memakai <see cref="SidebarCiut"/> untuk menyembunyikan label.
        /// </summary>
        public SidebarMode ModeSidebar
        {
            get => _modeSidebar;
            private set
            {
                if (!SetProperty(ref _modeSidebar, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(SidebarCiut));
                OnPropertyChanged(nameof(SidebarTerbuka));
                OnPropertyChanged(nameof(SidebarModeTeks));
                OnPropertyChanged(nameof(ModeSidebarBerikut));
            }
        }

        /// <summary>True bila sidebar hanya menampilkan ikon (mode ikon saja).</summary>
        public bool SidebarCiut => _modeSidebar != SidebarMode.Terbuka;

        /// <summary>Kebalikan <see cref="SidebarCiut"/> — memudahkan binding Visibility.</summary>
        public bool SidebarTerbuka => _modeSidebar == SidebarMode.Terbuka;

        /// <summary>Keterangan tampilan yang sedang dipakai (tooltip tombol).</summary>
        public string SidebarModeTeks => SidebarModePrefs.Teks(_modeSidebar);

        /// <summary>Mode yang akan dipakai bila tombol ganti tampilan ditekan.</summary>
        public SidebarMode ModeSidebarBerikut => SidebarModePrefs.Berikutnya(_modeSidebar);

        /// <summary>Ubah mode sidebar (dipanggil jendela saat tombol ganti tampilan diklik).</summary>
        public void SetSidebarMode(SidebarMode mode) => ModeSidebar = mode;

        /// <summary>
        /// Kompatibilitas: ciut = mode ikon saja, lebar = tampilan penuh.
        /// </summary>
        public void SetSidebarCiut(bool ciut)
            => SetSidebarMode(ciut ? SidebarMode.IkonRingkas : SidebarMode.Terbuka);

        /// <summary>Buka halaman awal dan sorot item Beranda pada sidebar.</summary>
        public void BukaBeranda()
        {
            _featureNavigation.PilihBeranda();
            _ = ShowBerandaAsync();
        }

        private Action ResolveFeatureRoute(string route)
        {
            // Gerbang menu vital: selama lisensi belum aktif (instalasi baru
            // belum trial, trial habis, atau langganan berakhir), rute pembuatan/
            // perubahan data tidak dijalankan. Daftar rutenya ada di Core supaya
            // sama dengan aturan yang diuji.
            if (SuDesApp.Services.PenjagaLisensi.ApakahMenuVital(route) &&
                _penjaga is { Terkunci: true })
            {
                return () => _ = TolakMenuVitalAsync(route);
            }

            if (route.StartsWith("surat:", StringComparison.Ordinal))
            {
                string jenis = route["surat:".Length..];
                return () => _ = LoadSuratAsync(jenis);
            }
            if (route.StartsWith("keputusan:", StringComparison.Ordinal))
            {
                string jenis = route["keputusan:".Length..];
                return () => _ = ShowKeputusanAsync(jenis);
            }
            if (route.StartsWith("agenda:", StringComparison.Ordinal))
            {
                string jenis = route["agenda:".Length..];
                return () => _ = ShowAgendaAsync(jenis);
            }
            if (route.StartsWith("template-pdf:", StringComparison.Ordinal))
            {
                string nama = route["template-pdf:".Length..];
                return () => _ = ShowFormulirPdfAsync(nama);
            }
            return route switch
            {
                "beranda" => () => _ = ShowBerandaAsync(),
                "google-login" => () => _ = ShowGoogleLoginAsync(),
                "register-surat" => () => _ = ShowRegisterSuratAsync(),
                "rekening-koran" => () => _ = ShowRekeningKoranAsync(),
                "template-surat" => () => _ = ShowTemplateSuratAsync(),
                "daftar-hadir" => () => _ = ShowDaftarHadirAsync(),
                "ntcr-paket" => () => _ = ShowNtcrPaketAsync(),
                "register-ntcr" => () => _ = ShowRegisterNtcrAsync(),
                "tutup-buku" => ShowTutupBuku,
                "verifikasi-surat" => ShowVerifikasiSurat,
                "riwayat" => () => _ = ShowRiwayatAsync(),
                "perangkat-desa" => () => _ = ShowPerangkatDesaAsync(),
                "sotk-desa" => () => _ = ShowSotkDesaAsync(),
                "apbdes" => () => _ = ShowApbdesAsync(),
                "idm" => () => _ = ShowIdmAsync(),
                "warga" => () => _ = ShowWargaAsync(),
                "laporan-penduduk" => () => _ = ShowLaporanAsync(),
                "pengaturan-aplikasi" => () => _ = ShowPengaturanAplikasiAsync(),
                "pengaturan-surat" => () => _ = ShowSettingsAsync(),
                "penomoran-surat" => () => _ = ShowPengaturanNomorSuratAsync(),
                "pengaturan-formulir" => () => _ = ShowFormulirAsync(),
                "kelola-pengguna" => () => _ = ShowKelolaPenggunaAsync(),
                "cadangkan-pulihkan" => () => _ = ShowExImdbAsync(),
                "langganan" => () => _ = ShowLanggananAsync(),
                "pusat-administrasi" => () => _ = BukaPusatAdministrasiAsync(),
                "formulir" => () => _ = ShowFormulirAsync(),
                "pembaruan" => () => _ = ShowPembaruanAsync(),
                "panduan-awal" => () => _ = ShowPanduanAwalAsync(),
                "dokumentasi" => () => _ = ShowDokumentasiAsync(),
                "api-desa" => ShowApi,
                "catatan-rilis" => () => _ = ShowCatatanRilisAsync(),
                "tentang" => () => _ = ShowAboutAsync(),
                "keluar" => () => ExitRequested?.Invoke(),
                _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Rute fitur tidak dikenal.")
            };
        }

        private async Task ShowBerandaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<BerandaViewModel>();
                vm.BuatSuratDiminta += jenis => _ = LoadSuratAsync(jenis);
                vm.BukaHalamanDiminta += halaman => _ = BukaDariBerandaAsync(halaman);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Beranda");
                await _messageService.ShowErrorAsync("Gagal membuka Beranda: " + ex.Message);
            }
        }

        /// <summary>Teruskan aksi cepat dari Beranda ke halaman yang bersangkutan.</summary>
        private Task BukaDariBerandaAsync(string halaman) => halaman switch
        {
            "REGISTER" => ShowRegisterSuratAsync(),
            "RIWAYAT" => ShowRiwayatAsync(),
            "PENGATURAN" => ShowSettingsAsync(),
            "TEMPLATE" => ShowTemplateSuratAsync(),
            _ => Task.CompletedTask
        };

        private async Task ShowRegisterSuratAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<RegisterSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Register Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Register Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Langganan: status masa aktif lisensi, kode aktivasi, dan
        /// perpanjangan. Berdiri sendiri di navigasi menu utama (bukan bagian
        /// Pengaturan Aplikasi), karena urusan pembayaran dicari tersendiri.
        /// </summary>
        private async Task ShowLanggananAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<LanggananViewModel>();
                NavigasiHalaman<Views.LanggananView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Langganan");
                await _messageService.ShowErrorAsync("Gagal membuka halaman Langganan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman pusat administrasi server lisensi (rute /admin) DI DALAM
        /// aplikasi lewat WebView2, dengan tombol Buka di Browser sebagai cadangan.
        /// Halaman itu milik server — bukan halaman WPF — dan meminta token admin
        /// yang diketik operator di halaman itu sendiri.
        ///
        /// Alamatnya berasal dari <see cref="SuDesApp.Services.LisensiManager.AlamatPusatAdministrasi"/>:
        /// server yang sama dengan yang dipakai halaman Langganan, sehingga menu ini
        /// tidak bisa menunjuk server yang berbeda dari yang dipakai aplikasi; baris
        /// menu di sidebar menyebut alamat itu dan keadaan sambungannya sejak awal.
        /// </summary>
        private async Task BukaPusatAdministrasiAsync()
        {
            // Penjagaan kedua: walau menu hanya dibuat untuk akun pusat, tindakan
            // ini tidak pernah membuka halaman operator dari sesi akun desa.
            if (!AkunPusat.SesiIniPusat)
            {
                _logger.LogWarning("Pusat administrasi diminta dari sesi non-pusat; permintaan ditolak.");
                await _messageService.ShowWarningAsync(
                    "Halaman pusat administrasi hanya tersedia untuk akun pusat.");
                return;
            }

            string alamat = _lisensi?.AlamatPusatAdministrasi() ?? string.Empty;
            if (alamat.Length == 0)
            {
                await _messageService.ShowWarningAsync(
                    "Alamat server lisensi belum diatur, jadi pusat administrasi belum bisa dibuka. " +
                    "Isi alamat server di halaman Langganan (bagian LANGGANAN) lalu buka menu ini lagi.");
                return;
            }

            try
            {
                var halaman = new Views.PusatAdministrasiView();
                _navigation.Navigate(halaman);
                await halaman.MuatAsync(alamat);
                _logger.LogInformation("Pusat administrasi dibuka di dalam aplikasi: {Alamat}", alamat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pusat administrasi {Alamat}", alamat);
                await _messageService.ShowErrorAsync("Gagal membuka pusat administrasi: " + ex.Message);
            }
        }

        private async Task ShowPengaturanAplikasiAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PengaturanAplikasiViewModel>();
                NavigasiHalaman<Views.PengaturanAplikasiView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Aplikasi");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Aplikasi: " + ex.Message);
            }
        }

        /// <summary>Klik menu Pengaturan Nomor Surat → halaman mandirinya.</summary>
        private async Task ShowPengaturanNomorSuratAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PengaturanNomorSuratViewModel>();
                NavigasiHalaman<Views.PengaturanNomorSuratView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Nomor Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan Nomor Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Login dengan Google dari luar ViewModel ini. Lewat jalur
        /// ini scope halamannya ikut dikelola NavigationService — code-behind
        /// view tidak boleh membuat scope sendiri.
        /// </summary>
        public Task ShowGoogleLoginPublicAsync() => ShowGoogleLoginAsync();

        /// <summary>
        /// Buka halaman Langganan dari luar ViewModel (mis. popup startup yang
        /// mengingatkan trial habis). Memakai jalur navigasi yang sama dengan
        /// tombol chip Langganan — tidak ada jalur kedua.
        /// </summary>
        public Task ShowLanggananPublicAsync() => ShowLanggananAsync();

        /// <summary>
        /// Tampilkan halaman dengan pohon visual yang dipertahankan antar bukaan:
        /// instance halaman dibuat sekali lalu digunakan lagi untuk ViewModel baru,
        /// sehingga membuka kembali halaman tidak membangun ulang seluruh elemennya.
        /// </summary>
        private void NavigasiHalaman<TView>(object viewModel) where TView : UserControl, new()
        {
            if (!_cacheHalaman.TryGetValue(typeof(TView), out var halaman))
            {
                halaman = new TView();
                _cacheHalaman[typeof(TView)] = halaman;
            }

            halaman.DataContext = viewModel;
            _navigation.Navigate(halaman);
        }

        /// <summary>
        /// Penjaga alur pembuatan surat baru: mode baca-saja menolak membuka form
        /// dengan pesan yang mengarahkan ke halaman Langganan. True = boleh lanjut.
        /// </summary>
        private async Task<bool> BolehBuatSuratBaruAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(SuDesApp.Services.PenjagaLisensi.PesanAksesDikunci);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Dipanggil ketika pengguna menekan menu VITAL saat lisensi terkunci.
        /// Instalasi baru (Demo) ditawari memulai trial 30 hari langsung dari
        /// sini; trial habis / langganan berakhir diarahkan ke halaman Langganan.
        /// Menu tetap tidak dibuka sampai trial dimulai atau pembayaran selesai.
        /// </summary>
        private async Task TolakMenuVitalAsync(string route)
        {
            var penjaga = _penjaga;
            if (penjaga is null)
            {
                return;
            }

            if (penjaga.Keadaan == SuDesApp.Services.KeadaanLisensi.Demo)
            {
                bool mulai = await _messageService.ShowConfirmationAsync(
                    "Menu terkunci — mulai trial 30 hari",
                    SuDesApp.Services.PenjagaLisensi.PesanMulaiTrial);

                if (!mulai)
                {
                    await _messageService.ShowInfoAsync(
                        SuDesApp.Services.PenjagaLisensi.PesanBelumTrial);
                    return;
                }

                // Kode aktivasi sudah ada & server terjangkau → trial server
                // (sekali per desa, bertanda tangan). Trial lokal hanya dipakai
                // bila kode belum ada atau server tidak terjangkau.
                var percobaan = await penjaga.MulaiPercobaanAsync();
                var (judulPercobaan, pesanPercobaan) =
                    SuDesApp.Services.PenjagaLisensi.JelaskanPercobaan(percobaan);

                if (percobaan.BerlakuSampai is DateTime sampaiPercobaan)
                {
                    pesanPercobaan +=
                        $" Berlaku sampai {sampaiPercobaan.ToLocalTime():dd MMM yyyy}.";
                }

                bool ditolak = percobaan.Asal
                    is SuDesApp.Services.AsalPercobaan.DitolakServer
                    or SuDesApp.Services.AsalPercobaan.SudahPernahTrial;

                await _messageService.ShowMessageAsync(
                    pesanPercobaan,
                    judulPercobaan,
                    SuDesApp.Utilities.AppMessageButton.Ok,
                    ditolak
                        ? SuDesApp.Utilities.AppMessageIcon.Warning
                        : SuDesApp.Utilities.AppMessageIcon.Info);

                // Menu hanya dibuka bila gerbangnya benar-benar sudah terbuka —
                // trial yang ditolak server tidak boleh membuka halaman.
                if (!penjaga.Terkunci)
                {
                    ResolveFeatureRoute(route)();
                }
                return;
            }

            // Trial habis / langganan berakhir: menu tetap terkunci, pengguna
            // diarahkan menyelesaikan pembayaran.
            await _messageService.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanTrialHabis);
            await ShowLanggananAsync();
        }

        private async Task ShowRiwayatAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<RiwayatViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Riwayat Aktivitas");
                await _messageService.ShowErrorAsync("Gagal membuka Riwayat Aktivitas: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Perangkat Desa: tabel jabatan perangkat desa (Kepala
        /// Desa sampai Kadus) berisi orang yang memegangnya, lengkap dengan
        /// ekspor Excel dan cetak PDF. View dimuat App.xaml lewat DataTemplate
        /// untuk <see cref="PerangkatDesaViewModel"/>.
        ///
        /// Data dimuat SEBELUM navigasi, sama seperti halaman Data Warga: tanpa
        /// ini halaman terbuka dengan kartu KPI dan tabel yang masih kosong —
        /// halaman ini tidak punya tombol Muat Ulang, jadi pemuatan awal ini
        /// satu-satunya pembacaan database saat dibuka.
        /// </summary>
        private async Task ShowPerangkatDesaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<PerangkatDesaViewModel>();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Perangkat Desa");
                await _messageService.ShowErrorAsync("Gagal membuka Perangkat Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman SOTK Desa: dokumen Susunan Organisasi dan Tata Kerja
        /// Pemerintah Desa dari data perangkat tersimpan. Data dimuat SEBELUM
        /// navigasi seperti halaman Perangkat Desa supaya ringkasan keterisian
        /// langsung tampil saat halaman terbuka.
        /// </summary>
        private async Task ShowSotkDesaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<SotkDesaViewModel>();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka SOTK Desa");
                await _messageService.ShowErrorAsync("Gagal membuka SOTK Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Data Warga: pencarian, filter, mutasi status, Kartu
        /// Keluarga, dan ekspor. View dipetakan App.xaml lewat DataTemplate untuk
        /// <see cref="WargaViewModel"/>, jadi baris pertama langsung dimuat di sini
        /// supaya tabel tidak tampak kosong saat halaman terbuka.
        ///
        /// Navigasikan lebih dulu agar halaman Data Warga langsung tampak; daftar
        /// kemudian dimuat asinkron. Rekapitulasi mengikuti di latar belakang.
        /// </summary>
        private async Task ShowWargaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<WargaViewModel>();
                _navigation.Navigate(vm);
                await vm.LoadAsync(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Data Warga");
                await _messageService.ShowErrorAsync("Gagal membuka Data Warga: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Laporan Penduduk: rekapitulasi yang bisa dicetak PDF atau
        /// disimpan Excel dari satu sumber angka yang sama.
        /// </summary>
        private async Task ShowLaporanAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<LaporanViewModel>();
                await vm.LoadAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Laporan Penduduk");
                await _messageService.ShowErrorAsync("Gagal membuka Laporan Penduduk: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Kelola Pengguna: daftar akun, peran, dan reset kata sandi.
        /// Hanya Administrator yang boleh memakainya (ditegakkan PenggunaService).
        /// </summary>
        private async Task ShowKelolaPenggunaAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<KelolaPenggunaViewModel>();
                await vm.MuatAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Kelola Pengguna");
                await _messageService.ShowErrorAsync("Gagal membuka Kelola Pengguna: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Tutup Buku Tahunan: status setiap tahun, verifikasi nomor,
        /// dan rekap yang bisa dicetak. MuatCommand dijalankan lewat perintahnya
        /// karena pemuatan di halaman ini memang dipicu dari tombol Muat.
        /// </summary>
        private void ShowTutupBuku()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<TutupBukuTahunViewModel>();
                vm.MuatCommand.Execute(null);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Tutup Buku Tahunan");
                _ = _messageService.ShowErrorAsync("Gagal membuka Tutup Buku Tahunan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman APBDES: rancangan anggaran dan realisasi per jenis,
        /// tersimpan lokal. Terbuka untuk semua peran.
        /// </summary>
        private async Task ShowApbdesAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ApbdesViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka APBDES");
                await _messageService.ShowErrorAsync("Gagal membuka APBDES: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman IDM (Indeks Desa Membangun): skor IKS/IKE/IKL, IDM, dan rincian
        /// indikator resmi untuk desa pada Pengaturan Surat. Halaman hanya
        /// menampilkan data idm.kemendesa.go.id, jadi pengambilan datanya dimulai
        /// setelah halaman tampil supaya pengguna melihat keadaan "memuat".
        ///
        /// Instance VM disimpan agar tidak memuat ulang data setiap kali halaman
        /// dibuka — hanya sekali saat aplikasi berjalan. Untuk memuat ulang, pengguna
        /// bisa menekan tombol "Muat ulang" di halaman tersebut.
        /// </summary>
        private async Task ShowIdmAsync()
        {
            try
            {
                // Gunakan instance yang sudah ada jika masih tersedia
                if (_idmViewModel == null)
                {
                    _idmViewModel = BuatScopeHalaman().GetRequiredService<IdmViewModel>();
                    _navigation.Navigate(_idmViewModel);
                    await _idmViewModel.MuatAsync();
                }
                else
                {
                    // Halaman sudah pernah dimuat, tampilkan saja tanpa memuat ulang
                    _navigation.Navigate(_idmViewModel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka IDM");
                await _messageService.ShowErrorAsync("Gagal membuka IDM: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Verifikasi Surat: masukkan kode verifikasi yang tercetak di
        /// surat untuk memeriksa keasliannya. Terbuka untuk semua peran, termasuk
        /// Kades dan Auditor yang tidak membuat surat.
        /// </summary>
        private void ShowVerifikasiSurat()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<VerifikasiSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Verifikasi Surat");
                _ = _messageService.ShowErrorAsync("Gagal membuka Verifikasi Surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Dokumentasi: daftar dokumen bawaan aplikasi (regulasi,
        /// panduan, kebijakan). Membaca satu dokumen membuka halaman baca lewat
        /// <see cref="DokumenBacaViewModel"/>, yang daftarnya tidak di sidebar.
        /// </summary>
        private async Task ShowDokumentasiAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<DokumentasiViewModel>();
                await vm.InitializeAsync();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Dokumentasi");
                _ = _messageService.ShowErrorAsync("Gagal membuka Dokumentasi: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman API Desa: nyala/mati listener HTTP lokal, port, dan kunci
        /// API. Pengaturan dibaca ulang setiap halaman dibuka karena disimpan di
        /// preferensi aplikasi, bukan di database.
        /// </summary>
        private void ShowApi()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ApiViewModel>();
                vm.MuatUlang();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka API Desa");
                _ = _messageService.ShowErrorAsync("Gagal membuka API Desa: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka alur paket NTCR: satu form untuk seluruh blanko N1–N6, disimpan
        /// sekaligus dan dicetak menjadi satu berkas PDF gabungan.
        /// </summary>
        private async Task ShowNtcrPaketAsync()
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<NtcrPaketViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
                await vm.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka paket NTCR");
                await _messageService.ShowErrorAsync("Gagal membuka paket NTCR: " + ex.Message);
            }
        }

        private async Task ShowRegisterNtcrAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<RegisterNtcrViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Register NTCR");
                await _messageService.ShowErrorAsync("Gagal membuka Register NTCR: " + ex.Message);
            }
        }

        private async Task ShowSettingsAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<SetelanViewModel>();
                NavigasiHalaman<Views.SetelanView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan");
                await _messageService.ShowErrorAsync("Gagal membuka Pengaturan: " + ex.Message);
            }
        }

        private async Task ShowFormulirAsync()
        {
            try
            {
                // Pengaturan Formulir bersifat "template sesi": instance VM disimpan
                // selama aplikasi berjalan sehingga daftar template tidak perlu di-scan
                // ulang (plus centang/pencarian pengguna) setiap kali menu dibuka —
                // sama seperti Daftar Hadir. Tombol Segarkan tetap menyegarkan daftar.
                if (_formulirViewModel is null)
                {
                    var sp = BuatScopeBertahan();
                    _formulirViewModel = sp.GetRequiredService<FormulirViewModel>();
                }

                _navigation.Navigate(new Views.FormulirView { DataContext = _formulirViewModel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Formulir");
                await _messageService.ShowErrorAsync("Gagal membuka Formulir: " + ex.Message);
            }
        }

        private async Task ShowFormulirPdfAsync(string templateName)
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<Func<string, FormulirPdfViewModel>>()(templateName);
                // Navigasikan instance view BARU (bukan via DataTemplate) agar PDF termuat
                // ulang setiap kali template dipilih — ContentPresenter yang merecycle elemen
                // lama akan tetap menampilkan PDF yang pertama dimuat.
                _navigation.Navigate(new Views.PdfPreviewView { DataContext = vm });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pratinjau formulir {Template}", templateName);
                await _messageService.ShowErrorAsync("Gagal membuka pratinjau formulir: " + ex.Message);
            }
        }

        private async Task ShowAgendaAsync(string jenisSurat)
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<AgendaSuratViewModel>();

                // Tampilkan halaman dulu, muat data menyusul (lihat ShowKeputusanAsync).
                _navigation.Navigate(vm);
                await vm.SetJenisAsync(jenisSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku agenda surat {Jenis}", jenisSurat);
                await _messageService.ShowErrorAsync("Gagal membuka buku agenda: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Daftar Hadir: pilih kolom yang dicetak, isi peserta, lalu
        /// cetak PDF dengan kop surat desa (lihat DaftarHadirViewModel).
        /// </summary>
        private async Task ShowDaftarHadirAsync()
        {
            try
            {
                // Daftar Hadir bersifat "template sesi": instance VM disimpan selama
                // aplikasi berjalan sehingga isian terakhir (setelah cetak PDF) tetap
                // tampil saat menu dibuka lagi. Keluar aplikasi → instance hilang dan
                // menu kembali ke isian default.
                if (_daftarHadirViewModel is null)
                {
                    var sp = BuatScopeBertahan();
                    _daftarHadirViewModel = sp.GetRequiredService<DaftarHadirViewModel>();
                    _daftarHadirViewModel.RequestClose += () => _navigation.ShowDefault();
                }

                _navigation.Navigate(_daftarHadirViewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Daftar Hadir");
                await _messageService.ShowErrorAsync("Gagal membuka Daftar Hadir: " + ex.Message);
            }
        }

        private async Task ShowKeputusanAsync(string jenisKeputusan)
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<KeputusanViewModel>();

                // Halaman ditampilkan lebih dulu (judul + grid kosong ber-indikator
                // memuat), baru data dimuat — klik menu terasa langsung merespons
                // walau arsipnya besar.
                _navigation.Navigate(vm);
                await vm.SetJenisAsync(jenisKeputusan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku SK/Peraturan {Jenis}", jenisKeputusan);
                await _messageService.ShowErrorAsync("Gagal membuka buku SK/Peraturan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Pencadangan Database — satu halaman berisi kartu Ekspor
        /// (membuat salinan cadangan) dan kartu Impor (memulihkan dari cadangan).
        /// </summary>
        private async Task ShowExImdbAsync()
        {
            try
            {
                var vm = BuatScopeHalaman().GetRequiredService<ExImdbViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pencadangan Database");
                await _messageService.ShowErrorAsync("Gagal membuka Pencadangan Database: " + ex.Message);
            }
        }

        private async Task ShowGoogleLoginAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<GoogleLoginViewModel>();
                vm.OnLoggedIn = email => { _ = RefreshGoogleBadgeAsync(); };

                // Bagian sinkron InitializeAsync (set state awal: belum dikonfigurasi /
                // belum login / "Menghubungkan…") berjalan seketika, tapi navigasi TIDAK
                // menunggu validasi token ke server Google — halaman tampil instan dan
                // status ter-update menyusul di background.
                var initTask = vm.InitializeAsync();
                _navigation.Navigate(vm);
                await initTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Login dengan Google");
                await _messageService.ShowErrorAsync("Gagal membuka Login dengan Google: " + ex.Message);
            }
        }

        private async Task ShowPembaruanAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PembaruanViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pembaruan");
                await _messageService.ShowErrorAsync("Gagal membuka Pembaruan: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman terkait sebuah notifikasi lonceng (dipanggil dari flyout
        /// saat item diklik). Kunci tujuan dipetakan ke navigasi yang sama dengan
        /// tombol sidebar supaya tidak ada dua jalur ke halaman yang sama.
        /// </summary>
        public Task BukaTujuanNotifikasiAsync(NotificationItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.TujuanMenu))
            {
                return Task.CompletedTask;
            }

            return item.TujuanMenu switch
            {
                "pembaruan" => ShowPembaruanAsync(),
                "pengaturan-surat" => ShowSettingsAsync(),
                "idm" => ShowIdmAsync(),
                _ => Task.CompletedTask
            };
        }

        /// <summary>
        /// Buka halaman Panduan Awal (langkah mengisi data desa, pejabat, dan nomor
        /// surat). Bisa dibuka kapan saja dari menu Panduan Awal.
        /// </summary>
        public async Task ShowPanduanAwalAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PanduanAwalViewModel>();
                vm.PanduanDitutup += BukaBeranda;
                await vm.PeriksaAsync();
                _featureNavigation.TandaiPanduanAwal();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka panduan awal");
                await _messageService.ShowErrorAsync("Gagal membuka panduan awal: " + ex.Message);
            }
        }

        /// <summary>
        /// Dipanggil sekali setelah pengguna masuk: bila data desa/pejabat belum beres
        /// dan pengguna belum pernah menutup panduan, halaman Panduan Awal dibuka
        /// menggantikan Beranda. Kalau datanya sudah beres, panduan hanya ditandai
        /// selesai dan tidak pernah muncul mengganggu.
        /// </summary>
        /// <returns>True bila panduan benar-benar dibuka (pemanggil tidak perlu membuka Beranda).</returns>
        public async Task<bool> TampilkanPanduanAwalJikaPerluAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<PanduanAwalViewModel>();
                await vm.PeriksaAsync();

                if (!vm.PerluDitampilkanSaatMulai)
                {
                    _logger.LogInformation("Panduan awal tidak perlu ditampilkan (data sudah beres atau sudah ditutup).");
                    return false;
                }

                vm.PanduanDitutup += BukaBeranda;
                _featureNavigation.TandaiPanduanAwal();
                _navigation.Navigate(vm);
                _logger.LogInformation("Panduan awal dibuka otomatis: data desa/pejabat belum lengkap.");
                return true;
            }
            catch (Exception ex)
            {
                // Panduan hanya bantuan: kegagalannya tidak boleh menghalangi aplikasi.
                _logger.LogWarning(ex, "Panduan awal tidak dapat ditampilkan otomatis.");
                return false;
            }
        }

        private async Task ShowAboutAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<AboutViewModel>();
                var view = new Views.AboutView { DataContext = vm };
                _navigation.Navigate(view);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Tentang Aplikasi: {Message}", ex.Message);
                await _messageService.ShowErrorAsync("Gagal membuka Tentang Aplikasi: " + ex.Message);
            }
        }

        private async Task ShowCatatanRilisAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<CatatanRilisViewModel>();
                NavigasiHalaman<Views.CatatanRilisView>(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Catatan Rilis");
                await _messageService.ShowErrorAsync("Gagal membuka Catatan Rilis: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka form Permohonan Rekening Koran (migrasi dari WinForms
        /// Views/Tambahan/PermohonanRekeningKoran.cs) di content host utama.
        /// </summary>
        private async Task ShowRekeningKoranAsync()
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<RekeningKoranViewModel>();
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form Rekening Koran");
                await _messageService.ShowErrorAsync("Gagal membuka form Rekening Koran: " + ex.Message);
            }
        }

        /// <summary>
        /// Buka halaman Template Surat: pengguna menyusun sendiri jenis surat yang
        /// belum tersedia di menu Buat Surat (wizard → pratinjau → pengisian → cetak).
        /// </summary>
        private async Task ShowTemplateSuratAsync()
        {
            try
            {
                var sp = BuatScopeHalaman();
                var vm = sp.GetRequiredService<TemplateSuratViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka halaman Template Surat");
                await _messageService.ShowErrorAsync("Gagal membuka Template Surat: " + ex.Message);
            }
        }

        public async Task LoadSuratAsync(string templateName)
        {
            if (!await BolehBuatSuratBaruAsync()) return;

            var template = MapDisplayToTemplate(templateName);
            try
            {
                // Form ditampilkan langsung di content host utama (bukan modal) —
                // InputSuratHostView dirender melalui DataTemplate InputWindowViewModel.
                var vm = BuatScopeHalaman().GetRequiredService<InputWindowViewModel>();
                vm.Configure(template);
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
            }
            catch (NotSupportedException)
            {
                await _messageService.ShowInfoAsync(
                    "Jenis surat \"" + templateName + "\" belum didukung.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka input surat {Template}", template);
                await _messageService.ShowErrorAsync("Gagal membuka input surat: " + ex.Message);
            }
        }

        private static string MapDisplayToTemplate(string display) => BuatSuratCatalog.ResolveTemplate(display);
    }
}
