using SuDesApp.Api;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Uji ujung-ke-ujung endpoint sesi dan pengaturan lewat HTTP sungguhan
    /// (HttpListener di localhost). Yang dijaga: login menolak sandi salah,
    /// pengaturan menolak permintaan tanpa token, dan token yang sudah keluar
    /// langsung tidak berlaku.
    /// </summary>
    /// <remarks>
    /// Satu koleksi dengan <see cref="ApiSambungPonselTests"/>: keduanya
    /// mengalihkan lokasi kunci API dan preferensi yang bersifat statis proses,
    /// jadi tidak boleh berjalan bersamaan.
    /// </remarks>
    [Collection("ApiListener")]
    public sealed class ApiListenerSesiTests : IDisposable
    {
        private readonly ApiUjiLingkungan _lingkungan = new();

        public void Dispose() => _lingkungan.Dispose();

        [Fact]
        public async Task Login_berhasil_menghasilkan_token_dan_membuka_pengaturan()
        {
            var token = await _lingkungan.MasukAsync();

            using var jawaban = await _lingkungan.DenganTokenAsync(
                HttpMethod.Get, "/sudes/api/v1/pengaturan", token);

            Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);

            using var dokumen = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            var desa = dokumen.RootElement.GetProperty("data").GetProperty("desa");

            Assert.Equal("Sukamaju", desa.GetProperty("namaDesa").GetString());
            Assert.Equal("Cikarang", desa.GetProperty("kecamatan").GetString());
            Assert.Equal("Bekasi", desa.GetProperty("kabupaten").GetString());
            Assert.Equal("H. Ahmad Fauzi", desa.GetProperty("kepalaDesa").GetString());
        }

        [Fact]
        public async Task Identitas_sesi_membalas_akun_yang_sedang_masuk()
        {
            var token = await _lingkungan.MasukAsync();

            using var jawaban = await _lingkungan.DenganTokenAsync(
                HttpMethod.Get, "/sudes/api/v1/sesi/siapa", token);

            Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);

            using var dokumen = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            var data = dokumen.RootElement.GetProperty("data");

            Assert.Equal(ApiUjiLingkungan.Username, data.GetProperty("username").GetString());
            Assert.Equal("Budi Santoso", data.GetProperty("nama").GetString());
            Assert.Equal("Administrator", data.GetProperty("peran").GetString());
            Assert.True(data.GetProperty("berlakuSampai").GetDateTime() > DateTime.Now);
        }

        [Fact]
        public async Task Pengaturan_tanpa_token_ditolak_401()
        {
            using var permintaan = new HttpRequestMessage(
                HttpMethod.Get, "/sudes/api/v1/pengaturan");

            using var jawaban = await _lingkungan.Klien.SendAsync(permintaan);

            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
            using var dokumen = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            Assert.Equal("sesi_diperlukan", dokumen.RootElement.GetProperty("kode").GetString());
        }

        [Fact]
        public async Task Login_sandi_salah_ditolak_dengan_kode_sesi_gagal()
        {
            using var jawaban = await _lingkungan.Klien.PostAsJsonAsync(
                "/sudes/api/v1/sesi",
                new { username = "budi", kata_sandi = "salah" },
                ApiUjiLingkungan.OpsiUji);

            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
            using var dokumen = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            Assert.Equal("sesi_gagal", dokumen.RootElement.GetProperty("kode").GetString());
        }

        [Fact]
        public async Task Endpoint_sesi_tanpa_kunci_api_ditolak()
        {
            using var tanpaKunci = new HttpClient
            {
                BaseAddress = _lingkungan.Klien.BaseAddress,
                Timeout = TimeSpan.FromSeconds(15)
            };

            using var jawaban = await tanpaKunci.PostAsJsonAsync(
                "/sudes/api/v1/sesi",
                new { username = "budi", kata_sandi = "rahasia123" },
                ApiUjiLingkungan.OpsiUji);

            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
        }

        [Fact]
        public async Task Keluar_membuat_token_tidak_berlaku_lagi()
        {
            var token = await _lingkungan.MasukAsync();

            using (var jawaban = await _lingkungan.DenganTokenAsync(
                HttpMethod.Delete, "/sudes/api/v1/sesi", token))
            {
                Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);
            }

            using var ulang = await _lingkungan.DenganTokenAsync(
                HttpMethod.Get, "/sudes/api/v1/pengaturan", token);

            Assert.Equal(HttpStatusCode.Unauthorized, ulang.StatusCode);
        }
    }
}
