using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

public sealed partial class TrueTypeFont : IPdfGlyphSource
{
    // ---------------------------------------------------------------- Tabel

    private int[] ReadLoca()
    {
        if (!_tables.TryGetValue("loca", out var loca)) return Array.Empty<int>();
        int count = _glyphCount + 1;
        var offsets = new int[count];
        int entrySize = _longLoca ? 4 : 2;
        for (int i = 0; i < count; i++)
        {
            int position = loca.Offset + i * entrySize;
            if (position + entrySize > _data.Length) break;
            offsets[i] = _longLoca
                ? (int)ReadUInt32(position)
                : ReadUInt16(position) * 2;
        }

        return offsets;
    }

    private double[] ReadAdvances()
    {
        var advances = new double[_glyphCount];
        if (!_tables.TryGetValue("hmtx", out var hmtx) || !_tables.TryGetValue("hhea", out var hhea)) return advances;
        int numberOfMetrics = ReadUInt16(hhea.Offset + 34);
        int lastAdvance = 0;
        for (int i = 0; i < _glyphCount; i++)
        {
            if (i < numberOfMetrics)
            {
                int position = hmtx.Offset + i * 4;
                lastAdvance = position + 2 <= _data.Length ? ReadUInt16(position) : lastAdvance;
            }

            advances[i] = lastAdvance;
        }

        return advances;
    }

    public double GetAdvance(int glyphId) =>
        glyphId >= 0 && glyphId < _advances.Length ? _advances[glyphId] : 0;

    // -------------------------------------------------------------- Pengkodean

    private void ReadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap)) return;
        int tableCount = ReadUInt16(cmap.Offset + 2);

        int bestScore = -1;
        int bestOffset = -1;
        for (int i = 0; i < tableCount; i++)
        {
            int record = cmap.Offset + 4 + i * 8;
            if (record + 8 > _data.Length) break;
            int platform = ReadUInt16(record);
            int encoding = ReadUInt16(record + 2);
            int offset = cmap.Offset + (int)ReadUInt32(record + 4);
            int score = platform switch
            {
                3 when encoding == 10 => 5,
                3 when encoding == 1 => 4,
                0 => 3,
                1 when encoding == 0 => 2,
                _ => 1
            };
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
            }
        }

        if (bestOffset < 0) return;
        int format = ReadUInt16(bestOffset);
        switch (format)
        {
            case 0:
                for (int code = 0; code < 256; code++)
                {
                    int glyph = _data[bestOffset + 6 + code];
                    if (glyph != 0) _unicodeToGlyph[code] = glyph;
                }

                break;
            case 4:
                ReadCmapFormat4(bestOffset);
                break;
            case 6:
            {
                int first = ReadUInt16(bestOffset + 6);
                int count = ReadUInt16(bestOffset + 8);
                for (int i = 0; i < count; i++)
                {
                    int glyph = ReadUInt16(bestOffset + 10 + i * 2);
                    if (glyph != 0) _unicodeToGlyph[first + i] = glyph;
                }

                break;
            }

            case 12:
            {
                int groups = (int)ReadUInt32(bestOffset + 12);
                for (int i = 0; i < groups; i++)
                {
                    int record = bestOffset + 16 + i * 12;
                    if (record + 12 > _data.Length) break;
                    uint start = ReadUInt32(record);
                    uint end = ReadUInt32(record + 4);
                    uint startGlyph = ReadUInt32(record + 8);
                    if (end - start > 0x10FFFF) continue;
                    for (uint code = start; code <= end; code++)
                    {
                        int glyph = (int)(startGlyph + (code - start));
                        if (glyph == 0) continue;
                        if (glyph < _glyphCount) _unicodeToGlyph[(int)code] = glyph;
                    }
                }

                break;
            }
        }
    }

    private void ReadCmapFormat4(int offset)
    {
        int segments = ReadUInt16(offset + 6) / 2;
        int endCodes = offset + 14;
        int startCodes = endCodes + segments * 2 + 2;
        int idDeltas = startCodes + segments * 2;
        int idRangeOffsets = idDeltas + segments * 2;
        int glyphArray = idRangeOffsets + segments * 2;

        for (int segment = 0; segment < segments; segment++)
        {
            int end = ReadUInt16(endCodes + segment * 2);
            int start = ReadUInt16(startCodes + segment * 2);
            int delta = ReadInt16(idDeltas + segment * 2);
            int rangeOffset = ReadUInt16(idRangeOffsets + segment * 2);
            if (start > end || start == 0xFFFF) continue;

            for (int code = start; code <= end; code++)
            {
                int glyph;
                if (rangeOffset == 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    int position = idRangeOffsets + segment * 2 + rangeOffset + (code - start) * 2;
                    if (position + 2 > _data.Length) continue;
                    glyph = ReadUInt16(position);
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }

                if (glyph != 0 && glyph < _glyphCount && !_unicodeToGlyph.ContainsKey(code))
                {
                    _unicodeToGlyph[code] = glyph;
                }
            }
        }
    }

    private void ReadPostNames()
    {
        if (!_tables.TryGetValue("post", out var post)) return;
        if (post.Offset + 32 > _data.Length) return;
        int version = (int)ReadUInt32(post.Offset);
        if (version != 0x00020000) return;

        int glyphCount = ReadUInt16(post.Offset + 32);
        var indices = new int[glyphCount];
        int cursor = post.Offset + 34;
        for (int i = 0; i < glyphCount; i++)
        {
            if (cursor + 2 > _data.Length) return;
            indices[i] = ReadUInt16(cursor);
            cursor += 2;
        }

        var pascalNames = new List<string>();
        while (cursor < _data.Length && pascalNames.Count < glyphCount)
        {
            int length = _data[cursor++];
            if (length == 0 || cursor + length > _data.Length) break;
            pascalNames.Add(Encoding.ASCII.GetString(_data, cursor, length));
            cursor += length;
        }

        for (int i = 0; i < glyphCount; i++)
        {
            int index = indices[i];
            string? name = index < StandardMacGlyphNames.Length
                ? StandardMacGlyphNames[index]
                : index - StandardMacGlyphNames.Length < pascalNames.Count
                    ? pascalNames[index - StandardMacGlyphNames.Length]
                    : null;
            if (name is not null && !_nameToGlyph.ContainsKey(name)) _nameToGlyph[name] = i;
        }
    }

}
