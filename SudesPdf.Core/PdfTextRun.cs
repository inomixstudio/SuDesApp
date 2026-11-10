using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Satu kemunculan teks: dipakai untuk menggambar glif (lewat matriks glif)
/// sekaligus membaca isi halaman (lewat posisi tiap glif).
/// </summary>
public sealed class PdfTextRun
{
    public required PdfFont Font { get; init; }

    /// <summary>Ukuran huruf efektif pada ruang perangkat.</summary>
    public double FontSize { get; init; }

    /// <summary>Teks Unicode dari glif yang ditampilkan.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Matriks dari satuan glif font ke ruang perangkat.</summary>
    public PdfMatrix GlyphMatrix { get; init; } = PdfMatrix.Identity;

    /// <summary>Nomor glif tiap kode; -1 bila glif tidak tersedia.</summary>
    public int[] GlyphIds { get; init; } = Array.Empty<int>();

    /// <summary>Posisi absis tiap glif di ruang perangkat.</summary>
    public double[] GlyphX { get; init; } = Array.Empty<double>();

    /// <summary>Posisi ordinat tiap glif di ruang perangkat.</summary>
    public double[] GlyphY { get; init; } = Array.Empty<double>();

    /// <summary>Perpindahan tiap glif (besaran pada arah teks).</summary>
    public double[] GlyphAdvance { get; init; } = Array.Empty<double>();

    /// <summary>Awal dan akhir teks pada ruang perangkat.</summary>
    public double StartX { get; init; }

    public double StartY { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    /// <summary>Arah penulisan satuan (untuk menyusun urutan baca).</summary>
    public double DirectionX { get; init; } = 1;

    public double DirectionY { get; init; }

    /// <summary>Mode tampilan teks PDF (0 isi, 1 garis, 2 dua-duanya, 3 tak terlihat).</summary>
    public int RenderMode { get; init; }

    public PdfPaint Fill { get; init; }

    public PdfPaint Stroke { get; init; }

    public double StrokeWidth { get; init; } = 1;
}

