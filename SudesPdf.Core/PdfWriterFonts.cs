using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Empat muka font standar Times yang tersedia di setiap pembaca PDF.</summary>
public enum PdfTimesFace
{
    Roman,
    Bold,
    Italic,
    BoldItalic,
}

/// <summary>
/// Font standar PDF (base-14 Times) untuk dokumen yang DITULIS SudesPdf.
///
/// Surat tidak lagi menyematkan berkas font: teks memakai font standar yang wajib
/// ada pada setiap pembaca PDF, dengan pengkodean WinAnsiEncoding. Cara ini
/// menghapus dua masalah sekaligus — berkas font pihak lain tidak ikut
/// didistribusikan, dan teks surat dapat dicari/dipilih langsung dari berkasnya.
///
/// Tabel lebar di bawah adalah metrik Times (satuan 1/1000 em) untuk kode
/// WinAnsi 32..255, yaitu data yang sama dengan yang dipakai penyemat dokumen
/// sebelumnya, sehingga pemenggalan baris surat tidak berubah. Lebar muka
/// BoldItalic memakai metrik Bold karena berkas acuannya tidak disertakan —
/// hanya berpengaruh pada teks yang sekaligus tebal dan miring.
/// </summary>
public static class PdfStandardTimes
{
    /// <summary>Kode WinAnsi pertama yang punya lebar sendiri (spasi).</summary>
    public const int FirstChar = 32;

    /// <summary>Kode WinAnsi terakhir.</summary>
    public const int LastChar = 255;

    /// <summary>Nama BaseFont pada berkas PDF untuk setiap muka.</summary>
    public static string BaseFontName(PdfTimesFace face) => face switch
    {
        PdfTimesFace.Bold => "Times-Bold",
        PdfTimesFace.Italic => "Times-Italic",
        PdfTimesFace.BoldItalic => "Times-BoldItalic",
        _ => "Times-Roman",
    };

    /// <summary>Nama saksi di halaman (dipakai sebagai /F1, /F2, …).</summary>
    public static string ResourceName(PdfTimesFace face) => face switch
    {
        PdfTimesFace.Bold => "F2",
        PdfTimesFace.Italic => "F3",
        PdfTimesFace.BoldItalic => "F4",
        _ => "F1",
    };

    /// <summary>
    /// Tinggi huruf di atas garis dasar (1/1000 em) — metrik Times yang dipakai
    /// penataan baris supaya letak teks pada halaman tidak berubah.
    /// </summary>
    public const double Ascent = 891;

    /// <summary>Kedalaman huruf di bawah garis dasar (1/1000 em).</summary>
    public const double Descent = 216;

    /// <summary>Lebar lebar setiap kode WinAnsi untuk muka tersebut.</summary>
    public static short[] Widths(PdfTimesFace face) => face switch
    {
        PdfTimesFace.Bold => LebarBold,
        PdfTimesFace.Italic => LebarItalic,
        // BoldItalic: metrik Bold (lihat catatan kelas).
        _ => LebarRoman,
    };

    /// <summary>Lebar satu kode WinAnsi dalam satuan 1/1000 em.</summary>
    public static int Width(PdfTimesFace face, int code)
    {
        var widths = Widths(face);
        int index = code - FirstChar;
        return index >= 0 && index < widths.Length ? widths[index] : 0;
    }

