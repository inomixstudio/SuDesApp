using System;
using System.Collections.Generic;
using System.IO;
using SudesPdf.Core;

namespace SudesPdf.Core.Layout;

/// <summary>
/// Gagang ke satu kotak tata letak. Seluruh sifat kotak diatur lewat
/// metode perluasan pada <see cref="LayoutContainerExtensions"/>, sehingga
/// susunan dokumen ditulis mengalir seperti sebelumnya.
/// </summary>
public interface IContainer
{
}

/// <summary>Gagang yang menyimpan simpul tata letaknya.</summary>
internal interface INodeOwner
{
    LayoutNode Node { get; }
}

internal sealed class ContainerHandle : IContainer, INodeOwner
{
    internal ContainerHandle(LayoutNode node) => Node = node;

    public LayoutNode Node { get; }
}

/// <summary>Kumpulan sifat kotak yang dapat dirantai.</summary>
public static class LayoutContainerExtensions
{
    internal static LayoutNode NodeOf(this IContainer container) =>
        container is INodeOwner owner
            ? owner.Node
            : throw new PdfException("Gagang tata letak tidak dikenali.");

    // --------------------------------------------------------------- ukuran

    public static IContainer Padding(this IContainer container, float all)
    {
        var node = container.NodeOf();
        node.PaddingTop = node.PaddingRight = node.PaddingBottom = node.PaddingLeft = all;
        return container;
    }

    public static IContainer Padding(this IContainer container, float vertical, float horizontal)
    {
        var node = container.NodeOf();
        node.PaddingTop = node.PaddingBottom = vertical;
        node.PaddingLeft = node.PaddingRight = horizontal;
        return container;
    }

    public static IContainer PaddingTop(this IContainer container, float value) => Set(container, node => node.PaddingTop = value);

    public static IContainer PaddingBottom(this IContainer container, float value) => Set(container, node => node.PaddingBottom = value);

    public static IContainer PaddingLeft(this IContainer container, float value) => Set(container, node => node.PaddingLeft = value);

    public static IContainer PaddingRight(this IContainer container, float value) => Set(container, node => node.PaddingRight = value);

    public static IContainer PaddingVertical(this IContainer container, float value) => Set(container, node =>
    {
        node.PaddingTop = value;
        node.PaddingBottom = value;
    });

    public static IContainer PaddingHorizontal(this IContainer container, float value) => Set(container, node =>
    {
        node.PaddingLeft = value;
        node.PaddingRight = value;
    });

    public static IContainer PaddingTop(this IContainer container, float value, Unit unit) => container.PaddingTop(UnitConversion.ToPoints(value, unit));

    public static IContainer PaddingBottom(this IContainer container, float value, Unit unit) => container.PaddingBottom(UnitConversion.ToPoints(value, unit));

    public static IContainer PaddingLeft(this IContainer container, float value, Unit unit) => container.PaddingLeft(UnitConversion.ToPoints(value, unit));

    public static IContainer PaddingRight(this IContainer container, float value, Unit unit) => container.PaddingRight(UnitConversion.ToPoints(value, unit));

    public static IContainer Margin(this IContainer container, float all, Unit unit) =>
        container.Margin(UnitConversion.ToPoints(all, unit));

    public static IContainer Margin(this IContainer container, float all) => Set(container, node =>
    {
        node.MarginTop = node.MarginRight = node.MarginBottom = node.MarginLeft = all;
    });

    public static IContainer Margin(this IContainer container, float vertical, float horizontal) => Set(container, node =>
    {
        node.MarginTop = node.MarginBottom = vertical;
        node.MarginLeft = node.MarginRight = horizontal;
    });

    public static IContainer MarginTop(this IContainer container, float value) => Set(container, node => node.MarginTop = value);

    public static IContainer MarginBottom(this IContainer container, float value) => Set(container, node => node.MarginBottom = value);

    public static IContainer MarginLeft(this IContainer container, float value) => Set(container, node => node.MarginLeft = value);

    public static IContainer MarginRight(this IContainer container, float value) => Set(container, node => node.MarginRight = value);

    public static IContainer Height(this IContainer container, float value) => Set(container, node => node.Height = value);

    public static IContainer Height(this IContainer container, float value, Unit unit) => container.Height(UnitConversion.ToPoints(value, unit));

    public static IContainer Width(this IContainer container, float value) => Set(container, node => node.Width = value);

    public static IContainer Width(this IContainer container, float value, Unit unit) => container.Width(UnitConversion.ToPoints(value, unit));

