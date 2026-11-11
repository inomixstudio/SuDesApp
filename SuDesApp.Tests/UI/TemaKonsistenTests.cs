using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SuDesApp.Wpf.Services;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Menjaga disiplin tema seperti yang didokumentasikan di docs/tema-dan-warna.md:
    /// halaman tidak boleh memakai warna literal (yang membeku dan tidak ikut berganti
    /// saat tema diganti), kunci kuas/warna tema tidak boleh dirujuk dengan StaticResource
    /// (yang membeku pada tema saat dimuat), dan sebelas tema harus memakai kunci yang
    /// sama supaya tidak ada bagian yang jatuh ke warna bawaan WPF.
    ///
    /// Warna <c>Transparent</c> dikecualikan: ia bukan warna tema, melainkan niat
    /// "biarkan latar di belakangnya tembus".
    /// </summary>
    public sealed class TemaKonsistenTests
    {
        private static readonly Regex Kunci = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex AtributWarna = new(
            @"(Foreground|Background|BorderBrush|Fill|Stroke|Color|OpacityMask)=""([^""]+)""",
            RegexOptions.Compiled);
        private static readonly Regex StaticResource = new(@"StaticResource\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);

        private static readonly HashSet<string> Netral = new(StringComparer.Ordinal) { "Transparent" };

        /// <summary>
        /// Nama warna yang sah di XAML/WPF (<c>Colors</c>). Hanya yang berawalan huruf
        /// besar dan ada di daftar ini yang dianggap warna — supaya <c>Fill="True"</c>
        /// pada <c>Shape</c> tidak terdeteksi sebagai warna.
        /// </summary>
        private static readonly HashSet<string> NamaWarna = new(StringComparer.Ordinal)
        {
            "White", "Black", "Red", "Green", "Blue", "Gray", "Grey", "Orange", "Yellow",
            "Purple", "Pink", "Brown", "Cyan", "Magenta", "Silver", "LightGray", "DarkGray",
            "LightSlateGray", "DarkSlateGray", "Ivory", "Beige", "Navy", "Teal", "Maroon",
            "Olive", "Lime", "Aqua", "Fuchsia", "Indigo", "Violet", "Gold", "Khaki",
            "Salmon", "Tan", "Turquoise", "Tomato", "SteelBlue", "MidnightBlue", "Snow",
            "Linen", "Lavender", "AliceBlue",
        };

        [Fact]
        public void SemuaTema_MendefinisikanKunciYangSama()
        {
            string? akar = CariRootRepo();
            Assert.True(akar is not null, "Akar repo (berisi SuDesApp.slnx) tidak ditemukan.");

            string[] tema = Directory.EnumerateFiles(Path.Combine(akar, "SuDesApp.Wpf", "Themes"), "*Theme.xaml")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            Assert.True(tema.Length >= 2, "Berkas tema (*Theme.xaml) tidak ditemukan.");

            var patokan = KunciTema(tema[0]);
            Assert.True(patokan.Count > 0, $"Tema {Path.GetFileName(tema[0])} tidak mempunyai kunci.");

            foreach (string berkas in tema.Skip(1))
            {
                var kunci = KunciTema(berkas);
                string[] kurang = patokan.Except(kunci).ToArray();
                string[] lebih = kunci.Except(patokan).ToArray();

                Assert.True(kurang.Length == 0 && lebih.Length == 0,
                    $"Tema {Path.GetFileName(berkas)} memakai kunci yang berbeda. "
                    + $"Kurang: [{string.Join(", ", kurang)}]. "
                    + $"Lebih: [{string.Join(", ", lebih)}].");
            }
        }

        [Fact]
        public void SetiapTemaDiLayanan_PunyaBerkasDanSebaliknya()
        {
            // Katalog layanan (ThemeCatalog) dan berkas tema harus sepakat:
            // tema tanpa berkas membuat pergantian tema jatuh ke tema bawaan,
            // berkas tanpa katalog menjadi tema yatim yang tak bisa dipilih.
            string? akar = CariRootRepo();
            Assert.True(akar is not null, "Akar repo (berisi SuDesApp.slnx) tidak ditemukan.");

            var diLayanan = ThemeCatalog.GetAvailableThemes()
                .Where(nama => !string.Equals(nama, "Auto", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            var diBerkas = Directory
                .EnumerateFiles(Path.Combine(akar, "SuDesApp.Wpf", "Themes"), "*Theme.xaml")
                .Select(Path.GetFileNameWithoutExtension)
                .Select(nama => nama!.EndsWith("Theme", StringComparison.Ordinal)
                    ? nama[..^"Theme".Length]
                    : nama)
                .ToHashSet(StringComparer.Ordinal);

            string[] tanpaBerkas = diLayanan.Except(diBerkas).ToArray();
            string[] tanpaKatalog = diBerkas.Except(diLayanan).ToArray();

            Assert.True(tanpaBerkas.Length == 0 && tanpaKatalog.Length == 0,
                "Daftar tema ThemeCatalog dan berkas *Theme.xaml tidak sama. "
                + $"Tanpa berkas: [{string.Join(", ", tanpaBerkas)}]. "
                + $"Tanpa katalog: [{string.Join(", ", tanpaKatalog)}].");
        }

        [Fact]
        public void Tampilan_TidakMemakaiWarnaLiteral()
        {
            string? akar = CariRootRepo();
            Assert.True(akar is not null, "Akar repo (berisi SuDesApp.slnx) tidak ditemukan.");

            var pelanggaran = new List<string>();

            foreach (string berkas in BerkasTampilan(akar))
            {
                int nomor = 0;
                foreach (string baris in File.ReadLines(berkas))
                {
                    nomor++;
                    foreach (Match m in AtributWarna.Matches(baris))
                    {
                        string nilai = m.Groups[2].Value;
                        if (Netral.Contains(nilai)) continue;

                        bool hex = nilai.StartsWith("#", StringComparison.Ordinal);
                        if (!hex && !NamaWarna.Contains(nilai)) continue;

                        pelanggaran.Add(
                            $"{Path.GetRelativePath(akar, berkas)}:{nomor} {m.Groups[1].Value}={nilai}");
                    }
                }
            }

            Assert.True(pelanggaran.Count == 0,
                "Warna literal di XAML tampilan membeku saat tema diganti; "
                + "pakai kunci kuas tema (mis. {DynamicResource TextBrush}).\n  "
                + string.Join("\n  ", pelanggaran.Take(40)));
        }

        [Fact]
        public void KunciMilikTema_TidakDirujukDenganStaticResource()
        {
            string? akar = CariRootRepo();
            Assert.True(akar is not null, "Akar repo (berisi SuDesApp.slnx) tidak ditemukan.");

            var kunciTema = KunciTemaKeys(akar);
            Assert.True(kunciTema.Count > 0, "Kunci kuas tema tidak ditemukan.");

            var pelanggaran = new List<string>();

            foreach (string berkas in BerkasTampilan(akar))
            {
                int nomor = 0;
                foreach (string baris in File.ReadLines(berkas))
                {
                    nomor++;
                    foreach (Match m in StaticResource.Matches(baris))
                    {
                        if (kunciTema.Contains(m.Groups[1].Value))
                        {
                            pelanggaran.Add(
                                $"{Path.GetRelativePath(akar, berkas)}:{nomor} StaticResource {m.Groups[1].Value}");
                        }
                    }
                }
            }

            Assert.True(pelanggaran.Count == 0,
                "Kunci kuas/warna tema dirujuk dengan StaticResource sehingga membeku "
                + "pada tema saat dimuat; ganti dengan DynamicResource.\n  "
                + string.Join("\n  ", pelanggaran.Take(40)));
        }

        private static HashSet<string> KunciTema(string berkas) =>
            new(Kunci.Matches(File.ReadAllText(berkas)).Select(m => m.Groups[1].Value), StringComparer.Ordinal);

        private static HashSet<string> KunciTemaKeys(string akar)
        {
            var gabungan = new HashSet<string>(StringComparer.Ordinal);
            foreach (string berkas in Directory.EnumerateFiles(
                         Path.Combine(akar, "SuDesApp.Wpf", "Themes"), "*Theme.xaml"))
            {
                gabungan.UnionWith(KunciTema(berkas));
            }
            return gabungan;
        }

        private static string[] BerkasTampilan(string akar)
        {
            string pemisah = Path.DirectorySeparatorChar.ToString();
            return Directory.EnumerateFiles(Path.Combine(akar, "SuDesApp.Wpf"), "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains(pemisah + "obj" + pemisah)
                         && !p.Contains(pemisah + "bin" + pemisah)
                         && !p.Contains(pemisah + "Themes" + pemisah))
                .ToArray();
        }

        private static string? CariRootRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
