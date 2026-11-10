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

        public async Task<WargaData> GetWargaByIdAsync(int idWarga)
        {
            if (idWarga <= 0)
            {
                _logger.LogWarning("GetWargaByIdAsync called with invalid ID: {ID}", idWarga);
                return null!;
            }

            string cacheKey = $"Warga_Id_{idWarga}";
            var cachedWarga = await _cacheService.GetAsync<WargaData>(cacheKey);
            if (cachedWarga != null)
            {
                _logger.LogDebug("Data warga diambil dari cache untuk ID={ID_Warga}.", idWarga);
                return cachedWarga;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var result = await _connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE ID_Warga = @ID_Warga",
                    new { ID_Warga = idWarga });

                if (result != null)
                {
                    result.AlamatLengkap = BuildAlamatLengkap(result);

                    if (!IsDummyNik(result.NIK!))
                    {
                        await _cacheService.SetAsync(cacheKey, result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                        await _cacheService.SetAsync($"Warga_{result.NIK}", result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Warga by ID: {ID_Warga}", idWarga);
                throw new DataRetrievalException($"Failed to retrieve Warga by ID: {idWarga}", ex);
            }
        }

        public async Task<int> CountWargaAsync()
        {
            const string cacheKey = "Warga_Count";
            var cached = await _cacheService.GetAsync<int?>(cacheKey);
            if (cached.HasValue)
            {
                return cached.Value;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                int jumlah = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");

                await _cacheService.SetAsync(cacheKey, jumlah, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return jumlah;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghitung jumlah warga");
                return 0;
            }
        }

        public async Task<IEnumerable<WargaData>> GetAllWargaAsync()
        {
            const string cacheKey = "Warga_All";
            var cachedWargaList = await _cacheService.GetAsync<List<WargaData>>(cacheKey);
            if (cachedWargaList != null)
            {
                _logger.LogDebug("Daftar seluruh warga diambil dari cache.");
                return cachedWargaList;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                const string query = @"
                    SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                           Agama, StatusPerkawinan, Pekerjaan, Dusun, Desa, Kecamatan, Kabupaten,
                           Pendidikan, Kewarganegaraan
                    FROM Warga
                    ORDER BY Nama";

                var wargaList = (await _connection.QueryAsync<WargaData>(query)).ToList();

                foreach (var warga in wargaList)
                {
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.SetAsync($"Warga_{warga.NIK}", warga, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                await _cacheService.SetAsync(cacheKey, wargaList, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return wargaList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil semua data warga.");
                throw new DataRetrievalException("Gagal mengambil semua data warga.", ex);
            }
        }

        public async Task<IEnumerable<WargaData>> SearchWargaAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogWarning("SearchWargaAsync called with null or empty search term.");
                return new List<WargaData>();
            }

            string cacheKey = $"Warga_Search_{searchTerm.ToLowerInvariant()}";
            var cachedResults = await _cacheService.GetAsync<List<WargaData>>(cacheKey);
            if (cachedResults != null)
            {
                _logger.LogDebug("Hasil pencarian warga diambil dari cache.");
                return cachedResults;
            }

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var query = @"
                    SELECT * FROM Warga
                    WHERE NIK LIKE @SearchTerm OR Nama LIKE @SearchTerm
                    ORDER BY Nama";

                var results = (await _connection.QueryAsync<WargaData>(
                    query,
                    new { SearchTerm = $"%{searchTerm}%" })).ToList();

                foreach (var warga in results)
                {
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.SetAsync($"Warga_{warga.NIK}", warga, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                }

                await _cacheService.SetAsync(cacheKey, results, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                });

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencari warga dengan term: {SearchTerm}", searchTerm);
                throw new DataRetrievalException($"Gagal mencari warga dengan term: {searchTerm}", ex);
            }
        }

        public async Task<IReadOnlyList<WargaData>> CariWargaUntukPerangkatAsync(string searchTerm, int batas = 20)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return Array.Empty<WargaData>();
            int limit = Math.Clamp(batas, 1, 50);
            try
            {
                if (_connection.State != ConnectionState.Open) await _connection.OpenAsync();
                const string sql = @"
                    SELECT * FROM Warga
                    WHERE (NIK IS NULL OR NIK NOT IN ('9999999999999999','0000000000000000'))
                      AND UPPER(COALESCE(StatusWarga,'AKTIF')) IN ('AKTIF','BARU')
                      AND (NIK LIKE @Cari OR Nama LIKE @Cari)
                    ORDER BY COALESCE(Nama,'') COLLATE NOCASE, ID_Warga
                    LIMIT @Batas";
                var rows = (await _connection.QueryAsync<WargaData>(sql, new
                {
                    Cari = $"%{searchTerm.Trim()}%", Batas = limit
                })).ToList();
                foreach (var warga in rows)
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);
                return rows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencari warga aktif untuk perangkat desa");
                throw new DataRetrievalException("Gagal mencari warga aktif untuk perangkat desa", ex);
            }
        }

        public async Task<bool> DeleteWargaAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("ID harus lebih besar dari 0.", nameof(id));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                var warga = await GetWargaByIdAsync(id);
                if (warga == null)
                {
                    _logger.LogWarning("Tidak ada warga ditemukan dengan ID: {ID}", id);
                    return false;
                }

                var rowsAffected = await _connection.ExecuteAsync(
                    "DELETE FROM Warga WHERE ID_Warga = @ID",
                    new { ID = id });

                if (rowsAffected > 0)
                {
                    await _cacheService.RemoveAsync<int?>("Warga_Count");
                    await _cacheService.RemoveAsync<WargaData>($"Warga_Id_{id}");
                    // Statistik ikut dibatalkan: tanpa ini warga yang baru dihapus
                    // masih terhitung di kartu Data Warga dan Laporan Penduduk
                    // sampai cache kedaluwarsa (sampai 1 jam).
                    await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
                    if (!IsDummyNik(warga.NIK!))
                    {
                        await _cacheService.RemoveAsync<WargaData>($"Warga_{warga.NIK}");
                        await _cacheService.RemoveByPrefixAsync("Warga_Search_");
                    }
                    _logger.LogInformation("Warga dengan ID: {ID} berhasil dihapus", id);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus warga dengan ID: {ID}, Message: {Message}", id, ex.Message);
                throw new DataAccessException($"Gagal menghapus warga dengan ID: {id}", ex);
            }
        }

        public async Task<string> GetAlamatByNikAsync(string nik)
        {
            var warga = await GetWargaByNikAsync(nik);
            return warga != null ? BuildAlamatLengkap(warga) : null!;
        }

    }
}
