using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Bentuk glif dalam satuan font (y ke atas). Geometri disimpan sebagai aliran
/// perintah bertumpuk agar hemat memori dan mudah dipetakan ulang ke ukuran
/// berapa pun: 0 = move(x,y), 1 = line(x,y), 2 = quad(cx,cy,x,y),
/// 3 = cubic(x1,y1,x2,y2,x,y), 4 = close.
/// </summary>
public sealed class GlyphOutline
{
    public static readonly GlyphOutline Empty = new(Array.Empty<double>());

    public GlyphOutline(double[] commands) => Commands = commands;

    public double[] Commands { get; }

    public bool IsEmpty => Commands.Length == 0;

    /// <summary>Petakan glif ke jalur perangkat melalui matriks yang diberikan.</summary>
    public void AppendTo(PdfPathBuilder builder, in PdfMatrix matrix)
    {
        double[] commands = Commands;
        int index = 0;
        while (index < commands.Length)
        {
            switch ((int)commands[index++])
            {
                case 0:
                {
                    var point = matrix.Transform(commands[index], commands[index + 1]);
                    index += 2;
                    builder.MoveTo(point.X, point.Y);
                    break;
                }

                case 1:
                {
                    var point = matrix.Transform(commands[index], commands[index + 1]);
                    index += 2;
                    builder.LineTo(point.X, point.Y);
                    break;
                }

                case 2:
                {
                    var control = matrix.Transform(commands[index], commands[index + 1]);
                    var point = matrix.Transform(commands[index + 2], commands[index + 3]);
                    index += 4;
                    builder.QuadTo(control.X, control.Y, point.X, point.Y);
                    break;
                }

                case 3:
                {
                    var c1 = matrix.Transform(commands[index], commands[index + 1]);
                    var c2 = matrix.Transform(commands[index + 2], commands[index + 3]);
                    var point = matrix.Transform(commands[index + 4], commands[index + 5]);
                    index += 6;
                    builder.CurveTo(c1.X, c1.Y, c2.X, c2.Y, point.X, point.Y);
                    break;
                }

                default:
                    builder.Close();
                    break;
            }
        }
    }

    /// <summary>Penyusun aliran perintah dari pengurai font.</summary>
    public sealed class Builder
    {
        private readonly List<double> _commands = new(64);

        public bool IsEmpty => _commands.Count == 0;

