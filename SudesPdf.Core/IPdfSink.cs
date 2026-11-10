using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Tujuan penggambaran halaman (raster, ekstraksi teks, dsb.).</summary>
public interface IPdfSink
{
    void Save();

    void Restore();

    void Clip(PdfPath path, PdfFillRule rule);

    void Fill(PdfPath path, PdfFillRule rule, in PdfPaint paint);

    void Stroke(PdfPath path, in PdfStrokeSpec spec, in PdfPaint paint);

    void DrawImage(PdfImage image, in PdfMatrix matrix, double alpha, bool interpolate);

    void DrawShading(PdfShading shading, in PdfMatrix matrix, double alpha);

    void ShowText(PdfTextRun run);
}

