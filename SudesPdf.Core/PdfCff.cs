using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>
/// Pengurai font CFF (Compact Font Format, ISO/IEC 14496-22 Lampiran C) dengan
/// penafsir charstring Type2. Mencakup font berkunci nama (Type1C) maupun
/// berkunci CID (CIDFontType0C) yang tersemat sebagai /FontFile3.
/// </summary>
public sealed class CffFont : IPdfGlyphSource
{
    private readonly byte[] _data;
    private readonly List<Range> _charStrings = new();
    private readonly List<PrivateDict> _privat = new();
    private readonly List<int?> _gidToPrivate = new();
    private readonly Dictionary<string, int> _nameToGlyph = new(StringComparer.Ordinal);
    private readonly Dictionary<int, GlyphOutline?> _outlines = new();
    private readonly int _globalSubrBias;
    private readonly Index _globalSubrs;

    private CffFont(byte[] data)
    {
        _data = data;
        int headerSize = data[2];
        int cursor = headerSize;
        _ = ReadIndex(ref cursor);           // Name INDEX
        var topDictIndex = ReadIndex(ref cursor);
        ReadIndex(ref cursor);               // String INDEX
        _globalSubrs = ReadIndex(ref cursor);
        _globalSubrBias = SubrBias(_globalSubrs.Count);

        if (topDictIndex.Count == 0) throw new PdfException("Font CFF tidak memiliki kamus utama.");
        var top = ReadDict(topDictIndex.At(0));

        int charStringsOffset = (int)(top.Get("CharStrings") ?? 0);
        var programCursor = charStringsOffset;
        var charStrings = ReadIndex(ref programCursor);
        for (int i = 0; i < charStrings.Count; i++)
        {
            _charStrings.Add(charStrings.At(i));
        }

        bool isCid = top.Has("ROS");
        int privateSize = (int)(top.Get("Private") ?? 0);
        int privateOffset = (int)(top.Get("Private", 1) ?? 0);
        _privat.Add(privateOffset > 0 && privateOffset < _data.Length
            ? ReadPrivate(privateOffset, privateSize)
            : new PrivateDict());

        if (isCid)
        {
            ReadCidLayout(top);
        }
        else
        {
            ReadCharset(top, charStrings.Count);
            for (int gid = 0; gid < _charStrings.Count; gid++) _gidToPrivate.Add(0);
        }
    }

    public double UnitsPerEm => 1000;

    public int GlyphCount => _charStrings.Count;

