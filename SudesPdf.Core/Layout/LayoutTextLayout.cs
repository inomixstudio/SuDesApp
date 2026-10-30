using System;
using System.Collections.Generic;

namespace SudesPdf.Core.Layout;

/// <summary>Hasil penataan satu blok teks: baris-baris siap gambar.</summary>
internal sealed class LaidOutText
{
    internal List<LaidOutLine> Lines { get; } = new();

    /// <summary>Lebar baris terlebar (dipakai perataan kotak).</summary>
    internal float Width { get; set; }

    /// <summary>Tinggi seluruh baris.</summary>
    internal float Height
    {
        get
        {
            float total = 0;
            foreach (var line in Lines) total += line.Height;
            return total;
        }
    }
}

/// <summary>Satu baris yang sudah dipenggal dan dihitung garis dasarnya.</summary>
internal sealed class LaidOutLine
{
    internal List<LaidOutPart> Parts { get; } = new();

    internal float Width { get; set; }

    /// <summary>Tinggi kotak baris (ukuran huruf × pengali tinggi baris).</summary>
    internal float Height { get; set; }

    /// <summary>Jarak dari tepi atas baris ke garis dasar huruf.</summary>
    internal float Baseline { get; set; }

    /// <summary>Jumlah spasi pada baris — dipakai perataan kanan-kiri.</summary>
    internal int Spaces { get; set; }
}

/// <summary>Potongan teks yang sudah punya posisi dan gaya akhir.</summary>
internal sealed class LaidOutPart
{
    internal string Text { get; set; } = string.Empty;

    internal float X { get; set; }

    internal float Width { get; set; }

    internal PdfTimesFace Face { get; set; }

    internal float Size { get; set; }

    internal bool Underline { get; set; }

    internal PdfRgb Color { get; set; }

    internal bool IsSpace { get; set; }

    /// <summary>Pengali tinggi baris bagian ini (1,25 = 125% ukuran huruf).</summary>
    internal float LineHeightFactor { get; set; } = TextDefaults.LineHeight;
}

/// <summary>
/// Penataan teks: menyelesaikan gaya (ukuran, tebal, warna) dari bawaan blok,
/// kotak, dan halaman; memenggal baris mengikuti lebar yang tersedia; lalu
/// menghitung tinggi baris dan garis dasarnya memakai metrik Times.
/// </summary>
internal static class TextLayout
{
    internal static LaidOutText Layout(
        LayoutNode node,
        TextBlock block,
        float maxWidth,
        TextStyleDescriptor? pageDefault,
        int pageNumber,
        int totalPages)
    {
        var hasil = new LaidOutText();
        float indent = block.FirstLineIndent;
        bool barisPertama = true;

        foreach (var sourceLine in block.Lines)
        {
            var current = new LaidOutLine();
            float x = barisPertama ? indent : 0f;

            // Tambahkan satu potongan kata ke baris berjalan (dipakai baik untuk
            // kata utuh maupun untuk pecahan kata yang terlalu panjang).
            void TambahBagian(string bagian, bool spasi, (PdfTimesFace Face, float Size, bool Underline, PdfRgb Color, float LineHeightFactor) gaya)
            {
                float lebarBagian = TextMeasure(bagian, gaya);
                current.Parts.Add(new LaidOutPart
                {
                    Text = bagian,
                    X = x,
                    Width = lebarBagian,
                    Face = gaya.Face,
                    Size = gaya.Size,
                    Underline = gaya.Underline,
                    Color = gaya.Color,
                    IsSpace = spasi,
                    LineHeightFactor = gaya.LineHeightFactor,
                });
                if (spasi) current.Spaces++;
                x += lebarBagian;
            }
            // x sudah menyertakan indentasi paragraf; lebar tetap dihitung dari
            // tepi kanan kotak agar ruang baris pertama tidak terpotong dua kali.
            float lebarTersedia = Math.Max(1f, maxWidth);

            foreach (var span in sourceLine.Spans)
            {
                var style = ResolveSpan(span, node, pageDefault);
                string text = PageNumberText(span, pageNumber, totalPages);
                if (text.Length == 0) continue;

                foreach (var token in Tokenize(text))
                {
                    if (token.Break)
                    {
                        SelesaikanBaris(current);
                        hasil.Lines.Add(current);
                        current = new LaidOutLine();
                        x = 0f;
                        continue;
                    }

                    float lebarToken = TextMeasure(token.Text, style);
                    bool melebihi = x + lebarToken > lebarTersedia + 0.001f;
                    if (melebihi && !token.Space && PunyaIsi(current))
                    {
                        // Buang spasi di ujung baris: pemenggalan tidak menyisakan
                        // spasi yang menggantung di tepi kanan.
                        BuangSpasiTerakhir(current, ref x);
                        SelesaikanBaris(current);
                        hasil.Lines.Add(current);
                        current = new LaidOutLine();
                        x = 0f;
                    }

                    // Satu kata yang sendirian lebih lebar dari kotak — mis. NIK 16
                    // digit atau nomor rekening pada kolom sempit — dipecah per
                    // karakter. Tanpa ini teksnya menembus bingkai dan menimpa sel
                    // sebelahnya, bukan turun ke baris berikut di dalam sel.
                    if (!token.Space && x + lebarToken > lebarTersedia + 0.001f)
                    {
                        string sisa = token.Text;
                        while (sisa.Length > 0)
                        {
                            int ambil = sisa.Length;
                            while (ambil > 1 && x + TextMeasure(sisa[..ambil], style) > lebarTersedia + 0.001f)
                            {
                                ambil--;
                            }

                            TambahBagian(sisa[..ambil], false, style);
                            sisa = sisa[ambil..];

                            if (sisa.Length > 0)
                            {
                                SelesaikanBaris(current);
                                hasil.Lines.Add(current);
                                current = new LaidOutLine();
                                x = 0f;
                            }
                        }

                        continue;
                    }

                    TambahBagian(token.Text, token.Space, style);
                }
            }

            SelesaikanBaris(current);
            hasil.Lines.Add(current);
            barisPertama = false;
        }

        foreach (var line in hasil.Lines)
        {
            if (line.Width > hasil.Width) hasil.Width = line.Width;
        }

        return hasil;
    }

