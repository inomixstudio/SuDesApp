using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

/// <summary>Hasil tata letak halaman-halaman dokumen menjadi satu berkas PDF.</summary>
internal static class LayoutFlow
{
    internal static byte[] Build(List<PageDescriptor> pages, DocumentMetadata? metadata = null)
    {
        bool perluNomorHalaman = false;
        foreach (var page in pages)
        {
            perluNomorHalaman |= PunyaNomorHalaman(page.ContentNode)
                || PunyaNomorHalaman(page.HeaderNode)
                || PunyaNomorHalaman(page.FooterNode);
        }

        // Nomor halaman (“Hal. 3 dari 12”) mengubah lebar teks, jadi dokumen
        // ditata ulang setelah jumlah halamannya diketahui. Tanpa nomor
        // halaman, sekali tata sudah cukup.
        var keterangan = metadata is null ? null : new PdfMetadata
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Subject = metadata.Subject,
            Keywords = metadata.Keywords,
            Creator = metadata.Creator,
        };

        int jumlahHalaman = 1;
        PdfWriter writer = new();
        int percobaan = perluNomorHalaman ? 5 : 1;
        for (int i = 0; i < percobaan; i++)
        {
            writer = new PdfWriter { Metadata = keterangan };
            var mesin = new LayoutEngine(writer, jumlahHalaman);
            foreach (var page in pages) mesin.Flow(page);
            if (!perluNomorHalaman || writer.PageCount == jumlahHalaman) break;
            jumlahHalaman = writer.PageCount;
        }

        return writer.Finish();
    }

    private static bool PunyaNomorHalaman(LayoutNode? node)
    {
        if (node is null) return false;
        if (node.Text is { } text)
        {
            foreach (var line in text.Lines)
            {
                foreach (var span in line.Spans)
                {
                    if (span.Number != PageNumberKind.None) return true;
                }
            }
        }

        foreach (var child in node.Children)
        {
            if (PunyaNomorHalaman(child)) return true;
        }

        if (node.HeaderCells is { } header)
        {
            foreach (var cell in header)
            {
                if (PunyaNomorHalaman(cell)) return true;
            }
        }

        return false;
    }
}

/// <summary>Sisa isi yang belum tertata: simpul, indeks anak/baris, dan sisanya di dalamnya.</summary>
internal sealed class Continuation
{
    internal Continuation(LayoutNode node, int index, Continuation? inner)
    {
        Node = node;
        Index = index;
        Inner = inner;
    }

    internal LayoutNode Node { get; }

    /// <summary>Indeks anak (kolom), baris (tabel), atau baris teks berikutnya.</summary>
    internal int Index { get; }

    /// <summary>Lanjutan di dalam anak yang sedang dikerjakan.</summary>
    internal Continuation? Inner { get; }
}

/// <summary>
/// Mesin tata letak: mengalirkan isi halaman ke kertas, memenggal pada batas
/// baris/baris tabel, mengulang kepala tabel, dan menggambar hasilnya lewat
/// <see cref="PdfWriter"/>.
/// </summary>
internal sealed class LayoutEngine
{
    private const float Epsilon = 0.01f;

    private readonly PdfWriter _writer;
    private readonly int _totalPages;
    private readonly Dictionary<string, (int Width, int Height)> _ukuranGambar = new();
    private readonly Dictionary<string, string?> _namaGambar = new();

    internal LayoutEngine(PdfWriter writer, int totalPages)
    {
        _writer = writer;
        _totalPages = totalPages;
    }

