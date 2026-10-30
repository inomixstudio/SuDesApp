using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SudesPdf;

public partial class PdfViewer : System.Windows.Controls.UserControl, IAsyncDisposable
{
    private readonly Document _document;
    private readonly string _title;
    private double _scale = 1;
    private bool _disposed;
    private bool _loaded;

    internal PdfViewer(Document document, string title)
    {
        _document = document;
        _title = title;
        InitializeComponent();
        Loaded += UserControl_Loaded;
        Unloaded += async (_, _) => await DisposeAsync();
    }

    public static async Task<PdfViewer> OpenAsync(string path, string title, CancellationToken cancellationToken = default)
    {
        return await Document.OpenViewerAsync(path, title, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<PdfViewer> OpenAsync(byte[] bytes, string title, CancellationToken cancellationToken = default)
    {
        return await Document.OpenViewerAsync(bytes, title, cancellationToken).ConfigureAwait(false);
    }

    public int PageCount => _document.PageCount;

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await RenderPagesAsync();
        FitWidth();
    }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _scale = Math.Min(8, _scale * 1.2);
        await RenderPagesAsync();
    }

    private async void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _scale = Math.Max(0.1, _scale / 1.2);
        await RenderPagesAsync();
    }

    private void FitWidth_Click(object sender, RoutedEventArgs e) => FitWidth();

    private void FitWidth()
    {
        if (_document.PageCount == 0 || PageScroll.ViewportWidth <= 0) return;
        // Lebar halaman 100% (DIP) tidak bergantung pada skala yang sedang aktif.
        double lebarPenuh = _document.PageSizes[0].Width * 96.0 / 72.0;
        _scale = Math.Clamp((PageScroll.ViewportWidth - 64) / lebarPenuh, 0.1, 8);
        _ = RenderPagesAsync();
    }

    /// <summary>Lebar halaman pada skala tampil (DIP) — 100% = ukuran kertas sebenarnya.</summary>
    private double LebarHalamanDip(int halaman) =>
        _document.PageSizes[halaman].Width * _scale * 96.0 / 72.0;

    /// <summary>Rasio piksel layar terhadap DIP (1,0 pada 96 dpi).</summary>
    private double SkalaPerangkat() => Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);

    private async Task RenderPagesAsync()
    {
        if (_disposed) return;
        Pages.Children.Clear();
        ZoomLabel.Text = $"{_scale * 100:0}%";

        double skalaPerangkat = SkalaPerangkat();
        double kerapatan = skalaPerangkat * 96.0;
        for (int page = 0; page < _document.PageCount; page++)
        {
            var size = _document.PageSizes[page];

            // Lebar bitmap dibuat tepat sebanyak piksel layar yang akan dipakai
            // tata letak, jadi WPF tidak menyaring ulang halaman (penyaringan
            // itulah yang membuat huruf tampak kabur).
            double lebarDip = LebarHalamanDip(page);
            int lebarPiksel = Math.Max(1, (int)Math.Round(lebarDip * skalaPerangkat));
            // Epsilon kecil supaya pembulatan ke atas di mesin render tidak
            // menambah satu piksel (yang membuat WPF menyaring ulang).
            double skalaGambar = Math.Min(16.0, lebarPiksel / Math.Max(1.0, size.Width) * (1 - 1e-9));
            var image = await _document.RenderAsync(page, skalaGambar, kerapatan);

            var card = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xCD, 0xD4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7)
            };
            var tampil = new System.Windows.Controls.Image
            {
                Source = image,
                Width = image.PixelWidth * 96.0 / image.DpiX,
                Height = image.PixelHeight * 96.0 / image.DpiY,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(tampil, BitmapScalingMode.HighQuality);
            card.Child = tampil;

            // Bayangan pada elemen kosong di belakang kartu: Effect WPF
            // menggambar seluruh isi elemen ke bitmap antara, jadi memasangnya
            // pada kartu akan mengaburkan gambar halaman.
            var bayangan = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(3),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.3 }
            };
            var wadah = new Grid
            {
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            wadah.Children.Add(bayangan);
            wadah.Children.Add(card);
            Pages.Children.Add(wadah);
        }
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!_disposed) await _document.PrintAsync(_title);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _document.DisposeAsync();
    }
}
