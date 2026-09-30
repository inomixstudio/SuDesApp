using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.GeneratorPdf;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji render label status alur persetujuan di atas surat.
    ///
    /// Label ini hanya muncul ketika surat sedang dalam alur atau sudah pernah
    /// dicetak — surat biasa tidak pernah diberi label (lihat
    /// <see cref="SuDesApp.Data.Models.StatusPersetujuanSurat.LabelCetak"/>).
    /// Uji render diperlukan karena api QuestPDF (Border/BorderColor/AlignCenter)
    /// baru dipakai di jalur cetak, dan kesalahannya hanya akan terasa saat
    /// operator mencetak surat ditolak/draf.
    /// </summary>
    public class LabelStatusSuratRenderTests
    {
        [Fact]
        public void LabelPeringatan_TercetakSatuHalaman()
        {
            byte[] pdf = Render(kolom =>
                SuratRenderer.LabelStatusSurat(kolom.Item(), "DITOLAK — TIDAK SAH, JANGAN DIPAKAI", peringatan: true));

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
            Assert.Equal(1, HitungHalamanPdf(pdf));
        }

        [Fact]
        public void LabelSalinan_TercetakSatuHalaman()
        {
            byte[] pdf = Render(kolom =>
                SuratRenderer.LabelStatusSurat(kolom.Item(), "SALINAN — CETAKAN KE-2", peringatan: false));

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.Equal(1, HitungHalamanPdf(pdf));
        }

        [Fact]
        public void LabelKosong_TidakMenyebabkanKesalahanRender()
        {
            // Label kosong = surat biasa; render harus tetap jalan tanpa menambah
            // apa pun ke halaman.
            byte[] pdf = Render(kolom => SuratRenderer.LabelStatusSurat(kolom.Item(), "  ", peringatan: true));

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.Equal(1, HitungHalamanPdf(pdf));
        }

        [Fact]
        public void LabelDenganIsiSurat_MuatsatuHalaman()
        {
            byte[] pdf = Render(kolom =>
            {
                kolom.Item().Text("Judul surat contoh");
                SuratRenderer.LabelStatusSurat(kolom.Item(), "DRAF — MENUNGGU VERIFIKASI", peringatan: true);
                kolom.Item().Text("Badan surat contoh");
            });

            Assert.Equal(1, HitungHalamanPdf(pdf));
        }

        /// <summary>Render satu halaman A4 berisi potongan yang diberikan.</summary>
        private static byte[] Render(Action<ColumnDescriptor> isi)
        {
            // Referensi ke kelas ini menjalankan konstruksi statisnya (lisensi
            // QuestPDF); font didaftarkan dari folder font hasil salinan ke output.
            SuratGeneratorBase.DaftarkanFont();

            using var penampung = new MemoryStream();
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(new PageSize(595f, 842f, Unit.Point));
                    page.MarginTop(45);
                    page.MarginRight(45);
                    page.MarginBottom(45);
                    page.MarginLeft(45);

                    page.Content().Column(kolom => isi(kolom));
                });
            }).GeneratePdf(penampung);

            return penampung.ToArray();
        }

        /// <summary>
        /// Jumlah halaman PDF: QuestPDF menulis "/Type /Page" per halaman dan
        /// "/Type /Pages" untuk akarnya, jadi pola "/Page" diikuti karakter
        /// non-"s" hanya menghitung halaman.
        /// </summary>
        private static int HitungHalamanPdf(byte[] pdf)
        {
            string isi = Encoding.GetEncoding(28591).GetString(pdf);
            return Regex.Matches(isi, @"/Type\s*/Page[^s]").Count;
        }
    }
}
