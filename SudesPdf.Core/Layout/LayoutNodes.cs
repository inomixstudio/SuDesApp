using System;
using System.Collections.Generic;

namespace SudesPdf.Core.Layout;

/// <summary>Satuan ukuran yang dipakai penyusun halaman.</summary>
public enum Unit
{
    Point,
    Millimetre,
    Centimetre,
    Inch,
}

/// <summary>Ukuran satu halaman (titik), dengan varian lanskap.</summary>
public readonly struct PageSize
{
    public PageSize(float width, float height, Unit unit = Unit.Point)
    {
        Width = UnitConversion.ToPoints(width, unit);
        Height = UnitConversion.ToPoints(height, unit);
    }

    private PageSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Lebar halaman dalam titik.</summary>
    public float Width { get; }

    /// <summary>Tinggi halaman dalam titik.</summary>
    public float Height { get; }

    /// <summary>Ukuran langsung dalam titik (tanpa konversi satuan).</summary>
    public static PageSize FromPoints(float width, float height) => new(width, height);

    /// <summary>Salinan dengan lebar dan tinggi bertukar (posisi mendatar).</summary>
    public PageSize Landscape() => new(Height, Width);

    /// <summary>Salinan dengan lebar dan tinggi bertukar (posisi tegak).</summary>
    public PageSize Portrait() => new(Math.Min(Width, Height), Math.Max(Width, Height));
}

/// <summary>Ukuran halaman yang lazim dipakai surat desa.</summary>
public static class PageSizes
{
    /// <summary>A4 (210 × 297 mm) — ukuran cetak bawaan aplikasi.</summary>
    public static PageSize A4 => PageSize.FromPoints(595.276f, 841.89f);

    /// <summary>F4/Folio (215 × 330 mm) — kertas yang lazim di kantor desa.</summary>
    public static PageSize F4 => PageSize.FromPoints(609.45f, 935.43f);
}

/// <summary>Konversi satuan menjadi titik (1/72 inci).</summary>
public static class UnitConversion
{
    public static float ToPoints(float value, Unit unit) => unit switch
    {
        Unit.Millimetre => value * 72f / 25.4f,
        Unit.Centimetre => value * 72f / 2.54f,
        Unit.Inch => value * 72f,
        _ => value,
    };
}

/// <summary>Palet warna bersama penulisan dokumen (nilai heksadesimal).</summary>
public static class Colors
{
    public const string Black = "#000000";
    public const string White = "#FFFFFF";

    public static class Grey
    {
        public const string Lighten5 = "#FAFAFA";
        public const string Lighten4 = "#F5F5F5";
        public const string Lighten3 = "#EEEEEE";
        public const string Lighten2 = "#E0E0E0";
        public const string Lighten1 = "#BDBDBD";
        public const string Medium = "#9E9E9E";
        public const string Darken1 = "#757575";
        public const string Darken2 = "#616161";
        public const string Darken3 = "#424242";
        public const string Darken4 = "#212121";
    }

    public static class Red
    {
        public const string Lighten4 = "#FFEBEE";
        public const string Lighten2 = "#EF9A9A";
        public const string Medium = "#F44336";
        public const string Darken2 = "#D32F2F";
    }

    public static class Teal
    {
        public const string Lighten4 = "#E0F2F1";
        public const string Lighten3 = "#B2DFDB";
        public const string Lighten2 = "#80CBC4";
        public const string Lighten1 = "#4DB6AC";
        public const string Medium = "#009688";
        public const string Darken1 = "#00897B";
        public const string Darken2 = "#00796B";
        public const string Darken3 = "#00695C";
    }
}

/// <summary>Perataan baris teks di dalam kotaknya.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>Perataan kotak terhadap ruang yang tersedia.</summary>
public enum HorizontalAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Perataan tegak kotak terhadap ruang yang tersedia.</summary>
public enum VerticalAlign
{
    Top,
    Middle,
    Bottom,
}

/// <summary>Nomor halaman otomatis di dalam teks.</summary>
public enum PageNumberKind
{
    None,
    Current,
    Total,
}

/// <summary>Jenis isi sebuah simpul tata letak.</summary>
public enum LayoutKind
{
    Empty,
    Column,
    Row,
    Table,
    Text,
    Image,
    PageBreak,
}

/// <summary>
/// Satu potongan teks dengan gaya tersendiri. Gaya yang tidak disebutkan
/// (null) diisi dari gaya bawaan blok, kotaknya, atau halamannya saat ditata.
/// </summary>
public sealed class TextSpan
{
    public string Text { get; set; } = string.Empty;

    /// <summary>Ukuran huruf (titik); null berarti mengikuti gaya bawaan.</summary>
    public float? Size { get; set; }

    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public bool Underline { get; set; }

    /// <summary>Warna teks; null berarti hitam.</summary>
    public PdfRgb? Color { get; set; }

