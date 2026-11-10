using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

public sealed partial class TrueTypeFont : IPdfGlyphSource
{
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


}
