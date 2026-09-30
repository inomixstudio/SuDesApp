using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;

using QuestPDF.Infrastructure;

using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;

using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji katalog dan judul SK perangkat.
    ///
    /// Dua kesalahan pernah lolos tanpa terdeteksi karena tidak ada uji di sini:
    /// judul keputusan tiap kelompok tidak pernah tercetak (properti tersimpan tetapi
    /// tidak dipakai generator, sehingga blok "TENTANG" hilang dari dokumen) dan
    /// kelompok Linmas memakai kata "ANGOTA". Uji-uji berikut menahan keduanya,
    /// sekaligus memastikan tidak ada placeholder yang tersisa tercetak di dokumen.
    /// </summary>
    [Collection("API")]
    public class SkPerangkatKatalogTests
    {
        /// <summary>Placeholder yang dikenali generator (lihat IsiPlaceholder).</summary>
        private static readonly string[] PlaceholderDikenal =
        {
            "{JABATAN}", "{NAMA}", "{NIK}", "{DESA}", "{WILAYAH}", "{NOMOR}",
            "{MULAI}", "{SELESAI}", "{TANGGAL}", "{ALASAN}"
        };

        [Fact]
        public void Katalog_SetiapKelompokPunyaTemplateKecualiLainnya()
        {
            foreach (var kelompok in JabatanPerangkat.UrutanKelompok)
            {
                if (kelompok == "LAINNYA")
                {
                    // Jabatan khusus desa tidak punya dasar hukum baku: sengaja ditolak
                    // (halaman Perangkat Desa menampilkan penjelasannya).
                    Assert.False(SkPerangkatKatalog.Ada(kelompok));
                    continue;
                }

                Assert.True(SkPerangkatKatalog.Ada(kelompok), $"Kelompok {kelompok} belum punya template SK.");
            }

            Assert.Null(SkPerangkatKatalog.Cari("LAINNYA"));
            Assert.Null(SkPerangkatKatalog.Cari("JABATAN KHUSUS DESA"));
            Assert.Null(SkPerangkatKatalog.Cari(null));
            Assert.Equal(9, SkPerangkatKatalog.Semua.Count);
        }

        [Fact]
        public void Katalog_JudulSetiapKelompokTerisiDanLinmasMemakaiKataAnggota()
        {
            foreach (var template in SkPerangkatKatalog.Semua)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(template.JudulPengangkatan),
                    $"{template.Kelompok}: judul pengangkatan kosong.");
                Assert.False(
                    string.IsNullOrWhiteSpace(template.JudulPemberhentian),
                    $"{template.Kelompok}: judul pemberhentian kosong.");
            }

            var linmas = SkPerangkatKatalog.Cari("LINMAS");
            Assert.NotNull(linmas);
            Assert.Contains("ANGGOTA", linmas!.JudulPengangkatan);
            Assert.Contains("ANGGOTA", linmas.JudulPemberhentian);

            // "ANGOTA" tidak boleh kembali ke teks mana pun.
            foreach (var template in SkPerangkatKatalog.Semua)
            {
                foreach (var teks in SemuaTeks(template))
                {
                    Assert.DoesNotContain("ANGOTA", teks);
                }
            }
        }

        [Fact]
        public void Katalog_HanyaMemakaiPlaceholderYangDikenal()
        {
            var dipakai = SkPerangkatKatalog.Semua
                .SelectMany(SemuaTeks)
                .SelectMany(teks => Regex.Matches(teks, "\\{[A-Z]+\\}").Select(m => m.Value))
                .Distinct()
                .Where(placeholder => !PlaceholderDikenal.Contains(placeholder))
                .ToList();

            Assert.Empty(dipakai);
        }

        [Fact]
        public void Generator_JudulTentangTerisiTanpaSisaPlaceholder()
        {
            var desa = DesaUji();

            foreach (var template in SkPerangkatKatalog.Semua)
            {
                foreach (bool pengangkatan in new[] { true, false })
                {
                    var judul = SkPerangkatGenerator.JudulTampil(
                        template, pengangkatan, IsiContoh(template, pengangkatan), desa);

                    Assert.False(string.IsNullOrWhiteSpace(judul));
                    Assert.DoesNotContain("{", judul);
                    Assert.DoesNotContain("}", judul);
                }
            }

            // Judul Linmas: kata "ANGGOTA" + jabatan & nama desa dalam huruf kapital.
            var templateLinmas = SkPerangkatKatalog.Cari("LINMAS")!;
            var judulLinmas = SkPerangkatGenerator.JudulTampil(
                templateLinmas, pengangkatan: true, IsiContoh(templateLinmas, pengangkatan: true), desa);

            Assert.Equal("PENGANGKATAN ANGGOTA LINMAS DESA SUMBERJAYA", judulLinmas);
        }

        [Fact]
        public void Generator_ButirDanKetentuanTanpaSisaPlaceholder_DenganDanTanpaIsian()
        {
            var desa = DesaUji();

            foreach (var template in SkPerangkatKatalog.Semua)
            {
                foreach (bool pengangkatan in new[] { true, false })
                {
                    // Isian lengkap (SK untuk orang tertentu) dan isian kosong
                    // (contoh SK yang dicetak bergaris titik-titik).
                    foreach (var isi in new[]
                             {
                                 IsiContoh(template, pengangkatan),
                                 IsiKosong(template, pengangkatan)
                             })
                    {
                        var teks = template.Menimbang
                            .Concat(template.Mengingat)
                            .Append(Ketentuan(template, isi))
                            .Select(satu => SkPerangkatGenerator.IsiPlaceholder(satu, isi, desa))
                            .ToList();

                        Assert.All(teks, satu =>
                        {
                            Assert.DoesNotContain("{", satu);
                            Assert.DoesNotContain("}", satu);
                            Assert.False(string.IsNullOrWhiteSpace(satu));
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Membuat PDF contoh SK (tanpa orang tertentu). Blok TENTANG yang baru
        /// menambah baris di atas Menimbang, jadi render-nya ikut diperiksa.
        /// </summary>
        [Fact]
        public void Generator_ContohSk_TetapMencetakSatuPdf()
        {
            var config = new AppConfig(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppConfig:databaseConnectionString"] = "Data Source=:memory:"
                })
                .Build());

            using var aliran = new MemoryStream();
            new SkPerangkatGenerator(config).GenerateSkPdf(aliran, DesaUji(), new SkPerangkatIsi
            {
                Jenis = SkJenisPerangkat.Pengangkatan,
                Kelompok = "BPD",
                Contoh = true
            });

            var pdf = aliran.ToArray();
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
        }

        // ── Pembantu ────────────────────────────────────────────────────────

        private static DesaData DesaUji() => new()
        {
            NamaDesa = "Sumberjaya",
            Kecamatan = "Tempuran",
            Kabupaten = "Karawang",
            Alamat = "Jl. Belendung 02 RT 008 RW 003",
            KepalaDesa = "A. Sopandi"
        };

        private static SkPerangkatIsi IsiContoh(SkKelompokTemplate template, bool pengangkatan) => new()
        {
            Jenis = pengangkatan ? SkJenisPerangkat.Pengangkatan : SkJenisPerangkat.Pemberhentian,
            Kelompok = template.Kelompok,
            Nomor = "340/01-Kep/Ds/2026",
            Tanggal = new DateTime(2026, 9, 29),
            Nama = "Uji Coba",
            NIK = "3204010101800001",
            Jabatan = JabatanPerangkat.Linmas,
            Wilayah = "Dusun Uji, RT 001/RW 001",
            Mulai = new DateTime(2026, 9, 29),
            Selesai = new DateTime(2029, 9, 28),
            Alasan = "karena masa jabatan telah berakhir"
        };

        /// <summary>Kalimat keputusan yang dipakai generator untuk isian ini.</summary>
        private static string Ketentuan(SkKelompokTemplate template, SkPerangkatIsi isi) =>
            isi.Jenis == SkJenisPerangkat.Pengangkatan
                ? (template.KetentuanPengangkatan ?? SkPerangkatKatalog.KetentuanPengangkatanUmum)
                : (template.KetentuanPemberhentian ?? SkPerangkatKatalog.KetentuanPemberhentianUmum);

        private static SkPerangkatIsi IsiKosong(SkKelompokTemplate template, bool pengangkatan) => new()
        {
            Jenis = pengangkatan ? SkJenisPerangkat.Pengangkatan : SkJenisPerangkat.Pemberhentian,
            Kelompok = template.Kelompok,
            Contoh = true
        };

        private static IEnumerable<string> SemuaTeks(SkKelompokTemplate template)
        {
            yield return template.JudulPengangkatan;
            yield return template.JudulPemberhentian;

            foreach (var butir in template.Menimbang)
            {
                yield return butir;
            }

            foreach (var dasar in template.Mengingat)
            {
                yield return dasar;
            }

            if (template.KetentuanPengangkatan is not null)
            {
                yield return template.KetentuanPengangkatan;
            }

            if (template.KetentuanPemberhentian is not null)
            {
                yield return template.KetentuanPemberhentian;
            }
        }
    }
}
