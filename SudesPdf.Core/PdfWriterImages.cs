using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Gambar siap tanam untuk dokumen yang ditulis SudesPdf. JPEG dipakai apa adanya
/// (DCTDecode), PNG dibaca menjadi piksel mentah lalu dipadatkan ulang
/// (FlateDecode). Saluran alfa dikirim sebagai gambar topeng (/SMask) sehingga
/// logo dan kode QR tetap tampak benar di atas kertas putih.
/// </summary>
public sealed class PdfWriterImage
{
    private PdfWriterImage(int width, int height, string colorSpace, bool jpeg, byte[] data, byte[]? alpha)
    {
        Width = width;
        Height = height;
        ColorSpace = colorSpace;
        Jpeg = jpeg;
        Data = data;
        Alpha = alpha;
    }

    /// <summary>Lebar gambar dalam piksel.</summary>
    public int Width { get; }

    /// <summary>Tinggi gambar dalam piksel.</summary>
    public int Height { get; }

    /// <summary>Bit per komponen (selalu 8 setelah pembacaan).</summary>
    public int BitsPerComponent => 8;

    /// <summary>Ruang warna PDF (“/DeviceRGB”, “/DeviceGray”, “/DeviceCMYK”).</summary>
    public string ColorSpace { get; }

    /// <summary>Benar bila <see cref="Data"/> adalah berkas JPEG utuh.</summary>
    public bool Jpeg { get; }

    /// <summary>Cuplikan gambar (JPEG utuh atau piksel mentah).</summary>
    public byte[] Data { get; }

    /// <summary>Saluran alfa 8-bit (satu byte per piksel), bila ada.</summary>
    public byte[]? Alpha { get; }

    /// <summary>Nomor objek setelah ditulis (supaya gambar ditulis sekali saja).</summary>
    internal int ObjectId { get; set; }

    /// <summary>
    /// Kerapatan tertinggi yang perlu ditanam (dpi). Logo kop tampil 60 pt —
    /// 250 px pada 300 dpi — sehingga pindaian 1507 px hanya menambah ukuran
    /// berkas tanpa satu pun piksel yang terlihat saat dicetak.
    /// </summary>
    public const float DpiCetak = 300f;

    /// <summary>
    /// Piksel yang benar-benar dipakai bila gambar tampil selebar
    /// <paramref name="widthPt"/> dan setinggi <paramref name="heightPt"/> point.
    /// Tidak pernah melebihi ukuran asli; rasio gambar dipertahankan.
    /// </summary>
    public (int Width, int Height) PikselCetak(float widthPt, float heightPt)
    {
        if (Jpeg || widthPt <= 0f || heightPt <= 0f || Width <= 0 || Height <= 0)
        {
            return (Width, Height);
        }

        double factor = Math.Min(
            widthPt * DpiCetak / 72.0 / Width,
            heightPt * DpiCetak / 72.0 / Height);
        if (factor >= 1.0) return (Width, Height);

        return (
            Math.Clamp((int)Math.Round(Width * factor), 1, Width),
            Math.Clamp((int)Math.Round(Height * factor), 1, Height));
    }

    /// <summary>
    /// Gambar siap tanam untuk ukuran tampil tertentu: versi yang diperkecil bila
    /// resolusinya jauh melebihi kebutuhan cetak, atau gambar ini sendiri bila
    /// sudah cukup kecil. JPEG tidak diperkecil karena datanya masih terkompresi
    /// (butuh kodek untuk diurai).
    /// </summary>
    public PdfWriterImage UntukUkuran(float widthPt, float heightPt)
    {
        var (width, height) = PikselCetak(widthPt, heightPt);
        return width == Width && height == Height ? this : Perkecil(width, height);
    }

