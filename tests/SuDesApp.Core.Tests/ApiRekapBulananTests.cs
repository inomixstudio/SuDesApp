using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Pemantauan alur persetujuan dari sistem luar lewat API desa: hitungan
    /// status persetujuan di <c>/statistik</c>, status per surat di
    /// <c>/verifikasi/{kode}</c>, dan rekap laporan bulanan
    /// <c>/rekap-bulanan</c>. Seluruh isi respons angka agregat — tidak ada
    /// nomor surat maupun data warga.
    ///
    /// Tanggal surat uji memakai tahun lampau (2019–2021) karena validator
    /// menolak tanggal masa depan; tahun yang berbeda per tes membuat hitungan
    /// tidak bergantung pada urutan eksekusi dalam kelas ini.
    /// </summary>
    public class ApiRekapBulananTests : IClassFixture<CoreTestFixture>
    {
        private static readonly DateTime Sekarang = new(2026, 9, 27, 10, 30, 0);

        private readonly CoreTestFixture _fixture;
        private readonly ApiRingkasanService _ringkasan;

        public ApiRekapBulananTests(CoreTestFixture fixture)
        {
            _fixture = fixture;

            _ringkasan = new ApiRingkasanService(
                fixture.UnitOfWork.WargaRepository,
                new PerangkatDesaService(
                    new PerangkatDesaRepository(
                        fixture.Connection, NullLogger<PerangkatDesaRepository>.Instance),
                    logger: null,
                    sekarang: () => Sekarang),
                fixture.UnitOfWork.SuratRepository,
                fixture.PermintaanWaRepo,
                logger: null,
                sekarang: () => Sekarang);
        }

        /// <summary>
        /// Simpan satu surat aktif pada tanggal tertentu; bila
        /// <paramref name="statusPersetujuan"/> diisi, alurnya diset lewat
        /// jalur penulisan resmi repository.
        /// </summary>
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

        // ==================== rute ====================

        [Fact]
        public void RuteRekapBulanan_Terdekat_TetapWajibKunciApi()
        {
            var cocok = ApiRute.Cocok("GET", "/sudes/api/v1/rekap-bulanan");

            Assert.True(cocok.Ditemukan);
            Assert.Equal("Rekap laporan bulanan", cocok.Endpoint!.Nama);
            Assert.Equal(ApiKategori.Agregat, cocok.Endpoint.Kategori);

            // Rekap memuat angka arsip desa — bukan endpoint publik seperti
            // verifikasi surat, jadi tetap butuh kunci API.
            Assert.False(cocok.Endpoint.TanpaKunci);
        }

        // ==================== rekap bulanan ====================

        [Fact]
        public async Task RekapBulanan_12BulanPenuh_DanRincianPerStatus()
        {
            // Dua surat Juli 2019: satu lewat alur, satu tanpa alur.
            await SimpanSuratAsync(new DateTime(2019, 7, 15), StatusPersetujuanSurat.Diverifikasi);
            await SimpanSuratAsync(new DateTime(2019, 7, 20));

            var rekap = await _ringkasan.AmbilRekapBulananAsync(2019);

            Assert.Equal(2019, rekap.Tahun);
            Assert.Equal(Sekarang, rekap.DibuatPada);

            // Selalu 12 baris Januari–Desember supaya dashboard sistem luar
            // bisa membaca tren tanpa menyusun bulan yang kosong sendiri.
            Assert.Equal(12, rekap.Bulan.Count);
            Assert.Equal("2019-01", rekap.Bulan[0].Bulan);
            Assert.Equal("2019-12", rekap.Bulan[11].Bulan);
            Assert.All(rekap.Bulan, b => Assert.Equal(5, b.PerStatusPersetujuan.Count));

            var juli = rekap.Bulan.Single(b => b.Bulan == "2019-07");
            Assert.Equal(2, juli.JumlahSurat);
            Assert.Equal(1, juli.PerStatusPersetujuan[StatusPersetujuanSurat.Diverifikasi]);
            Assert.Equal(1, juli.PerStatusPersetujuan[StatusPersetujuanSurat.TanpaAlur]);

            Assert.All(rekap.Bulan.Where(b => b.Bulan != "2019-07"),
                b => Assert.Equal(0, b.JumlahSurat));

            Assert.Equal(2, rekap.JumlahSuratTotal);
            Assert.Equal(1, rekap.SuratPerStatusPersetujuan[StatusPersetujuanSurat.Diverifikasi]);
            Assert.Equal(1, rekap.SuratPerStatusPersetujuan[StatusPersetujuanSurat.TanpaAlur]);
            Assert.Equal(0, rekap.SuratPerStatusPersetujuan[StatusPersetujuanSurat.Ditolak]);
        }

        [Fact]
        public async Task RekapBulanan_TanpaTahun_MemakaiTahunBerjalan()
        {
            var rekap = await _ringkasan.AmbilRekapBulananAsync();

            Assert.Equal(2026, rekap.Tahun);
            Assert.Equal(12, rekap.Bulan.Count);
            Assert.Equal(Sekarang, rekap.DibuatPada);
        }

        [Theory]
        [InlineData(1800)]
        [InlineData(2200)]
        public async Task RekapBulanan_TahunDiLuarRentang_Ditolak(int tahun)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => _ringkasan.AmbilRekapBulananAsync(tahun));
        }

        // ==================== hitungan per status ====================

        [Fact]
        public async Task HitungPerStatusPersetujuan_MengikutiAlur_TanpaAlurUntukSuratBiasa()
        {
            var repo = _fixture.UnitOfWork.SuratRepository;
            var awal = await repo.CountPerStatusPersetujuanAsync();

            // Surat baru tanpa alur terhitung sebagai TANPA_ALUR.
            int id = await SimpanSuratAsync(new DateTime(2020, 2, 10));
            var setelah = await repo.CountPerStatusPersetujuanAsync();
            Assert.Equal(
                awal.GetValueOrDefault(StatusPersetujuanSurat.TanpaAlur) + 1,
                setelah[StatusPersetujuanSurat.TanpaAlur]);

            // Diajukan: pindah dari TANPA_ALUR ke DIAJUKAN — angka total tidak
            // berubah, hanya sebarannya.
            var surat = await repo.GetByIdAsync(id);
            surat!.StatusPersetujuan = StatusPersetujuanSurat.Diajukan;
            Assert.True(await repo.SimpanPersetujuanAsync(surat));

            var diajukan = await repo.CountPerStatusPersetujuanAsync();
            Assert.Equal(
                awal.GetValueOrDefault(StatusPersetujuanSurat.Diajukan) + 1,
                diajukan[StatusPersetujuanSurat.Diajukan]);
            Assert.Equal(
                awal.GetValueOrDefault(StatusPersetujuanSurat.TanpaAlur),
                diajukan[StatusPersetujuanSurat.TanpaAlur]);
        }

        [Fact]
        public async Task Ringkasan_MembawaSuratPerStatusPersetujuan_KunciSelaluPenuh()
        {
            await SimpanSuratAsync(new DateTime(2021, 3, 5), StatusPersetujuanSurat.Diajukan);

            var ringkasan = await _ringkasan.AmbilAsync();

            // Kunci selalu ada walau nol, supaya sistem luar cukup membaca
            // nilai tanpa berurusan dengan kunci yang muncul-hilang.
            Assert.All(StatusPersetujuanSurat.Semua,
                s => Assert.Contains(s, ringkasan.SuratPerStatusPersetujuan.Keys));
            Assert.Contains(StatusPersetujuanSurat.TanpaAlur,
                ringkasan.SuratPerStatusPersetujuan.Keys);

            Assert.True(ringkasan.SuratPerStatusPersetujuan[StatusPersetujuanSurat.Diajukan] >= 1);
        }

        // ==================== status per surat (verifikasi) ====================

        [Fact]
        public void Verifikasi_HasilMembawaStatusPersetujuanTernormalisasi()
        {
            var dalamAlur = _fixture.BuatSuratLengkap();
            dalamAlur.StatusPersetujuan = " diverifikasi ";
            var hasil = HasilVerifikasiSurat.Dari(dalamAlur, "SD-AAAA-BBBB", hashCocok: true, cuplikanCocok: true);

            Assert.Equal(StatusPersetujuanSurat.Diverifikasi, hasil.StatusPersetujuan);

            // Surat yang tidak melewati alur ikut terbawa dengan kunci tetap —
            // jawaban selalu memuat statusPersetujuan tanpa perlu null-check.
            var tanpaAlur = _fixture.BuatSuratLengkap();
            var hasilBiasa = HasilVerifikasiSurat.Dari(tanpaAlur, "SD-CCCC-DDDD", hashCocok: true, cuplikanCocok: true);
            Assert.Equal(StatusPersetujuanSurat.TanpaAlur, hasilBiasa.StatusPersetujuan);
        }

        [Fact]
        public void Verifikasi_JsonResponsMemuatFieldStatusPersetujuan()
        {
            var surat = _fixture.BuatSuratLengkap();
            surat.StatusPersetujuan = StatusPersetujuanSurat.Terbit;
            var hasil = HasilVerifikasiSurat.Dari(surat, "SD-EEEE-FFFF", hashCocok: true, cuplikanCocok: true);

            // Kebijakan JSON listener: camelCase + buang null — field baru
            // harus ikut terbawa dengan nama yang bisa dibaca sistem luar.
            var json = JsonSerializer.Serialize(hasil, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            Assert.Contains("\"statusPersetujuan\":\"TERBIT\"", json, StringComparison.Ordinal);
        }
    }
}
