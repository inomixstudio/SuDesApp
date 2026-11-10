using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SudesPdf.Core;

/// <summary>Permukaan raster BGRA (8 bit/kanal, tidak premultiplied).</summary>
public sealed class RasterImage
{
    public RasterImage(int width, int height, bool transparent = false)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new byte[Width * Height * 4];
        if (!transparent)
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                Pixels[i] = 255;
                Pixels[i + 1] = 255;
                Pixels[i + 2] = 255;
                Pixels[i + 3] = 255;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Biru, hijau, merah, alfa.</summary>
    public byte[] Pixels { get; }

    public void Clear(byte blue, byte green, byte red, byte alpha)
    {
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            Pixels[i] = blue;
            Pixels[i + 1] = green;
            Pixels[i + 2] = red;
            Pixels[i + 3] = alpha;
        }
    }

    public RasterImage Crop(PdfRect area)
    {
        int x0 = Math.Max(0, (int)Math.Floor(area.Left));
        int y0 = Math.Max(0, (int)Math.Floor(area.Bottom));
        int x1 = Math.Min(Width, (int)Math.Ceiling(area.Right));
        int y1 = Math.Min(Height, (int)Math.Ceiling(area.Top));
        int w = Math.Max(1, x1 - x0);
        int h = Math.Max(1, y1 - y0);
        var result = new RasterImage(w, h, transparent: true);
        for (int y = 0; y < h; y++)
        {
            int sourceRow = ((y0 + y) * Width + x0) * 4;
            int targetRow = y * w * 4;
            Array.Copy(Pixels, sourceRow, result.Pixels, targetRow, w * 4);
        }

        return result;
    }

    /// <summary>Enkode PNG (RGBA) untuk diagnostik dan pengujian.</summary>
    public byte[] EncodePng()
    {
        using var output = new MemoryStream();
        WriteBigEndian(output, 0x89504E47);
        WriteBigEndian(output, 0x0D0A1A0A);

        var header = new byte[13];
        WriteInt(header, 0, Width);
        WriteInt(header, 4, Height);
        header[8] = 8;   // kedalaman bit
        header[9] = 6;   // RGBA
        WriteChunk(output, "IHDR", header);

        var raw = new byte[Height * (Width * 4 + 1)];
        for (int y = 0; y < Height; y++)
        {
            int target = y * (Width * 4 + 1);
            raw[target] = 0;
            for (int x = 0; x < Width; x++)
            {
                int source = (y * Width + x) * 4;
                int destination = target + 1 + x * 4;
                raw[destination] = Pixels[source + 2];
                raw[destination + 1] = Pixels[source + 1];
                raw[destination + 2] = Pixels[source];
                raw[destination + 3] = Pixels[source + 3];
            }
        }

        WriteChunk(output, "IDAT", PdfFilters.ZlibCompress(raw));
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteInt(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in first) crc = UpdateCrc(crc, b);
        foreach (byte b in second) crc = UpdateCrc(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc;
    }
}

