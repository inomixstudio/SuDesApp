using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using SudesPdf.Core.Layout;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Aliran isi lintas halaman pada mesin tata letak SudesPdf. Isi yang tidak muat
/// pada satu lembar harus lanjut ke halaman berikutnya: tidak ada baris yang
/// hilang atau tercetak ganda, tidak ada yang jatuh di luar area cetak, dan
/// alirannya berhenti pada jumlah halaman yang benar.
///
/// Tata letak bekerja pada titik PDF (y nol di tepi bawah kertas). Kesalahan arah
/// y pernah membuat isi surat menumpuk di atas tepi halaman — karena itu uji ini
/// juga memeriksa di mana baris pertama dan kaki halaman benar-benar tercetak.
/// </summary>
public sealed class TataLetakHalamanTests
{
    private const int JumlahBaris = 120;

    [Fact]
    public async Task IsiPanjangMengalirKeHalamanBerikutnyaTanpaBarisHilang()
    {
        byte[] pdf = BuatDokumen();

        int halaman = HitungHalaman(pdf);
        Assert.True(halaman > 1,
            $"{JumlahBaris} baris seharusnya tidak muat dalam satu halaman, tetapi berkas berisi {halaman} halaman.");

        await using var dokumen = await SudesPdf.Document.LoadAsync(pdf);
        Assert.Equal(halaman, dokumen.PageCount);

        var seluruh = new StringBuilder();
        for (int nomor = 0; nomor < dokumen.PageCount; nomor++)
        {
            string teksHalaman = await dokumen.GetTextAsync(nomor);
            Assert.Contains("KAKI", teksHalaman, StringComparison.Ordinal);
            seluruh.AppendLine(teksHalaman);
        }

        // Tiap baris tercetak tepat sekali dan berurutan.
        string teks = seluruh.ToString();
        int posisiSebelumnya = -1;
        for (int i = 1; i <= JumlahBaris; i++)
        {
            string label = Label(i);
            int posisi = teks.IndexOf(label, StringComparison.Ordinal);
            Assert.True(posisi >= 0, $"Baris \"{label}\" tidak tercetak sama sekali.");
            Assert.Equal(posisi, teks.LastIndexOf(label, StringComparison.Ordinal));
            Assert.True(posisi > posisiSebelumnya, $"Baris \"{label}\" tercetak sebelum baris sebelumnya.");
            posisiSebelumnya = posisi;
        }

        // Halaman pertama: banyak baris tinta di dalam kertas — bukan satu baris
        // yang menggantung di tepi atas, dan kaki halaman tetap di bagian bawah.
        double[] pita = await Pita(dokumen, 0);
        Assert.True(pita.Length >= 20, $"Halaman pertama hanya memuat {pita.Length} baris tinta.");
        Assert.True(pita[0] >= 30, $"Baris pertama tercetak di y={pita[0]:F1} — di luar area cetak.");

        double tinggi = dokumen.PageSizes[0].Height;
        Assert.True(pita[^1] >= tinggi - 80,
            $"Kaki halaman tercetak di y={pita[^1]:F1} dari {tinggi:F1} — bukan di bagian bawah kertas.");
    }

    private static byte[] BuatDokumen()
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(PageSizes.A4);
                halaman.Margin(36);
                halaman.Content().Column(kolom =>
                {
                    for (int i = 1; i <= JumlahBaris; i++) kolom.Item().Text(Label(i));
                });
                halaman.Footer().AlignCenter().Text("KAKI");
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);
        return aliran.ToArray();
    }

    private static string Label(int nomor) => $"BARIS {nomor:D3}";

    private static int HitungHalaman(byte[] pdf)
    {
        string isi = Encoding.Latin1.GetString(pdf);
        var m = Regex.Match(isi, @"/Type\s*/Pages[\s\S]{0,256}?/Count\s+(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 1;
    }

    /// <summary>Pusat tiap pita tinta pada satu halaman, diukur dari tepi atas (piksel).</summary>
    private static async Task<double[]> Pita(SudesPdf.Document dokumen, int halaman)
    {
        BitmapSource bitmap = await dokumen.RenderAsync(halaman, 1.0);
        int lebar = bitmap.PixelWidth;
        int tinggi = bitmap.PixelHeight;
        int langkah = lebar * 4;
        var piksel = new byte[langkah * tinggi];
        bitmap.CopyPixels(piksel, langkah, 0);

        var pusat = new List<double>();
        int mulai = -1;
        for (int y = 0; y < tinggi; y++)
        {
            bool ada = false;
            for (int x = 0; x < lebar; x++)
            {
                int i = y * langkah + x * 4;
                if (piksel[i] < 160 && piksel[i + 1] < 160 && piksel[i + 2] < 160)
                {
                    ada = true;
                    break;
                }
            }

            if (ada && mulai < 0) mulai = y;
            if (!ada && mulai >= 0)
            {
                pusat.Add((mulai + y - 1) / 2.0);
                mulai = -1;
            }
        }

        if (mulai >= 0) pusat.Add((mulai + tinggi - 1) / 2.0);
        return pusat.ToArray();
    }
}