    public static IContainer MinHeight(this IContainer container, float value) => Set(container, node => node.MinHeight = value);

    public static IContainer MinHeight(this IContainer container, float value, Unit unit) => container.MinHeight(UnitConversion.ToPoints(value, unit));

    /// <summary>Tinggi ruang kosong (tanpa isi) — dipakai blok tanda tangan.</summary>
    public static IContainer HeightSpacer(this IContainer container, float value) => Set(container, node =>
    {
        node.Height = value;
        node.Kind = LayoutKind.Empty;
    });

    // -------------------------------------------------------------- dekorasi

    public static IContainer Background(this IContainer container, string color) =>
        Set(container, node => node.Background = PdfRgb.FromHex(color));

    public static IContainer Background(this IContainer container, PdfRgb color) =>
        Set(container, node => node.Background = color);

    public static IContainer Border(this IContainer container, float thickness) =>
        Set(container, node => node.BorderThickness = thickness);

    public static IContainer BorderColor(this IContainer container, string color) =>
        Set(container, node => node.BorderColor = PdfRgb.FromHex(color));

    public static IContainer BorderColor(this IContainer container, PdfRgb color) =>
        Set(container, node => node.BorderColor = color);

    public static IContainer BorderBottom(this IContainer container, float thickness) =>
        Set(container, node => node.BorderBottomThickness = thickness);

    public static IContainer AlignLeft(this IContainer container) =>
        Set(container, node => node.HAlign = HorizontalAlign.Left);

    public static IContainer AlignCenter(this IContainer container) =>
        Set(container, node => node.HAlign = HorizontalAlign.Center);

    public static IContainer AlignRight(this IContainer container) =>
        Set(container, node => node.HAlign = HorizontalAlign.Right);

    public static IContainer AlignMiddle(this IContainer container) =>
        Set(container, node => node.VAlign = VerticalAlign.Middle);

    public static IContainer AlignBottom(this IContainer container) =>
        Set(container, node => node.VAlign = VerticalAlign.Bottom);

    /// <summary>Kotak ini tidak boleh terpotong oleh batas halaman.</summary>
    public static IContainer ShowEntire(this IContainer container) =>
        Set(container, node => node.ShowEntire = true);

    // ------------------------------------------------------------------ isi

    /// <summary>Susun anak-anak bertumpuk ke bawah.</summary>
    public static void Column(this IContainer container, Action<ColumnDescriptor> handler)
    {
        var node = container.NodeOf();
        node.Kind = node.Kind == LayoutKind.Empty ? LayoutKind.Column : node.Kind;
        handler(new ColumnDescriptor(node));
    }

    /// <summary>Susun anak-anak berdampingan.</summary>
    public static void Row(this IContainer container, Action<RowDescriptor> handler)
    {
        var node = container.NodeOf();
        node.Kind = node.Kind == LayoutKind.Empty ? LayoutKind.Row : node.Kind;
        handler(new RowDescriptor(node));
    }

    /// <summary>Tabel berkolom.</summary>
    public static void Table(this IContainer container, Action<TableDescriptor> handler)
    {
        var node = container.NodeOf();
        node.Kind = LayoutKind.Table;
        node.Columns = new List<TableColumnDefinition>();
        handler(new TableDescriptor(node));
    }

    /// <summary>Potongan bebas: dipakai helper yang menerima gagang tata letak.</summary>
    public static IContainer Element(this IContainer container, Action<IContainer> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler(container);
        return container;
    }

    /// <summary>Potongan bebas yang mengembalikan kotaknya (gaya bersama, mis. “SelBerbingkai”).</summary>
    public static IContainer Element(this IContainer container, Func<IContainer, IContainer> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler(container) ?? container;
    }

    /// <summary>Gaya teks bawaan untuk isi kotak ini.</summary>
    public static IContainer DefaultTextStyle(this IContainer container, Action<TextStyleDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var node = container.NodeOf();
        var style = node.DefaultText ?? new TextStyleDescriptor();
        handler(style);
        node.DefaultText = style;
        return container;
    }

    /// <summary>Satu baris teks (tanpa gaya khusus).</summary>
    public static TextSpanDescriptor Text(this IContainer container, string? text)
    {
        var block = EnsureTextBlock(container.NodeOf());
        var line = new TextLine();
        block.Lines.Add(line);
        return AddSpan(line, block, text);
    }

    /// <summary>Blok teks berisi beberapa potongan dengan gaya berbeda.</summary>
    public static void Text(this IContainer container, Action<TextDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var node = container.NodeOf();
        var block = EnsureTextBlock(node);
        handler(new TextDescriptor(node, block));
    }

