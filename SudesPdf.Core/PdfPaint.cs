using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Warna siap pakai pada ruang perangkat.</summary>
public struct PdfPaint
{
    public double R;
    public double G;
    public double B;
    public double Alpha;

    public PdfTilingPattern? Pattern;

    /// <summary>Matriks ruang pola ke ruang perangkat (diisi penafsir).</summary>
    public PdfMatrix PatternMatrix;

    public readonly bool IsPattern => Pattern is not null;

    public static PdfPaint FromRgb(double r, double g, double b, double alpha) => new()
    {
        R = r,
        G = g,
        B = b,
        Alpha = alpha
    };
}

