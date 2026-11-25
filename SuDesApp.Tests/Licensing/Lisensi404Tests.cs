using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Balasan 404 dari <c>/aktivasi</c> hanya boleh membuang cache token bila server
/// menyatakan kode aktivasi tidak terdaftar. 404 dari proxy desa atau rute yang
/// hilang tidak boleh menghapus lisensi yang masih sah.
/// </summary>
public sealed class Lisensi404Tests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "sudes-lisensi-404-" + Guid.NewGuid().ToString("N"));

    public Lisensi404Tests()
    {
        Directory.CreateDirectory(_folder);
        LisensiManager.FolderCacheOverride = _folder;
    }

    public void Dispose()
    {
        LisensiManager.FolderCacheOverride = null;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("{\"pesan\":\"Kode aktivasi tidak dikenal.\"}")]
    [InlineData("{\"pesan\":\"kode aktivasi tidak dikenal\"}")]
    public void Pesan_kode_tidak_dikenal_dikenali(string teks)
        => Assert.True(LisensiManager.KodeAktivasiTidakDikenal(teks));

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("{\"pesan\":\"Rute tidak ditemukan.\"}")]
    [InlineData("{\"error\":\"Kode aktivasi tidak dikenal.\"}")]
    public void Balasan_404_lain_tidak_dianggap_kode_tidak_dikenal(string teks)
        => Assert.False(LisensiManager.KodeAktivasiTidakDikenal(teks));

    [Theory]
    [InlineData("{\"pesan\":\"Kode aktivasi tidak dikenal.\"}", false)]
    [InlineData("<html><body>404 Not Found</body></html>", true)]
    public async Task Cache_token_hanya_dibuang_bila_server_menyatakan_kode_tidak_dikenal(
        string teksBalasan, bool cacheTetap)
    {
        var lokasiCache = Path.Combine(_folder, "lisensi-cache.json");
        await File.WriteAllTextAsync(lokasiCache, "{\"Token\":\"token-sah\"}");
        var lisensi = new LisensiManager(
            NullLogger<LisensiManager>.Instance,
            new HttpClient(new BalasanTetap(HttpStatusCode.NotFound, teksBalasan)))
        {
            KodeAktivasi = "AAAA-BBBB-CCCC-DDDD",
            KunciPublik = "kunci-uji",
        };

        await lisensi.CekAsync();

        Assert.Equal(cacheTetap, File.Exists(lokasiCache));
    }

    private sealed class BalasanTetap(HttpStatusCode status, string isi) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(isi, Encoding.UTF8, "application/json"),
            });
    }
}
