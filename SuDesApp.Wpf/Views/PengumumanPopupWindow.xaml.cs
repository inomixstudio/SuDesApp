using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Popup pengumuman: kartu kecil di pojok kanan-bawah layar (di atas tray),
    /// bukan dialog modal — operator tidak perlu menghentikan pekerjaannya
    /// hanya karena ada pemberitahuan baru. Satu instance dipakai ulang bila
    /// masih tampil: daftar diperbarui dan timer penutup dimulai ulang, bukan
    /// menumpuk dua jendela di satu tempat.
    /// </summary>
    public partial class PengumumanPopupWindow : Window
    {
        /// <summary>Durasi popup tampil sebelum menutup dirinya sendiri.</summary>
        private static readonly TimeSpan DurasiTampil = TimeSpan.FromSeconds(25);

        private static PengumumanPopupWindow? _instance;

        private readonly DispatcherTimer _timerTutup;

        private PengumumanPopupWindow()
        {
            InitializeComponent();

            _timerTutup = new DispatcherTimer { Interval = DurasiTampil };
            _timerTutup.Tick += (_, _) => Tutup();

            // Posisi dihitung SETELAH konten dirender: dengan SizeToContent,
            // ActualHeight masih NaN sebelum itu sehingga kartu bisa muncul
            // di luar layar atau menonjol keluar taskbar.
            ContentRendered += (_, _) => Posisikan();
        }

        /// <summary>
        /// Titik masuk dari <c>PengumumanLatar</c>: tampilkan daftar pengumuman.
        /// Tanpa Dispatcher (mode uji / sudah menutup) diabaikan dengan aman.
        /// </summary>
        public static void Tampilkan(IEnumerable<ItemPengumumanTampil> daftar)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                var popup = _instance ??= new PengumumanPopupWindow();
                popup.DaftarPengumuman.ItemsSource = daftar;
                popup.TampilkanDenganAutoTutup();
            }));
        }

        private void TampilkanDenganAutoTutup()
        {
            if (!IsVisible)
            {
                Show();
            }

            _timerTutup.Stop();
            _timerTutup.Start();
        }

        /// <summary>Taruh kartu di kanan-bawah AREA KERJA (di atas taskbar).</summary>
        private void Posisikan()
        {
            var area = SystemParameters.WorkArea;
            var lebar = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? Width : ActualWidth;
            var tinggi = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? 240 : ActualHeight;

            Left = Math.Max(area.Left, area.Right - lebar - 16);
            Top = Math.Max(area.Top, area.Bottom - tinggi - 16);
        }

        private void Tutup_Click(object sender, RoutedEventArgs e) => Tutup();

        private void Tutup()
        {
            _timerTutup.Stop();
            Hide();
        }

        /// <summary>Instance dilepas agar tidak bocor saat aplikasi keluar.</summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _instance = null;
        }
    }
}
