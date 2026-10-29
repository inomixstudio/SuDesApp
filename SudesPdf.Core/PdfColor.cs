using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>
/// Ruang warna PDF (ISO 32000-1 §8.6) yang sudah disederhanakan menjadi
/// perhitungan RGB. Profil ICC tidak ditafsirkan; jumlah komponennya dipakai
/// sebagai pendekatan (Gray/RGB/CMYK), sama seperti penampil sederhana lain.
/// </summary>
public sealed class PdfColorSpace
{
    private readonly byte[]? _lookupTable;
    private readonly PdfColorSpace? _base;
    private readonly PdfFunction? _tint;
    private readonly double _gamma;

    private PdfColorSpace(
        string name, int components, byte[]? lookupTable,
        PdfColorSpace? baseSpace, PdfFunction? tint, double gamma)
    {
        Name = name;
        Components = components;
        _lookupTable = lookupTable;
        _base = baseSpace;
        _tint = tint;
        _gamma = gamma <= 0 ? 1 : gamma;
    }

    public string Name { get; }

    public int Components { get; }

    public bool IsPattern => Name == "Pattern";

    public static readonly PdfColorSpace DeviceGray = new("DeviceGray", 1, null, null, null, 1);

    public static readonly PdfColorSpace DeviceRgb = new("DeviceRGB", 3, null, null, null, 1);

    public static readonly PdfColorSpace DeviceCmyk = new("DeviceCMYK", 4, null, null, null, 1);

    public static readonly PdfColorSpace Pattern = new("Pattern", 0, null, null, null, 1);

    private static readonly Dictionary<string, PdfColorSpace> Named = new(StringComparer.Ordinal)
    {
        ["DeviceGray"] = DeviceGray,
        ["G"] = DeviceGray,
        ["DeviceRGB"] = DeviceRgb,
        ["RGB"] = DeviceRgb,
        ["DeviceCMYK"] = DeviceCmyk,
        ["CMYK"] = DeviceCmyk,
        ["Pattern"] = Pattern,
        ["CalGray"] = DeviceGray,
        ["CalRGB"] = DeviceRgb,
        ["Lab"] = DeviceRgb
    };

    public static PdfColorSpace? Resolve(PdfObject? value, PdfDocument document, int depth = 0)
    {
        if (depth > 8) return DeviceGray;
        switch (document.Resolve(value))
        {
            case PdfName name:
                if (Named.TryGetValue(name.Value, out var known)) return known;
                return DeviceGray;

            case PdfArray array when array.Count > 0:
            {
                string? family = (document.Resolve(array.At(0)) as PdfName)?.Value;
                switch (family)
                {
                    case "ICCBased":
                    {
                        var profile = document.ResolveStream(array.At(1));
                        int components = (int)(profile?.Dictionary.GetNumber("N") ?? 1);
                        return components switch
                        {
                            1 => new PdfColorSpace("ICCBased-Gray", 1, null, null, null, 1),
                            4 => new PdfColorSpace("ICCBased-CMYK", 4, null, null, null, 1),
                            _ => new PdfColorSpace("ICCBased-RGB", 3, null, null, null, 1)
                        };
                    }

                    case "Indexed":
                    case "I":
                    {
                        var baseSpace = Resolve(array.At(1), document, depth + 1) ?? DeviceRgb;
                        int high = (int)(document.ResolveNumber(array.At(2))?.Value ?? 0);
                        byte[]? table = ReadLookup(array.At(3), document, baseSpace.Components, high + 1);
                        return new PdfColorSpace("Indexed", 1, table, baseSpace, null, 1);
                    }

                    case "Separation":
                    {
                        var alternate = Resolve(array.At(2), document, depth + 1) ?? DeviceGray;
                        var tint = PdfFunction.Create(array.At(3), document);
                        return new PdfColorSpace("Separation", 1, null, alternate, tint, 1);
                    }

                    case "DeviceN":
                    {
                        int components = document.ResolveArray(array.At(1))?.Count ?? 1;
                        var alternate = Resolve(array.At(2), document, depth + 1) ?? DeviceGray;
                        var tint = PdfFunction.Create(array.At(3), document);
                        return new PdfColorSpace("DeviceN", Math.Max(1, components), null, alternate, tint, 1);
                    }

                    case "CalGray":
                    {
                        var parameters = document.ResolveDictionary(array.At(1));
                        double gamma = parameters?.GetNumber("Gamma") ?? 1;
                        return new PdfColorSpace("CalGray", 1, null, null, null, gamma);
                    }

                    case "CalRGB":
                        return DeviceRgb;

                    case "Lab":
                        return DeviceRgb;

                    case "Pattern":
                        return Pattern;

                    default:
                        return DeviceGray;
                }
            }

            case PdfDictionary streamDictionary when streamDictionary.GetNameValue("N") is not null:
            {
                int components = (int)(streamDictionary.GetNumber("N") ?? 1);
                return components switch
                {
                    1 => new PdfColorSpace("ICCBased-Gray", 1, null, null, null, 1),
                    4 => new PdfColorSpace("ICCBased-CMYK", 4, null, null, null, 1),
                    _ => new PdfColorSpace("ICCBased-RGB", 3, null, null, null, 1)
                };
            }

            default:
                return null;
        }
    }

