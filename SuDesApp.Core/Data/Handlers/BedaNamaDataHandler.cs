using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.Data;
using System.Linq;

namespace SuDesApp.Data.Handlers
{
    public class BedaNamaDataHandler : ISuratDataHandler
    {
        private readonly ILogger<BedaNamaDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;

        public BedaNamaDataHandler(
            ILogger<BedaNamaDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
        }

        public string NamaJenis => SuratConstants.BEDANAMA;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting BedaNama data for ID_Surat={ID_Surat}", idSurat);

            var data = suratData?.BedaNama ?? new BedaNamaData();
            var wargaKK = suratData?.WargaKK;

            if (data.ID_Warga <= 0 && suratData?.Warga != null)
                data.ID_Warga = suratData.Warga.ID_Warga;

            try
            {
                var query = _queryProvider.GetQuery("InsertBedaNama");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(
                        query,
                        new
                        {
                            ID_Surat = idSurat,
                            ID_Warga = data.ID_Warga,
                            SumberDataKoreksi = data.SumberDataKoreksi?.Trim(),
                            SumberDataKeliru = data.SumberDataKeliru?.Trim(),
                            AlasanPerbedaan = data.AlasanPerbedaan?.Trim(),
                            NIK2 = data.NIK2 ?? wargaKK?.NIK,
                            Nama2 = data.Nama2 ?? wargaKK?.Nama,
                            TempatLahir2 = data.TempatLahir2 ?? wargaKK?.TempatLahir,
                            TanggalLahir2 = data.TanggalLahir2 ?? wargaKK?.TanggalLahir,
                            JenisKelamin2 = data.JenisKelamin2 ?? wargaKK?.JenisKelamin,
                            Dusun2 = data.Dusun2 ?? wargaKK?.Dusun,
                            Desa2 = data.Desa2 ?? wargaKK?.Desa,
                            Kecamatan2 = data.Kecamatan2 ?? wargaKK?.Kecamatan,
                            Kabupaten2 = data.Kabupaten2 ?? wargaKK?.Kabupaten
                        },
                        transaction),
                    "InsertBedaNama",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted BedaNama data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting BedaNama data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert BedaNama data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting BedaNama data for ID_Surat={ID_Surat}", idSurat);

            try
            {
                var query = _queryProvider.GetQuery("DeleteBedaNama");
                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(
                        query,
                        new { ID_Surat = idSurat },
                        transaction),
                    "DeleteBedaNama",
                    new { ID_Surat = idSurat }
                );

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted BedaNama data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting BedaNama data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete BedaNama data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading BedaNama data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            try
            {
                var query = _queryProvider.GetQuery("LoadBedaNama");
                var result = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<BedaNamaData>(
                        query,
                        new { ID_Surat = suratData.ID_Surat }),
                    "LoadBedaNama",
                    new { suratData.ID_Surat }
                );

                if (result != null)
                {
                    suratData.BedaNama = result;

                    // Restore warga kedua agar bisa diisi ulang di form dan dirender di PDF
                    var wargaKK = new WargaData
                    {
                        NIK = result.NIK2,
                        Nama = result.Nama2,
                        TempatLahir = result.TempatLahir2,
                        TanggalLahir = result.TanggalLahir2,
                        JenisKelamin = result.JenisKelamin2,
                        Dusun = result.Dusun2,
                        Desa = result.Desa2,
                        Kecamatan = result.Kecamatan2,
                        Kabupaten = result.Kabupaten2
                    };
                    wargaKK.AlamatLengkap = BuildAlamatLengkap(wargaKK.Dusun!, wargaKK.Desa!, wargaKK.Kecamatan!, wargaKK.Kabupaten!);
                    suratData.WargaKK = wargaKK;

                    // Source labels untuk PDF ("Data di: ...")
                    suratData.DataSource1 = result.SumberDataKoreksi;
                    suratData.DataSource2 = result.SumberDataKeliru;
                }
                else
                {
                    suratData.BedaNama = new BedaNamaData();
                    _logger.LogWarning("No BedaNama data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                }

                _logger.LogInformation("Successfully loaded BedaNama data for ID_Surat={ID_Surat}", suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading BedaNama data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load BedaNama data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        public static string BuildAlamatLengkap(string dusun, string desa, string kecamatan, string kabupaten)
        {
            var parts = new[]
            {
                (dusun ?? string.Empty).Trim(),
                (desa ?? string.Empty).Trim(),
                (kecamatan ?? string.Empty).Trim(),
                (kabupaten ?? string.Empty).Trim()
            };

            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}
