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
    private byte[]? _alpha;
    private int? _bits;
    private double[]? _maskValues;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Components { get; init; }

    /// <summary>Sampel mentah (atau byte terkompresi untuk JPEG/JPX).</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public PdfImageEncoding Encoding { get; init; }

    public bool IsImageMask { get; init; }

    public bool Interpolate { get; init; }

    /// <summary>
    /// Saluran alfa 8-bit (satu byte per piksel). Berasal dari /SMask dan baru
    /// dihitung saat benar-benar diminta; pelukis membaca <see cref="SoftMask"/>.
    /// </summary>
    public byte[]? Alpha
    {
        get
        {
            if (_alpha is null && SoftMask is { } mask) _alpha = HitungAlfa(mask);
            return _alpha;
        }
    }

    /// <summary>Topeng lembut (/SMask) yang belum dijadikan larik alfa.</summary>
    internal PdfImage? SoftMask { get; init; }

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

        // Larik nilai dipakai ulang tiap piksel; mengalokasikannya di dalam
        // perulangan berarti tiga juta alokasi untuk satu pindaian A4.
        var values = new double[Math.Max(1, Components)];
        for (int y = 0; y < height; y++)
        {
            int baris = y * width;
            for (int x = 0; x < width; x++)
            {
                int destination = (baris + x) * 4;
                ReadPixel(x, y, values, out double red, out double green, out double blue, out byte alpha);
                rgba[destination] = ToByte(red);
                rgba[destination + 1] = ToByte(green);
                rgba[destination + 2] = ToByte(blue);
                rgba[destination + 3] = alpha;
            }
        }

        _rgba = rgba;
        return rgba;
    }

    /// <summary>Banyak bit tiap komponen, dengan dugaan bila data terpotong.</summary>
    private int Bits => _bits ??= HitungBits();

    private int HitungBits()
    {
        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);
        int components = Math.Max(1, Components);
        int bits = IsImageMask ? 1 : BitsPerComponent;
        if (ExpectedBytes(width, height, components, bits) > Data.Length)
        {
            // Data lebih pendek dari yang seharusnya: pakai dugaan ukuran sampel.
            return GuessBits(Data.Length, width, height, components);
        }

        return bits;
    }

    /// <summary>
    /// Warna satu piksel gambar (0..1, tidak pramultiplied). Satu-satunya tempat
    /// kebijakan /Decode, ruang warna, topeng /Mask, dan alfa bertemu, dipakai
    /// baik oleh konversi RGBA utuh maupun oleh pengambil sampel langsung.
    /// </summary>
    internal void ReadPixel(
        int x, int y, double[] values, out double red, out double green, out double blue, out byte alpha)
    {
        int width = Math.Max(1, Width);
        if (IsImageMask)
        {
            double coverage = ReadSample(x, y, width, 1);
            bool paint = Decode is { Length: >= 2 } ? coverage >= Decode[0] : coverage >= 0.5;
            red = green = blue = 1;
            alpha = (byte)(paint ? 255 : 0);
            return;
        }

        int components = Math.Max(1, Components);
        int bits = Bits;
        int rowWidth = width * components;
        for (int c = 0; c < components; c++)
        {
            double sample = ReadSample(x * components + c, y, rowWidth, bits);
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
        space.ToRgb(values, out red, out green, out blue);

        alpha = 255;
        int index = y * width + x;
        if (_alpha is { Length: > 0 } alfa)
        {
            if (index < alfa.Length) alpha = alfa[index];
        }
        else if (SoftMask is { } mask)
        {
            alpha = mask.GrayAt(x, y, width, Math.Max(1, Height));
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
    }

    /// <summary>Kelegapan topeng lembut pada satu piksel gambar induk.</summary>
    internal byte GrayAt(int x, int y, int parentWidth, int parentHeight)
    {
        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);

        // Topeng biasanya seukuran gambar induk; bila tidak, ambil piksel terdekat.
        int mx = width == parentWidth ? x : (int)((long)x * width / Math.Max(1, parentWidth));
        int my = height == parentHeight ? y : (int)((long)y * height / Math.Max(1, parentHeight));
        _maskValues ??= new double[Math.Max(1, Components)];
        ReadPixel(
            Math.Clamp(mx, 0, width - 1),
            Math.Clamp(my, 0, height - 1),
            _maskValues,
            out double red,
            out double green,
            out double blue,
            out _);

        return (byte)((ToByte(red) * 30 + ToByte(green) * 59 + ToByte(blue) * 11) / 100);
    }

    /// <summary>Larik alfa utuh dari topeng lembut, satu byte per piksel.</summary>
    private static byte[] HitungAlfa(PdfImage mask)
    {
        byte[] rgba = mask.ToRgba();
        var alpha = new byte[Math.Max(1, mask.Width) * Math.Max(1, mask.Height)];
        for (int i = 0; i < alpha.Length; i++)
        {
            int source = i * 4;
            alpha[i] = source + 3 < rgba.Length
                ? (byte)((rgba[source] * 30 + rgba[source + 1] * 59 + rgba[source + 2] * 11) / 100)
                : (byte)255;
        }

        return alpha;
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

/// <summary>
/// Pengambil warna gambar pada satu titik pecahan dengan interpolasi bilinear.
/// Membaca sampel mentah langsung bila larik RGBA belum tersedia, sehingga
/// gambar besar yang tampil kecil (kop surat 1507×2050 px menjadi ±90 px) tidak
/// perlu dikonversi utuh lebih dulu.
/// </summary>
internal sealed class PdfImageSampler
{
    private readonly PdfImage _image;
    private readonly byte[]? _rgba;
    private readonly double[] _values;

    public PdfImageSampler(PdfImage image, byte[]? rgba)
    {
        _image = image;
        _rgba = rgba;
        _values = new double[Math.Max(1, image.Components)];
    }

    public void Sample(
        double x, double y, out double red, out double green, out double blue, out double alpha)
    {
        // Ruang gambar PDF berawal di kiri-bawah, sedangkan baris byte PNG/JPEG
        // berawal dari sisi atas. Pemanggil sudah membalik sumbu Y.
        double fx = x - 0.5;
        double fy = y - 0.5;
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        double tx = fx - x0;
        double ty = fy - y0;

        double sumR = 0, sumG = 0, sumB = 0, sumA = 0;
        for (int dy = 0; dy <= 1; dy++)
        {
            double wy = dy == 0 ? 1.0 - ty : ty;
            if (wy <= 0) continue;
            int py = Math.Clamp(y0 + dy, 0, _image.Height - 1);
            for (int dx = 0; dx <= 1; dx++)
            {
                double wx = dx == 0 ? 1.0 - tx : tx;
                if (wx <= 0) continue;
                int px = Math.Clamp(x0 + dx, 0, _image.Width - 1);
                double w = wx * wy;

                Read(px, py, out double r, out double g, out double b, out double a);

                // Warna dijumlahkan dalam bentuk pramultiplied supaya piksel
                // transparan tidak menarik warna ke arah hitam.
                sumR += w * r * a;
                sumG += w * g * a;
                sumB += w * b * a;
                sumA += w * a;
            }
        }

        if (sumA <= 0.0001)
        {
            red = green = blue = alpha = 0;
            return;
        }

        red = sumR / sumA;
        green = sumG / sumA;
        blue = sumB / sumA;
        alpha = sumA;
    }

    private void Read(int x, int y, out double red, out double green, out double blue, out double alpha)
    {
        if (_rgba is { } rgba)
        {
            int index = (y * _image.Width + x) * 4;
            if (index + 3 >= rgba.Length)
            {
                red = green = blue = alpha = 0;
                return;
            }

            red = rgba[index] / 255.0;
            green = rgba[index + 1] / 255.0;
            blue = rgba[index + 2] / 255.0;
            alpha = rgba[index + 3] / 255.0;
            return;
        }

        _image.ReadPixel(x, y, _values, out red, out green, out blue, out byte kelegapan);
        alpha = kelegapan / 255.0;
    }
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
        int ccittIndex = -1;
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
            // /DecodeParms faks beralamat pada posisi CCITT di rantai filter.
            for (int i = 0; i < filters.Length; i++)
            {
                if (PdfFilters.Normalize(filters[i]) == "CCITTFaxDecode") ccittIndex = i;
            }
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

        // JBIG2 belum mempunyai dekoder jelaga sendiri: lebih aman mengosongkan
        // gambar daripada merawat data terkompresi sebagai sampel raw (halaman
        // jadi hitam bengkok).
        if (encoding == PdfImageEncoding.Jbig2)
        {
            return null;
        }

        bool sudahFax = false;
        if (encoding == PdfImageEncoding.CcittFax)
        {
            var parms = PdfFilters.ParmsOf(dict, filters.Length);
            var parm = ccittIndex >= 0 && ccittIndex < parms.Length ? parms[ccittIndex] : null;
            int k = parm?.GetInt("K") ?? 0;
            bool endOfLine = parm?.GetBool("EndOfLine") ?? false;
            bool byteAligned = parm?.GetBool("EncodedByteAlign") ?? false;
            bool blackIs1 = parm?.GetBool("BlackIs1") ?? false;
            int kolom = (int)(parm?.GetNumber("Columns") ?? width);
            int baris = (int)(parm?.GetNumber("Rows") ?? height);
            var fax = PdfCcitt.Decode(data, kolom, baris, k, endOfLine, byteAligned, blackIs1);
            if (fax is null) return null;

            // Bitmap telah raster (bit 1 = putih) sehingga tidak ada lagi data
            // terkompresi; sampelnya selalu 1 bit tiap piksel.
            data = fax;
            encoding = PdfImageEncoding.Raw;
            sudahFax = true;
        }

        var colorSpace = PdfColorSpace.Resolve(dict.Raw("ColorSpace"), document);
        int components = isMask ? 1 : colorSpace?.Components ?? 1;
        if (encoding == PdfImageEncoding.Raw && colorSpace is null && components == 1)
        {
            components = dict.GetNameValue("ColorSpace") == "DeviceRGB" ? 3 : 1;
        }

        // Topeng lembut hanya diurai; larik alfanya dibuat bila ada yang meminta
        // (penulis PDF atau gambar yang tampil sebesar aslinya). Mengonversi
        // seluruh topeng untuk kop surat yang tampil ±90 px adalah kerja sia-sia.
        PdfImage? softMaskImage = null;
        var softMask = document.ResolveStream(dict.Raw("SMask"));
        if (softMask is not null)
        {
            softMaskImage = Decode(document, softMask);
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

        int bitsPerComponent = sudahFax ? 1 : (int)(dict.GetNumber("BitsPerComponent") ?? (isMask ? 1 : 8));
        return new PdfImage
        {
            Width = width,
            Height = height,
            Components = components,
            Data = data,
            Encoding = encoding,
            IsImageMask = isMask,
            Interpolate = dict.GetBool("Interpolate") ?? false,
            SoftMask = softMaskImage,
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

}
