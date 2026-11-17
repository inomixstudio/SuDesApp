using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI;

/// <summary>
/// Template <c>CheckBox</c> tema tidak boleh mengukur labelnya tanpa batas lebar.
///
/// <para>Gejala aslinya terlihat pada dialog <c>PersetujuanDokumenWindow</c>
/// (Kebijakan Privasi &amp; Syarat dan Ketentuan) yang muncul saat aplikasi baru
/// dibuka: kalimat "Saya telah membaca dan menyetujui …" berhenti di tepi kanan
/// jendela tanpa pindah baris, dan karena tinggi jendela dihitung untuk SATU
/// baris, baris kedua (beserta tombol) tidak pernah tampil. Operator
/// melaporkannya sebagai "tulisan terpotong, checkbox tidak kelihatan".</para>
///
/// <para>Penyebabnya murni template: akar <c>ControlTemplate</c> berupa
/// <c>StackPanel Orientation="Horizontal"</c>. StackPanel mengukur anaknya dengan
/// lebar tak hingga, sehingga <c>TextWrapping="Wrap"</c> di dalam konten tidak
/// pernah aktif. <c>BulletDecorator</c> — yang sudah dipakai template RadioButton
/// di berkas yang sama — memberi sisa lebar ke kontennya sehingga teks membungkus
/// dan tinggi kontrol ikut bertambah.</para>
/// </summary>
public sealed class KotakCentangMembungkusTests
{
    private static readonly string XamlTema = Path.Combine(
        CariAkarRepo(), "SuDesApp.Wpf", "Themes", "ThemeStyles.xaml");

    private static readonly string XamlPersetujuan = Path.Combine(
        CariAkarRepo(), "SuDesApp.Wpf", "Views", "PersetujuanDokumenWindow.xaml");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    /// <summary>Badan ControlTemplate untuk satu tipe kontrol.</summary>
    private static string BadanTemplate(string tipe)
    {
        var teks = File.ReadAllText(XamlTema);
        var mulai = Regex.Match(teks, @"<ControlTemplate\s+TargetType=""" + tipe + @"""");
        Assert.True(mulai.Success, $"ControlTemplate untuk {tipe} tidak ditemukan di ThemeStyles.xaml.");
        var sisa = teks.Substring(mulai.Index);
        var akhir = Regex.Match(sisa, "</ControlTemplate>");
        return sisa.Substring(0, akhir.Index + akhir.Value.Length);
    }

    [Fact]
    public void TemplateCheckBox_tidakMengukurLabelDenganLebarTakHingga()
    {
        var badan = BadanTemplate("CheckBox");

        Assert.DoesNotContain(
            "<StackPanel Orientation=\"Horizontal\"", badan, StringComparison.Ordinal);
        Assert.Contains("BulletDecorator", badan, StringComparison.Ordinal);

        // Tanpa ini, label yang membungkus beberapa baris tetap diukur melebar.
        var akar = Regex.Match(badan, @"<BulletDecorator\b");
        Assert.True(akar.Success,
            "Akar template CheckBox harus BulletDecorator supaya kontennya menerima sisa lebar.");
    }

    [Fact]
    public void TemplateCheckBox_danRadioButtonSejalanSoalPenjajaranVertikal()
    {
        // Dialog persetujuan memakai VerticalContentAlignment="Top" agar kotak
        // sejajar baris pertama label yang panjang. Template harus menghormatinya,
        // bukan memaksa Center seperti sebelumnya.
        var checkbox = BadanTemplate("CheckBox");
        Assert.Matches(
            @"VerticalAlignment=""\{TemplateBinding VerticalContentAlignment\}""", checkbox);

        var radio = BadanTemplate("RadioButton");
        Assert.Contains("BulletDecorator", radio, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogPersetujuan_membiarkanKalimatPersetujuanMembungkus()
    {
        var xaml = File.ReadAllText(XamlPersetujuan);

        // Konten CheckBox harus Grid (bukan StackPanel): StackPanel meloloskan
        // lebar tak hingga ke TextBlock sehingga pembungkusan mati lagi.
        var kotak = Regex.Match(xaml, @"<CheckBox\b[\s\S]*?</CheckBox>");
        Assert.True(kotak.Success, "CheckBox persetujuan tidak ditemukan di PersetujuanDokumenWindow.xaml.");
        Assert.DoesNotContain("<StackPanel", kotak.Value, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", kotak.Value, StringComparison.Ordinal);

        // Dialog tidak boleh memasang tinggi tetap: tingginya harus mengikuti
        // isi, justru supaya baris kedua kalimat dan tombol tidak terpotong.
        Assert.DoesNotMatch(@"<Window\b[^>]*\bHeight=""\d+""", xaml);
        Assert.Contains(@"SizeToContent=""Height""", xaml, StringComparison.Ordinal);
    }
}
