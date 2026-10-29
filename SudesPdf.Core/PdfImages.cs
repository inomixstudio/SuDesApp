using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

public enum PdfImageEncoding
{
    Raw,
    Jpeg,
    Jpeg2000,
    CcittFax,
    Jbig2
}

/// <summary>
/// Gambar PDF (/Subtype /Image). Sampel 8-bit disimpan apa adanya; JPEG/JPX
/// diserahkan ke kodek luar (lapisan WPF memakai Windows Imaging Component)
/// supaya mesin inti tetap bebas ketergantungan grafis.
/// </summary>
public sealed class PdfImage
{
    private byte[]? _rgba;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Components { get; init; }

    /// <summary>Sampel mentah (atau byte terkompresi untuk JPEG/JPX).</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public PdfImageEncoding Encoding { get; init; }

    public bool IsImageMask { get; init; }

    public bool Interpolate { get; init; }

    public byte[]? Alpha { get; init; }

    public PdfColorSpace? ColorSpace { get; init; }

    public double[]? Decode { get; init; }

    public int BitsPerComponent { get; init; } = 8;

    public ushort[]? ColorKeyMask { get; init; }

    /// <summary>Warna isi untuk gambar stensil (ImageMask), diisi penafsir.</summary>
    public PdfPaint MaskPaint { get; set; }

