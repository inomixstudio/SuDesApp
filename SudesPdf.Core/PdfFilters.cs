using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SudesPdf.Core;

/// <summary>
/// Filter aliran PDF (ISO 32000-1 §7.4). Dekompresi Flate ditulis sendiri supaya
/// data yang terpotong (mis. /Length meleset, lazim pada PDF nyata) tetap bisa
/// dibaca sebanyak yang tersedia, bukan gagal total seperti pustaka zlib ketat.
/// </summary>
public static class PdfFilters
{
    public static bool IsImageFilter(string name) => name switch
    {
        "DCTDecode" or "DCT" or "JPXDecode" or "JBIG2Decode" or "CCITTFaxDecode" or "CCF" => true,
        _ => false
    };

    public static string Normalize(string name) => name switch
    {
        "Fl" => "FlateDecode",
        "LZW" => "LZWDecode",
        "AHx" => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "RL" => "RunLengthDecode",
        "CCF" => "CCITTFaxDecode",
        "DCT" => "DCTDecode",
        _ => name
    };

    /// <summary>
    /// Terapkan rangkaian filter. Filter gambar (DCT/JPX/JBIG2/CCITT) tidak
    /// dijalankan di sini — berkas gambar mentah diteruskan ke pengurai gambar.
    /// </summary>
    public static byte[] Apply(
        byte[] data, IReadOnlyList<string> filters, IReadOnlyList<PdfDictionary?> parms)
    {
        byte[] current = data;
        for (int i = 0; i < filters.Count; i++)
        {
            string name = Normalize(filters[i]);
            var parm = i < parms.Count ? parms[i] : null;
            current = name switch
            {
                "FlateDecode" => ApplyPredictor(Inflate(current), parm),
                "LZWDecode" => ApplyPredictor(LzwDecode(current, parm), parm),
                "ASCIIHexDecode" => AsciiHexDecode(current),
                "ASCII85Decode" => Ascii85Decode(current),
                "RunLengthDecode" => RunLengthDecode(current),
                "Crypt" => throw new PdfException("Filter /Crypt tidak didukung."),
                _ => current
            };
        }

        return current;
    }

    public static string[] FiltersOf(PdfDictionary dict)
    {
        var filter = dict.Raw("Filter");
        switch (filter)
        {
            case PdfName name:
                return new[] { name.Value };
            case PdfArray array:
            {
                var names = new List<string>(array.Count);
                foreach (var item in array.Items)
                {
                    if (item is PdfName n) names.Add(n.Value);
                }

                return names.ToArray();
            }

            case null:
                return Array.Empty<string>();
            default:
                return Array.Empty<string>();
        }
    }

    public static PdfDictionary?[] ParmsOf(PdfDictionary dict, int count)
    {
        var parms = new PdfDictionary?[count];
        var value = dict.Raw("DecodeParms") ?? dict.Raw("DP");
        switch (value)
        {
            case PdfDictionary single:
                if (count > 0) parms[0] = single;
                break;
            case PdfArray array:
                for (int i = 0; i < count && i < array.Count; i++)
                {
                    parms[i] = array.At(i) as PdfDictionary;
                }

                break;
        }

        return parms;
    }

    // ---------------------------------------------------------------- Flate

    /// <summary>
    /// Dekompresi Flate. Memakai zlib bawaan .NET (bagian dari kerangka kerja,
    /// bukan pustaka pihak ketiga) dan tetap mengembalikan hasil sebagian bila
    /// data terpotong — PDF nyata sering menulis /Length yang meleset, dan
    /// pemulihan sebagian jauh lebih berguna daripada kegagalan total.
    /// </summary>
    public static byte[] Inflate(byte[] data)
    {
        if (data.Length == 0) return data;

        // Header zlib (CMF/FLG) menentukan apakah perlu pembungkus zlib.
        bool zlib = data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0;
        var result = InflateStream(data, zlib);
        if (result.Length == 0 && zlib)
        {
            // Sebagian penulis memakai deflate mentah tanpa header zlib.
            result = InflateStream(data, zlibMode: false);
        }

        return result;
    }

    private static byte[] InflateStream(byte[] data, bool zlibMode)
    {
        using var input = new MemoryStream(data, writable: false);
        using var output = new MemoryStream(Math.Max(1024, data.Length * 4));
        try
        {
            using Stream decompressor = zlibMode
                ? new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);
            decompressor.CopyTo(output);
        }
        catch (InvalidDataException)
        {
            // Data terpotong atau rusak di ujung: pakai apa yang sudah terbaca.
        }
        catch (IOException)
        {
            // Sama seperti InvalidDataException: pakai data yang sempat terbaca.
        }

