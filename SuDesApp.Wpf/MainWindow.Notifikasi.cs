using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf
{
    /// <summary>Lonceng notifikasi: flyout, toast, dan animasi lonceng.</summary>
    public partial class MainWindow
    {
        private Storyboard? _bellStoryboard;

        /// <summary>Waktu terakhir flyout notifikasi ditutup — dipakai untuk
        /// membedakan klik penutup (mouse up pada lonceng) dari klik buka.</summary>
        private DateTime _flyoutClosedAt = DateTime.MinValue;

        /// <summary>Toast Warning/Error: satu penunjuk waktu, tampil 6 detik.</summary>
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

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
        private void NotificationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotificationList.SelectedItem is not NotificationItem item) return;

            NotificationList.SelectedIndex = -1; // boleh diklik lagi lain waktu
            NotificationFlyout.IsOpen = false;
            _viewModel.Notifications.MarkAllRead();

            PenangananGalatUi.Jalankan(
                () => _viewModel.BukaTujuanNotifikasiAsync(item),
                "Navigasi notifikasi");
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
    }
}
