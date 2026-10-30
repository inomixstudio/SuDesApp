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

    // ===== Tabel penuh halaman + blok tanda tangan (ShowEntire) =====

    private const float MarginUji = 50f;
    private static readonly PageSize KertasUji = PageSizes.A4;

    [Fact]
    public async Task BlokTandaTanganSesudahTabelPenuhPindahHalamanTidakTerpotong()
    {
        byte[] pdf = BuatTabelDanBlokTandaTangan();

        await using var dokumen = await SudesPdf.Document.LoadAsync(pdf);

        // Seluruh isi tabel tetap ada, dan blok tanda tangan utuh di SATU halaman.
        var teks = new StringBuilder();
        for (int nomor = 0; nomor < dokumen.PageCount; nomor++)
        {
            teks.AppendLine(await dokumen.GetTextAsync(nomor));
        }

        Assert.Contains("PESERTA 30", teks.ToString(), StringComparison.Ordinal);
        Assert.Contains("KEPALA DESA", teks.ToString(), StringComparison.Ordinal);

        // Baris kota, jabatan, DAN nama pejabat harus berada di halaman yang sama:
        // dulu blok ini mulai di sisa ruang beberapa titik di tepi bawah kertas,
        // sehingga dua barisnya tercetak lalu sisanya terpotong hilang.
        int halamanBlok = -1;
        for (int nomor = 0; nomor < dokumen.PageCount; nomor++)
        {
            string isi = await dokumen.GetTextAsync(nomor);
            if (!isi.Contains("KEPALA DESA", StringComparison.Ordinal)) continue;

            Assert.Equal(-1, halamanBlok);
            halamanBlok = nomor;
            Assert.Contains("NAMA PEJABAT", isi, StringComparison.Ordinal);
            Assert.Contains("Sukamaju,", isi, StringComparison.Ordinal);
        }

        Assert.True(halamanBlok > 0,
            "Blok tanda tangan seharusnya pindah ke halaman berikutnya, bukan menempel di bawah tabel.");

        // Tidak ada tinta yang jatuh ke dalam margin bawah (atau lewat tepi kertas).
        double batasBawah = KertasUji.Height - MarginUji + 1.0;
        for (int nomor = 0; nomor < dokumen.PageCount; nomor++)
        {
            double[] pita = await Pita(dokumen, nomor);
            Assert.NotEmpty(pita);
            Assert.True(pita[^1] <= batasBawah,
                $"Halaman {nomor + 1}: ada tinta di y={pita[^1]:F1}, melewati batas cetak {batasBawah:F1}.");
        }
    }

    private static byte[] BuatTabelDanBlokTandaTangan()
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(KertasUji);
                halaman.Margin(MarginUji);
                halaman.DefaultTextStyle(x => x.FontSize(11));
                halaman.Content().Column(kolom =>
                {
                    kolom.Item().Table(tabel =>
                    {
                        tabel.ColumnsDefinition(k =>
                        {
                            k.ConstantColumn(30);
                            k.RelativeColumn(3);
                        });

                        tabel.Header(kepala =>
                        {
                            kepala.Cell().Border(1).Padding(4).Text("NO");
                            kepala.Cell().Border(1).Padding(4).Text("NAMA");
                        });

                        // 30 baris setinggi 46 pt: tabel berlanjut ke halaman kedua dan
                        // justru DI SANA menghabiskan hampir seluruh area cetak,
                        // menyisakan beberapa titik saja di bawahnya.
                        for (int i = 1; i <= 30; i++)
                        {
                            tabel.Cell().Border(1).MinHeight(46).Padding(4).Text(i.ToString());
                            tabel.Cell().Border(1).MinHeight(46).Padding(4).Text($"PESERTA {i}");
                        }
                    });

                    kolom.Item().PaddingTop(12).ShowEntire().Column(blok =>
                    {
                        blok.Item().AlignCenter().Text("Sukamaju, 30 Oktober 2026");
                        blok.Item().AlignCenter().Text("KEPALA DESA");
                        blok.Item().Height(40);
                        blok.Item().AlignCenter().Text("NAMA PEJABAT").Bold().Underline();
                    });
                });
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);
        return aliran.ToArray();
    }

    // ===== Pemisah halaman (PageBreak) =====

    /// <summary>
    /// <c>PageBreak()</c> harus benar-benar memulai lembar baru. Dulu sisa isi
    /// sesudahnya ditaruh di sisa ruang halaman yang sama, sehingga judul lampiran
    /// (mis. “LAMPIRAN” pada Perdes SOTK) menempel di bawah blok tanda tangan.
    /// </summary>
    [Fact]
    public async Task PemisahHalamanMemindahkanIsiBerikutnyaKeLembarBaru()
    {
        var dokumen = Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(PageSizes.A4);
                halaman.Margin(36);
                halaman.Content().Column(kolom =>
                {
                    kolom.Item().Text("SEBELUM PEMISAH");
                    kolom.Item().PageBreak();
                    kolom.Item().Text("SESUDAH PEMISAH");
                });
            });
        });

        using var aliran = new MemoryStream();
        dokumen.GeneratePdf(aliran);

        await using var hasil = await SudesPdf.Document.LoadAsync(aliran.ToArray());
        Assert.Equal(2, hasil.PageCount);

        string pertama = await hasil.GetTextAsync(0);
        string kedua = await hasil.GetTextAsync(1);
        Assert.Contains("SEBELUM PEMISAH", pertama, StringComparison.Ordinal);
        Assert.DoesNotContain("SESUDAH PEMISAH", pertama, StringComparison.Ordinal);
        Assert.Contains("SESUDAH PEMISAH", kedua, StringComparison.Ordinal);

        // Halaman kedua dimulai dari tepi atas area cetak, bukan di tengah kertas.
        double[] pita = await Pita(hasil, 1);
        Assert.NotEmpty(pita);
        Assert.True(pita[0] <= 60,
            $"Isi sesudah pemisah tercetak di y={pita[0]:F1} — bukan di awal halaman baru.");
    }
}
