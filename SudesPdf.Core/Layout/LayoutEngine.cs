using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

/// <summary>Hasil tata letak halaman-halaman dokumen menjadi satu berkas PDF.</summary>
internal static class LayoutFlow
{
    internal static byte[] Build(List<PageDescriptor> pages, out int pageCount, DocumentMetadata? metadata = null)
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

        pageCount = writer.PageCount;
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
    internal Continuation(LayoutNode node, int index, Continuation? inner, bool hentikanHalaman = false)
    {
        Node = node;
        Index = index;
        Inner = inner;
        HentikanHalaman = hentikanHalaman;
    }

    internal LayoutNode Node { get; }

    /// <summary>Indeks anak (kolom), baris (tabel), atau baris teks berikutnya.</summary>
    internal int Index { get; }

    /// <summary>Lanjutan di dalam anak yang sedang dikerjakan.</summary>
    internal Continuation? Inner { get; }

    /// <summary>
    /// Sisa isi ini harus dimulai di lembar baru karena naskah meminta pemisah
    /// halaman (<c>PageBreak()</c>) — bukan karena ruang halaman habis.
    /// </summary>
    internal bool HentikanHalaman { get; }
}

/// <summary>
/// Mesin tata letak: mengalirkan isi halaman ke kertas, memenggal pada batas
/// baris/baris tabel, mengulang kepala tabel, dan menggambar hasilnya lewat
/// <see cref="PdfWriter"/>.
/// </summary>
internal sealed partial class LayoutEngine
{
    private const float Epsilon = 0.01f;

    private readonly PdfWriter _writer;
    private readonly int _totalPages;
    private readonly Dictionary<string, (int Width, int Height)> _ukuranGambar = new();
    private readonly Dictionary<string, string?> _namaGambar = new();
    private readonly Dictionary<string, PdfWriterImage> _gambar = new();

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

            // Sisa ruang halaman sudah habis padahal isi belum selesai, atau
            // naskah meminta pemisah halaman: lanjutkan di lembar baru, jangan
            // menaruh sisanya di sisa ruang halaman ini.
            if (next is not null && (next.HentikanHalaman || y <= ctx.ContentBottom + Epsilon)) return next;

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

        // PlaceContent memakai kontrak KOTAK LUAR: padding dan margin simpul ini
        // dikurangi sendiri oleh fungsi isi (PlaceText, PlaceColumn, PlaceTable).
        // Ukuran yang diberikan harus kotak yang sama dengan yang dipakai saat
        // menggambar; dulu di sini dipakai lebar DALAM sehingga isi sel diukur
        // lebih sempit daripada saat dicetak.
        float innerHeight = MeasureContentHeight(node, boxWidth, ctx);
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

            // Isi ditata pada koordinat kotak luar, bukan koordinat dalam: dulu
            // padding dikurangkan di sini LALU dikurangkan lagi di PlaceText,
            // sehingga teks sel bergeser selebar dua kali padding dan terbungkus
            // jauh sebelum tepi sel — sel jadi sempit, baris jadi tinggi, dan
            // judul kolom terpecah di tengah kata.
            PlaceContent(node, x, yTop - offset, width, ctx, draw: true, budget: float.MaxValue);
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

}
