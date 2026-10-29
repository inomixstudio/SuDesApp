using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>
/// Pola ubin PDF (/PatternType 1). Isi sel pola digambar ulang pada matriks pola,
/// lalu diulang sesuai /XStep dan /YStep oleh penggambar raster.
/// </summary>
public sealed class PdfTilingPattern
{
    private PdfTilingPattern(PdfStream stream, PdfDocument document)
    {
        Stream = stream;
        Document = document;
    }

    public PdfStream Stream { get; }

    public PdfDocument Document { get; }

    public PdfMatrix Matrix { get; private set; } = PdfMatrix.Identity;

    public PdfRect BBox { get; private set; }

    public double XStep { get; private set; }

    public double YStep { get; private set; }

    public int PaintType { get; private set; } = 1;

    public int TilingType { get; private set; } = 1;

    public PdfDictionary? Resources { get; private set; }

    public IReadOnlyList<byte[]> Contents { get; private set; } = Array.Empty<byte[]>();

    public static PdfTilingPattern Create(PdfStream stream, PdfDocument document)
    {
        var pattern = new PdfTilingPattern(stream, document);
        var dict = stream.Dictionary;

        pattern.PaintType = dict.GetInt("PaintType") ?? 1;
        pattern.TilingType = dict.GetInt("TilingType") ?? 1;
        pattern.XStep = document.GetNumber(dict, "XStep", 0);
        pattern.YStep = document.GetNumber(dict, "YStep", 0);
        pattern.Matrix = ReadMatrix(document.ResolveArray(dict.Raw("Matrix")));
        var box = document.ResolveArray(dict.Raw("BBox"));
        pattern.BBox = ReadBox(box);
        pattern.Resources = document.ResolveDictionary(stream.Dictionary.Raw("Resources"));
        pattern.Contents = new[] { stream.Decoded() };

        if (Math.Abs(pattern.XStep) < 1e-6 || Math.Abs(pattern.YStep) < 1e-6) return pattern;
        if (pattern.BBox.Width <= 0 || pattern.BBox.Height <= 0) return pattern;
        return pattern;
    }

    private static PdfMatrix ReadMatrix(PdfArray? array)
    {
        if (array is null || array.Count < 6) return PdfMatrix.Identity;
        double[] parts = new double[6];
        for (int i = 0; i < 6; i++)
        {
            if (array.At(i) is PdfNumber number) parts[i] = number.Value;
        }

        return new PdfMatrix(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }

    private static PdfRect ReadBox(PdfArray? array)
    {
        if (array is null || array.Count < 4) return default;
        double[] parts = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (array.At(i) is PdfNumber number) parts[i] = number.Value;
        }

        return PdfRect.FromEdges(
            Math.Min(parts[0], parts[2]), Math.Min(parts[1], parts[3]),
            Math.Max(parts[0], parts[2]), Math.Max(parts[1], parts[3]));
    }
}
