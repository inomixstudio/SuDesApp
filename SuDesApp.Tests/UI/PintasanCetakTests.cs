using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.UI;

/// <summary>
/// Ctrl+P adalah pintasan cetak yang paling dicari operator, dan pintasan yang tidak
/// ada di dokumen maupun tooltip sama dengan tidak ada. Uji ini menolak halaman yang
/// punya tombol cetak tanpa pintasan — pola yang sama yang membuat Ctrl+N Template
/// Surat bisa berubah sendiri tanpa ada yang sadar.
/// </summary>
public sealed class PintasanCetakTests
{
    private static readonly string FolderView = Path.Combine(
        CariAkarRepo(), "SuDesApp.Wpf", "Views");

    /// <summary>Perintah yang menghasilkan PDF/cetakan, bukan ekspor berkas data.</summary>
    private static readonly Regex PerintahCetak =
        new(@"Command=""\{Binding (?<kunci>(?:Cetak|Print)[A-Za-z]*Command)\}""", RegexOptions.Compiled);

    private static readonly Regex PintasanCtrlP =
        new(@"<KeyBinding Gesture=""Ctrl\+P"" Command=""\{Binding (?<kunci>[A-Za-z]+)\}""/>", RegexOptions.Compiled);

    [Fact]
    public void SetiapHalamanBerpintasCetakMenunjukPerintahCetak()
    {
        foreach (string berkas in Directory.GetFiles(FolderView, "*.xaml").OrderBy(p => p))
        {
            string xaml = File.ReadAllText(berkas, System.Text.Encoding.UTF8);
            var pintasan = PintasanCtrlP.Matches(xaml).Select(m => m.Groups["kunci"].Value).ToList();
            if (pintasan.Count == 0) continue;

            var cetak = PerintahCetak.Matches(xaml).Select(m => m.Groups["kunci"].Value).Distinct().ToList();
            foreach (string kunci in pintasan)
            {
                Assert.True(cetak.Contains(kunci, StringComparer.Ordinal),
                    $"{Path.GetFileName(berkas)}: Ctrl+P menunjuk {kunci} yang bukan perintah cetak " +
                    "atau tidak dipakai tombolnya");
            }
        }
    }

    /// <summary>
    /// Aturan inti: halaman yang bisa mencetak lewat tombolnya wajib punya Ctrl+P.
    /// Inilah yang bocor kalau seseorang menambah tombol "Cetak" baru dan lupa pintasan.
    /// </summary>
    [Fact]
    public void HalamanDenganTombolCetakPunyaPintasanCtrlP()
    {
        var tanpa = new List<string>();
        int berpasangan = 0;

        foreach (string berkas in Directory.GetFiles(FolderView, "*.xaml").OrderBy(p => p))
        {
            string xaml = File.ReadAllText(berkas, System.Text.Encoding.UTF8);
            var cetak = PerintahCetak.Matches(xaml)
                .Select(m => m.Groups["kunci"].Value)
                .Where(k => !k.Equals("PrintSuratCommand", StringComparison.Ordinal)) // per baris, lewat menu konteks
                .Distinct()
                .ToList();
            if (cetak.Count == 0) continue;

            var pintasan = PintasanCtrlP.Matches(xaml).Select(m => m.Groups["kunci"].Value).ToList();
            if (pintasan.Intersect(cetak, StringComparer.Ordinal).Any()) berpasangan++;
            else tanpa.Add(Path.GetFileName(berkas) + " [" + string.Join(", ", cetak) + "]");
        }

        Assert.True(tanpa.Count == 0,
            $"halaman dengan tombol cetak tanpa pintasan Ctrl+P: {string.Join("; ", tanpa)}");
        Assert.True(berpasangan >= 10,
            $"dipastikan minimal 10 halaman berpintasan, ketemu {berpasangan} — periksa regexnya");
    }

