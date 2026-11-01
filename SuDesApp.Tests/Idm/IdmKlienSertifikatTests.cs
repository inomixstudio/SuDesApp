using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Jalur cadangan HTTP pada <see cref="IdmKemendesaKlien"/>. Situs pusat pernah
/// kedaluwarsa sertifikatnya (Agustus 2026) sehingga seluruh halaman IDM kosong
/// padahal datanya masih ada. Empat hal yang mudah rusak tanpa terlihat dijaga
/// di sini: cadangan hanya untuk kegagalan SERTIFIKAT (bukan jaringan biasa),
/// jawaban HTTP selalu berpenanda <c>LewatHttp</c> supaya peringatan tampil,
/// kegagalan ganda tetap menjadi galat yang jelas, dan HTTPS sehat tidak pernah
/// menyentuh HTTP.
/// </summary>
public sealed class IdmKlienSertifikatTests
{
    private const string KodeDesa = "3215192005";

    private const string JawabanSah = """
        {
          "error": false,
          "mapData": {
            "SUMMARIES": { "SKOR_SAAT_INI": 0.7870, "STATUS": "MAJU", "TAHUN": 2024 },
            "ROW": [ { "INDIKATOR": "IDM 2024", "SKOR": "0.7870" } ]
          }
        }
        """;

    [Fact]
    public async Task Sertifikat_bermasalah_jatuh_ke_http_dan_menandai_lewat_http()
    {
        var handler = new HandlerPalsu(request =>
            request.RequestUri!.Scheme == "https"
                ? throw SertifikatKedaluwarsa()
                : Jawaban(JawabanSah));
        var muatan = await Klien(handler).MuatAsync(KodeDesa, 2024);

        Assert.Equal(StatusMuatIdm.Berhasil, muatan.Status);
        Assert.True(muatan.LewatHttp);
        Assert.Equal(0.7870, muatan.Hasil!.Ringkasan.Skor, 4);

        // Cadangan memang memakai alamat HTTP — bukan sekadar menandai hasil.
        Assert.Contains(handler.Permintaan, u => u.Scheme == "http");
    }

    [Fact]
    public async Task Sertifikat_bermasalah_tapi_http_bisa_membaca_jawaban_tidak_ditemukan()
    {
        // Tahun yang belum terbit dijawab 400 + pesan; jalur cadangan tidak boleh
        // mengubahnya jadi galat jaringan supaya pemuatan multi-tahun tetap
        // melompati tahun kosong.
        var handler = new HandlerPalsu(request =>
            request.RequestUri!.Scheme == "https"
                ? throw SertifikatKedaluwarsa()
                : Jawaban("""{ "error": true, "message": "ID Desa tidak ditemukan" }"""));
        var muatan = await Klien(handler).MuatAsync(KodeDesa, 2026);

        Assert.Equal(StatusMuatIdm.TidakDitemukan, muatan.Status);
        Assert.Equal("ID Desa tidak ditemukan", muatan.Pesan);
    }

    [Fact]
    public async Task Sertifikat_bermasalah_dan_http_juga_gagal_menghasilkan_galat_jelas()
    {
        var handler = new HandlerPalsu(request =>
            request.RequestUri!.Scheme == "https"
                ? throw SertifikatKedaluwarsa()
                : throw new HttpRequestException("Connection refused",
                    new SocketException((int)SocketError.ConnectionRefused)));
        var muatan = await Klien(handler).MuatAsync(KodeDesa, 2024);

        Assert.Equal(StatusMuatIdm.Gagal, muatan.Status);
        Assert.Contains("jalur cadangan HTTP", muatan.Pesan);
    }

    [Fact]
    public async Task Galat_jaringan_bukan_sertifikat_tidak_mencoba_http()
    {
        var handler = new HandlerPalsu(request =>
            throw new HttpRequestException("No such host is known",
                new SocketException((int)SocketError.HostNotFound)));
        var muatan = await Klien(handler).MuatAsync(KodeDesa, 2024);

        Assert.Equal(StatusMuatIdm.Gagal, muatan.Status);
        Assert.Contains("tidak terjangkau", muatan.Pesan);
        Assert.DoesNotContain(handler.Permintaan, u => u.Scheme == "http");
    }

    [Fact]
    public async Task Sertifikat_sehat_tidak_menyentuh_http_dan_tanpa_peringatan()
    {
        var handler = new HandlerPalsu(_ => Jawaban(JawabanSah));
        var muatan = await Klien(handler).MuatAsync(KodeDesa, 2024);

        Assert.Equal(StatusMuatIdm.Berhasil, muatan.Status);
        Assert.False(muatan.LewatHttp);
        Assert.DoesNotContain(handler.Permintaan, u => u.Scheme == "http");
    }

    private static IdmKemendesaKlien Klien(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) });

    /// <summary>Galat persis seperti yang dihasilkan .NET untuk sertifikat kedaluwarsa.</summary>
    private static HttpRequestException SertifikatKedaluwarsa() => new(
        "The SSL connection could not be established, see inner exception.",
        new AuthenticationException(
            "The remote certificate is invalid according to the validation procedure."));

    private static HttpResponseMessage Jawaban(string isi) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(isi, Encoding.UTF8, "application/json"),
    };

    /// <summary>Handler buangan yang mencatat setiap URI yang diminta.</summary>
    private sealed class HandlerPalsu : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _jawab;

        public HandlerPalsu(Func<HttpRequestMessage, HttpResponseMessage> jawab) => _jawab = jawab;

        public List<Uri> Permintaan { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Permintaan.Add(request.RequestUri!);
            return Task.FromResult(_jawab(request));
        }
    }
}
