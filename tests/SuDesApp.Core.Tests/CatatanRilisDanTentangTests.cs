using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Penjaga isi halaman <b>Catatan Rilis</b> dan <b>Tentang Aplikasi</b> —
    /// dua halaman yang penggunanya baca untuk mengetahui versi dan kemampuan
    /// aplikasi. Pelajaran dari audit 30 September 2026: kartu versi terbaru
    /// sempat tertinggal di belakang nomor build (2.5.3 vs 2.5.4), daftar fitur
    /// ketinggalan enkripsi SQLCipher dan API Desa, serta bentuk versi berbeda
    /// antar halaman (v2.5.4 vs 2.5.4.0) — semuanya lolos karena tidak diuji.
    /// Uji di sini membaca berkas sumbernya langsung, jadi tidak perlu mereferensi
    /// proyek WPF dari proyek uji.
    /// </summary>
    public sealed class CatatanRilisDanTentangTests
    {
        private const string JalurCatatanRilis = "SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs";
        private const string JalurTentang = "SuDesApp.Wpf/ViewModels/AboutViewModel.cs";
        private const string JalurDetailTentang = "SuDesApp.Wpf/Views/About/BagianDetailAplikasiView.xaml";
        private const string JalurPembaruan = "SuDesApp.Wpf/ViewModels/PembaruanViewModel.cs";
        private const string JalurCsprojWpf = "SuDesApp.Wpf/SuDesApp.Wpf.csproj";

        private static string Sumber(string jalurRelatif) =>
            CoreTestFixture.ReadProjectFile(jalurRelatif);

        /// <summary>Versi singkat dari csproj WPF — 2.5.4.0 → "2.5.4" (sama dengan IdentitasAplikasi.VersiSingkat).</summary>
        private static string VersiSingkatDariCsproj()
        {
            var csproj = Sumber(JalurCsprojWpf);
            var m = Regex.Match(csproj, @"<AssemblyVersion>(?<v>[\d.]+)</AssemblyVersion>");
            Assert.True(m.Success, "SuDesApp.Wpf.csproj tidak memuat AssemblyVersion.");

            var bagian = m.Groups["v"].Value.Split('.');
            return bagian.Length >= 4 && bagian[3] != "0"
                ? string.Join(".", bagian)
                : string.Join(".", bagian, 0, 3);
        }

        /// <summary>Semua pasangan (versi, isTerbaru) pada urutan entri Catatan Rilis.</summary>
        private static List<(string Versi, bool Terbaru)> EntriCatatanRilis()
        {
            var sumber = Sumber(JalurCatatanRilis);
            return Regex.Matches(
                    sumber,
                    @"new CatatanRilisEntry\(\s*""Versi (?<v>[\d.]+) \((?<tanggal>[^""]+)\)"",\s*(?<flag>true|false),")
                .Select(m => (m.Groups["v"].Value, m.Groups["flag"].Value == "true"))
                .ToList();
        }

        // ---------- Catatan Rilis ----------

        [Fact]
        public void CatatanRilis_VersiTerbaru_CocokDenganVersiAplikasi()
        {
            // Kartu pertama adalah versi terbaru (IsLatest) — nomornya wajib sama
            // dengan AssemblyVersion di csproj, jika tidak pengguna melihat versi
            // aplikasi yang tidak pernah dirilis (atau rilis yang tak tercatat).
            var versi = VersiSingkatDariCsproj();
            var entri = EntriCatatanRilis();

            Assert.NotEmpty(entri);
            Assert.True(entri[0].Terbaru,
                "Entri pertama Catatan Rilis wajib IsLatest = true (kartu versi terbaru).");
            Assert.Equal(versi, entri[0].Versi);
        }

        [Fact]
        public void CatatanRilis_HanyaSatuKartuTerbaru_DanBeradaDiPalingAtas()
        {
            var entri = EntriCatatanRilis();

            Assert.NotEmpty(entri);
            var terbaru = entri.Where(e => e.Terbaru).ToList();
            Assert.Single(terbaru);
        }

        [Fact]
        public void CatatanRilis_TiapEntri_PunyaNomorVersiDanTanggalYangSah()
        {
            var sumber = Sumber(JalurCatatanRilis);

            var judul = Regex.Matches(sumber, @"""(?<judul>Versi [\d.]+ \(\d+ \w+ \d{4}\))""");
            Assert.NotEmpty(judul);

            // Entri terbaru mencantumkan tanggal hari jadinya, entri lama tanggal
            // rilisnya — semuanya wajib berbentuk "Versi x.y.z (d NamaBulan yyyy)".
            Assert.All(judul, m => Assert.Matches(@"^Versi [\d.]+ \(\d+ \w+ \d{4}\)$", m.Groups["judul"].Value));
        }

        [Fact]
        public void CatatanRilis_VersiTerbaru_MencatatFiturRilisTersebut()
        {
            // Fitur yang dirilis bersama entri terbaru wajib tercatat — pembaca
            // catatan rilis memakainya untuk tahu apa yang berubah.
            var sumber = Sumber(JalurCatatanRilis);

            foreach (var penanda in new[]
                     {
                         "API Desa",
                         "Lokasi Berkas Database Dapat Dipilih",
                         "Database Desa"
                     })
            {
                Assert.Contains(penanda, sumber);
            }
        }

        // ---------- Tentang Aplikasi ----------

        [Fact]
        public void Tentang_DaftarFiturMenyebutEnkripsiDatabaseDanApiDesa()
        {
            var sumber = Sumber(JalurTentang);

            foreach (var penanda in new[]
                     {
                         "SQLCipher",
                         "DPAPI",
                         "API Desa",
                         "Database Desa"
                     })
            {
                Assert.Contains(penanda, sumber);
            }
        }

        [Fact]
        public void Tentang_TeknologiMenyebutSqlcipherPdfiumViewerDanGoogleForms()
        {
            var sumber = Sumber(JalurTentang);
            var awal = sumber.IndexOf("Teknologi = new List<TeknologiItem>", StringComparison.Ordinal);
            Assert.True(awal >= 0, "Blok daftar Teknologi tidak ditemukan di AboutViewModel.");

            var blok = sumber[awal..];
            var akhir = blok.IndexOf("};", StringComparison.Ordinal);
            Assert.True(akhir > 0, "Blok daftar Teknologi tidak tertutup rapi.");
            blok = blok[..akhir];

            foreach (var nama in new[] { "SQLCipher", "PdfiumViewer", "Google Forms API" })
            {
                Assert.Contains(nama, blok);
            }

            // Nama lama yang tidak persis paketnya tidak boleh hidup lagi.
            Assert.DoesNotContain("\"Pdfium\"", blok);
            Assert.DoesNotContain("\"SQLite\"", blok);
        }

        [Fact]
        public void Tentang_DetailAplikasi_MembedakanFolderAplikasiDanPreferensiPengguna()
        {
            // C7: preferensi, kunci database, token Google, dan riwayat pembaruan
            // berada di %LOCALAPPDATA%\SuDesApp — BUKAN folder aplikasi. Menampilkannya
            // sebagai satu "lokasi data" membuat pengguna salah mencadangkan data.
            var detail = Sumber(JalurDetailTentang);
            var vm = Sumber(JalurTentang);

            Assert.Contains("Folder aplikasi", detail);
            Assert.Contains("Preferensi &amp; kunci", detail);
            Assert.Contains("{Binding LokasiPreferensi}", detail);

            Assert.Contains("LokasiPreferensi", vm);
            Assert.Contains("LocalApplicationData", vm);
            Assert.Contains("\"SuDesApp\"", vm);
        }

        // ---------- Halaman Pembaruan (bentuk versi) ----------

        [Fact]
        public void Pembaruan_VersiSaatIni_MemakaiBentukVersiStatusBar()
        {
            // C4: "Versi Saat Ini: 2.5.4.0" sementara status bar/footer "v2.5.4".
            // Tampilan wajib satu bentuk dengan IdentitasAplikasi; perbandingan
            // versi (empat komponen) boleh tetap memakai GetCurrentVersion().
            var sumber = Sumber(JalurPembaruan);

            Assert.Contains("Versi Saat Ini: {IdentitasAplikasi.VersiDenganPrefiks}", sumber);
            Assert.DoesNotContain("Versi Saat Ini: {GetCurrentVersion()}", sumber);
        }
    }
}
