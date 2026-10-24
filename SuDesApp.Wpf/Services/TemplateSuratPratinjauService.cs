using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using PdfiumViewer;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;

namespace SuDesApp.Wpf.Services
{
    /// <summary>Hasil pratinjau singkat sebuah template surat.</summary>
    public sealed class PratinjauTemplateSurat
    {
        /// <summary>Gambar halaman pertama surat contoh.</summary>
        public BitmapSource? Gambar { get; init; }

        /// <summary>Jumlah halaman PDF contoh (pratinjau hanya menampilkan halaman pertama).</summary>
        public int JumlahHalaman { get; init; }

        /// <summary>Keterangan singkat di bawah gambar.</summary>
        public string Keterangan => JumlahHalaman <= 1
            ? "Halaman 1 dari 1"
            : $"Halaman 1 dari {JumlahHalaman}";
    }

    /// <summary>
    /// Pratinjau singkat template surat: susunan surat belum tentu sudah disimpan, jadi
    /// PDF contoh dicetak lebih dulu (memakai isi contoh seperti pratinjau template biasa),
    /// lalu hanya halaman pertamanya digambar menjadi bitmap PDFium. Hasilnya ditampilkan
    /// di dalam dialog supaya pengguna yakin sebelum menyimpan tanpa berpindah jendela.
    /// </summary>
    public interface ITemplateSuratPratinjau
    {
        Task<PratinjauTemplateSurat?> BuatAsync(TemplateSuratKustom template, CancellationToken ct = default);
    }

    public class TemplateSuratPratinjauService : ITemplateSuratPratinjau
    {
        /// <summary>Lebar gambar pratinjau (piksel) — cukup untuk membaca susunan surat.</summary>
        private const double LebarGambar = 620.0;

        /// <summary>Batas halaman yang digambar; sisanya hanya diberitahukan jumlahnya.</summary>
        private const int HalamanMaks = 1;

        private readonly TemplateSuratGenerator _generator;
        private readonly ILogger<TemplateSuratPratinjauService> _logger;

        public TemplateSuratPratinjauService(
            TemplateSuratGenerator generator,
            ILogger<TemplateSuratPratinjauService> logger)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PratinjauTemplateSurat?> BuatAsync(
            TemplateSuratKustom template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (template.JumlahElemen == 0) return null;

            var nilai = TemplateSuratNilai.NilaiContoh(template);
            int urut;
            string nomor = template.NomorBerikutnya(DateTime.Now, out urut);

            // Pencetakan PDF dan penggambaran halaman dijalankan di luar thread antarmuka
            // supaya dialog tetap responsif; BitmapSource baru dibuat setelah kembali ke
            // thread pemanggil (BitmapSource dari HBITMAP terikat ke thread pembuatnya).
            var (gambar, halaman) = await Task.Run(() =>
                Cetak(template, nilai, nomor, ct), ct).ConfigureAwait(true);

            if (gambar == null) return null;

            try
            {
                var sumber = ToBitmapSource(gambar);
                // Dibekukan supaya gambar aman dipakai lintas thread (meski pemanggilnya
                // bukan thread antarmuka, mis. pada pengujian).
                sumber.Freeze();

                return new PratinjauTemplateSurat
                {
                    Gambar = sumber,
                    JumlahHalaman = halaman
                };
            }
            finally
            {
                gambar.Dispose();
            }
        }

        /// <summary>Cetak PDF contoh lalu gambar halamannya menjadi bitmap GDI.</summary>
        private (System.Drawing.Bitmap? Gambar, int Halaman) Cetak(
            TemplateSuratKustom template,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? nilai,
            string? nomor,
            CancellationToken ct)
        {
            var pdf = _generator.BuatPdfAsync(template, nilai, nomor, DateTime.Now)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            ct.ThrowIfCancellationRequested();

            if (pdf.Length == 0) return (null, 0);

            using var dokumen = PdfDocument.Load(new MemoryStream(pdf));
            if (dokumen.PageCount == 0) return (null, 0);

            var ukuran = dokumen.PageSizes[0];
            double skala = LebarGambar / Math.Max(1.0, ukuran.Width);
            int lebar = Math.Max(1, (int)(ukuran.Width * skala));
            int tinggi = Math.Max(1, (int)(ukuran.Height * skala));

            // Hanya halaman pertama yang digambar (pratinjau singkat), halaman lain
            // cukup disebut jumlahnya pada keterangan.
            int halamanDigambar = Math.Min(HalamanMaks, dokumen.PageCount);
            System.Drawing.Bitmap? gambar = null;

            for (int i = 0; i < halamanDigambar; i++)
            {
                gambar = (System.Drawing.Bitmap)dokumen.Render(i, lebar, tinggi, 96f, 96f, PdfRenderFlags.LcdText);
            }

            _logger.LogInformation(
                "Pratinjau singkat template dibuat: {Halaman} halaman ({Ukuran} byte).",
                dokumen.PageCount, pdf.Length);

            return (gambar, dokumen.PageCount);
        }

        private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