    /// <summary>Gaya akhir satu potongan teks.</summary>
    internal static (PdfTimesFace Face, float Size, bool Underline, PdfRgb Color, float LineHeightFactor) ResolveSpan(
        TextSpan span, LayoutNode node, TextStyleDescriptor? pageDefault)
    {
        var blockStyle = NearestDefault(node);
        float size = span.Size ?? blockStyle?.FontSizeValue ?? pageDefault?.FontSizeValue ?? TextDefaults.FontSize;
        bool bold = span.Bold ?? blockStyle?.BoldValue ?? pageDefault?.BoldValue ?? false;
        bool italic = span.Italic ?? blockStyle?.ItalicValue ?? pageDefault?.ItalicValue ?? false;
        PdfRgb color = span.Color ?? blockStyle?.Color ?? pageDefault?.Color ?? PdfRgb.Black;
        float factor = span.LineHeight ?? blockStyle?.LineHeightValue ?? pageDefault?.LineHeightValue ?? TextDefaults.LineHeight;

        var face = bold
            ? (italic ? PdfTimesFace.BoldItalic : PdfTimesFace.Bold)
            : (italic ? PdfTimesFace.Italic : PdfTimesFace.Roman);

        return (face, size, span.Underline, color, factor);
    }

    /// <summary>Gaya bawaan terdekat dari kotak pemilik teks ke atas.</summary>
    private static TextStyleDescriptor? NearestDefault(LayoutNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.DefaultText is { } style) return style;
        }

        return null;
    }

    private static string PageNumberText(TextSpan span, int pageNumber, int totalPages) => span.Number switch
    {
        PageNumberKind.Current => pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PageNumberKind.Total => totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => span.Text ?? string.Empty,
    };

    internal static float TextMeasure(string text, (PdfTimesFace Face, float Size, bool Underline, PdfRgb Color, float LineHeightFactor) style) =>
        (float)PdfStandardTimes.MeasureString(style.Face, style.Size, text);

    private static void SelesaikanBaris(LaidOutLine line)
    {
        foreach (var part in line.Parts)
        {
            if (part.Width == 0 && part.Text.Length == 0) continue;
            line.Width = Math.Max(line.Width, part.X + part.Width);
            line.Height = Math.Max(line.Height, part.Size * StyleFactor(part));
            line.Baseline = Math.Max(line.Baseline, BaselineOf(part));
        }

        if (line.Height <= 0f) line.Height = TextDefaults.FontSize * TextDefaults.LineHeight;
        if (line.Baseline <= 0f) line.Baseline = (float)(TextDefaults.FontSize * PdfStandardTimes.Ascent / 1000.0);
    }

    private static float StyleFactor(LaidOutPart part) => part.LineHeightFactor <= 0f ? TextDefaults.LineHeight : part.LineHeightFactor;

    private static float BaselineOf(LaidOutPart part)
    {
        float factor = StyleFactor(part);
        double contentHeight = part.Size * (PdfStandardTimes.Ascent + PdfStandardTimes.Descent) / 1000.0;
        double boxHeight = part.Size * factor;
        double sisa = Math.Max(0, boxHeight - contentHeight) / 2.0;
        return (float)(sisa + part.Size * PdfStandardTimes.Ascent / 1000.0);
    }

    private static bool PunyaIsi(LaidOutLine line)
    {
        foreach (var part in line.Parts)
        {
            if (!part.IsSpace) return true;
        }

        return false;
    }

    private static void BuangSpasiTerakhir(LaidOutLine line, ref float x)
    {
        while (line.Parts.Count > 0)
        {
            var last = line.Parts[^1];
            if (!last.IsSpace) break;
            line.Parts.RemoveAt(line.Parts.Count - 1);
            x -= last.Width;
            if (line.Spaces > 0) line.Spaces--;
        }
    }

    private readonly struct Token
    {
        internal Token(string text, bool space, bool lineBreak)
        {
            Text = text;
            Space = space;
            Break = lineBreak;
        }

        internal string Text { get; }

        /// <summary>Benar bila token ini hanya spasi (boleh dibuang saat memenggal).</summary>
        internal bool Space { get; }

        /// <summary>Benar bila token ini menandai ganti baris paksa.</summary>
        internal bool Break { get; }
    }

    private static IEnumerable<Token> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                yield return new Token(string.Empty, false, true);
                i++;
                continue;
            }

            if (c == '\r')
            {
                i++;
                continue;
            }

            bool space = c == ' ' || c == '\t';
            int start = i;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t') == space && text[i] != '\n' && text[i] != '\r') i++;
            yield return new Token(text.Substring(start, i - start), space, false);
        }
    }
}