    /// <summary>
    /// Perkecil dengan rata-rata luas (box filter): setiap piksel tujuan adalah
    /// purata piksel sumber yang tertutup di wilayahnya. Rata-rata dilakukan
    /// dalam bentuk pramultiplied supaya tepi transparan tidak menarik warna ke
    /// arah hitam, sama seperti aturan gambar di rasterizer.
    /// </summary>
    private PdfWriterImage Perkecil(int width, int height)
    {
        int komponen = ColorSpace == "/DeviceGray" ? 1 : 3;
        var data = new byte[width * height * komponen];
        byte[]? alfaSumber = Alpha;
        var alpha = alfaSumber is null ? null : new byte[width * height];
        var jumlah = new double[komponen];

        for (int y = 0; y < height; y++)
        {
            double y0 = (double)y * Height / height;
            double y1 = (double)(y + 1) * Height / height;
            int sy0 = (int)Math.Floor(y0);
            int sy1 = Math.Min(Height, (int)Math.Ceiling(y1));

            for (int x = 0; x < width; x++)
            {
                double x0 = (double)x * Width / width;
                double x1 = (double)(x + 1) * Width / width;
                int sx0 = (int)Math.Floor(x0);
                int sx1 = Math.Min(Width, (int)Math.Ceiling(x1));

                Array.Clear(jumlah, 0, komponen);
                double totalA = 0, totalW = 0;

                for (int sy = sy0; sy < sy1; sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    if (wy <= 0) continue;

                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        double wx = Math.Min(x1, sx + 1) - Math.Max(x0, sx);
                        if (wx <= 0) continue;

                        double w = wx * wy;
                        int sumber = sy * Width + sx;
                        double a = alfaSumber is null ? 1.0 : alfaSumber[sumber] / 255.0;
                        totalW += w;
                        totalA += w * a;
                        for (int c = 0; c < komponen; c++)
                        {
                            jumlah[c] += w * a * Data[sumber * komponen + c] / 255.0;
                        }
                    }
                }

                int tujuan = y * width + x;
                if (alpha is not null) alpha[tujuan] = KeByte(totalA / Math.Max(totalW, 1e-9));

                // Warna dibagi alfa purata supaya hasilnya kembali "straight alpha".
                double pembagi = Math.Max(totalA, 1e-9);
                for (int c = 0; c < komponen; c++)
                {
                    data[tujuan * komponen + c] = KeByte(jumlah[c] / pembagi);
                }
            }
        }

