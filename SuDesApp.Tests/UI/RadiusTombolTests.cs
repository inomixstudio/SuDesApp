using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI;

/// <summary>
/// Sudut tombol diatur dari satu sumber (<c>RadiusTombol</c> / <c>RadiusTombolIkon</c>).
/// Dijaga lewat teks XAML karena ada tiga Border bertumpuk di template tombol —
/// lapisan sorot dan lapisan tekan harus memakai radius yang sama dengan border
/// luar, kalau tidak sudutnya "keluar" saat hover dan hanya kelihatan di mata,
/// bukan di galat.
/// </summary>
public sealed class RadiusTombolTests
{
    private static readonly string TemaStyles = Path.Combine(
        CariAkarRepo(), "SuDesApp.Wpf", "Themes", "ThemeStyles.xaml");

    [Fact]
    public void SumberRadiusTombolAdaSatuTempat()
    {
        string xaml = File.ReadAllText(TemaStyles, System.Text.Encoding.UTF8);

        Assert.Matches(@"<CornerRadius x:Key=""RadiusTombol"">\d+</CornerRadius>", xaml);
        Assert.Matches(@"<CornerRadius x:Key=""RadiusTombolIkon"">\d+</CornerRadius>", xaml);
    }

    [Fact]
    public void TemplateTombolDasarPakaiRadiusDariSumberYangSama()
    {
        string xaml = File.ReadAllText(TemaStyles, System.Text.Encoding.UTF8);
        int mulai = xaml.IndexOf("<ControlTemplate TargetType=\"Button\">", StringComparison.Ordinal);
        Assert.True(mulai >= 0, "template Button dasar tidak ditemukan");
        string template = xaml.Substring(mulai, xaml.IndexOf("</ControlTemplate>", mulai, StringComparison.Ordinal) - mulai);

        MatchCollection radius = Regex.Matches(template, @"CornerRadius=""([^""]+)""");
        Assert.True(radius.Count >= 3, $"bd + SorotLapis + TekanLapis harus ikut radius, ketemu {radius.Count}");
        foreach (Match r in radius)
        {
            Assert.Equal("{StaticResource RadiusTombol}", r.Groups[1].Value);
        }
    }

    /// <summary>
    /// Tombol ikon (tutup, navigasi kalender, aksi di toolbar) ikut sumber radius
    /// ikon. Bar penanda dan baris navigasi sengaja tidak: bentuknya bukan tombol.
    /// </summary>
    [Theory]
    [InlineData("IconButton")]
    [InlineData("CalendarNavButton")]
    public void TombolIkonMemakaiRadiusTombolIkon(string gaya)
    {
        string xaml = File.ReadAllText(TemaStyles, System.Text.Encoding.UTF8);
        int mulai = xaml.IndexOf($"<Style x:Key=\"{gaya}\" TargetType=\"Button\"", StringComparison.Ordinal);
        Assert.True(mulai >= 0, $"gaya {gaya} tidak ditemukan");
        int akhir = xaml.IndexOf("</Style>", mulai, StringComparison.Ordinal);
        string isi = xaml.Substring(mulai, akhir - mulai);

        Assert.Contains("CornerRadius=\"{StaticResource RadiusTombolIkon}\"", isi, StringComparison.Ordinal);
    }

    private static string CariAkarRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo tidak ditemukan.");
    }
}
