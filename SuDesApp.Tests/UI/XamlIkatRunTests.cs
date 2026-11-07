using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// <c>Run.TextProperty</c> berstatus <c>BindsTwoWayByDefault</c>, jadi
    /// <c>&lt;Run Text="{Binding X}"&gt;</c> tanpa mode tersirat ialah ikatan
    /// dua arah. Bila X bersifat read-only (umum di ViewModel — setter-nya
    /// private), WPF melempar
    /// <c>InvalidOperationException: A TwoWay or OneWayToSource binding cannot
    /// work on the read-only property</c> saat tata letak berjalan, dan itu
    /// muncul sebagai UNHANDLED di error.log tanpa jejak halaman mana.
    ///
    /// <para>
    /// Seluruh pemakaian <c>Run</c> di aplikasi memakai <c>Mode=OneWay</c>;
    /// uji ini menjaga konsistensi itu supaya penambahan teks ber-ikatan baru
    /// tidak mengulang galat yang sama.
    /// </para>
    /// </summary>
    public sealed class XamlIkatRunTests
    {
        [Fact]
        public void Run_Berbentuk_Teks_Harus_OneWay()
        {
            var pelanggar = new List<string>();

            foreach (var berkas in DiraiXaml())
            {
                var teks = File.ReadAllText(berkas);
                foreach (Match cocok in Regex.Matches(teks, @"<Run\s+Text=""\{Binding\s+([^}]+)\}"""))
                {
                    var ikatan = cocok.Groups[1].Value;
                    if (!ikatan.Contains("Mode=", StringComparison.Ordinal))
                    {
                        pelanggar.Add($"{Path.GetFileName(berkas)}: {cocok.Value}");
                    }
                }
            }

            Assert.True(
                pelanggar.Count == 0,
                "Run ber-ikatan dua arah ke properti read-only melempar InvalidOperationException "
                + "saat tata letak. Tambahkan Mode=OneWay:\n" + string.Join("\n", pelanggar));
        }

        private static IEnumerable<string> DiraiXaml()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
                {
                    var akar = Path.Combine(folder.FullName, "SuDesApp.Wpf");
                    return Directory.Exists(akar)
                        ? Directory.EnumerateFiles(akar, "*.xaml", SearchOption.AllDirectories)
                        : Enumerable.Empty<string>();
                }

                folder = folder.Parent;
            }

            throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
        }
    }
}
