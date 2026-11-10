using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

public sealed partial class CffFont : IPdfGlyphSource
{
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


}