        return output.ToArray();
    }

    // ------------------------------------------------------------------ LZW

    public static byte[] LzwDecode(byte[] data, PdfDictionary? parms)
    {
        bool earlyChange = parms?.GetInt("EarlyChange") != 0;
        var output = new ByteBuffer(Math.Max(1024, data.Length * 3));
        var reader = new MsbBitReader(data);

        var prefixes = new int[4096];
        var suffixes = new byte[4096];
        var stack = new byte[4096];
        int nextCode = 258;
        int codeSize = 9;
        int previous = -1;
        byte previousFirst = 0;

        for (int i = 0; i < 256; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
        }

        while (reader.TryGetBits(codeSize, out int code))
        {
            if (code == 256)
            {
                nextCode = 258;
                codeSize = 9;
                previous = -1;
                continue;
            }

            if (code == 257) break;

            // Kode yang belum ada di tabel berarti "string sebelumnya + huruf
            // pertamanya" (kasus KwKwK).
            bool repeatPrevious = code >= nextCode;
            if (repeatPrevious && previous < 0) break;

            int current = repeatPrevious ? previous : code;
            int stackTop = 0;
            while (current >= 0 && stackTop < stack.Length)
            {
                stack[stackTop++] = suffixes[current];
                if (current < 256) break;
                current = prefixes[current];
            }

            byte firstChar = stackTop > 0 ? stack[stackTop - 1] : (byte)0;
            while (stackTop > 0) output.Add(stack[--stackTop]);
            if (repeatPrevious) output.Add(previousFirst);

            if (previous >= 0 && nextCode < 4096)
            {
                prefixes[nextCode] = previous;
                suffixes[nextCode] = firstChar;
                nextCode++;
            }

            previous = code;
            previousFirst = firstChar;

            int limit = nextCode + (earlyChange ? 1 : 0);
            codeSize = limit switch
            {
                <= 512 => 9,
                <= 1024 => 10,
                <= 2048 => 11,
                _ => 12
            };
        }

        return output.ToArray();
    }

    // ---------------------------------------------------------- ASCII & RLE

    public static byte[] AsciiHexDecode(byte[] data)
    {
        var output = new ByteBuffer(data.Length / 2 + 8);
        int high = -1;
        foreach (byte b in data)
        {
            if (b == (byte)'>') break;
            if (PdfLexer.IsWhitespace(b)) continue;
            int digit = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
                >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
                >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
                _ => -1
            };
            if (digit < 0) continue;
            if (high < 0)
            {
                high = digit;
                continue;
            }

            output.Add((byte)((high << 4) | digit));
            high = -1;
        }

        if (high >= 0) output.Add((byte)(high << 4));
        return output.ToArray();
    }

    public static byte[] Ascii85Decode(byte[] data)
    {
        var output = new ByteBuffer(data.Length * 4 / 5 + 8);
        int index = 0;
        if (data.Length >= 2 && data[0] == (byte)'<' && data[1] == (byte)'~') index = 2;

        var group = new int[5];
        int count = 0;
        for (; index < data.Length; index++)
        {
            byte b = data[index];
            if (PdfLexer.IsWhitespace(b)) continue;
            if (b == (byte)'~') break;
            if (b == (byte)'z' && count == 0)
            {
                output.Add(0);
                output.Add(0);
                output.Add(0);
                output.Add(0);
                continue;
            }

            if (b < (byte)'!' || b > (byte)'u') continue;
            group[count++] = b - (byte)'!';
            if (count < 5) continue;

            uint value = 0;
            for (int i = 0; i < 5; i++) value = value * 85 + (uint)group[i];
            output.Add((byte)(value >> 24));
            output.Add((byte)(value >> 16));
            output.Add((byte)(value >> 8));
            output.Add((byte)value);
            count = 0;
        }

        if (count > 1)
        {
            for (int i = count; i < 5; i++) group[i] = 84;
            uint value = 0;
            for (int i = 0; i < 5; i++) value = value * 85 + (uint)group[i];
            for (int i = 0; i < count - 1; i++) output.Add((byte)(value >> (24 - i * 8)));
        }

        return output.ToArray();
    }

    public static byte[] RunLengthDecode(byte[] data)
    {
        var output = new ByteBuffer(data.Length * 3 + 16);
        int index = 0;
        while (index < data.Length)
        {
            int length = data[index++];
            if (length == 128) break;
            if (length < 128)
            {
                for (int i = 0; i < length + 1 && index < data.Length; i++) output.Add(data[index++]);
                continue;
            }

            if (index >= data.Length) break;
            byte value = data[index++];
            for (int i = 0; i < 257 - length; i++) output.Add(value);
        }

        return output.ToArray();
    }

    // ------------------------------------------------------------- Predictors

    public static byte[] ApplyPredictor(byte[] data, PdfDictionary? parms)
    {
        int predictor = parms?.GetInt("Predictor") ?? 1;
        if (predictor <= 1) return data;

        int colors = parms?.GetInt("Colors") ?? 1;
        int bpc = parms?.GetInt("BitsPerComponent") ?? 8;
        int columns = parms?.GetInt("Columns") ?? 1;
        if (colors <= 0) colors = 1;
        if (bpc <= 0) bpc = 8;
        if (columns <= 0) columns = 1;

        int bytesPerPixel = Math.Max(1, (colors * bpc + 7) / 8);
        int rowLength = (columns * colors * bpc + 7) / 8;
        if (rowLength <= 0) return data;

        if (predictor == 2)
        {
            var output = new byte[data.Length];
            Array.Copy(data, output, data.Length);
            for (int row = 0; row * rowLength < output.Length; row++)
            {
                int offset = row * rowLength;
                for (int i = bytesPerPixel; i < rowLength && offset + i < output.Length; i++)
                {
                    output[offset + i] = (byte)(output[offset + i] + output[offset + i - bytesPerPixel]);
                }
            }

            return output;
        }

        // PNG: setiap baris diawali satu byte jenis filter.
        int stride = rowLength + 1;
        int rows = data.Length / stride;
        var result = new byte[rows * rowLength];
        var previous = new byte[rowLength];
        var current = new byte[rowLength];

        for (int row = 0; row < rows; row++)
        {
            int baseIndex = row * stride;
            int filter = data[baseIndex];
            Array.Copy(data, baseIndex + 1, current, 0, rowLength);

            for (int i = 0; i < rowLength; i++)
            {
                int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                int up = previous[i];
                int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                int value = filter switch
                {
                    0 => current[i],
                    1 => current[i] + left,
                    2 => current[i] + up,
                    3 => current[i] + (left + up) / 2,
                    4 => current[i] + Paeth(left, up, upLeft),
                    _ => current[i]
                };
                current[i] = (byte)value;
            }

            Array.Copy(current, 0, result, row * rowLength, rowLength);
            (previous, current) = (current, previous);
        }

        return result;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        if (pa <= pb && pa <= pc) return left;
        return pb <= pc ? up : upLeft;
    }

    // ----------------------------------------------------------------- Bits

    private struct MsbBitReader
    {
        private readonly byte[] _data;
        private int _bitPosition;

        public MsbBitReader(byte[] data)
        {
            _data = data;
            _bitPosition = 0;
        }

        public bool TryGetBits(int count, out int value)
        {
            value = 0;
            if (_bitPosition + count > _data.Length * 8) return false;
            for (int i = 0; i < count; i++)
            {
                int byteIndex = (_bitPosition + i) >> 3;
                int bitIndex = 7 - ((_bitPosition + i) & 7);
                value = (value << 1) | ((_data[byteIndex] >> bitIndex) & 1);
            }

            _bitPosition += count;
            return true;
        }
    }

    /// <summary>Buffer byte yang bertambah sendiri untuk hasil dekompresi.</summary>
    internal sealed class ByteBuffer
    {
        private byte[] _buffer;
        private int _length;

        public ByteBuffer(int capacity) => _buffer = new byte[Math.Max(16, capacity)];

        public int Length => _length;

        public void Add(byte value)
        {
            if (_length == _buffer.Length) Grow(_length + 1);
            _buffer[_length++] = value;
        }

        public bool CopyBack(int distance, int count)
        {
            if (distance <= 0 || distance > _length) return false;
            if (_length + count > _buffer.Length) Grow(_length + count);
            int source = _length - distance;
            for (int i = 0; i < count; i++)
            {
                _buffer[_length++] = _buffer[source + i];
            }

            return true;
        }

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Array.Copy(_buffer, result, _length);
            return result;
        }

        private void Grow(int required)
        {
            int size = _buffer.Length;
            while (size < required) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }

    /// <summary>Penulis zlib (untuk PNG diagnostik) memakai DeflateStream bawaan .NET.</summary>
    internal static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        // Header zlib: CMF 0x78 (deflate 32K) + FLG tanpa check bits.
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        uint adler = (b << 16) | a;
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }
}
