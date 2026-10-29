using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>Titik pada ruang perangkat (piksel) atau ruang pengguna PDF.</summary>
public readonly struct PdfPoint
{
    public PdfPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>Persegi sejajar sumbu, dinyatakan dengan sudut kiri-bawah + ukuran.</summary>
public readonly struct PdfRect
{
    public PdfRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public double Left => X;
    public double Bottom => Y;
    public double Right => X + Width;
    public double Top => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PdfRect FromEdges(double x0, double y0, double x1, double y1) =>
        new(x0, y0, x1 - x0, y1 - y0);

    public PdfRect Intersect(PdfRect other)
    {
        double x0 = Math.Max(Left, other.Left);
        double y0 = Math.Max(Bottom, other.Bottom);
        double x1 = Math.Min(Right, other.Right);
        double y1 = Math.Min(Top, other.Top);
        if (x1 <= x0 || y1 <= y0) return new PdfRect(x0, y0, 0, 0);
        return FromEdges(x0, y0, x1, y1);
    }

    public PdfRect Union(PdfRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(
            Math.Min(Left, other.Left),
            Math.Min(Bottom, other.Bottom),
            Math.Max(Right, other.Right),
            Math.Max(Top, other.Top));
    }

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Bottom && y <= Top;

    public override string ToString() => $"[{Left:0.###} {Bottom:0.###} {Right:0.###} {Top:0.###}]";
}

/// <summary>
/// Matriks PDF 3x2: [a b c d e f]. Pemetaan titik:
/// x' = a·x + c·y + e, y' = b·x + d·y + f.
/// </summary>
public readonly struct PdfMatrix
{
    public PdfMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }
    public double E { get; }
    public double F { get; }

    public static PdfMatrix Identity => new(1, 0, 0, 1, 0, 0);

    public static PdfMatrix Translation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    public static PdfMatrix Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static PdfMatrix Rotation(double radians)
    {
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new PdfMatrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Matriks ini dikalikan <paramref name="m"/> (yaitu "terapkan ini, lalu m").</summary>
    public PdfMatrix Multiply(in PdfMatrix m) => new(
        A * m.A + B * m.C,
        A * m.B + B * m.D,
        C * m.A + D * m.C,
        C * m.B + D * m.D,
        E * m.A + F * m.C + m.E,
        E * m.B + F * m.D + m.F);

    public PdfPoint Transform(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);

    /// <summary>Transformasi arah (tanpa translasi).</summary>
    public PdfPoint TransformVector(double x, double y) => new(A * x + C * y, B * x + D * y);

    public double Determinant => A * D - B * C;

    /// <summary>Panjang vektor basis X — skala horizontal yang berlaku pada garis.</summary>
    public double ScaleX => Math.Sqrt(A * A + B * B);

    public double ScaleY => Math.Sqrt(C * C + D * D);

    public bool IsInvertible => Math.Abs(Determinant) > 1e-12;

    public bool TryInvert(out PdfMatrix inverse)
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12)
        {
            inverse = Identity;
            return false;
        }

        double ia = D / det;
        double ib = -B / det;
        double ic = -C / det;
        double id = A / det;
        inverse = new PdfMatrix(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
        return true;
    }

    public override string ToString() => $"[{A:0.####} {B:0.####} {C:0.####} {D:0.####} {E:0.####} {F:0.####}]";
}

public enum PdfFillRule
{
    NonZero,
    EvenOdd
}

public enum PdfLineCap
{
    Butt = 0,
    Round = 1,
    Square = 2
}

public enum PdfLineJoin
{
    Miter = 0,
    Round = 1,
    Bevel = 2
}

/// <summary>Parameter garis yang sudah diterjemahkan ke ruang perangkat.</summary>
public sealed class PdfStrokeSpec
{
    public double Width { get; init; } = 1.0;
    public PdfLineCap Cap { get; init; } = PdfLineCap.Butt;
    public PdfLineJoin Join { get; init; } = PdfLineJoin.Miter;
    public double MiterLimit { get; init; } = 10.0;
    public double[]? DashArray { get; init; }
    public double DashPhase { get; init; }
}

/// <summary>Satu kontur poligonal (kurva sudah diratakan) pada ruang perangkat.</summary>
public sealed class PdfContour
{
    public PdfContour(double[] points, bool closed)
    {
        Points = points;
        Closed = closed;
    }

    /// <summary>Pasangan x,y berurutan.</summary>
    public double[] Points { get; }

    public bool Closed { get; }

    public int Count => Points.Length / 2;

    public double X(int index) => Points[index * 2];

    public double Y(int index) => Points[index * 2 + 1];
}

/// <summary>Jalur siap gambar: kumpulan kontur + batasnya.</summary>
public sealed class PdfPath
{
    public static readonly PdfPath Empty = new(Array.Empty<PdfContour>(), default);

    public PdfPath(IReadOnlyList<PdfContour> contours, PdfRect bounds)
    {
        Contours = contours;
        Bounds = bounds;
    }

    public IReadOnlyList<PdfContour> Contours { get; }

    public PdfRect Bounds { get; }

    public bool IsEmpty => Contours.Count == 0;
}

/// <summary>
/// Penyusun jalur: menerima operator jalur PDF (sudah dipetakan ke ruang
/// perangkat) dan meratakan kurva menjadi poligon supaya rasterizer hanya
/// berurusan dengan garis lurus.
/// </summary>
public sealed class PdfPathBuilder
{
    private readonly List<PdfContour> _contours = new();
    private List<double> _current = new();
    private double _startX;
    private double _startY;
    private double _x;
    private double _y;
    private bool _open;
    private bool _closedAtStart;
    private double _minX = double.MaxValue;
    private double _minY = double.MaxValue;
    private double _maxX = double.MinValue;
    private double _maxY = double.MinValue;