    /// <summary>Lebar rangkaian teks dalam point pada ukuran tertentu.</summary>
    public static double MeasureString(PdfTimesFace face, double fontSize, string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var widths = Widths(face);
        double total = 0;
        foreach (char character in text)
        {
            int code = EncodeChar(character);
            int index = code - FirstChar;
            total += index >= 0 && index < widths.Length ? widths[index] : 0;
        }

        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// Teks menjadi byte WinAnsi. Huruf yang tidak ada pada WinAnsi diganti
    /// tanda tanya, sama seperti yang dilakukan pembaca PDF saat menampilkan.
    /// </summary>
    public static byte[] Encode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();

        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) bytes[i] = (byte)EncodeChar(text[i]);
        return bytes;
    }

    /// <summary>Kode WinAnsi (satu byte) untuk sebuah karakter.</summary>
    public static int EncodeChar(char character)
    {
        // Jalur cepat: ASCII sama persis pada WinAnsi.
        if (character >= 0x20 && character < 0x7F) return character;

        // Karakter yang tidak ada di WinAnsi tetapi mudah digantikan tanpa
        // mengubah arti teks surat.
        switch (character)
        {
            case '\u00A0': return 0x20;            // spasi tak terpisah
            case '\u2018': case '\u2019': return 0x27;
            case '\u201C': case '\u201D': return 0x22;
            case '\u2013': case '\u2014': return 0x2D;
            case '\u2026': return 0x2E;
            case '\t': return 0x20;
        }

        if (_reverse is null) BuildReverse();
        if (character < 256 && _reverse!.TryGetValue(character, out int code)) return code;
        return 0x3F; // '?'
    }

    /// <summary>
    /// Peta balik WinAnsi. Disusun dari tabel yang dipakai pembaca PDF, jadi
    /// penulisan dan pembacaan dokumen tidak mungkin berbeda.
    /// </summary>
    private static Dictionary<char, int>? _reverse;

    private static void BuildReverse()
    {
        var map = new Dictionary<char, int>();
        for (int code = 0; code < 256; code++)
        {
            char? character = PdfEncodingTables.Lookup("WinAnsiEncoding", code);
            if (character is not null) map.TryAdd(character.Value, code);
        }

        _reverse = map;
    }

    /// <summary>Lebar Roman: kode WinAnsi 32..255 (1/1000 em).</summary>
    private static readonly short[] LebarRoman =
    {
          250,   333,   408,   500,   500,   833,   778,   180,   333,   333,   500,   564,   250,   333,   250,   278,
          500,   500,   500,   500,   500,   500,   500,   500,   500,   500,   278,   278,   564,   564,   564,   444,
          921,   722,   667,   667,   722,   611,   556,   722,   722,   333,   389,   722,   611,   889,   722,   722,
          556,   722,   667,   556,   611,   722,   722,   944,   722,   722,   611,   333,   278,   333,   469,   500,
          333,   444,   500,   444,   500,   444,   333,   500,   500,   278,   278,   500,   278,   778,   500,   500,
          500,   500,   333,   389,   278,   500,   500,   722,   500,   500,   444,   480,   200,   480,   541,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          250,   333,   500,   500,   500,   500,   200,   500,   333,   760,   276,   500,   564,   333,   760,   500,
          400,   549,   300,   300,   333,   576,   453,   333,   333,   300,   310,   500,   750,   750,   750,   444,
          722,   722,   722,   722,   722,   722,   889,   667,   611,   611,   611,   611,   333,   333,   333,   333,
          722,   722,   722,   722,   722,   722,   722,   564,   722,   722,   722,   722,   722,   722,   556,   500,
          444,   444,   444,   444,   444,   444,   667,   444,   444,   444,   444,   444,   278,   278,   278,   278,
          500,   500,   500,   500,   500,   500,   500,   549,   500,   500,   500,   500,   500,   500,   500,   500,
    };

    /// <summary>Lebar Bold: kode WinAnsi 32..255 (1/1000 em).</summary>
    private static readonly short[] LebarBold =
    {
          250,   333,   555,   500,   500,  1000,   833,   278,   333,   333,   500,   570,   250,   333,   250,   278,
          500,   500,   500,   500,   500,   500,   500,   500,   500,   500,   333,   333,   570,   570,   570,   500,
          930,   722,   667,   722,   722,   667,   611,   778,   778,   389,   500,   778,   667,   944,   722,   778,
          611,   778,   722,   556,   667,   722,   722,  1000,   722,   722,   667,   333,   278,   333,   581,   500,
          333,   500,   556,   444,   556,   444,   333,   500,   556,   278,   333,   556,   278,   833,   556,   500,
          556,   556,   444,   389,   333,   556,   500,   722,   500,   500,   444,   394,   220,   394,   520,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          250,   333,   500,   500,   500,   500,   220,   500,   333,   747,   300,   500,   570,   333,   747,   500,
          400,   549,   300,   300,   333,   576,   540,   333,   333,   300,   330,   500,   750,   750,   750,   500,
          722,   722,   722,   722,   722,   722,  1000,   722,   667,   667,   667,   667,   389,   389,   389,   389,
          722,   722,   778,   778,   778,   778,   778,   570,   778,   722,   722,   722,   722,   722,   611,   556,
          500,   500,   500,   500,   500,   500,   722,   444,   444,   444,   444,   444,   278,   278,   278,   278,
          500,   556,   500,   500,   500,   500,   500,   549,   500,   556,   556,   556,   556,   500,   556,   500,
    };

    /// <summary>Lebar Italic: kode WinAnsi 32..255 (1/1000 em).</summary>
    private static readonly short[] LebarItalic =
    {
          250,   333,   420,   500,   500,   833,   778,   214,   333,   333,   500,   675,   250,   333,   250,   278,
          500,   500,   500,   500,   500,   500,   500,   500,   500,   500,   333,   333,   675,   675,   675,   500,
          920,   611,   611,   667,   722,   611,   611,   722,   722,   333,   444,   667,   556,   833,   667,   722,
          611,   722,   611,   500,   556,   722,   611,   833,   611,   556,   556,   389,   278,   389,   422,   500,
          333,   500,   500,   444,   500,   444,   278,   500,   500,   278,   278,   444,   278,   722,   500,   500,
          500,   500,   389,   389,   278,   500,   444,   667,   444,   444,   389,   400,   275,   400,   541,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,   778,
          250,   389,   500,   500,   500,   500,   275,   500,   333,   760,   276,   500,   675,   333,   760,   500,
          400,   549,   300,   300,   333,   576,   523,   250,   333,   300,   310,   500,   750,   750,   750,   500,
          611,   611,   611,   611,   611,   611,   889,   667,   611,   611,   611,   611,   333,   333,   333,   333,
          722,   667,   722,   722,   722,   722,   722,   675,   722,   722,   722,   722,   722,   556,   611,   500,
          500,   500,   500,   500,   500,   500,   667,   444,   444,   444,   444,   444,   278,   278,   278,   278,
          500,   500,   500,   500,   500,   500,   500,   549,   500,   500,   500,   500,   500,   444,   500,   444,
    };
}
