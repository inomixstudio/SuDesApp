using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Keadaan sambungan server dipakai baris menu Pusat Administrasi di sidebar,
/// jadi yang dijaga di sini adalah KEJUJURANNYA: "Terhubung" hanya setelah ada
/// balasan dari server — termasuk balasan 404/500, karena server yang menjawab
/// galat tetap server yang hidup — dan "Belum terjangkau" hanya untuk kegagalan
/// jaringan sungguhan. Kegagalan lokal (mis. berkas JSON tidak sah) tidak boleh
/// menandai server sebagai tidak terjangkau.
/// </summary>
public sealed class SambunganServerTests
{
    private sealed class HandlerPalsu : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _balasan;

        public HandlerPalsu(Func<HttpResponseMessage> balasan) => _balasan = balasan;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_balasan());
    }

    private static (LanggananKlien Klien, SambunganServerLisensi Sambungan) BuatKlien(string urlServer,
        Func<HttpResponseMessage> balasan)
    {
        var sambungan = new SambunganServerLisensi();
        var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance,
            new HttpClient(new HandlerPalsu(balasan)), sambungan)
        {
            UrlServer = urlServer
        };
        var klien = new LanggananKlien(lisensi, new HttpClient(new HandlerPalsu(balasan)),
            NullLogger<LanggananKlien>.Instance, sambungan);
        return (klien, sambungan);
    }

    [Fact]
    public async Task Server_menjawab_galat_tetap_dibilang_terjangkau()
    {
        var (klien, sambungan) = BuatKlien("http://localhost:5000",
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var hasil = await klien.MuatDariServerAsync(paksa: true);

        Assert.Equal(HasilMuatKatalog.Kosong, hasil.Hasil);
        Assert.Equal(KeadaanSambunganServer.Terhubung, sambungan.Sekarang);
    }

    [Fact]
    public async Task Kegagalan_jaringan_menandai_tidak_terjangkau_dan_kabar_menyala_saat_benar_benar_berubah()
    {
        var (klien, sambungan) = BuatKlien("http://localhost:5000",
            () => throw new HttpRequestException("koneksi ditolak"));

        int pemberitahuan = 0;
        sambungan.Berubah += () => pemberitahuan++;

        var hasil = await klien.MuatDariServerAsync(paksa: true);

        Assert.Equal(HasilMuatKatalog.Gagal, hasil.Hasil);
        Assert.Equal(KeadaanSambunganServer.TidakTerjangkau, sambungan.Sekarang);
        Assert.Equal(1, pemberitahuan);

        // Percobaan gagal berikutnya: keadaan tidak berubah, jadi tidak ada
        // pemberitahuan baru — sidebar tidak berkedip tanpa sebab.
        await klien.MuatDariServerAsync(paksa: true);
        Assert.Equal(1, pemberitahuan);
    }

    [Fact]
    public async Task Tanpa_alamat_server_keadaan_tetap_belum_diuji()
    {
        var (klien, sambungan) = BuatKlien("", () => new HttpResponseMessage(HttpStatusCode.OK));

        var hasil = await klien.MuatDariServerAsync(paksa: true);

        Assert.Equal(HasilMuatKatalog.Kosong, hasil.Hasil);
        Assert.Equal(KeadaanSambunganServer.BelumDiuji, sambungan.Sekarang);
        Assert.Equal("Belum diuji", SambunganServerLisensi.Teks(sambungan.Sekarang));
    }
}
