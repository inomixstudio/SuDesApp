using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Penjaga isi halaman "Tentang Aplikasi". Isi halaman ini adalah identitas
    /// aplikasi yang dilihat pengguna (dan disebut saat melapor masalah), jadi
    /// kebenarannya diuji dari berkas sumbernya:
    ///
    /// 1. identitas hanya dibaca dari <c>IdentitasAplikasi</c> — bukan atribut
    ///    assembly sendiri, supaya versi di halaman ini tidak pernah berbeda dari
    ///    status bar (dulu "v2.5.4.0" di sini sementara status bar "v2.5.4");
    /// 2. alamat tautan (repo, website, donasi, surel) tidak ditulis di XAML —
    ///    repositori ikut <c>AppConfig.githubRepo</c> yang juga dipakai pemeriksa
    ///    pembaruan, jadi tautan dan sumber unduhan tidak bisa menunjuk repo berbeda;
    /// 3. daftar fitur/teknologi menyebut kemampuan yang benar-benar ada dan
    ///    tautan teknologinya sah.
    /// </summary>
    public sealed class TentangAplikasiTests
    {
        // Pemakaian garis miring tetap: jalur relatif yang sama berlaku di Windows
        // maupun di runner Linux, jadi uji ini tidak bergantung sistem operasi.
        private const string JalurViewModel = "SuDesApp.Wpf/ViewModels/AboutViewModel.cs";
        private const string JalurTampilanUtama = "SuDesApp.Wpf/Views/AboutView.xaml";
        private const string JalurBagianDetail = "SuDesApp.Wpf/Views/About/BagianDetailAplikasiView.xaml";
        private const string JalurBagianDukungan = "SuDesApp.Wpf/Views/About/BagianDukunganView.xaml";

        private static string Sumber(string jalurRelatif) =>
            CoreTestFixture.ReadProjectFile(jalurRelatif);

        // ---------- identitas tunggal ----------

        [Fact]
        public void AboutViewModel_MemakaiIdentitasAplikasi_UntukSeluruhIdentitas()
        {
            var sumber = Sumber(JalurViewModel);

            // Versi & identitas dipinjam dari IdentitasAplikasi — sumber kebenaran
            // yang sama dengan status bar, footer Catatan Rilis, dan jendela login.
            foreach (var acuan in new[]
                     {
                         "IdentitasAplikasi.Nama",
                         "IdentitasAplikasi.VersiDenganPrefiks",
                         "IdentitasAplikasi.VersiLengkap",
                         "IdentitasAplikasi.Pengembang",
                         "IdentitasAplikasi.HakCipta"
                     })
            {
                Assert.Contains(acuan, sumber);
            }

            // Membaca atribut assembly langsung di sini akan menghidupkan lagi
            // duplikasi identitas (dan perbedaan bentuk versi) yang sudah dibuang.
            foreach (var terlarang in new[]
                     {
                         "AssemblyProductAttribute",
                         "AssemblyCompanyAttribute",
                         "AssemblyCopyrightAttribute",
                         "AssemblyTitleAttribute"
                     })
            {
                Assert.DoesNotContain(terlarang, sumber);
            }
        }

        [Fact]
        public void IdentitasAplikasi_SatuSatunyaPembacaAtributAssembly()
        {
            var akar = Path.Combine(CoreTestFixture.FindProjectRoot(), "SuDesApp.Wpf");

            var pembaca = Directory
                .EnumerateFiles(akar, "*.cs", SearchOption.AllDirectories)
                .Where(BukanHasilBuild)
                .Where(berkas => File.ReadAllText(berkas).Contains("AssemblyProductAttribute"))
                .Select(Path.GetFileName)
                .OrderBy(nama => nama)
                .ToList();

            Assert.Equal(new[] { "IdentitasAplikasi.cs" }, pembaca);
        }

        private static bool BukanHasilBuild(string berkas)
        {
            var bagian = berkas.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !bagian.Contains("obj") && !bagian.Contains("bin");
        }

        // ---------- bentuk versi ----------

        [Fact]
        public void HalamanTentang_MenampilkanVersiSingkat_DanNomorBuildTerpisah()
        {
            var hero = Sumber(JalurTampilanUtama);
            var detail = Sumber(JalurBagianDetail);

            // Hero & baris "Versi" memakai bentuk pendek ("v2.5.4") seperti status bar.
            Assert.Contains("VersiTampil", hero);
            Assert.Contains("VersiTampil", detail);
            Assert.Contains("VersiBuild", detail);

            // Versi empat komponen (2.5.4.0) tidak lagi dipakai sebagai teks versi utama.
            Assert.DoesNotContain("AssemblyVersion", hero);
            Assert.DoesNotContain("AssemblyVersion", detail);
        }

        [Fact]
        public void BagianTentang_SeluruhBindingAdaDiViewModel()
        {
            // Penggantian nama properti di view model TIDAK menghasilkan galat build pada
            // WPF: binding yang salah hanya membuat bagian itu tampil kosong. Uji ini
            // membandingkan setiap {Binding ...} di tampilan Tentang dengan anggota publik
            // berkas view model-nya (AboutViewModel.cs memuat AboutViewModel,
            // AboutBagianViewModel, dan TeknologiItem).
            var akar = Path.Combine(CoreTestFixture.FindProjectRoot(), "SuDesApp.Wpf");
            var viewModel = Sumber(JalurViewModel);

            var anggota = Regex.Matches(
                    viewModel,
                    @"public\s+[\w<>?\.,\[\]]+\s+(?<nama>\w+)\s*[\{=]")
                .Select(m => m.Groups["nama"].Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("DeskripsiLengkap", anggota);

            var halaman = Directory
                .EnumerateFiles(Path.Combine(akar, "Views"), "About*.xaml")
                .Concat(Directory.EnumerateFiles(Path.Combine(akar, "Views", "About"), "*.xaml"))
                .ToList();

            Assert.NotEmpty(halaman);

            var menggantung = new List<string>();

            foreach (var berkas in halaman)
            {
                foreach (Match m in Regex.Matches(
                             File.ReadAllText(berkas), @"\{Binding\s+(?:Path=)?(?<jalur>[\w\.]+)"))
                {
                    var akarJalur = m.Groups["jalur"].Value.Split('.')[0];

                    // DataContext bukan anggota view model — dipakai binding relatif
                    // ke induk visual (lihat Bagian*View.xaml).
                    if (akarJalur == "DataContext" || anggota.Contains(akarJalur))
                    {
                        continue;
                    }

                    menggantung.Add($"{Path.GetFileName(berkas)}: {m.Groups["jalur"].Value}");
                }
            }

            Assert.True(menggantung.Count == 0,
                "Binding tanpa properti di AboutViewModel.cs: " + string.Join("; ", menggantung));
        }

        // ---------- tautan tidak dikunci di XAML ----------

        [Fact]
        public void HalamanTentang_TidakMengunciAlamatTautanDiXaml()
        {
            foreach (var jalur in new[] { JalurTampilanUtama, JalurBagianDetail, JalurBagianDukungan })
            {
                var xaml = Sumber(jalur);

                foreach (var alamat in new[] { "github.com", "saweria.co", "mailto:", "desa-sumberjaya.com" })
                {
                    Assert.False(xaml.Contains(alamat, StringComparison.OrdinalIgnoreCase),
                        $"{jalur} masih menulis alamat \"{alamat}\" langsung; tautan seharusnya dari view model.");
                }
            }

            // Repositori dibangun dari AppConfig — konfigurasi yang sama dengan pemeriksa pembaruan.
            var sumber = Sumber(JalurViewModel);
            Assert.Contains("appConfig?.GithubRepo", sumber);
            Assert.Contains("\"https://github.com/\" + repo", sumber);
        }

        [Fact]
        public void Appsettings_GithubRepo_BerbentukOwnerDanRepo()
        {
            using var dokumen = JsonDocument.Parse(Sumber("appsettings.json"));

            var repo = dokumen.RootElement
                .GetProperty("AppConfig")
                .GetProperty("githubRepo")
                .GetString();

            Assert.False(string.IsNullOrWhiteSpace(repo),
                "AppConfig.githubRepo wajib diisi — tanpa itu pemeriksaan pembaruan dan tautan repo mati.");
            Assert.Matches(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", repo);
        }

        [Fact]
        public void GithubRepo_SelarasDenganRemoteGitRepositori()
        {
            var berkasConfig = Path.Combine(CoreTestFixture.FindProjectRoot(), ".git", "config");
            if (!File.Exists(berkasConfig))
            {
                // Salinan tanpa riwayat git (mis. ekspor arsip) — tidak ada yang bisa dibandingkan.
                return;
            }

            var slug = Regex.Matches(File.ReadAllText(berkasConfig), @"github\.com[:/](?<slug>[^/\s""]+/[^/\s""]+)")
                .Select(m => m.Groups["slug"].Value.TrimEnd('.', '/'))
                .Select(s => s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s)
                .ToList();

            if (slug.Count == 0)
            {
                // Remote bukan GitHub (mis. folder lokal). Tidak ada rujukan untuk dibandingkan.
                return;
            }

            using var dokumen = JsonDocument.Parse(Sumber("appsettings.json"));
            var repo = dokumen.RootElement.GetProperty("AppConfig").GetProperty("githubRepo").GetString()!;

            Assert.Contains(repo, slug, StringComparer.OrdinalIgnoreCase);
        }

        // ---------- kelengkapan katalog ----------

        [Fact]
        public void DaftarFitur_MenyebutKemampuanBaruYangAdaDiAplikasi()
        {
            var sumber = Sumber(JalurViewModel);

            // Fitur yang sudah rilis tapi sempat tidak tercantum di halaman Tentang:
            // pembaca daftar fitur memakai ini untuk tahu apa yang bisa dilakukan aplikasi.
            foreach (var penanda in new[]
                     {
                         "Verifikasi surat",
                         "Lampiran tabel daftar nama pada SK perangkat desa",
                         "unit (mis. Posyandu)",
                         "Dokumentasi bawaan",
                         "Data perangkat desa per kelompok jabatan"
                     })
            {
                Assert.Contains(penanda, sumber);
            }
        }

        [Fact]
        public void DaftarTeknologi_SeluruhTautannyaHttpsSah()
        {
            // Hanya blok Teknologi yang diperiksa — daftar Bagian memakai bentuk
            // new(...) yang sama, tapi argumen keempatnya keterangan, bukan tautan.
            var sumber = Sumber(JalurViewModel);
            var awal = sumber.IndexOf("Teknologi = new List<TeknologiItem>", StringComparison.Ordinal);
            Assert.True(awal >= 0, "Blok daftar Teknologi tidak ditemukan di AboutViewModel.");

            var blok = sumber[awal..];
            var akhir = blok.IndexOf("};", StringComparison.Ordinal);
            Assert.True(akhir > 0, "Blok daftar Teknologi tidak tertutup rapi.");
            blok = blok[..akhir];

            // Baris TeknologiItem: new("ikon", "Nama", "Keterangan", "url")
            var baris = Regex.Matches(
                blok,
                @"new\(""[^""]*"", ""[^""]*"", ""[^""]*"", ""(?<url>[^""]+)""\)");

            Assert.NotEmpty(baris);

            foreach (Match satu in baris)
            {
                var url = satu.Groups["url"].Value;
                Assert.True(
                    Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                    $"Tautan teknologi \"{url}\" bukan alamat https yang sah.");
            }
        }
    }
}
