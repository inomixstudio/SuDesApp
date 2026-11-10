using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

internal sealed partial class LayoutEngine
    {

    // --------------------------------------------------------------- gambar

    private float PlaceImage(LayoutNode node, float left, float yTop, float width, PageContext ctx, bool draw)
    {
        float innerWidth = Math.Max(0f, width - node.PaddingLeft - node.PaddingRight);
        var (lebarAsli, tinggiAsli) = UkuranGambar(node);
        if (lebarAsli <= 0 || tinggiAsli <= 0) return node.PaddingTop + node.PaddingBottom;

        float boxWidth = node.Width ?? innerWidth;
        float boxHeight = node.Height ?? (boxWidth * tinggiAsli / lebarAsli);

        float skala = Math.Min(boxWidth / lebarAsli, boxHeight / tinggiAsli);
        float gambarWidth = lebarAsli * skala;
        float gambarHeight = tinggiAsli * skala;

        float x = left + node.PaddingLeft + Math.Max(0f, (innerWidth - gambarWidth) / 2f);
        float y = yTop - node.PaddingTop - Math.Max(0f, (boxHeight - gambarHeight) / 2f);

        if (draw) ctx.DrawImage(node, x, y, gambarWidth, gambarHeight);

        return node.PaddingTop + boxHeight + node.PaddingBottom + node.MarginTop + node.MarginBottom;
    }

    internal (int Width, int Height) UkuranGambar(LayoutNode node)
    {
        string kunci = KunciGambar(node);
        if (kunci.Length == 0) return (0, 0);
        if (_ukuranGambar.TryGetValue(kunci, out var ukuran)) return ukuran;

        var gambar = GambarAsli(node);
        ukuran = (gambar.Width, gambar.Height);

        _ukuranGambar[kunci] = ukuran;
        return ukuran;
    }

    /// <summary>
    /// Nama resource gambar untuk kotak selebar/tinggi <paramref name="widthPt"/> ×
    /// <paramref name="heightPt"/> point. Gambar diperkecil ke resolusi cetak yang
    /// benar-benar dipakai, sehingga kop 1507 px tidak lagi ditanam untuk 250 px.
    /// </summary>
    internal string? NamaGambar(LayoutNode node, float widthPt, float heightPt)
    {
        string kunci = KunciGambar(node);
        if (kunci.Length == 0) return null;

        var dasar = GambarAsli(node);
        var (pikselX, pikselY) = dasar.PikselCetak(widthPt, heightPt);
        string kunciTanam = $"{kunci}@{pikselX}x{pikselY}";
        if (_namaGambar.TryGetValue(kunciTanam, out var nama)) return nama;

        nama = _writer.RegisterImage(dasar.UntukUkuran(widthPt, heightPt));

        _namaGambar[kunciTanam] = nama;
        return nama;
    }

    /// <summary>
    /// Gambar asli yang sudah diurai, sekali per isi/jalur. Ukuran gambar dibaca
    /// dari sini juga, jadi setiap berkas hanya di-dekode satu kali.
    /// </summary>
    private PdfWriterImage GambarAsli(LayoutNode node)
    {
        string kunci = KunciGambar(node);
        if (_gambar.TryGetValue(kunci, out var gambar)) return gambar;

        gambar = node.ImageBytes is { Length: > 0 } bytes
            ? PdfWriterImage.FromBytes(bytes)
            : PdfWriterImage.FromFile(node.ImagePath!);

        _gambar[kunci] = gambar;
        return gambar;
    }

    /// <summary>Kunci gambar: isi byte atau jalur berkasnya.</summary>
    private static string KunciGambar(LayoutNode node)
    {
        if (node.ImageBytes is { Length: > 0 } bytes)
        {
            // Panjang byte bukan identitas gambar: dua logo yang berbeda bisa
            // berukuran sama. Kunci penuh mencegah salah pakai ukuran/resource.
            return "#" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        return string.IsNullOrWhiteSpace(node.ImagePath) ? string.Empty : node.ImagePath!;
    }

    private static void DrawBox(LayoutNode node, float left, float top, float width, float height, PageContext ctx)
    {
        if (node.Background is { } latar) ctx.FillRect(left, top, width, height, latar);
        if (node.BorderThickness > 0f) ctx.StrokeRect(left, top, width, height, node.BorderThickness, node.BorderColor);
        if (node.BorderBottomThickness > 0f)
        {
            ctx.DrawLine(left, top - height, left + width, top - height, node.BorderBottomThickness, node.BorderColor);
        }
    }
}

/// <summary>
/// Satu lembar kertas yang sedang diisi: luas isi, nomor halaman, dan
/// penulisan gambar pada titik PDF (y ke atas, diukur dari tepi bawah).
/// </summary>
internal sealed class PageContext
{
    private readonly LayoutEngine _engine;

    internal PageContext(LayoutEngine engine, PdfWriterPage page, PageDescriptor settings, int pageNumber, int totalPages)
    {
        _engine = engine;
        Page = page;
        Settings = settings;
        PageNumber = pageNumber;
        TotalPages = totalPages;
    }

    internal PdfWriterPage Page { get; }

    internal PageDescriptor Settings { get; }

    internal int PageNumber { get; }

    internal int TotalPages { get; }

    /// <summary>Benar bila hasil tata letak ditulis (bukan sekadar dihitung).</summary>
    internal bool Draw { get; set; } = true;

    internal float ContentLeft { get; private set; }

    internal float ContentTop { get; private set; }

    internal float ContentBottom { get; private set; }

    internal float ContentWidth { get; private set; }

    internal void SetArea(float left, float top, float bottom, float width)
    {
        ContentLeft = left;
        ContentTop = top;
        ContentBottom = bottom;
        ContentWidth = width;
    }

    /// <summary>Kotak berisi warna; <paramref name="top"/> adalah tepi atas kotak.</summary>
    internal void FillRect(float x, float top, float width, float height, PdfRgb color) =>
        Page.Content.FillRectangle(x, top - height, width, height, color);

    /// <summary>Bingkai kotak; <paramref name="top"/> adalah tepi atas kotak.</summary>
    internal void StrokeRect(float x, float top, float width, float height, float thickness, PdfRgb color) =>
        Page.Content.StrokeRectangle(x, top - height, width, height, thickness, color);

    internal void DrawLine(float x1, float y1, float x2, float y2, float thickness, PdfRgb color) =>
        Page.Content.DrawLine(x1, y1, x2, y2, thickness, color);

    internal void DrawText(LaidOutPart part, float x, float baselineY, float wordSpacing) =>
        Page.Content.DrawText(part.Text, part.Face, part.Size, x, baselineY, part.Color, wordSpacing);

    /// <summary>Gambar pada kotak selebar <paramref name="width"/> dan setinggi <paramref name="height"/>.</summary>
    internal void DrawImage(LayoutNode node, float x, float top, float width, float height)
    {
        string? nama = _engine.NamaGambar(node, width, height);
        if (nama is null) return;
        Page.Content.DrawImage(nama, x, top - height, width, height);
    }

    }
