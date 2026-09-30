using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji lapisan API desa yang tidak memerlukan soket: kunci API,
    /// autentikasi, pembatas laju, tabel rute, penyusunan statistik, dan
    /// penerimaan permintaan surat.
    ///
    /// Kunci API ditulis ke berkas sementara (LokasiOverride) supaya berkas
    /// preferensi user tidak tersentuh, dan database memakai SQLite in-memory
    /// milik fixture.
    ///
    /// Koleksi "API" dipakai bersama ApiListenerEndToEndTests: kedua kelas
    /// sama-sama menulis ApiKunci.LokasiOverride dan AppPreferenceStore.LokasiOverride
    /// (statis, proses satu), jadi tidak boleh berjalan berbarengan.
    /// </summary>
    [Collection("API")]
    public sealed class ApiServiceTests : IDisposable, IClassFixture<CoreTestFixture>
    {
        private static readonly DateTime Sekarang = new(2026, 9, 27, 10, 30, 0);

        private readonly CoreTestFixture _fixture;
        private readonly PermintaanWaRepository _permintaanWa;
        private readonly PerangkatDesaService _perangkatSvc;
        private readonly ApiRingkasanService _ringkasan;
        private readonly ApiPermintaanService _layananPermintaan;
        private readonly string _jalurKunci;

        public ApiServiceTests(CoreTestFixture fixture)
        {
            _fixture = fixture;
            _permintaanWa = fixture.PermintaanWaRepo;

            _perangkatSvc = new PerangkatDesaService(
                new PerangkatDesaRepository(
                    fixture.Connection, NullLoggerPerangkat()),
                logger: null,
                sekarang: () => Sekarang);

            _ringkasan = new ApiRingkasanService(
                fixture.UnitOfWork.WargaRepository,
                _perangkatSvc,
                fixture.UnitOfWork.SuratRepository,
                _permintaanWa,
                logger: null,
                sekarang: () => Sekarang);

            _layananPermintaan = new ApiPermintaanService(
                _permintaanWa, engine: null, audit: null, logger: null, sekarang: () => Sekarang);

            _jalurKunci = Path.Combine(Path.GetTempPath(),
                "uji-apikunci-" + Guid.NewGuid().ToString("N") + ".bin");
            ApiKunci.LokasiOverride = () => _jalurKunci;
        }

        private static Microsoft.Extensions.Logging.ILogger<PerangkatDesaRepository> NullLoggerPerangkat()
            => Microsoft.Extensions.Logging.Abstractions.NullLogger<PerangkatDesaRepository>.Instance;

        public void Dispose()
        {
            ApiKunci.LokasiOverride = null;
            try { File.Delete(_jalurKunci); } catch { /* biar OS membersihkan */ }
        }

        // ==================== kunci API ====================

        [Fact]
        public void KunciAcak_PanjangTetap_DanAmanDipakaiDiHeader()
        {
            var kunci = ApiKunci.BuatNilaiAcak();

            // 32 byte menjadi 43 karakter base64 tanpa padding.
            Assert.Equal(43, kunci.Length);
            Assert.DoesNotContain('+', kunci);
            Assert.DoesNotContain('/', kunci);
            Assert.DoesNotContain('=', kunci);
        }

        [Fact]
        public void KunciAcak_TidakBerulang()
        {
            var banyak = Enumerable.Range(0, 25)
                .Select(_ => ApiKunci.BuatNilaiAcak())
                .ToList();

            Assert.Equal(banyak.Count, banyak.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void SimpanDanMuat_KunciTersimpanTerenkripsi()
        {
            Assert.False(ApiKunci.Ada);

            var kunci = ApiKunci.BuatBaru();
            Assert.True(ApiKunci.Ada);
            Assert.Equal(kunci, ApiKunci.Muat());

            // Isi berkas tidak boleh memuat kunci polos (harus terenkripsi DPAPI).
            var isiBerkas = File.ReadAllText(_jalurKunci, System.Text.Encoding.UTF8);
            Assert.DoesNotContain(kunci, isiBerkas);
        }

        [Fact]
        public void Muat_BerkasRusak_TidakMelempar_TetapiNull()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jalurKunci)!);
            File.WriteAllBytes(_jalurKunci, new byte[] { 9, 9, 9, 9 });

            Assert.Null(ApiKunci.Muat());
        }

        [Fact]
        public void Simpan_KunciKosong_Ditolak()
        {
            Assert.Throws<ArgumentException>(() => ApiKunci.Simpan("   "));
        }

        // ==================== autentikasi ====================

        [Fact]
        public void Diizinkan_KunciCocok_Lolos()
        {
            Assert.True(ApiAutentikasi.Diizinkan("abc123", "abc123"));
            Assert.True(ApiAutentikasi.Diizinkan("  abc123  ", "abc123"));
        }

        [Fact]
        public void Diizinkan_KunciSalah_atauKosong_Ditolak()
        {
            Assert.False(ApiAutentikasi.Diizinkan("abc123", "abc999"));
            Assert.False(ApiAutentikasi.Diizinkan(null, "abc123"));
            Assert.False(ApiAutentikasi.Diizinkan("   ", "abc123"));
        }

        [Fact]
        public void Diizinkan_TanpaKunciTersimpan_SemuaDitolak()
        {
            // API aktif tanpa kunci tersimpan harus menolak semua permintaan,
            // bukan menerima kunci kosong.
            Assert.False(ApiAutentikasi.Diizinkan(null, null));
            Assert.False(ApiAutentikasi.Diizinkan("apa saja", null));
        }

        [Fact]
        public void AmbilKunci_HeaderKunciDiutamakan()
        {
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Key"] = "dari-header",
                ["Authorization"] = "Bearer dari-bearer"
            };

            Assert.Equal("dari-header", ApiAutentikasi.AmbilKunci(nama => Ambil(header, nama)));
        }

        [Fact]
        public void AmbilKunci_BearerTokenDipakaiBilaHeaderKunciTidakAda()
        {
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Bearer dari-bearer"
            };

            Assert.Equal("dari-bearer", ApiAutentikasi.AmbilKunci(nama => Ambil(header, nama)));
        }

        [Fact]
        public void AmbilKunci_TanpaHeader_TidakBolehNull()
        {
            var kosong = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Assert.Null(ApiAutentikasi.AmbilKunci(nama => Ambil(kosong, nama)));
        }

        [Fact]
        public void PerbandinganTetap_PanjangBerbeda_TidakSama()
        {
            // Kunci dengan panjang berbeda harus ditolak, bukan dibandingkan parsial.
            Assert.False(ApiAutentikasi.PerbandinganTetap("abc", "abcdef"));
            Assert.True(ApiAutentikasi.PerbandinganTetap("abc", "abc"));
        }

        private static string? Ambil(IDictionary<string, string> header, string nama)
            => header.TryGetValue(nama, out var nilai) ? nilai : null;

        // ==================== pembatas laju ====================

        [Fact]
        public void Pembatas_SampaiBatas_LaluDitolak()
        {
            var waktu = Sekarang;
            var pembatas = new ApiPembatas(3, TimeSpan.FromMinutes(1), () => waktu);

            Assert.True(pembatas.Izinkan("127.0.0.1"));
            Assert.True(pembatas.Izinkan("127.0.0.1"));
            Assert.True(pembatas.Izinkan("127.0.0.1"));
            Assert.False(pembatas.Izinkan("127.0.0.1"));

            // Klien lain tetap dilayani: pembatas per alamat, bukan global.
            Assert.True(pembatas.Izinkan("10.0.0.5"));
        }

        [Fact]
        public void Pembatas_JendelaBerlalu_MengizinkanLagi()
        {
            var waktu = Sekarang;
            var pembatas = new ApiPembatas(2, TimeSpan.FromMinutes(1), () => waktu);

            Assert.True(pembatas.Izinkan("klien"));
            Assert.True(pembatas.Izinkan("klien"));
            Assert.False(pembatas.Izinkan("klien"));

            waktu = Sekarang.AddMinutes(1).AddSeconds(1);
            Assert.True(pembatas.Izinkan("klien"));
        }

        [Fact]
        public void Pembatas_TungguBerikutnya_PositifSaatSudahPenuh()
        {
            var waktu = Sekarang;
            var pembatas = new ApiPembatas(1, TimeSpan.FromMinutes(1), () => waktu);

            Assert.Equal(TimeSpan.Zero, pembatas.TungguBerikutnya("klien"));
            Assert.True(pembatas.Izinkan("klien"));
            Assert.False(pembatas.Izinkan("klien"));

            Assert.True(pembatas.TungguBerikutnya("klien") > TimeSpan.Zero);
        }

        [Fact]
        public void Pembatas_BatasTidakSah_Ditolak()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ApiPembatas(0, TimeSpan.FromMinutes(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ApiPembatas(5, TimeSpan.Zero));
        }

        // ==================== tabel rute ====================

        [Fact]
        public void Rute_Status_Lolos()
        {
            var cocok = ApiRute.Cocok("GET", "/sudes/api/v1/status");

            Assert.True(cocok.Ditemukan);
            Assert.Equal("Status layanan", cocok.Endpoint!.Nama);
            Assert.Null(cocok.NilaiParameter);
        }

        [Theory]
        [InlineData("/sudes/api/v1/status/")]
        [InlineData("/SUDES/API/V1/STATUS")]
        [InlineData("  /sudes/api/v1/status  ")]
        public void Rute_GarisMiringAkhirDanHurufBesar_TetapLolos(string jalur)
        {
            Assert.True(ApiRute.Cocok("GET", jalur).Ditemukan);
        }

        [Fact]
        public void Rute_MetodeSalah_DitandaiAgarDijawab405()
        {
            var cocok = ApiRute.Cocok("POST", "/sudes/api/v1/status");

            Assert.False(cocok.Ditemukan);
            Assert.True(cocok.MetodeTidakDiizinkan);
            Assert.Equal("GET", cocok.Endpoint!.Metode);
        }

        [Fact]
        public void Rute_JalurTidakDikenal_TidakDitemukan()
        {
            var cocok = ApiRute.Cocok("GET", "/sudes/api/v1/data-warga");

            Assert.False(cocok.Ditemukan);
            Assert.False(cocok.MetodeTidakDiizinkan);
        }

        [Fact]
        public void Rute_ParameterKode_AmbilNilai()
        {
            var cocok = ApiRute.Cocok("GET", "/sudes/api/v1/permintaan/PMT-2026-0007");

            Assert.True(cocok.Ditemukan);
            Assert.Equal("Status permintaan", cocok.Endpoint!.Nama);
            Assert.Equal("PMT-2026-0007", cocok.NilaiParameter);
        }

        [Fact]
        public void Rute_DaftarEndpoint_UnikDanBerprefiks()
        {
            Assert.NotEmpty(ApiRute.Semua);

            foreach (var endpoint in ApiRute.Semua)
            {
                Assert.StartsWith(ApiRute.Prefix, endpoint.JalurPenuh);
                Assert.False(string.IsNullOrWhiteSpace(endpoint.Nama));
                Assert.False(string.IsNullOrWhiteSpace(endpoint.Keterangan));
            }

            // Tidak boleh ada dua endpoint dengan metode + jalur sama.
            var kembar = ApiRute.Semua
                .GroupBy(e => e.Metode + " " + e.Jalur, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            Assert.Empty(kembar);
        }

        [Fact]
        public void Rute_TidakAdaEndpointYangMembocorkanDataPribadiWarga()
        {
            // Penjaga kebijakan: tidak ada endpoint yang mengeluarkan data
            // pribadi warga secara langsung.
            var endpointPribadi = ApiRute.Semua
                .Where(e => e.Jalur.Contains("warga", StringComparison.OrdinalIgnoreCase)
                         || e.Jalur.Contains("penduduk", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(endpointPribadi);
        }

        // ==================== pengaturan (murni) ====================

        [Theory]
        [InlineData(80, false)]
        [InlineData(1024, false)]
        [InlineData(1025, true)]
        [InlineData(8790, true)]
        [InlineData(65535, true)]
        [InlineData(65536, false)]
        public void Port_Sah_KalauBeradaDiRentangDitentukanBenar(int port, bool harusSah)
        {
            Assert.Equal(harusSah, ApiPengaturan.NormalisasiPort(port, out _));
        }

        [Fact]
        public void AlamatDasar_BawaanMemakaiLocalhost()
        {
            Assert.Equal($"http://localhost:{ApiPengaturan.PortBawaan}/",
                ApiPengaturan.AlamatDasar(ApiPengaturan.PortBawaan));
        }

        [Fact]
        public void AlamatPrefix_MenyertakanPrefixEndpoint()
        {
            var prefix = ApiPengaturan.AlamatPrefix(ApiPengaturan.PortBawaan);

            Assert.EndsWith(ApiRute.Prefix, prefix);
            Assert.DoesNotContain("//sudes", prefix);
        }

        // ==================== permintaan dari API ====================

        [Fact]
        public async Task Buat_JenisLengkap_MenyimpanPermintaanBaru()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                NamaWarga = "Budi Santoso",
                Nik = "3204010101800001",
                NomorHp = "081234567890",
                JenisSurat = "SKTM"
            });

            Assert.True(hasil.Berhasil);
            Assert.False(hasil.SudahAda);
            Assert.StartsWith("PMT-2026-", hasil.Kode);

            var tersimpan = await _permintaanWa.GetByKodeAsync(hasil.Kode!);
            Assert.NotNull(tersimpan);
            Assert.Equal("SKTM", tersimpan!.NamaJenis);
            Assert.Equal("Budi Santoso", tersimpan.NamaWarga);
            Assert.Equal("3204010101800001", tersimpan.NIK);
            Assert.Equal("081234567890", tersimpan.NomorWA);
            Assert.Equal(WaRequestStatus.SumberApi, tersimpan.Sumber);
            Assert.Equal(WaRequestStatus.BARU, tersimpan.Status);
            Assert.False(tersimpan.IsRead);
        }

        [Fact]
        public async Task Buat_JenisTidakDikenal_Ditolak()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                NamaWarga = "Tanpa Jenis",
                JenisSurat = "SURAT AJAIB"
            });

            Assert.False(hasil.Berhasil);
            Assert.Contains("jenis_surat", hasil.Pesan, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Buat_TanpaJenisDanTanpaPesan_Ditolak()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                NamaWarga = "Tanpa Apa Pun"
            });

            Assert.False(hasil.Berhasil);
        }

        [Fact]
        public async Task Buat_DariPesanGratis_TebekJenisDanNIK()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                Pesan = "SKTM\nNama: Sari Wijaya\nNIK: 3273011505950003\nKeperluan:_beasiswa"
            });

            Assert.True(hasil.Berhasil);

            var tersimpan = await _permintaanWa.GetByKodeAsync(hasil.Kode!);
            Assert.NotNull(tersimpan);
            Assert.Equal("Sari Wijaya", tersimpan!.NamaWarga);
            Assert.Equal("3273011505950003", tersimpan.NIK);
            Assert.False(string.IsNullOrWhiteSpace(tersimpan.DataJson));
        }

        [Fact]
        public async Task Buat_ReferensiSama_TidakMenggandakanPermintaan()
        {
            var payload = new ApiPermintaanMasuk
            {
                Referensi = "WEB-2026-0001",
                NamaWarga = "Budi Santoso",
                JenisSurat = "SKTM"
            };

            var pertama = await _layananPermintaan.BuatAsync(payload);
            var kedua = await _layananPermintaan.BuatAsync(payload);

            Assert.True(pertama.Berhasil);
            Assert.True(kedua.Berhasil);
            Assert.True(kedua.SudahAda);
            Assert.Equal(pertama.Kode, kedua.Kode);

            var semua = await _permintaanWa.GetAllAsync(WaRequestStatus.BARU);
            Assert.Single(semua, p => p.Referensi == "WEB-2026-0001");
        }

        [Fact]
        public async Task Buat_TanpaReferensi_TetapBoleh_KarenaPengirimTidakWajib()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                NamaWarga = "Tanpa Referensi",
                JenisSurat = "SKTM"
            });

            Assert.True(hasil.Berhasil);
        }

        [Fact]
        public async Task AmbilStatus_MenampilkanStatusDanSumberApi()
        {
            var dibuat = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                Referensi = "WEB-STATUS-1",
                NamaWarga = "Status Tes",
                JenisSurat = "SKTM"
            });

            var status = await _layananPermintaan.AmbilStatusAsync(dibuat.Kode!);

            Assert.NotNull(status);
            Assert.Equal(dibuat.Kode, status!.Kode);
            Assert.Equal(WaRequestStatus.BARU, status.Status);
            Assert.Equal("API", status.Sumber);
            Assert.Equal("SKTM", status.JenisSurat);
            Assert.False(status.AdaSurat);

            // Status tidak boleh membocorkan NIK/nama warga ke pemanggil luar.
            var json = JsonSerializer.Serialize(status);
            Assert.DoesNotContain("Status Tes", json);
        }

        [Fact]
        public async Task AmbilStatus_KodeTidakDikenal_MengembalikanNull()
        {
            Assert.Null(await _layananPermintaan.AmbilStatusAsync("PMT-1999-9999"));
            Assert.Null(await _layananPermintaan.AmbilStatusAsync("   "));
        }

        [Fact]
        public async Task StatusSetelahDisetujui_TerdeteksiSuratTerhubung()
        {
            var dibuat = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                Referensi = "WEB-SURAT-1",
                NamaWarga = "Terhubung",
                JenisSurat = "SKTM"
            });

            var baris = await _permintaanWa.GetByKodeAsync(dibuat.Kode!);
            await _permintaanWa.UpdateStatusAsync(
                baris!.ID_Permintaan, WaRequestStatus.SELESAI, "Sudah jadi", 123, null);

            var status = await _layananPermintaan.AmbilStatusAsync(dibuat.Kode!);
            Assert.Equal(WaRequestStatus.SELESAI, status!.Status);
            Assert.True(status.AdaSurat);
            Assert.Equal("Sudah jadi", status.Catatan);
        }

        [Theory]
        [InlineData("SKTM", "SKTM")]
        [InlineData("sktm", "SKTM")]
        [InlineData("SKTM (Sudah Terbit)", null)]
        public void TentukanJenisSurat_MengenaliNamaBaku(string diberikan, string? harapan)
        {
            Assert.Equal(harapan, _layananPermintaan.TentukanJenisSurat(diberikan, null));
        }

        [Fact]
        public void TentukanJenisSurat_DariPesanSaatJenisKosong()
        {
            Assert.Equal("SKTM",
                _layananPermintaan.TentukanJenisSurat(null, "SKTM\nNama: Budi"));
        }

        [Fact]
        public void TampilanSumber_MengenaliApi_LainnyaDanGoogleSheet()
        {
            Assert.Equal("API", WaRequestStatus.TampilanSumber(WaRequestStatus.SumberApi));
            Assert.Equal("Google Sheet", WaRequestStatus.TampilanSumber(WaRequestStatus.SumberGoogleSheet));
            Assert.Equal("WhatsApp", WaRequestStatus.TampilanSumber(WaRequestStatus.SumberWhatsApp));
            Assert.Equal("Lainnya", WaRequestStatus.TampilanSumber("APZ"));
        }

        [Fact]
        public void PermintaanMasuk_JsonSnakeCase_Terbaca()
        {
            const string json = """
            {
              "referensi": "WEB-JSON-1",
              "nama_warga": "Dari Json",
              "nik": "3204010101800009",
              "no_hp": "081200000001",
              "jenis_surat": "SKTM",
              "pesan": "SKTM",
              "data": { "keterluan": "Uji" }
            }
            """;

            var masukan = JsonSerializer.Deserialize<ApiPermintaanMasuk>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(masukan);
            Assert.Equal("WEB-JSON-1", masukan!.Referensi);
            Assert.Equal("Dari Json", masukan.NamaWarga);
            Assert.Equal("3204010101800009", masukan.Nik);
            Assert.Equal("081200000001", masukan.NomorHp);
            Assert.Equal("SKTM", masukan.JenisSurat);
            Assert.Equal("Uji", masukan.Data!["keterluan"]);
        }

        [Fact]
        public async Task Nik_BukanAngka_DibuangDigitnya_Saja()
        {
            var hasil = await _layananPermintaan.BuatAsync(new ApiPermintaanMasuk
            {
                Referensi = "WEB-NIK-1",
                NamaWarga = "Nik Berdesimal",
                Nik = "NIK: 3204010101800001",
                JenisSurat = "SKTM"
            });

            Assert.True(hasil.Berhasil);
        }

        /// <summary>
        /// Tabel perangkat dipakai bersama seluruh kelas test ini, jadi tiap test
        /// yang butuh angka pasti mulai dari tabel kosong. Tanpa ini hasil test
        /// bergantung pada urutan eksekusi test.
        /// </summary>
        private void KosongkanPerangkat()
            => _fixture.Connection.ExecuteNonQuery("DELETE FROM PerangkatDesa;");

        // ==================== statistik agregat ====================

        [Fact]
        public async Task Ringkasan_MenampilkanAngkaPerangkatDanPermintaan()
        {
            KosongkanPerangkat();

            _fixture.Connection.ExecuteNonQuery(
                @"INSERT INTO PerangkatDesa (Nama, Jabatan, Dusun, Status, CreatedAt, UpdatedAt, DiperbaruiOleh)
                  VALUES ('Asep', 'KEPALA DESA', 'Dusun I', 'AKTIF', '2026-01-01', '2026-01-01', 'Operator')");
            _fixture.Connection.ExecuteNonQuery(
                @"INSERT INTO PerangkatDesa (Nama, Jabatan, Dusun, Status, CreatedAt, UpdatedAt, DiperbaruiOleh)
                  VALUES ('Slamet', 'KETUA RT', 'RT 01', 'SELESAI', '2026-01-01', '2026-01-01', 'Operator')");

            var ringkasan = await _ringkasan.AmbilAsync();

            Assert.Equal(Sekarang, ringkasan.DibuatPada);
            Assert.Equal(2, ringkasan.PerangkatTotal);
            Assert.Equal(1, ringkasan.PerangkatAktif);
            Assert.Equal(1, ringkasan.PerangkatSelesai);

            // Angka per status permintaan harus terisi walau semua nol.
            Assert.NotEmpty(ringkasan.PermintaanPerStatus);
            Assert.All(WaRequestStatus.All,
                status => Assert.True(ringkasan.PermintaanPerStatus.ContainsKey(status)));
        }

        [Fact]
        public async Task Perangkat_HanyaAktif_TidakMencakupYangSelesai()
        {
            KosongkanPerangkat();

            _fixture.Connection.ExecuteNonQuery(
                @"INSERT INTO PerangkatDesa (Nama, Jabatan, Dusun, Status, CreatedAt, UpdatedAt, DiperbaruiOleh)
                  VALUES ('Asep Aktif', 'KEPALA DESA', 'Dusun I', 'AKTIF', '2026-01-01', '2026-01-01', 'Operator')");
            _fixture.Connection.ExecuteNonQuery(
                @"INSERT INTO PerangkatDesa (Nama, Jabatan, Dusun, Status, CreatedAt, UpdatedAt, DiperbaruiOleh)
                  VALUES ('Slamet Selesai', 'KETUA RT', 'RT 01', 'SELESAI', '2026-01-01', '2026-01-01', 'Operator')");

            var aktif = await _ringkasan.AmbilPerangkatAsync(hanyaAktif: true);
            var semua = await _ringkasan.AmbilPerangkatAsync(hanyaAktif: false);

            Assert.Contains(aktif, p => p.Nama == "Asep Aktif");
            Assert.DoesNotContain(aktif, p => p.Nama == "Slamet Selesai");
            Assert.Contains(semua, p => p.Nama == "Slamet Selesai");

            // NIP/NIK/alamat pejabat tidak boleh ikut keluar lewat API.
            var json = JsonSerializer.Serialize(aktif);
            Assert.DoesNotContain("Nip", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Alamat", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PerangkatRingkas_MemuatJabatanDanWilayahSiapTampil()
        {
            KosongkanPerangkat();

            _fixture.Connection.ExecuteNonQuery(
                @"INSERT INTO PerangkatDesa (Nama, Jabatan, Dusun, RT, RW, Status, MasaJabatanMulai, MasaJabatanSelesai, CreatedAt, UpdatedAt, DiperbaruiOleh)
                  VALUES ('Asep Ringkas', 'kepala desa', 'Dusun Cibogo', '01', '02', 'AKTIF', '2021-05-10', '2026-05-09', '2026-01-01', '2026-01-01', 'Operator')");

            var daftar = await _ringkasan.AmbilPerangkatAsync();
            var kades = daftar.FirstOrDefault(p => p.Nama == "Asep Ringkas");

            Assert.NotNull(kades);
            Assert.Equal("KEPALA DESA", kades!.Jabatan);
            Assert.Equal("PERANGKAT DESA", kades.Kelompok);
            Assert.Equal("AKTIF", kades.Status);
            Assert.Contains("Dusun Cibogo", kades.Wilayah);
            Assert.Equal(new DateTime(2021, 5, 10), kades.MasaJabatanMulai);
        }

        [Fact]
        public void JenisSuratOnline_TidakKosong()
        {
            var jenis = _ringkasan.AmbilJenisSuratOnline();

            Assert.NotEmpty(jenis);
            Assert.All(jenis, nama => Assert.False(string.IsNullOrWhiteSpace(nama)));
        }
    }
}
