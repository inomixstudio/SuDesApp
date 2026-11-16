using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using SudesPdf.Core;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Blanko formulir pendataan perangkat desa harus selalu tepat SATU halaman,
/// baik di kertas A4 maupun F4. Isinya panjang (kop + petunjuk + 18 baris
/// isian + blok tanda tangan), dan kop bisa tambah baris bila alamat desa
/// membungkus atau ada baris surel — uji ini mengunci kedua keadaan itu.
/// </summary>
[Collection("PreferensiBersama")]
public sealed class FormulirPerangkatSatuHalamanTests : IDisposable
{
    private readonly string _folder;
    private readonly Func<string>? _overrideAwal;

    public FormulirPerangkatSatuHalamanTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sudesapp-uji-formulir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        // Preferensi ukuran kertas dialihkan ke berkas uji supaya pilihan kertas
        // pengguna sesungguhnya tidak diubah dan tidak dibaca.
        _overrideAwal = AppPreferenceStore.LokasiOverride;
        AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "preferensi.json");
    }

    public void Dispose()
    {
        AppPreferenceStore.LokasiOverride = _overrideAwal;
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Berkas sementara non-kritis — kegagalan menghapus tidak menggagalkan uji.
        }
    }

    [Theory]
    [InlineData(UkuranKertasSurat.A4)]
    [InlineData(UkuranKertasSurat.F4)]
    public void Formulir_pendataan_muat_satu_halaman(UkuranKertasSurat ukuran)
    {
        var (pdf, dokumen) = Buat(ukuran, DesaBiasa());

        Assert.Equal(1, dokumen.PageCount);
        Assert.Equal(LebarYangDiharapkan(ukuran), dokumen.Pages[0].Width, precision: 1);

        // Blok tanda tangan ikut di halaman yang sama — bukan hanya bagian atas.
        string teks = PdfRenderer.ExtractText(dokumen.Pages[0]);
        Assert.Contains("Yang memberi data,", teks, StringComparison.Ordinal);
        Assert.Contains("Nama terang & tanda tangan", teks, StringComparison.Ordinal);

        // Garis tanda tangan pemberi data harus setingkat dengan nama pejabat.
        Assert.True(
            GarisTandaTanganSejajar(dokumen),
            "Garis tanda tangan pemberi data tidak setingkat dengan nama pejabat desa.");

        File.WriteAllBytes(Path.Combine(_folder, $"formulir-{ukuran}.pdf"), pdf);
    }

    /// <summary>
    /// Kop yang tambah tinggi (alamat membungkus + baris surel, atau NIP pejabat
    /// kelurahan) tidak boleh mendorong blok tanda tangan ke halaman kedua.
    /// </summary>
    [Theory]
    [InlineData(UkuranKertasSurat.A4)]
    [InlineData(UkuranKertasSurat.F4)]
    public void Kop_lebih_panjang_tetap_satu_halaman(UkuranKertasSurat ukuran)
    {
        foreach (var desa in new DesaData[] { DesaAlamatPanjang(), DesaKelurahan() })
        {
            var (_, dokumen) = Buat(ukuran, desa);

            Assert.True(dokumen.PageCount == 1,
                $"Formulir untuk {desa.NamaDesa} di {ukuran} terlempar ke {dokumen.PageCount} halaman.");
        }
    }

    /// <summary>
    /// Memeriksa kesejajaran blok tanda tangan lewat raster halaman: garis
    /// titik-titik pemberi data (sel kiri) harus berada pada rentang y yang
    /// sama dengan nama pejabat (sel kanan). Keduanya memang dibangun sebagai
    /// baris tabel yang sama, dan uji ini menangkap kemunduran bila susunannya
    /// diubah kembali menjadi dua kolom yang tidak berbagi baris.
    /// </summary>
    private static bool GarisTandaTanganSejajar(PdfDocument dokumen)
    {
        var raster = PdfRenderer.RenderPage(dokumen, 0, 2.0);
        int lebar = raster.Width, tinggi = raster.Height;
        int paruh = lebar / 2;

        bool Tinta(int x, int y)
        {
            int i = (y * lebar + x) * 4;
            return raster.Pixels[i] < 200 || raster.Pixels[i + 1] < 200 || raster.Pixels[i + 2] < 200;
        }

        // Rentang baris bertinta di bawah 30% halaman (blok tanda tangan).
        var baris = new List<(int Atas, int Bawah)>();
        int awal = -1;
        for (int y = (int)(tinggi * 0.70); y < tinggi; y++)
        {
            bool ada = false;
            for (int x = 0; x < lebar; x += 2)
            {
                if (Tinta(x, y)) { ada = true; break; }
            }

            if (ada && awal < 0) awal = y;
            if (!ada && awal >= 0)
            {
                baris.Add((awal, y - 1));
                awal = -1;
            }
        }
        if (awal >= 0) baris.Add((awal, tinggi - 1));

        // Garis titik-titik adalah pita tinta paling tipis di paruh kiri.
        (int Atas, int Bawah) terpilih = (0, -1);
        int tipisTerkecil = int.MaxValue;
        foreach (var b in baris)
        {
            int jumlah = 0;
            for (int y = b.Atas; y <= b.Bawah; y++)
            {
                for (int x = 0; x < paruh; x++)
                {
                    if (Tinta(x, y)) { jumlah++; break; }
                }
            }

            if (jumlah > 0 && jumlah < tipisTerkecil)
            {
                tipisTerkecil = jumlah;
                terpilih = b;
            }
        }

        if (terpilih.Bawah < terpilih.Atas)
        {
            return false;
        }

        // Nama pejabat harus ikut berada pada rentang y yang sama.
        for (int y = terpilih.Atas; y <= terpilih.Bawah; y++)
        {
            for (int x = paruh; x < lebar; x++)
            {
                if (Tinta(x, y)) return true;
            }
        }

        return false;
    }

    private static double LebarYangDiharapkan(UkuranKertasSurat ukuran) =>
        ukuran == UkuranKertasSurat.F4 ? 607.5 : 595.28;

    private (byte[] Pdf, PdfDocument Dokumen) Buat(UkuranKertasSurat ukuran, DesaData desa)
    {
        PengaturanCetak.SetUkuranKertas(ukuran);

        string cfg = Path.Combine(_folder, "appsettings-uji.json");
        string Sep(string p) => p.Replace('\\', '/');
        File.WriteAllText(cfg, $$"""
        {
          "AppConfig": {
            "databaseConnectionString": "Data Source={{Sep(Path.Combine(_folder, "desa-uji.db"))}}",
            "templateFolder": "{{Sep(Path.Combine(CariAkar(), "Templates"))}}",
            "tempPdfFolder": "{{Sep(Path.Combine(_folder, "TempPDF"))}}",
            "pdfOutputPath": "{{Sep(Path.Combine(_folder, "Output"))}}",
            "fontFolder": "{{Sep(Path.Combine(CariAkar(), "Resources", "Fonts"))}}",
            "logoPath": "{{Sep(Path.Combine(CariAkar(), "Resources", "logo.png"))}}",
            "updateCheckUrl": null
          }
        }
        """);

        var config = new AppConfig(cfg);
        var generator = new FormulirPerangkatGenerator(
            config, NullLogger<FormulirPerangkatGenerator>.Instance);

        using var ms = new MemoryStream();
        generator.GeneratePdf(ms, new FormulirPerangkatData { Desa = desa });
        byte[] pdf = ms.ToArray();

        return (pdf, PdfDocument.Load(pdf));
    }

    private static DesaData DesaBiasa() => new()
    {
        NamaDesa = "Sumberjaya",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        Alamat = "Jl. Raya Sukamaju No. 1",
        Kodepos = "17530",
        KepalaDesa = "H. Ahmad Suryana",
        SekretarisDesa = "Dedi Suryadi",
    };

    private static DesaData DesaAlamatPanjang() => new()
    {
        NamaDesa = "Sumberjaya",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        Alamat = "Jl. Raya Sukamaju No. 1 Gang Melati RT 003 RW 007 Perumahan Griya Asri Blok C-12",
        Kodepos = "17530",
        KepalaDesa = "H. Ahmad Suryana",
        SekretarisDesa = "Dedi Suryadi",
        Email = "desa.sumberjaya@example.id",
    };

    private static DesaData DesaKelurahan() => new()
    {
        NamaDesa = "Sukamaju",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        Alamat = "Jl. Kenanga No. 8",
        Kodepos = "17530",
        KepalaDesa = "Drs. H. Endang Mulyana",
        NipKepalaDesa = "196805121990031004",
        SekretarisDesa = "Dedi Suryadi",
        Email = "kel.sukamaju@example.id",
    };

    private static string CariAkar()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx"))) return folder.FullName;
            folder = folder.Parent;
        }
        throw new InvalidOperationException("Folder repo tidak ditemukan");
    }
}
