using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


/// <summary>
/// Peta dari kode karakter ke teks (CMap /ToUnicode) atau ke CID
/// (/Encoding berupa stream CMap yang ditanam).
/// </summary>
public sealed class PdfCMap
{
    private readonly Dictionary<int, string> _single = new();
    private readonly List<(int Low, int High, string[] Values)> _ranges = new();
    private readonly int _codeLength;

    private PdfCMap(int codeLength) => _codeLength = codeLength;

    public int CodeLength => _codeLength;

    public int Count => _single.Count + _ranges.Count;

    public bool TryDecode(int code, out string value)
    {
        if (_single.TryGetValue(code, out value!)) return true;
        foreach (var (low, high, values) in _ranges)
        {
            if (code < low || code > high) continue;
            int index = code - low;
            value = index < values.Length ? values[index] : values[0];
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryDecodeInteger(int code, out int cid)
    {
        cid = 0;
        if (!TryDecode(code, out string text) || text.Length == 0) return false;
        cid = text[0];
        return true;
    }

    /// <summary>Baca CMap (ToUnicode maupun CMap pengkodean CID).</summary>
    public static PdfCMap Parse(byte[] data, int codeLengthHint)
    {
        var cmap = new PdfCMap(codeLengthHint);
        var parser = new PdfParser(new PdfLexer(data));

        while (true)
        {
            // Token dibaca lewat parser, bukan lewat lexer langsung: parser menyimpan
            // lookahead-nya sendiri, dan mencampur keduanya membuat token pertama tiap
            // blok pemetaan dianggap sudah terpakai sehingga CMap terbaca kosong.
            var token = parser.Next();
            if (token.Type == PdfTokenType.End) break;
            if (token.Type != PdfTokenType.Keyword) continue;

            switch (token.Name)
            {
                case "begincodespacerange":
                    ReadCodespaceRange(parser);
                    break;
                case "beginbfchar":
                case "begincidchar":
                    ReadCharacterMappings(parser, cmap, isRange: false);
                    break;
                case "beginbfrange":
                case "begincidrange":
                    ReadCharacterMappings(parser, cmap, isRange: true);
                    break;
            }
        }

        return cmap;
    }

    private static void ReadCodespaceRange(PdfParser parser)
    {
        // Rentang kode tidak dipakai: panjang kode sudah ditentukan pemanggil.
        // Isinya dibaca agar blok berikutnya tetap sejajar.
        while (parser.Peek().Type == PdfTokenType.String)
        {
            parser.Next();
        }
    }

    private static void ReadCharacterMappings(PdfParser parser, PdfCMap cmap, bool isRange)
    {
        while (true)
        {
            var token = parser.Peek();
            if (token.Type != PdfTokenType.String) return;
            parser.Next();
            int low = BigEndianValue(token.Bytes ?? Array.Empty<byte>());

            if (!isRange)
            {
                if (!parser.TryParseValue(out var value)) return;
                cmap._single[low] = Text(value);
                continue;
            }

            if (!parser.TryParseValue(out var highValue)) return;
            int high = highValue switch
            {
                PdfString text => BigEndianValue(text.Bytes),
                _ => low
            };

            if (!parser.TryParseValue(out var destination)) return;
            if (destination is PdfArray array)
            {
                var values = new string[array.Count];
                for (int i = 0; i < array.Count; i++) values[i] = Text(array.At(i));
                cmap._ranges.Add((low, Math.Max(low, high), values));
                continue;
            }

            string single = Text(destination);
            if (single.Length == 0) continue;
            int first = char.ConvertToUtf32(single, 0);
            var generated = new string[Math.Max(1, high - low + 1)];
            for (int i = 0; i < generated.Length; i++)
            {
                generated[i] = first + i <= 0x10FFFF ? char.ConvertFromUtf32(first + i) : single;
            }

            cmap._ranges.Add((low, Math.Max(low, high), generated));
        }
    }

    private static string Text(PdfObject value) => value switch
    {
        PdfString text => DecodeUtf16Be(text.Bytes),
        PdfNumber number => ((char)number.ToInt()).ToString(),
        _ => string.Empty
    };

    public static string DecodeUtf16Be(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        if (bytes.Length == 1) return ((char)bytes[0]).ToString();
        if (bytes.Length % 2 != 0) return Encoding.BigEndianUnicode.GetString(bytes.AsSpan(0, bytes.Length - 1).ToArray());
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static int BigEndianValue(byte[] bytes)
    {
        int value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }
}

