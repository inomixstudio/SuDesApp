using System.Text.Json;
using SuDesApp.Data.Models;
using SuDesApp.WhatsApp;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Test jalur WhatsApp: permintaan yang disetujui operator diproses
    /// WaSuratProcessor menjadi surat DRAFT bernomor melalui jalur simpan
    /// sentral (SuratSaveService → ISuratInsertion), lalu permintaan ditandai
    /// SELESAI dengan rujukan ID surat.
    /// </summary>
    public class WaSuratProcessorTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public WaSuratProcessorTests(CoreTestFixture fixture) => _fixture = fixture;

        private static string BungkusJson(WaRequestData data) =>
            JsonSerializer.Serialize(data);

        private async Task<PermintaanWa> BuatPermintaanAsync(
            string namaJenis = "SKD_UMUM", bool lengkap = true, bool denganWarga = true)
        {
            var data = new WaRequestData
            {
                NamaJenis = namaJenis,
                Keperluan = "Pengurusan surat online",
                NomorWA = "081234567890",
                Warga = denganWarga ? new WargaData
                {
                    NIK = "3204010101800001",
                    Nama = "Budi Santoso",
                    TempatLahir = "Bandung",
                    TanggalLahir = "1980-01-01",
                    JenisKelamin = "Laki-laki",
                    Agama = "Islam",
                    Pekerjaan = "Petani",
                    StatusPerkawinan = "Belum Kawin",
                    Kewarganegaraan = "WNI",
                    Dusun = "Dusun Uji", Desa = "Desa Uji",
                    Kecamatan = "Kec. Uji", Kabupaten = "Kab. Uji"
                } : null,
                Fields = lengkap
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["keterangan"] = "Permohonan via WhatsApp"
                    }
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            var permintaan = new PermintaanWa
            {
                KodePermintaan = "WA-TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                NomorWA = "081234567890",
                NamaWarga = denganWarga ? "Budi Santoso" : null,
                NIK = denganWarga ? "3204010101800001" : null,
                NamaJenis = namaJenis,
                PesanMentah = "SKD Budi Santoso dsb.",
                DataJson = BungkusJson(data),
                Status = WaRequestStatus.BARU,
                Sumber = WaRequestStatus.SumberWhatsApp
            };

            permintaan.ID_Permintaan = await _fixture.PermintaanWaRepo.InsertAsync(permintaan);
            return permintaan;
        }

        [Fact]
        public async Task PermintaanDisetujui_MenghasilkanSuratDraftBernomor()
        {
            _fixture.SiapkanDataDasar();
            var permintaan = await BuatPermintaanAsync("SKD_UMUM");
            var processor = _fixture.BuatWaProcessor();

            var hasil = await processor.ProsesAsync(permintaan.ID_Permintaan);

            // Surat tercipta, bernomor otomatis sesuai Perbup 74/2020 (kode 470),
            // dan berstatus Draft sesuai konsep jalur WhatsApp.
            Assert.True(hasil.IdSurat > 0);
            Assert.Equal("SKD_UMUM", hasil.NamaJenis);
            Assert.Matches(@"^470/\d{3}/Ds/2026$", hasil.NomorSurat);

            var surat = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(hasil.IdSurat);
            Assert.NotNull(surat);
            Assert.Equal("Draft", surat!.Status);
            Assert.Equal("SKD_UMUM", surat.NamaJenis);
            Assert.Equal("Budi Santoso", surat.Warga?.Nama);

            // Keberadaan baris surat + nomor bisa dicek langsung di register.
            long jumlah = Convert.ToInt64(
                _fixture.Connection.Scalar($"SELECT COUNT(*) FROM Surat WHERE ID_Surat = {hasil.IdSurat}"));
            Assert.Equal(1, jumlah);
        }

        [Fact]
        public async Task PermintaanDiproses_StatusMenjadiSelesaiDenganRujukanSurat()
        {
            _fixture.SiapkanDataDasar();
            var permintaan = await BuatPermintaanAsync("SKD_UMUM");
            var processor = _fixture.BuatWaProcessor();

            await processor.ProsesAsync(permintaan.ID_Permintaan);

            var terkini = await _fixture.PermintaanWaRepo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.NotNull(terkini);
            Assert.Equal(WaRequestStatus.SELESAI, terkini!.Status);
            Assert.True(terkini.IdSurat > 0);
            Assert.Contains("Nomor", terkini.PesanBalasan, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JenisSuratTidakDikenal_MelemparDanPermintaanTetap()
        {
            _fixture.SiapkanDataDasar();
            var permintaan = await BuatPermintaanAsync("JENIS_PALSU");
            var processor = _fixture.BuatWaProcessor();

            await Assert.ThrowsAsync<SuDesApp.Data.Repositories.DataRetrievalException>(
                () => processor.ProsesAsync(permintaan.ID_Permintaan));

            // Permintaan tidak diubah statusnya bila jenis surat tidak dikenal.
            var terkini = await _fixture.PermintaanWaRepo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.NotNull(terkini);
            Assert.Equal(WaRequestStatus.BARU, terkini!.Status);
            Assert.Null(terkini.IdSurat);
        }

        [Fact]
        public async Task WargaTanpaData_MelemparDanTidakAdaSurat()
        {
            _fixture.SiapkanDataDasar();
            // Permintaan tanpa data warga sama sekali (warga null pada DataJson).
            var permintaan = await BuatPermintaanAsync("SKD_UMUM", lengkap: false, denganWarga: false);

            var processor = _fixture.BuatWaProcessor();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ProsesAsync(permintaan.ID_Permintaan));
        }
    }
}