    /// <summary>Toleransi kerataan kurva (satuan perangkat).</summary>
    public double Flatness { get; set; } = 0.25;

    public bool HasSegments => _contours.Count > 0 || _current.Count >= 4;

    public void Reset()
    {
        _contours.Clear();
        _current = new List<double>();
        _open = false;
        _closedAtStart = false;
        _minX = double.MaxValue;
        _minY = double.MaxValue;
        _maxX = double.MinValue;
        _maxY = double.MinValue;
    }

    public void MoveTo(double x, double y)
    {
        FinishCurrent(false);
        StartSubpath(x, y);
    }

    public void LineTo(double x, double y)
    {
        if (!_open)
        {
            // Setelah 'h' titik aktif kembali ke awal subjalur tertutup.
            StartSubpath(_closedAtStart ? _startX : _x, _closedAtStart ? _startY : _y);
        }

        AddPoint(x, y);
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!_open) StartSubpath(_x, _y);
        FlattenCubic(_x, _y, x1, y1, x2, y2, x3, y3, 0);
        _x = x3;
        _y = y3;
        AddPoint(x3, y3);
    }

    /// <summary>Kurva kuadratik (dipakai glif TrueType) diubah menjadi bentuk kubik.</summary>
    public void QuadTo(double cx, double cy, double x, double y)
    {
        if (!_open) StartSubpath(_x, _y);
        double c1x = _x + 2.0 / 3.0 * (cx - _x);
        double c1y = _y + 2.0 / 3.0 * (cy - _y);
        double c2x = x + 2.0 / 3.0 * (cx - x);
        double c2y = y + 2.0 / 3.0 * (cy - y);
        CurveTo(c1x, c1y, c2x, c2y, x, y);
    }

    public void Close()
    {
        if (!_open) return;
        FinishCurrent(true);
        _closedAtStart = true;
    }

    public void AddRectangle(double x, double y, double width, double height)
    {
        FinishCurrent(false);
        StartSubpath(x, y);
        AddPoint(x + width, y);
        AddPoint(x + width, y + height);
        AddPoint(x, y + height);
        FinishCurrent(true);
        _closedAtStart = true;
    }

    /// <summary>Tambahkan jalur lain apa adanya (mis. glif pada matriks lain).</summary>
    public void AddPath(PdfPath path, in PdfMatrix matrix)
    {
        foreach (var contour in path.Contours)
        {
            FinishCurrent(false);
            if (contour.Count == 0) continue;
            StartSubpath(
                matrix.Transform(contour.X(0), contour.Y(0)).X,
                matrix.Transform(contour.X(0), contour.Y(0)).Y);
            for (int i = 1; i < contour.Count; i++)
            {
                var p = matrix.Transform(contour.X(i), contour.Y(i));
                AddPoint(p.X, p.Y);
            }

            FinishCurrent(contour.Closed);
            _closedAtStart = contour.Closed;
        }
    }

    public PdfPath Build()
    {
        FinishCurrent(false);
        if (_contours.Count == 0) return PdfPath.Empty;
        var bounds = _minX > _maxX
            ? default
            : PdfRect.FromEdges(_minX, _minY, _maxX, _maxY);
        return new PdfPath(_contours.ToArray(), bounds);
    }

    private void StartSubpath(double x, double y)
    {
        _current = new List<double>(16) { x, y };
        _startX = x;
        _startY = y;
        _x = x;
        _y = y;
        _open = true;
        _closedAtStart = false;
        Extend(x, y);
    }

    private void AddPoint(double x, double y)
    {
        _current.Add(x);
        _current.Add(y);
        Extend(x, y);
    }

    private void FinishCurrent(bool closed)
    {
        if (!_open) return;
        if (_current.Count >= 4)
        {
            _contours.Add(new PdfContour(_current.ToArray(), closed));
        }

        _current = new List<double>(16);
        _open = false;
        _closedAtStart = false;
    }

    private void Extend(double x, double y)
    {
        if (x < _minX) _minX = x;
        if (y < _minY) _minY = y;
        if (x > _maxX) _maxX = x;
        if (y > _maxY) _maxY = y;
    }

    /// <summary>Subdivisi rekursif De Casteljau sampai kurva cukup rata.</summary>
    private void FlattenCubic(
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, int depth)
    {
        if (depth > 12 || IsFlat(x0, y0, x1, y1, x2, y2, x3, y3))
        {
            AddPoint(x3, y3);
            return;
        }

        double x01 = (x0 + x1) / 2, y01 = (y0 + y1) / 2;
        double x12 = (x1 + x2) / 2, y12 = (y1 + y2) / 2;
        double x23 = (x2 + x3) / 2, y23 = (y2 + y3) / 2;
        double x012 = (x01 + x12) / 2, y012 = (y01 + y12) / 2;
        double x123 = (x12 + x23) / 2, y123 = (y12 + y23) / 2;
        double xm = (x012 + x123) / 2, ym = (y012 + y123) / 2;

        FlattenCubic(x0, y0, x01, y01, x012, y012, xm, ym, depth + 1);
        FlattenCubic(xm, ym, x123, y123, x23, y23, x3, y3, depth + 1);
    }

    private bool IsFlat(
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        double tolerance = Math.Max(Flatness, 0.01);
        // Jarak kedua titik kendali dari garis dasar lurus.
        double d1 = DistanceToLine(x1, y1, x0, y0, x3, y3);
        double d2 = DistanceToLine(x2, y2, x0, y0, x3, y3);
        return d1 <= tolerance && d2 <= tolerance;
    }

    private static double DistanceToLine(double px, double py, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return Math.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));
        return Math.Abs(dy * px - dx * py + x1 * y0 - y1 * x0) / length;
    }
}
