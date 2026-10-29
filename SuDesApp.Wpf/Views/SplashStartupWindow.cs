using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Jendela pembuka yang tampil seketika saat aplikasi dimulai — jauh sebelum
    /// jendela login. Pekerjaan berat pada detik pertama (skema dan migrasi)
    /// berjalan di latar belakang, dan jendela ini memberi
    /// tahu tahapannya agar aplikasi tidak terasa "macet" saat baru dibuka.
    ///
    /// Sengaja dibangun dari kode, bukan XAML, agar muncul sebelum
    /// ResourceDictionary tema dimuat. Warna TETAP diambil dari tema yang
    /// dipilih pengguna (theme.config) langsung oleh ThemeService — bila gagal,
    /// jatuh ke resource aplikasi (hijau bawaan), lalu ke warna tetap.
    ///
    /// Bentuk: kartu membulat dengan bayangan lembut, kepala bergradien
    /// Primary→Secondary (pola header aplikasi), lencana logo, strip progres,
    /// dan animasi fade-in singkat.
    ///
    /// Indikator tahap: sederet titik di atas teks tahap. Titik yang sedang
    /// berjalan berdenyut pelan; begitu tahapnya selesai, titik itu terisi penuh
    /// dan centangnya tergores masuk dari kiri ke kanan. Teks tahap baru memudar
    /// masuk sambil naik sedikit, jadi perpindahan tahap terasa mengalir.
    /// </summary>
    public sealed class SplashStartupWindow : Window
    {
        private readonly TextBlock _tahap;
        private readonly StackPanel _deretTahap;
        private readonly TranslateTransform _geserTahap = new TranslateTransform();
        private readonly WarnaTema _warna;

        /// <summary>Titik tahap yang sedang berjalan (belum diberi centang).</summary>
        private IndikatorTahap? _indikatorAktif;

        /// <summary>Teks tahap terakhir yang ditampilkan — panggilan berulang dengan
        /// teks yang sama diabaikan agar tidak menambah titik tahap palsu.</summary>
        private string? _teksTahapTerakhir;

        public SplashStartupWindow() : this(null)
        {
        }

        /// <summary>
        /// <paramref name="paksaTema"/> menimpa tema tersimpan — dipakai pengujian
        /// render agar preferensi tema pengguna asli tidak disentuh.
        /// </summary>
        public SplashStartupWindow(string? paksaTema)
        {
            Title = "SuDesApp - memulai";
            Width = 500;          // 460 px kartu + ruang bayangan di semua sisi
            Height = 300;         // cukup untuk kepala, deret titik tahap, dan teks
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Opacity = 0;

            var warna = AmbilWarnaTema(paksaTema);
            _warna = warna;

            var kartu = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = Warna(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
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
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // strip progres
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // isi

            grid.Children.Add(BuatKepala(warna, "SuDesApp", "Sistem Surat Desa"));
            Grid.SetRow(grid.Children[0], 0);
            grid.Children.Add(BuatStripProgres(warna));
            Grid.SetRow(grid.Children[1], 1);
            var (isi, tahap, deretTahap) = BuatIsi(warna);
            _tahap = tahap;
            _deretTahap = deretTahap;
            grid.Children.Add(isi);
            Grid.SetRow(grid.Children[2], 2);

            kartu.Child = grid;

            var wadah = new Grid { Margin = new Thickness(18) };
            wadah.Children.Add(kartu);
            Content = wadah;

            // Muncul pelan agar terasa halus, bukan mendadak.
            Loaded += (_, _) =>
            {
                var muncul = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, muncul);
            };
        }

        /// <summary>
        /// Kepala bergradien yang dipakai splash DAN popup pemberitahuan trial
        /// (<see cref="TrialPemberitahuanWindow"/>) — judul/subjudulnya menjadi
        /// parameter agar ikon lencana, ornamen mutiara, dan gradiennya tetap
        /// satu salinan untuk keduanya. Tingginya bisa diperkecil oleh popup
        /// (yang butuh ruang teks lebih banyak di bawah kepala); splash tetap
        /// memakai tinggi penuh bawaannya.
        /// </summary>
        internal static FrameworkElement BuatKepala(
            WarnaTema warna, string judul, string subjudul, double tinggi = 124)
        {
            var gradien = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            // Pola gradien kepala aplikasi: PrimaryColor -> SecondaryColor
            // (kedua ujungnya sudah teruji kontrasnya oleh check-kontras-wcag).
            gradien.GradientStops.Add(new GradientStop(warna.Primer, 0));
            gradien.GradientStops.Add(new GradientStop(warna.Sekunder, 1));
            gradien.Freeze();

            var kepala = new Border
            {
                Height = tinggi,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Background = gradien,
                ClipToBounds = true
            };

            var isi = new Grid();

            // Dua lingkaran tembus pandang sebagai ornamen halus di sudut kanan.
            var hiasan = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            var mutiaraBesar = new System.Windows.Shapes.Ellipse
            {
                Width = 190,
                Height = 190,
                Fill = Warna(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))
            };
            Canvas.SetLeft(mutiaraBesar, 322);
            Canvas.SetTop(mutiaraBesar, -78);
            hiasan.Children.Add(mutiaraBesar);
            var mutiaraKecil = new System.Windows.Shapes.Ellipse
            {
                Width = 96,
                Height = 96,
                Fill = Warna(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF))
            };
            Canvas.SetLeft(mutiaraKecil, 356);
            Canvas.SetTop(mutiaraKecil, 82);
            hiasan.Children.Add(mutiaraKecil);
            isi.Children.Add(hiasan);

            // Baris lencana + judul.
            var baris = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(26, 0, 26, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var lencana = new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(26),
                Background = Warna(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(8)
            };
            lencana.Child = BuatLogo(warna);
            baris.Children.Add(lencana);

            var teks = new StackPanel { Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            teks.Children.Add(new TextBlock
            {
                Text = judul,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = warna.OnPrimer
            });
            teks.Children.Add(new TextBlock
            {
                Text = subjudul,
                FontSize = 13,
                Foreground = warna.OnPrimerHalus,
                Margin = new Thickness(0, 2, 0, 0)
            });
            baris.Children.Add(teks);

            isi.Children.Add(baris);
            kepala.Child = isi;
            return kepala;
        }

        internal static FrameworkElement BuatLogo(WarnaTema warna)
        {
            try
            {
                var gambar = new BitmapImage(new Uri("pack://application:,,,/SuDesApp;component/Resources/AppIcon-256.png"));
                return new Image { Source = gambar, Stretch = Stretch.Uniform };
            }
            catch
            {
                // Bila berkas ikon tidak ada, pakai lencana huruf agar tetap rapi.
                return new TextBlock
                {
                    Text = "SD",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = warna.PrimerBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }

        private static FrameworkElement BuatStripProgres(WarnaTema warna)
        {
            // Strip tipis penuh lebar di batas kepala–isi: progres tak tentu
            // berjalan pelan, warna mengikuti aksen tema.
            return new ProgressBar
            {
                IsIndeterminate = true,
                Height = 4,
                BorderThickness = new Thickness(0),
                Background = warna.PrimerBrush,
                Foreground = warna.OnPrimer
            };
        }

        /// <summary>
        /// Isi kartu: deret titik tahap di atas teks tahap yang sedang berjalan,
        /// lalu keterangan singkat di bawahnya.
        /// </summary>
        private (FrameworkElement, TextBlock, StackPanel) BuatIsi(WarnaTema warna)
        {
            var isi = new StackPanel
            {
                Margin = new Thickness(26, 15, 26, 18),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Titik tahap bertambah satu per satu lewat Tahap(): yang berjalan
            // berdenyut, yang selesai berubah menjadi centang.
            var deret = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 11)
            };
            isi.Children.Add(deret);

            var tahap = new TextBlock
            {
                Text = "Menyiapkan aplikasi...",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = warna.Teks,
                TextWrapping = TextWrapping.Wrap,
                RenderTransform = _geserTahap
            };
            isi.Children.Add(tahap);

            isi.Children.Add(new TextBlock
            {
                Text = "Biasanya hanya membutuhkan beberapa detik.",
                FontSize = 11,
                Foreground = warna.TeksHalus,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            return (isi, tahap, deret);
        }

        /// <summary>
        /// Perbarui tahap yang sedang berjalan. Tahap sebelumnya langsung diberi
        /// centang, satu titik baru muncul untuk tahap ini, dan teksnya memudar
        /// masuk sambil naik sedikit — jadi perpindahan tahap terasa mengalir,
        /// bukan teks yang berganti mendadak. Aman dipanggil dari thread UI
        /// (pemanggilan antar tahap selalu berada di thread UI setelah await).
        /// </summary>
        public void Tahap(string teks)
        {
            if (string.IsNullOrWhiteSpace(teks) || teks == _teksTahapTerakhir)
            {
                return;
            }
            _teksTahapTerakhir = teks;

            // Tahap sebelumnya selesai: titiknya berubah menjadi centang.
            if (_indikatorAktif is not null)
            {
                Selesaikan(_indikatorAktif);
            }
            _indikatorAktif = TambahTitikTahap();

            // Teks tahap baru memudar masuk sambil naik sedikit dari bawah.
            _tahap.Text = teks;
            var durasi = KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(240));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            _tahap.BeginAnimation(UIElement.OpacityProperty, null);
            _geserTahap.BeginAnimation(TranslateTransform.YProperty, null);
            _tahap.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, durasi) { EasingFunction = ease });
            _geserTahap.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(7, 0, durasi) { EasingFunction = ease });
        }

        /// <summary>
        /// Tambah satu titik tahap (yang sedang berjalan) ke deret indikator.
        /// Bila sudah ada titik sebelumnya, garis penghubung di antaranya ikut
        /// menyala supaya deretnya terbaca sebagai satu jalur progres.
        /// </summary>
        private IndikatorTahap TambahTitikTahap()
        {
            if (_deretTahap.Children.Count > 0)
            {
                var konektor = new Rectangle
                {
                    Width = 14,
                    Height = 2,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = _warna.PrimerBrush,
                    Margin = new Thickness(5, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0
                };
                _deretTahap.Children.Add(konektor);
                konektor.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 0.85,
                        KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(260)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }

            var titik = BuatTitikTahap();
            _deretTahap.Children.Add(titik.Akar);

            // Titik baru muncul dengan memudar halus, tidak mendadak.
            titik.Akar.Opacity = 0;
            titik.Akar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1,
                    KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(200))));

            Denyutkan(titik);
            return titik;
        }

        /// <summary>Bangun elemen satu titik tahap: halo denyut, cincin, isi, centang.</summary>
        private IndikatorTahap BuatTitikTahap()
        {
            var wadah = new Grid
            {
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Halo yang berdenyut pelan selama tahap ini masih berjalan.
            var skalaDenyut = new ScaleTransform(0.7, 0.7);
            var denyut = new Ellipse
            {
                Width = 18,
                Height = 18,
                Stroke = _warna.PrimerBrush,
                StrokeThickness = 2,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransform = skalaDenyut,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            wadah.Children.Add(denyut);

            var cincin = new Ellipse
            {
                Width = 18,
                Height = 18,
                Stroke = _warna.PrimerBrush,
                StrokeThickness = 1.6
            };
            wadah.Children.Add(cincin);

            // Isi penuh yang membulat saat tahap selesai.
            var skalaIsi = new ScaleTransform(0.55, 0.55);
            var isi = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = _warna.PrimerBrush,
                Opacity = 0,
                RenderTransform = skalaIsi,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            wadah.Children.Add(isi);

            // Centang di atas isi — digores masuk lewat animasi dash offset:
            // pola dash lebih panjang dari garisnya, jadi offset besar = tak
            // terlihat dan offset 0 = tergambar penuh. Koordinatnya sengaja
            // ditulis langsung (Stretch tetap None) supaya pas di tengah titik.
            var centang = new Path
            {
                Data = Geometry.Parse("M 4.6,9.4 L 7.5,12.3 L 13.6,6"),
                Stroke = _warna.OnPrimer,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashArray = new DoubleCollection { 16, 16 },
                StrokeDashOffset = 16,
                Opacity = 0
            };
            wadah.Children.Add(centang);

            return new IndikatorTahap
            {
                Akar = wadah,
                Denyut = denyut,
                SkalaDenyut = skalaDenyut,
                Cincin = cincin,
                Isi = isi,
                SkalaIsi = skalaIsi,
                Centang = centang
            };
        }

        /// <summary>
        /// Denyut penanda "sedang bekerja": lingkaran luar membesar sambil memudar,
        /// berulang. Durasinya ikut pilihan kecepatan animasi pengguna.
        /// </summary>
        private static void Denyutkan(IndikatorTahap titik)
        {
            var durasi = KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(1400));
            var ease = new SineEase { EasingMode = EasingMode.EaseOut };

            titik.Denyut.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0.45, 0, durasi)
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                });

            var membesar = new DoubleAnimation(0.7, 1.55, durasi)
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease
            };
            titik.SkalaDenyut.BeginAnimation(ScaleTransform.ScaleXProperty, membesar);
            titik.SkalaDenyut.BeginAnimation(ScaleTransform.ScaleYProperty, membesar);
        }

        /// <summary>
        /// Tandai sebuah tahap selesai: denyut berhenti, titik terisi penuh, lalu
        /// centang tergores masuk. Urutannya bertingkat (isi dulu, centang menyusul)
        /// supaya terasa seperti pekerjaan yang tuntas, bukan satu lompatan.
        /// </summary>
        private static void Selesaikan(IndikatorTahap titik)
        {
            // Animasi berulang harus dilepas eksplisit sebelum nilainya diganti.
            titik.Denyut.BeginAnimation(UIElement.OpacityProperty, null);
            titik.SkalaDenyut.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            titik.SkalaDenyut.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            titik.Denyut.Opacity = 0;

            var durasiIsi = KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(240));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Cincin memudar memberi ruang pada isi yang membulat penuh.
            titik.Cincin.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, durasiIsi) { EasingFunction = ease });

            var membulat = new DoubleAnimation(0.55, 1, durasiIsi)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
            };
            titik.SkalaIsi.BeginAnimation(ScaleTransform.ScaleXProperty, membulat);
            titik.SkalaIsi.BeginAnimation(ScaleTransform.ScaleYProperty, membulat);
            titik.Isi.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));

            // Centang menyusul sejenak setelah isi terisi.
            titik.Centang.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80))
                {
                    BeginTime = TimeSpan.FromMilliseconds(70)
                });
            titik.Centang.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(16, 0, KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(280)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(70),
                    EasingFunction = ease
                });
        }

        /// <summary>Elemen satu titik tahap beserta bagian yang dianimasikan.</summary>
        private sealed class IndikatorTahap
        {
            public FrameworkElement Akar = null!;
            public Ellipse Denyut = null!;
            public ScaleTransform SkalaDenyut = null!;
            public Ellipse Cincin = null!;
            public Ellipse Isi = null!;
            public ScaleTransform SkalaIsi = null!;
            public Path Centang = null!;
        }

        // =====================================================================
        // Warna
        // =====================================================================

        /// <summary>Palet splash: seluruh warna diambil dari ResourceDictionary
        /// tema terpilih agar konsisten dengan tampilan aplikasi setelah masuk.</summary>
        internal sealed class WarnaTema
        {
            public Color Primer;
            public Color Sekunder;
            public Brush Permukaan = Brushes.White;
            public Brush PrimerBrush = Brushes.White;
            public Brush OnPrimer = Brushes.White;
            public Brush OnPrimerHalus = Brushes.White;
            public Brush Teks = Brushes.Black;
            public Brush TeksHalus = Brushes.Gray;
        }

        internal static WarnaTema AmbilWarnaTema(string? paksaTema)
        {
            var hasil = new WarnaTema();

            // Fallback terjauh: palet hijau bawaan (warna lama splash).
            Color primer = Color.FromRgb(0x16, 0x65, 0x34);
            Color sekunder = Color.FromRgb(0x14, 0x53, 0x2D);
            Color permukaan = Colors.White;
            Color teks = Color.FromRgb(0x05, 0x2E, 0x16);
            Color teksHalus = Color.FromRgb(0x4D, 0x7C, 0x5F);

            // ResourceDictionary aplikasi (GreenTheme sudah dimuat App.xaml) —
            // dipakai bila pembacaan theme.config gagal.
            ResourceDictionary? sumber = null;
            try
            {
                string tema = ThemeService.ResolveEffective(paksaTema ?? ThemeService.BacaTemaTersimpan());
                sumber = new ResourceDictionary { Source = ThemeService.UriTema(tema) };
            }
            catch
            {
                sumber = null;
            }

            Color AmbilColor(string kunci, Color cadangan)
            {
                if (sumber != null && sumber[kunci] is Color c) return c;
                try
                {
                    if (Application.Current?.TryFindResource(kunci) is Color c2) return c2;
                }
                catch { /* aplikasi uji tanpa resource */ }
                return cadangan;
            }

            Brush AmbilBrush(string kunci, Color cadangan)
            {
                if (sumber != null && sumber[kunci] is Brush b) return b;
                try
                {
                    if (Application.Current?.TryFindResource(kunci) is Brush b2) return b2;
                }
                catch { /* aplikasi uji tanpa resource */ }
                return Warna(cadangan);
            }

            primer = AmbilColor("PrimaryColor", primer);
            sekunder = AmbilColor("SecondaryColor", sekunder);
            permukaan = AmbilColor("SurfaceColor", permukaan);
            teks = AmbilColor("TextColor", teks);
            teksHalus = AmbilColor("TextSecondaryColor", teksHalus);

            hasil.Primer = primer;
            hasil.Sekunder = sekunder;
            hasil.Permukaan = Warna(permukaan);
            hasil.PrimerBrush = Warna(primer);
            hasil.OnPrimer = AmbilBrush("OnPrimaryTextBrush", Colors.White);
            hasil.OnPrimerHalus = AmbilBrush("OnPrimarySubtleBrush", Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
            hasil.Teks = AmbilBrush("TextColor", teks);
            hasil.TeksHalus = AmbilBrush("TextSecondaryColor", teksHalus);
            return hasil;
        }

        internal static SolidColorBrush Warna(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
