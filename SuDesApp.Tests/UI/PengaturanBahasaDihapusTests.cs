using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Pengaturan bahasa tampilan pernah ada tetapi tidak terpakai: satu-satunya teks
    /// yang benar-benar berpindah bahasa adalah baris pemilihnya sendiri, ditambah label
    /// versi di footer pengaturan dan di splash. Sisanya teks Indonesia yang ditulis
    /// langsung di ratusan berkas XAML/ViewModel, jadi memilih "English" hanya membuat
    /// aplikasi tampak setengah Inggris. Fitur itu sudah dibuang, dan uji ini menjaga
    /// supaya kerangka lokalisasi tidak tumbuh lagi diam-diam lewat pengeditan berikutnya.
    /// </summary>
    public sealed class PengaturanBahasaDihapusTests
    {
        private static readonly string AkarRepo = CariAkarRepo();

        private static readonly string[] BerkasYangHarusHilang =
        {
            "SuDesApp.Wpf/Services/Lokalisasi.cs",
            "SuDesApp.Wpf/ViewModels/SettingSelectionRowVM.cs",
            "SuDesApp.Wpf/ViewModels/PengaturanAplikasiViewModel.Bahasa.cs",
            "SuDesApp.Tests/UI/BahasaPengaturanTests.cs",
        };

        /// <summary>Nama simbol yang menjadi tanda kerangka bahasa masih dipakai.</summary>
        private static readonly string[] SimbolTerlarang =
        {
            "Lokalisasi",
            "SelectedBahasa",
            "RowsPilihan",
            "SettingSelectionRowVM",
            "SiapkanBahasa",
            "GetBahasa",
            "SetBahasa",
            "KeyBahasa",
        };

        [Fact]
        public void KerangkaFiturBahasaSudahDihapusDariRepo()
        {
            foreach (var relatif in BerkasYangHarusHilang)
            {
                var path = Path.Combine(AkarRepo, relatif);
                Assert.False(File.Exists(path),
                    $"Berkas fitur bahasa masih ada: {relatif}");
            }
        }

        [Fact]
        public void TidakAdaRujukanLokalisasiDiSeluruhSumber()
        {
            var sendiri = Path.GetFullPath(
                Path.Combine(AkarRepo, "SuDesApp.Tests", "UI", "PengaturanBahasaDihapusTests.cs"));
            var temuan = new List<string>();

            foreach (var proyek in new[] { "SuDesApp.Wpf", "SuDesApp.Core", "SuDesApp.Tests" })
            {
                var akar = Path.Combine(AkarRepo, proyek);
                if (!Directory.Exists(akar)) continue;

                var berkas = Directory.EnumerateFiles(akar, "*.cs", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(akar, "*.xaml", SearchOption.AllDirectories))
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                             && Path.GetFullPath(f) != sendiri);

                foreach (var file in berkas)
                {
                    var teks = File.ReadAllText(file);
                    foreach (var simbol in SimbolTerlarang)
                    {
                        if (teks.Contains(simbol))
                            temuan.Add($"{Path.GetRelativePath(AkarRepo, file)} -> {simbol}");
                    }
                }
            }

            Assert.True(temuan.Count == 0,
                "Rujukan fitur bahasa masih tersisa:\n" + string.Join("\n", temuan));
        }

        [Fact]
        public void HalamanPengaturanAplikasiTidakMenawarkanPilihanBahasa()
        {
            var xaml = File.ReadAllText(Path.Combine(
                AkarRepo, "SuDesApp.Wpf", "Views", "PengaturanAplikasiView.xaml"));

            Assert.DoesNotContain("RowsPilihan", xaml);
            Assert.DoesNotContain("SelectedBahasa", xaml);
            // Baris bahasa adalah satu-satunya ComboBox di halaman pengaturan yang
            // isinya datang dari daftar pilihan berkode; kalau pola itu muncul lagi,
            // berarti pemilih bahasa diam-diam dibangun ulang.
            Assert.DoesNotContain("SelectedValuePath=\"Kode\"", xaml);
        }

        [Fact]
        public void LabelVersiMasihDitampilkanSetelahLokalisasiDibuang()
        {
            // Dua tempat ini tadinya mengambil teks dari Lokalisasi. Kalau salah satu
            // ikut terhapus, halaman kehilangan baris versinya tanpa ada yang gagal build.
            var vm = File.ReadAllText(Path.Combine(
                AkarRepo, "SuDesApp.Wpf", "ViewModels", "PengaturanAplikasiViewModel.cs"));
            Assert.Contains("public string VersiAplikasi", vm);
            Assert.Contains("Versi Aplikasi: {IdentitasAplikasi.VersiSingkat}", vm);

            var xaml = File.ReadAllText(Path.Combine(
                AkarRepo, "SuDesApp.Wpf", "Views", "PengaturanAplikasiView.xaml"));
            Assert.Contains("Text=\"{Binding VersiAplikasi}\"", xaml);

            var splash = File.ReadAllText(Path.Combine(
                AkarRepo, "SuDesApp.Wpf", "Views", "SplashStartupWindow.cs"));
            Assert.Contains("Versi Aplikasi {IdentitasAplikasi.VersiSingkat}", splash);
        }

        private static string CariAkarRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Root repo (SuDesApp.slnx) tidak ditemukan.");
        }
    }
}
