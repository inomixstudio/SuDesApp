using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


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

