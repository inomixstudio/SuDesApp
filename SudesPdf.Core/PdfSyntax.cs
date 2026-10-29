using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SudesPdf.Core;

public enum PdfTokenType
{
    End,
    Number,
    Name,
    String,
    ArrayOpen,
    ArrayClose,
    DictOpen,
    DictClose,
    Keyword,
    Boolean,
    Null
}

public struct PdfToken
{
    public PdfTokenType Type;
    public string Name;
    public double Number;
    public byte[]? Bytes;
    public bool Bool;
}

/// <summary>Tokenizer PDF (dipakai untuk berkas maupun aliran isi halaman).</summary>
public sealed class PdfLexer
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _pos;

    public PdfLexer(byte[] data, int start = 0, int? end = null)
    {
        _data = data;
        _pos = Math.Clamp(start, 0, data.Length);
        _end = Math.Clamp(end ?? data.Length, _pos, data.Length);
    }

    public int Position
    {
        get => _pos;
        set => _pos = Math.Clamp(value, 0, _end);
    }

    public int End => _end;

    public byte[] Buffer => _data;

    public static bool IsWhitespace(byte b) => b switch
    {
        0 or 9 or 10 or 12 or 13 or 32 => true,
        _ => false
    };

    public static bool IsDelimiter(byte b) => b switch
    {
        (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%' => true,
        _ => false
    };

    public void SkipWhitespace()
    {
        while (_pos < _end)
        {
            byte b = _data[_pos];
            if (b == (byte)'%')
            {
                while (_pos < _end && _data[_pos] != 10 && _data[_pos] != 13) _pos++;
                continue;
            }

            if (IsWhitespace(b))
            {
                _pos++;
                continue;
            }

            break;
        }
    }

    public bool TryReadToken(out PdfToken token)
    {
        SkipWhitespace();
        token = default;
        if (_pos >= _end) return false;

        byte b = _data[_pos];
        if (b == (byte)'<' && _pos + 1 < _end && _data[_pos + 1] == (byte)'<')
        {
            _pos += 2;
            token.Type = PdfTokenType.DictOpen;
            return true;
        }

        if (b == (byte)'>' && _pos + 1 < _end && _data[_pos + 1] == (byte)'>')
        {
            _pos += 2;
            token.Type = PdfTokenType.DictClose;
            return true;
        }

        switch (b)
        {
            case (byte)'[':
                _pos++;
                token.Type = PdfTokenType.ArrayOpen;
                return true;
            case (byte)']':
                _pos++;
                token.Type = PdfTokenType.ArrayClose;
                return true;
            case (byte)('/'):
                _pos++;
                token = ReadName();
                return true;
            case (byte)'(':
                token.Type = PdfTokenType.String;
                token.Bytes = ReadLiteralString();
                return true;
            case (byte)'<':
                token.Type = PdfTokenType.String;
                token.Bytes = ReadHexString();
                return true;
            case (byte)')':
            case (byte)'>':
            case (byte)'{':
            case (byte)'}':
                // Pembatas liar: lewati supaya berkas cacat tidak menghentikan pembacaan.
                _pos++;
                return TryReadToken(out token);
        }

        if (b == (byte)'+' || b == (byte)'-' || b == (byte)'.' || (b >= (byte)'0' && b <= (byte)'9'))
        {
            return ReadNumber(out token);
        }

        return ReadKeyword(out token);
    }

    private PdfToken ReadName()
    {
        int start = _pos;
        while (_pos < _end && !IsWhitespace(_data[_pos]) && !IsDelimiter(_data[_pos])) _pos++;
        string raw = Encoding.Latin1.GetString(_data, start, _pos - start);
        return new PdfToken { Type = PdfTokenType.Name, Name = DecodeName(raw) };
    }

    /// <summary>Nama PDF memakai #hh untuk byte yang tidak aman.</summary>
    public static string DecodeName(string raw)
    {
        if (raw.IndexOf('#') < 0) return raw;
        var builder = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && i + 2 < raw.Length
                && Uri.IsHexDigit(raw[i + 1]) && Uri.IsHexDigit(raw[i + 2]))
            {
                builder.Append((char)Convert.ToInt32(raw.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            builder.Append(raw[i]);
        }

        return builder.ToString();
    }

    private bool ReadNumber(out PdfToken token)
    {
        int start = _pos;
        while (_pos < _end && !IsWhitespace(_data[_pos]) && !IsDelimiter(_data[_pos])) _pos++;
        string text = Encoding.Latin1.GetString(_data, start, _pos - start);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            token = new PdfToken { Type = PdfTokenType.Number, Number = value };
            return true;
        }

        // Bukan angka (mis. "1.2.3"): kembalikan sebagai kata kunci agar bisa dilewati.
        token = new PdfToken { Type = PdfTokenType.Keyword, Name = text };
        return true;
    }

    private bool ReadKeyword(out PdfToken token)
    {
        int start = _pos;
        while (_pos < _end && !IsWhitespace(_data[_pos]) && !IsDelimiter(_data[_pos])) _pos++;
        string text = Encoding.Latin1.GetString(_data, start, _pos - start);
        token = text switch
        {
            "true" => new PdfToken { Type = PdfTokenType.Boolean, Bool = true },
            "false" => new PdfToken { Type = PdfTokenType.Boolean, Bool = false },
            "null" => new PdfToken { Type = PdfTokenType.Null },
            _ => new PdfToken { Type = PdfTokenType.Keyword, Name = text }
        };
        return true;
    }

    private byte[] ReadLiteralString()
    {
        // '(' sudah dikonsumsi.
        _pos++;
        var bytes = new List<byte>(32);
        int depth = 1;
        while (_pos < _end)
        {
            byte b = _data[_pos++];
            if (b == (byte)'\\')
            {
                if (_pos >= _end) break;
                byte escape = _data[_pos++];
                switch (escape)
                {
                    case (byte)'n': bytes.Add(10); break;
                    case (byte)'r': bytes.Add(13); break;
                    case (byte)'t': bytes.Add(9); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'(': bytes.Add((byte)'('); break;
                    case (byte)')': bytes.Add((byte)')'); break;
                    case (byte)'\\': bytes.Add((byte)'\\'); break;
                    case 13:
                        if (_pos < _end && _data[_pos] == 10) _pos++;
                        break;
                    case 10:
                        break;
                    default:
                        if (escape >= (byte)'0' && escape <= (byte)'7')
                        {
                            int value = escape - (byte)'0';
                            for (int i = 0; i < 2 && _pos < _end; i++)
                            {
                                byte digit = _data[_pos];
                                if (digit < (byte)'0' || digit > (byte)'7') break;
                                value = value * 8 + (digit - (byte)'0');
                                _pos++;
                            }

                            bytes.Add((byte)(value & 0xFF));
                        }
                        else
                        {
                            bytes.Add(escape);
                        }

                        break;
                }

                continue;
            }

            if (b == (byte)'(')
            {
                depth++;
                bytes.Add(b);
                continue;
            }

            if (b == (byte)')')
            {
                depth--;
                if (depth == 0) break;
                bytes.Add(b);
                continue;
            }

            bytes.Add(b);
        }

        return bytes.ToArray();
    }

    private byte[] ReadHexString()
    {
        // '<' sudah dikonsumsi.
        _pos++;
        var bytes = new List<byte>(32);
        int high = -1;
        while (_pos < _end)
        {
            byte b = _data[_pos++];
            if (b == (byte)'>') break;
            if (IsWhitespace(b)) continue;
            int digit = HexValue(b);
            if (digit < 0) continue;
            if (high < 0)
            {
                high = digit;
                continue;
            }

            bytes.Add((byte)((high << 4) | digit));
            high = -1;
        }

        if (high >= 0) bytes.Add((byte)(high << 4));
        return bytes.ToArray();
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
        >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
        _ => -1
    };
}

