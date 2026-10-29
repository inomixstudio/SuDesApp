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
using SuDesApp.Utilities;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
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

            // Ikon jendela/taskbar mengikuti warna aksen tema aktif; ikon digambar
            // ulang setiap kali tema diganti (ubin aksen + amplop putih).
            viewModel.TemaService.TemaBerubah += () => TerapkanIkonTema();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"SuDesApp — {LoginViewModel.CurrentUserName}";
            _viewModel.UserName = LoginViewModel.CurrentUserName;

            // Ikon tema dipasang lagi saat jendela siap (event TemaBerubah sempat
            // menyala saat konstruksi ThemeService, sebelum jendela ada).
            TerapkanIkonTema();

            // Tampilan sidebar terakhir dipakai lagi. Tanpa animasi: lebar dipasang
            // langsung supaya sidebar tidak "tumbuh" saat jendela baru dibuka.
            TerapkanModeSidebar(SidebarModePrefs.Muat(), animasi: false);

            // Pemeriksaan resolusi layar: sesuaikan ukuran window dengan monitor
            // (laptop 1366×768 @125% sampai desktop 4K) lalu tampilkan ringkasan
            // layar di status bar.
            ApplyScreenResolutionCheck();

            // Halaman pembuka: panduan awal didahulukan bila data desa/pejabat belum
            // lengkap (menuntun mengisi data desa, pejabat, dan nomor surat); selain
            // itu Beranda berisi ringkasan surat & pintasan cepat yang langsung tampil.
            _ = BukaHalamanPembukaAsync();

            // Grup yang terlanjur terbuka saat pertama render (mis. dari pengaturan
            // tersimpan) tetap disinkronkan agar tingginya sudah benar.
            SinkronkanAccordion(this);

            // Fade-in halus seluruh jendela saat pertama tampil — tidak ada "blink"
            // konten saat aplikasi dibuka.
            LayoutGrid.BeginAnimation(OpacityProperty, null);
            LayoutGrid.BeginAnimation(RenderTransformProperty, null);
            var masuk = new DoubleAnimation(
                0, 1, KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarTransisiHalaman))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LayoutGrid.BeginAnimation(OpacityProperty, masuk);
        }

        /// <summary>
        /// Halaman pertama yang tampil di area konten. Panduan awal menunggu daftar
        /// data desa selesai diperiksa lebih dulu supaya tidak tertimpa Beranda
        /// (keduanya memuat datanya sendiri-sendiri secara asinkron).
        /// </summary>
        private async Task BukaHalamanPembukaAsync()
        {
            if (!await _viewModel.TampilkanPanduanAwalJikaPerluAsync())
            {
                _viewModel.BukaBeranda();
            }
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

            // Sudut membulat via DWM Windows 11 (opt-in, bukan AllowsTransparency)
            // sehingga WindowChrome dan grip resize tetap utuh; saat maximized DWM
            // otomatis mempertahankan sudut persegi.
            SudutDwm.Bulatkan(this);
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
        // Glyph spinner memakai font ikon (AppIconFont, lihat XAML): E895 = Sync.
        // Gerakan diperoleh dari rotasi 90° per langkah, bukan pergantian karakter —
        // karakter ◐◓◑◒ tidak punya glyph di font ikon (tampil kotak/rusak).
        /// <summary>Segmen animasi spinner pembaruan: glyph Sinkron (E895) diputar
        /// 90° tiap langkah lewat RotateTransform yang sudah ada.</summary>
        private static readonly string[] GlyphPembaruan = { SuDesApp.Wpf.Utilities.IkonMenu.Sinkron };

        /// <summary>Pita animasi glyph indikator pembaruan (dibangun sekali).</summary>
        private Storyboard? _pembaruanSpin;

        private int _indeksGlyph;

        /// <summary>Benar bila penutupan final sedang berlangsung (alur penundaan tidak boleh jalan lagi).</summary>
        private bool _keluarFinal;

        /// <summary>
        /// Penutupan yang DISengaja oleh tombol "Simpan &amp; Mulai Ulang Sekarang"
        /// (Pengaturan Aplikasi → Database Desa): lewati dialog "Yakin ingin keluar?"
        /// dan alur pemasangan otomatis pembaruan — pengguna sudah memastikan dari
        /// dialog konfirmasi tombol itu sendiri, dan pembaruan tidak boleh menyusul
        /// di tengah proses pindah lokasi database. OnExit (backup Drive saat
        /// keluar) tetap berjalan normal setelah ini.
        /// </summary>
        public void KeluarUntukMulaiUlang()
        {
            TutupFinal();
        }

        /// <summary>
        /// Tutup jendela karena pemasangan pembaruan sudah dijadwalkan (pengguna
        /// memilih "Instal Sekarang" pada notifikasi). Tanpa dialog konfirmasi keluar
        /// dan tanpa alur pasang-saat-keluar — keduanya hanya akan menghalangi
        /// pemasangan yang sudah disiapkan.
        /// </summary>
        public void TutupUntukPemasangan()
        {
            TutupFinal();
        }

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

            // Pembaruan yang SUDAH terunduh di latar belakang: bila pengguna
            // memilih "Instal Saat Keluar" (atau setelan pasang otomatis pembaruan
            // kecil aktif), pemasangan dijadwalkan lebih dulu, lalu penutupan
            // dilanjutkan segera — tidak ada pemeriksaan/unduhan baru yang menunda
            // keluar.
            if (_viewModel.PembaruanLatar.SiapkanPemasanganSaatKeluar())
            {
                e.Cancel = true;
                _ = TutupSetelahPemasanganAsync();
                return;
            }

            // Tutup semua scope DI yang dipegang MainWindowViewModel (terutama
            // scope "bertahan" untuk ViewModel berumur sesi). Tanpa ini handle
            // SQLite native tetap tertahan sampai proses mati, dan timer terus
            // berdetak pada ViewModel yang sudah tidak terlihat.
            (DataContext as ViewModels.MainWindowViewModel)?.Dispose();
        }

        /// <summary>
        /// Penutupan setelah pemasangan dijadwalkan: tunjukkan fase terakhir di chip
        /// status bar sebentar, lalu tutup tanpa dialog. Skrip pemasang menunggu proses
        /// ini benar-benar berakhir sebelum mengganti berkas.
        /// </summary>
        private async Task TutupSetelahPemasanganAsync()
        {
            try
            {
                TampilkanIndikatorPembaruan("Memasang pembaruan — menutup aplikasi…");
                await Task.Delay(800);
            }
            catch
            {
                // Apa pun yang terjadi, penutupan tetap dilanjutkan.
            }
            finally
            {
                TutupFinal();
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
            (DataContext as ViewModels.MainWindowViewModel)?.Dispose();
            Close();
        }

        /// <summary>
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                _viewModel.RefreshVillageInfo();
                return;
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
            //
            // Kabar pembaruan tidak memakai toast/overlay sama sekali: halaman
            // Pembaruan sudah menampilkan kabarnya sendiri, dan pengguna memintanya
            // bebas dari popup. Cukup badge + lonceng yang bisa diklik ke halaman itu.
            bool kabarPembaruan = string.Equals(e.TujuanMenu, "pembaruan", StringComparison.Ordinal);
            if (!kabarPembaruan && (e.Type == NotificationType.Warning || e.Type == NotificationType.Error))
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

        /// <summary>
        /// Jalankan tombol tindakan pada notifikasi (mis. "Instal Sekarang" /
        /// "Instal Saat Keluar"). Tindakannya sudah disiapkan pengirim notifikasi;
        /// handler ini hanya menutup flyout lalu menjalankannya.
        /// </summary>
        private void NotificationAction_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if ((sender as FrameworkElement)?.DataContext is not NotificationAction aksi) return;

            // Tombol tindakan bukan navigasi: batalkan pilihan baris agar flyout
            // tidak ikut membuka halaman Pembaruan ketika pengguna memilih
            // "Instal Saat Keluar".
            NotificationList.SelectedItem = null;
            NotificationFlyout.IsOpen = false;
            try
            {
                aksi.Execute();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Tindakan notifikasi gagal: " + ex.Message);
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

        // ===== Sidebar: dua tampilan — penuh (nama menu tampil) ↔ ikon saja =====
        // Tombol di header memutarnya, dan pilihannya diingat untuk pembukaan
        // aplikasi berikutnya.

        /// <summary>Gambar ulang ikon jendela (taskbar/Alt-Tab) dengan warna aksen
        /// tema aktif — dipanggil saat jendela dimuat dan tiap kali tema diganti.</summary>
        private void TerapkanIkonTema()
        {
            var ikon = PenataIkonJendela.BuatUntukTemaAktif();
            if (ikon != null)
            {
                Icon = ikon;
            }
        }

        private const double LebarSidebarTerbuka = 264;
        private const double LebarSidebarCiut = 64;

        // Tinggi header: 66 cukup untuk satu baris logo+nama (terbuka); saat ikon-saja
        // logo terpusat di atas dan tombol di dasar sehingga butuh 108 agar tidak
        // bertumpukan di lebar 64 px.
        private const double TinggiHeaderTerbuka = 66;
        private const double TinggiHeaderCiut = 108;

        private SidebarMode _modeSidebar = SidebarMode.Terbuka;
        private List<NavItem> _grupTerbukaSebelumCiut = new();

        /// <summary>Klik tombol tampilan: ke mode berikutnya (penuh ↔ ikon saja).</summary>
        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
            => TerapkanModeSidebar(SidebarModePrefs.Berikutnya(_viewModel.ModeSidebar),
                animasi: true, simpan: true);

        /// <summary>
        /// Terapkan salah satu dari dua tampilan sidebar. Lebarnya dianimasikan dan
        /// tampilannya diberitahukan ke ViewModel supaya template item (label/ikon
        /// terpusat/tooltip) ikut menyesuaikan.
        /// </summary>
        /// <param name="animasi">
        /// False dipakai saat memulihkan pilihan tersimpan ketika jendela baru dibuka:
        /// lebar langsung dipasang supaya tidak ada sidebar yang "tumbuh" saat start.
        /// </param>
        private void TerapkanModeSidebar(SidebarMode mode, bool animasi, bool simpan = false)
        {
            bool berubah = mode != _modeSidebar;
            _modeSidebar = mode;

            if (simpan)
            {
                SidebarModePrefs.Simpan(mode);
            }

            if (berubah && mode != SidebarMode.Terbuka)
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

            _viewModel.SetSidebarMode(mode);

            SidebarToggleButton.ToolTip =
                $"Tampilan navigasi: {_viewModel.SidebarModeTeks}\n" +
                $"Klik untuk {SidebarModePrefs.TeksBerikut(mode)}";

            bool ciut = mode != SidebarMode.Terbuka;

            // Panah tombol buka/tutup diputar halus (0° ↔ 180°) dengan durasi yang
            // sama seperti lebar sidebar, bukan diganti glyph seketika — dulu arahnya
            // berubah mendadak sehingga terasa melompat.
            AnimasiSederhana(
                SidebarToggleRotate, RotateTransform.AngleProperty,
                ciut ? 180d : 0d, KecepatanAnimasiPrefs.Skala(DurasiTransisiSidebar));

            // Nama aplikasi memudar mengikuti lebar sidebar, bukan hilang mendadak.
            AnimasiSederhana(
                HeaderBrandText, UIElement.OpacityProperty,
                ciut ? 0d : 1d, KecepatanAnimasiPrefs.Skala(DurasiTransisiSidebar));

            var kolom = LayoutGrid.ColumnDefinitions[0];
            double ke = ciut ? LebarSidebarCiut : LebarSidebarTerbuka;

            if (!animasi)
            {
                kolom.BeginAnimation(ColumnDefinition.WidthProperty, null);
                kolom.Width = new GridLength(ke);
                HeaderBorder.Height = ciut ? TinggiHeaderCiut : TinggiHeaderTerbuka;

                if (!ciut)
                {
                    PulihkanGrupTerbuka();
                }

                return;
            }

            // Seluruh transisi sidebar memakai satu durasi yang sudah diskalakan sesuai
            // kecepatan animasi pilihan pengguna (Lambat/Normal/Cepat).
            var durasi = KecepatanAnimasiPrefs.Skala(DurasiTransisiSidebar);

            // Tinggi header mengikuti mode (66 terbuka / 108 ikon-saja) satu tempo dengan
            // lebarnya, supaya logo & tombol tidak bertumpukan di lebar 64 px.
            AnimasiSederhana(
                HeaderBorder, FrameworkElement.HeightProperty,
                ciut ? TinggiHeaderCiut : TinggiHeaderTerbuka, durasi);

            double dari = kolom.ActualWidth > 0 ? kolom.ActualWidth : LebarSidebarTerbuka;

            var animasiLebar = new GridLengthAnimation
            {
                From = new GridLength(dari),
                To = new GridLength(ke),
                // Satu durasi & satu kurva untuk kedua arah: menciutkan dan melebarkan
                // kini terasa sama halusnya (dulu ciut 190 dtk EaseIn, lebar 280 dtk EaseOut).
                Duration = durasi,
                EasingMode = GridLengthEasingMode.EaseInOut
            };

            animasiLebar.Completed += (_, _) =>
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

            kolom.BeginAnimation(ColumnDefinition.WidthProperty, animasiLebar);
        }

        /// <summary>
        /// Durasi dasar transisi sidebar — dipakai lebar kolom, nama aplikasi, dan
        /// panahnya. Nilai akhirnya selalu lewat <c>KecepatanAnimasiPrefs.Skala</c>,
        /// sehingga mengikuti pilihan pengguna di Pengaturan Aplikasi.
        /// </summary>
        private static readonly TimeSpan DurasiTransisiSidebar = KecepatanAnimasiPrefs.DasarSidebar;

        /// <summary>Durasi dasar tinggi & geser isi dropdown grup (nilai akhirnya diskalakan).</summary>
        private static readonly TimeSpan DurasiDropdown = KecepatanAnimasiPrefs.DasarDropdown;

        /// <summary>Durasi dasar memudarnya isi dropdown grup (nilai akhirnya diskalakan).</summary>
        private static readonly TimeSpan DurasiDropdownFade = KecepatanAnimasiPrefs.DasarDropdownFade;

        /// <summary>
        /// Animasi nilai tunggal dengan kurva lembut yang sama untuk semua transisi
        /// sidebar. Nilai akhir sekaligus dikunci setelah selesai supaya tidak
        /// tertahan oleh animasi.
        /// </summary>
        private static void AnimasiSederhana(
            UIElement target, DependencyProperty properti, double ke, TimeSpan durasi)
            => JalankanAnimasiSederhana(target, properti, ke, durasi);

        private static void AnimasiSederhana(
            Animatable target, DependencyProperty properti, double ke, TimeSpan durasi)
            => JalankanAnimasiSederhana(target, properti, ke, durasi);

        /// <summary>
        /// Jalankan animasi dari nilai saat ini ke <paramref name="ke"/>. Nilai dasar
        /// TIDAK disetel lebih dulu (kalau disetel, animasi akan berjalan dari tujuan
        /// ke tujuan alias tidak bergerak); nilai dasar baru dikunci setelah animasi
        /// selesai sehingga perpindahan berikutnya berangkat dari keadaan nyata.
        /// </summary>
        private static void JalankanAnimasiSederhana(
            DependencyObject target, DependencyProperty properti, double ke, TimeSpan durasi)
        {
            var animasi = new DoubleAnimation
            {
                To = ke,
                Duration = durasi,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };

            animasi.Completed += (_, _) =>
            {
                if (target is UIElement ui)
                {
                    ui.BeginAnimation(properti, null);
                }
                else if (target is Animatable animatable)
                {
                    animatable.BeginAnimation(properti, null);
                }

                target.SetValue(properti, ke);
            };

            switch (target)
            {
                case UIElement ui:
                    ui.BeginAnimation(properti, animasi);
                    break;
                case Animatable animatable:
                    animatable.BeginAnimation(properti, animasi);
                    break;
            }
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
            if (_modeSidebar == SidebarMode.Terbuka)
            {
                return; // sidebar normal: biarkan toggle buka/tutup grup bekerja
            }

            // Klik header saat sidebar hanya ikon: lebarkan sidebar lebih dulu (agar
            // nama menu terbaca). Pilihan ini diingat seperti tombol tampilan.
            TerapkanModeSidebar(SidebarMode.Terbuka, animasi: true, simpan: true);

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

                // Satu durasi untuk tinggi, fade, dan putaran chevron — diskalakan
                // sesuai kecepatan animasi pilihan pengguna.
                var durasiIsi = KecepatanAnimasiPrefs.Skala(DurasiDropdown);
                var durasiFade = KecepatanAnimasiPrefs.Skala(DurasiDropdownFade);

                // Panah grup berputar serentak dengan tumbuh/menyusutnya isi.
                AnimasikanChevron(expander, terbuka ? 90d : 0d, durasiIsi);

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

                    var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

                    var animTinggi = new DoubleAnimation(0, tinggi, durasiIsi)
                    {
                        EasingFunction = ease
                    };
                    animTinggi.Completed += (_, _) => konten.MaxHeight = double.PositiveInfinity;
                    konten.BeginAnimation(MaxHeightProperty, animTinggi);

                    // Konten ikut fade + geser turun ke posisi — serentak dengan tumbuhnya.
                    var animFade = new DoubleAnimation(0, 1, durasiFade)
                    {
                        EasingFunction = ease
                    };
                    var animGeser = new DoubleAnimation(-8, 0, durasiIsi)
                    {
                        EasingFunction = ease
                    };
                    konten.BeginAnimation(OpacityProperty, animFade);
                    geser?.BeginAnimation(TranslateTransform.YProperty, animGeser);
                }
                else
                {
                    double tinggi = konten.ActualHeight;
                    // Durasi & kurva menutup disamakan dengan membuka — dulu 180/130 dtk
                    // dengan EaseIn sehingga menutup terasa jauh lebih cepat.
                    var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

                    var animTinggi = new DoubleAnimation(tinggi, 0, durasiIsi)
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

                    var animFade = new DoubleAnimation(1, 0, durasiFade)
                    {
                        EasingFunction = ease
                    };
                    var animGeser = new DoubleAnimation(0, -6, durasiIsi)
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
        private static TranslateTransform? SiapkanGeserBisaAnimasi(FrameworkElement konten)
            => SiapkanTransform<TranslateTransform>(konten);

        /// <summary>
        /// Putar panah (chevron) header grup agar serentak dengan isi dropdown yang
        /// tumbuh/menyusut — dulu lewat EventTrigger berdurasi tetap di XAML sehingga
        /// tidak ikut kecepatan animasi pilihan pengguna.
        /// </summary>
        private static void AnimasikanChevron(FrameworkElement grup, double sudut, TimeSpan durasi)
        {
            if (CariAnakBernama(grup, "ChevronIcon") is not FrameworkElement chevron)
            {
                return;
            }

            var putar = SiapkanTransform<RotateTransform>(chevron);
            AnimasiSederhana(putar, RotateTransform.AngleProperty, sudut, durasi);
        }

        /// <summary>Cari turunan pertama bernama <paramref name="nama"/> di pohon visual.</summary>
        private static FrameworkElement? CariAnakBernama(DependencyObject root, string nama)
        {
            int jumlah = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < jumlah; i++)
            {
                var anak = VisualTreeHelper.GetChild(root, i);
                if (anak is FrameworkElement elemen && string.Equals(elemen.Name, nama, StringComparison.Ordinal))
                {
                    return elemen;
                }

                var temuan = CariAnakBernama(anak, nama);
                if (temuan is not null)
                {
                    return temuan;
                }
            }

            return null;
        }

        /// <summary>
        /// Kembalikan transform aman-animasi milik sebuah elemen. Transform yang
        /// dideklarasikan di XAML templat dibekukan WPF (BeginAnimation langsung
        /// melempar InvalidOperationException), jadi versi bekunya diklon dulu.
        /// </summary>
        private static T SiapkanTransform<T>(FrameworkElement elemen) where T : Transform, new()
        {
            if (elemen.RenderTransform is T ada)
            {
                if (!ada.IsFrozen)
                {
                    return ada;
                }

                var klon = (T)ada.Clone();
                elemen.RenderTransform = klon;
                return klon;
            }

            var baru = new T();
            elemen.RenderTransform = baru;
            return baru;
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