    private static byte[]? ReadLookup(PdfObject? value, PdfDocument document, int components, int count)
    {
        byte[]? data = document.Resolve(value) switch
        {
            PdfString text => text.Bytes,
            PdfStream stream => stream.Decoded(),
            _ => null
        };

        if (data is null || components <= 0) return data;
        // Tabel boleh lebih panjang dari jumlah warna; potong sesuai kebutuhan.
        int needed = components * count;
        if (data.Length > needed) Array.Resize(ref data, needed);
        return data;
    }

    /// <summary>Ubah komponen warna menjadi RGB (0..1).</summary>
    public void ToRgb(double[] values, out double red, out double green, out double blue)
    {
        double Component(int index) => index < values.Length ? values[index] : 0;

        switch (Name)
        {
            case "DeviceGray":
            case "CalGray":
            {
                double gray = Math.Pow(Math.Clamp(Component(0), 0, 1), 1.0 / _gamma);
                gray = Math.Clamp(gray, 0, 1);
                red = green = blue = gray;
                return;
            }

            case "DeviceCMYK":
            case "ICCBased-CMYK":
            {
                double c = Math.Clamp(Component(0), 0, 1);
                double m = Math.Clamp(Component(1), 0, 1);
                double y = Math.Clamp(Component(2), 0, 1);
                double k = Math.Clamp(Component(3), 0, 1);
                red = 1 - Math.Min(1, c + k);
                green = 1 - Math.Min(1, m + k);
                blue = 1 - Math.Min(1, y + k);
                return;
            }

            case "Indexed":
            {
                int index = (int)Math.Round(Component(0));
                if (_lookupTable is null || _base is null)
                {
                    double gray = Math.Clamp(index / 255.0, 0, 1);
                    red = green = blue = gray;
                    return;
                }

                int components = _base.Components;
                int offset = index * components;
                var parts = new double[components];
                for (int i = 0; i < components; i++)
                {
                    parts[i] = offset + i < _lookupTable.Length ? _lookupTable[offset + i] / 255.0 : 0;
                }

                _base.ToRgb(parts, out red, out green, out blue);
                return;
            }

            case "Separation":
            case "DeviceN":
            {
                var alternate = _base ?? DeviceGray;
                var tintValue = new double[alternate.Components];
                if (_tint is not null)
                {
                    var input = new double[Components];
                    for (int i = 0; i < Components; i++) input[i] = Component(i);
                    var output = new double[Math.Max(alternate.Components, _tint.OutputCount)];
                    _tint.Evaluate(input, output);
                    for (int i = 0; i < tintValue.Length; i++) tintValue[i] = output[i];
                }
                else
                {
                    tintValue[0] = Component(0);
                }

                alternate.ToRgb(tintValue, out red, out green, out blue);
                return;
            }

            default:
            {
                // DeviceRGB, ICCBased-RGB, CalRGB, Lab (pendekatan).
                red = Math.Clamp(Component(0), 0, 1);
                green = Math.Clamp(Component(1), 0, 1);
                blue = Math.Clamp(Component(2), 0, 1);
                return;
            }
        }
    }
}

/// <summary>Warna yang sedang aktif beserta kekhususan pola (pattern).</summary>
public sealed class PdfColorValue
{
    private double[] _components = new double[3];

    public PdfColorSpace Space { get; set; } = PdfColorSpace.DeviceGray;

    public PdfTilingPattern? Pattern { get; set; }

    /// <summary>Ruang warna dasar untuk pola tak berwarna ([/Pattern &lt;ruang&gt;]).</summary>
    public PdfColorSpace? PatternBaseSpace { get; set; }

    public double[] Components => _components;

    public void Set(PdfColorSpace space, IReadOnlyList<double> values)
    {
        SetCore(space, values);
    }

    private void SetCore(PdfColorSpace space, IReadOnlyList<double> values)
    {
        Space = space;
        if (_components.Length < values.Count) _components = new double[values.Count];
        for (int i = 0; i < values.Count; i++) _components[i] = values[i];
        for (int i = values.Count; i < _components.Length; i++) _components[i] = 0;
    }

    public void GetRgb(out double red, out double green, out double blue)
    {
        if (Space.IsPattern)
        {
            // Pola tak berwarna: komponen adalah warna dasar yang diminta scn.
            var baseSpace = PatternBaseSpace;
            if (baseSpace is null)
            {
                red = green = blue = 0;
                return;
            }

            baseSpace.ToRgb(_components, out red, out green, out blue);
            return;
        }

        Space.ToRgb(_components, out red, out green, out blue);
    }
}
