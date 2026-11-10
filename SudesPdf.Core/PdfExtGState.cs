using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Status grafik tambahan (/ExtGState).</summary>
public sealed class PdfExtGState
{
    public double FillAlpha { get; set; } = 1;

    public double StrokeAlpha { get; set; } = 1;

    public PdfBlendMode Blend { get; set; } = PdfBlendMode.Normal;

    public double? LineWidth { get; set; }

    public PdfLineCap? Cap { get; set; }

    public PdfLineJoin? Join { get; set; }

    public double? MiterLimit { get; set; }

    public double[]? DashArray { get; set; }

    public double? DashPhase { get; set; }

    public PdfImage? SoftMask { get; set; }

    public bool AlphaIsShape { get; set; }

    public static PdfExtGState? Create(PdfDictionary? dictionary, PdfDocument document)
    {
        if (dictionary is null) return null;
        var state = new PdfExtGState
        {
            FillAlpha = dictionary.GetNumber("ca") ?? 1,
            StrokeAlpha = dictionary.GetNumber("CA") ?? 1,
            LineWidth = dictionary.GetNumber("LW"),
            MiterLimit = dictionary.GetNumber("ML"),
            AlphaIsShape = dictionary.GetBool("AIS") ?? false
        };

        if (dictionary.GetInt("LC") is { } cap) state.Cap = (PdfLineCap)Math.Clamp(cap, 0, 2);
        if (dictionary.GetInt("LJ") is { } join) state.Join = (PdfLineJoin)Math.Clamp(join, 0, 2);
        state.Blend = ParseBlend(dictionary.GetNameValue("BM"));

        var dash = document.ResolveArray(dictionary.Raw("D"));
        if (dash is not null && dash.Count >= 2)
        {
            var phase = document.ResolveNumber(dash.At(0))?.Value ?? 0;
            var pattern = document.ResolveArray(dash.At(1));
            if (pattern is not null)
            {
                var values = new List<double>(pattern.Count);
                foreach (var item in pattern.Items) values.Add(document.ResolveNumber(item)?.Value ?? 0);
                state.DashArray = values.ToArray();
                state.DashPhase = phase;
            }
        }

        var mask = document.ResolveDictionary(dictionary.Raw("SMask"));
        if (mask is not null && mask.GetNameValue("S") == "Alpha")
        {
            var group = document.ResolveStream(mask.Raw("G"));
            if (group is not null)
            {
                state.SoftMask = PdfImages.DecodeGroupMask(document, group);
            }
        }

        return state;
    }

    private static PdfBlendMode ParseBlend(string? name) => name switch
    {
        "Multiply" => PdfBlendMode.Multiply,
        "Screen" => PdfBlendMode.Screen,
        "Overlay" => PdfBlendMode.Overlay,
        "Darken" => PdfBlendMode.Darken,
        "Lighten" => PdfBlendMode.Lighten,
        "ColorDodge" => PdfBlendMode.ColorDodge,
        "ColorBurn" => PdfBlendMode.ColorBurn,
        "HardLight" => PdfBlendMode.HardLight,
        "SoftLight" => PdfBlendMode.SoftLight,
        "Difference" => PdfBlendMode.Difference,
        "Exclusion" => PdfBlendMode.Exclusion,
        _ => PdfBlendMode.Normal
    };
}

