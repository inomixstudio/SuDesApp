using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing
{
    /// <summary>
    /// Urutan permintaan masa uji coba: trial SERVER didahulukan bila kode
    /// aktivasi sudah ada dan server terjangkau; trial lokal hanya cadangan
    /// saat server tidak terjangkau. Server yang menjawab dan MENOLAK tidak
    /// boleh diberi cadangan lokal — kalau diberi, satu desa bisa mengumpulkan
    /// 28 hari dari dua jalur yang seharusnya 14 hari.
    ///
    /// Jaringan dipalsukan lewat <see cref="HttpMessageHandler"/>; tidak ada
    /// koneksi sungguhan.
    /// </summary>
    [Collection("LisensiBerbagiBerkas")]
    public sealed class MulaiPercobaanTests : IDisposable
    {
        private readonly string _folder;

        public MulaiPercobaanTests()
        {
            _folder = Path.Combine(Path.GetTempPath(),
                "sudesapp-percobaan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            TrialLokal.FolderOverride = _folder;
            LisensiManager.FolderCacheOverride = _folder;
        }

        public void Dispose()
        {
            TrialLokal.FolderOverride = null;
            LisensiManager.FolderCacheOverride = null;
            try
            {
                if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
            }
            catch
            {
                // Folder sementara yang gagal dihapus tidak menggagalkan uji.
            }
        }

        private bool TrialLokalTercatat => File.Exists(Path.Combine(_folder, "trial-lokal.json"));

        private static HttpResponseMessage Jawab(int status, string json = "{}") =>
            new((HttpStatusCode)status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private sealed class HandlerPalsu : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _jawab;

            public HandlerPalsu(Func<HttpRequestMessage, HttpResponseMessage> jawab) => _jawab = jawab;

            /// <summary>Berapa permintaan yang benar-benar sampai ke "server".</summary>
            public int Panggilan { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Panggilan++;
                return Task.FromResult(_jawab(request));
            }
        }

        private PenjagaLisensi BuatPenjaga(HandlerPalsu handler, string? kode)
        {
            var lisensi = new LisensiManager(
                NullLogger<LisensiManager>.Instance, new HttpClient(handler))
            {
                KodeAktivasi = kode
            };
            var klien = new LanggananKlien(
                lisensi, new HttpClient(handler), NullLogger<LanggananKlien>.Instance);

            return new PenjagaLisensi(
                lisensi, new TrialLokal(), NullLogger<PenjagaLisensi>.Instance, klien);
        }

        [Fact]
        public async Task TanpaKode_TrialLokal_TanpaMenghubungiServer()
        {
            var handler = new HandlerPalsu(_ => Jawab(500));
            var penjaga = BuatPenjaga(handler, kode: null);

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.Lokal, hasil.Asal);
            Assert.NotNull(hasil.BerlakuSampai);
            Assert.True(TrialLokalTercatat);
            Assert.Equal(0, handler.Panggilan);
            Assert.Equal(KeadaanLisensi.Trial, penjaga.Keadaan);
            Assert.False(penjaga.Terkunci);
        }

        [Fact]
        public async Task ServerMenolak_TidakDiberiCadanganLokal()
        {
            var handler = new HandlerPalsu(_ => Jawab(403));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.DitolakServer, hasil.Asal);
            Assert.True(handler.Panggilan > 0);
            Assert.False(TrialLokalTercatat);
            Assert.True(penjaga.Terkunci);
        }

        [Fact]
        public async Task ServerTakTerjangkau_TrialLokalSebagaiCadangan()
        {
            var handler = new HandlerPalsu(_ => throw new HttpRequestException("offline"));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.Lokal, hasil.Asal);
            Assert.True(TrialLokalTercatat);
            Assert.False(penjaga.Terkunci);
        }

        [Fact]
        public async Task ServerBermasalahSesaat_TrialLokalSebagaiCadangan()
        {
            // 5xx bukan penolakan: desa tidak boleh terkunci karena server rusak.
            var handler = new HandlerPalsu(_ => Jawab(500));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.Lokal, hasil.Asal);
            Assert.True(TrialLokalTercatat);
        }

        [Fact]
        public async Task BalasanServerRusak_TrialLokalSebagaiCadangan()
        {
            // 200 tetapi bukan JSON (mis. halaman galat proxy): server "tidak
            // terjangkau" secara fungsional, jadi cadangan lokal tetap diberikan.
            var handler = new HandlerPalsu(_ => Jawab(200, "<html>bukan json</html>"));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.Lokal, hasil.Asal);
            Assert.True(TrialLokalTercatat);
        }

        [Fact]
        public async Task ServerMenerima_TapiTokenBelumTiba_TidakDiberiTrialLokal()
        {
            var handler = new HandlerPalsu(permintaan =>
                permintaan.RequestUri!.AbsolutePath.Contains("aktifkan-trial", StringComparison.Ordinal)
                    ? Jawab(200, "{\"aktif\":true,\"berlakuSampai\":\"2026-11-07T00:00:00Z\"}")
                    : Jawab(500));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.ServerTertunda, hasil.Asal);
            Assert.False(TrialLokalTercatat);
        }

        [Fact]
        public async Task TrialLokalSudahDipakai_TidakMengulangDanTidakMenghubungiServer()
        {
            Assert.True(new TrialLokal().MulaiSekarang());
            var handler = new HandlerPalsu(_ => Jawab(500));
            var penjaga = BuatPenjaga(handler, "KODE-DESA");

            var hasil = await penjaga.MulaiPercobaanAsync();

            Assert.Equal(AsalPercobaan.SudahPernahTrial, hasil.Asal);
            Assert.Equal(0, handler.Panggilan);
        }

        [Theory]
        [InlineData(400, true)]
        [InlineData(401, true)]
        [InlineData(403, true)]
        [InlineData(409, true)]
        [InlineData(404, false)]
        [InlineData(408, false)]
        [InlineData(429, false)]
        [InlineData(500, false)]
        [InlineData(503, false)]
        [InlineData(0, false)]
        public void PenolakanFinal_HanyaUntukJawabanTegasServer(int statusHttp, bool final)
        {
            Assert.Equal(final, PenjagaLisensi.MenolakFinal(statusHttp));
        }
    }
}