    public static CffFont? Parse(byte[] data, string? subtype)
    {
        if (data.Length < 4) return null;
        if (subtype is "CIDFontType0C" or "CIDFontType0" or "Type1C" or "OpenType")
        {
            // Lanjut: isi berkas diperiksa sendiri oleh pengurai.
        }

        try
        {
            return new CffFont(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // --------------------------------------------------------------- Struktur

    private void ReadCidLayout(CffDict top)
    {
        int fdArrayOffset = (int)(top.Get("FDArray") ?? 0);
        int fdSelectOffset = (int)(top.Get("FDSelect") ?? 0);
        var fontDicts = new List<CffDict>();

        if (fdArrayOffset > 0 && fdArrayOffset < _data.Length)
        {
            int cursor = fdArrayOffset;
            var index = ReadIndex(ref cursor);
            for (int i = 0; i < index.Count; i++)
            {
                var dict = ReadDict(index.At(i));
                fontDicts.Add(dict);
                int size = (int)(dict.Get("Private") ?? 0);
                int offset = (int)(dict.Get("Private", 1) ?? 0);
                _privat.Add(offset > 0 && offset < _data.Length ? ReadPrivate(offset, size) : _privat[0]);
            }
        }

        var fdSelect = new int[_charStrings.Count];
        if (fdSelectOffset > 0 && fdSelectOffset < _data.Length)
        {
            ReadFdSelect(fdSelectOffset, fdSelect);
        }

        for (int gid = 0; gid < _charStrings.Count; gid++)
        {
            _gidToPrivate.Add(Math.Clamp(fdSelect[gid], 0, Math.Max(0, _privat.Count - 1)));
            _nameToGlyph.TryAdd(gid.ToString(), gid);
        }
    }

    private void ReadFdSelect(int offset, int[] select)
    {
        int format = _data[offset];
        if (format == 0)
        {
            for (int gid = 0; gid < select.Length; gid++)
            {
                int position = offset + 1 + gid;
                select[gid] = position < _data.Length ? _data[position] : 0;
            }

            return;
        }

        if (format != 3) return;
        int ranges = ReadUInt16(offset + 1);
        int cursor = offset + 3;
        for (int i = 0; i < ranges; i++)
        {
            if (cursor + 3 > _data.Length) break;
            int first = ReadUInt16(cursor);
            int fd = _data[cursor + 2];
            int next = i + 1 < ranges ? ReadUInt16(cursor + 3) : select.Length;
            for (int gid = first; gid < next && gid < select.Length; gid++) select[gid] = fd;
            cursor += 3;
        }
    }

    private void ReadCharset(CffDict top, int glyphCount)
    {
        int charsetOffset = (int)(top.Get("charset") ?? 0);
        var sidToGid = new Dictionary<int, int>();
        var gidToSid = new int[glyphCount];

        if (charsetOffset is 0 or 1 or 2)
        {
            for (int gid = 1; gid < glyphCount; gid++) gidToSid[gid] = gid;
        }
        else if (charsetOffset > 2 && charsetOffset < _data.Length)
        {
            ReadCharsetTable(charsetOffset, glyphCount, gidToSid);
        }
        else
        {
            for (int gid = 1; gid < glyphCount; gid++) gidToSid[gid] = gid;
        }

        for (int gid = 1; gid < glyphCount; gid++)
        {
            string? name = StandardString(gidToSid[gid]);
            if (name is not null)
            {
                sidToGid.TryAdd(gidToSid[gid], gid);
                _nameToGlyph.TryAdd(name, gid);
            }
        }
    }

    private void ReadCharsetTable(int offset, int glyphCount, int[] gidToSid)
    {
        int format = _data[offset];
        int cursor = offset + 1;
        int gid = 1;
        switch (format)
        {
            case 0:
                while (gid < glyphCount && cursor + 2 <= _data.Length)
                {
                    gidToSid[gid++] = ReadUInt16(cursor);
                    cursor += 2;
                }

                break;
            case 1:
                // Rentang format 1: SID awal + jumlah sisa (1 byte).
                while (gid < glyphCount && cursor + 3 <= _data.Length)
                {
                    int first = ReadUInt16(cursor);
                    cursor += 2;
                    int count = _data[cursor++];
                    for (int i = 0; i <= count && gid < glyphCount; i++) gidToSid[gid++] = first + i;
                }

                break;
            case 2:
                // Rentang format 2: SID awal + jumlah sisa (2 byte).
                while (gid < glyphCount && cursor + 4 <= _data.Length)
                {
                    int first = ReadUInt16(cursor);
                    cursor += 2;
                    int count = ReadUInt16(cursor);
                    cursor += 2;
                    for (int i = 0; i <= count && gid < glyphCount; i++) gidToSid[gid++] = first + i;
                }

                break;
        }
    }

    private PrivateDict ReadPrivate(int offset, int size)
    {
        var dict = new PrivateDict();
        if (offset + size > _data.Length || size <= 0) return dict;
        var parsed = ReadDict(new Range(offset, size));
        dict.DefaultWidthX = parsed.Get("defaultWidthX") ?? 0;
        dict.NominalWidthX = parsed.Get("nominalWidthX") ?? 0;
        int subrs = (int)(parsed.Get("Subrs") ?? 0);
        if (subrs > 0)
        {
            int cursor = offset + subrs;
            dict.Subrs = ReadIndex(ref cursor);
        }

        return dict;
    }

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

    // ------------------------------------------------------------- Pembacaan

    private Index ReadIndex(ref int cursor)
    {
        if (cursor + 2 > _data.Length) return new Index();
        int count = ReadUInt16(cursor);
        cursor += 2;
        if (count == 0) return new Index();
        if (cursor >= _data.Length) return new Index();

        int offSize = _data[cursor++];
        if (offSize is < 1 or > 4) throw new PdfException("Panjang offset CFF tidak sah.");
        int offsetsStart = cursor;
        cursor += (count + 1) * offSize;
        int dataStart = cursor;

        var offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int position = offsetsStart + i * offSize;
            int value = 0;
            for (int b = 0; b < offSize; b++)
            {
                value = (value << 8) | (position + b < _data.Length ? _data[position + b] : 0);
            }

            offsets[i] = value - 1;
        }

        var index = new Index { DataStart = dataStart, Count = count };
        index.Offsets.AddRange(offsets);
        return index;
    }

    private CffDict ReadDict(Range range)
    {
        var dict = new CffDict();
        var operands = new List<double>();
        int start = range.Start;
        int limit = Math.Min(_data.Length, range.Start + range.Length);
        for (int index = start; index < limit;)
        {
            int b0 = _data[index];
            if (b0 <= 21)
            {
                int operation = b0;
                index++;
                if (b0 == 12)
                {
                    if (index >= limit) break;
                    operation = 1200 + _data[index++];
                }

                for (int i = 0; i < operands.Count; i++)
                {
                    dict.Set(operation, i, operands[i]);
                }

                operands.Clear();
                continue;
            }

            if (b0 == 28)
            {
                if (index + 3 > limit) break;
                operands.Add((short)((_data[index + 1] << 8) | _data[index + 2]));
                index += 3;
                continue;
            }

            if (b0 == 29)
            {
                if (index + 5 > limit) break;
                int value = (_data[index + 1] << 24) | (_data[index + 2] << 16) | (_data[index + 3] << 8) | _data[index + 4];
                operands.Add(value);
                index += 5;
                continue;
            }

            if (b0 == 30)
            {
                index++;
                double value = 0;
                bool negative = false;
                bool building = false;
                double fraction = 0.1;
                while (index < limit)
                {
                    int nibble = _data[index++];
                    for (int half = 0; half < 2; half++)
                    {
                        int digit = half == 0 ? (nibble >> 4) : (nibble & 0x0F);
                        if (digit <= 9)
                        {
                            if (building)
                            {
                                value += digit * fraction;
                                fraction /= 10;
                            }
                            else
                            {
                                value = value * 10 + digit;
                            }

                            continue;
                        }

                        switch (digit)
                        {
                            case 0xA: value += 0; building = true; break;
                            case 0xB: value += fraction * 1; fraction /= 10; break;
                            case 0xC: value += fraction * 2; fraction /= 10; break;
                            case 0xD: value += fraction * 3; fraction /= 10; break;
                            case 0xE: negative = true; break;
                            case 0xF: goto done;
                        }
                    }
                }

            done:
                operands.Add(negative ? -value : value);
                continue;
            }

            if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                index++;
                continue;
            }

            if (b0 >= 247 && b0 <= 250)
            {
                if (index + 2 > limit) break;
                operands.Add((b0 - 247) * 256 + _data[index + 1] + 108);
                index += 2;
                continue;
            }

            if (b0 >= 251 && b0 <= 254)
            {
                if (index + 2 > limit) break;
                operands.Add(-(b0 - 251) * 256 - _data[index + 1] - 108);
                index += 2;
                continue;
            }

            index++;
        }

        return dict;
    }

    private int ReadUInt16(int offset) => offset + 2 <= _data.Length
        ? (_data[offset] << 8) | _data[offset + 1]
        : 0;

    private static int SubrBias(int count) => count switch
    {
        < 1240 => 107,
        < 33900 => 1131,
        _ => 32768
    };

    // --------------------------------------------------------- Tipe bantu

    private readonly struct Range
    {
        public Range(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }

    private sealed class Index
    {
        public List<int> Offsets { get; } = new();
        public int DataStart { get; set; }
        public int Count { get; set; }

        /// <summary>Rentang data entri ke-<paramref name="index"/>.</summary>
        public Range At(int index)
        {
            if (index < 0 || index + 1 >= Offsets.Count) return default;
            int start = DataStart + Offsets[index];
            int end = DataStart + Offsets[index + 1];
            return new Range(start, Math.Max(0, end - start));
        }
    }

    private sealed class CffDict
    {
        private readonly Dictionary<int, double[]> _values = new();

        public bool Has(string key) => _values.ContainsKey(KeyOf(key));

        public void Set(int operation, int operandIndex, double value)
        {
            if (!_values.TryGetValue(operation, out var array))
            {
                array = new double[8];
                _values[operation] = array;
            }

            if (operandIndex < array.Length) array[operandIndex] = value;
        }

        public double? Get(string key, int index = 0)
        {
            int operation = KeyOf(key);
            if (!_values.TryGetValue(operation, out var array)) return null;
            return index < array.Length ? array[index] : null;
        }

        private static int KeyOf(string key) => key switch
        {
            "charset" => 15,
            "Encoding" => 16,
            "CharStrings" => 17,
            "Private" => 18,
            "Subrs" => 19,
            "ROS" => 1230,
            "FDArray" => 1236,
            "FDSelect" => 1237,
            "defaultWidthX" => 20,
            "nominalWidthX" => 21,
            _ => -1
        };
    }

    private sealed class PrivateDict
    {
        public double DefaultWidthX { get; set; }

        public double NominalWidthX { get; set; }

        public Index? Subrs { get; set; }
    }

    /// <summary>Penafsir charstring Type2 (ISO/IEC 14496-22 §C.5).</summary>
    private sealed class CharstringRunner
    {
        private readonly CffFont _font;
        private readonly PrivateDict _private;
        private readonly GlyphOutline.Builder _builder;
        private readonly double[] _stack = new double[96];
        private readonly double[] _transient = new double[32];
        private int _sp;
        private double _x;
        private double _y;
        private int _hints;
        private bool _widthParsed;
        private bool _open;

        public CharstringRunner(CffFont font, PrivateDict privateDict, GlyphOutline.Builder builder)
        {
            _font = font;
            _private = privateDict;
            _builder = builder;
        }

        public void Run(int start, int end, int depth)
        {
            if (depth > 10) return;
            int index = start;
            while (index < end)
            {
                int b0 = _font._data[index];
                if (b0 >= 32 || b0 == 28 || b0 == 255)
                {
                    if (!ReadOperand(ref index, end)) return;
                    continue;
                }

                index++;
                switch (b0)
                {
                    case 1:
                    case 3:
                    case 18:
                    case 23:
                        CountStems();
                        break;
                    case 19:
                    case 20:
                        CountStems();
                        index += (_hints + 7) / 8;
                        break;
                    case 21:
                        ParseWidth(2);
                        Move(_stack[_sp - 2], _stack[_sp - 1]);
                        _sp = 0;
                        break;
                    case 22:
                        ParseWidth(1);
                        Move(_stack[_sp - 1], 0);
                        _sp = 0;
                        break;
                    case 4:
                        ParseWidth(1);
                        Move(0, _stack[_sp - 1]);
                        _sp = 0;
                        break;
                    case 5:
                        for (int i = 0; i + 1 < _sp; i += 2) Line(_stack[i], _stack[i + 1]);
                        _sp = 0;
                        break;
                    case 6:
                    case 7:
                    {
                        bool horizontal = b0 == 6;
                        for (int i = 0; i < _sp; i++)
                        {
                            if (horizontal) Line(_stack[i], 0);
                            else Line(0, _stack[i]);
                            horizontal = !horizontal;
                        }

                        _sp = 0;
                        break;
                    }

                    case 8:
                        for (int i = 0; i + 5 < _sp; i += 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    case 24:
                    {
                        int i = 0;
                        for (; i + 5 < _sp - 2; i += 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        if (_sp - i >= 2) Line(_stack[i], _stack[i + 1]);
                        _sp = 0;
                        break;
                    }

                    case 25:
                    {
                        int i = 0;
                        for (; i + 1 < _sp - 6; i += 2) Line(_stack[i], _stack[i + 1]);
                        if (_sp - i >= 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    }

                    case 26:
                    {
                        int i = 0;
                        double dx1 = _sp % 4 == 1 ? _stack[i++] : 0;
                        double dy1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4)
                        {
                            Curve(dx1, dy1, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3]);
                            dx1 = 0;
                            dy1 = 0;
                        }

                        _sp = 0;
                        break;
                    }

                    case 27:
                    {
                        int i = 0;
                        double dy1 = _sp % 4 == 1 ? _stack[i++] : 0;
                        double dx1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4)
                        {
                            Curve(dx1, dy1, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3]);
                            dx1 = 0;
                            dy1 = 0;
                        }

                        _sp = 0;
                        break;
                    }

                    case 30:
                    case 31:
                    {
                        bool horizontal = b0 == 31;
                        int i = 0;
                        while (_sp - i >= 4)
                        {
                            if (horizontal)
                            {
                                double dxa = _stack[i];
                                double dxb = _stack[i + 1];
                                double dyb = _stack[i + 2];
                                int remaining = _sp - i;
                                double dyc = remaining == 5 ? _stack[i + 4] : 0;
                                double dxc = remaining == 5 ? 0 : _stack[i + 3];
                                Curve(dxa, 0, dxb, dyb, dxc, dyc);
                            }
                            else
                            {
                                double dya = _stack[i];
                                double dxb = _stack[i + 1];
                                double dyb = _stack[i + 2];
                                int remaining = _sp - i;
                                double dxc = remaining == 5 ? _stack[i + 4] : 0;
                                double dyc = remaining == 5 ? 0 : _stack[i + 3];
                                Curve(0, dya, dxb, dyb, dxc, dyc);
                            }

                            horizontal = !horizontal;
                            i += 4;
                        }

                        _sp = 0;
                        break;
                    }

                    case 10:
                        CallSubr(_private.Subrs, SubrBias(_private.Subrs?.Count ?? 0), depth);
                        break;
                    case 29:
                        CallSubr(_font._globalSubrs, _font._globalSubrBias, depth);
                        break;
                    case 11:
                        return;
                    case 14:
                        ParseWidth(0);
                        _sp = 0;
                        CloseContour();
                        return;
                    case 12:
                    {
                        if (index >= end) return;
                        int operation = _font._data[index++];
                        ExecuteEscape(operation);
                        break;
                    }

                    default:
                        _sp = 0;
                        break;
                }
            }
        }

        private bool ReadOperand(ref int index, int end)
        {
            int b0 = _font._data[index];
            if (b0 == 28)
            {
                if (index + 3 > end) return false;
                Push((short)((_font._data[index + 1] << 8) | _font._data[index + 2]));
                index += 3;
                return true;
            }

            if (b0 == 255)
            {
                if (index + 5 > end) return false;
                int value = (_font._data[index + 1] << 24) | (_font._data[index + 2] << 16)
                            | (_font._data[index + 3] << 8) | _font._data[index + 4];
                Push(value / 65536.0);
                index += 5;
                return true;
            }

            if (b0 >= 32 && b0 <= 246)
            {
                Push(b0 - 139);
                index++;
                return true;
            }

            if (b0 >= 247 && b0 <= 250)
            {
                if (index + 2 > end) return false;
                Push((b0 - 247) * 256 + _font._data[index + 1] + 108);
                index += 2;
                return true;
            }

            if (b0 >= 251 && b0 <= 254)
            {
                if (index + 2 > end) return false;
                Push(-(b0 - 251) * 256 - _font._data[index + 1] - 108);
                index += 2;
                return true;
            }

            return false;
        }

        private void Push(double value)
        {
            if (_sp < _stack.Length) _stack[_sp++] = value;
        }

        private void ParseWidth(int expectedArgs)
        {
            if (!_widthParsed)
            {
                _widthParsed = true;
                if (_sp > expectedArgs)
                {
                    // Argumen pertama adalah lebar (defaultWidthX/nominalWidthX);
                    // lebar tidak dipakai karena PDF punya /Widths sendiri.
                    for (int i = 1; i < _sp; i++) _stack[i - 1] = _stack[i];
                    _sp--;
                }
            }
        }

        private void CountStems()
        {
            if (!_widthParsed)
            {
                _widthParsed = true;
                if (_sp % 2 == 1)
                {
                    for (int i = 1; i < _sp; i++) _stack[i - 1] = _stack[i];
                    _sp--;
                }
            }

            _hints += _sp / 2;
            _sp = 0;
        }

        private void Move(double dx, double dy)
        {
            CloseContour();
            _x += dx;
            _y += dy;
            _builder.MoveTo(_x, _y);
            _open = true;
        }

        private void Line(double dx, double dy)
        {
            if (!_open) Move(0, 0);
            _x += dx;
            _y += dy;
            _builder.LineTo(_x, _y);
        }

        private void Curve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            if (!_open) Move(0, 0);
            double c1x = _x + dx1;
            double c1y = _y + dy1;
            double c2x = c1x + dx2;
            double c2y = c1y + dy2;
            _x = c2x + dx3;
            _y = c2y + dy3;
            _builder.CurveTo(c1x, c1y, c2x, c2y, _x, _y);
        }

        private void CloseContour()
        {
            if (!_open) return;
            _builder.Close();
            _open = false;
        }

        private void CallSubr(Index? index, int bias, int depth)
        {
            if (index is null || _sp < 1) return;
            int subrNumber = (int)_stack[--_sp] + bias;
            if (subrNumber < 0 || subrNumber >= index.Count) return;
            var range = index.At(subrNumber);
            if (range.Length <= 0) return;
            Run(range.Start, range.Start + range.Length, depth + 1);
        }

        private void ExecuteEscape(int operation)
        {
            switch (operation)
            {
                case 34: // hflex
                    if (_sp >= 7)
                    {
                        double dx1 = _stack[0], dx2 = _stack[1], dx3 = _stack[2], dx4 = _stack[3];
                        double dx5 = _stack[4], dx6 = _stack[5];
                        Curve(dx1, 0, dx2, 0, dx3, 0);
                        Curve(dx4, 0, dx5, 0, dx6, 0);
                    }

                    break;
                case 35: // flex
                    if (_sp >= 13)
                    {
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        Curve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                    }

                    break;
                case 36: // hflex1
                    if (_sp >= 9)
                    {
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                        Curve(_stack[5], 0, _stack[6], 0, _stack[7], _stack[8]);
                    }

                    break;
                case 37: // flex1
                    if (_sp >= 11)
                    {
                        double dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                        double dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        Curve(_stack[6], _stack[7], _stack[8], _stack[9],
                            Math.Abs(dx) > Math.Abs(dy) ? _stack[10] : 0,
                            Math.Abs(dx) > Math.Abs(dy) ? 0 : _stack[10]);
                    }

                    break;
                case 3: Binary((a, b) => a != 0 && b != 0 ? 1 : 0); break;
                case 4: Binary((a, b) => a != 0 || b != 0 ? 1 : 0); break;
                case 5: Unary(a => a == 0 ? 1 : 0); break;
                case 9: Unary(Math.Abs); break;
                case 10: Binary((a, b) => a + b); break;
                case 11: Binary((a, b) => a - b); break;
                case 12: Binary((a, b) => b == 0 ? 0 : a / b); break;
                case 14: Unary(a => -a); break;
                case 15: Binary((a, b) => a == b ? 1 : 0); break;
                case 18: if (_sp > 0) _sp--; break;
                case 20:
                    if (_sp >= 2)
                    {
                        int slot = (int)_stack[--_sp];
                        double value = _stack[--_sp];
                        if (slot >= 0 && slot < _transient.Length) _transient[slot] = value;
                    }

                    break;
                case 21:
                    if (_sp >= 1)
                    {
                        int slot = (int)_stack[_sp - 1];
                        _stack[_sp - 1] = slot >= 0 && slot < _transient.Length ? _transient[slot] : 0;
                    }

                    break;
                case 22:
                    if (_sp >= 4)
                    {
                        double s2 = _stack[--_sp];
                        double s1 = _stack[--_sp];
                        double v2 = _stack[--_sp];
                        double v1 = _stack[--_sp];
                        Push(s1 <= s2 ? v1 : v2);
                    }

                    break;
                case 23: Push(Random.Shared.NextDouble()); break;
                case 24: Binary((a, b) => a * b); break;
                case 26: Unary(a => a < 0 ? 0 : Math.Sqrt(a)); break;
                case 27: if (_sp > 0) Push(_stack[_sp - 1]); break;
                case 28:
                    if (_sp >= 2)
                    {
                        (_stack[_sp - 1], _stack[_sp - 2]) = (_stack[_sp - 2], _stack[_sp - 1]);
                    }

                    break;
                case 29:
                    if (_sp >= 1)
                    {
                        int offset = (int)_stack[_sp - 1];
                        int source = _sp - 2 - offset;
                        _stack[_sp - 1] = source >= 0 && source < _sp - 1 ? _stack[source] : 0;
                    }

                    break;
                case 30:
                    if (_sp >= 2)
                    {
                        int times = (int)_stack[_sp - 1];
                        int count = (int)_stack[_sp - 2];
                        _sp -= 2;
                        if (count > 0 && count <= _sp)
                        {
                            times = ((times % count) + count) % count;
                            for (int round = 0; round < times; round++)
                            {
                                double last = _stack[_sp - 1];
                                for (int i = _sp - 1; i > _sp - count; i--) _stack[i] = _stack[i - 1];
                                _stack[_sp - count] = last;
                            }
                        }
                    }

                    break;
            }
        }

        private void Unary(Func<double, double> operation)
        {
            if (_sp < 1) return;
            _stack[_sp - 1] = operation(_stack[_sp - 1]);
        }

        private void Binary(Func<double, double, double> operation)
        {
            if (_sp < 2) return;
            double b = _stack[--_sp];
            double a = _stack[--_sp];
            Push(operation(a, b));
        }
    }