    /// <summary>Gambar dari berkas (PNG/JPEG); ukurannya diatur <see cref="ImageDescriptor"/>.</summary>
    public static ImageDescriptor Image(this IContainer container, string path)
    {
        var node = container.NodeOf();
        node.Kind = LayoutKind.Image;
        node.ImagePath = path;
        return new ImageDescriptor(node);
    }

    /// <summary>Gambar dari byte (mis. lambang yang ditanam di assembly).</summary>
    public static ImageDescriptor Image(this IContainer container, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var node = container.NodeOf();
        node.Kind = LayoutKind.Image;
        node.ImageBytes = data;
        return new ImageDescriptor(node);
    }

    /// <summary>Paksa halaman baru sebelum isi berikutnya.</summary>
    public static void PageBreak(this IContainer container) => container.NodeOf().Kind = LayoutKind.PageBreak;

    /// <summary>Garis mendatar setebal <paramref name="thickness"/> titik.</summary>
    public static void LineHorizontal(this IContainer container, float thickness)
    {
        var node = container.NodeOf();
        node.Kind = LayoutKind.Empty;
        node.Height = thickness;
        node.Background = PdfRgb.Black;
    }

    internal static TextBlock EnsureTextBlock(LayoutNode node)
    {
        if (node.Kind == LayoutKind.Text && node.Text is not null) return node.Text;
        node.Kind = LayoutKind.Text;
        node.Text = new TextBlock();
        return node.Text;
    }

    internal static TextSpanDescriptor AddSpan(TextLine line, TextBlock block, string? text)
    {
        var span = new TextSpan { Text = text ?? string.Empty };
        line.Spans.Add(span);
        return new TextSpanDescriptor(block, span);
    }

    private static IContainer Set(IContainer container, Action<LayoutNode> change)
    {
        change(container.NodeOf());
        return container;
    }
}

/// <summary>Nyawa isi sebuah kolom: anak-anak dan jarak antar anak.</summary>
public sealed class ColumnDescriptor
{
    private readonly LayoutNode _node;

    internal ColumnDescriptor(LayoutNode node) => _node = node;

    /// <summary>Satu anak baru di dalam kolom.</summary>
    public IContainer Item() => AddChild(_node);

    /// <summary>Jarak antar anak (titik).</summary>
    public void Spacing(float value) => _node.Spacing = value;

    internal static IContainer AddChild(LayoutNode parent)
    {
        var child = new LayoutNode { Parent = parent };
        parent.Children.Add(child);
        return new ContainerHandle(child);
    }
}

/// <summary>Isi sebuah baris: anak tetap, anak relatif, dan jaraknya.</summary>
public sealed class RowDescriptor
{
    private readonly LayoutNode _node;

    internal RowDescriptor(LayoutNode node) => _node = node;

    /// <summary>Anak yang lebarnya mengikuti sisa ruang dengan bobot bawaan 1.</summary>
    public IContainer Item() => RelativeItem(1f);

    /// <summary>Anak selebar tetap <paramref name="width"/> titik.</summary>
    public IContainer ConstantItem(float width)
    {
        var child = new LayoutNode { Width = width, Parent = _node };
        _node.Children.Add(child);
        return new ContainerHandle(child);
    }

    /// <summary>Anak yang lebarnya mengikuti bobot terhadap sisa ruang.</summary>
    public IContainer RelativeItem(float weight = 1f)
    {
        var child = new LayoutNode { Weight = weight <= 0 ? 1f : weight, Parent = _node };
        _node.Children.Add(child);
        return new ContainerHandle(child);
    }

    /// <summary>Anak yang lebarnya mengikuti isinya.</summary>
    public IContainer AutoItem()
    {
        var child = new LayoutNode { Weight = 0f, Parent = _node };
        _node.Children.Add(child);
        return new ContainerHandle(child);
    }

    /// <summary>Jarak antar anak (titik).</summary>
    public void Spacing(float value) => _node.Spacing = value;
}

/// <summary>Definisi kolom tabel.</summary>
public sealed class TableColumnsDefinitionDescriptor
{
    private readonly List<TableColumnDefinition> _columns;

    internal TableColumnsDefinitionDescriptor(List<TableColumnDefinition> columns) => _columns = columns;

    /// <summary>Kolom dengan bobot lebar relatif.</summary>
    public void RelativeColumn(float weight = 1f) => _columns.Add(TableColumnDefinition.Relative(weight <= 0 ? 1f : weight));

