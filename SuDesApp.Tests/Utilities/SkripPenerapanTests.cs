using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Menjalankan skrip penerap pembaruan sungguhan (PowerShell) di folder sementara.
/// Kontraknya: bila penerapan gagal di tengah jalan, keadaan semula harus pulih
/// utuh (berkas lama kembali, berkas baru hilang), dan tidak boleh ada instalasi
/// campuran yang tersisa.
/// </summary>
public sealed class SkripPenerapanTests : IDisposable
{
    private readonly string _akar = Path.Combine(
        Path.GetTempPath(), "sudes-skrip-" + Guid.NewGuid().ToString("N"));

    private string Aplikasi => Path.Combine(_akar, "app");
    private string Sumber => Path.Combine(_akar, "sumber");
    private string Kerja => Path.Combine(_akar, "kerja");
    private string Daftar => Path.Combine(Kerja, "daftar-berkas.txt");
    private string Hapus => Path.Combine(Kerja, "daftar-hapus.txt");
    private string Hasil => Path.Combine(_akar, "hasil.json");

    public SkripPenerapanTests()
    {
        Directory.CreateDirectory(Aplikasi);
        Directory.CreateDirectory(Sumber);
        Directory.CreateDirectory(Kerja);
    }

    public void Dispose()
    {
        try { Directory.Delete(_akar, recursive: true); }
        catch (IOException) { /* folder sementara; dibersihkan OS bila terkunci */ }
        catch (UnauthorizedAccessException) { /* sama seperti di atas */ }
    }

    [Fact]
    public void Penerapan_berhasil_mengganti_menambah_dan_menghapus()
    {
        Tulis(Aplikasi, "a.txt", "lama-a");
        Tulis(Aplikasi, "hapus.txt", "lama-h");
        Tulis(Sumber, "a.txt", "baru-a");
        Tulis(Sumber, Path.Combine("sub", "c.txt"), "baru-c");
        File.WriteAllText(Daftar, "a.txt\r\nsub\\c.txt\r\n", new UTF8Encoding(false));
        File.WriteAllText(Hapus, "hapus.txt\r\n", new UTF8Encoding(false));

        JalankanSkrip();

        Assert.Equal("baru-a", File.ReadAllText(Path.Combine(Aplikasi, "a.txt")));
        Assert.Equal("baru-c", File.ReadAllText(Path.Combine(Aplikasi, "sub", "c.txt")));
        Assert.False(File.Exists(Path.Combine(Aplikasi, "hapus.txt")));
        Assert.True(BacaHasil().GetProperty("Berhasil").GetBoolean());
    }

    [Fact]
    public void Penerapan_yang_gagal_di_tengah_memulihkan_keadaan_semula()
    {
        Tulis(Aplikasi, "a.txt", "lama-a");
        Tulis(Aplikasi, "hapus.txt", "lama-h");
        Tulis(Sumber, "a.txt", "baru-a");
        Tulis(Sumber, "baru.txt", "baru-b");
        // sub\hilang.txt sengaja tidak ada di sumber: salinannya gagal SETELAH
        // a.txt dan baru.txt sudah diganti/ditambahkan.
        File.WriteAllText(Daftar, "a.txt\r\nbaru.txt\r\nsub\\hilang.txt\r\n", new UTF8Encoding(false));
        File.WriteAllText(Hapus, "hapus.txt\r\n", new UTF8Encoding(false));

        JalankanSkrip();

        Assert.Equal("lama-a", File.ReadAllText(Path.Combine(Aplikasi, "a.txt")));
        Assert.False(File.Exists(Path.Combine(Aplikasi, "baru.txt")));
        Assert.Equal("lama-h", File.ReadAllText(Path.Combine(Aplikasi, "hapus.txt")));
        Assert.False(File.Exists(Path.Combine(Aplikasi, "sub", "hilang.txt")));

        var hasil = BacaHasil();
        Assert.False(hasil.GetProperty("Berhasil").GetBoolean());
        Assert.Contains("dipulihkan", hasil.GetProperty("Pesan").GetString());
    }

    private JsonElement BacaHasil() =>
        JsonDocument.Parse(File.ReadAllText(Hasil)).RootElement.Clone();

    private void JalankanSkrip()
    {
        var skrip = Path.Combine(Kerja, "terapkan-tambalan.ps1");
        File.WriteAllText(skrip, PatchUpdateService.SkripPenerapan(), new UTF8Encoding(false));

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(skrip);
        info.ArgumentList.Add("-ProsesId");
        info.ArgumentList.Add("0");
        info.ArgumentList.Add("-Aplikasi");
        info.ArgumentList.Add(Aplikasi);
        info.ArgumentList.Add("-Sumber");
        info.ArgumentList.Add(Sumber);
        info.ArgumentList.Add("-Daftar");
        info.ArgumentList.Add(Daftar);
        info.ArgumentList.Add("-Hapus");
        info.ArgumentList.Add(Hapus);
        info.ArgumentList.Add("-Hasil");
        info.ArgumentList.Add(Hasil);
        info.ArgumentList.Add("-Versi");
        info.ArgumentList.Add("9.9.9");
        info.ArgumentList.Add("-Mulai");
        info.ArgumentList.Add(string.Empty);

        using var proses = Process.Start(info)!;
        Assert.True(proses.WaitForExit(60_000), "Skrip penerapan melewati batas waktu.");
    }

    private static void Tulis(string akar, string relatif, string isi)
    {
        var path = Path.Combine(akar, relatif);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, isi, new UTF8Encoding(false));
    }
}