/// <summary>Pembaca nilai PDF dengan kemampuan mengintip token berikutnya.</summary>
public sealed class PdfParser
{
    private readonly PdfLexer _lexer;
    private readonly PdfToken[] _lookahead = new PdfToken[3];
    private readonly bool[] _lookaheadValid = new bool[3];

    public PdfParser(PdfLexer lexer) => _lexer = lexer;

    public PdfLexer Lexer => _lexer;

    public PdfToken Peek(int index = 0)
    {
        for (int i = 0; i <= index; i++)
        {
            if (!_lookaheadValid[i])
            {
                _lookahead[i] = _lexer.TryReadToken(out var token) ? token : default;
                _lookaheadValid[i] = true;
            }
        }

        return _lookahead[index];
    }

    public PdfToken Next()
    {
        var token = Peek();
        for (int i = 0; i < 2; i++)
        {
            _lookahead[i] = _lookahead[i + 1];
            _lookaheadValid[i] = _lookaheadValid[i + 1];
        }

        _lookaheadValid[2] = false;
        return token;
    }

    public void SkipKeyword()
    {
        if (Peek().Type == PdfTokenType.Keyword) Next();
    }

    public bool TryParseValue(out PdfObject value, int depth = 0)
    {
        value = PdfObject.Null;
        if (depth > 64) return false;

        var token = Next();
        switch (token.Type)
        {
            case PdfTokenType.End:
                return false;

            case PdfTokenType.DictOpen:
            {
                var dict = new PdfDictionary();
                while (true)
                {
                    var key = Peek();
                    if (key.Type == PdfTokenType.DictClose)
                    {
                        Next();
                        break;
                    }

                    if (key.Type == PdfTokenType.End) break;
                    if (key.Type != PdfTokenType.Name)
                    {
                        Next();
                        continue;
                    }

                    Next();
                    if (!TryParseValue(out var item, depth + 1)) break;
                    dict.Set(key.Name, item);
                }

                value = dict;
                return true;
            }

            case PdfTokenType.ArrayOpen:
            {
                var array = new PdfArray();
                while (true)
                {
                    var peek = Peek();
                    if (peek.Type == PdfTokenType.ArrayClose)
                    {
                        Next();
                        break;
                    }

                    if (peek.Type == PdfTokenType.End) break;
                    if (!TryParseValue(out var item, depth + 1)) break;
                    array.Items.Add(item);
                }

                value = array;
                return true;
            }

            case PdfTokenType.Name:
                value = new PdfName(token.Name);
                return true;

            case PdfTokenType.String:
                value = new PdfString(token.Bytes ?? Array.Empty<byte>());
                return true;

            case PdfTokenType.Boolean:
                value = token.Bool ? PdfBoolean.True : PdfBoolean.False;
                return true;

            case PdfTokenType.Null:
                value = PdfObject.Null;
                return true;

            case PdfTokenType.Number:
            {
                // "12 0 R" adalah referensi tidak langsung.
                if (IsWholeNonNegative(token.Number)
                    && Peek(0).Type == PdfTokenType.Number
                    && IsWholeNonNegative(Peek(0).Number)
                    && Peek(1).Type == PdfTokenType.Keyword
                    && Peek(1).Name == "R")
                {
                    int number = (int)token.Number;
                    int generation = (int)Next().Number;
                    Next();
                    value = new PdfReference(number, generation);
                    return true;
                }

                value = new PdfNumber(token.Number);
                return true;
            }

            default:
                // Kata kunci asing (obj, endobj, stream, ...): dianggap nilai kosong.
                value = PdfObject.Null;
                return true;
        }
    }

