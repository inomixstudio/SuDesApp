using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SuDesApp.Installer.Services;

namespace SuDesApp.Installer.Views
{
    /// <summary>
    /// Jendela installer kustom (kode, bukan XAML) — kartu radius 14 + emerald
    /// #166534 sama seperti splash &amp; login, judul gradien Primary→Secondary,
    /// lencana logo, strip progres, dan tombol Primary/Secondary.
    /// Alur: Sambutan → Lokasi → Pasang → Selesai (atau Cabut bila --uninstall).
    /// </summary>
    public sealed class InstallerWindow : Window
    {
        private readonly PemasangService _pemasang = new();
        private readonly bool _cabut;
        private int _langkah; // 0 sambut, 1 lokasi, 2 pasang, 3 selesai
        private TextBox _kotakLokasi = null!;
        private CheckBox _centangDesktop = null!;
        private CheckBox _centangJalankan = null!;
        private TextBlock _tahap = null!;
        private ProgressBar _progres = null!;
        private TextBlock _persen = null!;
        private Button _btnKembali = null!;
        private Button _btnLanjut = null!;
        private StackPanel _panelLokasi = null!;
        private StackPanel _panelSelesai = null!;
        private TextBlock _judul = null!;
        private TextBlock _subjudul = null!;
        private Grid _isiLokasi = null!;
        private CancellationTokenSource? _cts;

        private static readonly Color Primer = Color.FromRgb(0x16, 0x65, 0x34);
        private static readonly Color Sekunder = Color.FromRgb(0x14, 0x53, 0x2D);

        public InstallerWindow(bool cabut)
        {
            _cabut = cabut;
            Title = cabut ? "Copot SuDesApp" : "Pasang SuDesApp " + App.Versi;
            Width = 660;
            SizeToContent = SizeToContent.Height;
            MaxWidth = 760;
            MaxHeight = 780;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var kartuLuar = new Border
            {
                Margin = new Thickness(18),
                CornerRadius = new CornerRadius(14),
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 3, Opacity = 0.38, Color = Colors.Black }
            };
            var kartu = new Border
            {
                CornerRadius = new CornerRadius(14),
                ClipToBounds = true,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderThickness = new Thickness(1)
            };
            var grid = new Grid { Margin = new Thickness(22, 10, 22, 18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title bar
            var titleBar = BuatTitleBar();
            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);

            // Kepala gradien emerald
            var kepala = BuatKepala();
            Grid.SetRow(kepala, 1);
            grid.Children.Add(kepala);

            // Isi (diganti per langkah)
            var isi = BuatIsi();
            Grid.SetRow(isi, 2);
            grid.Children.Add(isi);

            // Tombol
            var tombol = BuatTombol();
            Grid.SetRow(tombol, 3);
            grid.Children.Add(tombol);

            kartu.Child = grid;
            kartuLuar.Child = kartu;
            Content = kartuLuar;
            TampilkanLangkah(0);
        }

