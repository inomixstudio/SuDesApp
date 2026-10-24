using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Denyut sambungan server lisensi menentukan chip "Terhubung / Belum
/// terjangkau" tanpa menunggu halaman dibuka ulang, jadi yang dijaga di sini
/// adalah kejujurannya: server yang menjawab galat tetap dihitung terjangkau,
/// hanya kegagalan jaringan yang menandai tidak terjangkau, dan alamat kosong
/// tidak memancing permintaan ke mana pun.
/// </summary>
public sealed class DetakSambunganServerTests
{
    private sealed class HandlerPalsu : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _balasan;

        public List<string> Diminta { get; } = new();

        public HandlerPalsu(Func<HttpRequestMessage, HttpResponseMessage> balasan) => _balasan = balasan;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Diminta.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(_balasan(request));
        }
    }

    private static (DetakSambunganServer Detak, SambunganServerLisensi Sambungan, HandlerPalsu Handler) Buat(
        string urlServer, Func<HttpRequestMessage, HttpResponseMessage>? balasan = null)
    {
        var sambungan = new SambunganServerLisensi();
        var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance,
            new HttpClient(), sambungan)
        {
            UrlServer = urlServer
        };
        var handler = new HandlerPalsu(balasan ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var detak = new DetakSambunganServer(lisensi, new HttpClient(handler),
            NullLogger<DetakSambunganServer>.Instance, sambungan);
        return (detak, sambungan, handler);
    }

    [Fact]
    public async Task Healthz_yang_menjawab_menandai_terhubung()
    {
        var (detak, sambungan, handler) = Buat("http://127.0.0.1:5110");

        await detak.CekSekaliAsync();

        Assert.Equal(KeadaanSambunganServer.Terhubung, sambungan.Sekarang);
        Assert.Equal("http://127.0.0.1:5110/healthz", Assert.Single(handler.Diminta));
    }

    [Fact]
    public async Task Galat_server_tetap_dihitung_terhubung()
    {
        var (detak, sambungan, _) = Buat("http://127.0.0.1:5110",
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await detak.CekSekaliAsync();

        Assert.Equal(KeadaanSambunganServer.Terhubung, sambungan.Sekarang);
    }

    [Fact]
    public async Task Kegagalan_jaringan_menandai_tidak_terjangkau()
    {
        var (detak, sambungan, _) = Buat("http://127.0.0.1:5110",
            _ => throw new HttpRequestException("koneksi ditolak"));

        await detak.CekSekaliAsync();

        Assert.Equal(KeadaanSambunganServer.TidakTerjangkau, sambungan.Sekarang);
    }

    [Fact]
    public async Task Tanpa_alamat_tidak_menghubungi_apa_pun()
    {
        var (detak, sambungan, handler) = Buat(string.Empty);

        await detak.CekSekaliAsync();

        Assert.Equal(KeadaanSambunganServer.BelumDiuji, sambungan.Sekarang);
        Assert.Empty(handler.Diminta);
    }

    [Fact]
    public async Task Galat_lokal_tidak_menandai_tidak_terjangkau()
    {
        var (detak, sambungan, _) = Buat("http://127.0.0.1:5110",
            _ => throw new InvalidOperationException("alamat tidak sah"));

        await detak.CekSekaliAsync();

        // Servernya belum tentu bermasalah — hanya permintaannya yang tak bisa
        // dibentuk — jadi keadaan terakhir tidak boleh berubah.
        Assert.Equal(KeadaanSambunganServer.BelumDiuji, sambungan.Sekarang);
    }

    [Fact]
    public void Jeda_pendek_saat_belum_terhubung_dan_panjang_setelah_terhubung()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DetakSambunganServer.Jeda(KeadaanSambunganServer.BelumDiuji));
        Assert.Equal(TimeSpan.FromSeconds(30), DetakSambunganServer.Jeda(KeadaanSambunganServer.TidakTerjangkau));
        Assert.Equal(TimeSpan.FromMinutes(5), DetakSambunganServer.Jeda(KeadaanSambunganServer.Terhubung));
    }
}
