using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Statusbar bawah halaman yang dipakai bersama: pesan status (kiri) +
    /// identitas aplikasi — ikon, nama, versi, pengembang, bisa diklik untuk
    /// membuka Tentang Aplikasi — plus tombol aksi opsional (kanan).
    /// Dipakai di Pengaturan Aplikasi, Pengaturan Surat, Catatan Rilis,
    /// Panduan WhatsApp, Ubah Kata Sandi, dan Pembaruan.
    /// Nama/versi/pengembang dibaca dari atribut assembly sehingga ikut berubah
    /// begitu versi di csproj dinaikkan.
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

        /// <summary>Chip ikon + nama + versi + pengembang; klik membuka Tentang Aplikasi.</summary>
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

            var ikon = new Image
            {
                Width = 16, Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Source = new BitmapImage(new Uri("pack://application:,,,/Resources/AppIcon-256.png"))
            };
            RenderOptions.SetBitmapScalingMode(ikon, BitmapScalingMode.HighQuality);
            panel.Children.Add(ikon);

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

                var pengembang = new TextBlock
                {
                    Text = "Pengembang: " + Pengembang,
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
        private static Assembly AssemblyAplikasi => Assembly.GetExecutingAssembly();

        private static string NamaAplikasi =>
            AssemblyAplikasi.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } produk
                ? produk
                : "SuDesApp";

        private static string Pengembang =>
            AssemblyAplikasi.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

        private static string HakCipta =>
            AssemblyAplikasi.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        private static string VersiAplikasi
        {
            get
            {
                var versi = AssemblyAplikasi.GetName().Version;
                if (versi == null) return string.Empty;

                // 2.5.3.0 → "v2.5.3": nomor revisi hanya ikut bila benar-benar dipakai.
                var teks = versi.Revision > 0
                    ? $"{versi.Major}.{versi.Minor}.{versi.Build}.{versi.Revision}"
                    : $"{versi.Major}.{versi.Minor}.{versi.Build}";
                return "v" + teks;
            }
        }
    }
}