    /// <summary>Kolom selebar tetap (titik).</summary>
    public void ConstantColumn(float width) => _columns.Add(TableColumnDefinition.ConstantWidth(width));
}

/// <summary>Susunan tabel: definisi kolom, sel, dan baris kepala.</summary>
public sealed class TableDescriptor
{
    private readonly LayoutNode _node;

    internal TableDescriptor(LayoutNode node) => _node = node;

    /// <summary>Tentukan lebar setiap kolom.</summary>
    public void ColumnsDefinition(Action<TableColumnsDefinitionDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _node.Columns ??= new List<TableColumnDefinition>();
        handler(new TableColumnsDefinitionDescriptor(_node.Columns));
    }

    /// <summary>Satu sel pada baris berikutnya.</summary>
    public TableCellDescriptor Cell()
    {
        var cell = new LayoutNode { IsTableCell = true, Parent = _node };
        _node.Children.Add(cell);
        return new TableCellDescriptor(cell);
    }

    /// <summary>Baris kepala: dicetak di atas tabel dan diulang pada halaman lanjutan.</summary>
    public void Header(Action<TableDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _node.HeaderCells ??= new List<LayoutNode>();
        handler(new TableDescriptor(new LayoutNode { IsTableCell = false }));
    }
}

/// <summary>Satu sel tabel.</summary>
public sealed class TableCellDescriptor : IContainer, INodeOwner
{
    internal TableCellDescriptor(LayoutNode node) => Node = node;

    public LayoutNode Node { get; }

    /// <summary>Sel menggabungkan beberapa kolom sekaligus.</summary>
    public TableCellDescriptor ColumnSpan(uint span)
    {
        Node.ColumnSpan = span == 0 ? 1 : span;
        return this;
    }
}

/// <summary>Pengaturan teks satu blok.</summary>
public sealed class TextDescriptor
{
    private readonly LayoutNode _node;
    private readonly TextBlock _block;
    private TextLine _line = new();

    internal TextDescriptor(LayoutNode node, TextBlock block)
    {
        _node = node;
        _block = block;
        _block.Lines.Add(_line);
    }

    /// <summary>Tambahkan potongan teks pada baris berjalan.</summary>
    public TextSpanDescriptor Span(string? text) => LayoutContainerExtensions.AddSpan(_line, _block, text);

    /// <summary>Mulai baris baru lalu tambahkan potongan teks.</summary>
    public TextSpanDescriptor Line(string? text)
    {
        _line = new TextLine();
        _block.Lines.Add(_line);
        return LayoutContainerExtensions.AddSpan(_line, _block, text);
    }

    public TextDescriptor AlignLeft()
    {
        _block.Align = TextAlign.Left;
        return this;
    }

    public TextDescriptor AlignCenter()
    {
        _block.Align = TextAlign.Center;
        return this;
    }

    public TextDescriptor AlignRight()
    {
        _block.Align = TextAlign.Right;
        return this;
    }

    public TextDescriptor Justify()
    {
        _block.Align = TextAlign.Justify;
        return this;
    }

    /// <summary>Indentasi baris pertama paragraf (titik).</summary>
    public TextDescriptor ParagraphFirstLineIndentation(float value)
    {
        _block.FirstLineIndent = value;
        return this;
    }

    /// <summary>Gaya bawaan untuk potongan teks berikutnya pada blok ini.</summary>
    public TextDescriptor DefaultTextStyle(Action<TextStyleDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var style = _node.DefaultText ?? new TextStyleDescriptor();
        handler(style);
        _node.DefaultText = style;
        _node.Kind = LayoutKind.Text;
        return this;
    }

    /// <summary>Nomor halaman yang sedang dicetak.</summary>
    public TextDescriptor CurrentPageNumber()
    {
        var span = LayoutContainerExtensions.AddSpan(_line, _block, string.Empty);
        span.Number = PageNumberKind.Current;
        _node.Kind = LayoutKind.Text;
        return this;
    }

    /// <summary>Jumlah seluruh halaman dokumen.</summary>
    public TextDescriptor TotalPages()
    {
        var span = LayoutContainerExtensions.AddSpan(_line, _block, string.Empty);
        span.Number = PageNumberKind.Total;
        _node.Kind = LayoutKind.Text;
        return this;
    }
}

/// <summary>Pengaturan satu potongan teks.</summary>
public sealed class TextSpanDescriptor
{
    private readonly TextBlock _block;
    private readonly TextSpan _span;