    /// <summary>Hasil konversi RGBA (8 bit per kanal, tidak premultiplied).</summary>
    public byte[] ToRgba()
    {
        if (_rgba is not null) return _rgba;

        if (Encoding is PdfImageEncoding.Jpeg or PdfImageEncoding.Jpeg2000)
        {
            if (PdfImages.ExternalCodec is not null
                && PdfImages.ExternalCodec.TryDecode(Data, Encoding, out int decodedWidth, out int decodedHeight, out var decoded))
            {
                _rgba = decoded;
                if (decodedWidth != Width || decodedHeight != Height)
                {
                    // Ukuran dari kodek lebih dipercaya daripada kamus.
                }

                return _rgba;
            }

            _rgba = new byte[Math.Max(1, Width) * Math.Max(1, Height) * 4];
            return _rgba;
        }

        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);
        var rgba = new byte[width * height * 4];
        int components = Math.Max(1, Components);
        int bits = IsImageMask ? 1 : BitsPerComponent;
        if (ExpectedBytes(width, height, components, bits) > Data.Length)
        {
            // Data lebih pendek dari yang seharusnya: pakai dugaan ukuran sampel.
            bits = GuessBits(Data.Length, width, height, components);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int destination = (y * width + x) * 4;
                if (IsImageMask)
                {
                    double sample = ReadSample(x, y, width, 1);
                    bool paint = Decode is { Length: >= 2 } ? sample >= Decode[0] : sample >= 0.5;
                    rgba[destination] = 255;
                    rgba[destination + 1] = 255;
                    rgba[destination + 2] = 255;
                    rgba[destination + 3] = (byte)(paint ? 255 : 0);
                    continue;
                }

                var values = new double[components];
                for (int c = 0; c < components; c++)
                {
                    double sample = ReadSample(x * components + c, y, width * components, bits);
                    if (Decode is { Length: >= 2 } && c * 2 + 1 < Decode.Length)
                    {
                        sample = Decode[c * 2] + sample * (Decode[c * 2 + 1] - Decode[c * 2]);
                    }

                    values[c] = sample;
                }

                var space = ColorSpace ?? (components switch
                {
                    1 => PdfColorSpace.DeviceGray,
                    4 => PdfColorSpace.DeviceCmyk,
                    _ => PdfColorSpace.DeviceRgb
                });

                space.ToRgb(values, out double red, out double green, out double blue);
                rgba[destination] = ToByte(red);
                rgba[destination + 1] = ToByte(green);
                rgba[destination + 2] = ToByte(blue);

                byte alpha = 255;
                if (Alpha is not null && y * width + x < Alpha.Length)
                {
                    alpha = Alpha[y * width + x];
                }

                if (ColorKeyMask is { Length: >= 2 } key)
                {
                    bool masked = true;
                    for (int c = 0; c < components && c * 2 + 1 < key.Length; c++)
                    {
                        int value = (int)Math.Round(values[c] * 255);
                        if (value < key[c * 2] || value > key[c * 2 + 1]) masked = false;
                    }

                    if (masked) alpha = 0;
                }

                rgba[destination + 3] = alpha;
            }
        }

        _rgba = rgba;
        return rgba;
    }

    private static int ExpectedBytes(int width, int height, int components, int bits) =>
        (width * components * bits + 7) / 8 * height;

    private static int GuessBits(int byteCount, int width, int height, int components)
    {
        foreach (int bits in new[] { 8, 4, 2, 1 })
        {
            if (byteCount >= ExpectedBytes(width, height, components, bits)) return bits;
        }

        return 8;
    }

    private double ReadSample(int index, int row, int rowWidth, int bits)
    {
        switch (bits)
        {
            case 1:
            {
                int perRow = (rowWidth + 7) / 8;
                int byteIndex = row * perRow + index / 8;
                if (byteIndex >= Data.Length) return 0;
                int bit = 7 - (index % 8);
                return (Data[byteIndex] >> bit) & 1;
            }

            case 2:
            {
                int perRow = (rowWidth + 3) / 4;
                int byteIndex = row * perRow + index / 4;
                if (byteIndex >= Data.Length) return 0;
                int shift = 6 - (index % 4) * 2;
                return ((Data[byteIndex] >> shift) & 0x3) / 3.0;
            }

            case 4:
            {
                int perRow = (rowWidth + 1) / 2;
                int byteIndex = row * perRow + index / 2;
                if (byteIndex >= Data.Length) return 0;
                int value = (index % 2) == 0 ? (Data[byteIndex] >> 4) & 0xF : Data[byteIndex] & 0xF;
                return value / 15.0;
            }

            case 16:
            {
                int byteIndex = row * rowWidth * 2 + index * 2;
                if (byteIndex + 1 >= Data.Length) return 0;
                return ((Data[byteIndex] << 8) | Data[byteIndex + 1]) / 65535.0;
            }

            default:
            {
                int byteIndex = row * rowWidth + index;
                return byteIndex < Data.Length ? Data[byteIndex] / 255.0 : 0;
            }
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}

/// <summary>Kodek gambar terkompresi (JPEG/JPEG2000) yang dipasang lapisan luar.</summary>
public interface IPdfImageCodec
{
    bool TryDecode(byte[] encoded, PdfImageEncoding encoding, out int width, out int height, out byte[] rgba);
}

public static class PdfImages
{
    /// <summary>Kodek luar; null berarti JPEG/JPX tidak digambar (wajar pada mesin inti).</summary>
    public static IPdfImageCodec? ExternalCodec { get; set; }

    public static PdfImage? Decode(PdfDocument document, PdfStream stream)
    {
        var dict = stream.Dictionary;
        if (dict.GetNameValue("Subtype") != "Image") return null;

        int width = (int)(document.GetNumber(dict, "Width"));
        int height = (int)(document.GetNumber(dict, "Height"));
        if (width <= 0 || height <= 0) return null;

        bool isMask = dict.GetBool("ImageMask") ?? false;
        var filters = PdfFilters.FiltersOf(dict);
        var encoding = PdfImageEncoding.Raw;
        if (filters.Length > 0)
        {
            string last = PdfFilters.Normalize(filters[^1]);
            encoding = last switch
            {
                "DCTDecode" => PdfImageEncoding.Jpeg,
                "JPXDecode" => PdfImageEncoding.Jpeg2000,
                "CCITTFaxDecode" => PdfImageEncoding.CcittFax,
                "JBIG2Decode" => PdfImageEncoding.Jbig2,
                _ => PdfImageEncoding.Raw
            };
        }

        // DecodeStream mendekripsi stream lalu menjalankan filter sebelum DCT/JPX,
        // sambil mempertahankan payload terkompresi untuk kodek gambar.
        byte[] data;
        try
        {
            data = stream.Decoded();
        }
        catch (PdfException)
        {
            return null;
        }

        var colorSpace = PdfColorSpace.Resolve(dict.Raw("ColorSpace"), document);
        int components = isMask ? 1 : colorSpace?.Components ?? 1;
        if (encoding == PdfImageEncoding.Raw && colorSpace is null && components == 1)
        {
            components = dict.GetNameValue("ColorSpace") == "DeviceRGB" ? 3 : 1;
        }

        byte[]? alpha = null;
        var softMask = document.ResolveStream(dict.Raw("SMask"));
        if (softMask is not null)
        {
            var maskImage = Decode(document, softMask);
            if (maskImage is not null)
            {
                alpha = GrayAlpha(maskImage);
            }
        }

        ushort[]? colorKey = null;
        if (document.Resolve(dict.Raw("Mask")) is PdfArray maskArray && maskArray.Count >= 2)
        {
            colorKey = new ushort[maskArray.Count];
            for (int i = 0; i < maskArray.Count; i++)
            {
                colorKey[i] = (ushort)Math.Clamp((int)(document.ResolveNumber(maskArray.At(i))?.Value ?? 0), 0, 65535);
            }
        }

        double[]? decode = null;
        var decodeArray = document.ResolveArray(dict.Raw("Decode"));
        if (decodeArray is not null && decodeArray.Count >= 2)
        {
            decode = new double[decodeArray.Count];
            for (int i = 0; i < decodeArray.Count; i++)
            {
                decode[i] = document.ResolveNumber(decodeArray.At(i))?.Value ?? 0;
            }
        }

        int bitsPerComponent = (int)(dict.GetNumber("BitsPerComponent") ?? (isMask ? 1 : 8));
        return new PdfImage
        {
            Width = width,
            Height = height,
            Components = components,
            Data = data,
            Encoding = encoding,
            IsImageMask = isMask,
            Interpolate = dict.GetBool("Interpolate") ?? false,
            Alpha = alpha,
            ColorSpace = colorSpace,
            Decode = decode,
            BitsPerComponent = Math.Clamp(bitsPerComponent, 1, 16),
            ColorKeyMask = colorKey
        };
    }

    /// <summary>Topeng lembut dari grup transparansi (/SMask /G).</summary>
    public static PdfImage? DecodeGroupMask(PdfDocument document, PdfStream group)
    {
        var dict = group.Dictionary;
        int width = (int)document.GetNumber(dict, "Width");
        int height = (int)document.GetNumber(dict, "Height");
        if (width > 0 && height > 0)
        {
            return Decode(document, group);
        }

        return null;
    }

    private static byte[] GrayAlpha(PdfImage mask)
    {
        byte[] rgba = mask.ToRgba();
        var alpha = new byte[mask.Width * mask.Height];
        for (int i = 0; i < alpha.Length; i++)
        {
            int source = i * 4;
            alpha[i] = source + 1 < rgba.Length
                ? (byte)((rgba[source] * 30 + rgba[source + 1] * 59 + rgba[source + 2] * 11) / 100)
                : (byte)255;
        }

        return alpha;
    }
}
