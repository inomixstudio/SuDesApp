using SuDesApp.GeneratorPdf;
using Xunit;

namespace SuDesApp.Tests.Surat;

public sealed class SuratPdfSaveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sudes-pdf-save-" + Guid.NewGuid().ToString("N"));

    public SuratPdfSaveTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A failed assertion should not be hidden by best-effort test cleanup.
        }
    }

    [Fact]
    public async Task SimpanAsync_MenggantiTujuanDenganHasilLengkap()
    {
        string tujuan = Path.Combine(_folder, "surat.pdf");
        await File.WriteAllTextAsync(tujuan, "lama");

        string dipakai = await SuratPdfSave.SimpanAsync(tujuan, async stream =>
        {
            await stream.WriteAsync("PDF baru"u8.ToArray());
        });

        Assert.Equal(tujuan, dipakai);
        Assert.Equal("PDF baru", await File.ReadAllTextAsync(tujuan));
        Assert.Equal(new[] { tujuan }, Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task SimpanAsync_JikaPenulisGagal_MembersihkanSementaraDanMempertahankanTujuan()
    {
        string tujuan = Path.Combine(_folder, "surat.pdf");
        await File.WriteAllTextAsync(tujuan, "lama");

        await Assert.ThrowsAsync<IOException>(() => SuratPdfSave.SimpanAsync(tujuan, async stream =>
        {
            await stream.WriteAsync("parsial"u8.ToArray());
            throw new IOException("simulasi kegagalan generator");
        }));

        Assert.Equal("lama", await File.ReadAllTextAsync(tujuan));
        Assert.Equal(new[] { tujuan }, Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task SimpanAsync_JikaTujuanTerkunci_MengembalikanSementaraTanpaMerusakTujuan()
    {
        string tujuan = Path.Combine(_folder, "surat.pdf");
        await File.WriteAllTextAsync(tujuan, "versi lama");
        string sementara;

        using (new FileStream(tujuan, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sementara = await SuratPdfSave.SimpanAsync(tujuan, async stream =>
            {
                await stream.WriteAsync("versi baru"u8.ToArray());
            });

            Assert.NotEqual(tujuan, sementara);
            Assert.True(File.Exists(sementara));
        }

        Assert.Equal("versi lama", await File.ReadAllTextAsync(tujuan));
        Assert.Equal("versi baru", await File.ReadAllTextAsync(sementara));
    }

    [Fact]
    public async Task SimpanAsync_PenulisanBersamaan_MenghasilkanSatuPDFUtuhTanpaBerkasSementaraTertinggal()
    {
        string tujuan = Path.Combine(_folder, "surat.pdf");
        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> Tulis(string isi) => SuratPdfSave.SimpanAsync(tujuan, async stream =>
        {
            await gerbang.Task;
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(isi));
        });

        Task<string> pertama = Tulis("PDF pertama");
        Task<string> kedua = Tulis("PDF kedua");
        gerbang.SetResult();

        string[] hasil = await Task.WhenAll(pertama, kedua);

        Assert.All(hasil, path => Assert.True(File.Exists(path)));
        string[] isi = await Task.WhenAll(hasil.Select(path => File.ReadAllTextAsync(path)));
        Assert.All(isi, konten => Assert.Contains(konten, new[] { "PDF pertama", "PDF kedua" }));
        Assert.Equal(hasil.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path),
            Directory.GetFiles(_folder).OrderBy(path => path));
    }
}
