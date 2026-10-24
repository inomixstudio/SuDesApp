using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public class IjinTinggalDataHandler : ISuratDataHandler
    {
        private readonly ILogger<IjinTinggalDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public IjinTinggalDataHandler(
            ILogger<IjinTinggalDataHandler> logger,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => SuratConstants.IJIN_TINGGAL;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);

            if (suratData == null || string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                _logger.LogWarning("SuratData or Keterangan is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("SuratData and Keterangan are required for IJIN_TINGGAL surat");
            }

            try
            {
                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for IJIN_TINGGAL: ID_Surat={ID_Surat}, Errors={Errors}",
                        idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                const string insertQuery = @"
                    INSERT INTO IjinTinggal (
                        ID_Surat,
                        DusunTujuan,
                        DesaTujuan,
                        KecamatanTujuan,
                        KabupatenTujuan,
                        NikPenanggungJawab,
                        NamaPenanggungJawab,
                        TglLahirPenanggungJawab,
                        PekerjaanPenanggungJawab
                    ) VALUES (
                        @ID_Surat,
                        @DusunTujuan,
                        @DesaTujuan,
                        @KecamatanTujuan,
                        @KabupatenTujuan,
                        @NikPenanggungJawab,
                        @NamaPenanggungJawab,
                        @TglLahirPenanggungJawab,
                        @PekerjaanPenanggungJawab
                    )";

                await connection.ExecuteAsync(insertQuery, new
                {
                    ID_Surat = idSurat,
                    DusunTujuan = suratData.DusunTujuan,
                    DesaTujuan = suratData.DesaTujuan,
                    KecamatanTujuan = suratData.KecTujuan,
                    KabupatenTujuan = suratData.KabTujuan,
                    NikPenanggungJawab = suratData.NikPenanggungJawab,
                    NamaPenanggungJawab = suratData.NamaPenanggungJawab,
                    TglLahirPenanggungJawab = suratData.TglLahirPenanggungJawab,
                    PekerjaanPenanggungJawab = suratData.PekerjaanPenanggungJawab
                }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert IJIN_TINGGAL data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                const string deleteQuery = "DELETE FROM IjinTinggal WHERE ID_Surat = @ID_Surat";
                await connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting IJIN_TINGGAL data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete IJIN_TINGGAL data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading IJIN_TINGGAL data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                const string loadQuery = @"
                    SELECT DusunTujuan, DesaTujuan, KecamatanTujuan, KabupatenTujuan,
                           NikPenanggungJawab, NamaPenanggungJawab, TglLahirPenanggungJawab, PekerjaanPenanggungJawab
                    FROM IjinTinggal
                    WHERE ID_Surat = @ID_Surat";

                var row = await connection.QueryFirstOrDefaultAsync<IjinTinggalRow>(loadQuery, new { ID_Surat = suratData.ID_Surat });
                if (row == null)
                {
                    _logger.LogWarning("No IJIN_TINGGAL data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    return;
                }

                suratData.DusunTujuan = row.DusunTujuan;
                suratData.DesaTujuan = row.DesaTujuan;
                suratData.KecTujuan = row.KecamatanTujuan;
                suratData.KabTujuan = row.KabupatenTujuan;
                suratData.NikPenanggungJawab = row.NikPenanggungJawab;
                suratData.NamaPenanggungJawab = row.NamaPenanggungJawab;
                suratData.TglLahirPenanggungJawab = row.TglLahirPenanggungJawab;
                suratData.PekerjaanPenanggungJawab = row.PekerjaanPenanggungJawab;

                _logger.LogInformation("Successfully loaded IJIN_TINGGAL data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading IJIN_TINGGAL data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load IJIN_TINGGAL data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        private class IjinTinggalRow
        {
            public string ?DusunTujuan { get; set; }
            public string ?DesaTujuan { get; set; }
            public string ?KecamatanTujuan { get; set; }
            public string ?KabupatenTujuan { get; set; }
            public string ?NikPenanggungJawab { get; set; }
            public string ?NamaPenanggungJawab { get; set; }
            public string ?TglLahirPenanggungJawab { get; set; }
            public string ?PekerjaanPenanggungJawab { get; set; }
        }
    }
}
