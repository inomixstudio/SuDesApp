using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public class DomisiliWargaDataHandler : ISuratDataHandler
    {
        private readonly ILogger<DomisiliWargaDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public DomisiliWargaDataHandler(
            ILogger<DomisiliWargaDataHandler> logger,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => SuratConstants.DOMISILI_WARGA;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);

            if (suratData == null || string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                _logger.LogWarning("SuratData or Keterangan is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("SuratData and Keterangan are required for DOMISILI_WARGA surat");
            }

            try
            {
                // Validasi data
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for DOMISILI_WARGA: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                // Data disimpan di Surat.Keterangan, tidak ada tabel tambahan
                _logger.LogInformation("Successfully inserted DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert DOMISILI_WARGA data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                // Tidak ada tabel tambahan untuk dihapus
                _logger.LogInformation("Successfully deleted DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting DOMISILI_WARGA data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete DOMISILI_WARGA data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading DOMISILI_WARGA data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            // Data sudah ada di SuratData.Keterangan
            _logger.LogInformation("Successfully loaded DOMISILI_WARGA data for ID_Surat={ID_Surat}", suratData.ID_Surat);
        }
    }
}
