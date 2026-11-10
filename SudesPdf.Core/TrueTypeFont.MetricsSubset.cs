using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

public sealed partial class TrueTypeFont : IPdfGlyphSource
{
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
