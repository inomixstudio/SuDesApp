using System;
using System.Collections.Generic;
using System.IO;
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
    /// Penjaga lisensi QuestPDF dan jalur cetak SK perangkat.
    ///
    /// QuestPDF menolak menggambar dokumen apa pun bila
    /// <c>QuestPDF.Settings.License</c> belum diisi: "Please configure the QuestPDF
    /// license by setting 'QuestPDF.Settings.License' at application startup".
    /// Sebelum diperbaiki, generator SK perangkat tidak mewarisi
    /// <see cref="SuratGeneratorBase"/> dan tidak pernah memasang lisensi maupun
    /// mendaftarkan font, sehingga <b>SK pertama</b> setelah aplikasi dibuka gagal
    /// sedangkan SK berikutnya berhasil (generator surat lain sudah memasangnya).
    /// Uji terakhir di kelas ini menahan agar keadaan itu tidak kembali.
    ///
    /// Kelas ini juga memuat penjaga struktur: satu-satunya berkas yang boleh menulis
    /// <c>QuestPDF.Settings.License</c> adalah <see cref="QuestPdfLisensi"/>, supaya
    /// konsolidasi itu tidak luruh lagi saat generator baru ditambahkan.
    /// </summary>
    [Collection("API")]
    public class LisensiQuestPdfTests
    {
        /// <summary>
        /// Berkas penetapan lisensi — satu-satunya yang boleh menulis
        /// <c>QuestPDF.Settings.License</c>. Jalur relatif terhadap akar repo, memakai
        /// garis miring tetap supaya sama di Windows maupun Linux.
        /// </summary>
        private const string BerkasPenetapanLisensi = "SuDesApp.Core/GeneratorPdf/QuestPdfLisensi.cs";

        /// <summary>
        /// Pola penulisan properti: <c>QuestPDF.Settings.License = …</c>, dengan atau tanpa
        /// awalan <c>QuestPDF.</c> (untuk pemakaian <c>using static QuestPDF;</c>), termasuk
        /// bentuk majemuk (<c>+=</c>, <c>??=</c>). Pembacaan sengaja tidak cocok: lookbehind
        /// dan lookahead menolak <c>==</c>, <c>!=</c>, <c>&lt;=</c>, dan <c>&gt;=</c>.
        /// </summary>
        private static readonly Regex PolaPenulisanLisensi = new(
            @"(?:QuestPDF\s*\.\s*)?Settings\s*\.\s*License\s*(?:[+\-*/%&|^]|\?\?)?=(?!=)",
            RegexOptions.Compiled);

        /// <summary>Folder yang isinya hasil build/alat, bukan kode sumber.</summary>
        private static readonly HashSet<string> FolderDiabaikan =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", ".git", ".vs", "artifacts", "node_modules"
            };
        [Fact]
        public void Pastikan_MemasangTingkatCommunityDanIdempoten()
        {
            QuestPdfLisensi.Pastikan();
            var sesudahPanggilanPertama = QuestPDF.Settings.License;

            QuestPdfLisensi.Pastikan();

            Assert.Equal(LicenseType.Community, sesudahPanggilanPertama);
            Assert.Equal(sesudahPanggilanPertama, QuestPDF.Settings.License);
        }

        /// <summary>
        /// Menolak penulisan <c>QuestPDF.Settings.License</c> di luar berkas penetapan.
        ///
        /// Riwayatnya: baris lisensi dulu ditulis ulang di setiap generator, lalu satu
        /// generator baru (SK perangkat) lupa menyalinnya sehingga SK pertama setelah
        /// aplikasi dibuka gagal digambar sedangkan SK berikutnya berhasil. Konsolidasi itu
        /// hanya bertahan bila penambahan generator berikutnya tidak bisa mengembalikan
        /// barisnya — uji inilah yang menahan.
        ///
        /// Bila uji ini gagal, jangan menambah berkas penulis kedua: panggil
        /// <see cref="QuestPdfLisensi.Pastikan"/> dari generator baru itu. Pengecualian yang
        /// benar-benar perlu (mis. uji yang harus mengosongkan lisensi) ditambahkan sadar
        /// lewat daftar pengecualian, bukan dengan melebarkan polanya.
        /// </summary>
        [Fact]
        public void PenulisanLisensiQuestPdf_HanyaDiBerkasPenetapan()
        {
            var akar = CoreTestFixture.FindProjectRoot();
            var pelanggar = new List<string>();
            var penetapanMenulis = false;

            foreach (var berkas in BerkasSumberCSharp(akar))
            {
                var relatif = Path.GetRelativePath(akar, berkas).Replace('\\', '/');
                var baris = File.ReadAllLines(berkas);

                for (var i = 0; i < baris.Length; i++)
                {
                    if (KomentarUtuh(baris[i]) || !PolaPenulisanLisensi.IsMatch(baris[i]))
                    {
                        continue;
                    }

                    if (relatif == BerkasPenetapanLisensi)
                    {
                        penetapanMenulis = true;
                    }
                    else
                    {
                        pelanggar.Add($"{relatif}:{i + 1} -> {baris[i].Trim()}");
                    }
                }
            }

            // Penetapannya harus tetap ada; tanpa pemeriksaan ini, menghapus penetapannya
            // justru membuat uji "tidak ada penulis lain" lolos.
            Assert.True(penetapanMenulis,
                $"{BerkasPenetapanLisensi} tidak lagi menulis QuestPDF.Settings.License. " +
                "Penetapan lisensi harus tetap ada di satu tempat ini (lihat QuestPdfLisensi.Pastikan).");

            Assert.True(pelanggar.Count == 0,
                $"QuestPDF.Settings.License ditulis di luar berkas penetapan {BerkasPenetapanLisensi}. " +
                "Pindahkan penetapannya ke QuestPdfLisensi.Pastikan() lalu panggil itu dari generator barunya:\n  " +
                string.Join("\n  ", pelanggar));
        }

        /// <summary>
        /// Baris komentar tidak diperiksa: penjelasan berbentuk contoh kode di dalam
        /// dokumentasi tidak boleh ikut dianggap sebagai penulisan sungguhan.
        /// </summary>
        private static bool KomentarUtuh(string baris)
        {
            var rapi = baris.TrimStart();

            return rapi.StartsWith("//", StringComparison.Ordinal)
                || rapi.StartsWith("/*", StringComparison.Ordinal)
                || rapi.StartsWith("*", StringComparison.Ordinal);
        }

        /// <summary>
        /// Semua berkas .cs di bawah akar repo, tanpa menyentuh folder hasil build —
        /// penelusuran dilakukan sendiri supaya isi bin/obj tidak perlu dibaca.
        /// </summary>
        private static IEnumerable<string> BerkasSumberCSharp(string akar)
        {
            var antrean = new Stack<string>();
            antrean.Push(akar);

            while (antrean.Count > 0)
            {
                var folder = antrean.Pop();

                foreach (var anak in Directory.EnumerateDirectories(folder))
                {
                    if (!FolderDiabaikan.Contains(Path.GetFileName(anak)))
                    {
                        antrean.Push(anak);
                    }
                }

                foreach (var berkas in Directory.EnumerateFiles(folder, "*.cs"))
                {
                    yield return berkas;
                }
            }
        }

        /// <summary>
        /// Membuat PDF SK tanpa menyiapkan apa pun lebih dulu — persis seperti operator
        /// yang baru membuka aplikasi lalu langsung menekan tombol SK di halaman Data
        /// Perangkat Desa. Generator ini sendirilah yang harus memasang lisensi dan
        /// mendaftarkan font, tanpa bergantung pada generator lain.
        /// </summary>
        [Fact]
        public void SkPerangkat_TanpaPenyiapanSebelumnya_TetapTercetak()
        {
            var config = new AppConfig(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppConfig:databaseConnectionString"] = "Data Source=:memory:"
                })
                .Build());

            var desa = new DesaData
            {
                NamaDesa = "Desa Uji",
                Kecamatan = "Kec. Uji",
                Kabupaten = "Kab. Uji",
                KepalaDesa = "Kades Uji"
            };

            var isi = new SkPerangkatIsi
            {
                Jenis = SkJenisPerangkat.Pengangkatan,
                Kelompok = "LINMAS",
                Nomor = "340/01-Kep/Ds/2026",
                Nama = "Uji Coba",
                NIK = "3204010101800001",
                Jabatan = JabatanPerangkat.Linmas,
                Wilayah = "Dusun Uji, RT 001/RW 001",
                Tanggal = new DateTime(2026, 9, 29),
                Mulai = new DateTime(2026, 9, 29),
                Selesai = new DateTime(2029, 9, 28)
            };

            byte[] pdf;
            using (var aliran = new MemoryStream())
            {
                new SkPerangkatGenerator(config).GenerateSkPdf(aliran, desa, isi);
                pdf = aliran.ToArray();
            }

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
        }
    }
}