    internal TextSpanDescriptor(TextBlock block, TextSpan span)
    {
        _block = block;
        _span = span;
    }

    public TextSpanDescriptor FontSize(float size)
    {
        _span.Size = size;
        return this;
    }

    public TextSpanDescriptor FontFamily(string? family) => this;

    public TextSpanDescriptor Bold()
    {
        _span.Bold = true;
        return this;
    }

    /// <summary>Tebal (Times tidak punya bobot setengah, jadi sama dengan tebal).</summary>
    public TextSpanDescriptor SemiBold() => Bold();

    public TextSpanDescriptor Italic()
    {
        _span.Italic = true;
        return this;
    }

    public TextSpanDescriptor Underline()
    {
        _span.Underline = true;
        return this;
    }

    public TextSpanDescriptor FontColor(string? color)
    {
        _span.Color = string.IsNullOrWhiteSpace(color) ? (PdfRgb?)null : PdfRgb.FromHex(color);
        return this;
    }

    public TextSpanDescriptor FontColor(PdfRgb color)
    {
        _span.Color = color;
        return this;
    }

    /// <summary>Pengali tinggi baris (1,25 = 125% ukuran huruf).</summary>
    public TextSpanDescriptor LineHeight(float factor)
    {
        _span.LineHeight = factor;
        return this;
    }

    public TextSpanDescriptor AlignLeft()
    {
        _block.Align = TextAlign.Left;
        return this;
    }

    public TextSpanDescriptor AlignCenter()
    {
        _block.Align = TextAlign.Center;
        return this;
    }

    public TextSpanDescriptor AlignRight()
    {
        _block.Align = TextAlign.Right;
        return this;
    }

    public TextSpanDescriptor Justify()
    {
        _block.Align = TextAlign.Justify;
        return this;
    }

    internal PageNumberKind Number
    {
        get => _span.Number;
        set => _span.Number = value;
    }
}

/// <summary>Gaya teks bawaan halaman atau kotak; hanya nilai yang diisi yang menimpa bawaan.</summary>
public sealed class TextStyleDescriptor
{
    internal float? FontSizeValue { get; private set; }

    internal float? LineHeightValue { get; private set; }

    internal PdfRgb? Color { get; private set; }

    internal bool? BoldValue { get; private set; }

    internal bool? ItalicValue { get; private set; }

    public TextStyleDescriptor FontFamily(string? family) => this;

    public TextStyleDescriptor FontSize(float size)
    {
        FontSizeValue = size;
        return this;
    }

    public TextStyleDescriptor LineHeight(float factor)
    {
        LineHeightValue = factor;
        return this;
    }

    public TextStyleDescriptor FontColor(string? color)
    {
        Color = string.IsNullOrWhiteSpace(color) ? (PdfRgb?)null : PdfRgb.FromHex(color);
        return this;
    }

    public TextStyleDescriptor FontColor(PdfRgb color)
    {
        Color = color;
        return this;
    }

    public TextStyleDescriptor Bold()
    {
        BoldValue = true;
        return this;
    }

    /// <summary>Tebal (Times tidak punya bobot setengah, jadi sama dengan tebal).</summary>
    public TextStyleDescriptor SemiBold() => Bold();

    public TextStyleDescriptor Italic()
    {
        ItalicValue = true;
        return this;
    }
}

/// <summary>Gambar dan cara menyesuaikannya dengan kotak.</summary>
public sealed class ImageDescriptor
{
    private readonly LayoutNode _node;

    internal ImageDescriptor(LayoutNode node) => _node = node;

    /// <summary>Skalakan gambar agar pas di dalam kotaknya (rasio tetap).</summary>
    public void FitArea() => _node.Weight = 1f;

    /// <summary>Skalakan mengikuti lebar kotak.</summary>
    public void FitWidth() => _node.Weight = 0.5f;

    /// <summary>Skalakan mengikuti tinggi kotak.</summary>
    public void FitHeight() => _node.Weight = 0.25f;
}

/// <summary>Halaman yang disusun: ukuran, margin, gaya teks, kepala, dan kaki.</summary>
public sealed class PageDescriptor
{
    internal PageDescriptor() => ContentNode = new LayoutNode { Kind = LayoutKind.Column };

    internal PageSize SheetSize { get; private set; } = PageSizes.A4;

    internal float TopMargin { get; private set; } = 36f;

    internal float RightMargin { get; private set; } = 45f;

    internal float BottomMargin { get; private set; } = 30f;

