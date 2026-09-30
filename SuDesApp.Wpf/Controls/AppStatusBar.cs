using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Statusbar bawah halaman yang dipakai bersama: pesan status (kiri) +
    /// identitas aplikasi — ikon, nama, versi, nama pengembang, bisa diklik untuk
    /// membuka Tentang Aplikasi — plus tombol aksi opsional (kanan).
    /// Dipakai di Pengaturan Aplikasi, Pengaturan Surat, Pengaturan Formulir,
    /// Catatan Rilis, Panduan WhatsApp, dan Pembaruan.
    /// Nama/versi/pengembang dibaca dari atribut assembly sehingga ikut berubah
    /// begitu versi di csproj dinaikkan; nama pengembang ditulis apa adanya
    /// (mis. "Sumberjaya Dev.") tanpa label tambahan.
    /// </summary>
    [System.Windows.Markup.ContentProperty(nameof(Buttons))]
    public class AppStatusBar : Border
    {
        private readonly TextBlock _statusText;

        public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
            nameof(StatusText), typeof(string), typeof(AppStatusBar),
            new PropertyMetadata(null, OnStatusTextChanged));

        public AppStatusBar()
        {
            // Kerangka bar: Border membulat berlatar permukaan (mengikuti tema).
            SetResourceReference(BackgroundProperty, "SurfaceBrush");
            SetResourceReference(BorderBrushProperty, "BorderBrush");
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(14, 8, 14, 8);
            SnapsToDevicePixels = true;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Child = grid;

            // ----- Kiri: garis aksen + pesan status -----
            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var aksen = new Border
            {
                Width = 3, Height = 15, CornerRadius = new CornerRadius(1.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            aksen.SetResourceReference(BackgroundProperty, "AccentBrush");
            statusPanel.Children.Add(aksen);

            _statusText = new TextBlock
            {
                Margin = new Thickness(9, 0, 0, 0),
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json")
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            statusPanel.Children.Add(_statusText);
            Grid.SetColumn(statusPanel, 0);
            grid.Children.Add(statusPanel);

            // ----- Kanan: chip identitas aplikasi + tombol tambahan -----
            var chipIdentitas = BuildChipIdentitas();
            Grid.SetColumn(chipIdentitas, 1);
            chipIdentitas.Margin = new Thickness(0, 0, 12, 0);
            grid.Children.Add(chipIdentitas);

            var tombolHost = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tombolHost, 2);
            grid.Children.Add(tombolHost);

            Buttons.CollectionChanged += (_, _) => RefreshButtons(tombolHost);
            RefreshButtons(tombolHost);

            OnStatusTextChanged(this, new DependencyPropertyChangedEventArgs(
                StatusTextProperty, null, StatusText));
        }

        /// <summary>Pesan status di sisi kiri bar. Bila kosong, dipakai teks bawaan "Siap".</summary>
        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        /// <summary>Tombol aksi tambahan di sisi kanan bar (mis. Batal/Simpan).</summary>
        public ObservableCollection<UIElement> Buttons { get; } = new();

        private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var bar = (AppStatusBar)d;
            var teks = e.NewValue as string;
            if (string.IsNullOrWhiteSpace(teks)) teks = "Siap";
            bar._statusText.Text = teks;
        }

        private void RefreshButtons(StackPanel host)
        {
            host.Children.Clear();
            foreach (var tombol in Buttons)
            {
                host.Children.Add(tombol);
            }
        }

        /// <summary>
        /// Ikon aplikasi untuk chip identitas, dibaca dari rakitan yang MEMUAT kontrol ini
        /// (bukan rakitan pemanggil), lalu alamat gaya lama sebagai cadangan. Bila ikonnya
        /// tidak ada, chip tetap tampil dengan nama &amp; versi — kegagalan memuat ikon
        /// tidak boleh membuat seluruh halaman gagal dibuka.
        /// </summary>
        private static BitmapSource? MuatIkonAplikasi()
        {
            string[] alamat =
            {
                "pack://application:,,,/SuDesApp;component/Resources/AppIcon-256.png",
                "pack://application:,,,/Resources/AppIcon-256.png"
            };

            foreach (var satu in alamat)
            {
                try
                {
                    var gambar = new BitmapImage();
                    gambar.BeginInit();
                    gambar.UriSource = new Uri(satu, UriKind.Absolute);
                    gambar.CacheOption = BitmapCacheOption.OnLoad;
                    gambar.EndInit();
                    return gambar;
                }
                catch (Exception)
                {
                    // Coba alamat berikutnya; tanpa ikon pun statusbar tetap berguna.
                }
            }

            return null;
        }

        /// <summary>Chip ikon + nama + versi + nama pengembang; klik membuka Tentang Aplikasi.</summary>
        private Border BuildChipIdentitas()
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 4, 9, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            chip.SetResourceReference(BackgroundProperty, "PanelBrush");
            chip.SetResourceReference(BorderBrushProperty, "BorderBrush");
            chip.BorderThickness = new Thickness(1);
            chip.ToolTip = string.IsNullOrEmpty(HakCipta)
                ? "Identitas aplikasi — klik untuk membuka halaman Tentang Aplikasi"
                : HakCipta;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var ikonAplikasi = MuatIkonAplikasi();
            if (ikonAplikasi != null)
            {
                var ikon = new Image
                {
                    Width = 16, Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Source = ikonAplikasi
                };
                RenderOptions.SetBitmapScalingMode(ikon, BitmapScalingMode.HighQuality);
                panel.Children.Add(ikon);
            }

            var nama = new TextBlock
            {
                Text = NamaAplikasi,
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            nama.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(nama);

            var pilVersi = new Border
            {
                Margin = new Thickness(7, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            pilVersi.SetResourceReference(BackgroundProperty, "AccentSubtleBrush");
            var versi = new TextBlock
            {
                Text = VersiAplikasi,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            versi.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            pilVersi.Child = versi;
            panel.Children.Add(pilVersi);

            if (!string.IsNullOrEmpty(Pengembang))
            {
                var pemisah = new Border
                {
                    Width = 1, Margin = new Thickness(9, 1, 9, 1),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                pemisah.SetResourceReference(BackgroundProperty, "BorderBrush");
                panel.Children.Add(pemisah);

                // Hanya nama pengembang yang ditulis (tanpa kata "Pengembang:") —
                // chip ini sudah jelas bagian identitas aplikasi.
                var pengembang = new TextBlock
                {
                    Text = Pengembang,
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                pengembang.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                panel.Children.Add(pengembang);
            }

            chip.Child = panel;

            chip.InputBindings.Add(new MouseBinding
            {
                Gesture = new MouseGesture(MouseAction.LeftClick),
                Command = new RelayCommand(BukaTentang)
            });
            return chip;
        }

        private void BukaTentang()
        {
            try
            {
                var nav = ((App)Application.Current).ServiceProvider
                    .GetRequiredService<ViewModels.NavigationService>();
                nav.Navigate<ViewModels.AboutViewModel>();
            }
            catch
            {
                // Navigasi tidak tersedia (mis. saat shutdown) — abaikan.
            }
        }

        // ----- Identitas dari atribut assembly (sumber sama dengan Tentang Aplikasi) -----
        // Pembacaannya dipusatkan di IdentitasAplikasi (Utilities) agar satu sumber kebenaran.
        private static string NamaAplikasi => IdentitasAplikasi.Nama;

        private static string Pengembang => IdentitasAplikasi.Pengembang;

        private static string HakCipta => IdentitasAplikasi.HakCipta;

        private static string VersiAplikasi => IdentitasAplikasi.VersiDenganPrefiks;
    }
}
