using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Aliran isi satu halaman: perintah gambar (teks, kotak, garis, gambar) yang
/// ditulis langsung sebagai operator PDF. Halaman yang memakai aliran ini
/// mencatat font dan gambar mana saja yang dipakai agar /Resources-nya ringkas.
/// </summary>
public sealed class PdfContentStream
{
    private readonly StringBuilder _operations = new();

    internal HashSet<PdfTimesFace> FontsUsed { get; } = new();

    /// <summary>
    /// Kode WinAnsi yang benar-benar tertulis per muka font. Dipakai penyemat
    /// font untuk memilih glif yang perlu ikut ke dalam subset TrueType.
    /// </summary>
    internal Dictionary<PdfTimesFace, HashSet<int>> CodesUsed { get; } = new();

    internal HashSet<string> ImagesUsed { get; } = new();

    /// <summary>Kotak terisi warna (dipakai latar, garis pemisah kop, dan bingkai sel).</summary>
    public void FillRectangle(float x, float y, float width, float height, PdfRgb color)
    {
        if (width <= 0 || height <= 0) return;
        _operations.Append("q ").Append(Color(color)).Append(' ')
            .Append(Number(x)).Append(' ').Append(Number(y)).Append(' ')
            .Append(Number(width)).Append(' ').Append(Number(height))
            .Append(" re f Q\n");
    }

    /// <summary>Garis tepi kotak (tanpa isi).</summary>
    public void StrokeRectangle(float x, float y, float width, float height, float thickness, PdfRgb color)
    {
        if (width <= 0 || height <= 0 || thickness <= 0) return;
        _operations.Append("q ").Append(Color(color)).Append(' ').Append(Number(thickness))
            .Append(" w ").Append(Number(x)).Append(' ').Append(Number(y)).Append(' ')
            .Append(Number(width)).Append(' ').Append(Number(height))
            .Append(" re S Q\n");
    }

    /// <summary>Garis lurus (dipakai garis bawah teks dan garis pemisah).</summary>
    public void DrawLine(float x1, float y1, float x2, float y2, float thickness, PdfRgb color)
    {
        _operations.Append("q ").Append(Color(color)).Append(' ').Append(Number(thickness))
            .Append(" w ").Append(Number(x1)).Append(' ').Append(Number(y1)).Append(" m ")
            .Append(Number(x2)).Append(' ').Append(Number(y2)).Append(" l S Q\n");
    }

    /// <summary>
    /// Tulis satu baris teks. <paramref name="wordSpacing"/> menambah jarak antar
    /// kata (untuk paragraf rata kanan-kiri) tanpa mengubah jarak antar huruf.
    /// </summary>
    public void DrawText(
        string? text,
        PdfTimesFace face,
        float fontSize,
        float x,
        float baselineY,
        PdfRgb color,
        float wordSpacing = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;

        FontsUsed.Add(face);
        if (!CodesUsed.TryGetValue(face, out var kodeDipakai))
        {
            kodeDipakai = new HashSet<int>();
            CodesUsed[face] = kodeDipakai;
        }

        foreach (char karakter in text) kodeDipakai.Add(PdfStandardTimes.EncodeChar(karakter));

        _operations.Append("q ").Append(Color(color)).Append(" BT /")
            .Append(PdfStandardTimes.ResourceName(face)).Append(' ').Append(Number(fontSize)).Append(" Tf ");
        if (wordSpacing > 0f)
        {
            _operations.Append(Number(wordSpacing)).Append(" Tw ");
        }

        _operations.Append("1 0 0 1 ").Append(Number(x)).Append(' ').Append(Number(baselineY))
            .Append(" Tm ").Append(Escape(text)).Append(" Tj ET Q\n");
    }

    /// <summary>Gambar terdaftar pada <see cref="PdfWriter.RegisterImage"/>.</summary>
    public void DrawImage(string resourceName, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0) return;
        ImagesUsed.Add(resourceName);
        _operations.Append("q ").Append(Number(width)).Append(" 0 0 ").Append(Number(height))
            .Append(' ').Append(Number(x)).Append(' ').Append(Number(y))
            .Append(" cm /").Append(resourceName).Append(" Do Q\n");
    }

    /// <summary>Byte aliran isi (terkompresi) yang siap ditulis ke berkas.</summary>
    internal byte[] Build() => PdfWriter.Compress(Encoding.Latin1.GetBytes(_operations.ToString()));

    /// <summary>Teks sebagai runtun byte WinAnsi yang aman di dalam tanda kurung.</summary>
    private static string Escape(string? text)
    {
        var builder = new StringBuilder("(");
        foreach (byte b in PdfStandardTimes.Encode(text))
        {
            switch (b)
            {
                case (byte)'(': builder.Append("\\("); break;
                case (byte)')': builder.Append("\\)"); break;
                case (byte)'\\': builder.Append("\\\\"); break;
                default:
                    if (b < 32 || b > 126) builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else builder.Append((char)b);
                    break;
            }
        }

        return builder.Append(')').ToString();
    }

    private static string Color(PdfRgb color) =>
        Number(color.R / 255.0) + " " + Number(color.G / 255.0) + " " + Number(color.B / 255.0) + " rg";

    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