    internal float LeftMargin { get; private set; } = 45f;

    internal PdfRgb BackColor { get; private set; } = PdfRgb.White;

    internal TextStyleDescriptor DefaultStyle { get; } = new();

    internal LayoutNode ContentNode { get; }

    internal LayoutNode? HeaderNode { get; private set; }

    internal LayoutNode? FooterNode { get; private set; }

    public void Size(PageSize size) => SheetSize = size;

    public void Margin(float all) => TopMargin = RightMargin = BottomMargin = LeftMargin = all;

    public void Margin(float all, Unit unit) => Margin(UnitConversion.ToPoints(all, unit));

    public void MarginTop(float value, Unit unit = Unit.Point) => TopMargin = UnitConversion.ToPoints(value, unit);

    public void MarginBottom(float value, Unit unit = Unit.Point) => BottomMargin = UnitConversion.ToPoints(value, unit);

    public void MarginLeft(float value, Unit unit = Unit.Point) => LeftMargin = UnitConversion.ToPoints(value, unit);

    public void MarginRight(float value, Unit unit = Unit.Point) => RightMargin = UnitConversion.ToPoints(value, unit);

    public void PageColor(string color) => BackColor = PdfRgb.FromHex(color);

    public void DefaultTextStyle(Action<TextStyleDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler(DefaultStyle);
    }

    /// <summary>Isi halaman yang mengalir dari atas ke bawah.</summary>
    public IContainer Content() => new ContainerHandle(ContentNode);

    /// <summary>Kepala halaman: dicetak di atas isi pada setiap halaman.</summary>
    public IContainer Header() => new ContainerHandle(HeaderNode ??= new LayoutNode { Kind = LayoutKind.Column });

    /// <summary>Kaki halaman: dicetak di bawah isi pada setiap halaman.</summary>
    public IContainer Footer() => new ContainerHandle(FooterNode ??= new LayoutNode { Kind = LayoutKind.Column });
}

/// <summary>Keterangan dokumen yang dicatat pada berkas PDF.</summary>
public sealed class DocumentMetadata
{
    /// <summary>Keterangan bawaan (tanpa judul dan penulis).</summary>
    public static DocumentMetadata Default { get; } = new();

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string? Creator { get; set; }
}

/// <summary>Dokumen yang dapat menghasilkan berkas PDF.</summary>
public interface IDocument
{
    /// <summary>Keterangan dokumen (judul, penulis, subjek).</summary>
    DocumentMetadata GetMetadata();

    /// <summary>Isi halaman-halaman dokumen.</summary>
    void Compose(IDocumentContainer container);
}

/// <summary>Tempat menambahkan halaman ke dokumen.</summary>
public interface IDocumentContainer
{
    void Page(Action<PageDescriptor> handler);
}

/// <summary>
/// Titik masuk penulisan dokumen. Seluruh tata letak dan penulisan berkas
/// dikerjakan <c>SudesPdf.Core</c> — tanpa pustaka PDF pihak ketiga.
/// </summary>
public static class Document
{
    /// <summary>Buat dokumen baru dari sebuah penyusun halaman.</summary>
    public static IDocument Create(Action<IDocumentContainer> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new ComposedDocument(handler);
    }
}

/// <summary>Dokumen yang isinya disusun sebuah penyusun halaman.</summary>
internal sealed class ComposedDocument : IDocument
{
    private readonly Action<IDocumentContainer> _handler;

    internal ComposedDocument(Action<IDocumentContainer> handler) => _handler = handler;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _handler(container);
    }
}

/// <summary>Penampung halaman yang sedang disusun.</summary>
internal sealed class PageCollector : IDocumentContainer
{
    internal List<PageDescriptor> Pages { get; } = new();

    public void Page(Action<PageDescriptor> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var page = new PageDescriptor();
        handler(page);
        Pages.Add(page);
    }
}

/// <summary>Menghasilkan berkas PDF dari dokumen.</summary>
public static class DocumentExtensions
{
    /// <summary>Tulis dokumen ke sebuah aliran (stream).</summary>
    public static void GeneratePdf(this IDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        var collector = new PageCollector();
        document.Compose(collector);
        if (collector.Pages.Count == 0) throw new PdfException("Dokumen harus memiliki paling sedikit satu halaman.");

        byte[] bytes = LayoutFlow.Build(collector.Pages, document.GetMetadata());
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Tulis dokumen ke sebuah berkas.</summary>
    public static void GeneratePdf(this IDocument document, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        document.GeneratePdf(stream);
    }
}