    /// <summary>Alirkan satu kelompok halaman: isinya boleh memakai beberapa lembar.</summary>
    internal void Flow(PageDescriptor settings)
    {
        float contentWidth = settings.SheetSize.Width - settings.LeftMargin - settings.RightMargin;
        Continuation? pending = new(settings.ContentNode, 0, null);

        int penjaga = 0;
        while (pending is not null && penjaga++ < 4000)
        {
            var physical = _writer.AddPage(settings.SheetSize.Width, settings.SheetSize.Height);
            var ctx = new PageContext(this, physical, settings, _writer.PageCount, _totalPages);

            if (settings.BackColor != PdfRgb.White)
            {
                ctx.FillRect(0f, settings.SheetSize.Height, settings.SheetSize.Width, settings.SheetSize.Height, settings.BackColor);
            }

            // Seluruh penataan memakai jarak dari tepi BAWAH kertas, sama dengan
            // titik PDF (y ke atas): mengurangi berarti bergerak turun.
            float top = settings.SheetSize.Height - settings.TopMargin;
            float bottom = settings.BottomMargin;

            if (settings.HeaderNode is { } header)
            {
                float tinggi = MeasureNodeHeight(header, contentWidth, ctx);
                PlaceNoSplit(header, settings.LeftMargin, top, contentWidth, ctx, draw: true);
                top -= tinggi;
            }

            if (settings.FooterNode is { } footer)
            {
                float tinggi = MeasureNodeHeight(footer, contentWidth, ctx);
                bottom += tinggi;
                PlaceNoSplit(footer, settings.LeftMargin, bottom, contentWidth, ctx, draw: true);
            }

            ctx.SetArea(settings.LeftMargin, top, bottom, contentWidth);
            pending = PlacePage(pending, ctx);
        }
    }

