using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


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

