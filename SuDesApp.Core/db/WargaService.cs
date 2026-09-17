using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.db
{
    public class WargaService
    {
        private readonly AppConfig _config;
        private readonly ILogger<WargaService> _logger;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5); // Cache berlaku 5 menit

        public WargaService(AppConfig config, ILogger<WargaService> logger, IMemoryCache memoryCache)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger.LogInformation("WargaService initialized with cache expiration: {CacheExpiration}", _cacheExpiration);
        }

        public async Task<WargaData> GetWargaByIdAsync(int idWarga)
        {
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            try
            {
                await connection.OpenAsync();
                var warga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE ID_Warga = @ID_Warga",
                    new { ID_Warga = idWarga });
                if (warga != null && !IsDummyNik(warga.NIK))
                {
                    // Simpan ke cache jika bukan NIK dummy
                    string cacheKey = $"Warga_{warga.NIK}";
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheExpiration
                    };
                    _cache.Set(cacheKey, warga, cacheOptions);
                    _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", warga.NIK, idWarga);
                }
                return warga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting warga with ID {WargaId}", idWarga);
                throw new DataAccessException($"Gagal mengambil data warga dengan ID {idWarga}", ex);
            }
        }

        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null)
        {
            if (wargaData == null) throw new ArgumentNullException(nameof(wargaData));

            if (string.IsNullOrWhiteSpace(wargaData.NIK) && !wargaData.isForKematian && !wargaData.isForInstansi)
            {
                _logger.LogError("NIK kosong untuk warga baru (non-kematian/non-instansi). Nama: {Nama}", wargaData.Nama);
                throw new ArgumentException("NIK tidak boleh kosong untuk data warga ini.", nameof(wargaData.NIK));
            }
            if (!string.IsNullOrWhiteSpace(wargaData.NIK) && wargaData.NIK.Length != 16 && wargaData.NIK != "0000000000000000" && wargaData.NIK != "9999999999999999")
            {
                _logger.LogError("Format NIK tidak valid: {NIK}", wargaData.NIK);
                throw new ArgumentException("NIK harus 16 digit angka jika diisi (dan bukan NIK dummy).", nameof(wargaData.NIK));
            }

            SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
            bool manageConnectionLifecycle = existingConnection == null;

            try
            {
                if (manageConnectionLifecycle) await connection.OpenAsync();

                WargaData existingWarga = null;
                if (!string.IsNullOrWhiteSpace(wargaData.NIK))
                {
                    existingWarga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                        "SELECT * FROM Warga WHERE NIK = @NIK",
                        new { wargaData.NIK },
                        existingTransaction);
                }

                if (existingWarga != null)
                {
                    _logger.LogInformation("Warga dengan NIK {NIK} ditemukan (ID: {ID_Warga}). Memperbarui data jika perlu.", wargaData.NIK, existingWarga.ID_Warga);
                    await UpdateWargaAsync(existingWarga.ID_Warga, wargaData, connection, existingTransaction, wargaData.isForInstansi, wargaData.isForKematian);
                    return existingWarga.ID_Warga;
                }
                else
                {
                    _logger.LogInformation("Menambahkan Warga baru. NIK: {NIK}, Nama: {Nama}", wargaData.NIK, wargaData.Nama);
                    var query = @"
                        INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, StatusPerkawinan, Pekerjaan, Alamat, Pendidikan, Kewarganegaraan)
                        VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama, @StatusPerkawinan, @Pekerjaan, @Alamat, @Pendidikan, @Kewarganegaraan);
                        SELECT last_insert_rowid();";

                    var insertData = new
                    {
                        wargaData.NIK,
                        wargaData.Nama,
                        TempatLahir = wargaData.TempatLahir ?? "-",
                        TanggalLahir = wargaData.TanggalLahir ?? "1900-01-01",
                        JenisKelamin = wargaData.JenisKelamin ?? "-",
                        Agama = wargaData.Agama ?? "-",
                        StatusPerkawinan = wargaData.StatusPerkawinan ?? "-",
                        Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                        Alamat = wargaData.Alamat ?? "-",
                        Pendidikan = wargaData.Pendidikan ?? string.Empty,
                        Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
                    };
                    var newIdWarga = await connection.ExecuteScalarAsync<int>(query, insertData, existingTransaction);
                    _logger.LogInformation("Warga baru ditambahkan dengan ID: {ID_Warga}", newIdWarga);

                    // Simpan ke cache jika bukan NIK dummy
                    if (!IsDummyNik(wargaData.NIK))
                    {
                        wargaData.ID_Warga = newIdWarga; // Set ID_Warga untuk cache
                        string cacheKey = $"Warga_{wargaData.NIK}";
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        };
                        _cache.Set(cacheKey, wargaData, cacheOptions);
                        _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", wargaData.NIK, newIdWarga);
                    }

                    return newIdWarga;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambah atau memperbarui warga: NIK={NIK}", wargaData.NIK);
                throw new DataAccessException("Gagal memproses data warga.", ex);
            }
            finally
            {
                if (manageConnectionLifecycle && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        //public async Task<WargaData> GetWargaByNikAsync(string nik)
        //{
        //    using var connection = new SqliteConnection(_config.DatabaseConnectionString);
        //    await connection.OpenAsync();
        //    try
        //    {
        //        return await connection.QueryFirstOrDefaultAsync<WargaData>(
        //            "SELECT * FROM Warga WHERE NIK = @NIK", new { NIK = nik });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Gagal mengambil data warga berdasarkan NIK {NIK}.", nik);
        //        throw new DataRetrievalException("Gagal mengambil data warga.", ex);
        //    }
        //}

        public async Task<WargaData> GetWargaByNikAsync(string nik)
        {
            if (string.IsNullOrWhiteSpace(nik))
            {
                _logger.LogWarning("GetWargaByNikAsync called with null or empty NIK.");
                return null;
            }

            // Cek cache terlebih dahulu
            string cacheKey = $"Warga_{nik}";
            if (_cache.TryGetValue(cacheKey, out WargaData cachedWarga))
            {
                _logger.LogInformation("Warga data retrieved from cache for NIK: {NIK}", nik);
                return cachedWarga;
            }

            // Jika tidak ada di cache, query database
            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();
                var result = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, StatusPerkawinan, Pendidikan, Kewarganegaraan, Pekerjaan, Alamat, ID_Warga " +
                    "FROM Warga WHERE NIK = @NIK",
                    new { NIK = nik });

                if (result != null && !IsDummyNik(nik))
                {
                    // Simpan ke cache dengan expiration jika bukan NIK dummy
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheExpiration
                    };
                    _cache.Set(cacheKey, result, cacheOptions);
                    _logger.LogInformation("Warga data retrieved from database and cached for NIK: {NIK}, Nama: {Nama}", nik, result.Nama);
                }
                else
                {
                    _logger.LogInformation("No Warga data found for NIK: {NIK}", nik);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Warga data for NIK: {NIK}", nik);
                return null;
            }
        }

        public async Task<bool> InsertWargaAsync(WargaData warga)
        {
            if (warga == null || string.IsNullOrWhiteSpace(warga.NIK))
            {
                _logger.LogWarning("InsertWargaAsync called with null or invalid Warga data.");
                return false;
            }

            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();
                var rowsAffected = await connection.ExecuteAsync(
                    "INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, StatusPerkawinan, Pendidikan, Kewarganegaraan, Pekerjaan, Alamat) " +
                    "VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama, @StatusPerkawinan, @Pendidikan, @Kewarganegaraan, @Pekerjaan, @Alamat)",
                    warga);

                if (rowsAffected > 0)
                {
                    // Simpan ke cache jika bukan NIK dummy
                    if (!IsDummyNik(warga.NIK))
                    {
                        string cacheKey = $"Warga_{warga.NIK}";
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        };
                        _cache.Set(cacheKey, warga, cacheOptions);
                        _logger.LogInformation("Warga inserted and cached successfully for NIK: {NIK}, Nama: {Nama}", warga.NIK, warga.Nama);
                    }
                    return true;
                }

                _logger.LogWarning("No rows affected while inserting Warga for NIK: {NIK}", warga.NIK);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert Warga for NIK: {NIK}", warga.NIK);
                return false;
            }
        }

        public async Task<WargaData> GetWargaDataAsync(int idWarga) => await GetWargaByIdAsync(idWarga);

        public async Task<List<WargaData>> GetAllWargaAsync(int skip, int take)
        {
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            const string query = @"
                SELECT ID_Warga, NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                       Agama, StatusPerkawinan, Pekerjaan, Alamat, Pendidikan, Kewarganegaraan
                FROM Warga
                ORDER BY Nama
                LIMIT @Take OFFSET @Skip";
            try
            {
                var wargaList = (await connection.QueryAsync<WargaData>(query, new { Take = take, Skip = skip })).ToList();
                // Cache setiap warga yang bukan dummy
                foreach (var warga in wargaList)
                {
                    if (!IsDummyNik(warga.NIK))
                    {
                        string cacheKey = $"Warga_{warga.NIK}";
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        };
                        _cache.Set(cacheKey, warga, cacheOptions);
                        _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", warga.NIK, warga.ID_Warga);
                    }
                }
                return wargaList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil semua data warga.");
                throw new DataRetrievalException("Gagal mengambil semua data warga.", ex);
            }
        }

        public async Task<int> AddOrGetWargaAsync(WargaData wargaData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null)
        {
            if (wargaData == null || (string.IsNullOrWhiteSpace(wargaData.NIK) && !wargaData.isForKematian && !wargaData.isForInstansi))
            {
                _logger.LogError("WargaData or NIK is null/empty in AddOrGetWargaAsync, Context: isForKematian={isForKematian}, isForInstansi={isForInstansi}",
                    wargaData?.isForKematian, wargaData?.isForInstansi);
                throw new ArgumentNullException(nameof(wargaData), "Data warga atau NIK tidak boleh kosong (kecuali NIK opsional untuk kematian/instansi).");
            }

            if (!string.IsNullOrWhiteSpace(wargaData.NIK) &&
                wargaData.NIK != "0000000000000000" &&
                wargaData.NIK != "9999999999999999" &&
                (wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit)))
            {
                _logger.LogError("Invalid NIK format: {NIK}", wargaData.NIK);
                throw new ArgumentException("NIK harus 16 digit angka jika diisi (dan bukan NIK dummy).", nameof(wargaData.NIK));
            }

            _logger.LogDebug("AddOrGetWargaAsync: NIK={NIK}, Nama={Nama}, TempatLahir={TempatLahir}, TanggalLahir={TanggalLahir}, JenisKelamin={JenisKelamin}, Agama={Agama}, Alamat={Alamat}, Using existing connection: {UseExistingConn}, existing transaction: {UseExistingTx}",
                wargaData.NIK, wargaData.Nama, wargaData.TempatLahir, wargaData.TanggalLahir, wargaData.JenisKelamin, wargaData.Agama, wargaData.Alamat, existingConnection != null, existingTransaction != null);

            SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
            bool manageConnectionLifecycle = existingConnection == null;

            try
            {
                if (manageConnectionLifecycle)
                {
                    await connection.OpenAsync();
                }

                WargaData existingWarga = null;
                if (!string.IsNullOrWhiteSpace(wargaData.NIK))
                {
                    existingWarga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                        "SELECT * FROM Warga WHERE NIK = @NIK",
                        new { wargaData.NIK },
                        existingTransaction);
                }

                if (existingWarga != null)
                {
                    _logger.LogInformation("Found existing warga: NIK={NIK}, ID_Warga={ID_Warga}", wargaData.NIK, existingWarga.ID_Warga);
                    bool needsUpdate = (existingWarga.Nama != wargaData.Nama) ||
                                       (existingWarga.TempatLahir != wargaData.TempatLahir) ||
                                       (existingWarga.TanggalLahir != wargaData.TanggalLahir) ||
                                       (existingWarga.JenisKelamin != wargaData.JenisKelamin) ||
                                       (existingWarga.Agama != wargaData.Agama) ||
                                       (existingWarga.Alamat != wargaData.Alamat) ||
                                       (existingWarga.Pekerjaan != wargaData.Pekerjaan);

                    if (needsUpdate)
                    {
                        _logger.LogInformation("Updating warga: NIK={NIK}, ID_Warga={ID_Warga}", wargaData.NIK, existingWarga.ID_Warga);
                        await UpdateWargaAsync(existingWarga.ID_Warga, wargaData, connection, existingTransaction, wargaData.isForInstansi, wargaData.isForKematian);
                    }

                    // Simpan ke cache jika bukan NIK dummy
                    if (!IsDummyNik(wargaData.NIK))
                    {
                        wargaData.ID_Warga = existingWarga.ID_Warga;
                        string cacheKey = $"Warga_{wargaData.NIK}";
                        var cacheOptions = new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = _cacheExpiration
                        };
                        _cache.Set(cacheKey, wargaData, cacheOptions);
                        _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", wargaData.NIK, existingWarga.ID_Warga);
                    }

                    return existingWarga.ID_Warga;
                }

                _logger.LogInformation("Adding new warga: NIK={NIK}, Nama={Nama}", wargaData.NIK, wargaData.Nama);
                var nikToInsert = wargaData.NIK;
                if ((wargaData.isForKematian || wargaData.isForInstansi) && string.IsNullOrWhiteSpace(nikToInsert))
                {
                    nikToInsert = wargaData.isForInstansi ? "9999999999999999" : "0000000000000000";
                    _logger.LogInformation("NIK is empty for Kematian/Instansi, using NIK dummy: {NikDummy}", nikToInsert);
                }
                else if (string.IsNullOrWhiteSpace(nikToInsert))
                {
                    throw new ArgumentException("NIK tidak boleh kosong untuk warga baru (non-kematian/non-instansi).", nameof(wargaData.NIK));
                }

                var insertData = new
                {
                    NIK = nikToInsert,
                    wargaData.Nama,
                    TempatLahir = wargaData.TempatLahir ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "-" : throw new ArgumentException("TempatLahir wajib diisi.")),
                    TanggalLahir = wargaData.TanggalLahir ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "1900-01-01" : throw new ArgumentException("TanggalLahir wajib diisi.")),
                    JenisKelamin = wargaData.JenisKelamin ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "-" : throw new ArgumentException("JenisKelamin wajib diisi.")),
                    Agama = wargaData.Agama ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "-" : throw new ArgumentException("Agama wajib diisi.")),
                    StatusPerkawinan = wargaData.StatusPerkawinan ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "-" : throw new ArgumentException("StatusPerkawinan wajib diisi.")),
                    Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                    Alamat = wargaData.Alamat ?? ((wargaData.isForKematian || wargaData.isForInstansi) ? "-" : throw new ArgumentException("Alamat wajib diisi.")),
                    Pendidikan = wargaData.Pendidikan ?? string.Empty,
                    Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
                };
                var query = @"
                    INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama, StatusPerkawinan, Pekerjaan, Alamat, Pendidikan, Kewarganegaraan)
                    VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Agama, @StatusPerkawinan, @Pekerjaan, @Alamat, @Pendidikan, @Kewarganegaraan);
                    SELECT last_insert_rowid();";
                var newIdWarga = await connection.ExecuteScalarAsync<int>(query, insertData, existingTransaction);

                // Simpan ke cache jika bukan NIK dummy
                if (!IsDummyNik(nikToInsert))
                {
                    wargaData.ID_Warga = newIdWarga;
                    string cacheKey = $"Warga_{nikToInsert}";
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheExpiration
                    };
                    _cache.Set(cacheKey, wargaData, cacheOptions);
                    _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", nikToInsert, newIdWarga);
                }

                return newIdWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add or get warga: NIK={NIK}, Nama={Nama}, TempatLahir={TempatLahir}, TanggalLahir={TanggalLahir}, JenisKelamin={JenisKelamin}, Agama={Agama}, Alamat={Alamat}",
                    wargaData.NIK, wargaData.Nama, wargaData.TempatLahir, wargaData.TanggalLahir, wargaData.JenisKelamin, wargaData.Agama, wargaData.Alamat);
                throw new DataAccessException("Gagal menambahkan atau mengambil warga.", ex);
            }
            finally
            {
                if (manageConnectionLifecycle && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                    connection.Dispose();
                }
            }
        }

        public async Task<int> UpdateWargaAsync(int idWarga, WargaData wargaData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null, bool isForInstansi = false, bool isForKematian = false)
        {
            if (wargaData == null) throw new ArgumentNullException(nameof(wargaData));
            _logger.LogDebug("Memperbarui Warga ID: {ID_Warga}, NIK: {NIK}, Nama: {Nama}, isForInstansi: {isForInstansi}, isForKematian: {isForKematian}",
                idWarga, wargaData.NIK, wargaData.Nama, isForInstansi, isForKematian);

            SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
            bool manageConnectionLifecycle = existingConnection == null;

            try
            {
                if (manageConnectionLifecycle) await connection.OpenAsync();

                // Cek NIK unik jika NIK diubah dan tidak kosong
                if (!string.IsNullOrWhiteSpace(wargaData.NIK) && wargaData.NIK != "9999999999999999" && wargaData.NIK != "0000000000000000")
                {
                    var existingNikWarga = await connection.QueryFirstOrDefaultAsync<WargaData>(
                        "SELECT ID_Warga, Nama FROM Warga WHERE NIK = @NIK AND ID_Warga != @ID_Warga_Param",
                        new { wargaData.NIK, ID_Warga_Param = idWarga },
                        existingTransaction);
                    if (existingNikWarga != null)
                    {
                        _logger.LogError("NIK {NIK} sudah digunakan oleh warga lain: ID_Warga={ID_Warga}, Nama={Nama}",
                            wargaData.NIK, existingNikWarga.ID_Warga, existingNikWarga.Nama);
                        throw new DataAccessException($"NIK '{wargaData.NIK}' sudah digunakan oleh warga lain: {existingNikWarga.Nama}.");
                    }
                }

                var query = @"
                    UPDATE Warga
                    SET NIK = @NIK, Nama = @Nama, TempatLahir = @TempatLahir, TanggalLahir = @TanggalLahir, 
                        JenisKelamin = @JenisKelamin, Agama = @Agama, StatusPerkawinan = @StatusPerkawinan, 
                        Pekerjaan = @Pekerjaan, Alamat = @Alamat, Pendidikan = @Pendidikan, Kewarganegaraan = @Kewarganegaraan
                    WHERE ID_Warga = @ID_Warga_Param";

                var updateData = new
                {
                    ID_Warga_Param = idWarga,
                    NIK = wargaData.NIK ?? (isForInstansi ? "9999999999999999" : isForKematian ? "0000000000000000" : throw new ArgumentException("NIK wajib diisi.")),
                    wargaData.Nama,
                    TempatLahir = wargaData.TempatLahir ?? "-",
                    TanggalLahir = wargaData.TanggalLahir ?? "1900-01-01",
                    JenisKelamin = wargaData.JenisKelamin ?? "-",
                    Agama = wargaData.Agama ?? "-",
                    StatusPerkawinan = wargaData.StatusPerkawinan ?? "-",
                    Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
                    Alamat = wargaData.Alamat ?? "-",
                    Pendidikan = wargaData.Pendidikan ?? string.Empty,
                    Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
                };

                var rowsAffected = await connection.ExecuteAsync(query, updateData, existingTransaction);
                _logger.LogInformation("Warga updated: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}, RowsAffected={RowsAffected}",
                    idWarga, wargaData.Nama, wargaData.NIK, rowsAffected);

                // Simpan ke cache jika bukan NIK dummy
                if (!IsDummyNik(wargaData.NIK))
                {
                    wargaData.ID_Warga = idWarga;
                    string cacheKey = $"Warga_{wargaData.NIK}";
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheExpiration
                    };
                    _cache.Set(cacheKey, wargaData, cacheOptions);
                    _logger.LogInformation("Warga data cached for NIK: {NIK}, ID_Warga: {ID_Warga}", wargaData.NIK, idWarga);
                }

                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memperbarui warga ID: {ID_Warga}, Nama: {Nama}, NIK: {NIK}", idWarga, wargaData.Nama, wargaData.NIK);
                throw new DataAccessException($"Gagal memperbarui warga dengan ID {idWarga}.", ex);
            }
            finally
            {
                if (manageConnectionLifecycle && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        //public async Task<int> UpdateWargaAsync(int idWarga, WargaData wargaData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null, bool isForInstansi = false, bool isForKematian = false)
        //{
        //    if (wargaData == null)
        //    {
        //        _logger.LogError("WargaData is null in UpdateWargaAsync for ID_Warga={ID_Warga}", idWarga);
        //        throw new ArgumentNullException(nameof(wargaData));
        //    }

        //    // Logging detail nilai WargaData
        //    _logger.LogDebug("UpdateWargaAsync: ID_Warga={ID_Warga}, NIK={NIK}, Nama={Nama}, TempatLahir={TempatLahir}, TanggalLahir={TanggalLahir}, JenisKelamin={JenisKelamin}, Agama={Agama}, Alamat={Alamat}, Pekerjaan={Pekerjaan}, StatusPerkawinan={StatusPerkawinan}, isForInstansi={isForInstansi}, isForKematian={isForKematian}",
        //        idWarga, wargaData.NIK, wargaData.Nama, wargaData.TempatLahir, wargaData.TanggalLahir, wargaData.JenisKelamin, wargaData.Agama, wargaData.Alamat, wargaData.Pekerjaan, wargaData.StatusPerkawinan, isForInstansi, isForKematian);

        //    // Validasi WargaData
        //    var missingFields = new List<string>();
        //    if (string.IsNullOrWhiteSpace(wargaData.Nama) || wargaData.Nama.Length < 3) missingFields.Add("Nama");
        //    if (!string.IsNullOrWhiteSpace(wargaData.NIK) && (wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit))) missingFields.Add("NIK");
        //    if (!isForInstansi && !isForKematian)
        //    {
        //        if (string.IsNullOrWhiteSpace(wargaData.TempatLahir)) missingFields.Add("TempatLahir");
        //        if (string.IsNullOrWhiteSpace(wargaData.TanggalLahir)) missingFields.Add("TanggalLahir");
        //        if (string.IsNullOrWhiteSpace(wargaData.JenisKelamin)) missingFields.Add("JenisKelamin");
        //        if (string.IsNullOrWhiteSpace(wargaData.Agama)) missingFields.Add("Agama");
        //        if (string.IsNullOrWhiteSpace(wargaData.Alamat)) missingFields.Add("Alamat");
        //        if (wargaData.NamaJenis?.ToUpperInvariant() != "GARAPAN_SAWAH" && string.IsNullOrWhiteSpace(wargaData.StatusPerkawinan))
        //            missingFields.Add("StatusPerkawinan");
        //    }

        //    if (missingFields.Any() || !wargaData.IsValid())
        //    {
        //        _logger.LogError("WargaData validation failed in UpdateWargaAsync: ID_Warga={ID_Warga}, NIK={NIK}, Nama={Nama}, TempatLahir={TempatLahir}, TanggalLahir={TanggalLahir}, JenisKelamin={JenisKelamin}, Agama={Agama}, Alamat={Alamat}, Pekerjaan={Pekerjaan}, StatusPerkawinan={StatusPerkawinan}, MissingFields={MissingFields}, IsValid={IsValid}",
        //            idWarga, wargaData.NIK ?? "null", wargaData.Nama ?? "null", wargaData.TempatLahir ?? "null", wargaData.TanggalLahir ?? "null", wargaData.JenisKelamin ?? "null", wargaData.Agama ?? "null", wargaData.Alamat ?? "null", wargaData.Pekerjaan ?? "null", wargaData.StatusPerkawinan ?? "null", string.Join(", ", missingFields), wargaData.IsValid());
        //        throw new ArgumentException($"Data warga tidak valid. Kolom yang bermasalah: {string.Join(", ", missingFields)}.", nameof(wargaData));
        //    }

        //    // Validasi NIK dan Nama disesuaikan
        //    if (isForInstansi)
        //    {
        //        if (string.IsNullOrWhiteSpace(wargaData.NIK) || wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit) || wargaData.NIK != "9999999999999999")
        //            throw new ArgumentException("NIK penanggung jawab instansi tidak valid.", nameof(wargaData.NIK));
        //        if (string.IsNullOrWhiteSpace(wargaData.Nama) || wargaData.Nama.Length < 3)
        //            throw new ArgumentException("Nama penanggung jawab instansi harus minimal 3 karakter.", nameof(wargaData.Nama));
        //    }
        //    else if (isForKematian)
        //    {
        //        if (string.IsNullOrWhiteSpace(wargaData.Nama) || wargaData.Nama.Length < 3)
        //            throw new ArgumentException("Nama almarhum/ah harus minimal 3 karakter.", nameof(wargaData.Nama));
        //        if (!string.IsNullOrWhiteSpace(wargaData.NIK) && wargaData.NIK != "0000000000000000" && (wargaData.NIK.Length != 16 || !wargaData.NIK.All(char.IsDigit)))
        //            throw new ArgumentException("NIK almarhum/ah harus 16 digit angka jika diisi (dan bukan NIK dummy kematian).", nameof(wargaData.NIK));
        //    }
        //    else
        //    {
        //        if (!wargaData.IsValid()) throw new ArgumentException("Data warga tidak valid.", nameof(wargaData));
        //    }

        //    SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
        //    IDbTransaction transactionToUse = existingTransaction;
        //    bool manageConnectionLifecycle = existingConnection == null;
        //    bool manageTransactionLifecycle = false;

        //    try
        //    {
        //        if (manageConnectionLifecycle)
        //        {
        //            await connection.OpenAsync();
        //        }

        //        if (existingTransaction == null && manageConnectionLifecycle)
        //        {
        //            transactionToUse = await connection.BeginTransactionAsync();
        //            manageTransactionLifecycle = true;
        //        }

        //        _logger.LogDebug("Updating warga: ID_Warga={ID_Warga}, NIK={NIK}. Using existing transaction: {UseExistingTx}",
        //            idWarga, wargaData.NIK, transactionToUse != null && existingTransaction != null);

        //        var query = @"
        //            UPDATE Warga
        //            SET NIK = @NIK, Nama = @Nama, TempatLahir = @TempatLahir, TanggalLahir = @TanggalLahir, 
        //                JenisKelamin = @JenisKelamin, Agama = @Agama, StatusPerkawinan = @StatusPerkawinan, 
        //                Pekerjaan = @Pekerjaan, Alamat = @Alamat, Pendidikan = @Pendidikan, Kewarganegaraan = @Kewarganegaraan
        //            WHERE ID_Warga = @ID_Warga_Param";

        //        var nikUpdateValue = wargaData.NIK;
        //        if ((isForKematian || isForInstansi) && string.IsNullOrWhiteSpace(nikUpdateValue))
        //        {
        //            nikUpdateValue = isForInstansi ? "9999999999999999" : "0000000000000000";
        //        }

        //        var updateData = new
        //        {
        //            ID_Warga_Param = idWarga,
        //            NIK = nikUpdateValue,
        //            wargaData.Nama,
        //            TempatLahir = wargaData.TempatLahir ?? ((isForKematian || isForInstansi) ? "-" : null),
        //            TanggalLahir = wargaData.TanggalLahir ?? ((isForKematian || isForInstansi) ? "1900-01-01" : null),
        //            JenisKelamin = wargaData.JenisKelamin ?? ((isForKematian || isForInstansi) ? "-" : null),
        //            Agama = wargaData.Agama ?? ((isForKematian || isForInstansi) ? "-" : null),
        //            StatusPerkawinan = wargaData.StatusPerkawinan ?? ((isForKematian || isForInstansi) ? "-" : null),
        //            Pekerjaan = wargaData.Pekerjaan ?? string.Empty,
        //            Alamat = wargaData.Alamat ?? ((isForKematian || isForInstansi) ? "-" : null),
        //            Pendidikan = wargaData.Pendidikan ?? string.Empty,
        //            Kewarganegaraan = wargaData.Kewarganegaraan ?? "WNI"
        //        };

        //        var rowsAffected = await connection.ExecuteAsync(query, updateData, transactionToUse);

        //        if (manageTransactionLifecycle && transactionToUse != null)
        //        {
        //            if (transactionToUse is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync(); else transactionToUse.Commit();
        //        }
        //        _logger.LogInformation("Warga updated: ID_Warga={ID_Warga}, RowsAffected={RowsAffected}", idWarga, rowsAffected);
        //        return rowsAffected;
        //    }
        //    catch (Exception ex)
        //    {
        //        if (manageTransactionLifecycle && transactionToUse != null)
        //        {
        //            try { if (transactionToUse is System.Data.Common.DbTransaction dbTrans) await dbTrans.RollbackAsync(); else transactionToUse.Rollback(); }
        //            catch (Exception rbEx) { _logger.LogError(rbEx, "Rollback failed in UpdateWargaAsync."); }
        //        }
        //        _logger.LogError(ex, "Failed to update warga: ID_Warga={ID_Warga}, NIK={NIK}, Nama={Nama}, TempatLahir={TempatLahir}, TanggalLahir={TanggalLahir}, JenisKelamin={JenisKelamin}, Agama={Agama}, Alamat={Alamat}",
        //            idWarga, wargaData.NIK, wargaData.Nama, wargaData.TempatLahir, wargaData.TanggalLahir, wargaData.JenisKelamin, wargaData.Agama, wargaData.Alamat);
        //        throw new DataAccessException($"Gagal memperbarui warga dengan ID {idWarga}.", ex);
        //    }
        //    finally
        //    {
        //        if (manageTransactionLifecycle && transactionToUse != null)
        //        {
        //            if (transactionToUse is System.Data.Common.DbTransaction dbTrans) await dbTrans.DisposeAsync(); else transactionToUse.Dispose();
        //        }
        //        if (manageConnectionLifecycle && connection?.State == ConnectionState.Open)
        //        {
        //            await connection.CloseAsync();
        //            connection.Dispose();
        //        }
        //    }
        //}

        public async Task<int> GetOrCreateDummyWargaAsync(IDbConnection connection, IDbTransaction transaction)
        {
            const string dummyNik = "9999999999999999";
            const string dummyName = "INSTANSI DUMMY";

            _logger.LogDebug("GetOrCreateDummyWargaAsync (for Instansi): Checking for NIK={DummyNik}", dummyNik);
            try
            {
                var existing = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE NIK = @NIK",
                    new { NIK = dummyNik },
                    transaction: transaction);

                if (existing != null)
                {
                    _logger.LogInformation("Found dummy warga (Instansi): ID_Warga={ID_Warga}, NIK={NIK}", existing.ID_Warga, existing.NIK);
                    return existing.ID_Warga;
                }

                _logger.LogInformation("Creating new dummy warga (Instansi): NIK={DummyNik}", dummyNik);
                var dummyWarga = new WargaData
                {
                    NIK = dummyNik,
                    Nama = dummyName,
                    TempatLahir = "N/A",
                    TanggalLahir = "1900-01-01",
                    JenisKelamin = "N/A",
                    Alamat = "N/A",
                    Agama = "N/A",
                    StatusPerkawinan = "N/A",
                    Pekerjaan = "N/A",
                    Pendidikan = "N/A",
                    Kewarganegaraan = "N/A"
                };

                var idWarga = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Alamat, Agama, StatusPerkawinan, Pekerjaan, Pendidikan, Kewarganegaraan)
                      VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, @Alamat, @Agama, @StatusPerkawinan, @Pekerjaan, @Pendidikan, @Kewarganegaraan);
                      SELECT last_insert_rowid();", dummyWarga, transaction: transaction);

                if (idWarga <= 0) throw new DataAccessException("Gagal membuat dummy warga (Instansi).");
                _logger.LogInformation("Created dummy warga (Instansi): ID_Warga={ID_Warga}, NIK={NIK}", idWarga, dummyNik);
                return idWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create dummy warga (Instansi): NIK={DummyNik}", dummyNik);
                throw new DataAccessException("Gagal mendapatkan atau membuat dummy warga (Instansi).", ex);
            }
        }

        private bool IsDummyNik(string nik)
        {
            return nik == "0000000000000000" || nik == "9999999999999999";
        }
    }

    [Serializable]
    internal class DataRetrievalException : Exception
    {
        public DataRetrievalException() { }
        public DataRetrievalException(string? message) : base(message) { }
        public DataRetrievalException(string? message, Exception? innerException) : base(message, innerException) { }
    }

    [Serializable]
    internal class DataAccessException : Exception
    {
        public DataAccessException() { }
        public DataAccessException(string? message) : base(message) { }
        public DataAccessException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
