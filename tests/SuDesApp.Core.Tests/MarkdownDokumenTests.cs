using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using SuDesApp.Utilities;

using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji pengurai Markdown halaman Dokumentasi. Halaman itu membaca dokumen
    /// <c>.md</c> di dalam aplikasi, jadi salah urai akan terlihat sebagai dokumen
    /// yang berantakan bagi pengguna desa — bukan sekadar tampilan yang kurang rapi.
    /// </summary>
    public class MarkdownDokumenTests
    {
        private const string ContohMarkdown =
            "# Judul Bab\n" +
            "\n" +
            "Paragraf pertama\n" +
            "bersambung di baris kedua.\n" +
            "\n" +
            "- butir satu\n" +
            "- butir dua\n" +
            "\n" +
            "1. langkah satu\n" +
            "2. langkah dua\n" +
            "\n" +
            "| Kolom A | Kolom B |\n" +
            "|---|---|\n" +
            "| isi 1 | isi 2 |\n" +
            "| isi 3 | isi 4 |\n" +
            "\n" +
            "> kutipan penting\n" +
            "\n" +
            "```json\n" +
            "{ \"a\": 1 }\n" +
            "```\n" +
            "\n" +
            "---\n";

        [Fact]
        public void Uraikan_MengenaliSeluruhBlokYangDipakaiDokumen()
        {
            var blok = MarkdownDokumen.Uraikan(ContohMarkdown);

            var jenis = blok.Select(b => b.Jenis).ToList();
            Assert.Equal(
                new[]
                {
                    JenisBlokMarkdown.Judul,
                    JenisBlokMarkdown.Paragraf,
                    JenisBlokMarkdown.Butir,
                    JenisBlokMarkdown.ButirBernomor,
                    JenisBlokMarkdown.Tabel,
                    JenisBlokMarkdown.Kutipan,
                    JenisBlokMarkdown.Kode,
                    JenisBlokMarkdown.Garis
                },
                jenis);
        }

        [Fact]
        public void Uraikan_MenggabungkanBarisParagrafDanMenjagaIsiBlokLain()
        {
            var blok = MarkdownDokumen.Uraikan(ContohMarkdown);

            Assert.Equal("Judul Bab", blok[0].Teks);
            Assert.Equal(1, blok[0].Tingkat);
            Assert.Equal("Paragraf pertama bersambung di baris kedua.", blok[1].Teks);

            Assert.Equal(new[] { "butir satu", "butir dua" }, blok[2].Baris);
            Assert.Equal(new[] { "langkah satu", "langkah dua" }, blok[3].Baris);

            var tabel = blok[4];
            Assert.Equal(3, tabel.IsiTabel.Count);
            Assert.Equal(new[] { "Kolom A", "Kolom B" }, tabel.IsiTabel[0]);
            Assert.Equal(new[] { "isi 3", "isi 4" }, tabel.IsiTabel[2]);

            Assert.Equal("kutipan penting", blok[5].Teks);

            Assert.Equal("json", blok[6].Bahasa);
            Assert.Contains("\"a\": 1", blok[6].Teks);
        }

        [Fact]
        public void Uraikan_TeksKosong_MenghasilkanDaftarKosong()
        {
            Assert.Empty(MarkdownDokumen.Uraikan(null));
            Assert.Empty(MarkdownDokumen.Uraikan("   \n\n  "));
        }

        /// <summary>
        /// Setiap dokumen yang ikut terpasang harus terurai: judulnya terbaca dan
        /// tabelnya benar-benar dikenali (bukan tercetak sebagai paragraf berpipa).
        /// Berkas baru yang ditambahkan ke folder docs ikut terperiksa otomatis.
        /// </summary>
        [Fact]
        public void Uraikan_DokumenBawaan_TeruraiDenganJudulDanTabel()
        {
            var folder = Path.Combine(CoreTestFixture.FindProjectRoot(), "docs");
            var berkas = Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(berkas);

            foreach (var jalur in berkas)
            {
                var isi = File.ReadAllText(jalur);
                var blok = MarkdownDokumen.Uraikan(isi);
                var nama = Path.GetFileName(jalur);

                Assert.True(blok.Count > 0, $"{nama}: tidak ada blok yang terbaca.");
                Assert.True(
                    blok.Any(b => b.Jenis == JenisBlokMarkdown.Judul && b.Teks.Length > 0),
                    $"{nama}: tidak ada judul yang terbaca.");

                // Tabel ditandai baris pemisah "|---|"; bila ada di berkas, pengurai
                // wajib mengenalinya sebagai tabel.
                if (isi.Contains("|---", StringComparison.Ordinal))
                {
                    Assert.True(
                        blok.Any(b => b.Jenis == JenisBlokMarkdown.Tabel && b.IsiTabel.Count >= 2),
                        $"{nama}: tabel tidak dikenali sebagai tabel.");
                }
            }
        }
    }
}
