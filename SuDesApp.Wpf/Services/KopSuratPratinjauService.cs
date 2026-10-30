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
    /// </summary>
    public static class KopSuratPratinjauService
    {
        /// <summary>Lebar raster (piksel) — cukup tajam untuk panel pratinjau.</summary>
        private const double LebarGambar = 620.0;

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

            gambar.Freeze();
            return gambar;
        }
    }
}
