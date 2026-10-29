using System.Text;
using SudesPdf;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using Xunit;

namespace SuDesApp.Tests.Perdes;

/// <summary>
/// Naskah contoh Perdes kini berupa data halaman, bukan berkas Word. Uji ini
/// memastikan keenam contoh terbaca, identitas desa contoh diganti, dan
/// generator SudesPdf menghasilkan PDF beberapa halaman dengan nomor/tanggal
/// dari formulir. Berkas hasil ditulis ke %TEMP%/sudesapp-perdes untuk
/// pemeriksaan visual.
/// </summary>
public sealed class NaskahPerdesTests : IDisposable
{
    private static readonly string[] Contoh =
    {
        "Contoh-Perdes-APBDes.json",
        "Contoh-Perdes-BPD.json",
        "Contoh-Perdes-Kewenangan-Asal-Usul.json",
        "Contoh-Perdes-Kewenangan-Ditugaskan.json",
        "Contoh-Perdes-Kewenangan-Lokal-Skala-Desa.json",
        "Contoh-Perdes-PADes-2023.json"
    };

    private readonly string _folderSampel =
        Path.Combine(Path.GetTempPath(), "sudesapp-perdes");

    public NaskahPerdesTests() => Directory.CreateDirectory(_folderSampel);

    public void Dispose()
    {
        // Folder sampel sengaja dipertahankan untuk inspeksi visual.
    }

    private static DesaData DesaContoh() => new()
    {
        NamaDesa = "Sukamaju",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        KepalaDesa = "H. Ahmad Suryana"
    };

    [Theory]
    [MemberData(nameof(SemuaContoh))]
    public void ContohTerbacaDanMencetakBeberapaHalaman(string namaBerkas)
    {
        var naskah = NaskahPerdesContoh.Muat(namaBerkas, DesaContoh());
        Assert.True(naskah.Halaman.Count >= 3, $"{namaBerkas} hanya memuat {naskah.Halaman.Count} halaman.");
        Assert.Contains(naskah.Halaman, h => !h.Kosong);

        var pdf = new NaskahPerdesGenerator(
            DesaContoh(), naskah, "9", new DateTime(2026, 11, 2)).Buat();
        string jalur = Path.Combine(_folderSampel, Path.GetFileNameWithoutExtension(namaBerkas) + ".pdf");
        File.WriteAllBytes(jalur, pdf);

        Assert.True(pdf.Length > 1000);
        string teks = BacaTeks(jalur);
        Assert.Contains("PERATURAN DESA SUKAMAJU", teks, StringComparison.Ordinal);
        Assert.Contains("NOMOR 9 TAHUN 2026", teks, StringComparison.Ordinal);
        Assert.DoesNotContain("SUMBERJAYA", teks, StringComparison.Ordinal);
        Assert.DoesNotContain("KARAWANG", teks, StringComparison.Ordinal);
    }

    [Fact]
    public void NomorDanTanggalTidakMengubahSumberNaskah()
    {
        var naskah = NaskahPerdesContoh.Muat("Contoh-Perdes-APBDes.json", DesaContoh());
        string isiAsal = naskah.Halaman[0].Isi;

        new NaskahPerdesGenerator(DesaContoh(), naskah, "12", new DateTime(2027, 1, 5)).Buat();

        Assert.Equal(isiAsal, naskah.Halaman[0].Isi);
        Assert.Contains("NOMOR 1 TAHUN 2026", isiAsal, StringComparison.Ordinal);
    }

    [Fact]
    public void TanggalPenutupMengikutiTanggalFormulir()
    {
        var naskah = NaskahPerdesContoh.Muat("Contoh-Perdes-APBDes.json", DesaContoh());
        var pdf = new NaskahPerdesGenerator(
            DesaContoh(), naskah, "9", new DateTime(2026, 11, 2)).Buat();

        string jalur = Path.Combine(_folderSampel, "tanggal-penutup.pdf");
        File.WriteAllBytes(jalur, pdf);

        string teks = BacaTeks(jalur);
        Assert.True(teks.Contains("2 November 2026", StringComparison.Ordinal), teks);
    }

    [Fact]
    public void LembaranDesaMemakaiTahunPenetapan()
    {
        var naskah = NaskahPerdesContoh.Muat(
            "Contoh-Perdes-Kewenangan-Asal-Usul.json", DesaContoh());
        var pdf = new NaskahPerdesGenerator(
            DesaContoh(), naskah, "9", new DateTime(2026, 11, 2)).Buat();

        string jalur = Path.Combine(_folderSampel, "lembaran.pdf");
        File.WriteAllBytes(jalur, pdf);

        string teks = BacaTeks(jalur);
        Assert.Contains("LEMBARAN DESA SUKAMAJU TAHUN 2026 NOMOR", teks, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tahun anggaran Perdes mengikuti tanggal formulir di bagian Menimbang,
    /// tetapi rujukan APBD pada bagian Mengingat tetap menyebut tahun dokumen
    /// aslinya.
    /// </summary>
    [Fact]
    public void TahunAnggaranMenimbangMengikutiFormulirTanpaMengubahRujukan()
    {
        var naskah = NaskahPerdesContoh.Muat("Contoh-Perdes-PADes-2023.json", DesaContoh());
        var pdf = new NaskahPerdesGenerator(
            DesaContoh(), naskah, "9", new DateTime(2026, 11, 2)).Buat();

        string jalur = Path.Combine(_folderSampel, "tahun-anggaran.pdf");
        File.WriteAllBytes(jalur, pdf);

        string teks = string.Join(' ', BacaTeks(jalur).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            "Peraturan Desa Sukamaju tentang Pungutan-Pungutan Desa Tahun Anggaran 2026.",
            teks, StringComparison.Ordinal);
        Assert.Contains("Tahun Anggaran 2018", teks, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sumber halaman disimpan di samping PDF arsip supaya data lama dapat
    /// disunting lagi; menghapusnya tidak menyentuh PDF yang sudah terbit.
    /// </summary>
    [Fact]
    public void SumberHalamanTersimpanDanTerbacaDariSampingPdf()
    {
        string folder = Path.Combine(_folderSampel, "sampingan");
        Directory.CreateDirectory(folder);
        string pdf = Path.Combine(folder, "Perdes 1-2026.pdf");
        File.WriteAllBytes(pdf, new byte[] { 1, 2, 3 });

        var naskah = NaskahPerdesContoh.Muat("Contoh-Perdes-BPD.json", DesaContoh());
        NaskahPerdesStore.Simpan(pdf, naskah);

        string sampingan = Path.ChangeExtension(pdf, ".naskah.json");
        Assert.True(File.Exists(sampingan));

        var muat = NaskahPerdesStore.Muat(pdf);
        Assert.NotNull(muat);
        Assert.Equal(naskah.Halaman.Count, muat!.Halaman.Count);
        Assert.Equal(naskah.Halaman[1].Isi, muat.Halaman[1].Isi);

        NaskahPerdesStore.Hapus(pdf);
        Assert.False(File.Exists(sampingan));
        Assert.True(File.Exists(pdf));
    }

    public static IEnumerable<object[]> SemuaContoh()
    {
        foreach (string nama in Contoh) yield return new object[] { nama };
    }

    private static string BacaTeks(string jalur)
    {
        var dokumen = Document.LoadAsync(jalur).GetAwaiter().GetResult();
        try
        {
            var teks = new StringBuilder();
            for (int i = 0; i < dokumen.PageCount; i++)
            {
                teks.AppendLine(dokumen.GetTextAsync(i).GetAwaiter().GetResult());
            }
            return teks.ToString();
        }
        finally
        {
            dokumen.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
