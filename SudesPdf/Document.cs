using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SudesPdf.Core;

namespace SudesPdf;

/// <summary>Ukuran halaman yang tampil (setelah rotasi), dalam satuan titik.</summary>
public sealed record PageSize(double Width, double Height);

/// <summary>
/// Dokumen PDF untuk aplikasi: memuat berkas, menyajikan halaman sebagai bitmap,
/// membaca teks, dan mencetak. Seluruh pekerjaan berat ada di mesin
/// <see cref="SudesPdf.Core"/>; kelas ini hanya menjembatani ke WPF dan menjadi
/// pemilik tunggal state dokumen — pemakaian mesin diserialkan lewat satu kunci
/// supaya cache font/bentuk tidak diakses bersamaan oleh pratinjau dan cetak.
/// </summary>
public sealed class Document : IAsyncDisposable
{
    private readonly PdfDocument _engine;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Pasang kodek JPEG/JPX milik Windows sekali saja. Kop dan SK menyimpan logo
    /// berformat .jpg sebagai gambar DCTDecode; tanpa kodek ini lambangnya hilang
    /// dari pratinjau maupun hasil cetak.
    /// </summary>
    static Document()
    {
        PdfImages.ExternalCodec ??= new WicPdfImageCodec();
    }

    private Document(PdfDocument engine)
    {
        _engine = engine;
        PageCount = engine.PageCount;
        var sizes = new PageSize[PageCount];
        for (int i = 0; i < PageCount; i++)
        {
            var (width, height) = PdfPageGeometry.DisplaySize(engine.Pages[i]);
            sizes[i] = new PageSize(width, height);
        }

        PageSizes = sizes;
    }

    public int PageCount { get; }

    public IReadOnlyList<PageSize> PageSizes { get; }

    public static async Task<PdfViewer> OpenViewerAsync(
        string path, string title, CancellationToken cancellationToken = default) =>
        new(await LoadAsync(path, cancellationToken).ConfigureAwait(false), title);

    public static async Task<PdfViewer> OpenViewerAsync(
        byte[] bytes, string title, CancellationToken cancellationToken = default) =>
        new(await LoadAsync(bytes, cancellationToken).ConfigureAwait(false), title);

    public static async Task<Document> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> LoadAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) throw new InvalidDataException("Berkas PDF kosong.");
        var engine = await Task.Run(() => PdfDocument.Load(bytes), cancellationToken).ConfigureAwait(false);
        return new Document(engine);
    }

    public async Task<BitmapSource> RenderAsync(
        int pageIndex, double scale = 1.0, CancellationToken cancellationToken = default)
    {
        var image = await Task.Run(() => Render(pageIndex, scale), cancellationToken).ConfigureAwait(false);
        return ToBitmap(image);
    }

    /// <summary>
    /// Gambar halaman pada kerapatan piksel perangkat lalu tandai bitmap dengan
    /// DPI layar. Dengan begitu bitmap punya satu piksel untuk setiap piksel
    /// layar dan WPF tidak perlu memperkecil/memperbesar apa pun — teks tidak
    /// lagi melewati penskalaan bilinear yang membuatnya kabur.
    /// </summary>
    /// <param name="dpi">DPI bitmap; isi 96 × skala tampilan Windows.</param>
    public async Task<BitmapSource> RenderAsync(
        int pageIndex, double scale, double dpi, CancellationToken cancellationToken = default)
    {
        var image = await Task.Run(() => Render(pageIndex, scale), cancellationToken).ConfigureAwait(false);
        return ToBitmap(image, dpi);
    }

    public async Task<byte[]> RenderPngAsync(
        int pageIndex, double scale = 1.0, CancellationToken cancellationToken = default)
    {
        var image = await Task.Run(() => Render(pageIndex, scale), cancellationToken).ConfigureAwait(false);
        return image.EncodePng();
    }

    public async Task<string> GetTextAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidatePage(pageIndex);
        var page = _engine.Pages[pageIndex];
        return await Task.Run(
                () =>
                {
                    lock (_gate) return PdfRenderer.ExtractText(page);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tampilkan dialog cetak Windows (printer, rentang halaman, salinan) lalu
    /// kirim dokumen. Harus dipanggil dari thread UI; panggilan dari thread lain
    /// dipindahkan ke dispatcher aplikasi.
    /// </summary>
    public async Task PrintAsync(string title, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(
                    () => PrintOnCurrentThread(title),
                    DispatcherPriority.Normal,
                    cancellationToken)
                .Task.ConfigureAwait(false);
            return;
        }

        PrintOnCurrentThread(title);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Gambar satu halaman pada skala tertentu (satuan titik → piksel).</summary>
    internal RasterImage Render(int pageIndex, double scale)
    {
        ThrowIfDisposed();
        ValidatePage(pageIndex);
        if (!double.IsFinite(scale) || scale <= 0 || scale > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        lock (_gate)
        {
            return PdfRenderer.RenderPage(_engine, pageIndex, scale);
        }
    }

    internal static BitmapSource ToBitmap(RasterImage image, double dpi = 96)
    {
        double kerapatan = double.IsFinite(dpi) && dpi > 0 ? dpi : 96;
        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            kerapatan,
            kerapatan,
            PixelFormats.Bgra32,
            null,
            image.Pixels,
            image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void PrintOnCurrentThread(string title) => PdfPrinter.ShowDialogAndPrint(this, title);

    private void ValidatePage(int index)
    {
        if ((uint)index >= (uint)PageCount) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