    /// <summary>Taruh sisa isi pada satu halaman; kembalikan sisanya bila tidak muat.</summary>
    private Continuation? PlacePage(Continuation state, PageContext ctx)
    {
        float y = ctx.ContentTop;
        Continuation? current = state;
        int penjaga = 0;

        while (current is not null && penjaga++ < 20000)
        {
            float budget = y - ctx.ContentBottom;
            bool atTop = y >= ctx.ContentTop - Epsilon;
            float dipakai = PlaceNode(current.Node, ctx.ContentLeft, y, budget, ctx.ContentWidth, ctx, current, out var next, atTop, draw: true);
            y -= dipakai;

            // Tidak ada yang tertata dan masih ada sisa: pindah ke halaman berikutnya.
            if (dipakai <= 0.001f && next is not null) return next;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Tuliskan satu simpul pada ruang setinggi <paramref name="budget"/>.
    /// Kembalikan tinggi yang terpakai; <paramref name="next"/> berisi sisanya.
    /// </summary>
    private float PlaceNode(
        LayoutNode node,
        float x,
        float yTop,
        float budget,
        float width,
        PageContext ctx,
        Continuation state,
        out Continuation? next,
        bool atPageTop,
        bool draw = true)
    {
        if (node.Kind == LayoutKind.PageBreak)
        {
            next = null;
            return 0f;
        }

        if (!IsAtomic(node))
        {
            switch (node.Kind)
            {
                case LayoutKind.Text:
                    return PlaceText(node, x, yTop, width, ctx, budget, state.Index, atPageTop, out next, draw);
                case LayoutKind.Column:
                    return PlaceColumn(node, x, yTop, width, ctx, budget, state.Index, state.Inner, atPageTop, out next, draw);
                case LayoutKind.Table:
                    return PlaceTable(node, x, yTop, width, ctx, budget, state.Index, atPageTop, out next, draw);
            }
        }

        // Kotak yang tidak boleh terpotong: pindah halaman bila tidak muat.
        float tinggi = MeasureNodeHeight(node, width, ctx);
        if (tinggi > budget + Epsilon && !atPageTop)
        {
            next = state;
            return 0f;
        }

        PlaceNoSplit(node, x, yTop, width, ctx, draw);
        next = null;
        return tinggi;
    }

    /// <summary>Kotak yang selalu ditata utuh (tidak dipenggal batas halaman).</summary>
    private static bool IsAtomic(LayoutNode node) =>
        node.ShowEntire
        || node.Background is not null
        || node.BorderThickness > 0f
        || node.BorderBottomThickness > 0f
        || node.Height is not null
        || node.Kind is LayoutKind.Row or LayoutKind.Image or LayoutKind.Empty;

    /// <summary>Tata satu kotak utuh (tanpa penggalan) beserta latar dan bingkainya.</summary>
    private float PlaceNoSplit(LayoutNode node, float x, float yTop, float width, PageContext ctx, bool draw, float? forcedHeight = null)
    {
        float left = x + node.MarginLeft;
        float boxTop = yTop - node.MarginTop;
        float boxWidth = node.Width is { } fixedWidth
            ? fixedWidth + node.PaddingLeft + node.PaddingRight
            : Math.Max(0f, width - node.MarginLeft - node.MarginRight);
        float innerWidth = Math.Max(0f, boxWidth - node.PaddingLeft - node.PaddingRight);

        float innerHeight = MeasureContentHeight(node, innerWidth, ctx);
        float boxHeight = forcedHeight ?? innerHeight + node.PaddingTop + node.PaddingBottom;

        if (draw)
        {
            DrawBox(node, left, boxTop, boxWidth, boxHeight, ctx);
            float free = Math.Max(0f, boxHeight - node.PaddingTop - node.PaddingBottom - innerHeight);
            float offset = node.VAlign switch
            {
                VerticalAlign.Middle => free / 2f,
                VerticalAlign.Bottom => free,
                _ => 0f,
            };
            float contentTop = boxTop - node.PaddingTop - offset;
            PlaceContent(node, left + node.PaddingLeft, contentTop, innerWidth, ctx, draw: true, budget: float.MaxValue);
        }

        return node.MarginTop + boxHeight + node.MarginBottom;
    }

    /// <summary>Tinggi satu kotak utuh termasuk marginnya.</summary>
    private float MeasureNodeHeight(LayoutNode node, float width, PageContext ctx) =>
        PlaceNoSplit(node, 0f, 0f, width, ctx, draw: false);

    /// <summary>Tinggi isi kotak (tanpa padding dan margin).</summary>
    private float MeasureContentHeight(LayoutNode node, float innerWidth, PageContext ctx) =>
        PlaceContent(node, 0f, 0f, innerWidth, ctx, draw: false, budget: float.MaxValue);

    /// <summary>Tata isi kotak pada lebar tertentu; kembalikan tinggi yang terpakai.</summary>
    private float PlaceContent(LayoutNode node, float left, float yTop, float width, PageContext ctx, bool draw, float budget)
    {
        switch (node.Kind)
        {
            case LayoutKind.Text:
                return PlaceText(node, left, yTop, width, ctx, budget, 0, true, out _, draw);

            case LayoutKind.Column:
                return PlaceColumn(node, left, yTop, width, ctx, budget, 0, null, true, out _, draw);

            case LayoutKind.Row:
                return PlaceRow(node, left, yTop, width, ctx, draw);

            case LayoutKind.Table:
                return PlaceTable(node, left, yTop, width, ctx, budget, 0, true, out _, draw);

            case LayoutKind.Image:
                return PlaceImage(node, left, yTop, width, ctx, draw);

            default:
                return node.Height ?? 0f;
        }
    }

    // ------------------------------------------------------------------ teks

    private float PlaceText(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startLine,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        var block = node.Text ?? new TextBlock();
        float left = x + node.MarginLeft;
        float boxWidth = node.Width is { } fixedWidth
            ? fixedWidth + node.PaddingLeft + node.PaddingRight
            : Math.Max(0f, width - node.MarginLeft - node.MarginRight);
        float innerWidth = Math.Max(0f, boxWidth - node.PaddingLeft - node.PaddingRight);

        var texts = TextLayout.Layout(node, block, innerWidth, ctx.Settings.DefaultStyle, ctx.PageNumber, ctx.TotalPages);

        float y = yTop - node.MarginTop - node.PaddingTop;
        float dipakai = node.MarginTop + node.PaddingTop;
        int index = startLine;

        for (; index < texts.Lines.Count; index++)
        {
            var line = texts.Lines[index];
            bool terakhir = index == texts.Lines.Count - 1;
            if (dipakai + line.Height > budget + Epsilon && (index > 0 || !force))
            {
                next = new Continuation(node, index, null);
                return dipakai;
            }

            if (draw)
            {
                DrawTextLine(node, block, line, left + node.PaddingLeft, y, innerWidth, ctx, terakhir);
            }

            y -= line.Height;
            dipakai += line.Height;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    /// <summary>Gambar satu baris teks beserta garis bawahnya.</summary>
    private static void DrawTextLine(
        LayoutNode node,
        TextBlock block,
        LaidOutLine line,
        float left,
        float yTop,
        float innerWidth,
        PageContext ctx,
        bool barisTerakhir)
    {
        var align = block.Align ?? (node.HAlign switch
        {
            HorizontalAlign.Center => TextAlign.Center,
            HorizontalAlign.Right => TextAlign.Right,
            _ => TextAlign.Left,
        });

        float offset = align switch
        {
            TextAlign.Center => Math.Max(0f, (innerWidth - line.Width) / 2f),
            TextAlign.Right => Math.Max(0f, innerWidth - line.Width),
            _ => 0f,
        };

        float wordSpacing = 0f;
        if (align == TextAlign.Justify && !barisTerakhir && line.Spaces > 0 && line.Width < innerWidth)
        {
            wordSpacing = (innerWidth - line.Width) / line.Spaces;
        }

        float baseline = yTop - line.Baseline;
        float tambahanSpasi = 0f;
        foreach (var part in line.Parts)
        {
            if (part.Text.Length > 0)
            {
                float x = left + offset + part.X + tambahanSpasi;
                ctx.DrawText(part, x, baseline, 0f);

                if (part.Underline)
                {
                    float thickness = Math.Max(0.5f, part.Size * 0.06f);
                    float underlineY = baseline - part.Size * 0.12f;
                    ctx.DrawLine(x, underlineY, x + part.Width, underlineY, thickness, part.Color);
                }
            }

            if (wordSpacing > 0f && part.IsSpace) tambahanSpasi += wordSpacing;
        }
    }

    // ----------------------------------------------------------------- kolom

    private float PlaceColumn(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startIndex,
        Continuation? inner,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        float innerWidth = Math.Max(0f, width - node.MarginLeft - node.MarginRight - node.PaddingLeft - node.PaddingRight);
        float left = x + node.MarginLeft + node.PaddingLeft;
        float y = yTop - node.MarginTop - node.PaddingTop;
        float dipakai = node.MarginTop + node.PaddingTop;

        // Anak yang terpotong halaman sebelumnya dilanjutkan lebih dulu.
        bool sedangLanjutan = inner is not null;
        int index = startIndex;
        LayoutNode? child = sedangLanjutan
            ? inner!.Node
            : (index < node.Children.Count ? node.Children[index] : null);

        while (child is not null)
        {
            if (!sedangLanjutan && child.Kind == LayoutKind.PageBreak)
            {
                if (index > startIndex)
                {
                    next = new Continuation(node, index + 1, null);
                    return dipakai + node.PaddingBottom;
                }

                // Pemisah halaman di awal halaman: tidak perlu apa-apa lagi.
                index++;
                child = index < node.Children.Count ? node.Children[index] : null;
                continue;
            }

            var childState = sedangLanjutan ? inner! : new Continuation(child, 0, null);
            bool paksaAnak = !sedangLanjutan && force && index == startIndex;
            float tinggiAnak = PlaceNode(child, left, y, budget - dipakai, innerWidth, ctx, childState, out var lanjutan, paksaAnak, draw);
            dipakai += tinggiAnak;
            y -= tinggiAnak;

            if (lanjutan is not null)
            {
                next = new Continuation(node, sedangLanjutan ? index : index + 1, lanjutan);
                return dipakai;
            }

            if (!sedangLanjutan) index++;
            sedangLanjutan = false;

            if (index < node.Children.Count)
            {
                dipakai += node.Spacing;
                y -= node.Spacing;
            }

            child = index < node.Children.Count ? node.Children[index] : null;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    // ----------------------------------------------------------------- baris

    private float PlaceRow(LayoutNode node, float left, float yTop, float width, PageContext ctx, bool draw)
    {
        float innerWidth = Math.Max(0f, width - node.PaddingLeft - node.PaddingRight);
        var widths = ResolveRowWidths(node, innerWidth, ctx);

        float boxLeft = left;
        float x = boxLeft + node.PaddingLeft;
        float y = yTop - node.PaddingTop;
        float tinggi = 0f;

        for (int i = 0; i < node.Children.Count; i++)
        {
            float tinggiAnak = MeasureNodeHeight(node.Children[i], widths[i], ctx);
            if (tinggiAnak > tinggi) tinggi = tinggiAnak;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            float tinggiAnak = MeasureNodeHeight(child, widths[i], ctx);
            float atas = y;
            if (child.VAlign == VerticalAlign.Middle && tinggi > tinggiAnak) atas -= (tinggi - tinggiAnak) / 2f;
            else if (child.VAlign == VerticalAlign.Bottom && tinggi > tinggiAnak) atas -= tinggi - tinggiAnak;

            PlaceNoSplit(child, x, atas, widths[i], ctx, draw, tinggi);
            x += widths[i] + node.Spacing;
        }

        return node.PaddingTop + tinggi + node.PaddingBottom + node.MarginTop + node.MarginBottom;
    }

    private float[] ResolveRowWidths(LayoutNode node, float innerWidth, PageContext ctx)
    {
        int jumlah = node.Children.Count;
        var widths = new float[jumlah];
        if (jumlah == 0) return widths;

        float spasi = node.Spacing * (jumlah - 1);
        float tersedia = Math.Max(0f, innerWidth - spasi);
        float tetap = 0f;
        float bobot = 0f;

        for (int i = 0; i < jumlah; i++)
        {
            var child = node.Children[i];
            if (child.Width is { } lebar)
            {
                widths[i] = lebar;
                tetap += lebar;
            }
            else if (child.Weight <= 0f)
            {
                float alami = NaturalWidth(child, ctx);
                widths[i] = alami;
                tetap += alami;
            }
            else
            {
                bobot += child.Weight;
            }
        }

        float sisa = Math.Max(0f, tersedia - tetap);
        for (int i = 0; i < jumlah; i++)
        {
            var child = node.Children[i];
            if (child.Width is null && child.Weight > 0f)
            {
                widths[i] = bobot <= 0f ? 0f : sisa * child.Weight / bobot;
            }
        }

        return widths;
    }

    private float NaturalWidth(LayoutNode node, PageContext ctx)
    {
        switch (node.Kind)
        {
            case LayoutKind.Text:
            {
                var block = node.Text ?? new TextBlock();
                var texts = TextLayout.Layout(node, block, float.MaxValue / 4f, ctx.Settings.DefaultStyle, ctx.PageNumber, ctx.TotalPages);
                return texts.Width + node.PaddingLeft + node.PaddingRight;
            }

            case LayoutKind.Column:
            {
                float terlebar = 0f;
                foreach (var child in node.Children) terlebar = Math.Max(terlebar, NaturalWidth(child, ctx));
                return terlebar;
            }

            case LayoutKind.Row:
            {
                float total = 0f;
                foreach (var child in node.Children) total += NaturalWidth(child, ctx) + node.Spacing;
                return total;
            }

            case LayoutKind.Image:
            {
                var (lebar, _) = UkuranGambar(node);
                return lebar;
            }

            default:
                return node.Width ?? 0f;
        }
    }

    // ---------------------------------------------------------------- tabel

    private float PlaceTable(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startRow,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        var definisi = node.Columns ?? new List<TableColumnDefinition>();
        int jumlahKolom = Math.Max(1, definisi.Count);
        var lebar = ResolveColumnWidths(definisi, Math.Max(0f, width - node.PaddingLeft - node.PaddingRight));

        var barisKepala = BuildRows(node.HeaderCells, jumlahKolom);
        var barisIsi = BuildRows(node.Children, jumlahKolom);

        float left = x + node.PaddingLeft;
        float y = yTop - node.PaddingTop;
        float dipakai = node.PaddingTop;

        foreach (var baris in barisKepala)
        {
            float tinggi = MeasureRowHeight(baris, lebar, jumlahKolom, ctx);
            if (draw) DrawRow(baris, lebar, jumlahKolom, left, y, tinggi, ctx);
            y -= tinggi;
            dipakai += tinggi;
        }

        for (int r = startRow; r < barisIsi.Count; r++)
        {
            var baris = barisIsi[r];
            float tinggiBaris = MeasureRowHeight(baris, lebar, jumlahKolom, ctx);

            if (dipakai + tinggiBaris > budget + Epsilon && (r > startRow || !force))
            {
                next = new Continuation(node, r, null);
                return dipakai;
            }

            if (draw) DrawRow(baris, lebar, jumlahKolom, left, y, tinggiBaris, ctx);
            y -= tinggiBaris;
            dipakai += tinggiBaris;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    private static float[] ResolveColumnWidths(List<TableColumnDefinition> definisi, float innerWidth)
    {
        var lebar = new float[definisi.Count];
        float tetap = 0f;
        float bobot = 0f;
        for (int i = 0; i < definisi.Count; i++)
        {
            if (definisi[i].Constant is { } nilai)
            {
                lebar[i] = nilai;
                tetap += nilai;
            }
            else
            {
                bobot += definisi[i].Weight;
            }
        }

        float sisa = Math.Max(0f, innerWidth - tetap);
        for (int i = 0; i < definisi.Count; i++)
        {
            if (definisi[i].Constant is null)
            {
                lebar[i] = bobot <= 0f ? 0f : sisa * definisi[i].Weight / bobot;
            }
        }

        return lebar;
    }

    /// <summary>Kelompokkan sel menjadi baris menurut jumlah kolom yang digabung.</summary>
    private static List<List<(LayoutNode Cell, int Column, int Span)>> BuildRows(List<LayoutNode>? cells, int jumlahKolom)
    {
        var baris = new List<List<(LayoutNode, int, int)>>();
        if (cells is null || cells.Count == 0) return baris;

        var barisSekarang = new List<(LayoutNode, int, int)>();
        int kolom = 0;
        foreach (var cell in cells)
        {
            int span = (int)Math.Clamp(cell.ColumnSpan, 1, (uint)jumlahKolom);
            if (kolom + span > jumlahKolom && barisSekarang.Count > 0)
            {
                baris.Add(barisSekarang);
                barisSekarang = new List<(LayoutNode, int, int)>();
                kolom = 0;
            }

            barisSekarang.Add((cell, kolom, span));
            kolom += span;
            if (kolom >= jumlahKolom)
            {
                baris.Add(barisSekarang);
                barisSekarang = new List<(LayoutNode, int, int)>();
                kolom = 0;
            }
        }

        if (barisSekarang.Count > 0) baris.Add(barisSekarang);
        return baris;
    }

    private float MeasureRowHeight(List<(LayoutNode Cell, int Column, int Span)> baris, float[] lebar, int jumlahKolom, PageContext ctx)
    {
        float tinggi = 0f;
        foreach (var (cell, kolom, span) in baris)
        {
            float lebarSel = ColumnSpanWidth(lebar, kolom, span, jumlahKolom);
            float tinggiSel = MeasureNodeHeight(cell, lebarSel, ctx);
            if (cell.MinHeight is { } minimal) tinggiSel = Math.Max(tinggiSel, minimal);
            if (tinggiSel > tinggi) tinggi = tinggiSel;
        }

        return tinggi;
    }

    private void DrawRow(
        List<(LayoutNode Cell, int Column, int Span)> baris,
        float[] lebar,
        int jumlahKolom,
        float left,
        float yTop,
        float rowHeight,
        PageContext ctx)
    {
        float x = left;
        foreach (var (cell, kolom, span) in baris)
        {
            float lebarSel = ColumnSpanWidth(lebar, kolom, span, jumlahKolom);
            PlaceNoSplit(cell, x, yTop, lebarSel, ctx, draw: true, rowHeight);
            x += lebarSel;
        }
    }

    private static float ColumnSpanWidth(float[] lebar, int kolom, int span, int jumlahKolom)
    {
        float total = 0f;
        for (int i = kolom; i < kolom + span && i < jumlahKolom; i++) total += lebar[i];
        return total;
    }

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

        var gambar = BacaGambar(node);
        ukuran = (gambar.Width, gambar.Height);

        _ukuranGambar[kunci] = ukuran;
        return ukuran;
    }

    internal string? NamaGambar(LayoutNode node)
    {
        string kunci = KunciGambar(node);
        if (kunci.Length == 0) return null;
        if (_namaGambar.TryGetValue(kunci, out var nama)) return nama;

        nama = _writer.RegisterImage(BacaGambar(node));

        _namaGambar[kunci] = nama;
        return nama;
    }

    private static PdfWriterImage BacaGambar(LayoutNode node) =>
        node.ImageBytes is { Length: > 0 } bytes
            ? PdfWriterImage.FromBytes(bytes)
            : PdfWriterImage.FromFile(node.ImagePath!);

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
        string? nama = _engine.NamaGambar(node);
        if (nama is null) return;
        Page.Content.DrawImage(nama, x, top - height, width, height);
    }
}