    /// <summary>
    /// Pratinjau PDF mencetak dari kode view (dokumennya hidup di sana), jadi
    /// pintasnya tidak lewat XAML dan harus diperiksa terpisah.
    /// </summary>
    [Fact]
    public void PratinjauPdfMendaftarkanCtrlP()
    {
        string kode = File.ReadAllText(Path.Combine(FolderView, "PdfPreviewView.xaml.cs"),
            System.Text.Encoding.UTF8);

        Assert.Contains("InputBindings.Add(new System.Windows.Input.KeyBinding(", kode, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.Key.P", kode, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.ModifierKeys.Control", kode, StringComparison.Ordinal);
        // Pintasan harus memanggil aksi yang sama dengan tombol, tidak boleh cabang baru.
        Assert.Contains("AsyncRelayCommand(PrintAsync)", kode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pintasan hanya berguna kalau pengguna tahu ia ada: tombol yang dibatasi
    /// pintasan ini menyebutnya di tooltip.
    /// </summary>
    [Fact]
    public void TombolCetakBerpintasanMenyebutCtrlPDiTooltip()
    {
        var hilang = new List<string>();

        foreach (string berkas in Directory.GetFiles(FolderView, "*.xaml").OrderBy(p => p))
        {
            string xaml = File.ReadAllText(berkas, System.Text.Encoding.UTF8);
            foreach (Match pintasan in PintasanCtrlP.Matches(xaml))
            {
                string kunci = pintasan.Groups["kunci"].Value;
                Match tombol = Regex.Match(
                    xaml,
                    @"<Button[^>]*?Command=""\{Binding " + Regex.Escape(kunci) + @"\}""[^>]*?>",
                    RegexOptions.Singleline);
                if (!tombol.Success) continue;   // tombolnya ada di tempat lain (mis. menu konteks)

                string atribut = tombol.Value;
                Match tip = Regex.Match(atribut, "ToolTip=\"(?<isi>[^\"]*)\"");
                if (!tip.Success || !tip.Groups["isi"].Value.Contains("Ctrl+P", StringComparison.Ordinal))
                {
                    hilang.Add(Path.GetFileName(berkas) + " → " + kunci);
                }
            }
        }

        Assert.True(hilang.Count == 0,
            "tooltip tombol cetak berpintasan tidak menyebut Ctrl+P: " + string.Join("; ", hilang));
    }

    /// <summary>
    /// Bukti runtime, bukan teks: KeyBinding Ctrl+P benar-benar terpasang di halaman
    /// pratinjau dan perintahnya bisa dijalankan. Tanpa dokumen, aksi cetak keluar
    /// lebih awal, jadi memanggilnya di sini aman dan tetap membuktikan ikatannya.
    /// </summary>
    [Fact]
    public void PratinjauPdf_mengikat_Ctrl_P_ke_perintah_cetak()
    {
        var utas = new System.Threading.Thread(() =>
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new SuDesApp.Wpf.App();
                app.InitializeComponent();
            }

            var halaman = new SuDesApp.Wpf.Views.PdfPreviewView();
            var jendela = new System.Windows.Window
            {
                Content = halaman,
                Width = 300,
                Height = 200,
                WindowStyle = System.Windows.WindowStyle.None,
                ShowInTaskbar = false,
                Visibility = System.Windows.Visibility.Hidden,
            };
            jendela.Content = halaman;
            jendela.UpdateLayout();

            System.Windows.Input.KeyBinding? ikatan = halaman.InputBindings
                .OfType<System.Windows.Input.KeyBinding>()
                .FirstOrDefault(k => k.Key == System.Windows.Input.Key.P
                                  && k.Modifiers == System.Windows.Input.ModifierKeys.Control);
            Assert.NotNull(ikatan);
            Assert.NotNull(ikatan!.Command);
            Assert.True(ikatan.Command!.CanExecute(null));
            ikatan.Command.Execute(null);   // tanpa dokumen: keluar awal, tidak mencetak
            jendela.Close();
        });
        utas.SetApartmentState(System.Threading.ApartmentState.STA);
        utas.Start();
        Assert.True(utas.Join(TimeSpan.FromSeconds(30)), "halaman pratinjau tidak selesai dibangun");
    }

    private static string CariAkarRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo tidak ditemukan.");
    }
}
