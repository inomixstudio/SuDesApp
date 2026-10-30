using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>Pemetaan kotak halaman ke ruang tampilan dan raster.</summary>
public static class PdfPageGeometry
{
    /// <summary>
    /// Ruang tampilan: y ke atas, titik (0,0) di kiri-bawah halaman setelah
    /// dipotong CropBox dan diputar sesuai /Rotate.
    /// </summary>
    public static PdfMatrix ToDisplay(PdfPage page)
    {
        var box = page.CropBox;
        double width = box.Width;
        double height = box.Height;
        if (width <= 0 || height <= 0)
        {
            box = page.MediaBox;
            width = box.Width;
            height = box.Height;
        }

        PdfMatrix rotation = page.Rotation switch
        {
            90 => new PdfMatrix(0, -1, 1, 0, 0, width),
            180 => new PdfMatrix(-1, 0, 0, -1, width, height),
            270 => new PdfMatrix(0, 1, -1, 0, height, 0),
            _ => PdfMatrix.Identity
        };

        return PdfMatrix.Translation(-box.X, -box.Y).Multiply(rotation);
    }

    /// <summary>Ruang piksel: y ke bawah, 0,0 di kiri-atas.</summary>
    public static PdfMatrix ToRaster(PdfPage page, double scale)
    {
        var display = ToDisplay(page);
        var (width, height) = DisplaySize(page);
        return display.Multiply(new PdfMatrix(scale, 0, 0, -scale, 0, height * scale));
    }

    /// <summary>Ukuran halaman yang tampil (setelah rotasi), dalam poin.</summary>
    public static (double Width, double Height) DisplaySize(PdfPage page)
    {
        var box = page.CropBox;
        if (box.Width <= 0 || box.Height <= 0) box = page.MediaBox;
        return page.Rotation is 90 or 270
            ? (box.Height, box.Width)
            : (box.Width, box.Height);
    }
}

/// <summary>Penggambar halaman PDF menjadi raster dan pembaca teksnya.</summary>
public static class PdfRenderer
{
    public static RasterImage RenderPage(PdfDocument document, int pageIndex, double scale, bool whiteBackground = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return RenderPage(document.Pages[pageIndex], scale, whiteBackground);
    }

    public static RasterImage RenderPage(PdfPage page, double scale, bool whiteBackground = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        scale = Math.Clamp(scale, 0.01, 32);

        var (displayWidth, displayHeight) = PdfPageGeometry.DisplaySize(page);
        int width = Math.Max(1, (int)Math.Ceiling(displayWidth * scale));
        int height = Math.Max(1, (int)Math.Ceiling(displayHeight * scale));

        var image = new RasterImage(width, height, transparent: !whiteBackground);
        var matrix = PdfPageGeometry.ToRaster(page, scale);
        var sink = new RasterSink(page.Document, image, scale);
        var interpreter = new PdfContentInterpreter(page.Document);
        var contents = PdfContentInterpreter.ReadContents(page.Document, page.ContentsRaw);

        try
        {
            interpreter.Run(page.Resources, contents, matrix, sink, scale);
        }
        catch (PdfException)
        {
            // Halaman rusak tetap menghasilkan gambar sebagian.
        }

        return image;
    }

    public static string ExtractText(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var extractor = new PdfTextExtractor(PdfPageGeometry.ToDisplay(page));
        var interpreter = new PdfContentInterpreter(page.Document);
        var contents = PdfContentInterpreter.ReadContents(page.Document, page.ContentsRaw);
        try
        {
            interpreter.Run(page.Resources, contents, PdfPageGeometry.ToDisplay(page), extractor);
        }
        catch (PdfException)
        {
            // Teks yang sempat diekstrak tetap dipakai meski halaman rusak.
        }

        return extractor.Build();
    }

