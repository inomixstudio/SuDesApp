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

        private async Task CacheWargaAsync(string nik, int idWarga, WargaData wargaData)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            };

            await _cacheService.SetAsync($"Warga_{nik}", wargaData, cacheOptions);
            await _cacheService.SetAsync($"Warga_Id_{idWarga}", wargaData, cacheOptions);
        }

        public async Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction? transaction = null)
        {
            if (string.IsNullOrWhiteSpace(nik))
            {
                _logger.LogWarning("GetWargaByNikAsync called with null or empty NIK.");
                return null!;
            }

            string cacheKey = $"Warga_{nik}";
            var cachedWarga = await _cacheService.GetAsync<WargaData>(cacheKey);
            if (cachedWarga != null)
            {
                _logger.LogDebug("Data warga diambil dari cache.");
                return cachedWarga;
            }

            var conn = connection ?? _connection;
            try
            {
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                var result = await conn.QueryFirstOrDefaultAsync<WargaData>(
                    @"SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, 
                             StatusPerkawinan, Pekerjaan, Dusun, RT, RW, Desa, Kecamatan, Kabupaten, 
                             Pendidikan, Kewarganegaraan 
                      FROM Warga WHERE NIK = @NIK",
                    new { NIK = nik },
                    transaction);

                if (result != null)
                {
                    result.AlamatLengkap = BuildAlamatLengkap(result);

                    if (!IsDummyNik(nik))
                    {
                        await _cacheService.SetAsync(cacheKey, result, new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        });
                    }
                    _logger.LogDebug("Data warga diambil dari basis data dan disimpan di cache.");
                }
                else
                {
                    _logger.LogDebug("Data warga tidak ditemukan.");
                }

                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil data warga berdasarkan NIK.");
                throw new DataRetrievalException("Gagal mengambil data warga berdasarkan NIK.", ex);
            }
        }

        public async Task<List<WargaData>> AmbilDaftarByNikAsync(IEnumerable<string> daftarNik, CancellationToken ct = default)
        {
            if (daftarNik == null) return new List<WargaData>();

            var nik = daftarNik
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (nik.Count == 0) return new List<WargaData>();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                // Dapper memperluas daftar ke placeholders berparameter,
                // jadi nilai NIK tidak pernah dirangkai ke dalam SQL.
                var hasil = new List<WargaData>();
                const int ukuranBatch = 500;
                for (int i = 0; i < nik.Count; i += ukuranBatch)
                {
                    ct.ThrowIfCancellationRequested();

                    var potongan = nik.Skip(i).Take(ukuranBatch).ToList();
                    var baris = (await _connection.QueryAsync<WargaData>(
                        @"SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                                 StatusPerkawinan, Pekerjaan, Dusun, RT, RW, Desa, Kecamatan, Kabupaten,
                                 Pendidikan, Kewarganegaraan, GolonganDarah, NomorHP, StatusWarga
                          FROM Warga
                         WHERE NIK IN @Nik",
                        new { Nik = potongan })).ToList();

                    foreach (var w in baris)
                    {
                        w.AlamatLengkap = BuildAlamatLengkap(w);
                        hasil.Add(w);
                    }
                }

                return hasil;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca {Jumlah} warga berdasarkan daftar NIK", nik.Count);
                throw new DataRetrievalException("Gagal membaca data warga berdasarkan daftar NIK", ex);
            }
        }

    }
}
