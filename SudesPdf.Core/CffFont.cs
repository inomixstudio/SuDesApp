using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

/// <summary>
/// Pengurai font CFF (Compact Font Format, ISO/IEC 14496-22 Lampiran C) dengan
/// penafsir charstring Type2. Mencakup font berkunci nama (Type1C) maupun
/// berkunci CID (CIDFontType0C) yang tersemat sebagai /FontFile3.
/// </summary>
public sealed partial class CffFont : IPdfGlyphSource
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

}