    public static string ExtractText(PdfDocument document)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < document.PageCount; i++)
        {
            builder.Append(ExtractText(document.Pages[i]));
            builder.Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// Tujuan gambar raster: menerjemahkan panggilan penafsir menjadi piksel
/// (jalur, garis, gambar, gradien, pola ubin, dan glif teks).
/// </summary>
internal sealed class RasterSink : IPdfSink
{
    private readonly PdfDocument _document;
    private readonly RasterImage _image;
    private readonly Rasterizer _rasterizer;
    private readonly double _scale;
    private readonly Stack<ClipState> _clips = new();
    private PdfContentInterpreter? _patternInterpreter;
    private ClipState _clip = new();

    public RasterSink(PdfDocument document, RasterImage image, double scale)
    {
        _document = document;
        _image = image;
        _scale = scale;
        _rasterizer = new Rasterizer(image);
        _clip = new ClipState { Bounds = new PdfRect(0, 0, image.Width, image.Height) };
    }

    private sealed class ClipState
    {
        public PdfRect Bounds;
        public byte[]? Mask;
    }

    public void Save() => _clips.Push(new ClipState { Bounds = _clip.Bounds, Mask = _clip.Mask });

    public void Restore()
    {
        if (_clips.Count > 0) _clip = _clips.Pop();
    }

    public void Clip(PdfPath path, PdfFillRule rule)
    {
        if (path.IsEmpty) return;
        var bounds = path.Bounds.Intersect(_clip.Bounds);
        if (bounds.IsEmpty)
        {
            _clip = new ClipState { Bounds = new PdfRect(0, 0, 0, 0) };
            return;
        }

        if (_clip.Mask is null && IsAxisAlignedRectangle(path, bounds))
        {
            _clip = new ClipState { Bounds = bounds };
            return;
        }

        var mask = new byte[_image.Width * _image.Height];
        _rasterizer.RenderCoverage(path, rule, mask);
        if (_clip.Mask is not null)
        {
            for (int i = 0; i < mask.Length; i++) mask[i] = (byte)Math.Min(mask[i], _clip.Mask[i]);
        }

        _clip = new ClipState { Bounds = bounds, Mask = mask };
    }

    private static bool IsAxisAlignedRectangle(PdfPath path, PdfRect bounds)
    {
        if (path.Contours.Count != 1) return false;
        var contour = path.Contours[0];
        if (contour.Count < 4) return false;
        foreach (var pointIndex in new[] { 0, 1, 2, 3 })
        {
            int i = pointIndex % contour.Count;
            bool onVerticalEdge = Math.Abs(contour.X(i) - bounds.Left) < 0.05 || Math.Abs(contour.X(i) - bounds.Right) < 0.05;
            bool onHorizontalEdge = Math.Abs(contour.Y(i) - bounds.Bottom) < 0.05 || Math.Abs(contour.Y(i) - bounds.Top) < 0.05;
            if (!onVerticalEdge && !onHorizontalEdge) return false;
        }

        return true;
    }

    public void Fill(PdfPath path, PdfFillRule rule, in PdfPaint paint)
    {
        if (path.IsEmpty) return;
        if (paint.Pattern is not null)
        {
            FillWithPattern(path, rule, paint.Pattern, in paint);
            return;
        }

        _rasterizer.Fill(path, rule, paint.R, paint.G, paint.B, paint.Alpha, PdfBlendMode.Normal, _clip.Mask);
    }

    public void Stroke(PdfPath path, in PdfStrokeSpec spec, in PdfPaint paint)
    {
        if (path.IsEmpty) return;
        if (paint.Pattern is not null)
        {
            StrokeWithPattern(path, spec, in paint);
            return;
        }

        _rasterizer.Stroke(
            path, in spec, paint.R, paint.G, paint.B, paint.Alpha, PdfBlendMode.Normal, _clip.Mask);
    }

    public void DrawImage(PdfImage image, in PdfMatrix matrix, double alpha, bool interpolate)
    {
        _ = interpolate;
        _rasterizer.DrawImage(image, in matrix, alpha, _clip.Mask, PdfBlendMode.Normal);
    }

    public void DrawShading(PdfShading shading, in PdfMatrix matrix, double alpha)
    {
        _rasterizer.DrawShading(shading, in matrix, alpha, _clip.Mask, _clip.Bounds, PdfBlendMode.Normal);
    }

    // Glif TIDAK diraster dengan pengambilan sampel. Cakupannya dihitung
    // sebagai luas eksak per piksel (Rasterizer.FillTepat), cara yang sama
    // dengan rasterizer FreeType di pdfium: tiap tepi lurus menyumbangkan luas
    // trapesiumnya, lalu dijumlahkan sebagai cakupan.
    //
    // Bentuk glif dipakai APA ADANYA, persis perilaku pdfium: mereka tidak
    // membulatkan lebar batang huruf ke piksel, karena pembulatan itu membuat
    // batang tebal melebar dan jarak antara dua batang (lawang) menutup —
    // huruf tebal justru terlihat tersumbat dan kacau. Yang membuat teks tetap
    // pekat adalah langkah berikutnya: cakupan parsial dilewatkan tabel gamma
    // pdfium (PdfTeksGamma) sebelum di-blend, sehingga tepi huruf lebih gelap
    // tanpa memperlebar bagian yang sudah penuh.

    public void ShowText(PdfTextRun run)
    {
        if (run.GlyphIds.Length == 0) return;
        if (run.RenderMode == 3) return;

        var builder = new PdfPathBuilder { Flatness = 0.25 };

        // Penyelarasan garis dasar ke piksel utuh. Huruf yang jatuh di antara
        // baris piksel kehilangan ketajamannya — batang huruf tebal berubah jadi
        // dua kolom abu-abu dan lawangnya terlihat tersumbat. Perpindahannya
        // kurang dari setengah piksel, jadi tata letak tidak berubah; seluruh
        // satu baris memakai garis dasar yang sama sehingga teks campur
        // (reguler + tebal dalam satu kalimat) tetap lurus.
        double snapY = Math.Round(run.StartY) - run.StartY;

        for (int i = 0; i < run.GlyphIds.Length; i++)
        {
            int glyphId = run.GlyphIds[i];
            if (glyphId < 0) continue;
            if (!run.Font.TryGetOutline(glyphId, out var outline) || outline.IsEmpty) continue;

            double dx = run.GlyphX[i] - run.StartX;
            double dy = run.GlyphY[i] - run.StartY + snapY;
            // GlyphX/GlyphY sudah dalam satuan perangkat, jadi perpindahannya
            // diterapkan SESUDAH matriks glif — bukan sebelumnya.
            var matrix = run.GlyphMatrix.Multiply(PdfMatrix.Translation(dx, dy));
            outline.AppendTo(builder, matrix);
        }

        var path = builder.Build();
        if (path.IsEmpty) return;

        switch (run.RenderMode)
        {
            case 1:
                _rasterizer.Stroke(
                    path, new PdfStrokeSpec { Width = Math.Max(run.StrokeWidth, 0.08) },
                    run.Stroke.R, run.Stroke.G, run.Stroke.B, run.Stroke.Alpha, PdfBlendMode.Normal, _clip.Mask);
                break;
            case 2:
                _rasterizer.FillTepat(path, PdfFillRule.NonZero, run.Fill.R, run.Fill.G, run.Fill.B, run.Fill.Alpha, PdfBlendMode.Normal, _clip.Mask, coverageTable: PdfTeksGamma.Tabel);
                _rasterizer.Stroke(
                    path, new PdfStrokeSpec { Width = Math.Max(run.StrokeWidth, 0.08) },
                    run.Stroke.R, run.Stroke.G, run.Stroke.B, run.Stroke.Alpha, PdfBlendMode.Normal, _clip.Mask);
                break;
            case 4:
            case 5:
            case 6:
            case 7:
                Clip(path, PdfFillRule.NonZero);
                break;
            default:
                _rasterizer.FillTepat(path, PdfFillRule.NonZero, run.Fill.R, run.Fill.G, run.Fill.B, run.Fill.Alpha, PdfBlendMode.Normal, _clip.Mask, coverageTable: PdfTeksGamma.Tabel);
                break;
        }
    }

    private void StrokeWithPattern(PdfPath path, in PdfStrokeSpec spec, in PdfPaint paint)
    {
        // Garis berpola jarang dipakai dokumen kantor; digambar dengan warna dasar
        // pola agar garis tetap terlihat dan lebar garis tetap terjaga.
        _rasterizer.Stroke(
            path, in spec, paint.R, paint.G, paint.B, paint.Alpha, PdfBlendMode.Normal, _clip.Mask);
    }

    private void FillWithPattern(PdfPath path, PdfFillRule rule, PdfTilingPattern pattern, in PdfPaint paint)
    {
        if (pattern.BBox.Width <= 0 || pattern.BBox.Height <= 0) return;

        var patternToDevice = paint.PatternMatrix;
        if (!patternToDevice.TryInvert(out var deviceToPattern)) return;

        var cell = RenderPatternCell(pattern, paint);

        // Cakupan jalur pada kotak batasnya.
        var bounds = path.Bounds.Intersect(_clip.Bounds).Intersect(new PdfRect(0, 0, _image.Width, _image.Height));
        if (bounds.IsEmpty) return;
        int x0 = Math.Max(0, (int)Math.Floor(bounds.Left));
        int y0 = Math.Max(0, (int)Math.Floor(bounds.Bottom));
        int x1 = Math.Min(_image.Width - 1, (int)Math.Ceiling(bounds.Right));
        int y1 = Math.Min(_image.Height - 1, (int)Math.Ceiling(bounds.Top));
        if (x0 > x1 || y0 > y1) return;

        int maskWidth = x1 - x0 + 1;
        int maskHeight = y1 - y0 + 1;
        var mask = new byte[maskWidth * maskHeight];
        var shifted = new PdfPathBuilder();
        foreach (var contour in path.Contours)
        {
            if (contour.Count == 0) continue;
            shifted.MoveTo(contour.X(0) - x0, contour.Y(0) - y0);
            for (int i = 1; i < contour.Count; i++) shifted.LineTo(contour.X(i) - x0, contour.Y(i) - y0);
            if (contour.Closed) shifted.Close();
        }

        var localPath = shifted.Build();
        if (localPath.IsEmpty) return;
        var localRaster = new Rasterizer(new RasterImage(maskWidth, maskHeight, transparent: true));
        localRaster.RenderCoverage(localPath, rule, mask);

        double stepX = Math.Abs(pattern.XStep);
        double stepY = Math.Abs(pattern.YStep);
        if (stepX < 1e-6 || stepY < 1e-6) return;

        byte[] cellPixels = cell.Image.Pixels;
        double cellWidth = cell.Width;
        double cellHeight = cell.Height;
        double scaleX = cellWidth / pattern.BBox.Width;
        double scaleY = cellHeight / pattern.BBox.Height;

        for (int y = 0; y < maskHeight; y++)
        {
            for (int x = 0; x < maskWidth; x++)
            {
                int coverage = mask[y * maskWidth + x];
                if (coverage == 0) continue;

                int deviceX = x0 + x;
                int deviceY = y0 + y;
                int clipIndex = deviceY * _image.Width + deviceX;
                double clip = _clip.Mask is null ? 1 : _clip.Mask[clipIndex] / 255.0;
                if (clip <= 0) continue;

                var point = deviceToPattern.Transform(deviceX + 0.5, deviceY + 0.5);
                double localX = Modulo(point.X - pattern.BBox.Left, stepX);
                double localY = Modulo(point.Y - pattern.BBox.Bottom, stepY);
                if (localX > pattern.BBox.Width || localY > pattern.BBox.Height) continue;

                int cellX = Math.Clamp((int)(localX * scaleX), 0, cell.Image.Width - 1);
                int cellY = Math.Clamp((int)((pattern.BBox.Height - localY) * scaleY), 0, cell.Image.Height - 1);
                int cellIndex = (cellY * cell.Image.Width + cellX) * 4;
                double alpha = paint.Alpha * (coverage / 255.0) * clip * (cellPixels[cellIndex + 3] / 255.0);
                if (alpha <= 0.002) continue;

                CompositePixel(
                    clipIndex,
                    cellPixels[cellIndex + 2] / 255.0,
                    cellPixels[cellIndex + 1] / 255.0,
                    cellPixels[cellIndex] / 255.0,
                    alpha);
            }
        }
    }

    private static double Modulo(double value, double step)
    {
        double result = value % step;
        return result < 0 ? result + step : result;
    }

    private void CompositePixel(int index, double red, double green, double blue, double alpha)
    {
        int offset = index * 4;
        byte[] pixels = _image.Pixels;
        double destinationAlpha = pixels[offset + 3] / 255.0;
        double outputAlpha = alpha + destinationAlpha * (1 - alpha);
        if (outputAlpha <= 0) return;
        double destinationRed = pixels[offset + 2] / 255.0;
        double destinationGreen = pixels[offset + 1] / 255.0;
        double destinationBlue = pixels[offset] / 255.0;
        pixels[offset] = ToByte((blue * alpha + destinationBlue * destinationAlpha * (1 - alpha)) / outputAlpha);
        pixels[offset + 1] = ToByte((green * alpha + destinationGreen * destinationAlpha * (1 - alpha)) / outputAlpha);
        pixels[offset + 2] = ToByte((red * alpha + destinationRed * destinationAlpha * (1 - alpha)) / outputAlpha);
        pixels[offset + 3] = ToByte(outputAlpha);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private (RasterImage Image, double Width, double Height) RenderPatternCell(PdfTilingPattern pattern, in PdfPaint paint)
    {
        var box = pattern.BBox;
        // Ukuran sel mengikuti skala perangkat agar tidak pecah.
        double sx = Math.Abs(pattern.Matrix.A) + Math.Abs(pattern.Matrix.C);
        double sy = Math.Abs(pattern.Matrix.B) + Math.Abs(pattern.Matrix.D);
        sx = Math.Max(sx, 0.01);
        sy = Math.Max(sy, 0.01);
        int width = Math.Clamp((int)Math.Ceiling(box.Width * sx * _scale), 1, 2048);
        int height = Math.Clamp((int)Math.Ceiling(box.Height * sy * _scale), 1, 2048);

        double scaleX = width / box.Width;
        double scaleY = height / box.Height;
        var cellMatrix = new PdfMatrix(scaleX, 0, 0, -scaleY, -box.Left * scaleX, box.Top * scaleY);

        var cellImage = new RasterImage(width, height, transparent: true);
        var cellSink = new RasterSink(_document, cellImage, _scale);
        var interpreter = _patternInterpreter ??= new PdfContentInterpreter(_document);
        var initialFill = pattern.PaintType == 2 ? paint : (PdfPaint?)null;

        try
        {
            interpreter.Run(
                pattern.Resources,
                pattern.Contents,
                cellMatrix,
                cellSink,
                _scale,
                initialFill);
        }
        catch (PdfException)
        {
            // Pola rusak tidak boleh menggagalkan halaman; sel kosong tetap dipakai.
        }

        return (cellImage, width, height);
    }
}
