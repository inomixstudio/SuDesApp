using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Menjaga kepala halaman tetap satu rupa di seluruh aplikasi.
    ///
    /// <para>
    /// <b>Judul 20.</b> Kepala halaman adalah satu-satunya teks yang menandai
    /// "Anda sedang di halaman apa". Selama ukurannya 15 — hanya satu langkah di
    /// atas <c>SectionTitle</c> 13 Bold — hierarki tipografi halaman tidak terbaca
    /// sama sekali. 20 adalah anak tangga "judul" pada skala baku yang dijaga
    /// <see cref="UkuranHurufStandarTests"/>, jadi angka ini tidak boleh diturunkan
    /// diam-diam oleh standardiser berikutnya.
    /// </para>
    ///
    /// <para>
    /// <b>Subtitle ber-MaxWidth harus rata kiri.</b> TextBlock yang merentang
    /// dengan <c>MaxWidth</c> tidak dipotong di kiri: WPF membagi sisa ruang ke
    /// kiri DAN kanan, jadi kalimat penjelasan mengambang di tengah header sementara
    /// judulnya di kiri — gejala yang sempat terlihat di halaman Dokumentasi,
    /// API Desa, Kelola Pengguna, Sinkron, dan Verifikasi Surat.
    /// </para>
    /// </summary>
    public sealed class HeaderHalamanTests
    {
        [Fact]
        public void HeaderTitle_memakai_ukuran_judul_baku_20()
        {
            var xaml = File.ReadAllText(BerkasTema("ThemeStyles.xaml"));
            var gaya = Regex.Match(xaml, @"<Style x:Key=""HeaderTitle""[\s\S]*?</Style>");
            Assert.True(gaya.Success, "Style HeaderTitle tidak ditemukan di ThemeStyles.xaml.");

            var ukuran = Regex.Match(gaya.Value, @"FontSize""\s+Value=""([\d.]+)""");
            Assert.True(ukuran.Success, "HeaderTitle tidak menetapkan FontSize.");
            Assert.Equal(20d, double.Parse(ukuran.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Subtitle_header_dengan_MaxWidth_tidak_mengambang_di_tengah()
        {
            var pelanggar = new List<string>();

            foreach (var berkas in DiraiXaml())
            {
                var teks = File.ReadAllText(berkas);
                foreach (Match cocok in Regex.Matches(teks, @"<TextBlock[^>]*?>", RegexOptions.Singleline))
                {
                    var tag = cocok.Value;
                    // "HeaderSubtitleBrush" (warna) bukan gaya subtitle — kunci pada
                    // persis pemakaian gayanya supaya penjaga tidak memungut teks lain.
                    if (!tag.Contains("StaticResource HeaderSubtitle}", StringComparison.Ordinal)) continue;
                    if (!tag.Contains("MaxWidth=", StringComparison.Ordinal)) continue;
                    if (tag.Contains("HorizontalAlignment=\"Left\"", StringComparison.Ordinal)) continue;

                    pelanggar.Add($"{Path.GetFileName(berkas)}: {Regex.Replace(tag, @"\s+", " ")}");
                }
            }

            Assert.True(
                pelanggar.Count == 0,
                "MaxWidth pada TextBlock yang merentang membuat WPF mengiset teks ke tengah, "
                + "sementara judul header tetap di kiri. Tambahkan HorizontalAlignment=\"Left\":\n"
                + string.Join("\n", pelanggar));
        }

        private static string BerkasTema(string nama)
        {
            var akar = AkarRepo();
            var jalur = Path.Combine(akar, "SuDesApp.Wpf", "Themes", nama);
            Assert.True(File.Exists(jalur), "Berkas tidak ditemukan: " + jalur);
            return jalur;
        }

        private static IEnumerable<string> DiraiXaml()
            => Directory.EnumerateFiles(Path.Combine(AkarRepo(), "SuDesApp.Wpf"), "*.xaml", SearchOption.AllDirectories);

        private static string AkarRepo()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
        }
    }
}