        public void MoveTo(double x, double y)
        {
            _commands.Add(0);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void LineTo(double x, double y)
        {
            _commands.Add(1);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void QuadTo(double cx, double cy, double x, double y)
        {
            _commands.Add(2);
            _commands.Add(cx);
            _commands.Add(cy);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void CurveTo(double x1, double y1, double x2, double y2, double x, double y)
        {
            _commands.Add(3);
            _commands.Add(x1);
            _commands.Add(y1);
            _commands.Add(x2);
            _commands.Add(y2);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void Close() => _commands.Add(4);

        public GlyphOutline Build() => _commands.Count == 0 ? Empty : new GlyphOutline(_commands.ToArray());
    }
}

/// <summary>Sumber bentuk glif dari program font tersemat.</summary>
public interface IPdfGlyphSource
{
    /// <summary>Satuan per em (1000 pada CFF, 2048/1024 pada TrueType).</summary>
    double UnitsPerEm { get; }

    bool TryGetOutline(int glyphId, out GlyphOutline outline);

    /// <summary>Nomor glif untuk nama glif (dari /Differences pada PDF).</summary>
    bool TryGetGlyphIdByName(string glyphName, out int glyphId);

    /// <summary>Nomor glif untuk titik kode Unicode (font non-simbolik).</summary>
    bool TryGetGlyphIdByUnicode(int codePoint, out int glyphId);
}

/// <summary>Tabel pengkodean karakter sederhana (ISO 32000-1 Lampiran D).</summary>
public static class PdfEncodingTables
{
    private static readonly char?[] WinAnsi = BuildWinAnsi();
    private static readonly char?[] MacRoman = BuildMacRoman();
    private static readonly char?[] Standard = BuildStandard();
    private static readonly char?[] PdfDoc = BuildPdfDoc();

    public static char? Lookup(string? encodingName, int code)
    {
        var table = encodingName switch
        {
            "WinAnsiEncoding" => WinAnsi,
            "MacRomanEncoding" => MacRoman,
            "MacExpertEncoding" => MacRoman,
            "StandardEncoding" => Standard,
            "PDFDocEncoding" => PdfDoc,
            _ => WinAnsi
        };

        return code >= 0 && code < table.Length ? table[code] : null;
    }

    public static char? LookupByName(string name, int code) => Lookup(name, code);

    private static char?[] BuildWinAnsi()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        table[0x20] = ' ';
        string specials = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F\u0090''\"\"•–—˜™š›œ\u009DžŸ";
        for (int i = 0; i < specials.Length; i++) table[0x80 + i] = specials[i];
        for (int i = 0xA0; i < 0x100; i++) table[i] = (char)i;
        return table;
    }

    private static char?[] BuildMacRoman()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        string high = "ÄÅÇÉÑÖÜáàâäãåçéèêëíìîïñóòôöõúùûü†°¢£§•¶ß®©™´¨≠ÆØ∞±≤≥¥µ∂∑∏π∫ªºΩæø¿¡¬√ƒ≈∆«»…\u00A0ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔ\uF8FFÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";
        for (int i = 0; i < high.Length && 0x80 + i < 256; i++) table[0x80 + i] = high[i];
        return table;
    }

    private static char?[] BuildStandard()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        table[0xA0] = ' ';
        string high =
            "\u00A1\u00A2\u00A3\u2044\u00A5\u0192\u00A7\u00A4'\u201C\u00AB\u2039\u203A\uFB01\uFB02\u2013" +
            "\u2020\u2021\u00B7\u201A\u201E\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A" +
            "\u0153\u0161\u0178\u0178\u00A0\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB" +
            "\u00AC\u00AD\u00AE\u00AF\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB" +
            "\u00BC\u00BD\u00BE\u00BF\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB" +
            "\u00CC\u00CD\u00CE\u00CF\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB" +
            "\u00DC\u00DD\u00DE\u00DF\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB" +
            "\u00EC\u00ED\u00EE\u00EF\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB" +
            "\u00FC\u00FD\u00FE\u00FF";
        // Daftar di atas memuat lebih banyak nama glif daripada ruang yang
        // tersedia (0xA1..0xFF); sisanya diabaikan supaya tabel tetap sah.
        for (int i = 0; i < high.Length && 0xA1 + i < 256; i++) table[0xA1 + i] = high[i];
        return table;
    }

    private static char?[] BuildPdfDoc()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        string high =
            "\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203A\u2212\u2030\u201E\u201C\u201D\u2018" +
            "\u2019\u201A\u2122\uFB01\uFB02\u0141\u0152\u0160\u0178\u017D\u0131\u0142\u0153\u0161\u017E\uFFFD" +
            "\u20AC\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB\u00AC\u00AD\u00AE\u00AF" +
            "\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB\u00BC\u00BD\u00BE\u00BF" +
            "\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC\u00CD\u00CE\u00CF" +
            "\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB\u00DC\u00DD\u00DE\u00DF" +
            "\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF" +
            "\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB\u00FC\u00FD\u00FE\u00FF";
        for (int i = 0; i < high.Length; i++) table[0x80 + i] = high[i];
        return table;
    }
}

/// <summary>
/// Peta dari kode karakter ke teks (CMap /ToUnicode) atau ke CID
/// (/Encoding berupa stream CMap yang ditanam).
/// </summary>
public sealed class PdfCMap
{
    private readonly Dictionary<int, string> _single = new();
    private readonly List<(int Low, int High, string[] Values)> _ranges = new();
    private readonly int _codeLength;

    private PdfCMap(int codeLength) => _codeLength = codeLength;

    public int CodeLength => _codeLength;

    public int Count => _single.Count + _ranges.Count;

