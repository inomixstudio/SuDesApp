using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

public sealed partial class TrueTypeFont : IPdfGlyphSource
{

    public bool TryGetGlyphIdByUnicode(int codePoint, out int glyphId) =>
        _unicodeToGlyph.TryGetValue(codePoint, out glyphId);

    public bool TryGetGlyphIdByName(string glyphName, out int glyphId) =>
        _nameToGlyph.TryGetValue(glyphName, out glyphId);

    public bool TryGetGlyphIdForCharacter(char character, out int glyphId) =>
        TryGetGlyphIdByUnicode(character, out glyphId);

    // ----------------------------------------------------------------- Glif

    public bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        if (_outlines.TryGetValue(glyphId, out var cached))
        {
            outline = cached ?? GlyphOutline.Empty;
            return cached is not null;
        }

        var built = BuildGlyph(glyphId, 0);
        _outlines[glyphId] = built;
        outline = built ?? GlyphOutline.Empty;
        return built is not null;
    }

    private GlyphOutline? BuildGlyph(int glyphId, int depth)
    {
        if (depth > 6 || glyphId < 0 || glyphId >= _glyphCount) return null;
        if (!_tables.TryGetValue("glyf", out var glyf)) return null;
        if (glyphId + 1 >= _loca.Length) return null;

        int start = _loca[glyphId];
        int end = _loca[glyphId + 1];
        if (start >= end) return null;
        int offset = glyf.Offset + start;
        if (offset + 10 > _data.Length) return null;

        int contourCount = ReadInt16(offset);
        if (contourCount < 0) return BuildComposite(offset, depth);
        if (contourCount == 0) return null;

        var builder = new GlyphOutline.Builder();
        int cursor = offset + 10;
        var endPoints = new int[contourCount];
        for (int i = 0; i < contourCount; i++)
        {
            endPoints[i] = ReadUInt16(cursor);
            cursor += 2;
        }

        int instructionLength = ReadUInt16(cursor);
        cursor += 2 + instructionLength;

        int pointCount = endPoints[^1] + 1;
        if (pointCount <= 0 || pointCount > 100000) return null;

        var flags = new byte[pointCount];
        for (int i = 0; i < pointCount;)
        {
            if (cursor >= _data.Length) return null;
            byte flag = _data[cursor++];
            flags[i++] = flag;
            if ((flag & 8) != 0)
            {
                if (cursor >= _data.Length) return null;
                int repeat = _data[cursor++];
                for (int r = 0; r < repeat && i < pointCount; r++) flags[i++] = flag;
            }
        }

        var xs = new int[pointCount];
        int x = 0;
        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];
            if ((flag & 2) != 0)
            {
                if (cursor >= _data.Length) return null;
                int delta = _data[cursor++];
                x += (flag & 16) != 0 ? delta : -delta;
            }
            else if ((flag & 16) == 0)
            {
                x += ReadInt16(cursor);
                cursor += 2;
            }

            xs[i] = x;
        }

        var ys = new int[pointCount];
        int y = 0;
        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];
            if ((flag & 4) != 0)
            {
                if (cursor >= _data.Length) return null;
                int delta = _data[cursor++];
                y += (flag & 32) != 0 ? delta : -delta;
            }
            else if ((flag & 32) == 0)
            {
                y += ReadInt16(cursor);
                cursor += 2;
            }

            ys[i] = y;
        }

        int first = 0;
        foreach (int last in endPoints)
        {
            AppendContour(builder, flags, xs, ys, first, last);
            first = last + 1;
        }

        return builder.IsEmpty ? null : builder.Build();
    }

}
