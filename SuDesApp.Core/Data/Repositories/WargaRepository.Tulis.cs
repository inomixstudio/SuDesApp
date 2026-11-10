using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public partial class WargaRepository : IWargaRepository
    {
        private void ValidateWargaData(WargaData wargaData)
        {
            if (wargaData == null)
                throw new ArgumentNullException(nameof(wargaData));

            if (!wargaData.IsValid(out var wargaErrors))
            {
                _logger.LogWarning("Validasi data warga gagal: {Errors}", string.Join("; ", wargaErrors));
                throw new ValidationException($"Data warga tidak valid: {string.Join("; ", wargaErrors)}");
            }

            // Validasi NIK khusus untuk kasus instansi atau kematian
            if (wargaData.IsForInstansi)
            {
                if (wargaData.NIK != DummyNikInstansi)
                {
                    throw new ArgumentException($"NIK untuk instansi harus {DummyNikInstansi}.");
                }
            }
            else if (wargaData.IsForKematian)
            {
                if (wargaData.NIK != DummyNikKematian)
                {
                    throw new ArgumentException($"NIK untuk kematian harus {DummyNikKematian}.");
                }

                if (string.IsNullOrWhiteSpace(wargaData.TempatLahir))
                    wargaData.TempatLahir = "Tidak diketahui";

                if (string.IsNullOrWhiteSpace(wargaData.TanggalLahir))
                    wargaData.TanggalLahir = "01-01-1900";
            }
            else
            {
                // Validasi NIK untuk kasus non-dummy
                if (string.IsNullOrWhiteSpace(wargaData.NIK) ||
                    wargaData.NIK == DummyNikInstansi ||
                    wargaData.NIK == DummyNikKematian)
                {
                    throw new ArgumentException("NIK tidak valid atau menggunakan NIK dummy untuk kasus non-instansi/kematian.");
                }

                // Validasi format NIK (16 digit angka)
                if (wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit))
                {
                    throw new ValidationException("NIK harus terdiri dari 16 digit angka.");
                }
            }
        }

        public async Task<int> AddOrUpdateWargaAsync(
            WargaData wargaData,
            SqliteConnection? existingConnection = null,
            IDbTransaction? existingTransaction = null)
        {
            if (wargaData == null)
                throw new ArgumentNullException(nameof(wargaData));

            ValidateWargaData(wargaData);

            // Normalisasi SEBELUM objek dicache: tanpa ini entri cache membawa
            // null dan pembacaan berikutnya melaporkan status warga kosong.
            wargaData.StatusWarga = StatusWargaTipe.Normalisasi(wargaData.StatusWarga);

            var connection = existingConnection ?? _connection;
            var transaction = existingTransaction;

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var existingWarga = !string.IsNullOrWhiteSpace(wargaData.NIK)
                    ? await connection.QueryFirstOrDefaultAsync<WargaData>(
                        "SELECT * FROM Warga WHERE NIK = @NIK",
                        new { wargaData.NIK },
                        transaction)
                    : null;

                int idWarga;
                if (existingWarga != null)
                {
                    idWarga = existingWarga.ID_Warga;
                    await UpdateWargaInternalAsync(idWarga, wargaData, connection, transaction!);
                }
                else
                {
                    idWarga = await InsertWargaInternalAsync(wargaData, connection, transaction!);
                }

                if (!IsDummyNik(wargaData.NIK!))
                {
                    await CacheWargaAsync(wargaData.NIK!, idWarga, wargaData);
                }

                // Baris baru membuat jumlah warga, rekap, dan seluruh turunan
                // statistik berubah. Tanpa pembatalan ini kartu Data Warga dan
                // Laporan Penduduk menampilkan angka lama sampai cache kedaluwarsa
                // (sampai 1 jam) — warga ke-11 belum terhitung setelah disimpan.
                await _cacheService.RemoveAsync<int?>("Warga_Count");
                await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
                await _cacheService.RemoveByPrefixAsync("Warga_Search_");

                return idWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AddOrUpdateWargaAsync");
                throw new DataAccessException("Gagal menambah/memperbarui data warga", ex);
            }
        }

        private async Task<int> InsertWargaInternalAsync(WargaData wargaData, IDbConnection connection, IDbTransaction transaction)
        {
            var nikToInsert = wargaData.NIK ?? (wargaData.IsForInstansi ? DummyNikInstansi : wargaData.IsForKematian ? DummyNikKematian : throw new ArgumentException("NIK wajib diisi."));

            var insertData = new
            {
                NIK = nikToInsert,
                Nama = wargaData.Nama ?? string.Empty,
                TempatLahir = wargaData.TempatLahir ?? string.Empty,
                TanggalLahir = wargaData.TanggalLahir ?? string.Empty,
                JenisKelamin = wargaData.JenisKelamin ?? string.Empty,
                Agama = wargaData.Agama ?? string.Empty,
                StatusPerkawinan = wargaData.StatusPerkawinan ?? string.Empty,
                StatusKeluarga = wargaData.StatusKeluarga ?? string.Empty,
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                RT = wargaData.RT ?? string.Empty,
                RW = wargaData.RW ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI",
                GolonganDarah = wargaData.GolonganDarah ?? string.Empty,
                NomorHP = wargaData.NomorHP ?? string.Empty,
                StatusWarga = wargaData.StatusWarga ?? StatusWargaTipe.Aktif,
                NamaAyah = wargaData.NamaAyah ?? string.Empty,
                NamaIbu = wargaData.NamaIbu ?? string.Empty,
                NoKK = wargaData.NoKK ?? string.Empty,
                AlamatDetail = wargaData.AlamatDetail ?? string.Empty,
                TanggalStatus = wargaData.TanggalStatus ?? string.Empty,
                KeteranganWarga = wargaData.KeteranganWarga ?? string.Empty
            };

            var query = @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin,
                   Agama, StatusPerkawinan, StatusKeluarga, Pekerjaan, Dusun, RT, RW, Desa, Kecamatan, Kabupaten,
                   Pendidikan, Kewarganegaraan, GolonganDarah, NomorHP, StatusWarga,
                   NamaAyah, NamaIbu, NoKK, AlamatDetail, TanggalStatus, KeteranganWarga)
                VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama,
                        @StatusPerkawinan, @StatusKeluarga, @Pekerjaan, @Dusun, @RT, @RW, @Desa, @Kecamatan, @Kabupaten,
                        @Pendidikan, @Kewarganegaraan, @GolonganDarah, @NomorHP, @StatusWarga,
                        @NamaAyah, @NamaIbu, @NoKK, @AlamatDetail, @TanggalStatus, @KeteranganWarga);
                SELECT last_insert_rowid();";

            return await connection.ExecuteScalarAsync<int>(query, insertData, transaction);
        }

        private async Task<int> UpdateWargaInternalAsync(int idWarga, WargaData wargaData, SqliteConnection connection, IDbTransaction transaction)
        {
            if (!string.IsNullOrWhiteSpace(wargaData.NIK) && !IsDummyNik(wargaData.NIK))
            {
                var existingNikWarga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT ID_Warga, Nama FROM Warga WHERE NIK = @NIK AND ID_Warga != @ID_Warga_Param",
                    new { wargaData.NIK, ID_Warga_Param = idWarga },
                    transaction);

                if (existingNikWarga != null)
                {
                    throw new ValidationException($"NIK '{wargaData.NIK}' sudah digunakan oleh warga lain: {existingNikWarga.Nama}.");
                }
            }

            var query = @"UPDATE Warga
                SET NIK = @NIK, Nama = @Nama, TempatLahir = @TempatLahir,
                    TanggalLahir = @TanggalLahir, JenisKelamin = @JenisKelamin,
                    Agama = @Agama, StatusPerkawinan = @StatusPerkawinan,
                    StatusKeluarga = @StatusKeluarga,
                    Pekerjaan = @Pekerjaan, Dusun = @Dusun, RT = @RT, RW = @RW, Desa = @Desa,
                    Kecamatan = @Kecamatan, Kabupaten = @Kabupaten,
                    Pendidikan = @Pendidikan, Kewarganegaraan = @Kewarganegaraan,
                    GolonganDarah = @GolonganDarah, NomorHP = @NomorHP, StatusWarga = @StatusWarga,
                    NamaAyah = @NamaAyah, NamaIbu = @NamaIbu, NoKK = @NoKK,
                    AlamatDetail = @AlamatDetail, TanggalStatus = @TanggalStatus,
                    KeteranganWarga = @KeteranganWarga
                WHERE ID_Warga = @ID_Warga_Param";

            var updateData = new
            {
                ID_Warga_Param = idWarga,
                NIK = wargaData.NIK ?? (wargaData.IsForInstansi ? DummyNikInstansi : wargaData.IsForKematian ? DummyNikKematian : throw new ArgumentException("NIK wajib diisi.")),
                Nama = wargaData.Nama ?? string.Empty,
                TempatLahir = wargaData.TempatLahir ?? string.Empty,
                TanggalLahir = wargaData.TanggalLahir ?? string.Empty,
                JenisKelamin = wargaData.JenisKelamin ?? string.Empty,
                Agama = wargaData.Agama ?? string.Empty,
                StatusPerkawinan = wargaData.StatusPerkawinan ?? string.Empty,
                StatusKeluarga = wargaData.StatusKeluarga ?? string.Empty,
                Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                Dusun = wargaData.Dusun ?? string.Empty,
                RT = wargaData.RT ?? string.Empty,
                RW = wargaData.RW ?? string.Empty,
                Desa = wargaData.Desa ?? string.Empty,
                Kecamatan = wargaData.Kecamatan ?? string.Empty,
                Kabupaten = wargaData.Kabupaten ?? string.Empty,
                Pendidikan = wargaData.Pendidikan ?? string.Empty,
                Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI",
                GolonganDarah = wargaData.GolonganDarah ?? string.Empty,
                NomorHP = wargaData.NomorHP ?? string.Empty,
                StatusWarga = wargaData.StatusWarga ?? StatusWargaTipe.Aktif,
                NamaAyah = wargaData.NamaAyah ?? string.Empty,
                NamaIbu = wargaData.NamaIbu ?? string.Empty,
                NoKK = wargaData.NoKK ?? string.Empty,
                AlamatDetail = wargaData.AlamatDetail ?? string.Empty,
                TanggalStatus = wargaData.TanggalStatus ?? string.Empty,
                KeteranganWarga = wargaData.KeteranganWarga ?? string.Empty
            };

            var rowsAffected = await connection.ExecuteAsync(query, updateData, transaction);

            if (rowsAffected > 0)
            {
                // Jumlah warga (ringkasan Beranda) ikut berubah setiap ada penulisan
                // baris warga — termasuk baris dummy yang dipakai surat tanpa data warga.
                await _cacheService.RemoveAsync<int?>("Warga_Count");

                // Rekap & Laporan Penduduk membaca cache statistik yang bisa hidup
                // sampai 1 jam. Tanpa pembatalan ini, menambah/mengubah/menghapus
                // warga membuat kartu dan halaman laporan menampilkan angka LAMA
                // — warga ke-11 belum terhitung, warga yang sudah dihapus masih
                // terhitung — sampai kedaluwarsa sendirinya.
                await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
            }

            if (rowsAffected > 0 && !IsDummyNik(wargaData.NIK!))
            {
                wargaData.ID_Warga = idWarga;
                wargaData.AlamatLengkap = BuildAlamatLengkap(wargaData);

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                };

                await _cacheService.SetAsync($"Warga_{wargaData.NIK}", wargaData, cacheOptions);
                await _cacheService.SetAsync($"Warga_Id_{idWarga}", wargaData, cacheOptions);

                // Bukan RemoveAsync di sini: baris di atas baru saja ditulis, jadi
                // yang dihapus adalah data yang sedang dibutuhkan tombol Ubah.
                // Yang perlu dibatalkan adalah cache turunan (statistik + pencarian).
                await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
                await _cacheService.RemoveByPrefixAsync("Warga_Search_");
            }

            return rowsAffected;
        }

    }
}
