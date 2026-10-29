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
    /// Data khusus Izin Keramaian (IZIN_KERAMAIAN): jenis kegiatan, tanggal, dan
    /// lokasi pelaksanaan pada tabel Keramaian. Penanggung jawab kegiatan adalah
    /// pemohonnya sendiri (tabel Warga lewat Surat.ID_Warga).
    /// </summary>
    public class KeramaianDataHandler : ISuratDataHandler
    {
        private readonly ILogger<KeramaianDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public KeramaianDataHandler(
            ILogger<KeramaianDataHandler> logger,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => SuratConstants.IZIN_KERAMAIAN;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);

            if (suratData == null ||
                string.IsNullOrWhiteSpace(suratData.JenisKegiatan) ||
                string.IsNullOrWhiteSpace(suratData.TanggalKegiatan) ||
                string.IsNullOrWhiteSpace(suratData.LokasiKegiatan))
            {
                _logger.LogWarning("Data kegiatan tidak lengkap untuk ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("JenisKegiatan, TanggalKegiatan, and LokasiKegiatan are required for IZIN_KERAMAIAN surat");
            }

            try
            {
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for IZIN_KERAMAIAN: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                const string insertQuery = @"
                    INSERT INTO Keramaian (ID_Surat, JenisKegiatan, TanggalKegiatan, LokasiKegiatan)
                    VALUES (@ID_Surat, @JenisKegiatan, @TanggalKegiatan, @LokasiKegiatan)";

                await connection.ExecuteAsync(insertQuery, new
                {
                    ID_Surat = idSurat,
                    JenisKegiatan = suratData.JenisKegiatan,
                    TanggalKegiatan = suratData.TanggalKegiatan,
                    LokasiKegiatan = suratData.LokasiKegiatan
                }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogDebug("Successfully inserted IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert IZIN_KERAMAIAN data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                const string deleteQuery = "DELETE FROM Keramaian WHERE ID_Surat = @ID_Surat";
                await connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogDebug("Successfully deleted IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete IZIN_KERAMAIAN data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                const string loadQuery = @"
                    SELECT JenisKegiatan, TanggalKegiatan, LokasiKegiatan
                    FROM Keramaian
                    WHERE ID_Surat = @ID_Surat";

                var row = await connection.QueryFirstOrDefaultAsync<KeramaianRow>(loadQuery, new { ID_Surat = suratData.ID_Surat });
                if (row == null)
                {
                    _logger.LogWarning("No IZIN_KERAMAIAN data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    return;
                }

                suratData.JenisKegiatan = row.JenisKegiatan;
                suratData.TanggalKegiatan = row.TanggalKegiatan;
                suratData.LokasiKegiatan = row.LokasiKegiatan;

                _logger.LogDebug("Successfully loaded IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading IZIN_KERAMAIAN data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load IZIN_KERAMAIAN data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        private class KeramaianRow
        {
            public string? JenisKegiatan { get; set; }
            public string? TanggalKegiatan { get; set; }
            public string? LokasiKegiatan { get; set; }
        }
    }
}
