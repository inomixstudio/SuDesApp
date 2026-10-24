using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Layanan navigasi untuk content host utama.
    /// ==== Skema migrasi dari WinForms ====
    /// SuDesApp.Services.ControlService.SetMainPanel(IContentHost)
    ///   -&gt; NavigationService.Navigate(viewModel/view)
    /// ControlManager.ShowControl(...) / LoadDokumenAsync / ShowSuratControl
    ///   -&gt; NavigationService menampilkan view-model; ContentControl di MainWindow
    ///       otomatis memuat view melalui DataTemplate.
    ///
    /// ==== Pemilik masa hidup scope halaman ====
    /// Setiap halaman punya IServiceScope sendiri (di dalamnya ada SqliteConnection
    /// scoped), dan kepemilikannya ada di kelas ini — bukan pada slot global yang
    /// bisa disuntik VM mana pun. Karena itu scope hanya bisa tertukar lewat jalur
    /// navigasi:
    ///   <list type="bullet">
    ///     <item><see cref="BukaHalaman"/> hanya MENYIAPKAN scope halaman berikutnya;
    ///           halaman yang sedang tampil tidak disentuh.</item>
    ///     <item><see cref="Navigate(object)"/> memasangnya: scope halaman lama baru
    ///           ditutup tepat saat penggantinya benar-benar ditampilkan.</item>
    ///   </list>
    /// Akibatnya kegagalan membangun halaman (muat data gagal, dst.) tidak pernah
    /// mematikan halaman lama yang masih tampil di layar. Manipulasi state scope
    /// dikunci, sehingga aman dari pemanggilan lintas thread (penutupan jendela
    /// vs tugas latar); menampilkan isi halaman tetap urusan UI thread.
    /// </summary>
    public class NavigationService : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NavigationService> _logger;
        private object? _currentView;

        // Scope halaman yang sedang tampil, scope halaman berikutnya yang sudah
        // disiapkan lewat BukaHalaman() tapi belum ditampilkan, dan scope
        // bertahan seumur sesi (Formulir, Daftar Hadir, Panduan WA).
        private IServiceScope? _halamanAktif;
        private IServiceScope? _halamanBerikutnya;
        private readonly List<IServiceScope> _bertahan = new();
        // Kunci khusus state scope: hanya melindungi tiga field di atas,
        // tidak pernah diambil bersama lock lain sehingga bebas deadlock.
        private readonly object _kunciScope = new();

        public event Action<object?>? CurrentViewChanged;

        public object? CurrentView
        {
            get => _currentView;
            private set
            {
                if (ReferenceEquals(_currentView, value))
                {
                    return;
                }
                _currentView = value;
                CurrentViewChanged?.Invoke(value);
            }
        }

        public NavigationService(
            IServiceProvider serviceProvider,
            ILogger<NavigationService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Siapkan scope untuk halaman yang sedang dibangun. Halaman yang sedang
        /// tampil TIDAK ikut ditutup — penutupannya terjadi di
        /// <see cref="Navigate(object)"/>. Scope yang disiapkan tetapi tak pernah
        /// ditampilkan ditutup pada <see cref="BukaHalaman"/> berikutnya, sehingga
        /// tidak menumpuk.
        /// </summary>
        public IServiceProvider BukaHalaman()
        {
            IServiceScope scope;
            lock (_kunciScope)
            {
                Tutup(ref _halamanBerikutnya, "scope halaman yang tak jadi ditampilkan");
                scope = _scopeFactory.CreateScope();
                _halamanBerikutnya = scope;
            }
            return scope.ServiceProvider;
        }

        /// <summary>
        /// Scope untuk ViewModel yang SENGAJA disimpan seumur sesi (mis. Formulir,
        /// Daftar Hadir, Panduan WA). Bukan kebocoran: ini keputusan agar halaman
        /// tidak dibangun ulang tiap kali menu dibuka. Ditutup sekali lewat
        /// <see cref="Dispose"/> saat aplikasi keluar.
        /// </summary>
        public IServiceProvider BukaBertahan()
        {
            var scope = _scopeFactory.CreateScope();
            lock (_kunciScope)
            {
                _bertahan.Add(scope);
            }
            return scope.ServiceProvider;
        }

        /// <summary>Tampilkan view-model / view yang sudah di-resolve dari DI.</summary>
        public void Navigate<TViewModel>() where TViewModel : notnull
        {
            // Resolve dari root provider (bukan scope halaman), jadi tidak ikut
            // menukar scope halaman.
            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            CurrentView = vm;
        }

        /// <summary>
        /// Tampilkan object (view-model atau view) secara langsung. Satu-satunya
        /// tempat scope halaman lama ditutup dan penggantinya dipasang — tukar
        /// scope terikat pada kenyataan bahwa halaman memang berganti.
        /// </summary>
        public void Navigate(object content)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));

            PasangHalamanBerikutnya();
            CurrentView = content;
        }

        /// <summary>Kosongkan content host (analog ControlManager.ShowDefaultControl).</summary>
        public void ShowDefault()
        {
            CurrentView = null;
        }

        /// <summary>
        /// Buka halaman Pengaturan Surat tepat pada salah satu bagiannya.
        /// Dipakai tombol "Isi Data Desa" pada peringatan data desa contoh
        /// (bagian 0 = Data Desa).
        /// </summary>
        public void ShowSetelanBagianSurat(int bagian)
        {
            var setelan = _serviceProvider.GetRequiredService<SetelanViewModel>();
            setelan.TampilkanBagian(bagian);
            CurrentView = setelan;
        }

        /// <summary>
        /// Tutup semua scope halaman. Dipanggil container lewat App.OnExit
        /// bersama pembuangan service provider — inilah satu-satunya pemiliknya.
        /// </summary>
        public void Dispose()
        {
            lock (_kunciScope)
            {
                Tutup(ref _halamanAktif, "scope halaman aktif");
                Tutup(ref _halamanBerikutnya, "scope halaman berikutnya");

                foreach (var scope in _bertahan)
                {
                    try { scope.Dispose(); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal menutup scope bertahan.");
                    }
                }
                _bertahan.Clear();
            }
        }

        /// <summary>
        /// Jadikan scope berikutnya sebagai scope halaman aktif; yang lama baru
        /// ditutup di sini. Tanpa scope berikutnya (navigasi memakai VM lama /
        /// VM bertahan), halaman aktif dibiarkan apa adanya.
        /// </summary>
        private void PasangHalamanBerikutnya()
        {
            lock (_kunciScope)
            {
                if (_halamanBerikutnya is null) return;

                var berikutnya = _halamanBerikutnya;
                _halamanBerikutnya = null;
                Tutup(ref _halamanAktif, "scope halaman sebelumnya");
                _halamanAktif = berikutnya;
            }
        }

        private void Tutup(ref IServiceScope? scope, string keterangan)
        {
            if (scope is null) return;

            try
            {
                scope.Dispose();
            }
            catch (Exception ex)
            {
                // Menutup scope tidak boleh menggagalkan navigasi.
                _logger.LogWarning(ex, "Gagal menutup {Scope}.", keterangan);
            }
            finally
            {
                scope = null;
            }
        }
    }
}