    /// <summary>Pengali tinggi baris (mis. 1,25 = 125% ukuran huruf).</summary>
    public float? LineHeight { get; set; }

    /// <summary>Isi otomatis: nomor halaman berjalan atau jumlah halaman.</summary>
    public PageNumberKind Number { get; set; }
}

/// <summary>Satu baris teks yang ditulis generator (sebelum dipenggal).</summary>
public sealed class TextLine
{
    public List<TextSpan> Spans { get; } = new();
}

/// <summary>Blok teks: beberapa baris dengan perataan dan indentasi bersama.</summary>
public sealed class TextBlock
{
    public List<TextLine> Lines { get; } = new();

    /// <summary>Perataan eksplisit; null berarti mengikuti perataan kotaknya.</summary>
    public TextAlign? Align { get; set; }

    /// <summary>Indentasi baris pertama paragraf (titik).</summary>
    public float FirstLineIndent { get; set; }

    internal bool IsEmpty => Lines.Count == 0;
}

/// <summary>Bawaan teks surat.</summary>
public static class TextDefaults
{
    /// <summary>Ukuran huruf bawaan (point) — sama dengan surat desa.</summary>
    public const float FontSize = 12f;

    /// <summary>Pengali tinggi baris bawaan.</summary>
    public const float LineHeight = 1.2f;
}

/// <summary>Definisi satu kolom tabel: lebar tetap atau bobot relatif.</summary>
public sealed class TableColumnDefinition
{
    private TableColumnDefinition(float? constant, float weight)
    {
        Constant = constant;
        Weight = weight;
    }

    /// <summary>Lebar tetap (titik), bila kolomnya bukan kolom relatif.</summary>
    public float? Constant { get; }

    /// <summary>Bobot pembagian sisa lebar.</summary>
    public float Weight { get; }

    public static TableColumnDefinition ConstantWidth(float width) => new(width, 0f);

    public static TableColumnDefinition Relative(float weight) => new(null, weight);
}

/// <summary>
/// Satu kotak pada pohon tata letak. Sifat geometrinya (padding, lebar, latar,
/// bingkai) dimiliki simpul ini; isinya salah satu dari anak, teks, gambar,
/// tabel, atau pemisah halaman.
/// </summary>
public sealed class LayoutNode
{
    public LayoutKind Kind { get; set; } = LayoutKind.Empty;

    public List<LayoutNode> Children { get; } = new();

    public TextBlock? Text { get; set; }

    public string? ImagePath { get; set; }

    /// <summary>Isi gambar bila gambarnya datang dari byte, bukan dari berkas.</summary>
    public byte[]? ImageBytes { get; set; }

    public List<TableColumnDefinition>? Columns { get; set; }

    /// <summary>Baris kepala tabel (dicetak ulang di setiap halaman lanjutan).</summary>
    public List<LayoutNode>? HeaderCells { get; set; }

    /// <summary>Benar bila simpul ini adalah sel tabel.</summary>
    public bool IsTableCell { get; set; }

    /// <summary>Jumlah kolom yang digabung oleh sel (1 = satu kolom).</summary>
    public uint ColumnSpan { get; set; } = 1;

    /// <summary>Bobot lebar pada baris (RelativeItem).</summary>
    public float Weight { get; set; } = 1f;

    /// <summary>Jarak antar anak pada kolom/baris/tabel (titik).</summary>
    public float Spacing { get; set; }

    public float PaddingTop { get; set; }

    public float PaddingRight { get; set; }

    public float PaddingBottom { get; set; }

    public float PaddingLeft { get; set; }

    public float MarginTop { get; set; }

    public float MarginRight { get; set; }

    public float MarginBottom { get; set; }

    public float MarginLeft { get; set; }

    /// <summary>Lebar isi tetap; null berarti mengikuti ruang yang tersedia.</summary>
    public float? Width { get; set; }

    /// <summary>Tinggi isi tetap; null berarti mengikuti isinya.</summary>
    public float? Height { get; set; }

    /// <summary>Tinggi isi paling sedikit (titik).</summary>
    public float? MinHeight { get; set; }

    public PdfRgb? Background { get; set; }

    public float BorderThickness { get; set; }

    public float BorderBottomThickness { get; set; }

    public PdfRgb BorderColor { get; set; } = PdfRgb.Black;

    public HorizontalAlign HAlign { get; set; } = HorizontalAlign.Left;

    public VerticalAlign VAlign { get; set; } = VerticalAlign.Top;

    /// <summary>Benar bila kotak ini tidak boleh terpotong halaman.</summary>
    public bool ShowEntire { get; set; }

    /// <summary>Gaya teks bawaan untuk isi kotak ini (bila diatur).</summary>
    public TextStyleDescriptor? DefaultText { get; set; }

    /// <summary>Kotak induk — dipakai mencari gaya teks bawaan terdekat.</summary>
    internal LayoutNode? Parent { get; set; }
}
