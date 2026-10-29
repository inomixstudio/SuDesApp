using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SudesPdf.Core;

namespace SudesPdf;

/// <summary>
/// Kodek gambar terkompresi untuk mesin <see cref="SudesPdf.Core"/> memakai
/// Windows Imaging Component (WIC) — bagian dari sistem operasi Windows, bukan
/// pustaka pihak ketiga yang harus ikut didistribusikan.
///
/// Sambungan ini wajib: berkas PDF menyimpan foto dan logo berformat .jpg sebagai
/// gambar DCTDecode, dan mesin inti sengaja tidak memuat dekoder JPEG sendiri.
/// Tanpa kodek, gambar tersebut tidak digambar sama sekali — kop surat dan SK
/// kehilangan lambangnya tanpa pesan galat.
/// </summary>
public sealed class WicPdfImageCodec : IPdfImageCodec
{
    public bool TryDecode(byte[] encoded, PdfImageEncoding encoding, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = Array.Empty<byte>();
        if (encoded.Length == 0) return false;

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;

            BitmapSource frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32)
            {
                frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            int w = frame.PixelWidth;
            int h = frame.PixelHeight;
            if (w <= 0 || h <= 0) return false;

            int stride = w * 4;
            var bgra = new byte[stride * h];
            frame.CopyPixels(bgra, stride, 0);

            // Mesin inti membaca gambar sebagai R, G, B, A; WIC menyerahkan
            // B, G, R, A. Urutan kanal dibalik di sini, bukan di pemakai.
            var hasil = new byte[bgra.Length];
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                hasil[i] = bgra[i + 2];
                hasil[i + 1] = bgra[i + 1];
                hasil[i + 2] = bgra[i];
                hasil[i + 3] = bgra[i + 3];
            }

            width = w;
            height = h;
            rgba = hasil;
            return true;
        }
        catch (Exception)
        {
            // Format yang tidak dikenal WIC (mis. JPX pada Windows tanpa kodek
            // tersebut) dibiarkan kosong; halaman tetap dirender selebihnya.
            return false;
        }
    }
}
