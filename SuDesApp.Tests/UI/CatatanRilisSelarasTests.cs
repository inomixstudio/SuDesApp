using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Penjaga keselarasan catatan rilis.
    ///
    /// <para>Catatan rilis hidup di dua tempat: kartu di aplikasi
    /// (<c>CatatanRilisViewModel.RiwayatVersi.cs</c>) dan dokumen rilis
    /// (<c>docs/catatan-rilis-X.md</c>) yang dibaca halaman rilis GitHub. Keduanya
    /// ditulis tangan oleh alur rilis, dan tidak ada gerbang yang membandingkannya
    /// — versi yang kartunya menyebut fitur A sementara dokumennya menyebut fitur B
    /// bisa lolos tanpa disadari.</para>
    ///
    /// <para>Yang dijaga di sini hal yang mengubah perilaku alur rilis, bukan
    /// keseluruhan teks: kartu teratas harus menyebut versi yang sama dengan
    /// <c>AssemblyVersion</c> di csproj (sebab <c>Tools/rilis.ps1</c> menolak rilis
    /// bila beda), dan setiap versi yang punya dokumen rilis harus punya kartu di
    /// aplikasi.</para>
    /// </summary>
    public sealed class CatatanRilisSelarasTests
    {
        private static readonly Regex JudulKartu = new(
            @"new CatatanRilisEntry\(\s*""Versi (?<versi>[\d.]+) \((?<tanggal>[^""]+)\)""",
            RegexOptions.Compiled);

        private static readonly Regex AssemblyVersion = new(
            @"<AssemblyVersion>(?<versi>[\d.]+)</AssemblyVersion>",
            RegexOptions.Compiled);

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

        private static string Baca(string jalurRelatif, string root)
            => File.ReadAllText(Path.Combine(root, jalurRelatif));

        /// <summary>
        /// Kartu teratas (yang tampil paling awal di halaman Catatan Rilis) harus
        /// menyebut versi yang sama dengan AssemblyVersion di csproj. Ketidakcocokan
        /// berarti halaman Catatan Rilis menampilkan versi yang sedang dikembangkan
        /// seolah sudah rilis, atau sebaliknya.
        /// </summary>
        [Fact]
        public void Kartu_teratas_menyebut_versi_yang_sama_dengan_csproj()
        {
            var root = CariRootRepo();
            Assert.NotNull(root);

            var vm = Baca(
                Path.Combine("SuDesApp.Wpf", "ViewModels", "CatatanRilisViewModel.RiwayatVersi.cs"), root!);
            var csproj = Baca(
                Path.Combine("SuDesApp.Wpf", "SuDesApp.Wpf.csproj"), root!);

            var kartu = JudulKartu.Match(vm);
            Assert.True(kartu.Success, "Kartu catatan rilis teratas tidak ditemukan di RiwayatVersi.cs.");

            var versiCsproj = AssemblyVersion.Match(csproj);
            Assert.True(versiCsproj.Success, "AssemblyVersion tidak ditemukan di SuDesApp.Wpf.csproj.");

            var kartuSingkat = Singkat(kartu.Groups["versi"].Value);
            var csprojSingkat = Singkat(versiCsproj.Groups["versi"].Value);

            Assert.Equal(csprojSingkat, kartuSingkat);
        }

        /// <summary>
        /// Setiap versi yang punya dokumen rilis di docs/ harus punya kartu di
        /// aplikasi. Dokumen tanpa kartu berarti pengguna aplikasi tidak pernah
        /// melihat catatan rilis versi itu lewat menu Catatan Rilis.
        /// </summary>
        [Fact]
        public void Setiap_dokumen_rilis_punya_kartu_di_aplikasi()
        {
            var root = CariRootRepo();
            Assert.NotNull(root);

            var vm = Baca(
                Path.Combine("SuDesApp.Wpf", "ViewModels", "CatatanRilisViewModel.RiwayatVersi.cs"), root!);
            var versiKartu = new HashSet<string>(
                JudulKartu.Matches(vm).Select(m => Singkat(m.Groups["versi"].Value)),
                StringComparer.OrdinalIgnoreCase);

            var folderDocs = Path.Combine(root!, "docs");
            Assert.True(Directory.Exists(folderDocs), "Folder docs/ tidak ditemukan.");

            var tanpaKartu = Directory
                .EnumerateFiles(folderDocs, "catatan-rilis-*.md")
                .Select(Path.GetFileName)
                .Select(n => n!.Replace("catatan-rilis-", "", StringComparison.Ordinal)
                              .Replace(".md", "", StringComparison.Ordinal))
                .Select(Singkat)
                .Where(v => !versiKartu.Contains(v))
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.True(tanpaKartu.Count == 0,
                "Dokumen rilis tanpa kartu di aplikasi: " + string.Join(", ", tanpaKartu));
        }

        /// <summary>
        /// Kartu di aplikasi tidak boleh menyebut tanggal kosong atau versi kosong —
        /// bentuk yang membuat kartu tak terbaca di halaman Catatan Rilis.
        /// </summary>
        [Fact]
        public void Semua_kartu_memakai_bentuk_yang_baku()
        {
            var root = CariRootRepo();
            Assert.NotNull(root);

            var vm = Baca(
                Path.Combine("SuDesApp.Wpf", "ViewModels", "CatatanRilisViewModel.RiwayatVersi.cs"), root!);
            var kartu = JudulKartu.Matches(vm);

            Assert.NotEmpty(kartu);
            foreach (Match m in kartu)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Groups["versi"].Value),
                    "Kartu tanpa versi.");
                Assert.False(string.IsNullOrWhiteSpace(m.Groups["tanggal"].Value),
                    $"Kartu versi {m.Groups["versi"].Value} tanpa tanggal.");
            }
        }

        /// <summary>Angka versi disingkat ke x.y.z supaya 2.15.0.0 == 2.15.0.</summary>
        private static string Singkat(string versi)
        {
            var bagian = versi.Split('.');
            return bagian.Length >= 3 ? string.Join(".", bagian.Take(3)) : versi;
        }
    }
}
