using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private Storyboard? _bellStoryboard;

        /// <summary>Waktu terakhir flyout notifikasi ditutup — dipakai untuk
        /// membedakan klik penutup (mouse up pada lonceng) dari klik buka.</summary>
        private DateTime _flyoutClosedAt = DateTime.MinValue;

        /// <summary>Toast Warning/Error: satu penunjuk waktu, tampil 6 detik.</summary>
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            // Chrome bertema: command minimize/maximize/close + state ikon.
            ChromeWindowBehavior.Attach(this);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            KeyDown += MainWindow_KeyDown;

            // Grup sidebar (accordion): tinggi kontennya dianimasikan dari kode,
            // bukan ditampilkan seketika — inilah yang membuat buka/tutup terasa mengalir.
            AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(Accordion_Expanded));
            AddHandler(Expander.CollapsedEvent, new RoutedEventHandler(Accordion_Collapsed));

            // Lonceng berayun setiap ada notifikasi baru masuk; Warning/Error
            // juga ditampilkan sebagai toast singkat di pojok kanan bawah.
            viewModel.Notifications.NotificationAdded += Notifications_NotificationAdded;
            _toastTimer.Tick += (_, _) => SembunyikanToast();

            // "Keluar" dari sidebar cukup menutup jendela; konfirmasi hanya
            // ditangani satu kali oleh MainWindow_Closing (menghindari dobel prompt).
            viewModel.ExitRequested += () => Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"SuDesApp — {LoginViewModel.CurrentUserName}";
            _viewModel.UserName = LoginViewModel.CurrentUserName;

            // Pemeriksaan resolusi layar: sesuaikan ukuran window dengan monitor
            // (laptop 1366×768 @125% sampai desktop 4K) lalu tampilkan ringkasan
            // layar di status bar.
            ApplyScreenResolutionCheck();

            // Halaman pembuka: Beranda berisi ringkasan surat & pintasan cepat,
            // supaya area konten tidak kosong saat aplikasi pertama dibuka.
            _viewModel.BukaBeranda();

            // Grup yang terlanjur terbuka saat pertama render (mis. dari pengaturan
            // tersimpan) tetap disinkronkan agar tingginya sudah benar.
            SinkronkanAccordion(this);

            // Fade-in halus seluruh jendela saat pertama tampil — tidak ada "blink"
            // konten saat aplikasi dibuka.
            LayoutGrid.BeginAnimation(OpacityProperty, null);
            LayoutGrid.BeginAnimation(RenderTransformProperty, null);
            var masuk = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LayoutGrid.BeginAnimation(OpacityProperty, masuk);
        }

        /// <summary>Seret area judul untuk memindah jendela; seret ke tepi atas
        /// layar memaksimalkan jendela (perilaku standar Windows).</summary>
        private void CaptionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Klik ganda area judul = maximize/restore.
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    if (WindowState == WindowState.Maximized)
                    {
                        // Seret saat maximize: kembalikan ukuran normal mengikuti
                        // posisi kursor (perilaku standar Windows).
                        double percentX = e.GetPosition(this).X / ActualWidth;
                        WindowState = WindowState.Normal;
                        Left = e.GetPosition(null).X - Width * percentX;
                        Top = 2;
                    }
                    DragMove();
                }
                catch
                {
                    // DragMove melempar bila dipanggil di luar down asli — abaikan.
                }
            }
        }

        #region WM_GETMINMAXINFO — maximize pas area kerja (anti-terpotong)

        /// <summary>
        /// Dengan WindowStyle=None, Windows memaksimalkan jendela 8px melebihi
        /// tiap tepi layar (menutupi border tak terlihat). Tanpa jebakan ini,
        /// title bar dan status bar terpotong keluar layar. Handler membatasi
        /// maximize ke area kerja monitor tempat jendela berada.
        /// </summary>
        private static class MaximizedBoundsFix
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            public struct MINMAXINFO
            {
#pragma warning disable 649 // struct interop — sebagian field diisi oleh Windows, bukan oleh kode
                public POINT ptReserved;
                public POINT ptMaxSize;
                public POINT ptMaxPosition;
                public POINT ptMinTrackSize;
                public POINT ptMaxTrackSize;
#pragma warning restore 649
            }

            public struct POINT { public int X, Y; }

            [DllImport("user32.dll")]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

            [StructLayout(LayoutKind.Sequential)]
            public struct MONITORINFO
            {
                public int cbSize;
                public RECT rcMonitor;
                public RECT rcWork;
                public uint dwFlags;
            }

            public const int MONITOR_DEFAULTTONEAREST = 2;
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MaximizedBoundsFix.MINMAXINFO>(lParam);
                var monitor = MaximizedBoundsFix.MonitorFromWindow(hwnd, MaximizedBoundsFix.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MaximizedBoundsFix.MONITORINFO { cbSize = Marshal.SizeOf<MaximizedBoundsFix.MONITORINFO>() };
                    if (MaximizedBoundsFix.GetMonitorInfo(monitor, ref info))
                    {
                        // Maximize = pas seluruh area KERJA (di bawah taskbar), bukan melebihi layar.
                        mmi.ptMaxPosition.X = info.rcWork.Left;
                        mmi.ptMaxPosition.Y = info.rcWork.Top;
                        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                        Marshal.StructureToPtr(mmi, lParam, true);
                        handled = true;
                    }
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = (HwndSource)PresentationSource.FromDependencyObject(this);
            source?.AddHook(WndProcHook);
        }

        #endregion

        /// <summary>
        /// Mengecek resolusi & skala DPI monitor, lalu:
        /// 1. Memperkecil ukuran startup/minimal window bila layar tidak memuat 1280×800.
        /// 2. Memusatkan window pada area kerja.
        /// 3. Menampilkan ringkasan resolusi di status bar (chip 🖥️).
        /// </summary>
        private void ApplyScreenResolutionCheck()
        {
            try
            {
                var screenService = App.Current is App app
                    ? app.ServiceProvider.GetService<ScreenResolutionService>()
                    : null;
                if (screenService == null) return;

                var check = screenService.TryCheck(dpiSource: (Visual)this);
                if (check == null) return;

                if (check.ShouldResizeWindow)
                {
                    // Jangan mengecilkan window yang sudah dimaksimalkan pengguna.
                    if (WindowState != WindowState.Maximized)
                    {
                        Width = Math.Max(check.RecommendedMinWidth, check.RecommendedStartWidth);
                        Height = Math.Max(check.RecommendedMinHeight, check.RecommendedStartHeight);
                    }

                    // Batas minimum dinamis agar elemen UI tak terpotong di layar kecil.
                    MinWidth = Math.Max(720, check.RecommendedMinWidth);
                    MinHeight = Math.Max(520, check.RecommendedMinHeight);
                }

                // Pusatkan di area kerja setelah ukuran final ditentukan.
                Left = Math.Max(0, (SystemParameters.WorkArea.Width - ActualWidth) / 2
                                    + SystemParameters.WorkArea.Left);
                Top = Math.Max(0, (SystemParameters.WorkArea.Height - ActualHeight) / 2
                                    + SystemParameters.WorkArea.Top);

                _viewModel.ScreenInfo = check.SummaryLine;

                if (!check.IsCompatible)
                {
                    SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Pemeriksaan Resolusi Layar",
                        "Layar monitor ini di bawah spesifikasi yang disarankan:\n\n" +
                        string.Join("\n", check.Issues) + "\n\n" +
                        "Saran: " + string.Join(" ", check.Recommendations),
                        SuDesApp.Utilities.AppMessageButton.Ok,
                        SuDesApp.Utilities.AppMessageIcon.Warning);
                }
            }
            catch
            {
                // Pemeriksaan layar tidak boleh mengganggu proses startup aplikasi.
            }
        }

        /// <summary>Deretan glyph indikator (diputar + diganti tiap 120 ms → cakram berputar).</summary>
        private static readonly string[] GlyphPembaruan = { "◐", "◓", "◑", "◒" };

        /// <summary>Pita animasi glyph indikator pembaruan (dibangun sekali).</summary>
        private Storyboard? _pembaruanSpin;

        private int _indeksGlyph;

        /// <summary>Benar bila penutupan final sedang berlangsung (alur penundaan tidak boleh jalan lagi).</summary>
        private bool _keluarFinal;

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Penutupan final setelah pemasangan siap: izinkan lewat tanpa dialog.
            if (_keluarFinal) return;

            // Dialog bertema; sinkron karena event Closing harus langsung tahu hasilnya.
            var result = SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                "Konfirmasi Keluar",
                "Yakin ingin keluar dari aplikasi?",
                SuDesApp.Utilities.AppMessageButton.YesNo,
                SuDesApp.Utilities.AppMessageIcon.Question);

            if (result != true)
            {
                e.Cancel = true;
                return;
            }

            // Pemasangan otomatis pembaruan kecil: bila diaktifkan pengguna lewat
            // Pengaturan Aplikasi, penutupan DITUNDA sesaat sementara paket pembaruan
            // diunduh & diverifikasi (chip indikator di status bar berputar); setelah
            // itu aplikasi menutup diri untuk pemasangan. Bila fitur mati, tidak ada
            // penundaan sama sekali — penutupan tetap instan. Pembaruan besar TIDAK
            // pernah ikut alur ini.
            if (SuDesApp.Utilities.AppPreferenceStore.IsPasangOtomatisSaatKeluar())
            {
                e.Cancel = true;
                _ = SiapkanPembaruanOtomatisSaatKeluarAsync();
            }
        }

        /// <summary>Tampilkan chip indikator status bar pada fase yang diberikan.</summary>
        private void TampilkanIndikatorPembaruan(string fase)
        {
            AutoUpdateChipText.Text = fase;
            AutoUpdateChip.Visibility = Visibility.Visible;

            if (_pembaruanSpin == null)
            {
                var putar = new DoubleAnimation
                {
                    From = 0,
                    To = 90,
                    Duration = TimeSpan.FromMilliseconds(120)
                };
                _pembaruanSpin = new Storyboard { Duration = TimeSpan.FromMilliseconds(120) };
                _pembaruanSpin.Children.Add(putar);
                Storyboard.SetTarget(putar, AutoUpdateSpin);
                Storyboard.SetTargetProperty(putar, new PropertyPath("(TextBlock.RenderTransform).(RotateTransform.Angle)"));
                _pembaruanSpin.Completed += (_, _) =>
                {
                    if (AutoUpdateChip.Visibility != Visibility.Visible) return;

                    _indeksGlyph = (_indeksGlyph + 1) % GlyphPembaruan.Length;
                    AutoUpdateSpin.Text = GlyphPembaruan[_indeksGlyph];
                    _pembaruanSpin!.Begin(this, true);
                };
            }

            _pembaruanSpin.Begin(this, true);
        }

        /// <summary>Sembunyikan chip indikator dan hentikan animasinya.</summary>
        private void SembunyikanIndikatorPembaruan()
        {
            AutoUpdateChip.Visibility = Visibility.Collapsed;
            _pembaruanSpin?.Stop(this);
        }

        /// <summary>Tutup jendela utama secara final (alur penundaan tidak jalan lagi).</summary>
        private void TutupFinal()
        {
            _keluarFinal = true;
            SembunyikanIndikatorPembaruan();
            Close();
        }

        /// <summary>
        /// Alur keluar dengan pemasangan otomatis: menampilkan fase di chip indikator
        /// status bar (memeriksa → mengunduh → siap), memasang skrip penerap bila
        /// berhasil, lalu MENUTUP aplikasi pada setiap cabang — pengguna sudah
        /// mengonfirmasi keluar, jadi alur ini tidak boleh meninggalkan aplikasi
        /// menggantung. Seluruh kegagalan cukup dilewati (penutupan tetap berjalan).
        /// </summary>
        private async Task SiapkanPembaruanOtomatisSaatKeluarAsync()
        {
            bool lewatBatas = false;

            try
            {
                if (!SuDesApp.Utilities.AppPreferenceStore.IsPasangOtomatisSaatKeluar()) return;

                var vm = _viewModel;
                var updateService = vm?.PembaruanUpdateService;
                var patchService = vm?.PembaruanPatchService;
                if (updateService == null || patchService == null || !updateService.IsAvailable) return;

                // Fase 1: memeriksa rilis terbaru.
                TampilkanIndikatorPembaruan("Memeriksa pembaruan…");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var rilis = await updateService.CheckForUpdatesAsync(cts.Token);
                if (rilis == null || string.IsNullOrWhiteSpace(rilis.Version)) return;

                var versiTerpasang = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                                     ?? new Version(0, 0);
                if (!Version.TryParse(rilis.Version, out var versiRilis) || versiRilis <= versiTerpasang) return;

                // Fase 2: mengunduh & memverifikasi paket tambalan.
                TampilkanIndikatorPembaruan($"Mengunduh pembaruan {rilis.Version}…");

                var siap = await patchService.SiapkanPembaruanOtomatisAsync(rilis, cts.Token);
                if (siap == null) return; // bukan tambalan / versi tidak cocok / gagal siap

                // Gerbang ukuran: paket yang lebih besar dari batas pengguna tidak
                // pernah diunduh senyap-senyap (siap.TotalByte = jumlah berkas diganti).
                long batasByte = (long)SuDesApp.Utilities.AppPreferenceStore.GetBatasUkuranTambalanMb() * 1024 * 1024;
                if (siap.TotalByte > batasByte)
                {
                    lewatBatas = true;
                    SembunyikanIndikatorPembaruan();

                    // Pengguna sudah memilih keluar — konfirmasikan sekali lagi karena
                    // pembaruan dilewati (bukan senyap-senyap).
                    var lanjutKeluar = SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                        "Pembaruan Otomatis Dilewati",
                        $"Pembaruan kecil {siap.Versi} ({siap.TotalByte / (1024.0 * 1024.0):0.#} MB) melebihi batas " +
                        $"otomatis {SuDesApp.Utilities.AppPreferenceStore.GetBatasUkuranTambalanMb()} MB, jadi tidak dipasang otomatis.\n\n" +
                        "Tetap keluar sekarang? Pembaruan tetap bisa dipasang manual dari menu Pembaruan.",
                        SuDesApp.Utilities.AppMessageButton.YesNo,
                        SuDesApp.Utilities.AppMessageIcon.Warning);

                    if (lanjutKeluar != true)
                    {
                        // Pengguna membatalkan keluar: aplikasi tetap terbuka;
                        // pembaruan bisa dipasang manual dari menu Pembaruan.
                        return;
                    }

                    return; // keluar tanpa pemasangan (ditutup oleh blok finally/akhir)
                }

                // Fase 3: siap dipasang — jalankan skrip penerap (menunggu proses
                // tertutup, mengganti berkas, lalu membuka aplikasi kembali).
                TampilkanIndikatorPembaruan("Siap dipasang — menutup aplikasi…");
                patchService.JalankanPenerapan(siap);

                // Riwayat: pemasangan otomatis tercatat dengan jelas (hasil akhirnya
                // dilaporkan skrip penerap saat aplikasi dibuka kembali).
                SuDesApp.Utilities.RiwayatPembaruanStore.Tambah(new SuDesApp.Utilities.EntriRiwayatPembaruan
                {
                    Versi = siap.Versi,
                    Jenis = "Tambalan",
                    JumlahBerkas = siap.JumlahBerkas,
                    Berhasil = true,
                    Pesan = "Dipasang otomatis saat aplikasi ditutup.",
                    DariVersi = versiTerpasang.ToString(),
                    Waktu = DateTime.Now
                });

                // Beri waktu sesaat agar pengguna sempat melihat fase terakhir.
                await Task.Delay(1200);
            }
            catch (OperationCanceledException)
            {
                // Batas 60 detik tercapai (mis. internet lambat): cukup dilewati.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Pemasangan otomatis pembaruan dilewati: " + ex.Message);
            }
            finally
            {
                if (!lewatBatas)
                {
                    SembunyikanIndikatorPembaruan();
                }

                // Pengguna sudah mengonfirmasi keluar di awal; semua cabang —
                // tanpa pembaruan, gagal unduh, batas waktu, maupun berhasil —
                // berakhir di sini: aplikasi ditutup untuk benar-benar keluar
                // (atau lanjut ke pemasangan oleh skrip penerap).
                TutupFinal();
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                _viewModel.RefreshVillageInfo();
            }
        }

        private void NotificationBell_Click(object sender, MouseButtonEventArgs e)
        {
            // Popup StaysOpen=False menutup sendiri saat klik di luarnya — termasuk
            // klik lonceng ini (sudah terjadi pada mouse down). Tanpa pengaman ini,
            // mouse up yang memicu handler akan membuka ulang sehingga lonceng
            // tidak pernah bisa menutup flyout dan popup tampak berkelip.
            if ((DateTime.Now - _flyoutClosedAt).TotalMilliseconds < 250) return;

            NotificationFlyout.IsOpen = true;
        }

        private void NotificationFlyout_Closed(object? sender, EventArgs e)
        {
            _flyoutClosedAt = DateTime.Now;

            // Tandai dibaca saat flyout DITUTUP (bukan dibuka) supaya titik
            // belum-baca pada daftar tetap terlihat selama flyout terbuka.
            _viewModel.Notifications.MarkAllRead();
        }

        /// <summary>
        /// Animasi lonceng berayun (ala lonceng menggantung) setiap ada
        /// notifikasi baru. Storyboard dibangun sekali lalu diputar ulang;
        /// aman bila notifikasi masuk beruntun karena Begin selalu restart
        /// dari awal tanpa menumpuk.
        /// </summary>
        private void AnimateBellRing()
        {
            if (NotificationBellIcon.RenderTransform is not RotateTransform) return;

            // Storyboard dibangun SEKALI lalu diputar ulang — tanpa alokasi ulang
            // keyframe per notifikasi, tetap ringan walau notifikasi masuk beruntun.
            if (_bellStoryboard == null)
            {
                var swing = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(0.9),
                    KeyFrames =
                    {
                        // Ayunan melebar lalu meredup perlahan (damped oscillation).
                        new EasingDoubleKeyFrame(0,    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(28,   KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(-22,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.30))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(16,   KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.46))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(-10,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.60))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(5,    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.74))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                        new EasingDoubleKeyFrame(0,    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } },
                    }
                };

                _bellStoryboard = new Storyboard();
                _bellStoryboard.Children.Add(swing);
                Storyboard.SetTarget(swing, NotificationBellIcon);
                Storyboard.SetTargetProperty(swing, new PropertyPath("(TextBlock.RenderTransform).(RotateTransform.Angle)"));
            }

            _bellStoryboard.Begin(this, true); // isCancellable: restart dari awal
        }

        private void Notifications_NotificationAdded(object? sender, NotificationItem e)
        {
            // Event dari service dijamin di UI thread (Dispatcher.Invoke di service).
            AnimateBellRing();

            // Warning/Error ditampilkan juga sebagai toast singkat supaya tidak
            // terlewat — Info/Success cukup lewat badge + ayunan lonceng.
            if (e.Type == NotificationType.Warning || e.Type == NotificationType.Error)
            {
                TampilkanToast(e);
            }
        }

        private void NotificationClear_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Notifications.Clear();
            NotificationFlyout.IsOpen = false;
        }

        // ===== Interaksi flyout & toast notifikasi =====

        /// <summary>Klik baris notifikasi → tutup flyout, buka halaman terkait.</summary>
        private async void NotificationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotificationList.SelectedItem is not NotificationItem item) return;

            NotificationList.SelectedIndex = -1; // boleh diklik lagi lain waktu
            NotificationFlyout.IsOpen = false;
            _viewModel.Notifications.MarkAllRead();

            // Metode tujuan sudah menangani galatnya sendiri (log + dialog),
            // jadi tidak perlu try/catch tambahan di sini.
            await _viewModel.BukaTujuanNotifikasiAsync(item);
        }

        /// <summary>Tombol ✕ pada satu baris notifikasi — hapus item itu saja.</summary>
        private void NotificationItemDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NotificationItem item)
            {
                _viewModel.Notifications.Hapus(item);
            }
        }

        /// <summary>Tampilkan toast Warning/Error di pojok kanan bawah selama 6 detik.</summary>
        private void TampilkanToast(NotificationItem item)
        {
            bool error = item.Type == NotificationType.Error;
            ToastIcon.Text = item.Icon;
            ToastTitle.Text = item.Title;
            ToastMessage.Text = item.Message;
            ToastIcon.Foreground = FindResource(error ? "ErrorTextBrush" : "WarningTextBrush") as Brush
                ?? (error ? Brushes.IndianRed : Brushes.DarkOrange);
            NotificationToast.BorderBrush = FindResource(error ? "ErrorBrush" : "WarningBrush") as Brush;

            SembunyikanToast();
            NotificationToast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void SembunyikanToast()
        {
            _toastTimer.Stop();
            NotificationToast.Visibility = Visibility.Collapsed;
        }

        /// <summary>Klik toast → buka daftar notifikasi lengkap.</summary>
        private void NotificationToast_Click(object sender, MouseButtonEventArgs e)
        {
            SembunyikanToast();
            NotificationFlyout.IsOpen = true;
        }

        private void NotificationToastClose_Click(object sender, RoutedEventArgs e)
        {
            SembunyikanToast();
        }

        // ===== Sidebar: lebar mengalir + mode ciut yang benar-benar aktif =====

        private const double LebarSidebarTerbuka = 264;
        private const double LebarSidebarCiut = 64;

        private bool _sidebarCiut;
        private List<NavItem> _grupTerbukaSebelumCiut = new();

        private void SidebarToggle_Click(object sender, RoutedEventArgs e) => SetSidebarCiut(!_sidebarCiut);

        /// <summary>
        /// Ubah sidebar antara lebar penuh dan mode ciut (hanya ikon).
        /// Lebarnya dianimasikan, dan mode ciut diberitahukan ke ViewModel supaya
        /// template item (label/ikon terpusat/tooltip) ikut menyesuaikan.
        /// </summary>
        private void SetSidebarCiut(bool ciut)
        {
            if (ciut == _sidebarCiut)
            {
                return;
            }

            _sidebarCiut = ciut;

            if (ciut)
            {
                // Ingat grup yang sedang terbuka, lalu tutup semuanya: di lebar 64px
                // daftar anak tidak punya ruang untuk ditampilkan dengan layak.
                _grupTerbukaSebelumCiut = _viewModel.MenuItems
                    .Where(item => item.IsAccordion && item.IsExpanded)
                    .ToList();

                foreach (var grup in _grupTerbukaSebelumCiut)
                {
                    grup.IsExpanded = false;
                }
            }

            _viewModel.SetSidebarCiut(ciut);

            SidebarToggleIcon.Text = ciut ? "\uE00F" : "\uE00E"; // chevron kanan / kiri
            SidebarToggleButton.ToolTip = ciut
                ? "Tampilkan sidebar navigasi"
                : "Sembunyikan sidebar navigasi";

            var kolom = LayoutGrid.ColumnDefinitions[0];
            double dari = kolom.ActualWidth > 0 ? kolom.ActualWidth : LebarSidebarTerbuka;
            double ke = ciut ? LebarSidebarCiut : LebarSidebarTerbuka;

            var animasi = new GridLengthAnimation
            {
                From = new GridLength(dari),
                To = new GridLength(ke),
                Duration = TimeSpan.FromMilliseconds(ciut ? 190 : 280),
                // Ciut: mulailah lebih cepat agar terasa lincah; lebar: landas lembut.
                EasingMode = ciut ? GridLengthEasingMode.EaseIn : GridLengthEasingMode.EaseOut
            };

            animasi.Completed += (_, _) =>
            {
                // Lepas animasi lalu kunci lebar akhir sebagai nilai biasa, agar
                // pengaturan lebar berikutnya (dan drag) tidak "ditahan" animasi.
                kolom.BeginAnimation(ColumnDefinition.WidthProperty, null);
                kolom.Width = new GridLength(ke);

                if (!ciut)
                {
                    PulihkanGrupTerbuka();
                }
            };

            kolom.BeginAnimation(ColumnDefinition.WidthProperty, animasi);
        }

        /// <summary>Buka kembali grup yang tadinya terbuka sebelum sidebar diciutkan.</summary>
        private void PulihkanGrupTerbuka()
        {
            foreach (var grup in _grupTerbukaSebelumCiut)
            {
                grup.IsExpanded = true;
            }

            _grupTerbukaSebelumCiut = new List<NavItem>();
        }

        /// <summary>
        /// Klik header grup saat sidebar ciut: lebarkan sidebar lebih dulu (agar nama
        /// menu terbaca), bukan langsung membuka grup.
        /// </summary>
        private void AccordionHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!_sidebarCiut)
            {
                return; // sidebar normal: biarkan toggle buka/tutup grup bekerja
            }

            SetSidebarCiut(false);

            // Kembalikan status grup ini: klik tadi hanya dimaksudkan untuk melebarkan
            // sidebar, bukan membuka grup yang namanya belum sempat terbaca.
            if (sender is ToggleButton tombol)
            {
                tombol.IsChecked = false;
            }

            e.Handled = true;
        }

        // ===== Animasi accordion: tinggi konten tumbuh/menyusut, tidak melompat =====

        private void Accordion_Expanded(object sender, RoutedEventArgs e)
            => AnimasikanAccordion(e.OriginalSource as Expander, terbuka: true);

        private void Accordion_Collapsed(object sender, RoutedEventArgs e)
            => AnimasikanAccordion(e.OriginalSource as Expander, terbuka: false);

        private void AnimasikanAccordion(Expander? expander, bool terbuka)
        {
            if (expander is null)
            {
                return;
            }

            // Tunggu satu putaran layout: tinggi alami konten hanya bisa diukur setelah
            // template grup selesai disusun.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var konten = CariKontenAccordion(expander);
                if (konten is null)
                {
                    return;
                }

                // Bersihkan animasi transisi sebelumnya (apa pun statusnya) agar mulai
                // dari nilai aktual, bukan sisa keyframe lama.
                konten.BeginAnimation(MaxHeightProperty, null);
                konten.BeginAnimation(OpacityProperty, null);
                var geser = SiapkanGeserBisaAnimasi(konten);
                geser?.BeginAnimation(TranslateTransform.YProperty, null);

                if (terbuka)
                {
                    konten.Visibility = Visibility.Visible;

                    // Ukur tinggi alaminya, lalu tumbuhkan dari 0 ke tinggi itu.
                    konten.MaxHeight = double.PositiveInfinity;
                    konten.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double tinggi = konten.DesiredSize.Height;
                    konten.MaxHeight = 0;

                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                    var animTinggi = new DoubleAnimation(0, tinggi, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = ease
                    };
                    animTinggi.Completed += (_, _) => konten.MaxHeight = double.PositiveInfinity;
                    konten.BeginAnimation(MaxHeightProperty, animTinggi);

                    // Konten ikut fade + geser turun ke posisi — serentak dengan tumbuhnya.
                    var animFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = ease
                    };
                    var animGeser = new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = ease
                    };
                    konten.BeginAnimation(OpacityProperty, animFade);
                    geser?.BeginAnimation(TranslateTransform.YProperty, animGeser);
                }
                else
                {
                    double tinggi = konten.ActualHeight;
                    var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

                    var animTinggi = new DoubleAnimation(tinggi, 0, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = ease
                    };
                    animTinggi.Completed += (_, _) =>
                    {
                        if (!expander.IsExpanded)
                        {
                            konten.Visibility = Visibility.Collapsed;
                            konten.MaxHeight = double.PositiveInfinity;
                        }
                    };
                    konten.BeginAnimation(MaxHeightProperty, animTinggi);

                    var animFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130))
                    {
                        EasingFunction = ease
                    };
                    var animGeser = new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = ease
                    };
                    konten.BeginAnimation(OpacityProperty, animFade);
                    geser?.BeginAnimation(TranslateTransform.YProperty, animGeser);
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Kembalikan TranslateTransform yang aman dianimasikan dari code-behind.
        /// Transform yang dideklarasikan di XAML (dalam template) dibekukan WPF,
        /// sehingga BeginAnimation langsung melontarkan InvalidOperationException —
        /// solusinya klon ke instans baru yang bisa diedit dan tempelkan kembali.
        /// </summary>
        private static TranslateTransform? SiapkanGeserBisaAnimasi(StackPanel konten)
        {
            if (konten.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                konten.RenderTransform = transform;
            }
            else if (transform.IsFrozen)
            {
                transform = transform.Clone();
                konten.RenderTransform = transform;
            }

            return transform;
        }

        /// <summary>Panel anak sebuah grup accordion di sidebar (nama: AccordionContent).</summary>
        private static StackPanel? CariKontenAccordion(DependencyObject root)
        {
            int jumlah = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < jumlah; i++)
            {
                var anak = VisualTreeHelper.GetChild(root, i);
                if (anak is StackPanel panel && panel.Name == "AccordionContent")
                {
                    return panel;
                }

                var temuan = CariKontenAccordion(anak);
                if (temuan is not null)
                {
                    return temuan;
                }
            }

            return null;
        }

        /// <summary>Pastikan grup yang memang sedang terbuka tampil penuh (bukan terpotong).</summary>
        private static void SinkronkanAccordion(DependencyObject root)
        {
            if (root is Expander expander && expander.IsExpanded && CariKontenAccordion(expander) is StackPanel konten)
            {
                konten.Visibility = Visibility.Visible;
                konten.MaxHeight = double.PositiveInfinity;
            }

            int jumlah = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < jumlah; i++)
            {
                SinkronkanAccordion(VisualTreeHelper.GetChild(root, i));
            }
        }
    }
}
