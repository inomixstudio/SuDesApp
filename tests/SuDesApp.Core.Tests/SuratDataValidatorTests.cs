using SuDesApp.Data.Models;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Test SuratDataValidator via SuratSaveService (pemilik keputusan draft/aktif):
    /// surat NTCR lengkap diterima sebagai Aktif; isian warga/NTCR yang kurang
    /// memaksa status Draft pada mode Draft dan ditolak pada mode Aktif.
    /// Nomor surat diisi otomatis lebih dulu — persis seperti alur UI
    /// (InputWindowViewModel.FillNomorSuratAsync) yang berlaku sebelum validasi.
    /// </summary>
    public class SuratDataValidatorTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public SuratDataValidatorTests(CoreTestFixture fixture) => _fixture = fixture;

        private async Task<HasilSimpanSurat> Simpan(SuratData surat, ModeSimpan mode)
        {
            return await _fixture.SaveService.SimpanBaruAsync(surat, mode, s => Task.CompletedTask);
        }

        /// <summary>Paritas alur UI: nomor dibuat sistem SEBELUM validasi mode aktif.</summary>
        private async Task IsiNomorOtomatis(SuratData surat)
        {
            var jenis = await _fixture.UnitOfWork.JenisSuratRepository.GetJenisSuratByNamaAsync(surat.NamaJenis);
            if (jenis?.KodeJenis != null)
                surat.NomorSurat = await _fixture.UnitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(jenis.KodeJenis);
        }

        [Fact]
        public async Task SuratLengkap_ModeAktif_TersimpanSebagaiAktif()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            await IsiNomorOtomatis(surat);

            var hasil = await Simpan(surat, ModeSimpan.Aktif);

            Assert.True(hasil.ID_Surat > 0);
            Assert.Equal("Active", hasil.Status);
            Assert.True(hasil.Lengkap);
            Assert.False(string.IsNullOrWhiteSpace(hasil.NomorSurat));
        }

        [Fact]
        public async Task TanpaCalonIstri_ModeAktif_Ditolak()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            surat.Ntcr = new NtcrData();   // data calon istri kosong total

            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
                () => Simpan(surat, ModeSimpan.Aktif));
        }

        [Fact]
        public async Task IsianSebagian_ModeDraft_TersimpanSebagaiDraft()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            surat.Ntcr = new NtcrData();   // belum lengkap — wajar untuk draft

            var hasil = await Simpan(surat, ModeSimpan.Draft);

            Assert.Equal("Draft", hasil.Status);
            Assert.NotEmpty(hasil.Kekurangan);
        }

        [Fact]
        public async Task WargaTanpaNama_ModeAktif_Ditolak()
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            surat.Warga = new WargaData { NIK = "3204010101800001" };   // Nama kosong

            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
                () => Simpan(surat, ModeSimpan.Aktif));
        }
    }
}
