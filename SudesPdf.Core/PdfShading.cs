using System;

namespace SudesPdf.Core;

/// <summary>
/// Gradien PDF (ISO 32000-1 §8.7.4). Jenis 2 (aksial) dan 3 (radial) digambar;
/// jenis mesh (4–7) dilewati karena jarang dipakai dokumen kantor.
/// </summary>
public sealed class PdfShading
{
    private PdfShading(int type, PdfColorSpace colorSpace, double[] coords, PdfFunction? function, bool[] extend)
    {
        Type = type;
        ColorSpace = colorSpace;
        Coords = coords;
        Function = function;
        Extend = extend;
    }

    public int Type { get; }

    public PdfColorSpace ColorSpace { get; }

    public double[] Coords { get; }

    public PdfFunction? Function { get; }

    public bool[] Extend { get; }

    public static PdfShading? Create(PdfObject? value, PdfDocument document)
    {
        var stream = document.ResolveStream(value);
        if (stream is null) return null;
        var dict = stream.Dictionary;
        int type = (int)(dict.GetNumber("ShadingType") ?? 0);
        if (type is not (2 or 3)) return null;

        var colors = PdfColorSpace.Resolve(dict.Raw("ColorSpace"), document) ?? PdfColorSpace.DeviceRgb;
        var coordinates = ReadNumbers(document, dict.Raw("Coords"));
        int required = type == 2 ? 4 : 6;
        if (coordinates.Length < required) return null;

        var extendArray = document.ResolveArray(dict.Raw("Extend"));
        bool[] extend =
        {
            (extendArray?.At(0) as PdfBoolean)?.Value ?? false,
            (extendArray?.At(1) as PdfBoolean)?.Value ?? false
        };

        var function = PdfFunction.Create(dict.Raw("Function"), document);
        return new PdfShading(type, colors, coordinates, function, extend);
    }

    private static double[] ReadNumbers(PdfDocument document, PdfObject? value)
    {
        var array = document.ResolveArray(value);
        if (array is null) return Array.Empty<double>();
        var numbers = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            numbers[i] = document.ResolveNumber(array.At(i))?.Value ?? 0;
        }

        return numbers;
    }

    /// <summary>Hitung parameter gradien pada titik ruang gradien; false bila di luar.</summary>
    public bool TryGetParameter(double x, double y, out double parameter)
    {
        parameter = 0;
        switch (Type)
        {
            case 2:
            {
                double x0 = Coords[0], y0 = Coords[1];
                double x1 = Coords[2], y1 = Coords[3];
                double dx = x1 - x0;
                double dy = y1 - y0;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared < 1e-12) return false;
                double t = ((x - x0) * dx + (y - y0) * dy) / lengthSquared;
                if (t < 0)
                {
                    if (!Extend[0]) return false;
                    t = 0;
                }
                else if (t > 1)
                {
                    if (!Extend[1]) return false;
                    t = 1;
                }

                parameter = t;
                return true;
            }

            case 3:
            {
                double x0 = Coords[0], y0 = Coords[1], r0 = Coords[2];
                double x1 = Coords[3], y1 = Coords[4], r1 = Coords[5];
                double dx = x1 - x0;
                double dy = y1 - y0;
                // Persamaan kuadrat untuk t pada tabung kerucut terpancung.
                double a = dx * dx + dy * dy - (r1 - r0) * (r1 - r0);
                double px = x - x0;
                double py = y - y0;
                double b = -2 * (px * dx + py * dy + r0 * (r1 - r0));
                double c = px * px + py * py - r0 * r0;

                if (Math.Abs(a) < 1e-9)
                {
                    if (Math.Abs(b) < 1e-12) return false;
                    double linear = -c / b;
                    if (!ClampParameter(linear, ref linear)) return false;
                    parameter = linear;
                    return true;
                }

                double discriminant = b * b - 4 * a * c;
                if (discriminant < 0) return false;
                double root = Math.Sqrt(discriminant);
                double t1 = (-b + root) / (2 * a);
                double t2 = (-b - root) / (2 * a);
                double candidate = t1 > t2 ? t1 : t2;
                if (!ClampParameter(candidate, ref candidate)) return false;
                parameter = candidate;
                return true;
            }

            default:
                return false;
        }
    }

    private bool ClampParameter(double value, ref double clamped)
    {
        if (value < 0)
        {
            if (!Extend[0]) return false;
            clamped = 0;
            return true;
        }

        if (value > 1)
        {
            if (!Extend[1]) return false;
            clamped = 1;
            return true;
        }

        clamped = value;
        return true;
    }

    /// <summary>Warna pada parameter t (0..1).</summary>
    public void GetColor(double parameter, out double red, out double green, out double blue)
    {
        int components = Math.Max(1, ColorSpace.Components);
        var output = new double[Math.Max(components, Function?.OutputCount ?? components)];
        if (Function is not null)
        {
            Function.Evaluate(new[] { parameter }, output);
        }
        else
        {
            output[0] = parameter;
        }

        ColorSpace.ToRgb(output, out red, out green, out blue);
    }
}
