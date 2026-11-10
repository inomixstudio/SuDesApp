using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


/// <summary>Tabel pengkodean karakter sederhana (ISO 32000-1 Lampiran D).</summary>
public static class PdfEncodingTables
{
    private static readonly char?[] WinAnsi = BuildWinAnsi();
    private static readonly char?[] MacRoman = BuildMacRoman();
    private static readonly char?[] Standard = BuildStandard();
    private static readonly char?[] PdfDoc = BuildPdfDoc();

    public static char? Lookup(string? encodingName, int code)
    {
        var table = encodingName switch
        {
            "WinAnsiEncoding" => WinAnsi,
            "MacRomanEncoding" => MacRoman,
            "MacExpertEncoding" => MacRoman,
            "StandardEncoding" => Standard,
            "PDFDocEncoding" => PdfDoc,
            _ => WinAnsi
        };

        return code >= 0 && code < table.Length ? table[code] : null;
    }

    public static char? LookupByName(string name, int code) => Lookup(name, code);

    private static char?[] BuildWinAnsi()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        table[0x20] = ' ';
        string specials = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F\u0090''\"\"•–—˜™š›œ\u009DžŸ";
        for (int i = 0; i < specials.Length; i++) table[0x80 + i] = specials[i];
        for (int i = 0xA0; i < 0x100; i++) table[i] = (char)i;
        return table;
    }

    private static char?[] BuildMacRoman()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        string high = "ÄÅÇÉÑÖÜáàâäãåçéèêëíìîïñóòôöõúùûü†°¢£§•¶ß®©™´¨≠ÆØ∞±≤≥¥µ∂∑∏π∫ªºΩæø¿¡¬√ƒ≈∆«»…\u00A0ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔ\uF8FFÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";
        for (int i = 0; i < high.Length && 0x80 + i < 256; i++) table[0x80 + i] = high[i];
        return table;
    }

    private static char?[] BuildStandard()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        table[0xA0] = ' ';
        string high =
            "\u00A1\u00A2\u00A3\u2044\u00A5\u0192\u00A7\u00A4'\u201C\u00AB\u2039\u203A\uFB01\uFB02\u2013" +
            "\u2020\u2021\u00B7\u201A\u201E\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A" +
            "\u0153\u0161\u0178\u0178\u00A0\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB" +
            "\u00AC\u00AD\u00AE\u00AF\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB" +
            "\u00BC\u00BD\u00BE\u00BF\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB" +
            "\u00CC\u00CD\u00CE\u00CF\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB" +
            "\u00DC\u00DD\u00DE\u00DF\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB" +
            "\u00EC\u00ED\u00EE\u00EF\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB" +
            "\u00FC\u00FD\u00FE\u00FF";
        // Daftar di atas memuat lebih banyak nama glif daripada ruang yang
        // tersedia (0xA1..0xFF); sisanya diabaikan supaya tabel tetap sah.
        for (int i = 0; i < high.Length && 0xA1 + i < 256; i++) table[0xA1 + i] = high[i];
        return table;
    }

    private static char?[] BuildPdfDoc()
    {
        var table = new char?[256];
        for (int i = 0x20; i < 0x7F; i++) table[i] = (char)i;
        string high =
            "\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203A\u2212\u2030\u201E\u201C\u201D\u2018" +
            "\u2019\u201A\u2122\uFB01\uFB02\u0141\u0152\u0160\u0178\u017D\u0131\u0142\u0153\u0161\u017E\uFFFD" +
            "\u20AC\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB\u00AC\u00AD\u00AE\u00AF" +
            "\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB\u00BC\u00BD\u00BE\u00BF" +
            "\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC\u00CD\u00CE\u00CF" +
            "\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB\u00DC\u00DD\u00DE\u00DF" +
            "\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF" +
            "\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB\u00FC\u00FD\u00FE\u00FF";
        for (int i = 0; i < high.Length; i++) table[0x80 + i] = high[i];
        return table;
    }
}

