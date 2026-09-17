using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public class SKDUmumDataHandler : ISuratDataHandler
    {
        private readonly ILogger<SKDUmumDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public SKDUmumDataHandler(
            ILogger<SKDUmumDataHandler> logger,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => SuratConstants.SKD_UMUM;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentNullException(nameof(suratData));
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                _logger.LogWarning("Keterangan is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Keterangan is required for SKD_UMUM surat");
            }

            try
            {
                // Validate the complete SuratData object
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for SKD_UMUM: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                // Clear cache for this surat
                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));

                // For SKD_UMUM, data is stored in Surat.Keterangan field
                // No additional tables need to be updated

                _logger.LogInformation("Successfully processed SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to process SKD_UMUM data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Cleaning up SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                // Clear cache for this surat
                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));

                // No additional tables to clean up for SKD_UMUM

                _logger.LogInformation("Successfully cleaned up SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up SKD_UMUM data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to clean up SKD_UMUM data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading SKD_UMUM data for ID_Surat={ID_Surat}", suratData?.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null while loading SKD_UMUM data");
                throw new ArgumentNullException(nameof(suratData));
            }

            // For SKD_UMUM, all data is already in SuratData.Keterangan
            // No additional data needs to be loaded

            _logger.LogInformation("Successfully loaded SKD_UMUM data for ID_Surat={ID_Surat}", suratData.ID_Surat);
        }
    }
}
