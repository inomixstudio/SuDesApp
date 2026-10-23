using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public class GarapanDataHandler : ISuratDataHandler
    {
        private readonly ILogger<GarapanDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public GarapanDataHandler(
            ILogger<GarapanDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
        }

        public string NamaJenis => SuratConstants.GARAPAN_SAWAH;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting Garapan data for ID_Surat={ID_Surat}", idSurat);

            if (suratData.RincianGarapans == null || !suratData.RincianGarapans.Any())
            {
                // Mode DRAFT: surat boleh baru berisi NIK/Nama (rincian tanah menyusul
                // saat surat dilengkapi lewat Edit) — jangan gagalkan insert.
                if (string.Equals(suratData.Status, "Draft", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "RincianGarapans kosong untuk DRAFT ID_Surat={ID_Surat}; lewati insert rincian.", idSurat);
                    return;
                }
                _logger.LogWarning("RincianGarapans is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("RincianGarapans is required for GARAPAN_SAWAH surat");
            }

            try
            {
                // Validasi data sebelum insert
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for Garapan: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                var query = _queryProvider.GetQuery("InsertGarapan");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, suratData.RincianGarapans.Select(item => new
                    {
                        ID_Surat = idSurat,
                        item.Luas,
                        item.Lokasi,
                        item.PemilikTanah,
                        item.NomorPersil,
                        item.KeteranganGarapan
                    }), transaction),
                    "InsertGarapan",
                    new { ID_Surat = idSurat, ItemCount = suratData.RincianGarapans.Count }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted {ItemCount} garapan items for ID_Surat={ID_Surat}",
                    suratData.RincianGarapans.Count, idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting Garapan data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert Garapan data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting Garapan data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                var query = _queryProvider.GetQuery("DeleteGarapan");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, new { ID_Surat = idSurat }, transaction),
                    "DeleteGarapan",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted Garapan data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Garapan data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete Garapan data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading Garapan data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            try
            {
                var query = _queryProvider.GetQuery("LoadGarapan");
                var items = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryAsync<GarapanData>(query, new { suratData.ID_Surat }),
                    "LoadGarapan",
                    new { suratData.ID_Surat }
                );

                suratData.RincianGarapans = items.ToList();
                _logger.LogInformation("Successfully loaded {ItemCount} garapan items for ID_Surat={ID_Surat}",
                    suratData.RincianGarapans.Count, suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Garapan data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load Garapan data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }
    }
}
