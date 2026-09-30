using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji kode verifikasi surat: bentuk kode, normalisasi masukan, cap dokumen,
    /// dan integrasi dengan jalur simpan surat yang sesungguhnya (kode dibuat
    /// otomatis saat surat terbit, tidak pernah diganti setelahnya).
    /// </summary>
    public class KodeVerifikasiSuratTests
    {
        /// <summary>Crockford base32 tanpa I, L, O, U.</summary>
        private static readonly Regex PolaKode = new(@"^SD-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$");

        [Fact]
        public void BuatKode_FormatnyaBaku()
        {
            for (int i = 0; i < 50; i++)
            {
                var kode = KodeVerifikasiSurat.BuatKode();
                Assert.Matches(PolaKode, kode);
            }
        }

        [Fact]
        public void BuatKode_SeribuKali_TidakAdaYangSama()
        {
            var semua = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 1000; i++)
            {
                Assert.True(semua.Add(KodeVerifikasiSurat.BuatKode()), "Kode verifikasi terulang.");
            }
        }

        [Theory]
        [InlineData("sd-7k3m-9qx2", "SD-7K3M-9QX2")]
        [InlineData("SD7K3M9QX2", "SD-7K3M-9QX2")]
        [InlineData("  7k3m9qx2 ", "SD-7K3M-9QX2")]
        [InlineData("SUDES|SD-7K3M-9QX2|A1B2C3D4E5", "SD-7K3M-9QX2")]
        [InlineData("SUDES|SD-7K3M-9QX2", "SD-7K3M-9QX2")]
        public void Normalisasi_MenerimaBerbagaiPenulisan(string masukan, string harapan)
        {
            Assert.Equal(harapan, KodeVerifikasiSurat.Normalisasi(masukan));
        }

        [Theory]
        [InlineData("")]
        [InlineData("SD-123")]
        [InlineData("bukan-kode-sama-sekali")]
        [InlineData("SD-IIII-IIII")] // I bukan bagian alfabet (mudah tertukar dengan 1)
        [InlineData("SD-LLLL-LLLL")] // L tidak dipakai
        [InlineData("SD-OOOO-OOOO")] // O tidak dipakai
        [InlineData("SD-UUUU-UUUU")] // U tidak dipakai
        public void Normalisasi_MasukanTidakSah_MengembalikanKosong(string masukan)
        {
            Assert.Equal(string.Empty, KodeVerifikasiSurat.Normalisasi(masukan));
        }

        [Fact]
        public void UraiMasukan_MengambilHashDariPayloadQr()
        {
            var (kode, hash) = KodeVerifikasiSurat.UraiMasukan("SUDES|SD-7K3M-9QX2|A1B2C3D4E5");

            Assert.Equal("SD-7K3M-9QX2", kode);
            Assert.Equal("A1B2C3D4E5", hash);
        }

        [Fact]
        public void HitungHash_StabilUntukDataSama()
        {
            var surat = SuratUji();
            Assert.Equal(KodeVerifikasiSurat.HitungHash(surat), KodeVerifikasiSurat.HitungHash(surat));
        }

        [Fact]
        public void HitungHash_BerubahSaatDataIntiBerubah()
        {
            var asli = SuratUji();
            var hashAsli = KodeVerifikasiSurat.HitungHash(asli);

            var tanggalBerubah = SuratUji();
            tanggalBerubah.TanggalSurat = tanggalBerubah.TanggalSurat.AddDays(1);
            Assert.NotEqual(hashAsli, KodeVerifikasiSurat.HitungHash(tanggalBerubah));

            var namaBerubah = SuratUji();
            namaBerubah.Warga!.Nama = "Nama Lain";
            Assert.NotEqual(hashAsli, KodeVerifikasiSurat.HitungHash(namaBerubah));

            var nomorBerubah = SuratUji();
            nomorBerubah.NomorSurat = "470/999/Ds/2026";
            Assert.NotEqual(hashAsli, KodeVerifikasiSurat.HitungHash(nomorBerubah));
        }

        [Fact]
        public void BuatPayload_MemuatKodeDanCuplikanHash()
        {
            var payload = KodeVerifikasiSurat.BuatPayload("SD-7K3M-9QX2", "A1B2C3D4E5F60718");

            Assert.Equal("SUDES|SD-7K3M-9QX2|A1B2C3D4E5", payload);
        }

        [Fact]
        public void SamarkanNama_HanyaMenyisakanNamaDepanDanInisial()
        {
            Assert.Equal("BUDI S.", ApiListener.SamarkanNama("Budi Santoso"));
            Assert.Equal("BUDI", ApiListener.SamarkanNama("Budi"));
            Assert.Equal("SITI A. R.", ApiListener.SamarkanNama("siti aminah rahayu"));
            Assert.Null(ApiListener.SamarkanNama("   "));
        }

        [Fact]
        public void RuteVerifikasi_TerdaftarDanTanpaKunci()
        {
            var cocok = ApiRute.Cocok("GET", "/sudes/api/v1/verifikasi/sd-7k3m-9qx2");

            Assert.True(cocok.Ditemukan);
            Assert.Equal("/verifikasi/{kode}", cocok.Endpoint!.Jalur);
            Assert.True(cocok.Endpoint.TanpaKunci);
            Assert.Equal("sd-7k3m-9qx2", cocok.NilaiParameter);
        }

        [Fact]
        public void RutePermintaan_TetapWajibKunci()
        {
            var endpoint = ApiRute.Semua.Single(e => e.Jalur == "/permintaan/{kode}");
            Assert.False(endpoint.TanpaKunci);
        }

        private static SuratData SuratUji() => new()
        {
            ID_Surat = 7,
            ID_Jenis = 3,
            NamaJenis = "SKTM",
            NomorSurat = "470/012/Ds/2026",
            TanggalSurat = new DateTime(2026, 9, 25),
            Warga = new WargaData { NIK = "3204010101800001", Nama = "Budi Santoso" }
        };
    }

    /// <summary>
    /// Uji integrasi: kode verifikasi harus benar-benar dibuat saat surat
    /// disimpan lewat jalur yang dipakai aplikasi, dan pemeriksaannya harus
    /// bekerja di skema SQLite asli.
    /// </summary>
    public class VerifikasiSuratIntegrasiTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public VerifikasiSuratIntegrasiTests(CoreTestFixture fixture) => _fixture = fixture;

        private IVerifikasiSuratService Service() =>
            new VerifikasiSuratService(_fixture.UnitOfWork.SuratRepository);

        private async Task<SuratData> SimpanSuratAsync(string status = "Active")
        {
            _fixture.SiapkanDataDasar();
            var surat = _fixture.BuatSuratLengkap();
            surat.Status = status;

            int id = await ((ISuratInsertion)_fixture.UnitOfWork.SuratRepository).InsertSuratAsync(surat);
            Assert.True(id > 0);

            var tersimpan = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(id);
            return tersimpan!;
        }

        [Fact]
        public async Task SuratTerbit_LangsungPunyaKodeDanCap()
        {
            var surat = await SimpanSuratAsync();

            Assert.False(string.IsNullOrWhiteSpace(surat.KodeVerifikasi));
            Assert.False(string.IsNullOrWhiteSpace(surat.HashVerifikasi));
            Assert.Equal(KodeVerifikasiSurat.HitungHash(surat), surat.HashVerifikasi);
        }

        [Fact]
        public async Task Verifikasi_KodeSendiri_DinyatakanSah()
        {
            var surat = await SimpanSuratAsync();

            var hasil = await Service().VerifikasiAsync(surat.KodeVerifikasi!);

            Assert.True(hasil.Ditemukan);
            Assert.True(hasil.Sah);
            Assert.False(hasil.DataBerubah);
            Assert.Equal(surat.NomorSurat, hasil.NomorSurat);
            Assert.Equal("NTCR_N1", hasil.NamaJenis);
            Assert.Equal("Budi Santoso", hasil.NamaPemohon);
        }

        [Fact]
        public async Task Verifikasi_IsiQrUtuh_DinyatakanSah()
        {
            var surat = await SimpanSuratAsync();
            var payload = KodeVerifikasiSurat.BuatPayload(surat.KodeVerifikasi!, surat.HashVerifikasi);

            var hasil = await Service().VerifikasiAsync(payload);

            Assert.True(hasil.Sah);
        }

        [Fact]
        public async Task Verifikasi_KodeTidakAda_TidakDitemukan()
        {
            await SimpanSuratAsync();

            var hasil = await Service().VerifikasiAsync("SD-0000-0000");

            Assert.False(hasil.Ditemukan);
            Assert.False(hasil.Sah);
        }

        [Fact]
        public async Task Verifikasi_SetelahIsiBerubah_TerdeteksiTidakCocok()
        {
            var surat = await SimpanSuratAsync();

            // Operator mengubah nama pemohon setelah surat terbit: kode & cap
            // lama tetap tersimpan (surat yang sudah dicetak tidak dibatalkan),
            // sehingga pemeriksaan berikutnya harus melaporkan ketidakcocokan.
            surat.Warga!.Nama = "Budi Santoso Diubah";
            Assert.True(await _fixture.UnitOfWork.SuratRepository.UpdateAsync(surat));

            var hasil = await Service().VerifikasiAsync(surat.KodeVerifikasi!);

            Assert.True(hasil.Ditemukan);
            Assert.False(hasil.Sah);
            Assert.True(hasil.DataBerubah);
        }

        [Fact]
        public async Task SuratDraft_TidakPunyaKode_SampaiMenjadiTerbit()
        {
            var draft = await SimpanSuratAsync("Draft");
            Assert.True(string.IsNullOrWhiteSpace(draft.KodeVerifikasi));

            draft.Status = "Active";
            Assert.True(await _fixture.UnitOfWork.SuratRepository.UpdateAsync(draft));

            var sesudah = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(draft.ID_Surat);
            Assert.False(string.IsNullOrWhiteSpace(sesudah!.KodeVerifikasi));
        }

        [Fact]
        public async Task KodeYangSudahAda_TidakPernahDigantiSaatSuratDiedit()
        {
            var surat = await SimpanSuratAsync();
            var kodeAsli = surat.KodeVerifikasi;

            surat.Keterangan = "diubah setelah terbit";
            Assert.True(await _fixture.UnitOfWork.SuratRepository.UpdateAsync(surat));

            var sesudah = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(surat.ID_Surat);
            Assert.Equal(kodeAsli, sesudah!.KodeVerifikasi);
            Assert.Equal(surat.HashVerifikasi, sesudah.HashVerifikasi);
        }

        [Fact]
        public async Task PencarianKode_MenerimaPenulisanHurufKecilTanpaTandaHubung()
        {
            var surat = await SimpanSuratAsync();
            var longgar = surat.KodeVerifikasi!.Replace("-", string.Empty).ToLowerInvariant();

            var ditemukan = await _fixture.UnitOfWork.SuratRepository.GetByKodeVerifikasiAsync(longgar);

            Assert.NotNull(ditemukan);
            Assert.Equal(surat.ID_Surat, ditemukan!.ID_Surat);
        }

        [Fact]
        public async Task SimpanVerifikasi_KodeKosong_Ditolak()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => _fixture.UnitOfWork.SuratRepository.SimpanVerifikasiAsync(1, "  ", "hash"));
        }
    }
}
