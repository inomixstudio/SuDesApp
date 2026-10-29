using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Pengurai font TrueType (ISO/IEC 14496-22): tabel glyf/loca/cmap/post.
/// Dipakai untuk mengambil bentuk glif dari FontFile2 pada PDF.
/// </summary>
public sealed class TrueTypeFont : IPdfGlyphSource
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly int[] _loca;
    private readonly int _glyphCount;
    private readonly bool _longLoca;
    private readonly Dictionary<int, int> _unicodeToGlyph = new();
    private readonly Dictionary<string, int> _nameToGlyph = new(StringComparer.Ordinal);
    private readonly Dictionary<int, GlyphOutline?> _outlines = new();
    private readonly double[] _advances;

    private TrueTypeFont(byte[] data, int tableStart)
    {
        _data = data;
        int tableCount = ReadUInt16(tableStart + 4);
        for (int i = 0; i < tableCount; i++)
        {
            int record = tableStart + 12 + i * 16;
            if (record + 16 > data.Length) break;
            string tag = Encoding.ASCII.GetString(data, record, 4);
            int offset = (int)ReadUInt32(record + 8);
            int length = (int)ReadUInt32(record + 12);
            _tables[tag] = (offset, length);
        }

        UnitsPerEm = GetInt16("head", 18);
        if (UnitsPerEm <= 0) UnitsPerEm = 1000;
        _longLoca = GetInt16("head", 50) != 0;
        _glyphCount = GetUInt16("maxp", 4);

        _loca = ReadLoca();
        ReadCmap();
        ReadPostNames();
        _advances = ReadAdvances();
    }

    public double UnitsPerEm { get; }

    public int GlyphCount => _glyphCount;

    public static TrueTypeFont? Parse(byte[] data)
    {
        if (data.Length < 12) return null;
        int start = 0;
        uint tag = ReadUInt32(data, 0);
        if (tag == 0x74746366) // 'ttcf'
        {
            int count = (int)ReadUInt32(data, 8);
            if (count <= 0) return null;
            start = (int)ReadUInt32(data, 12);
        }
        else if (tag != 0x00010000 && tag != 0x74727565 && tag != 0x4F54544F)
        {
            return null;
        }

        try
        {
            return new TrueTypeFont(data, start);
        }
        catch (Exception)
        {
            return null;
        }
    }

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

    private static void AppendContour(
        GlyphOutline.Builder builder, byte[] flags, int[] xs, int[] ys, int first, int last)
    {
        int count = last - first + 1;
        if (count < 2) return;

        bool OnCurve(int index) => (flags[index] & 1) != 0;

        // Titik awal: on-curve pertama; bila tidak ada, titik tengah dua off-curve.
        int startIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (OnCurve(first + i))
            {
                startIndex = i;
                break;
            }
        }

        double startX;
        double startY;
        int scan;
        if (startIndex < 0)
        {
            int a = first;
            int b = first + (count > 1 ? 1 : 0);
            startX = (xs[a] + xs[b]) / 2.0;
            startY = (ys[a] + ys[b]) / 2.0;
            scan = 0;
        }
        else
        {
            startX = xs[first + startIndex];
            startY = ys[first + startIndex];
            scan = startIndex + 1;
        }

        builder.MoveTo(startX, startY);

        double currentX = startX;
        double currentY = startY;
        double? pendingX = null;
        double? pendingY = null;

        for (int step = 0; step < count; step++)
        {
            int index = first + ((scan + step) % count);
            double px = xs[index];
            double py = ys[index];
            if (OnCurve(index))
            {
                if (pendingX is not null)
                {
                    builder.QuadTo(pendingX.Value, pendingY!.Value, px, py);
                    pendingX = null;
                }
                else
                {
                    builder.LineTo(px, py);
                }

                currentX = px;
                currentY = py;
            }
            else if (pendingX is null)
            {
                pendingX = px;
                pendingY = py;
            }
            else
            {
                double midX = (pendingX.Value + px) / 2.0;
                double midY = (pendingY!.Value + py) / 2.0;
                builder.QuadTo(pendingX.Value, pendingY.Value, midX, midY);
                currentX = midX;
                currentY = midY;
                pendingX = px;
                pendingY = py;
            }
        }

        if (pendingX is not null)
        {
            builder.QuadTo(pendingX.Value, pendingY!.Value, startX, startY);
        }
        else if (Math.Abs(currentX - startX) > 1e-9 || Math.Abs(currentY - startY) > 1e-9)
        {
            builder.LineTo(startX, startY);
        }

        builder.Close();
    }

    private GlyphOutline? BuildComposite(int offset, int depth)
    {
        var builder = new GlyphOutline.Builder();
        int cursor = offset + 10;
        int guard = 0;

        while (guard++ < 64)
        {
            if (cursor + 4 > _data.Length) break;
            int flags = ReadUInt16(cursor);
            int glyphIndex = ReadUInt16(cursor + 2);
            cursor += 4;

            int arg1;
            int arg2;
            if ((flags & 1) != 0)
            {
                arg1 = ReadInt16(cursor);
                arg2 = ReadInt16(cursor + 2);
                cursor += 4;
            }
            else
            {
                arg1 = (sbyte)_data[cursor];
                arg2 = (sbyte)_data[cursor + 1];
                cursor += 2;
            }

            double a = 1;
            double b = 0;
            double c = 0;
            double d = 1;
            if ((flags & 8) != 0)
            {
                a = d = ReadF2Dot14(cursor);
                cursor += 2;
            }
            else if ((flags & 64) != 0)
            {
                a = ReadF2Dot14(cursor);
                d = ReadF2Dot14(cursor + 2);
                cursor += 4;
            }
            else if ((flags & 128) != 0)
            {
                a = ReadF2Dot14(cursor);
                b = ReadF2Dot14(cursor + 2);
                c = ReadF2Dot14(cursor + 4);
                d = ReadF2Dot14(cursor + 6);
                cursor += 8;
            }

            var component = BuildGlyph(glyphIndex, depth + 1);
            double dx = (flags & 2) != 0 ? arg1 : 0;
            double dy = (flags & 2) != 0 ? arg2 : 0;
            if ((flags & 2) == 0)
            {
                // Titik acuan: jarang dipakai; abaikan pergeseran agar tetap aman.
                dx = 0;
                dy = 0;
            }

            if (component is not null)
            {
                AppendTransformed(builder, component, a, b, c, d, dx, dy);
            }

            if ((flags & 32) == 0) break;
        }

        return builder.IsEmpty ? null : builder.Build();
    }

    private static void AppendTransformed(
        GlyphOutline.Builder builder, GlyphOutline outline,
        double a, double b, double c, double d, double dx, double dy)
    {
        double[] commands = outline.Commands;
        int index = 0;
        while (index < commands.Length)
        {
            int command = (int)commands[index++];
            switch (command)
            {
                case 0:
                case 1:
                {
                    double x = commands[index++];
                    double y = commands[index++];
                    double tx = a * x + c * y + dx;
                    double ty = b * x + d * y + dy;
                    if (command == 0) builder.MoveTo(tx, ty);
                    else builder.LineTo(tx, ty);
                    break;
                }

                case 2:
                {
                    double cx = commands[index++];
                    double cy = commands[index++];
                    double x = commands[index++];
                    double y = commands[index++];
                    builder.QuadTo(a * cx + c * cy + dx, b * cx + d * cy + dy, a * x + c * y + dx, b * x + d * y + dy);
                    break;
                }

                case 3:
                {
                    double x1 = commands[index++];
                    double y1 = commands[index++];
                    double x2 = commands[index++];
                    double y2 = commands[index++];
                    double x = commands[index++];
                    double y = commands[index++];
                    builder.CurveTo(
                        a * x1 + c * y1 + dx, b * x1 + d * y1 + dy,
                        a * x2 + c * y2 + dx, b * x2 + d * y2 + dy,
                        a * x + c * y + dx, b * x + d * y + dy);
                    break;
                }

                default:
                    builder.Close();
                    break;
            }
        }
    }

    // ------------------------------------------------------------- Pembacaan

    private int GetInt16(string table, int relative)
    {
        if (!_tables.TryGetValue(table, out var entry)) return 0;
        int position = entry.Offset + relative;
        return position + 2 <= _data.Length ? ReadInt16(position) : 0;
    }

    private int GetUInt16(string table, int relative)
    {
        if (!_tables.TryGetValue(table, out var entry)) return 0;
        int position = entry.Offset + relative;
        return position + 2 <= _data.Length ? ReadUInt16(position) : 0;
    }

    private int ReadInt16(int offset) => offset + 2 <= _data.Length
        ? (short)((_data[offset] << 8) | _data[offset + 1])
        : 0;

    private int ReadUInt16(int offset) => offset + 2 <= _data.Length
        ? (_data[offset] << 8) | _data[offset + 1]
        : 0;

    private uint ReadUInt32(int offset) => offset + 4 <= _data.Length
        ? ((uint)_data[offset] << 24) | ((uint)_data[offset + 1] << 16) | ((uint)_data[offset + 2] << 8) | _data[offset + 3]
        : 0;

    private static uint ReadUInt32(byte[] data, int offset) => offset + 4 <= data.Length
        ? ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3]
        : 0;

    private static int ReadUInt16(byte[] data, int offset) => offset + 2 <= data.Length
        ? (data[offset] << 8) | data[offset + 1]
        : 0;

    private double ReadF2Dot14(int offset) => ReadInt16(offset) / 16384.0;

    // ------------------------------------------------------------- Subset

    /// <summary>Metrik huruf untuk /FontDescriptor PDF, dalam satuan font asli.</summary>
    internal sealed record FontMetrics(
        int XMin, int YMin, int XMax, int YMax, int Ascent, int Descent, int CapHeight, double ItalicAngle);

    internal FontMetrics ReadFontMetrics()
    {
        int xMin = GetInt16("head", 36);
        int yMin = GetInt16("head", 38);
        int xMax = GetInt16("head", 40);
        int yMax = GetInt16("head", 42);
        int ascent = GetInt16("hhea", 4);
        int descent = GetInt16("hhea", 6);

        int capHeight = 0;
        if (_tables.TryGetValue("OS/2", out var os2) && os2.Length >= 90 &&
            os2.Offset + 90 <= _data.Length && ReadUInt16(os2.Offset) >= 2)
        {
            capHeight = ReadInt16(os2.Offset + 88);
        }

        double italicAngle = 0;
        if (_tables.TryGetValue("post", out var post) && post.Length >= 8 && post.Offset + 8 <= _data.Length)
        {
            italicAngle = ReadInt16(post.Offset + 4) / 65536.0;
        }

        return new FontMetrics(xMin, yMin, xMax, yMax, ascent, descent, capHeight, italicAngle);
    }

    /// <summary>
    /// Bangun program TrueType baru yang hanya memuat glif terpakai (beserta
    /// glif kompositnya). Nomor glif TIDAK diubah, sehingga cmap dan rujukan
    /// komposit tetap sah tanpa pemetaan ulang — syarat penting agar teks tidak
    /// berubah bentuk saat dibaca pembaca PDF mana pun. Tabel hinting (cvt/fpgm/
    /// prep/gasp) ikut disalin supaya huruf kecil tetap tajam saat dicetak.
    /// </summary>
    /// <returns>Program font, atau array kosong bila tabel wajib tidak lengkap.</returns>
    internal byte[] BuildSubset(IEnumerable<int> glyphIds)
    {
        if (!_tables.ContainsKey("glyf") || !_tables.ContainsKey("head") ||
            !_tables.ContainsKey("hhea") || !_tables.ContainsKey("hmtx") ||
            !_tables.ContainsKey("maxp") || !_tables.ContainsKey("cmap"))
        {
            return Array.Empty<byte>();
        }

        var dipakai = new HashSet<int>();
        foreach (int gid in glyphIds) TandaiGlif(gid, dipakai, 0);
        dipakai.Add(0);

        var glyf = new MemoryStream();
        var offsets = new int[_glyphCount + 1];
        var glyfEntry = _tables["glyf"];
        for (int gid = 0; gid < _glyphCount; gid++)
        {
            offsets[gid] = (int)glyf.Length;
            if (dipakai.Contains(gid) && gid + 1 < _loca.Length)
            {
                int start = _loca[gid];
                int end = _loca[gid + 1];
                if (start < end && glyfEntry.Offset + end <= _data.Length)
                {
                    glyf.Write(_data, glyfEntry.Offset + start, end - start);
                }
            }

            while ((glyf.Length & 3) != 0) glyf.WriteByte(0);
        }

        offsets[_glyphCount] = (int)glyf.Length;

        var loca = new byte[(_glyphCount + 1) * 4];
        for (int i = 0; i <= _glyphCount; i++)
        {
            uint nilai = (uint)offsets[i];
            int pos = i * 4;
            loca[pos] = (byte)(nilai >> 24);
            loca[pos + 1] = (byte)(nilai >> 16);
            loca[pos + 2] = (byte)(nilai >> 8);
            loca[pos + 3] = (byte)nilai;
        }

        byte[] head = CopyTable("head", 54);
        if (head.Length == 0) return Array.Empty<byte>();
        TulisUInt32(head, 8, 0);
        head[50] = 0;
        head[51] = 1; // loca panjang

        var post = new byte[32];
        if (_tables.TryGetValue("post", out var postEntry) &&
            postEntry.Length >= 32 && postEntry.Offset + 32 <= _data.Length)
        {
            Array.Copy(_data, postEntry.Offset, post, 0, 32);
        }

        post[0] = 0;
        post[1] = 0;
        post[2] = 3;
        post[3] = 0; // versi 3.0: tanpa nama glif
        for (int i = 16; i < 32; i++) post[i] = 0;

        var tabel = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["OS/2"] = CopyTable("OS/2"),
            ["cmap"] = CopyTable("cmap"),
            ["glyf"] = glyf.ToArray(),
            ["head"] = head,
            ["hhea"] = CopyTable("hhea"),
            ["hmtx"] = CopyTable("hmtx"),
            ["loca"] = loca,
            ["maxp"] = CopyTable("maxp"),
            ["name"] = CopyTable("name"),
            ["post"] = post,
        };

        foreach (string tag in new[] { "cvt ", "fpgm", "prep", "gasp" })
        {
            if (_tables.ContainsKey(tag)) tabel[tag] = CopyTable(tag);
        }

        foreach (var isi in tabel)
        {
            if (isi.Value.Length == 0) return Array.Empty<byte>();
        }

        int jumlah = tabel.Count;
        int pangkat = 1;
        int pemilih = 0;
        while (pangkat * 2 <= jumlah)
        {
            pangkat *= 2;
            pemilih++;
        }

        using var output = new MemoryStream();
        TulisUInt32(output, 0x00010000);
        TulisUInt16(output, jumlah);
        TulisUInt16(output, pangkat * 16);
        TulisUInt16(output, pemilih);
        TulisUInt16(output, jumlah * 16 - pangkat * 16);

        int posisi = 12 + jumlah * 16;
        var catatan = new List<(string Tag, int Posisi, int Panjang, uint Checksum)>();
        foreach (var pasangan in tabel)
        {
            byte[] badan = pasangan.Value;
            catatan.Add((pasangan.Key, posisi, badan.Length, HitungChecksum(badan)));
            posisi += (badan.Length + 3) & ~3;
        }

        foreach (var item in catatan)
        {
            byte[] tag = Encoding.ASCII.GetBytes(item.Tag);
            output.Write(tag, 0, 4);
            TulisUInt32(output, item.Checksum);
            TulisUInt32(output, (uint)item.Posisi);
            TulisUInt32(output, (uint)item.Panjang);
        }

        foreach (var item in catatan)
        {
            byte[] badan = tabel[item.Tag];
            output.Write(badan, 0, badan.Length);
            int sisa = ((badan.Length + 3) & ~3) - badan.Length;
            for (int i = 0; i < sisa; i++) output.WriteByte(0);
        }

        byte[] hasil = output.ToArray();
        uint penyesuaian = 0xB1B0AFBA - HitungChecksum(hasil);
        foreach (var item in catatan)
        {
            if (item.Tag != "head") continue;
            TulisUInt32(hasil, item.Posisi + 8, penyesuaian);
            break;
        }

        return hasil;
    }

    /// <summary>Tandai glif ini dan (rekursif) glif komponennya.</summary>
    private void TandaiGlif(int glyphId, HashSet<int> dipakai, int depth)
    {
        if (depth > 8 || glyphId < 0 || glyphId >= _glyphCount || !dipakai.Add(glyphId)) return;
        if (!_tables.TryGetValue("glyf", out var glyf)) return;
        if (glyphId + 1 >= _loca.Length) return;

        int start = _loca[glyphId];
        int end = _loca[glyphId + 1];
        if (start >= end) return;

        int offset = glyf.Offset + start;
        if (offset + 10 > _data.Length || ReadInt16(offset) >= 0) return;

        int cursor = offset + 10;
        while (cursor + 4 <= _data.Length)
        {
            int flags = ReadUInt16(cursor);
            int komponen = ReadUInt16(cursor + 2);
            cursor += 4;
            cursor += (flags & 0x0001) != 0 ? 4 : 2;
            if ((flags & 0x0008) != 0) cursor += 2;
            else if ((flags & 0x0040) != 0) cursor += 4;
            else if ((flags & 0x0080) != 0) cursor += 8;

            TandaiGlif(komponen, dipakai, depth + 1);
            if ((flags & 0x0020) == 0) break;
        }
    }

    private byte[] CopyTable(string tag, int panjangMinimum = 0)
    {
        if (!_tables.TryGetValue(tag, out var entry) || entry.Length < panjangMinimum) return Array.Empty<byte>();
        if (entry.Offset < 0 || entry.Offset + entry.Length > _data.Length) return Array.Empty<byte>();

        var hasil = new byte[entry.Length];
        Array.Copy(_data, entry.Offset, hasil, 0, entry.Length);
        return hasil;
    }

    private static void TulisUInt16(Stream stream, int nilai)
    {
        stream.WriteByte((byte)(nilai >> 8));
        stream.WriteByte((byte)nilai);
    }

    private static void TulisUInt32(Stream stream, uint nilai)
    {
        stream.WriteByte((byte)(nilai >> 24));
        stream.WriteByte((byte)(nilai >> 16));
        stream.WriteByte((byte)(nilai >> 8));
        stream.WriteByte((byte)nilai);
    }

    private static void TulisUInt32(byte[] data, int offset, uint nilai)
    {
        data[offset] = (byte)(nilai >> 24);
        data[offset + 1] = (byte)(nilai >> 16);
        data[offset + 2] = (byte)(nilai >> 8);
        data[offset + 3] = (byte)nilai;
    }

    private static uint HitungChecksum(byte[] data)
    {
        uint jumlah = 0;
        int i = 0;
        for (; i + 4 <= data.Length; i += 4)
        {
            jumlah += ((uint)data[i] << 24) | ((uint)data[i + 1] << 16) | ((uint)data[i + 2] << 8) | data[i + 3];
        }

        if (i < data.Length)
        {
            uint terakhir = 0;
            int sisa = data.Length - i;
            for (int j = 0; j < 4; j++)
            {
                terakhir = (terakhir << 8) | (j < sisa ? data[i + j] : 0u);
            }

            jumlah += terakhir;
        }

        return jumlah;
    }

    /// <summary>Nama glif standar Macintosh (258 entri, spesifikasi TrueType).</summary>
    private static readonly string[] StandardMacGlyphNames =
    {
        ".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign", "dollar",
        "percent", "ampersand", "quotesingle", "parenleft", "parenright", "asterisk", "plus", "comma",
        "hyphen", "period", "slash", "zero", "one", "two", "three", "four",
        "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less",
        "equal", "greater", "question", "at", "A", "B", "C", "D",
        "E", "F", "G", "H", "I", "J", "K", "L",
        "M", "N", "O", "P", "Q", "R", "S", "T",
        "U", "V", "W", "X", "Y", "Z", "bracketleft", "backslash",
        "bracketright", "asciicircum", "underscore", "grave", "a", "b", "c", "d",
        "e", "f", "g", "h", "i", "j", "k", "l",
        "m", "n", "o", "p", "q", "r", "s", "t",
        "u", "v", "w", "x", "y", "z", "braceleft", "bar",
        "braceright", "asciitilde", "Adieresis", "Aring", "Ccedilla", "Eacute", "Ntilde", "Odieresis",
        "Udieresis", "aacute", "agrave", "acircumflex", "adieresis", "atilde", "aring", "ccedilla",
        "eacute", "egrave", "ecircumflex", "edieresis", "iacute", "igrave", "icircumflex", "idieresis",
        "ntilde", "oacute", "ograve", "ocircumflex", "odieresis", "otilde", "uacute", "ugrave",
        "ucircumflex", "udieresis", "dagger", "degree", "cent", "sterling", "section", "bullet",
        "paragraph", "germandbls", "registered", "copyright", "trademark", "acute", "dieresis", "notequal",
        "AE", "Oslash", "infinity", "plusminus", "lessequal", "greaterequal", "yen", "mu",
        "partialdiff", "summation", "product", "pi", "integral", "ordfeminine", "ordmasculine", "Omega",
        "ae", "oslash", "questiondown", "exclamdown", "logicalnot", "radical", "florin", "approxequal",
        "Delta", "guillemotleft", "guillemotright", "ellipsis", "nonbreakingspace", "Agrave", "Atilde", "Otilde",
        "OE", "oe", "endash", "emdash", "quotedblleft", "quotedblright", "quoteleft", "quoteright",
        "divide", "lozenge", "ydieresis", "Ydieresis", "fraction", "currency", "guilsinglleft", "guilsinglright",
        "fi", "fl", "daggerdbl", "periodcentered", "quotesinglbase", "quotedblbase", "perthousand", "Acircumflex",
        "Ecircumflex", "Aacute", "Edieresis", "Egrave", "Iacute", "Icircumflex", "Idieresis", "Igrave",
        "Oacute", "Ocircumflex", "apple", "Ograve", "Uacute", "Ucircumflex", "Ugrave", "dotlessi",
        "circumflex", "tilde", "macron", "breve", "dotaccent", "ring", "cedilla", "hungarumlaut",
        "ogonek", "caron", "Lslash", "lslash", "Scaron", "scaron", "Zcaron", "zcaron",
        "brokenbar", "Eth", "eth", "Yacute", "yacute", "Thorn", "thorn", "minus",
        "multiply", "onesuperior", "twosuperior", "threesuperior", "onehalf", "onequarter", "threequarters", "franc",
        "Gbreve", "gbreve", "Idotaccent", "Scedilla", "scedilla", "Cacute", "cacute", "Ccaron",
        "ccaron", "dcroat"
    };
}
