using System.Diagnostics;
using SudesPdf.Core;
using SudesPdf.Core.Layout;
using Xunit;
using Xunit.Abstractions;

namespace SuDesApp.Tests.Perf;

/// <summary>
/// Harness angka untuk jalur pratinjau: muat dokumen lalu raster halaman pada
/// skala tampil. Isinya BUKAN uji kebenaran tata letak (itu tugas
/// <c>CetakContohSuratTests</c>); tujuannya mencetak durasi supaya perubahan
/// mesin render bisa dinilai dengan angka, bukan kesan "terasa berat".
///
/// Jalankan dengan <c>-c Release</c>: build Debug membuat render 4-5x lebih
/// lambat sehingga kesimpulan tentang bagian mana yang mahal bisa terbalik.
/// Angka keluar lewat <see cref="ITestOutputHelper"/>, jadi perintah uji ala
/// README (<c>-v q</c>) menyembunyikannya — tambahkan
/// <c>--logger "console;verbosity=detailed"</c>.
///
/// Sengaja memakai <see cref="PdfRenderer"/> (bukan <c>Document</c> WPF) agar
/// harness tidak bergantung pada thread UI; yang diukur adalah mesin render,
/// bagian yang memang mahal.
/// </summary>
public sealed class HarnessRenderPratinjauTests
{
    private readonly ITestOutputHelper _output;

    public HarnessRenderPratinjauTests(ITestOutputHelper output) => _output = output;

    /// <summary>Skala pratinjau nyata: halaman A4 (595 pt) tampil ±800 px.</summary>
    private const double SkalaPratinjau = 800.0 / 595.0;

    /// <summary>Batas longgar: hanya menangkap kemunduran besar, bukan variasi mesin uji.</summary>
    private const double BatasRenderDinginMs = 5000;

    [Fact]
    public void RenderSuratSatuHalamanTerukur()
    {
        byte[] pdf = BuatSuratSatuHalaman();
        Ukur("surat 1 halaman (kop + isian)", pdf);
    }

    [Fact]
    public void RenderLaporanTabelPadatTerukur()
    {
        byte[] pdf = BuatLaporanTabelPadat(halaman: 3);
        Ukur("laporan tabel padat 3 halaman", pdf);
    }

    private void Ukur(string nama, byte[] pdf)
    {
        var jamMuat = Stopwatch.StartNew();
        var dokumen = PdfDocument.Load(pdf);
        jamMuat.Stop();

        Assert.True(dokumen.PageCount > 0);

        // Halaman pertama sekali (dingin: cache font/gambar masih kosong), lalu
        // dua kali lagi (hangat). Selisih keduanya menunjukkan berapa besar
        // biaya yang masih dibayar ulang setiap kali pengguna menggulir/zoom.
        var jamRender = Stopwatch.StartNew();
        var dingin = PdfRenderer.RenderPage(dokumen, 0, SkalaPratinjau);
        jamRender.Stop();

        var hangat = new List<double>();
        for (int i = 0; i < 2; i++)
        {
            var jam = Stopwatch.StartNew();
            var gambar = PdfRenderer.RenderPage(dokumen, 0, SkalaPratinjau);
            jam.Stop();
            hangat.Add(jam.Elapsed.TotalMilliseconds);
            Assert.Equal(dingin.Width, gambar.Width);
            Assert.Equal(dingin.Height, gambar.Height);
        }
        hangat.Sort();

        Assert.Equal(dingin.Width * dingin.Height * 4, dingin.Pixels.Length);
        Assert.True(dingin.Width > 100 && dingin.Height > 100,
            $"Raster halaman terlalu kecil: {dingin.Width}x{dingin.Height}");

        _output.WriteLine(
            "[harness] {0}: muat {1:N1} ms | render dingin {2:N1} ms | render hangat {3:N1}/{4:N1} ms | {5}x{6} px",
            nama, jamMuat.Elapsed.TotalMilliseconds, jamRender.Elapsed.TotalMilliseconds,
            hangat[0], hangat[1], dingin.Width, dingin.Height);

        Assert.True(jamRender.Elapsed.TotalMilliseconds < BatasRenderDinginMs,
            $"Render halaman {nama} memakan {jamRender.Elapsed.TotalMilliseconds:N0} ms — " +
            "jauh di atas batas longgar harness; periksa kemunduran mesin render.");
    }

