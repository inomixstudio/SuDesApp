using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Teks PDF: penguraian string dokumen dan penyusunan urutan baca.</summary>
public static class PdfText
{
    /// <summary>
    /// String PDF: UTF-16BE bila diawali BOM FE FF, UTF-8 BOM, atau
    /// PDFDocEncoding (ISO 32000-1 §7.9.2.2).
    /// </summary>
    public static string DecodePdfString(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        var builder = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            char? character = PdfEncodingTables.Lookup("PDFDocEncoding", b);
            builder.Append(character ?? (char)b);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Ekstraksi teks halaman: mengumpulkan kemunculan teks lalu menyusunnya
/// menjadi baris dengan urutan baca dari atas ke bawah, kiri ke kanan. Jarak
/// antar-kemunculan yang lebih lebar dari seperempat ukuran huruf dianggap spasi,
/// sebagaimana penampil PDF menampilkan teks yang dipisah operator Td/TJ.
/// </summary>
public sealed class PdfTextExtractor : IPdfSink
{
    private readonly List<Run> _runs = new();
    private readonly PdfMatrix _toPage;

    public PdfTextExtractor(in PdfMatrix toPage) => _toPage = toPage;

    private sealed class Run
    {
        public required string Text { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double EndX { get; init; }
        public double EndY { get; init; }
        public double Size { get; init; }
        public int Order { get; init; }
    }

    public void Save()
    {
    }

    public void Restore()
    {
    }

    public void Clip(PdfPath path, PdfFillRule rule)
    {
        _ = path;
        _ = rule;
    }

    public void Fill(PdfPath path, PdfFillRule rule, in PdfPaint paint)
    {
        _ = path;
        _ = rule;
        _ = paint;
    }

    public void Stroke(PdfPath path, in PdfStrokeSpec spec, in PdfPaint paint)
    {
        _ = path;
        _ = spec;
        _ = paint;
    }

    public void DrawImage(PdfImage image, in PdfMatrix matrix, double alpha, bool interpolate)
    {
        _ = image;
        _ = matrix;
        _ = alpha;
        _ = interpolate;
    }

    public void DrawShading(PdfShading shading, in PdfMatrix matrix, double alpha)
    {
        _ = shading;
        _ = matrix;
        _ = alpha;
    }

    public void ShowText(PdfTextRun run)
    {
        if (string.IsNullOrEmpty(run.Text)) return;

        var start = _toPage.Transform(run.StartX, run.StartY);
        var end = _toPage.Transform(run.EndX, run.EndY);
        double size = Math.Max(0.5, run.FontSize * _toPage.ScaleY);
        _runs.Add(new Run
        {
            Text = run.Text,
            X = start.X,
            Y = start.Y,
            EndX = end.X,
            EndY = end.Y,
            Size = size,
            Order = _runs.Count
        });
    }

    /// <summary>Susun teks akhir halaman (baris dipisah '\n').</summary>
    public string Build()
    {
        if (_runs.Count == 0) return string.Empty;

        // Kelompokkan per baris berdasarkan garis dasar; toleransi setengah
        // tinggi huruf supaya superskrip/NIP ikut dalam barisnya.
        var lines = new List<List<Run>>();
        var lineBaselines = new List<double>();
        var lineSizes = new List<double>();

        foreach (var run in _runs.OrderByDescending(r => r.Y).ThenBy(r => r.X))
        {
            int target = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                double tolerance = 0.4 * Math.Max(lineSizes[i], run.Size);
                if (Math.Abs(lineBaselines[i] - run.Y) <= tolerance)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                lines.Add(new List<Run> { run });
                lineBaselines.Add(run.Y);
                lineSizes.Add(run.Size);
                continue;
            }

            lines[target].Add(run);
            lineSizes[target] = Math.Max(lineSizes[target], run.Size);
        }

        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.Sort((a, b) => a.X.CompareTo(b.X));
            double cursor = double.NaN;
            double lastSize = 1;
            foreach (var run in line)
            {
                if (!double.IsNaN(cursor))
                {
                    double gap = run.X - cursor;
                    double threshold = 0.2 * Math.Max(lastSize, run.Size);
                    bool needsSpace = gap > threshold
                                      && !builder.ToString().EndsWith(' ')
                                      && !run.Text.StartsWith(' ');
                    if (needsSpace) builder.Append(' ');
                }

                builder.Append(run.Text);
                cursor = Math.Max(run.X + Math.Max(0, run.EndX - run.X), run.EndX);
                lastSize = run.Size;
            }

            if (i < lines.Count - 1) builder.Append('\n');
        }

        return builder.ToString();
    }
}
