using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

public sealed partial class CffFont : IPdfGlyphSource
{
    // ----------------------------------------------------------- Charstrings

    public bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        if (_outlines.TryGetValue(glyphId, out var cached))
        {
            outline = cached ?? GlyphOutline.Empty;
            return cached is not null;
        }

        var built = BuildGlyph(glyphId);
        _outlines[glyphId] = built;
        outline = built ?? GlyphOutline.Empty;
        return built is not null;
    }

    public bool TryGetGlyphIdByName(string glyphName, out int glyphId) =>
        _nameToGlyph.TryGetValue(glyphName, out glyphId);

    public bool TryGetGlyphIdByUnicode(int codePoint, out int glyphId)
    {
        glyphId = 0;
        if (codePoint is >= 0 and <= 0xFFFF)
        {
            if (_nameToGlyph.TryGetValue(((char)codePoint).ToString(), out glyphId)) return true;
        }

        // Nama gaya OpenType "uniXXXX" / "uXXXXXX".
        string uni = $"uni{codePoint:X4}";
        if (_nameToGlyph.TryGetValue(uni, out glyphId)) return true;
        string u = $"u{codePoint:X}";
        return _nameToGlyph.TryGetValue(u, out glyphId);
    }

    private GlyphOutline? BuildGlyph(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStrings.Count) return null;
        var range = _charStrings[glyphId];
        if (range.Length <= 0) return null;

        int privateIndex = glyphId < _gidToPrivate.Count ? _gidToPrivate[glyphId] ?? 0 : 0;
        var privateDict = privateIndex >= 0 && privateIndex < _privat.Count ? _privat[privateIndex] : _privat[0];

        var builder = new GlyphOutline.Builder();
        var runner = new CharstringRunner(this, privateDict, builder);
        try
        {
            runner.Run(range.Start, range.Start + range.Length, 0);
        }
        catch (Exception)
        {
            return builder.IsEmpty ? null : builder.Build();
        }

        return builder.IsEmpty ? null : builder.Build();
    }


}