    /// <summary>Nama standar CFF (SID 0..390) — dipakai memetakan nama glif PDF.</summary>
    internal static string? StandardString(int sid) =>
        sid >= 0 && sid < StandardStrings.Length ? StandardStrings[sid] : null;

    internal static int StandardStringCount => StandardStrings.Length;

    private static readonly string[] StandardStrings =
    {
        ".notdef", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand",
        "quoteright", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period",
        "slash", "zero", "one", "two", "three", "four", "five", "six",
        "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater",
        "question", "at", "A", "B", "C", "D", "E", "F",
        "G", "H", "I", "J", "K", "L", "M", "N",
        "O", "P", "Q", "R", "S", "T", "U", "V",
        "W", "X", "Y", "Z", "bracketleft", "backslash", "bracketright", "asciicircum",
        "underscore", "quoteleft", "a", "b", "c", "d", "e", "f",
        "g", "h", "i", "j", "k", "l", "m", "n",
        "o", "p", "q", "r", "s", "t", "u", "v",
        "w", "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
        "exclamdown", "cent", "sterling", "fraction", "yen", "florin", "section", "currency",
        "quotesingle", "quotedblleft", "guillemotleft", "guilsinglleft", "guilsinglright", "fi", "fl", "endash",
        "dagger", "daggerdbl", "periodcentered", "paragraph", "bullet", "quotesinglbase", "quotedblbase", "quotedblright",
        "guillemotright", "ellipsis", "perthousand", "questiondown", "grave", "acute", "circumflex", "tilde",
        "macron", "breve", "dotaccent", "dieresis", "ring", "cedilla", "hungarumlaut", "ogonek",
        "caron", "emdash", "AE", "ordfeminine", "Lslash", "Oslash", "OE", "ordmasculine",
        "ae", "dotlessi", "lslash", "oslash", "oe", "germandbls", "onesuperior", "logicalnot",
        "mu", "trademark", "Eth", "onehalf", "plusminus", "Thorn", "onequarter", "divide",
        "brokenbar", "degree", "thorn", "threequarters", "twosuperior", "registered", "minus", "eth",
        "multiply", "threesuperior", "copyright", "Aacute", "Acircumflex", "Adieresis", "Agrave", "Aring",
        "Atilde", "Ccedilla", "Eacute", "Ecircumflex", "Edieresis", "Egrave", "Iacute", "Icircumflex",
        "Idieresis", "Igrave", "Ntilde", "Oacute", "Ocircumflex", "Odieresis", "Ograve", "Otilde",
        "Scaron", "Uacute", "Ucircumflex", "Udieresis", "Ugrave", "Yacute", "Ydieresis", "Zcaron",
        "aacute", "acircumflex", "adieresis", "agrave", "aring", "atilde", "ccedilla", "eacute",
        "ecircumflex", "edieresis", "egrave", "iacute", "icircumflex", "idieresis", "igrave", "ntilde",
        "oacute", "ocircumflex", "odieresis", "ograve", "otilde", "scaron", "uacute", "ucircumflex",
        "udieresis", "ugrave", "yacute", "ydieresis", "zcaron", "exclamsmall", "Hungarumlautsmall", "dollaroldstyle",
        "dollarsuperior", "ampersandsmall", "Acutesmall", "parenleftsuperior", "parenrightsuperior", "twodotenleader", "onedotenleader", "zerooldstyle",
        "oneoldstyle", "twooldstyle", "threeoldstyle", "fouroldstyle", "fiveoldstyle", "sixoldstyle", "sevenoldstyle", "eightoldstyle",
        "nineoldstyle", "commasuperior", "threequartersemdash", "periodsuperior", "questionsmall", "asuperior", "bsuperior", "centsuperior",
        "dsuperior", "esuperior", "isuperior", "lsuperior", "msuperior", "nsuperior", "osuperior", "rsuperior",
        "ssuperior", "tsuperior", "ff", "ffi", "ffl", "parenleftinferior", "parenrightinferior", "Circumflexsmall",
        "hyphensuperior", "Gravesmall", "Asmall", "Bsmall", "Csmall", "Dsmall", "Esmall", "Fsmall",
        "Gsmall", "Hsmall", "Ismall", "Jsmall", "Ksmall", "Lsmall", "Msmall", "Nsmall",
        "Osmall", "Psmall", "Qsmall", "Rsmall", "Ssmall", "Tsmall", "Usmall", "Vsmall",
        "Wsmall", "Xsmall", "Ysmall", "Zsmall", "colonmonetary", "onefitted", "rupiah", "Tildesmall",
        "exclamdownsmall", "centoldstyle", "Lslashsmall", "Scaronsmall", "Zcaronsmall", "Dieresissmall", "Brevesmall", "Caronsmall",
        "Dotaccentsmall", "Macronsmall", "figuredash", "hypheninferior", "Ogoneksmall", "Ringsmall", "Cedillasmall", "questiondownsmall",
        "oneeighth", "threeeighths", "fiveeighths", "seveneighths", "onethird", "twothirds", "zerosuperior", "foursuperior",
        "fivesuperior", "sixsuperior", "sevensuperior", "eightsuperior", "ninesuperior", "zeroinferior", "oneinferior", "twoinferior",
        "threeinferior", "fourinferior", "fiveinferior", "sixinferior", "seveninferior", "eightinferior", "nineinferior", "centinferior",
        "dollarinferior", "periodinferior", "commainferior", "Agravesmall", "Aacutesmall", "Acircumflexsmall", "Atildesmall", "Adieresissmall",
        "Aringsmall", "AEsmall", "Ccedillasmall", "Egravesmall", "Eacutesmall", "Ecircumflexsmall", "Edieresissmall", "Igravesmall",
        "Iacutesmall", "Icircumflexsmall", "Idieresissmall", "Ethsmall", "Ntildesmall", "Ogravesmall", "Oacutesmall", "Ocircumflexsmall",
        "Otildesmall", "Odieresissmall", "OEsmall", "Oslashsmall", "Ugravesmall", "Uacutesmall", "Ucircumflexsmall", "Udieresissmall",
        "Yacutesmall", "Thornsmall", "Ydieresissmall", "001.000", "001.001", "001.002", "001.003", "Black",
        "Bold", "Book", "Light", "Medium", "Regular", "Roman", "Semibold"
    };
}