        return new PdfWriterImage(width, height, ColorSpace, false, data, alpha);
    }

    private static byte KeByte(double nilai) => (byte)Math.Clamp((int)Math.Round(nilai * 255), 0, 255);

    /// <summary>Baca gambar dari berkas (PNG atau JPEG).</summary>
    public static PdfWriterImage FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromBytes(File.ReadAllBytes(path));
    }

    /// <summary>Baca gambar dari byte (PNG atau JPEG).</summary>
    public static PdfWriterImage FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (IsPng(bytes)) return FromPng(bytes);
        if (IsJpeg(bytes)) return FromJpeg(bytes);
        throw new PdfException("Gambar harus berformat PNG atau JPEG.");
    }

    private static bool IsPng(byte[] data) =>
        data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    private static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8;

    // ------------------------------------------------------------------ PNG

    private static PdfWriterImage FromPng(byte[] bytes)
    {
        int width = 0, height = 0, bitDepth = 8, colorType = 0;
        bool headerSeen = false;
        byte[]? palette = null;
        byte[]? transparency = null;
        var idat = new List<byte[]>();

        int offset = 8;
        while (offset + 8 <= bytes.Length)
        {
            int length = ReadInt32(bytes, offset);
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            int dataStart = offset + 8;
            if (length < 0 || dataStart + length + 4 > bytes.Length)
                throw new PdfException("Berkas PNG rusak (panjang blok tidak sah).");

            switch (type)
            {
                case "IHDR":
                    width = ReadInt32(bytes, dataStart);
                    height = ReadInt32(bytes, dataStart + 4);
                    bitDepth = bytes[dataStart + 8];
                    colorType = bytes[dataStart + 9];
                    if (bytes[dataStart + 12] != 0)
                        throw new PdfException("PNG berselang-seling (interlaced) belum didukung.");
                    if (width <= 0 || height <= 0)
                        throw new PdfException("Ukuran PNG tidak sah.");
                    headerSeen = true;
                    break;
                case "PLTE":
                    palette = new byte[length];
                    Array.Copy(bytes, dataStart, palette, 0, length);
                    break;
                case "tRNS":
                    transparency = new byte[length];
                    Array.Copy(bytes, dataStart, transparency, 0, length);
                    break;
                case "IDAT":
                    var chunk = new byte[length];
                    Array.Copy(bytes, dataStart, chunk, 0, length);
                    idat.Add(chunk);
                    break;
                case "IEND":
                    offset = bytes.Length;
                    continue;
            }

            offset = dataStart + length + 4;
        }

        if (!headerSeen) throw new PdfException("PNG tidak memuat kepala gambar (IHDR).");
        if (idat.Count == 0) throw new PdfException("PNG tidak memuat data gambar (IDAT).");

        byte[] pixels = Unfilter(Inflate(idat), width, height, bitDepth, colorType);
        return Expand(pixels, width, height, bitDepth, colorType, palette, transparency);
    }

    /// <summary>Susun kembali baris-baris piksel PNG (membuka filter per baris).</summary>
    private static byte[] Unfilter(byte[] raw, int width, int height, int bitDepth, int colorType)
    {
        int channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };

        int bitsPerPixel = channels * bitDepth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int filterBytesPerRow = Math.Max(1, bitsPerPixel / 8);
        long needed = (long)(stride + 1) * height;
        if (raw.Length < needed) throw new PdfException("Data PNG terpotong.");

        var pixels = new byte[stride * height];
        var previous = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            int source = y * (stride + 1) + 1;
            int target = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int left = x >= filterBytesPerRow ? pixels[target + x - filterBytesPerRow] : 0;
                int above = previous[x];
                int aboveLeft = x >= filterBytesPerRow ? previous[x - filterBytesPerRow] : 0;
                int value = raw[source + x];
                value = filter switch
                {
                    0 => value,
                    1 => value + left,
                    2 => value + above,
                    3 => value + ((left + above) >> 1),
                    4 => value + Paeth(left, above, aboveLeft),
                    _ => throw new PdfException("Filter PNG tidak dikenal: " + filter),
                };
                pixels[target + x] = (byte)value;
            }

            Array.Copy(pixels, target, previous, 0, stride);
        }

        return pixels;
    }

    private static int Paeth(int left, int above, int aboveLeft)
    {
        int estimate = left + above - aboveLeft;
        int dl = Math.Abs(estimate - left);
        int da = Math.Abs(estimate - above);
        int dal = Math.Abs(estimate - aboveLeft);
        if (dl <= da && dl <= dal) return left;
        return da <= dal ? above : aboveLeft;
    }

    /// <summary>Ubah piksel PNG menjadi cuplikan 8-bit yang dipakai PDF.</summary>
    private static PdfWriterImage Expand(
        byte[] pixels, int width, int height, int bitDepth, int colorType, byte[]? palette, byte[]? transparency)
    {
        switch (colorType)
        {
            case 0:
            {
                byte[] gray = ToEightBit(pixels, width * height, bitDepth);
                return new PdfWriterImage(width, height, "/DeviceGray", false, gray, null);
            }

            case 2:
            {
                byte[] rgb = new byte[width * height * 3];
                if (bitDepth == 8)
                {
                    Array.Copy(pixels, rgb, Math.Min(rgb.Length, pixels.Length));
                }
                else
                {
                    for (int i = 0; i < width * height; i++)
                    {
                        rgb[i * 3] = pixels[i * 6];
                        rgb[i * 3 + 1] = pixels[i * 6 + 2];
                        rgb[i * 3 + 2] = pixels[i * 6 + 4];
                    }
                }

                return new PdfWriterImage(width, height, "/DeviceRGB", false, rgb, null);
            }

            case 3:
            {
                if (palette is null) throw new PdfException("PNG berpalet tanpa tabel warna (PLTE).");
                byte[] indices = ToEightBit(pixels, width * height, bitDepth);
                byte[] rgb = new byte[width * height * 3];
                byte[]? alpha = transparency is null ? null : new byte[width * height];
                for (int i = 0; i < indices.Length; i++)
                {
                    int index = indices[i];
                    int p = index * 3;
                    rgb[i * 3] = p + 2 < palette.Length ? palette[p] : (byte)0;
                    rgb[i * 3 + 1] = p + 2 < palette.Length ? palette[p + 1] : (byte)0;
                    rgb[i * 3 + 2] = p + 2 < palette.Length ? palette[p + 2] : (byte)0;
                    if (alpha is not null) alpha[i] = index < transparency!.Length ? transparency[index] : (byte)255;
                }

                return new PdfWriterImage(width, height, "/DeviceRGB", false, rgb, alpha);
            }

            case 4:
            {
                byte[] gray = new byte[width * height];
                byte[] alpha = new byte[width * height];
                for (int i = 0; i < gray.Length; i++)
                {
                    gray[i] = pixels[bitDepth == 16 ? i * 4 : i * 2];
                    alpha[i] = pixels[bitDepth == 16 ? i * 4 + 2 : i * 2 + 1];
                }

                return new PdfWriterImage(width, height, "/DeviceGray", false, gray, alpha);
            }

            default:
            {
                byte[] rgb = new byte[width * height * 3];
                byte[] alpha = new byte[width * height];

                // RGBA 8-bit: satu byte per kanal (R,G,B,A). RGBA 16-bit: dua
                // byte per kanal — PDF memakai byte tinggi tiap kanal.
                if (bitDepth == 16)
                {
                    for (int i = 0; i < alpha.Length; i++)
                    {
                        int o = i * 8;
                        rgb[i * 3] = pixels[o];
                        rgb[i * 3 + 1] = pixels[o + 2];
                        rgb[i * 3 + 2] = pixels[o + 4];
                        alpha[i] = pixels[o + 6];
                    }
                }
                else
                {
                    for (int i = 0; i < alpha.Length; i++)
                    {
                        int o = i * 4;
                        rgb[i * 3] = pixels[o];
                        rgb[i * 3 + 1] = pixels[o + 1];
                        rgb[i * 3 + 2] = pixels[o + 2];
                        alpha[i] = pixels[o + 3];
                    }
                }

                return new PdfWriterImage(width, height, "/DeviceRGB", false, rgb, alpha);
            }
        }
    }

    /// <summary>Rentangkan contoh 1/2/4 bit menjadi satu byte per piksel (0..255).</summary>
    private static byte[] ToEightBit(byte[] pixels, int count, int bitDepth)
    {
        if (bitDepth == 8) return pixels;

        var result = new byte[count];
        if (bitDepth == 16)
        {
            for (int i = 0; i < count; i++) result[i] = pixels[i * 2];
            return result;
        }

        int mask = (1 << bitDepth) - 1;
        int scale = 255 / mask;
        for (int i = 0; i < count; i++)
        {
            int bitIndex = i * bitDepth;
            int value = (pixels[bitIndex >> 3] >> (8 - bitDepth - (bitIndex & 7))) & mask;
            result[i] = (byte)(value * scale);
        }

        return result;
    }

    private static byte[] Inflate(List<byte[]> chunks)
    {
        int total = 0;
        foreach (var chunk in chunks) total += chunk.Length;

        var input = new MemoryStream(total);
        foreach (var chunk in chunks) input.Write(chunk, 0, chunk.Length);
        input.Position = 0;

        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    // ----------------------------------------------------------------- JPEG

    private static PdfWriterImage FromJpeg(byte[] bytes)
    {
        int offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            int marker = bytes[offset + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            int length = (bytes[offset + 2] << 8) | bytes[offset + 3];
            bool isFrame = marker >= 0xC0 && marker <= 0xCF &&
                marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrame && offset + 9 < bytes.Length)
            {
                int height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                int width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                int components = bytes[offset + 9];
                string colorSpace = components switch
                {
                    1 => "/DeviceGray",
                    4 => "/DeviceCMYK",
                    _ => "/DeviceRGB",
                };

                return new PdfWriterImage(width, height, colorSpace, true, bytes, null);
            }

            offset += 2 + Math.Max(length, 2);
        }

        throw new PdfException("Berkas JPEG tidak memuat ukuran gambar.");
    }
}
