using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

public sealed partial class CffFont : IPdfGlyphSource
{
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


}
