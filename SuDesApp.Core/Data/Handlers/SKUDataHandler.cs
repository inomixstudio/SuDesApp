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
    public class SKUDto
    {
        public string ?BidangUsaha { get; set; }
        public int SejakTahun { get; set; }
        public string ?LokasiUsaha { get; set; }
    }

    public class SKUDataHandler : ISuratDataHandler
    {
        private readonly ILogger<SKUDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public SKUDataHandler(
            ILogger<SKUDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
        }

        public string NamaJenis => SuratConstants.SKU;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting SKU data for ID_Surat={ID_Surat}", idSurat);

            if (suratData?.SKU == null)
            {
                _logger.LogWarning("SuratData or SKU data is null for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentNullException(nameof(suratData), "SuratData or SKU data is null.");
            }

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            var data = suratData.SKU;

            var validationErrors = ValidateSKUData(data);
            if (validationErrors.Any())
            {
                _logger.LogWarning("Validation failed for SKU: ID_Surat={ID_Surat}, Errors={Errors}",
                    idSurat, string.Join("; ", validationErrors));
                throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
            }

            try
            {
                const string insertQuery = @"
                    INSERT INTO SKU (
                        ID_Surat, 
                        BidangUsaha, 
                        SejakTahun, 
                        LokasiUsaha
                    ) VALUES (
                        @ID_Surat, 
                        @BidangUsaha, 
                        @SejakTahun, 
                        @LokasiUsaha
                    )";

                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(insertQuery, new
                    {
                        ID_Surat = idSurat,
                        BidangUsaha = data.BidangUsaha?.Trim(),
                        SejakTahun = data.SejakTahun,  // int langsung, tidak perlu convert
                        LokasiUsaha = data.LokasiUsaha?.Trim()
                    }, transaction),
                    "InsertSKU",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                await _cacheService.RemoveAsync<SuratData>("SKU_All");

                _logger.LogInformation("Successfully inserted SKU data for ID_Surat={ID_Surat}, BidangUsaha={BidangUsaha}, SejakTahun={SejakTahun}",
                    idSurat, data.BidangUsaha, data.SejakTahun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting SKU data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert SKU data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting SKU data for ID_Surat={ID_Surat}", idSurat);

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            try
            {
                const string deleteQuery = "DELETE FROM SKU WHERE ID_Surat = @ID_Surat";

                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction),
                    "DeleteSKU",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted SKU data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting SKU data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete SKU data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading SKU data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                const string loadQuery = @"
                    SELECT 
                        BidangUsaha,
                        SejakTahun,
                        LokasiUsaha
                    FROM SKU 
                    WHERE ID_Surat = @ID_Surat";

                var data = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<SKUDto>(loadQuery, new { ID_Surat = suratData.ID_Surat }),
                    "LoadSKU",
                    new { suratData.ID_Surat }
                );

                if (data == null)
                {
                    _logger.LogWarning("No SKU data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    suratData.SKU = new SKUData();
                    return;
                }

                // Map dengan tipe data yang benar
                suratData.SKU = new SKUData
                {
                    BidangUsaha = data.BidangUsaha ?? string.Empty,
                    SejakTahun = data.SejakTahun,  // int ke int, langsung
                    LokasiUsaha = data.LokasiUsaha
                };

                _logger.LogInformation("Successfully loaded SKU data for ID_Surat={ID_Surat}, BidangUsaha={BidangUsaha}, SejakTahun={SejakTahun}",
                    suratData.ID_Surat, data.BidangUsaha, data.SejakTahun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SKU data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load SKU data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        // Validation method yang sesuai dengan model SKUData (int SejakTahun)
        private List<string> ValidateSKUData(SKUData skuData)
        {
            var errors = new List<string>();

            if (skuData == null)
            {
                errors.Add("Data SKU tidak boleh null.");
                return errors;
            }

            // Validasi BidangUsaha
            if (string.IsNullOrWhiteSpace(skuData.BidangUsaha))
            {
                errors.Add("Bidang usaha wajib diisi.");
            }
            else if (skuData.BidangUsaha.Trim().Length > 100)
            {
                errors.Add("Bidang usaha maksimal 100 karakter.");
            }

            // Validasi SejakTahun (int)
            if (skuData.SejakTahun <= 0)
            {
                errors.Add("Sejak tahun harus diisi dengan nilai yang valid.");
            }
            else if (skuData.SejakTahun < 1900)
            {
                errors.Add("Sejak tahun tidak boleh kurang dari 1900.");
            }
            else if (skuData.SejakTahun > DateTime.Now.Year)
            {
                errors.Add($"Sejak tahun tidak boleh lebih dari tahun sekarang ({DateTime.Now.Year}).");
            }

            // Validasi LokasiUsaha (optional)
            if (!string.IsNullOrWhiteSpace(skuData.LokasiUsaha) && skuData.LokasiUsaha.Trim().Length > 200)
            {
                errors.Add("Lokasi usaha maksimal 200 karakter.");
            }

            return errors;
        }

        // Helper method untuk compatibility dengan interface lama
        private bool IsValidSKUData(SKUData skuData)
        {
            return skuData != null &&
                   !string.IsNullOrWhiteSpace(skuData.BidangUsaha) &&
                   skuData.SejakTahun > 1900 &&
                   skuData.SejakTahun <= DateTime.Now.Year;
        }
    }
}
