using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SudesPdf;
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
    /// lalu hanya halaman pertamanya digambar menjadi bitmap lewat library SudesPdf.
    /// Hasilnya ditampilkan di dalam dialog supaya pengguna yakin sebelum menyimpan.
    /// </summary>
    public interface ITemplateSuratPratinjau
    {
        Task<PratinjauTemplateSurat?> BuatAsync(TemplateSuratKustom template, CancellationToken ct = default);
    }

    public class TemplateSuratPratinjauService : ITemplateSuratPratinjau
    {
        /// <summary>Lebar gambar pratinjau (piksel) — cukup untuk membaca susunan surat.</summary>
        private const double LebarGambar = 620.0;

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

            var pdf = await _generator.BuatPdfAsync(template, nilai, nomor, DateTime.Now).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (pdf.Length == 0) return null;

            await using var document = await Document.LoadAsync(pdf, ct).ConfigureAwait(false);
            if (document.PageCount == 0) return null;
            var gambar = await document.RenderAsync(0, LebarGambar / Math.Max(1.0, document.PageSizes[0].Width), ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Pratinjau singkat template dibuat: {Halaman} halaman ({Ukuran} byte).",
                document.PageCount, pdf.Length);

            return new PratinjauTemplateSurat
            {
                Gambar = gambar,
                JumlahHalaman = document.PageCount
            };
        }

    }
}
