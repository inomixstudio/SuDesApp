using System;
using System.Linq;
using System.Threading.Tasks;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji integrasi jalur register: SQL pengambilan nomor terbit yang dipakai
    /// pemeriksaan tutup buku harus benar-benar jalan di skema SQLite asli.
    /// Test unit memakai data buatan, jadi nama kolom dan tabelnya perlu dijaga
    /// di sini juga.
    ///
    /// Nomor tidak diketik manual — semuanya dari generator, karena itu justru
    /// yang sedang dibuktikan bekerja. Tahun yang dipakai adalah tahun berjalan,
    /// jadi nomor yang terbit dan tanggal surat selalu satu tahun yang sama.
    /// </summary>
    public class VerifikasiPenomoranIntegrasiTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public VerifikasiPenomoranIntegrasiTests(CoreTestFixture fixture) => _fixture = fixture;

        private VerifikasiPenomoranService Service() =>
            new(_fixture.UnitOfWork.JenisSuratRepository);

        private async Task<SuratData> TerbitkanAsync()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            int id = await ((ISuratInsertion)_fixture.UnitOfWork.SuratRepository).InsertSuratAsync(surat);
            Assert.True(id > 0);
            return surat;
        }

        [Fact]
        public async Task GetNomorTerbit_MengembalikanBaris_LengkapDenganNamaJenis()
        {
            int tahun = DateTime.Now.Year;
            var surat = await TerbitkanAsync();

            var baris = await _fixture.UnitOfWork.JenisSuratRepository.GetNomorTerbitAsync(tahun);

            Assert.Contains(baris, b => b.NomorSurat == surat.NomorSurat);
            Assert.All(baris.Where(b => b.NomorSurat == surat.NomorSurat), b =>
            {
                Assert.True(b.ID_Surat > 0);
                Assert.False(string.IsNullOrWhiteSpace(b.TanggalSurat));
                Assert.False(string.IsNullOrWhiteSpace(b.NamaJenis));
            });
        }

        [Fact]
        public async Task GetNomorTerbit_HanyaMengambilTahunYangDiminta()
        {
            var surat = await TerbitkanAsync();
            Assert.EndsWith($"/Ds/{DateTime.Now.Year}", surat.NomorSurat);

            var baris = await _fixture.UnitOfWork.JenisSuratRepository.GetNomorTerbitAsync(2001);

            Assert.DoesNotContain(baris, b => b.NomorSurat == surat.NomorSurat);
        }

        [Fact]
        public async Task GetNomorTerbit_TanpaTahun_MengambilSemuaTahun()
        {
            await TerbitkanAsync();

            var semua = await _fixture.UnitOfWork.JenisSuratRepository.GetNomorTerbitAsync();
            var tahunIni = await _fixture.UnitOfWork.JenisSuratRepository
                .GetNomorTerbitAsync(DateTime.Now.Year);

            Assert.True(semua.Count >= tahunIni.Count);
        }

        [Fact]
        public async Task Periksa_DariRegisterAsli_TidakBlocking_SaatRapi()
        {
            await TerbitkanAsync();
            await TerbitkanAsync();
            await TerbitkanAsync();

            var hasil = await Service().PeriksaAsync(DateTime.Now.Year);

            Assert.NotEmpty(hasil.Deret);
            Assert.False(hasil.AdaMasalahBlocking, hasil.Ringkasan);
            Assert.Empty(hasil.NomorTahunTidakCocok);

            // Deret yang terbit lewat generator selalu mulai dari 001 dan
            // berurutan tanpa celah.
            Assert.All(hasil.Deret, d =>
            {
                Assert.True(d.MulaiDariSatu, d.Ringkasan);
                Assert.False(d.AdaNomorHilang, d.Ringkasan);
                Assert.Equal(d.Jumlah, d.NomorTerakhir - d.NomorPertama + 1);
            });
        }

        [Fact]
        public async Task Periksa_DeretTerpisah_SatuDeretPerKodeJenis()
        {
            await TerbitkanAsync();
            await TerbitkanAsync();

            var hasil = await Service().PeriksaAsync(DateTime.Now.Year);

            // Fixture memakai NTCR_N1 (format 474.3) — satu kode jenis, satu deret.
            DeretNomor deret = Assert.Single(hasil.Deret);
            Assert.StartsWith("474.3", deret.Awalan);
            Assert.Contains("NTCR", deret.NamaJenis.First());
        }

        [Fact]
        public async Task Periksa_TahunKosong_TidakThrowDanTidakBlocking()
        {
            var hasil = await Service().PeriksaAsync(2001);

            Assert.Empty(hasil.Deret);
            Assert.False(hasil.AdaMasalahBlocking);
        }

        [Fact]
        public async Task Periksa_TahunTidakWajar_Ditolak()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().PeriksaAsync(99));
        }

        [Fact]
        public async Task AmbilTahunTersedia_MencakupTahunBerjalan()
        {
            await TerbitkanAsync();

            var tahunTersedia = await Service().AmbilTahunTersediaAsync();

            Assert.Contains(DateTime.Now.Year, tahunTersedia);
            Assert.Equal(tahunTersedia.OrderByDescending(t => t), tahunTersedia);
        }
    }
}
