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
    public class InstansiDataHandler : ISuratDataHandler
    {
        private readonly ILogger<InstansiDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public InstansiDataHandler(
            ILogger<InstansiDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
        }

        public string NamaJenis => SuratConstants.INSTANSI;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting Instansi data for ID_Surat={ID_Surat}", idSurat);

            if (suratData?.Instansi == null || string.IsNullOrWhiteSpace(suratData.Instansi.NamaInstansi))
            {
                _logger.LogWarning("Instansi data is invalid for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Instansi data and NamaInstansi are required for INSTANSI surat");
            }

            try
            {
                // Validasi data
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for Instansi: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                var query = _queryProvider.GetQuery("InsertInstansi");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, new
                    {
                        ID_Surat = idSurat,
                        suratData.Instansi.NamaInstansi,
                        suratData.Instansi.AlamatInstansi,
                        suratData.Instansi.PimpinanInstansi
                    }, transaction),
                    "InsertInstansi",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted Instansi data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting Instansi data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert Instansi data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting Instansi data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                var query = _queryProvider.GetQuery("DeleteInstansi");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(query, new { ID_Surat = idSurat }, transaction),
                    "DeleteInstansi",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted Instansi data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Instansi data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete Instansi data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading Instansi data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                var query = _queryProvider.GetQuery("LoadInstansi");
                suratData.Instansi = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<Instansi>(
                        query, new { ID_Surat = suratData.ID_Surat }),
                    "LoadInstansi",
                    new { suratData.ID_Surat }
                );

                if (suratData.Instansi == null)
                {
                    suratData.Instansi = new Instansi();
                    _logger.LogWarning("No Instansi data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                }

                _logger.LogInformation("Successfully loaded Instansi data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Instansi data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load Instansi data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }
    }
}
