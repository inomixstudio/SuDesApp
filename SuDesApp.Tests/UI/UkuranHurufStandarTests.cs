using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Menjaga skala ukuran huruf baku (10/11/13/14/15/20/24/28) seperti yang
    /// didokumentasikan di docs/tema-dan-warna.md. Literal di luar skala membuat
    /// hierarki teks antar halaman menyimpang tanpa build gagal.
    ///
    /// Tiga bentuk penulisan diperiksa karena semuanya mengatur FontSize:
    /// atribut XAML, &lt;Setter&gt; di dalam Style (urutan atribut boleh terbalik),
    /// dan literal C#.
    ///
    /// Pengecualian yang disengaja: nilai di atas 28 (emoji/piagam) dan nilai di
    /// bawah 5 (pemisah hairline) — sama seperti Tools/standarkan-ukuran-huruf.py.
    /// </summary>
    public sealed class UkuranHurufStandarTests
    {
        private static readonly double[] Skala = { 10, 11, 13, 14, 15, 20, 24, 28 };

        private static readonly Regex AtributXaml = new(@"FontSize=""(\d+(?:\.\d+)?)""", RegexOptions.Compiled);
        private static readonly Regex Setter = new(@"Property=""FontSize""\s+Value=""(\d+(?:\.\d+)?)""", RegexOptions.Compiled);
        private static readonly Regex SetterTerbalik = new(@"Value=""(\d+(?:\.\d+)?)""\s+Property=""FontSize""", RegexOptions.Compiled);
        private static readonly Regex LiteralCs = new(@"FontSize\s*=\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        [Fact]
        public void Literal_FontSize_seluruhnya_di_skala_baku()
        {
            string? akar = CariRootRepo();
            Assert.True(akar is not null, "Akar repo (berisi SuDesApp.slnx) tidak ditemukan.");

            var wpf = Path.Combine(akar, "SuDesApp.Wpf");
            var pelanggaran = new List<string>();

            foreach (string berkas in Directory.EnumerateFiles(wpf, "*.*", SearchOption.AllDirectories))
            {
                if (Terlewat(berkas)) continue;

                var pola = Path.GetExtension(berkas) == ".xaml"
                    ? new[] { AtributXaml, Setter, SetterTerbalik }
                    : new[] { LiteralCs };

                int nomor = 0;
                foreach (string baris in File.ReadLines(berkas))
                {
                    nomor++;
                    foreach (Regex p in pola)
                    {
                        foreach (Match m in p.Matches(baris))
                        {
                            double nilai = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

                            // Emoji/piagam (di atas 28) dan pemisah hairline (di bawah 5)
                            // sengaja di luar skala.
                            if (nilai > 28 || nilai < 5) continue;
                            if (Skala.Contains(nilai)) continue;

                            pelanggaran.Add(
                                $"{Path.GetRelativePath(akar, berkas)}:{nomor} FontSize={m.Groups[1].Value}");
                        }
                    }
                }
            }

            Assert.True(pelanggaran.Count == 0,
                "Literal FontSize di luar skala baku (10/11/13/14/15/20/24/28) ditemukan. "
                + "Jalankan: py Tools/standarkan-ukuran-huruf.py\n  "
                + string.Join("\n  ", pelanggaran.Take(40)));
        }

        private static bool Terlewat(string berkas)
        {
            string pemisah = Path.DirectorySeparatorChar.ToString();
            return Path.GetExtension(berkas) is not (".xaml" or ".cs")
                || berkas.Contains(pemisah + "obj" + pemisah)
                || berkas.Contains(pemisah + "bin" + pemisah);
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
