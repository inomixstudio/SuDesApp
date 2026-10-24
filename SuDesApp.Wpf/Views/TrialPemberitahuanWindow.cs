using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Popup pemberitahuan lisensi/trial yang meniru <see cref="SplashStartupWindow"/>
    /// secara persis — ukuran jendela sama (500×300), kartu membulat dengan
    /// bayangan yang sama, kepala bergradien Primary→Secondary yang sama (lencana
    /// logo + ornamen mutiara ikut, karena kepala dibangun lewat builder yang
    /// sama), dan seluruh warna dari tema yang sama. Laporan pengguna: popup pesan
    /// umum terlalu polos di samping splash; dengan popup ini transisi
    /// splash → pemberitahuan trial terasa seperti satu layar yang berlanjut,
    /// bukan dialog lain yang dibuang dari gaya aplikasi.
    ///
    /// Sengaja dibangun dari kode, bukan XAML — pola yang sama dengan splash —
    /// supaya kepala kartu tetap satu salinan dengan splash.
    ///
    /// Modal &amp; input: tombol utama = Enter (IsDefault), tombol kedua (atau
    /// tombol utama pada mode pesan) = Esc (IsCancel); kepala kartu bisa diseret.
    /// Tanpa tombol tutup di header — bentuknya mengikuti splash, yang memang
    /// tidak punya chrome jendela.
    /// </summary>
    public sealed class TrialPemberitahuanWindow : Window
    {
        private bool _utamaDitekan;

        /// <summary>
        /// Popup tanya (dua tombol). Mengembalikan true hanya bila tombol utama
        /// ditekan; Esc/tombol kedua/tutup = false — pola hasil yang sama dengan
        /// <c>MessageDialogWindow.Show</c> (YesNo) supaya pemanggil di App
        /// tidak perlu menafsirkan ulang makna "diterima".
        /// </summary>
        public static bool Pilih(string judul, string pesan, string tombolUtama, string tombolKedua)
        {
            var dlg = Buat(judul, pesan, tombolUtama, tombolKedua);
            System.Media.SystemSounds.Question.Play();
            dlg.ShowDialog();
            return dlg._utamaDitekan;
        }

        /// <summary>Popup pesan satu tombol (OK) — dipakai hasil trial &amp; pengingat.</summary>
        public static void Pesan(string judul, string pesan, bool nadaPeringatan = false)
        {
            var dlg = Buat(judul, pesan, "OK", null);
            if (nadaPeringatan) System.Media.SystemSounds.Exclamation.Play();
            else System.Media.SystemSounds.Asterisk.Play();
            dlg.ShowDialog();
        }

        private static TrialPemberitahuanWindow Buat(
            string judul, string pesan, string tombolUtama, string? tombolKedua)
        {
            // Owner = jendela aktif, pola yang sama dengan MessageDialogWindow.Show,
            // supaya popup selalu modal di depan aplikasi.
            var owner = Application.Current?.Windows.OfType<Window>()
                            .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

            var dlg = new TrialPemberitahuanWindow(
                SplashStartupWindow.AmbilWarnaTema(null),
                judul, pesan, tombolUtama, tombolKedua);
            if (owner != null && owner != dlg)
            {
                dlg.Owner = owner;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            return dlg;
        }

        private TrialPemberitahuanWindow(
            SplashStartupWindow.WarnaTema warna,
            string judul, string pesan, string tombolUtama, string? tombolKedua)
        {
            Title = "SuDesApp";
            Width = 500;          // SAMA dengan SplashStartupWindow: 460 px kartu + ruang bayangan
            Height = 300;         // SAMA dengan SplashStartupWindow
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Opacity = 0;

            // Kartu: pola dan angka yang persis sama dengan splash (border tipis,
            // sudut 14, bayangan lembut 26/3/0.38, permukaan dari tema).
            var kartu = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = SplashStartupWindow.Warna(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
                Background = warna.Permukaan,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 3,
                    Opacity = 0.38,
                    Color = Colors.Black
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // kepala
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // isi
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tombol

            // Kepala adalah builder yang SAMA dengan splash; judul popup menjadi
            // judul kepala, dengan penanda aplikasi tetap ada di bawahnya.
            // Kepala adalah builder yang SAMA dengan splash; judul popup menjadi
            // judul kepala, dengan penanda aplikasi tetap ada di bawahnya. Tingginya
            // sedang lebih pendek dari splash supaya pesan utuh terbaca tanpa
            // scrollbar di ketinggian kartu yang sama.
            var kepala = SplashStartupWindow.BuatKepala(warna, judul, "SuDesApp · Sistem Surat Desa", tinggi: 104);
            kepala.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { /* jendela sedang dimodali */ }
                }
            };
            grid.Children.Add(kepala);
            Grid.SetRow(kepala, 0);

            // Isi: pesan pengguna — dibungkus scroll agar teks panjang tetap wajar
            // di dalam tinggi kartu yang terpasang (300, sama dengan splash).
            var isi = new ScrollViewer
            {
                Margin = new Thickness(26, 14, 26, 6),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            isi.Content = new TextBlock
            {
                Text = pesan,
                FontSize = 13,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Foreground = warna.Teks
            };
            grid.Children.Add(isi);
            Grid.SetRow(isi, 1);

            // Tombol: gaya tombol aplikasi (tema yang sama), utama kanan bawah
            // seperti kartu-kartu lain di aplikasi ini.
            var barisTombol = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(26, 2, 26, 14)
            };

            var bUtama = new Button
            {
                Content = tombolUtama,
                MinWidth = 140,
                Height = 34,
                Margin = new Thickness(0, 0, tombolKedua is null ? 0 : 10, 0),
                IsDefault = tombolKedua is null, // mode pesan: Enter = OK
                IsCancel = tombolKedua is null,  // mode pesan: Esc = tutup
                Style = Application.Current?.TryFindResource("PrimaryButton") as Style
            };
            bUtama.Click += (_, _) => { _utamaDitekan = true; DialogResult = true; };
            barisTombol.Children.Add(bUtama);

            if (tombolKedua is not null)
            {
                var bKedua = new Button
                {
                    Content = tombolKedua,
                    MinWidth = 110,
                    Height = 34,
                    IsDefault = true,  // mode tanya: Enter = pilihan utama
                    IsCancel = true,   // Esc = menolak
                    Style = Application.Current?.TryFindResource("SecondaryButton") as Style
                };
                bKedua.Click += (_, _) => DialogResult = false;
                barisTombol.Children.Add(bKedua);
            }

            grid.Children.Add(barisTombol);
            Grid.SetRow(barisTombol, 2);

            kartu.Child = grid;
            Content = new Grid { Margin = new Thickness(18) };
            ((Grid)Content).Children.Add(kartu);

            // Muncul pelan — animasi yang sama dengan splash, jadi perpindahan
            // splash → popup ini terasa satu kelanjutan.
            Loaded += (_, _) =>
            {
                bUtama.Focus();
                var muncul = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, muncul);
            };
        }
    }
}
