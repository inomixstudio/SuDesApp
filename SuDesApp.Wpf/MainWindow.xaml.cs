using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

            // Lonceng berayun setiap ada notifikasi baru masuk.
            viewModel.Notifications.NotificationAdded += Notifications_NotificationAdded;

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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Dialog bertema; sinkron karena event Closing harus langsung tahu hasilnya.
            var result = SuDesApp.Wpf.Views.MessageDialogWindow.Show(
                "Konfirmasi Keluar",
                "Yakin ingin keluar dari aplikasi?",
                SuDesApp.Utilities.AppMessageButton.YesNo,
                SuDesApp.Utilities.AppMessageIcon.Question);

            if (result != true)
            {
                e.Cancel = true;
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
            // Toggle flyout notifikasi; klik di luar popup menutupnya otomatis
            // (StaysOpen=False) dan menandai semua sudah dibaca saat terbuka.
            if (NotificationFlyout.IsOpen)
            {
                NotificationFlyout.IsOpen = false;
                return;
            }

            NotificationFlyout.IsOpen = true;
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

        private void Notifications_NotificationAdded(object? sender, System.EventArgs e)
        {
            // Event dari service dijamin di UI thread (Dispatcher.Invoke di service).
            AnimateBellRing();
        }

        private void NotificationClear_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Notifications.Clear();
            NotificationFlyout.IsOpen = false;
        }
    }
}
