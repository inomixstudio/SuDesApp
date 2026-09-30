using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji ujung-ke-ujung listener API desa lewat HTTP sungguhan — satu-satunya
    /// lapisan yang menyambungkan tabel rute, autentikasi kunci API, pembatas
    /// laju, dan handler endpoint. Fokus: pemantauan persetujuan dari sistem
    /// luar lewat <c>/rekap-bulanan</c> (termasuk validasi parameter tahun),
    /// <c>/statistik</c>, dan penolakan tanpa kunci.
    ///
    /// Kunci API dan preferensi (apiAktif, port, dsj.) dialihkan ke berkas
    /// sementara lewat LokasiOverride supaya profil user tidak pernah disentuh.
    /// Kelas ini berbagi koleksi "API" dengan ApiServiceTests karena keduanya
    /// menulis override statis yang sama — tidak boleh berjalan berbarengan.
    /// </summary>
    [Collection("API")]
    public sealed class ApiListenerEndToEndTests : IDisposable, IClassFixture<CoreTestFixture>
    {
        private static readonly DateTime Sekarang = new(2026, 9, 27, 10, 30, 0);

        private readonly CoreTestFixture _fixture;
        private readonly string _jalurKunci;
        private readonly string _jalurPreferensi;
        private readonly ServiceProvider _penyedia;
        private readonly ApiListener _listener;
        private readonly HttpClient _klien;
        private readonly string _kunci;
        private readonly string _dasar;

        public ApiListenerEndToEndTests(CoreTestFixture fixture)
        {
            _fixture = fixture;

            // 1) Berkas sementara untuk kunci API + preferensi. Listener harus
            //    yakin "API aktif, kunci ada, port sah" tanpa membaca/menulis
            //    berkas asli di profil user.
            _jalurKunci = Path.Combine(Path.GetTempPath(),
                "uji-listener-kunci-" + Guid.NewGuid().ToString("N") + ".bin");
            _jalurPreferensi = Path.Combine(Path.GetTempPath(),
                "uji-listener-prefs-" + Guid.NewGuid().ToString("N") + ".json");
            ApiKunci.LokasiOverride = () => _jalurKunci;
            AppPreferenceStore.LokasiOverride = () => _jalurPreferensi;

            _kunci = ApiKunci.BuatBaru();
            ApiPengaturan.SetAktif(true);
            ApiPengaturan.SetPort(PortBebas());
            ApiPengaturan.SetIzinkanJaringan(false);
            ApiPengaturan.SetCatatAktivitas(false); // tanpa menulis Riwayat Aktivitas
            ApiPengaturan.SetBatasPermintaan(600);  // batas laju longgar untuk uji

            // 2) Penyedia layanan mini: listener hanya meminta
            //    IApiRingkasanService untuk /statistik dan /rekap-bulanan.
            var layanan = new ServiceCollection();
            layanan.AddScoped<IApiRingkasanService>(_ => new ApiRingkasanService(
                fixture.UnitOfWork.WargaRepository,
                new PerangkatDesaService(
                    new PerangkatDesaRepository(
                        fixture.Connection, NullLogger<PerangkatDesaRepository>.Instance),
                    logger: null,
                    sekarang: () => Sekarang),
                fixture.UnitOfWork.SuratRepository,
                fixture.PermintaanWaRepo,
                logger: null,
                sekarang: () => Sekarang));
            _penyedia = layanan.BuildServiceProvider();

            // 3) Listener sungguhan di port bebas; kegagalan start ditampilkan
            //    apa adanya lewat GalatTerakhir (port dipakai, hak akses, dsj.).
            _listener = new ApiListener(() => _penyedia, NullLogger<ApiListener>.Instance);
            Assert.True(_listener.Mulai(),
                _listener.GalatTerakhir ?? "Listener API tidak bisa dinyalakan.");

            _dasar = _listener.Alamat;

            // Proxy sistem dimatikan supaya panggilan ke localhost tidak pernah
            // terarah ke proxy yang bisa menggagalkan uji di mesin tertentu.
            _klien = new HttpClient(new HttpClientHandler
            {
                UseProxy = false,
                UseCookies = false
            })
            { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>Port TCP bebas diambil sesaat sebelum dipakai listener.</summary>
        private static int PortBebas()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            try
            {
                return ((IPEndPoint)tcp.LocalEndpoint).Port;
            }
            finally
            {
                tcp.Stop();
            }
        }

        /// <summary>Simpan satu surat aktif pada tanggal tertentu (opsional lewat alur persetujuan).</summary>
        private async Task<int> SimpanSuratAsync(DateTime tanggal, string? statusPersetujuan = null)
        {
            _fixture.SiapkanDataDasar();
            _fixture.SiapkanCacheDesa();

            var surat = _fixture.BuatSuratLengkap();
            surat.Status = "Active";
            surat.TanggalSurat = tanggal;

            var repo = _fixture.UnitOfWork.SuratRepository;
            int id = await ((ISuratInsertion)repo).InsertSuratAsync(surat);
            Assert.True(id > 0);

            if (statusPersetujuan != null)
            {
                var tersimpan = await repo.GetByIdAsync(id);
                tersimpan!.StatusPersetujuan = statusPersetujuan;
                Assert.True(await repo.SimpanPersetujuanAsync(tersimpan));
            }

            return id;
        }

        private async Task<HttpResponseMessage> GETAsync(string jalur, bool pakaiKunci = true)
        {
            var permintaan = new HttpRequestMessage(HttpMethod.Get, _dasar + jalur);
            if (pakaiKunci) permintaan.Headers.Add("X-Api-Key", _kunci);
            return await _klien.SendAsync(permintaan);
        }

        private static JsonElement DataDari(HttpResponseMessage jawaban, string isi)
        {
            using var akar = JsonDocument.Parse(isi);
            Assert.True(akar.RootElement.GetProperty("sukses").GetBoolean());
            return akar.RootElement.GetProperty("data").Clone();
        }

        // ==================== /rekap-bulanan ====================

        [Fact]
        public async Task RekapBulanan_LewatHttp_Membawa12BulanDariDatabase()
        {
            // Alur penuh: SQL → ApiRingkasanService → listener → JSON.
            await SimpanSuratAsync(new DateTime(2019, 7, 15), StatusPersetujuanSurat.Diverifikasi);

            var jawaban = await GETAsync("/rekap-bulanan?tahun=2019");
            Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);

            var data = DataDari(jawaban, await jawaban.Content.ReadAsStringAsync());
            Assert.Equal(2019, data.GetProperty("tahun").GetInt32());
            Assert.Equal(1, data.GetProperty("jumlahSuratTotal").GetInt32());

            var bulan = data.GetProperty("bulan");
            Assert.Equal(12, bulan.GetArrayLength());
            Assert.Equal("2019-01", bulan[0].GetProperty("bulan").GetString());
            Assert.Equal("2019-12", bulan[11].GetProperty("bulan").GetString());

            var juli = bulan[6];
            Assert.Equal("2019-07", juli.GetProperty("bulan").GetString());
            Assert.Equal(1, juli.GetProperty("jumlahSurat").GetInt32());
            Assert.Equal(1,
                juli.GetProperty("perStatusPersetujuan")
                    .GetProperty(StatusPersetujuanSurat.Diverifikasi).GetInt32());
        }

        [Fact]
        public async Task RekapBulanan_TanpaParameter_MemakaiTahunBerjalan()
        {
            var jawaban = await GETAsync("/rekap-bulanan");
            Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);

            var data = DataDari(jawaban, await jawaban.Content.ReadAsStringAsync());
            Assert.Equal(2026, data.GetProperty("tahun").GetInt32());
            Assert.Equal(12, data.GetProperty("bulan").GetArrayLength());
        }

        [Theory]
        [InlineData("?tahun=abc")]
        [InlineData("?tahun=1800")]
        [InlineData("?tahun=2200")]
        public async Task RekapBulanan_TahunTidakSah_Dibalas400(string query)
        {
            var jawaban = await GETAsync("/rekap-bulanan" + query);
            Assert.Equal(HttpStatusCode.BadRequest, jawaban.StatusCode);

            using var akar = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            Assert.False(akar.RootElement.GetProperty("sukses").GetBoolean());
            Assert.Equal("permintaan_tidak_sah",
                akar.RootElement.GetProperty("kode").GetString());
        }

        // ==================== autentikasi ====================

        [Fact]
        public async Task TanpaKunciApi_Ditolak401()
        {
            var jawaban = await GETAsync("/rekap-bulanan", pakaiKunci: false);

            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
            using var akar = JsonDocument.Parse(await jawaban.Content.ReadAsStringAsync());
            Assert.False(akar.RootElement.GetProperty("sukses").GetBoolean());
            Assert.Equal("api_key_salah", akar.RootElement.GetProperty("kode").GetString());
        }

        [Fact]
        public async Task KunciSalah_Ditolak401()
        {
            var permintaan = new HttpRequestMessage(HttpMethod.Get, _dasar + "/rekap-bulanan");
            permintaan.Headers.Add("X-Api-Key", ApiKunci.BuatNilaiAcak());

            var jawaban = await _klien.SendAsync(permintaan);
            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
        }

        // ==================== /statistik ====================

        [Fact]
        public async Task Statistik_MembawaSuratPerStatusPersetujuanLewatHttp()
        {
            await SimpanSuratAsync(new DateTime(2018, 5, 5), StatusPersetujuanSurat.Diajukan);

            var jawaban = await GETAsync("/statistik");
            Assert.Equal(HttpStatusCode.OK, jawaban.StatusCode);

            var data = DataDari(jawaban, await jawaban.Content.ReadAsStringAsync());
            var perPersetujuan = data.GetProperty("suratPerStatusPersetujuan");

            // Kunci selalu penuh — sistem luar cukup membaca nilai.
            foreach (var kunci in new[]
            {
                StatusPersetujuanSurat.Diajukan,
                StatusPersetujuanSurat.Diverifikasi,
                StatusPersetujuanSurat.Terbit,
                StatusPersetujuanSurat.Ditolak,
                StatusPersetujuanSurat.TanpaAlur
            })
            {
                Assert.True(perPersetujuan.GetProperty(kunci).GetInt32() >= 0, kunci);
            }

            Assert.True(
                perPersetujuan.GetProperty(StatusPersetujuanSurat.Diajukan).GetInt32() >= 1);
        }

        public void Dispose()
        {
            _klien.Dispose();
            _listener.Dispose();
            _penyedia.Dispose();

            ApiKunci.LokasiOverride = null;
            AppPreferenceStore.LokasiOverride = null;
            try { File.Delete(_jalurKunci); } catch { /* biar OS membersihkan */ }
            try { File.Delete(_jalurPreferensi); } catch { /* biar OS membersihkan */ }
        }
    }
}
