using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Data.Handlers
{
    public class AhliWarisDataHandler : ISuratDataHandler
    {
        private readonly ILogger<AhliWarisDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public AhliWarisDataHandler(
            ILogger<AhliWarisDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger;
            _cacheService = cacheService;
            _queryProvider = queryProvider;
            _queryInterceptor = queryInterceptor;
        }

        public string NamaJenis => "AHLI_WARIS";

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting AhliWaris data for ID_Surat={ID_Surat}", idSurat);

            if (suratData.AhliWaris == null || suratData.AhliWaris.Waris == null || !suratData.AhliWaris.Waris.Any())
            {
                _logger.LogWarning("AhliWaris.Waris is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("AhliWaris.Waris is required for AHLI_WARIS surat");
            }

            try
            {
                // Validasi NIKWaris
                foreach (var waris in suratData.AhliWaris.Waris)
                {
                    if (string.IsNullOrWhiteSpace(waris.NamaWaris) || string.IsNullOrWhiteSpace(waris.NIKWaris))
                        throw new ArgumentException("NamaWaris and NIKWaris are required");
                    if (!IsValidNIK(waris.NIKWaris))
                        throw new ArgumentException($"Invalid NIKWaris format: {waris.NIKWaris}");
                }

                // Serialisasi AhliWarisData ke JSON
                string? additionalData = null;
                if (suratData.AhliWarisData != null)
                {
                    additionalData = JsonSerializer.Serialize(suratData.AhliWarisData, new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                }

                // Update AdditionalData di tabel Surat
                var queryUpdate = "UPDATE Surat SET AdditionalData = @AdditionalData WHERE ID_Surat = @ID_Surat";
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(queryUpdate, new { ID_Surat = idSurat, AdditionalData = additionalData }, transaction),
                    "UpdateSuratAdditionalData",
                    new { ID_Surat = idSurat }
                );

                // Insert Waris (batch insert)
                var queryWaris = _queryProvider.GetQuery("InsertAhliWaris");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(queryWaris, suratData.AhliWaris.Waris.Select(w => new
                    {
                        ID_Surat = idSurat,
                        w.NamaWaris,
                        w.NIKWaris,
                        w.HubunganWaris
                    }), transaction),
                    "InsertAhliWaris",
                    new { ID_Surat = idSurat, suratData.AhliWaris.Waris.Count }
                );

                _logger.LogInformation("Successfully inserted {WarisCount} waris and updated AdditionalData for ID_Surat={ID_Surat}",
                    suratData.AhliWaris.Waris.Count, idSurat);

                //await _cacheService.RemoveAsync(CacheKeys.Surat(idSurat));
                _logger.LogDebug("Cache removed for key={CacheKey}", CacheKeys.Surat(idSurat));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting AhliWaris data for ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting AhliWaris data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                // Hapus AdditionalData
                var queryUpdate = "UPDATE Surat SET AdditionalData = NULL WHERE ID_Surat = @ID_Surat";
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(queryUpdate, new { ID_Surat = idSurat }, transaction),
                    "ClearSuratAdditionalData",
                    new { ID_Surat = idSurat }
                );

                // Hapus Waris
                var queryWaris = _queryProvider.GetQuery("DeleteAhliWaris");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(queryWaris, new { ID_Surat = idSurat }, transaction),
                    "DeleteAhliWaris",
                    new { ID_Surat = idSurat }
                );

                //await _cacheService.RemoveAsync(CacheKeys.Surat(idSurat));
                _logger.LogDebug("Cache removed for key={CacheKey}", CacheKeys.Surat(idSurat));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting AhliWaris data for ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading AhliWaris data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            try
            {
                // Load Waris
                var queryWaris = _queryProvider.GetQuery("LoadAhliWaris");
                var warisList = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryAsync<Waris>(queryWaris, new { suratData.ID_Surat }),
                    "LoadAhliWaris",
                    new { suratData.ID_Surat }
                );
                suratData.AhliWaris = new AhliWaris { Waris = warisList.ToList() };

                // Load AdditionalData dan deserialisasi ke AhliWarisData
                var querySurat = "SELECT AdditionalData FROM Surat WHERE ID_Surat = @ID_Surat";
                var additionalData = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<string>(querySurat, new { suratData.ID_Surat }),
                    "LoadSuratAdditionalData",
                    new { suratData.ID_Surat }
                );

                if (!string.IsNullOrEmpty(additionalData))
                {
                    try
                    {
                        suratData.AhliWarisData = JsonSerializer.Deserialize<AhliWarisData>(additionalData);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize AdditionalData for ID_Surat={ID_Surat}", suratData.ID_Surat);
                        suratData.AhliWarisData = new AhliWarisData();
                    }
                }
                else
                {
                    suratData.AhliWarisData = new AhliWarisData();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading AhliWaris data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw;
            }
        }

        private bool IsValidNIK(string nik)
        {
            return !string.IsNullOrEmpty(nik) && nik.Length == 16 && nik.All(char.IsDigit);
        }
    }

    public static class CacheKeys
    {
        public static string Surat(int idSurat) => $"surat_{idSurat}";
    }
}
