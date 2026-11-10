using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Pengurai font TrueType (ISO/IEC 14496-22): tabel glyf/loca/cmap/post.
/// Dipakai untuk mengambil bentuk glif dari FontFile2 pada PDF.
/// </summary>
public sealed partial class TrueTypeFont : IPdfGlyphSource
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

}