        private Border BuatTitleBar()
        {
            var bar = new Border
            {
                Margin = new Thickness(-22, 0, -22, 0),
                Height = 30,
                Background = Brushes.White,
            };
            bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) { try { DragMove(); } catch { /* sedang dimodali */ } } };
            var dock = new DockPanel { LastChildFill = false };
            var judul = new TextBlock { Text = Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            dock.Children.Add(judul);
            DockPanel.SetDock(judul, Dock.Left);
            var tutup = new Button { Content = "✕", Width = 36, Height = 24, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)) };
            tutup.Click += (_, _) => { if (_langkah == 2) return; Close(); };
            dock.Children.Add(tutup);
            DockPanel.SetDock(tutup, Dock.Right);
            bar.Child = dock;
            return bar;
        }

        private Border BuatKepala()
        {
            var gradien = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradien.GradientStops.Add(new GradientStop(Primer, 0));
            gradien.GradientStops.Add(new GradientStop(Sekunder, 1));
            gradien.Freeze();
            var kepala = new Border
            {
                Margin = new Thickness(-22, 0, -22, 0),
                Height = 108,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background = gradien,
                ClipToBounds = true,
                Padding = new Thickness(22)
            };
            var baris = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lencana = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(8) };
            try
            {
                var img = new BitmapImage(new Uri("pack://application:,,,/SuDesAppSetup;component/Resources/AppIcon-256.png"));
                lencana.Child = new Image { Source = img, Stretch = Stretch.Uniform };
            }
            catch
            {
                lencana.Child = new TextBlock { Text = "SD", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Primer), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            baris.Children.Add(lencana);
            var teks = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _judul = new TextBlock { Text = _cabut ? "Copot SuDesApp" : "Pasang SuDesApp " + App.Versi, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            _subjudul = new TextBlock { Text = _cabut ? "Hapus berkas aplikasi — data desa tidak dihapus" : "Sistem Surat Desa · Sumberjaya Dev.", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap };
            teks.Children.Add(_judul);
            teks.Children.Add(_subjudul);
            baris.Children.Add(teks);
            kepala.Child = baris;
            return kepala;
        }

        private FrameworkElement BuatIsi()
        {
            var wadah = new Grid { Margin = new Thickness(0, 14, 0, 10) };
            wadah.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wadah.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wadah.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Langkah 0/1: teks sambutan + lokasi (Grid agar membungkus, bukan StackPanel)
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sambutan = new TextBlock
            {
                Text = _cabut
                    ? "Copot SuDesApp dari komputer ini? Berkas aplikasi akan dihapus; database desa (desa.db), template yang sudah diedit, dan hasil PDF TIDAK dihapus."
                    : "Selamat datang di pemasangan SuDesApp " + App.Versi + ". Aplikasi akan dipasang per pengguna (tanpa hak admin) dan data desa yang sudah ada tidak akan ditimpa. Pilih lokasi dan lanjutkan.",
                FontSize = 13,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21))
            };
            g.Children.Add(sambutan);
            Grid.SetRow(sambutan, 0);

            var labelLokasi = new TextBlock { Text = "Lokasi pemasangan:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), Margin = new Thickness(0, 14, 0, 6) };
            g.Children.Add(labelLokasi);
            Grid.SetRow(labelLokasi, 1);

            var baris = new Grid();
            baris.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            baris.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _kotakLokasi = new TextBox { Text = App.LokasiBawaan, FontSize = 13, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 0), MinWidth = 320 };
            baris.Children.Add(_kotakLokasi);
            Grid.SetColumn(_kotakLokasi, 0);
            var btnJelajah = new Button { Content = "Jelajah…", Padding = new Thickness(14, 8, 14, 8), MinWidth = 100, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), Background = Brushes.White };
            btnJelajah.Click += (_, _) =>
            {
                var dlg = new System.Windows.Controls.Primitives.Popup();
                _ = dlg;
                // FolderBrowserDialog sistem Toreh aj disyaratkan — pakai dialog folder persediaan kedua
                var picker = new Microsoft.Win32.OpenFolderDialog();
                picker.Title = "Pilih lokasi pemasangan";
                picker.InitialDirectory = _kotakLokasi.Text;
                var ok = picker.ShowDialog(this);
                if (ok is true && picker.FolderName is { } f) _kotakLokasi.Text = f;
            };
            baris.Children.Add(btnJelajah);
            Grid.SetColumn(btnJelajah, 1);
            g.Children.Add(baris);
            Grid.SetRow(baris, 2);

            var opsi = new Grid();
            opsi.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opsi.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _centangDesktop = new CheckBox { Content = "Buat ikon Desktop", IsChecked = false, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
            _centangJalankan = new CheckBox { Content = "Jalankan SuDesApp setelah pasang", IsChecked = true, FontSize = 12, Margin = new Thickness(16, 10, 0, 0) };
            opsi.Children.Add(_centangDesktop);
            Grid.SetColumn(_centangDesktop, 0);
            opsi.Children.Add(_centangJalankan);
            Grid.SetColumn(_centangJalankan, 1);
            g.Children.Add(opsi);
            Grid.SetRow(opsi, 3);
            _panelLokasi = new StackPanel();
            _panelLokasi.Children.Add(g);

            // Langkah 2: progres
            var progres = new Grid();
            progres.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progres.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progres.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _tahap = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 };
            progres.Children.Add(_tahap);
            Grid.SetRow(_tahap, 0);
            _progres = new ProgressBar { Minimum = 0, Maximum = 100, Height = 10, Margin = new Thickness(0, 10, 0, 6) };
            _progres.Foreground = new SolidColorBrush(Primer);
            progres.Children.Add(_progres);
            Grid.SetRow(_progres, 1);
            _persen = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)) };
            progres.Children.Add(_persen);
            Grid.SetRow(_persen, 2);
            var panelProgres = new StackPanel();
            panelProgres.Children.Add(progres);
            panelProgres.Visibility = Visibility.Collapsed;
            _panelSelesai = new StackPanel();
            _panelSelesai.Visibility = Visibility.Collapsed;

            wadah.Children.Add(panelProgres);
            Grid.SetRow(panelProgres, 0);
            wadah.Children.Add(_panelLokasi);
            Grid.SetRow(_panelLokasi, 1);
            wadah.Children.Add(_panelSelesai);
            Grid.SetRow(_panelSelesai, 2);
            // Simpan referensi progres agar bisa ditampilkan
            _isiLokasi = new Grid();
            wadah.Tag = panelProgres;
            return wadah;
        }

        private FrameworkElement BuatTombol()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var catatan = new TextBlock { Text = "Per pengguna (tanpa hak admin) · Data desa tidak ditimpa", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            grid.Children.Add(catatan);
            Grid.SetColumn(catatan, 0);
            var pil = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _btnKembali = BuatTombolSekunder("Kembali");
            _btnKembali.Click += (_, _) => TampilkanLangkah(Math.Max(0, _langkah - 1));
            pil.Children.Add(_btnKembali);
            var sel = BuatTombolUtama("Lanjut");
            sel.Margin = new Thickness(8, 0, 0, 0);
            sel.Click += async (_, _) => await LanjutAsync();
            _btnLanjut = sel;
            pil.Children.Add(sel);
            grid.Children.Add(pil);
            Grid.SetColumn(pil, 1);
            return grid;
        }

        private static Button BuatTombolUtama(string label)
        {
            var b = new Button { Content = label, MinWidth = 130, Padding = new Thickness(20, 10, 20, 10), Background = new SolidColorBrush(Primer), BorderThickness = new Thickness(0), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
            return b;
        }

        private static Button BuatTombolSekunder(string label)
        {
            var b = new Button { Content = label, MinWidth = 110, Padding = new Thickness(16, 10, 16, 10), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), BorderThickness = new Thickness(1), Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand };
            return b;
        }

        private void TampilkanLangkah(int langkah)
        {
            _langkah = langkah;
            var wadah = (Grid)Content;
            var kartu = (Border)wadah.Children[0];
            var grid = (Grid)kartu.Child;
            var isi = (Grid)grid.Children[2];
            var panelProgres = (StackPanel)isi.Tag;
            bool pasang = langkah == 2;
            panelProgres.Visibility = pasang ? Visibility.Visible : Visibility.Collapsed;
            var aktif = pasang ? (FrameworkElement)panelProgres : (FrameworkElement)_panelLokasi;
            _panelLokasi.Visibility = (langkah == 0 || langkah == 1) ? Visibility.Visible : Visibility.Collapsed;
            _panelSelesai.Visibility = langkah == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (langkah == 3)
            {
                _panelSelesai.Children.Clear();
                _panelSelesai.Children.Add(new TextBlock { Text = _cabut ? "Copot selesai. Data desa tetap ada bila pernah dipasang." : "Pemasangan selesai. SuDesApp " + App.Versi + " siap dipakai.", FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap });
                _panelSelesai.Children.Add(new TextBlock { Text = "Data desa (desa.db, Templates yang sudah diedit, hasil PDF) tidak ditimpa.", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            }
            _btnKembali.Visibility = (langkah == 1) ? Visibility.Visible : Visibility.Collapsed;
            _btnLanjut.Content = langkah switch { 0 => "Lanjut", 1 => _cabut ? "Copot" : "Pasang", 2 => "Tunggu…", _ => "Selesai" };
            _btnLanjut.IsEnabled = langkah != 2;
            _subjudul.Text = langkah switch { 0 => "Sistem Surat Desa · Sumberjaya Dev.", 1 => "Pilih lokasi pemasangan", 2 => "Memasang… jangan tutup jendela ini", _ => "Selesai" };
        }

        private async Task LanjutAsync()
        {
            if (_langkah == 0) { TampilkanLangkah(1); return; }
            if (_langkah == 1)
            {
                var lokasi = _kotakLokasi.Text.Trim();
                if (string.IsNullOrWhiteSpace(lokasi))
                {
                    MessageBox.Show(this, "Lokasi pemasangan belum diisi.", "Pasang SuDesApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                TampilkanLangkah(2);
                _cts = new CancellationTokenSource();
                var lapor = new Progress<PemasangService.InstallStep>(s =>
                {
                    _tahap.Text = s.Teks;
                    _progres.Value = s.Persen;
                    _persen.Text = s.Persen + " %";
                });
                try
                {
                    var sumber = _pemasang.CariPayloadEkstrak(lapor, default);
                    if (_cabut)
                    {
                        await _pemasang.CabutAsync(lokasi, lapor, default);
                    }
                    else
                    {
                        await _pemasang.PasangAsync(sumber, lokasi, _centangDesktop.IsChecked is true, lapor, _cts.Token);
                    }
                    if (_langkah == 2) TampilkanLangkah(3);
                    if (_langkah == 3 && _centangJalankan.IsChecked is true && !_cabut)
                    {
                        var exe = Path.Combine(lokasi, "SuDesApp.exe");
                        if (File.Exists(exe))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Pemasangan gagal", MessageBoxButton.OK, MessageBoxImage.Error);
                    TampilkanLangkah(1);
                }
                return;
            }
            if (_langkah == 3) Close();
        }
    }
}