    public PdfObject ParseValue() => TryParseValue(out var value) ? value : PdfObject.Null;

    private static bool IsWholeNonNegative(double value) =>
        value >= 0 && Math.Abs(value - Math.Round(value)) < 1e-9;
}

internal static class PdfIndirectReader
{
    /// <summary>
    /// Baca objek tidak langsung pada offset tertentu: "N G obj &lt;nilai&gt; endobj",
    /// termasuk data stream setelah kamus.
    /// </summary>
    public static PdfObject? Read(byte[] data, int offset, out int endOffset, int objectNumber = 0, int generation = 0)
    {
        endOffset = offset;
        if (offset < 0 || offset >= data.Length) return null;

        var lexer = new PdfLexer(data, offset);
        var parser = new PdfParser(lexer);

        // Lewati kepala "N G obj" sebelum membaca nilai objek.
        int headerTokens = 0;
        while (headerTokens < 3)
        {
            var head = parser.Peek();
            if (head.Type == PdfTokenType.Number)
            {
                parser.Next();
                headerTokens++;
                continue;
            }

            if (head.Type == PdfTokenType.Keyword && head.Name == "obj")
            {
                parser.Next();
                headerTokens++;
            }

            break;
        }

        if (!parser.TryParseValue(out var value, 0)) return null;

        if (value is not PdfDictionary dict) return value;

        lexer.SkipWhitespace();
        int afterDict = lexer.Position;
        var keyword = lexer.TryReadToken(out var token) ? token : default;
        if (token.Type != PdfTokenType.Keyword || token.Name != "stream")
        {
            endOffset = afterDict;
            return dict;
        }

        // Data stream dimulai setelah EOL tunggal.
        int dataStart = lexer.Position;
        if (dataStart < data.Length && data[dataStart] == 13) dataStart++;
        if (dataStart < data.Length && data[dataStart] == 10) dataStart++;

        int length = ResolveLength(dict);
        int dataEnd;
        if (length >= 0 && dataStart + length <= data.Length)
        {
            dataEnd = dataStart + length;
        }
        else
        {
            dataEnd = FindEndstream(data, dataStart);
        }

        var bytes = new byte[Math.Max(0, dataEnd - dataStart)];
        Array.Copy(data, dataStart, bytes, 0, bytes.Length);
        endOffset = dataEnd;
        return new PdfStream(dict, bytes)
        {
            ObjectNumber = objectNumber,
            Generation = generation
        };
    }

    private static int ResolveLength(PdfDictionary dict)
    {
        // Hanya /Length langsung yang dipakai di sini; referensi tak langsung
        // diselesaikan pemanggil melalui pencarian "endstream".
        if (dict.Raw("Length") is PdfNumber number && number.Value >= 0)
        {
            return (int)Math.Min(number.Value, int.MaxValue);
        }

        return -1;
    }

    public static int FindEndstream(byte[] data, int from)
    {
        // "endstream" yang didahului EOL; saat tidak ada pencocokan, ambil akhir berkas.
        for (int i = from; i + 9 <= data.Length; i++)
        {
            if (data[i] != (byte)'e') continue;
            if (Matches(data, i, "endstream"))
            {
                int end = i;
                if (end > 0 && data[end - 1] == 10) end--;
                if (end > 0 && data[end - 1] == 13) end--;
                return end;
            }
        }

        return data.Length;
    }

    private static bool Matches(byte[] data, int offset, string text)
    {
        if (offset + text.Length > data.Length) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (data[offset + i] != (byte)text[i]) return false;
        }

        return true;
    }
}
