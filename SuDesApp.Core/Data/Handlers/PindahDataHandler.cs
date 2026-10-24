using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    /// <summary>
    /// Data khusus Surat Keterangan Pindah (SK_PINDAH): alamat tujuan pindah dan
    /// alasan pindah pada tabel Pindah. Data warganya tetap lewat tabel Warga
    /// (Surat.ID_Warga) seperti surat keterangan lain.
    /// </summary>
    public class PindahDataHandler : ISuratDataHandler
    {
        private readonly ILogger<PindahDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public PindahDataHandler(
            ILogger<PindahDataHandler> logger,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => SuratConstants.SK_PINDAH;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);

            if (suratData == null || string.IsNullOrWhiteSpace(suratData.AlamatTujuanPindah))
            {
                _logger.LogWarning("SuratData or AlamatTujuanPindah is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("SuratData and AlamatTujuanPindah are required for SK_PINDAH surat");
            }

            try
            {
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for SK_PINDAH: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                const string insertQuery = @"
                    INSERT INTO Pindah (ID_Surat, AlamatTujuanPindah, AlasanPindah)
                    VALUES (@ID_Surat, @AlamatTujuanPindah, @AlasanPindah)";

                await connection.ExecuteAsync(insertQuery, new
                {
                    ID_Surat = idSurat,
                    AlamatTujuanPindah = suratData.AlamatTujuanPindah,
                    AlasanPindah = suratData.AlasanPindah
                }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert SK_PINDAH data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                const string deleteQuery = "DELETE FROM Pindah WHERE ID_Surat = @ID_Surat";
                await connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting SK_PINDAH data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete SK_PINDAH data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading SK_PINDAH data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                const string loadQuery = @"
                    SELECT AlamatTujuanPindah, AlasanPindah
                    FROM Pindah
                    WHERE ID_Surat = @ID_Surat";

                var row = await connection.QueryFirstOrDefaultAsync<PindahRow>(loadQuery, new { ID_Surat = suratData.ID_Surat });
                if (row == null)
                {
                    _logger.LogWarning("No SK_PINDAH data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    return;
                }

                suratData.AlamatTujuanPindah = row.AlamatTujuanPindah;
                suratData.AlasanPindah = row.AlasanPindah;

                _logger.LogInformation("Successfully loaded SK_PINDAH data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SK_PINDAH data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load SK_PINDAH data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        private class PindahRow
        {
            public string? AlamatTujuanPindah { get; set; }
            public string? AlasanPindah { get; set; }
        }
    }
}
