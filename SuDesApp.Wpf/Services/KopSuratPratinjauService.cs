using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SudesPdf;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pratinjau kop surat di Pengaturan Surat: PDF kop dicetak lewat generator
    /// asli (<see cref="KopSuratCetak"/>) lalu halamannya diraster, sehingga kop di
    /// layar dan kop di kertas tidak mungkin berbeda tata letak.
    ///
    /// Hanya kop yang dicetak — bukan surat penuh — jadi raster-nya murah dan aman
    /// dipanggil setiap kali isian berubah.
    ///
    /// <para>
    /// Halaman pada <see cref="KopSuratCetak"/> sengaja longgar (kop terpanjang
    /// pun muat satu halaman). Raster lalu dipangkas ke batas tinta + margin bawah,
    /// sehingga pratinjau tidak menyisakan pita kertas kosong di bawah garis ganda
    /// yang membuat kop terlihat kecil dan jauh dari blok tanda tangan.
    /// </para>
    /// </summary>
    public static class KopSuratPratinjauService
    {
        /// <summary>Lebar raster (piksel) — cukup tajam untuk panel pratinjau.</summary>
        private const double LebarGambar = 620.0;

        /// <summary>Ambang keputihan piksel saat mencari batas tinta terakhir (0–255).</summary>
        private const byte AmbangPutih = 250;

        public static async Task<BitmapSource?> BuatAsync(
            DesaData? desa,
            string? logoPath,
            UkuranKertasSurat ukuranKertas,
            CancellationToken cancellationToken = default)
        {
            byte[] pdf = KopSuratCetak.BuatPdf(desa, logoPath, ukuranKertas);
            cancellationToken.ThrowIfCancellationRequested();

            await using var dokumen = await Document.LoadAsync(pdf, cancellationToken).ConfigureAwait(false);
            if (dokumen.PageCount == 0) return null;

            double skala = LebarGambar / Math.Max(1.0, dokumen.PageSizes[0].Width);
            var gambar = await dokumen
                .RenderAsync(0, skala, cancellationToken)
                .ConfigureAwait(false);

            int batasBawah = BatasTintaBawahPiksel(gambar);
            if (batasBawah < 0 || batasBawah >= gambar.PixelHeight - 1) return gambar;

            // Batas tinta + margin bawah halaman (sama dengan surat tercetak).
            int tinggiPangkas = batasBawah + 1 + (int)Math.Ceiling(KerapatanSurat.Aktif.MarginBawah * skala);
            if (tinggiPangkas >= gambar.PixelHeight) return gambar;

            var piksel = new byte[gambar.PixelWidth * tinggiPangkas * 4];
            gambar.CopyPixels(
                new System.Windows.Int32Rect(0, 0, gambar.PixelWidth, tinggiPangkas),
                piksel, gambar.PixelWidth * 4, 0);

            var terpotong = BitmapSource.Create(
                gambar.PixelWidth, tinggiPangkas, gambar.DpiX, gambar.DpiY,
                gambar.Format, gambar.Palette, piksel, gambar.PixelWidth * 4);
            terpotong.Freeze();
            return terpotong;
        }

        /// <summary>
        /// Baris paling bawah yang masih memuat tinta (piksel), dihitung dari tepi
        /// bawah gambar. -1 bila gambar kosong/semua putih.
        /// </summary>
        private static int BatasTintaBawahPiksel(BitmapSource gambar)
        {
            int lebar = gambar.PixelWidth;
            int tinggi = gambar.PixelHeight;
            if (lebar <= 0 || tinggi <= 0) return -1;

            byte[] piksel = new byte[lebar * tinggi * 4];
            gambar.CopyPixels(piksel, lebar * 4, 0);

            for (int y = tinggi - 1; y >= 0; y--)
            {
                int baris = y * lebar * 4;
                for (int x = 0; x < lebar; x++)
                {
                    int i = baris + x * 4;
                    if (piksel[i] < AmbangPutih ||
                        piksel[i + 1] < AmbangPutih ||
                        piksel[i + 2] < AmbangPutih)
                    {
                        return y;
                    }
                }
            }

            return -1;
        }
    }
}