    /// <summary>
    /// Satu halaman selebar A4 seperti surat keterangan: kop bergambar (logo
    /// desa), judul, isian bertabel, dan penutup. Gambar kop ikut disertakan
    /// karena kop adalah bagian termahal pada dokumen surat sungguhan.
    /// </summary>
    private static byte[] BuatSuratSatuHalaman()
    {
        string? logo = PathLogo();
        using var penampung = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(halaman =>
            {
                halaman.Size(new PageSize(595, 842, Unit.Point));
                halaman.MarginTop(36);
                halaman.MarginBottom(36);
                halaman.MarginLeft(45);
                halaman.MarginRight(45);
                halaman.DefaultTextStyle(teks => teks.FontFamily("Times New Roman").FontSize(12));

                halaman.Content().Column(kolom =>
                {
                    if (logo is not null)
                    {
                        kolom.Item().Row(baris =>
                        {
                            baris.AutoItem().Image(logo).FitHeight();
                            baris.RelativeItem().Column(tengah =>
                            {
                                tengah.Item().Text("PEMERINTAH KABUPATEN BEKASI").FontSize(14).Bold().AlignCenter();
                                tengah.Item().Text("KECAMATAN CIKARANG").FontSize(13).Bold().AlignCenter();
                                tengah.Item().Text("DESA SUKAMAJU").FontSize(16).Bold().AlignCenter();
                                tengah.Item().Text("Jl. Raya Sukamaju No. 1, Kode Pos 17530")
                                    .FontSize(9).AlignCenter();
                            });
                        });
                    }

                    kolom.Item().LineHorizontal(1.5f);
                    kolom.Item().Text("SURAT KETERANGAN DESA").FontSize(14).Bold().AlignCenter();
                    kolom.Item().Text("NOMOR : 470/001/Ds/2026").FontSize(12).AlignCenter();

                    kolom.Item().Text(
                            "Yang bertanda tangan di bawah ini Kepala Desa Sukamaju, Kecamatan Cikarang, " +
                            "Kabupaten Bekasi, dengan ini menerangkan bahwa:")
                        .Justify();

                    kolom.Item().Table(tabel =>
                    {
                        tabel.ColumnsDefinition(kolomTabel =>
                        {
                            kolomTabel.ConstantColumn(120);
                            kolomTabel.RelativeColumn();
                        });

                        void Baris(string label, string nilai)
                        {
                            tabel.Cell().Text(label);
                            tabel.Cell().Text(nilai);
                        }

                        Baris("Nama", "BUDI SANTOSO");
                        Baris("NIK", "3216010101900001");
                        Baris("Tempat/Tanggal Lahir", "Bekasi, 1 Januari 1990");
                        Baris("Jenis Kelamin", "Laki-laki");
                        Baris("Pekerjaan", "Karyawan Swasta");
                        Baris("Alamat", "Jl. Melati No. 12, RT 002/RW 003, Desa Sukamaju");
                        Baris("Keperluan", "Persyaratan administrasi");
                    });

                    for (int i = 0; i < 6; i++)
                    {
                        kolom.Item().Text(
                                "Demikian surat keterangan ini dibuat dengan sebenarnya untuk dapat " +
                                "dipergunakan sebagaimana mestinya, atas perhatiannya kami ucapkan terima kasih.")
                            .Justify();
                    }

                    kolom.Item().Column(ttd =>
                    {
                        ttd.Item().AlignRight().Text("Sukamaju, 14 Agustus 2026");
                        ttd.Item().AlignRight().Text("KEPALA DESA SUKAMAJU");
                        ttd.Item().Text(" ");
                        ttd.Item().Text(" ");
                        ttd.Item().AlignRight().Text("H. AHMAD SURYANA").Bold();
                    });
                });
            });
        }).GeneratePdf(penampung);

        return penampung.ToArray();
    }

    /// <summary>Beberapa halaman tabel padat — kasus laporan yang paling mahal di jalur teks.</summary>
    private static byte[] BuatLaporanTabelPadat(int halaman)
    {
        using var penampung = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(deskripsi =>
            {
                deskripsi.Size(new PageSize(595, 842, Unit.Point));
                deskripsi.Margin(36);
                deskripsi.DefaultTextStyle(teks => teks.FontFamily("Times New Roman").FontSize(9));

                deskripsi.Content().Column(kolom =>
                {
                    kolom.Item().Text("LAPORAN REKAPITULASI DATA DESA").FontSize(12).Bold().AlignCenter();
                    kolom.Item().Text("Tahun Anggaran 2026").FontSize(10).AlignCenter();

                    for (int h = 0; h < halaman; h++)
                    {
                        if (h > 0) kolom.Item().PageBreak();

                        kolom.Item().Table(tabel =>
                        {
                            tabel.ColumnsDefinition(k =>
                            {
                                k.ConstantColumn(30);
                                k.RelativeColumn(2);
                                k.RelativeColumn(1);
                                k.RelativeColumn(1);
                                k.RelativeColumn(1);
                            });

                            tabel.Header(kepala =>
                            {
                                kepala.Cell().Text("No.").Bold();
                                kepala.Cell().Text("Uraian").Bold();
                                kepala.Cell().Text("Anggaran").Bold();
                                kepala.Cell().Text("Realisasi").Bold();
                                kepala.Cell().Text("Sisa").Bold();
                            });

                            for (int baris = 1; baris <= 28; baris++)
                            {
                                tabel.Cell().Text(baris.ToString());
                                tabel.Cell().Text(
                                    $"Belanja operasional kegiatan nomor {h + 1}.{baris} " +
                                    "untuk penyelenggaraan pemerintahan desa");
                                tabel.Cell().Text($"{baris * 1_250_000:N0}");
                                tabel.Cell().Text($"{baris * 940_000:N0}");
                                tabel.Cell().Text($"{baris * 310_000:N0}");
                            }
                        });
                    }
                });
            });
        }).GeneratePdf(penampung);

        return penampung.ToArray();
    }

    /// <summary>Logo aplikasi di repo; kosong bila harness dijalankan di luar checkout.</summary>
    private static string? PathLogo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string calon = Path.Combine(dir.FullName, "Resources", "logo.png");
            if (File.Exists(calon)) return calon;
            dir = dir.Parent;
        }
        return null;
    }
}
