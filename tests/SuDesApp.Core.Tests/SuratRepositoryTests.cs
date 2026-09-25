using SuDesApp.Data.Models;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Test SuratRepository di SQLite in-memory: penomoran otomatis dari format
    /// jenis surat (string.Format: {0}=nomor urut, {2}=tahun), penolakan nomor
    /// dobel, nomor berurutan tanpa duplikat, dan round-trip data rincian NTCR.
    /// </summary>
    public class SuratRepositoryTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public SuratRepositoryTests(CoreTestFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task InsertTanpaNomor_NomorDibuatOtomatis_FormatBenar()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();

            int id = await _fixture.UnitOfWork.SuratRepository.AddSuratAsync(surat);

            Assert.True(id > 0);
            // Format jenis di JSON: 474.3/{0:D3}/Ds/{2:yyyy} — nomor urut berlanjut
            // antar test dalam kelas (fixture per-kelas), jadi hanya format yang dijaga.
            Assert.Matches(@"^474\.3/\d{3}/Ds/2026$", surat.NomorSurat);
        }

        [Fact]
        public async Task NomorDobel_Ditolak()
        {
            _fixture.SiapkanDataDasar();
            var pertama = _fixture.BuatSuratLengkap();
            await _fixture.UnitOfWork.SuratRepository.AddSuratAsync(pertama);

            var kedua = _fixture.BuatSuratLengkap();
            kedua.NomorSurat = pertama.NomorSurat;   // paksa duplikat

            await Assert.ThrowsAnyAsync<Exception>(
                () => _fixture.UnitOfWork.SuratRepository.AddSuratAsync(kedua));
        }

        [Fact]
        public async Task NomorBerurut_TanpaDuplikat()
        {
            _fixture.SiapkanDataDasar();
            var nomor = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var surat = _fixture.BuatSuratLengkap();
                await _fixture.UnitOfWork.SuratRepository.AddSuratAsync(surat);
                nomor.Add(surat.NomorSurat!);
            }

            Assert.Equal(3, nomor.Distinct().Count());
        }

        [Fact]
        public async Task DetailNtcr_TersimpanDanTerbacaKembali()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            int id = await _fixture.UnitOfWork.SuratRepository.AddSuratAsync(surat);

            var terbaca = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(id);

            Assert.NotNull(terbaca);
            Assert.NotNull(terbaca!.Ntcr);
            Assert.Equal("Siti Aminah", terbaca.Ntcr!.NamaIstri);
            Assert.Equal("Budi Santoso", terbaca.Warga?.Nama);
        }
    }
}
