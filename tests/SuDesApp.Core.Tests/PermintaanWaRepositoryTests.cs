using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji penyimpanan permintaan WhatsApp di level repositori — terutama
    /// <c>UpdateAsync</c>, yang harus menulis DataJson juga dan bukan hanya saat
    /// INSERT, supaya perubahan data terstruktur (hasil parsing atau suntingan
    /// operator) tidak hilang diam-diam.
    /// Semua memakai SQLite in-memory; berkas database pengguna tidak disentuh.
    /// </summary>
    public sealed class PermintaanWaRepositoryTests : IDisposable
    {
        private const string DataJsonAwal = "{\"NamaJenis\":\"SKD_UMUM\",\"Keperluan\":\"Awal\"}";

        private readonly SqliteConnection _connection;
        private readonly PermintaanWaRepository _repo;

        public PermintaanWaRepositoryTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _repo = new PermintaanWaRepository(
                _connection, NullLogger<PermintaanWaRepository>.Instance);
        }

        public void Dispose() => _connection.Dispose();

        /// <summary>Tabel dibuat lewat repositori sendiri; CREATE TABLE IF NOT EXISTS jadi aman diulang.</summary>
        private Task SiapkanTabelAsync() => _repo.InitializeTableAsync();

        private async Task<PermintaanWa> BuatPermintaanAsync(string? dataJson = DataJsonAwal)
        {
            var permintaan = new PermintaanWa
            {
                KodePermintaan = "PMT-TEST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                NomorWA = "081234567890",
                NamaWarga = "Budi Santoso",
                NamaJenis = "SKD_UMUM",
                PesanMentah = "SKD Budi Santoso",
                DataJson = dataJson,
                Status = WaRequestStatus.BARU,
                Sumber = WaRequestStatus.SumberWhatsApp
            };

            permintaan.ID_Permintaan = await _repo.InsertAsync(permintaan);
            return permintaan;
        }

        [Fact]
        public async Task UpdateAsync_MenyimpanPerubahanDataJson()
        {
            await SiapkanTabelAsync();
            var permintaan = await BuatPermintaanAsync();

            // Pola pemakaian nyata: entitas dimuat, dilengkapi, lalu disimpan.
            var dimuat = await _repo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.NotNull(dimuat);
            Assert.Equal(DataJsonAwal, dimuat!.DataJson);

            const string dataJsonBaru = "{\"NamaJenis\":\"SKD_UMUM\",\"Keperluan\":\"Dilengkapi operator\"}";
            dimuat.DataJson = dataJsonBaru;
            dimuat.Status = WaRequestStatus.DIPROSES;
            dimuat.Catatan = "Dilengkapi operator";
            dimuat.IsRead = true;

            Assert.True(await _repo.UpdateAsync(dimuat));

            var terkini = await _repo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.NotNull(terkini);
            Assert.Equal(dataJsonBaru, terkini!.DataJson);
            Assert.Equal(WaRequestStatus.DIPROSES, terkini.Status);
            Assert.Equal("Dilengkapi operator", terkini.Catatan);
            Assert.True(terkini.IsRead);

            // Kolom identitas tetap tidak ikut ditulis UpdateAsync, walau nilainya diubah
            // di entitas — supaya pemanggil tidak tanpa sengaja menimpa identitas baris.
            dimuat.NomorWA = "080000000000";
            dimuat.KodePermintaan = "PMT-DIABAIKAN";
            Assert.True(await _repo.UpdateAsync(dimuat));

            var setelahnya = await _repo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.Equal("081234567890", setelahnya!.NomorWA);
            Assert.Equal(permintaan.KodePermintaan, setelahnya.KodePermintaan);
        }

        [Fact]
        public async Task UpdateAsync_DataJsonNull_MengosongkanKolom()
        {
            await SiapkanTabelAsync();
            var permintaan = await BuatPermintaanAsync();

            // Nilai null menimpa kolom (sama seperti Catatan/PesanBalasan) — bukan
            // "pertahankan nilai lama" — sehingga pemanggil yang hanya ingin mengubah
            // sebagian kolom wajib memuat entitas dulu lewat GetByIdAsync.
            var dimuat = await _repo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.NotNull(dimuat);
            dimuat!.DataJson = null;

            Assert.True(await _repo.UpdateAsync(dimuat));

            var terkini = await _repo.GetByIdAsync(permintaan.ID_Permintaan);
            Assert.Null(terkini!.DataJson);
        }

        [Fact]
        public async Task UpdateAsync_IdTidakDikenal_MengembalikanFalse()
        {
            await SiapkanTabelAsync();

            var hantu = new PermintaanWa
            {
                ID_Permintaan = 999_999,
                KodePermintaan = "PMT-TIDAK-ADA",
                NomorWA = "081234567890",
                NamaJenis = "SKD_UMUM",
                DataJson = DataJsonAwal,
                Status = WaRequestStatus.BARU
            };

            Assert.False(await _repo.UpdateAsync(hantu));
        }
    }
}