    public bool TryDecode(int code, out string value)
    {
        if (_single.TryGetValue(code, out value!)) return true;
        foreach (var (low, high, values) in _ranges)
        {
            if (code < low || code > high) continue;
            int index = code - low;
            value = index < values.Length ? values[index] : values[0];
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryDecodeInteger(int code, out int cid)
    {
        cid = 0;
        if (!TryDecode(code, out string text) || text.Length == 0) return false;
        cid = text[0];
        return true;
    }

    /// <summary>Baca CMap (ToUnicode maupun CMap pengkodean CID).</summary>
    public static PdfCMap Parse(byte[] data, int codeLengthHint)
    {
        var cmap = new PdfCMap(codeLengthHint);
        var parser = new PdfParser(new PdfLexer(data));

        while (true)
        {
            // Token dibaca lewat parser, bukan lewat lexer langsung: parser menyimpan
            // lookahead-nya sendiri, dan mencampur keduanya membuat token pertama tiap
            // blok pemetaan dianggap sudah terpakai sehingga CMap terbaca kosong.
            var token = parser.Next();
            if (token.Type == PdfTokenType.End) break;
            if (token.Type != PdfTokenType.Keyword) continue;

            switch (token.Name)
            {
                case "begincodespacerange":
                    ReadCodespaceRange(parser);
                    break;
                case "beginbfchar":
                case "begincidchar":
                    ReadCharacterMappings(parser, cmap, isRange: false);
                    break;
                case "beginbfrange":
                case "begincidrange":
                    ReadCharacterMappings(parser, cmap, isRange: true);
                    break;
            }
        }

        return cmap;
    }

    private static void ReadCodespaceRange(PdfParser parser)
    {
        // Rentang kode tidak dipakai: panjang kode sudah ditentukan pemanggil.
        // Isinya dibaca agar blok berikutnya tetap sejajar.
        while (parser.Peek().Type == PdfTokenType.String)
        {
            parser.Next();
        }
    }

    private static void ReadCharacterMappings(PdfParser parser, PdfCMap cmap, bool isRange)
    {
        while (true)
        {
            var token = parser.Peek();
            if (token.Type != PdfTokenType.String) return;
            parser.Next();
            int low = BigEndianValue(token.Bytes ?? Array.Empty<byte>());

            if (!isRange)
            {
                if (!parser.TryParseValue(out var value)) return;
                cmap._single[low] = Text(value);
                continue;
            }

            if (!parser.TryParseValue(out var highValue)) return;
            int high = highValue switch
            {
                PdfString text => BigEndianValue(text.Bytes),
                _ => low
            };

            if (!parser.TryParseValue(out var destination)) return;
            if (destination is PdfArray array)
            {
                var values = new string[array.Count];
                for (int i = 0; i < array.Count; i++) values[i] = Text(array.At(i));
                cmap._ranges.Add((low, Math.Max(low, high), values));
                continue;
            }

            string single = Text(destination);
            if (single.Length == 0) continue;
            int first = char.ConvertToUtf32(single, 0);
            var generated = new string[Math.Max(1, high - low + 1)];
            for (int i = 0; i < generated.Length; i++)
            {
                generated[i] = first + i <= 0x10FFFF ? char.ConvertFromUtf32(first + i) : single;
            }

            cmap._ranges.Add((low, Math.Max(low, high), generated));
        }
    }

    private static string Text(PdfObject value) => value switch
    {
        PdfString text => DecodeUtf16Be(text.Bytes),
        PdfNumber number => ((char)number.ToInt()).ToString(),
        _ => string.Empty
    };

    public static string DecodeUtf16Be(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        if (bytes.Length == 1) return ((char)bytes[0]).ToString();
        if (bytes.Length % 2 != 0) return Encoding.BigEndianUnicode.GetString(bytes.AsSpan(0, bytes.Length - 1).ToArray());
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static int BigEndianValue(byte[] bytes)
    {
        int value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }
}

/// <summary>Font halaman: lebar, pemetaan teks, dan bentuk glif.</summary>
public abstract class PdfFont
{
    private readonly Dictionary<int, GlyphOutline> _outlineCache = new();
    private readonly HashSet<int> _missingOutlines = new();

    public abstract string Name { get; }

    public abstract bool IsComposite { get; }

    public virtual bool IsVertical => false;

    public virtual bool IsSymbolic => false;

    /// <summary>Latar belakang serba satu em (1000 unit) — dipakai penataan huruf.</summary>
    public virtual double Ascent => 800;

    public virtual double Descent => -200;

    /// <summary>Bentuk glif tersedia (font tersemat dengan program yang didukung).</summary>
    public virtual bool HasOutlines => false;

    /// <summary>Satuan per em pada program glif (1000 untuk CFF, 2048 umumnya TrueType).</summary>
    public virtual double GlyphUnitsPerEm => 1000;

    /// <summary>Baca satu kode dari data string dan hitung panjangnya.</summary>
    public abstract int ReadCode(byte[] data, int position, int end, out int code);

    /// <summary>Lebar kode dalam satuan 1/1000 em.</summary>
    public abstract double GetWidth(int code);

    /// <summary>Teks Unicode untuk kode tersebut, atau null bila tidak dikenal.</summary>
    public abstract string? Decode(int code);

    public abstract bool TryGetGlyph(int code, out int glyphId);

    public virtual bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        outline = GlyphOutline.Empty;
        return false;
    }

    protected bool TryGetCachedOutline(int glyphId, out GlyphOutline outline)
    {
        if (_outlineCache.TryGetValue(glyphId, out var cached))
        {
            outline = cached;
            return true;
        }

        if (_missingOutlines.Contains(glyphId))
        {
            outline = GlyphOutline.Empty;
            return false;
        }

        outline = GlyphOutline.Empty;
        return false;
    }

    protected void CacheOutline(int glyphId, GlyphOutline? outline)
    {
        if (outline is null || outline.IsEmpty)
        {
            _missingOutlines.Add(glyphId);
            return;
        }

        _outlineCache[glyphId] = outline;
    }
}

/// <summary>Font sederhana (Type1/TrueType/MMType1): kode 1 byte.</summary>
public sealed class PdfSimpleFont : PdfFont
{
    private readonly PdfCMap? _toUnicode;
    private readonly string? _encodingName;
    private readonly Dictionary<int, string> _differences = new();
    private readonly double[] _widths;
    private readonly int _firstChar;
    private readonly double _missingWidth;
    private readonly IPdfGlyphSource? _glyphSource;

    internal PdfSimpleFont(
        string name,
        PdfCMap? toUnicode,
        string? encodingName,
        Dictionary<int, string> differences,
        double[] widths,
        int firstChar,
        double missingWidth,
        IPdfGlyphSource? glyphSource,
        double ascent,
        double descent,
        bool symbolic)
    {
        Name = name;
        _toUnicode = toUnicode;
        _encodingName = encodingName;
        _differences = differences;
        _widths = widths;
        _firstChar = firstChar;
        _missingWidth = missingWidth;
        _glyphSource = glyphSource;
        Ascent = ascent;
        Descent = descent;
        IsSymbolic = symbolic;
    }

    public override string Name { get; }

    public override bool IsComposite => false;

    public override bool IsSymbolic { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override bool HasOutlines => _glyphSource is not null;

    public override double GlyphUnitsPerEm =>
        _glyphSource?.UnitsPerEm is { } units && units > 0 ? units : 1000;

    public override int ReadCode(byte[] data, int position, int end, out int code)
    {
        code = position < end ? data[position] : 0;
        return position < end ? 1 : 0;
    }

    public override double GetWidth(int code)
    {
        int index = code - _firstChar;
        if (index >= 0 && index < _widths.Length) return _widths[index];
        return _missingWidth;
    }

    public override string? Decode(int code)
    {
        if (_toUnicode is not null && _toUnicode.TryDecode(code, out string mapped) && mapped.Length > 0)
        {
            return mapped;
        }

        if (_differences.TryGetValue(code, out string? glyphName) && TryMapGlyphName(glyphName, out char named))
        {
            return named.ToString();
        }

        char? character = PdfEncodingTables.Lookup(_encodingName, code);
        return character?.ToString();
    }

    private bool TryMapGlyphName(string name, out char value)
    {
        value = '\0';
        if (name.Length == 1)
        {
            value = name[0];
            return true;
        }

        // Nama glif standar yang lazim (subset AGL yang dipakai surat desa).
        switch (name)
        {
            case "space": value = ' '; return true;
            case "period": value = '.'; return true;
            case "comma": value = ','; return true;
            case "colon": value = ':'; return true;
            case "semicolon": value = ';'; return true;
            case "slash": value = '/'; return true;
            case "hyphen": value = '-'; return true;
            case "parenleft": value = '('; return true;
            case "parenright": value = ')'; return true;
            case "bracketleft": value = '['; return true;
            case "bracketright": value = ']'; return true;
            case "quotedblleft": value = '\u201C'; return true;
            case "quotedblright": value = '\u201D'; return true;
            case "quoteright": value = '\u2019'; return true;
            case "quoteleft": value = '\u2018'; return true;
            case "endash": value = '\u2013'; return true;
            case "emdash": value = '\u2014'; return true;
            case "bullet": value = '\u2022'; return true;
            case "underscore": value = '_'; return true;
            case "numbersign": value = '#'; return true;
            case "percent": value = '%'; return true;
            case "ampersand": value = '&'; return true;
            case "asterisk": value = '*'; return true;
            case "plus": value = '+'; return true;
            case "equal": value = '='; return true;
            case "question": value = '?'; return true;
            case "exclam": value = '!'; return true;
            case "at": value = '@'; return true;
            case "quotedbl": value = '"'; return true;
            case "apostrophe": value = '\''; return true;
            case "grave": value = '`'; return true;
            case "asciitilde": value = '~'; return true;
            case "bar": value = '|'; return true;
            case "backslash": value = '\\'; return true;
            case "braceleft": value = '{'; return true;
            case "braceright": value = '}'; return true;
            case "less": value = '<'; return true;
            case "greater": value = '>'; return true;
            case "zero": value = '0'; return true;
            case "one": value = '1'; return true;
            case "two": value = '2'; return true;
            case "three": value = '3'; return true;
            case "four": value = '4'; return true;
            case "five": value = '5'; return true;
            case "six": value = '6'; return true;
            case "seven": value = '7'; return true;
            case "eight": value = '8'; return true;
            case "nine": value = '9'; return true;
        }

        return false;
    }

    public override bool TryGetGlyph(int code, out int glyphId)
    {
        glyphId = 0;
        if (_glyphSource is null) return false;

        // Font non-simbolik dicari lewat karakter dari pengkodean; font dengan
        // /Differences memakai nama glif langsung (pola Word/LibreOffice).
        if (_differences.TryGetValue(code, out string? glyphName)
            && _glyphSource.TryGetGlyphIdByName(glyphName, out glyphId))
        {
            return true;
        }

        char? character = PdfEncodingTables.Lookup(_encodingName, code);
        if (character is not null && _glyphSource.TryGetGlyphIdByUnicode(character.Value, out glyphId))
        {
            return true;
        }

        if (!IsSymbolic && _glyphSource.TryGetGlyphIdByUnicode(code, out glyphId)) return true;
        return IsSymbolic && _glyphSource.TryGetGlyphIdByName(((char)code).ToString(), out glyphId);
    }

    public override bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        if (TryGetCachedOutline(glyphId, out outline)) return true;
        if (_glyphSource is null) return false;
        if (_glyphSource.TryGetOutline(glyphId, out outline))
        {
            CacheOutline(glyphId, outline);
            return true;
        }

        CacheOutline(glyphId, null);
        return false;
    }
}

/// <summary>Font gabungan (Type0): kode multi-byte, CID sebagai glif.</summary>
public sealed class PdfCompositeFont : PdfFont
{
    private readonly PdfCMap? _toUnicode;
    private readonly PdfCMap? _cidMap;
    private readonly Dictionary<int, double> _widths = new();
    private readonly double _defaultWidth;
    private readonly IPdfGlyphSource? _glyphSource;
    private readonly ushort[]? _cidToGid;
    private readonly int _codeLength;

    internal PdfCompositeFont(
        string name,
        PdfCMap? toUnicode,
        PdfCMap? cidMap,
        Dictionary<int, double> widths,
        double defaultWidth,
        IPdfGlyphSource? glyphSource,
        ushort[]? cidToGid,
        int codeLength,
        bool vertical,
        bool symbolic,
        double ascent,
        double descent)
    {
        Name = name;
        _toUnicode = toUnicode;
        _cidMap = cidMap;
        _widths = widths;
        _defaultWidth = defaultWidth;
        _glyphSource = glyphSource;
        _cidToGid = cidToGid;
        _codeLength = Math.Clamp(codeLength, 1, 4);
        IsVertical = vertical;
        IsSymbolic = symbolic;
        Ascent = ascent;
        Descent = descent;
    }

    public override string Name { get; }

    public override bool IsComposite => true;

    public override bool IsVertical { get; }

    public override bool IsSymbolic { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override bool HasOutlines => _glyphSource is not null;

    public override double GlyphUnitsPerEm =>
        _glyphSource?.UnitsPerEm is { } units && units > 0 ? units : 1000;

    public override int ReadCode(byte[] data, int position, int end, out int code)
    {
        code = 0;
        int available = end - position;
        if (available <= 0) return 0;
        int length = Math.Min(_codeLength, available);
        for (int i = 0; i < length; i++)
        {
            code = (code << 8) | data[position + i];
        }

        return length;
    }

    public override double GetWidth(int code)
    {
        int cid = ResolveCid(code);
        return _widths.TryGetValue(cid, out double width) ? width : _defaultWidth;
    }

    public override string? Decode(int code)
    {
        if (_toUnicode is not null && _toUnicode.TryDecode(code, out string mapped) && mapped.Length > 0)
        {
            return mapped;
        }

        // Tanpa ToUnicode: kode dianggap titik kode Unicode (Identity).
        if (code > 0 && code <= 0x10FFFF)
        {
            try
            {
                return char.ConvertFromUtf32(code);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private int ResolveCid(int code)
    {
        if (_cidMap is not null && _cidMap.TryDecodeInteger(code, out int cid)) return cid;
        return code;
    }

    public override bool TryGetGlyph(int code, out int glyphId)
    {
        glyphId = 0;
        int cid = ResolveCid(code);
        if (_cidToGid is not null)
        {
            glyphId = cid >= 0 && cid < _cidToGid.Length ? _cidToGid[cid] : 0;
            return glyphId != 0 || cid == 0;
        }

        glyphId = cid;
        return true;
    }

    public override bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        if (TryGetCachedOutline(glyphId, out outline)) return true;
        if (_glyphSource is null) return false;
        if (_glyphSource.TryGetOutline(glyphId, out outline))
        {
            CacheOutline(glyphId, outline);
            return true;
        }

        CacheOutline(glyphId, null);
        return false;
    }
}

/// <summary>Pembuat font dari kamus sumber daya halaman.</summary>
public sealed class PdfFontCache
{
    private readonly PdfDocument _document;
    private readonly Dictionary<long, PdfFont?> _cache = new();

    public PdfFontCache(PdfDocument document) => _document = document;

    public PdfFont? Get(PdfObject? fontValue)
    {
        var dictionary = _document.ResolveDictionary(fontValue);
        if (dictionary is null) return null;

        long key = fontValue is PdfReference reference
            ? ((long)reference.Number << 16) | (uint)reference.Generation
            : -dictionary.Items.Count - 1;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        PdfFont? font;
        try
        {
            font = Build(dictionary);
        }
        catch (PdfException)
        {
            font = null;
        }

        _cache[key] = font;
        return font;
    }

    private PdfFont? Build(PdfDictionary dictionary)
    {
        string? subtype = dictionary.GetNameValue("Subtype");
        string name = dictionary.GetNameValue("BaseFont") ?? "Font";
        var descriptor = ResolveDescriptor(dictionary);
        var toUnicode = ReadToUnicode(dictionary);

        return subtype switch
        {
            "Type0" => BuildComposite(dictionary, name, descriptor, toUnicode),
            "Type1" or "TrueType" or "MMType1" => BuildSimple(dictionary, name, descriptor, toUnicode),
            "Type3" => null,
            _ => BuildSimple(dictionary, name, descriptor, toUnicode)
        };
    }

    private PdfDictionary? ResolveDescriptor(PdfDictionary dictionary)
    {
        var direct = _document.ResolveDictionary(dictionary.Raw("FontDescriptor"));
        if (direct is not null) return direct;
        var descendants = _document.ResolveArray(dictionary.Raw("DescendantFonts"));
        var descendant = _document.ResolveDictionary(descendants?.At(0));
        return _document.ResolveDictionary(descendant?.Raw("FontDescriptor"));
    }

    private PdfCMap? ReadToUnicode(PdfDictionary dictionary)
    {
        var stream = _document.ResolveStream(dictionary.Raw("ToUnicode"));
        if (stream is null) return null;
        try
        {
            return PdfCMap.Parse(stream.Decoded(), 2);
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private PdfFont? BuildSimple(
        PdfDictionary dictionary, string name, PdfDictionary? descriptor, PdfCMap? toUnicode)
    {
        double[] widths = Array.Empty<double>();
        var widthArray = _document.ResolveArray(dictionary.Raw("Widths"));
        if (widthArray is not null)
        {
            widths = new double[widthArray.Count];
            for (int i = 0; i < widthArray.Count; i++)
            {
                widths[i] = _document.ResolveNumber(widthArray.At(i))?.Value ?? 0;
            }
        }

        int firstChar = (int)(dictionary.GetNumber("FirstChar") ?? 0);
        double missingWidth = descriptor?.GetNumber("MissingWidth") ?? 0;

        string? encodingName = null;
        var differences = new Dictionary<int, string>();
        var encoding = _document.Resolve(dictionary.Raw("Encoding"));
        switch (encoding)
        {
            case PdfName direct:
                encodingName = direct.Value;
                break;
            case PdfDictionary encodingDict:
                encodingName = encodingDict.GetNameValue("BaseEncoding");
                ReadDifferences(encodingDict, differences);
                break;
        }

        bool symbolic = false;
        if (descriptor?.Raw("Flags") is PdfNumber flags) symbolic = (flags.ToInt() & 4) != 0;

        var (ascent, descent) = ReadMetrics(descriptor, 800, -200);
        var glyphSource = CreateGlyphSource(descriptor, name);
        return new PdfSimpleFont(
            name, toUnicode, encodingName, differences, widths, firstChar, missingWidth,
            glyphSource, ascent, descent, symbolic);
    }

    private void ReadDifferences(PdfDictionary encodingDict, Dictionary<int, string> differences)
    {
        var array = _document.ResolveArray(encodingDict.Raw("Differences"));
        if (array is null) return;
        int current = 0;
        foreach (var item in array.Items)
        {
            var resolved = _document.Resolve(item);
            switch (resolved)
            {
                case PdfNumber number:
                    current = number.ToInt();
                    break;
                case PdfName glyphName:
                    differences[current++] = glyphName.Value;
                    break;
            }
        }
    }

    private PdfFont? BuildComposite(
        PdfDictionary dictionary, string name, PdfDictionary? descriptor, PdfCMap? toUnicode)
    {
        var descendants = _document.ResolveArray(dictionary.Raw("DescendantFonts"));
        var descendant = _document.ResolveDictionary(descendants?.At(0));
        if (descendant is null) return null;
        if (descriptor is null) descriptor = _document.ResolveDictionary(descendant.Raw("FontDescriptor"));
        string? cidSubtype = descendant.GetNameValue("Subtype");
        bool isCidType2 = cidSubtype == "CIDFontType2";

        var widths = new Dictionary<int, double>();
        ReadCidWidths(descendant, widths);
        double defaultWidth = descendant.GetNumber("DW") ?? 1000;

        var encoding = _document.Resolve(dictionary.Raw("Encoding"));
        PdfCMap? cidMap = null;
        bool vertical = false;
        int codeLength = 2;
        switch (encoding)
        {
            case PdfName encodingName:
                vertical = encodingName.Value.EndsWith("-V", StringComparison.Ordinal);
                if (encodingName.Value is "Identity-H" or "Identity-V"
                    || encodingName.Value.StartsWith("UniGB", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniJIS", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniKS", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniCNS", StringComparison.Ordinal))
                {
                    codeLength = 2;
                }
                else
                {
                    // CMap bawaan (mis. 90ms-RKSJ-H) tidak dibundel; kode 2 byte tetap
                    // dibaca dan teks diambil dari /ToUnicode bila ada.
                    codeLength = 2;
                }

                break;
            case PdfStream cmapStream:
            {
                var parsed = PdfCMap.Parse(cmapStream.Decoded(), 2);
                cidMap = parsed;
                codeLength = parsed.CodeLength is 1 or 2 or 4 ? parsed.CodeLength : 2;
                break;
            }
        }

        ushort[]? cidToGid = null;
        if (isCidType2)
        {
            var map = _document.Resolve(descendant.Raw("CIDToGIDMap"));
            if (map is PdfName mapName && mapName.Value == "Identity")
            {
                cidToGid = null;
            }
            else if (map is PdfStream mapStream)
            {
                byte[] bytes = mapStream.Decoded();
                cidToGid = new ushort[bytes.Length / 2];
                for (int i = 0; i < cidToGid.Length; i++)
                {
                    cidToGid[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
                }
            }
        }

        var (ascent, descent) = ReadMetrics(descriptor, 880, -220);
        bool symbolic = false;
        if (descriptor?.Raw("Flags") is PdfNumber flagsNumber) symbolic = (flagsNumber.ToInt() & 4) != 0;
        var glyphSource = CreateGlyphSource(descriptor, name);

        return new PdfCompositeFont(
            name, toUnicode, cidMap, widths, defaultWidth, glyphSource, cidToGid, codeLength,
            vertical, symbolic, ascent, descent);
    }

    private void ReadCidWidths(PdfDictionary descendant, Dictionary<int, double> widths)
    {
        var array = _document.ResolveArray(descendant.Raw("W"));
        if (array is null) return;

        int index = 0;
        while (index < array.Count)
        {
            int first = (int)(_document.ResolveNumber(array.At(index))?.Value ?? 0);
            index++;
            if (index >= array.Count) break;

            var next = _document.Resolve(array.At(index));
            if (next is PdfArray list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    widths[first + i] = _document.ResolveNumber(list.At(i))?.Value ?? 0;
                }

                index++;
                continue;
            }

            if (next is PdfNumber lastNumber)
            {
                index++;
                if (index >= array.Count) break;
                double width = _document.ResolveNumber(array.At(index))?.Value ?? 0;
                index++;
                int last = lastNumber.ToInt();
                for (int cid = first; cid <= last && cid - first < 65536; cid++) widths[cid] = width;
                continue;
            }

            index++;
        }
    }

    private (double Ascent, double Descent) ReadMetrics(PdfDictionary? descriptor, double defaultAscent, double defaultDescent)
    {
        if (descriptor is null) return (defaultAscent, defaultDescent);
        double ascent = descriptor.GetNumber("Ascent") ?? defaultAscent;
        double descent = descriptor.GetNumber("Descent") ?? defaultDescent;
        return (ascent, descent);
    }

    private IPdfGlyphSource? CreateGlyphSource(PdfDictionary? descriptor, string baseFontName)
    {
        // Font standar tanpa berkas tersemat (mis. Times-Roman pada surat yang
        // ditulis SudesPdf): bentuk hurufnya dipinjam dari font sistem, sama
        // seperti pembaca PDF lain — tidak ada berkas font yang didistribusikan.
        if (descriptor is null || !HasEmbeddedProgram(descriptor))
        {
            return PdfStandardFontSource.Resolve(baseFontName);
        }

        var program = _document.Resolve(descriptor.Raw("FontFile2"));
        if (program is PdfStream truetype)
        {
            try
            {
                return TrueTypeFont.Parse(truetype.Decoded());
            }
            catch (PdfException)
            {
                return null;
            }
        }

        var cff = _document.Resolve(descriptor.Raw("FontFile3"));
        if (cff is PdfStream cffStream)
        {
            try
            {
                return CffFont.Parse(cffStream.Decoded(), cffStream.Dictionary.GetNameValue("Subtype"));
            }
            catch (PdfException)
            {
                return null;
            }
        }

        // FontFile (Type1 mentah) belum didukung; teksnya tetap terbaca lewat
        // /ToUnicode dan lebar /Widths.
        return null;
    }

    /// <summary>Benar bila font membawa program huruf tersemat.</summary>
    private static bool HasEmbeddedProgram(PdfDictionary descriptor) =>
        descriptor.Raw("FontFile") is not (null or PdfNull)
        || descriptor.Raw("FontFile2") is not (null or PdfNull)
        || descriptor.Raw("FontFile3") is not (null or PdfNull);
}
