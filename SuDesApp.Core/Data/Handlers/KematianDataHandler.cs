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
    public class KematianDataHandler : ISuratDataHandler
    {
        private readonly ILogger<KematianDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public KematianDataHandler(
            ILogger<KematianDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
        }

        public string NamaJenis => SuratConstants.KEMATIAN;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting Kematian data for ID_Surat={ID_Surat}", idSurat);

            if (suratData?.Kematian == null)
            {
                _logger.LogWarning("Kematian data is null for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Kematian data is required for KEMATIAN surat");
            }

            try
            {
                // Validasi data
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for Kematian: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                var query = _queryProvider.GetQuery("InsertKematian");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, new
                    {
                        ID_Surat = idSurat,
                        suratData.Kematian.HariKematian,
                        suratData.Kematian.TanggalKematian,
                        suratData.Kematian.PukulKematian,
                        suratData.Kematian.PenyebabKematian,
                        suratData.Kematian.TempatKematian,
                        suratData.Kematian.HubunganPelapor,
                        suratData.Kematian.NIKPelapor,
                        suratData.Kematian.NamaPelapor,
                        suratData.Kematian.AgamaPelapor,
                        suratData.Kematian.UmurPelapor,
                        suratData.Kematian.PekerjaanPelapor,
                        suratData.Kematian.AlamatPelapor
                    }, transaction),
                    "InsertKematian",
                    new { ID_Surat = idSurat }
                );


                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted Kematian data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting Kematian data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert Kematian data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting Kematian data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                var query = _queryProvider.GetQuery("DeleteKematian");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, new { ID_Surat = idSurat }, transaction),
                    "DeleteKematian",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted Kematian data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Kematian data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete Kematian data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading Kematian data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                var query = _queryProvider.GetQuery("LoadKematian");
                suratData.Kematian = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<KematianData>(
                        query, new { ID_Surat = suratData.ID_Surat }),
                    "LoadKematian",
                    new { suratData.ID_Surat }
                );

                if (suratData.Kematian == null)
                {
                    suratData.Kematian = new KematianData();
                    _logger.LogWarning("No Kematian data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                }

                _logger.LogInformation("Successfully loaded Kematian data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Kematian data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load Kematian data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }
    }
}
