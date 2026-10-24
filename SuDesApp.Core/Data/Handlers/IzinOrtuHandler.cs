using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public class IzinOrtuHandler : ISuratDataHandler
    {
        private readonly ILogger _logger;
        private readonly ICacheService _cacheService;
        private readonly IWargaRepository _wargaRepository;

        public IzinOrtuHandler(
            ILogger<IzinOrtuHandler> logger,
            ICacheService cacheService,
            IWargaRepository wargaRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
        }

        public string NamaJenis => SuratConstants.IZIN_ORTU;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting IzinOrtu data for ID_Surat: {ID_Surat}", idSurat);

            if (suratData?.IzinOrtu == null)
            {
                _logger.LogWarning("SuratData or IzinOrtu data is null for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentNullException(nameof(suratData), "SuratData or IzinOrtu data is null.");
            }

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            var data = suratData.IzinOrtu;

            // ID_Surat baru dari hasil insert harus disebar ke data IzinOrtu
            // agar validasi di bawah (data.ID_Surat <= 0) tidak gagal.
            data.ID_Surat = idSurat;
            suratData.ID_Surat = idSurat;

            var validationErrors = ValidateIzinOrtuData(data);
            if (validationErrors.Any())
            {
                _logger.LogWarning("Validation failed for IzinOrtu: ID_Surat={ID_Surat}, Errors={Errors}",
                    idSurat, string.Join("; ", validationErrors));
                throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
            }

            try
            {
                const string insertQuery = @"
                    INSERT INTO IZIN (
                        ID_Surat,
                        ID_Warga_Anak,
                        NegaraTujuan,
                        NamaPT
                    ) VALUES (
                        @ID_Surat,
                        @ID_Warga_Anak,
                        @NegaraTujuan,
                        @NamaPT
                    )";

                await connection.ExecuteAsync(insertQuery, new
                {
                    ID_Surat = idSurat,
                    ID_Warga_Anak = data.ID_Anak,
                    NegaraTujuan = data.NegaraTujuan?.Trim(),
                    NamaPT = data.NamaPT?.Trim()
                }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                await _cacheService.RemoveAsync<SuratData>("IzinOrtu_All");

                _logger.LogInformation("Successfully inserted IzinOrtu data for ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}, NegaraTujuan={NegaraTujuan}",
                    idSurat, data.ID_Anak, data.NegaraTujuan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting IzinOrtu data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert IzinOrtu data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting IzinOrtu data for ID_Surat={ID_Surat}", idSurat);

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            try
            {
                const string deleteQuery = "DELETE FROM IZIN WHERE ID_Surat = @ID_Surat";

                await connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted IzinOrtu data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting IzinOrtu data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete IzinOrtu data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading IzinOrtu data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                const string loadQuery = @"
                    SELECT ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT
                    FROM IZIN
                    WHERE ID_Surat = @ID_Surat";

                var row = await connection.QueryFirstOrDefaultAsync<IzinOrtuRow>(loadQuery, new { ID_Surat = suratData.ID_Surat });

                if (row == null)
                {
                    _logger.LogWarning("No IzinOrtu data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    suratData.IzinOrtu = new IzinOrtuData();
                    return;
                }

                var izinData = new IzinOrtuData
                {
                    ID_Surat = row.ID_Surat,
                    ID_Anak = row.ID_Warga_Anak,
                    NegaraTujuan = row.NegaraTujuan,
                    NamaPT = row.NamaPT
                };

                await RestoreDisplayChildAsync(izinData);

                suratData.IzinOrtu = izinData;

                _logger.LogInformation("Successfully loaded IzinOrtu data for ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}",
                    suratData.ID_Surat, izinData.ID_Anak);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading IzinOrtu data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load IzinOrtu data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        private async Task RestoreDisplayChildAsync(IzinOrtuData izinData)
        {
            if (izinData.ID_Anak <= 0)
                return;

            try
            {
                var warga = await _wargaRepository.GetWargaByIdAsync(izinData.ID_Anak);
                if (warga == null)
                {
                    _logger.LogWarning("Warga anak dengan ID_Warga={ID_Warga} tidak ditemukan saat memuat data IzinOrtu", izinData.ID_Anak);
                    return;
                }

                izinData.NamaAnak = warga.Nama;
                izinData.NIKAnak = warga.NIK;
                izinData.TempatLahirAnak = warga.TempatLahir;
                izinData.TanggalLahirAnak = warga.TanggalLahir;
                izinData.JenisKelaminAnak = warga.JenisKelamin;
                izinData.AgamaAnak = warga.Agama;
                izinData.StatusPerkawinanAnak = warga.StatusPerkawinan;
                izinData.AlamatAnak = warga.AlamatLengkap;
                izinData.PekerjaanAnak = warga.Pekerjaan;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil data Warga anak untuk ID_Warga={ID_Warga}", izinData.ID_Anak);
            }
        }

        // Validation method yang sesuai dengan model IzinOrtuData
        private List<string> ValidateIzinOrtuData(IzinOrtuData data)
        {
            var errors = new List<string>();

            if (data == null)
            {
                errors.Add("Data Izin Orang Tua tidak boleh null.");
                return errors;
            }

            if (data.ID_Surat <= 0)
                errors.Add("ID_Surat harus valid.");

            if (data.ID_Anak <= 0)
                errors.Add("Anak (ID_Warga) harus dipilih.");

            if (string.IsNullOrWhiteSpace(data.NegaraTujuan))
                errors.Add("Negara Tujuan wajib diisi.");
            else if (data.NegaraTujuan.Trim().Length > 100)
                errors.Add("Negara Tujuan maksimal 100 karakter.");

            if (!string.IsNullOrWhiteSpace(data.NamaPT) && data.NamaPT.Trim().Length > 100)
                errors.Add("Nama PT/Sponsor maksimal 100 karakter.");

            return errors;
        }

        private class IzinOrtuRow
        {
            public int ID_Surat { get; set; }
            public int ID_Warga_Anak { get; set; }
            public string ?NegaraTujuan { get; set; }
            public string ?NamaPT { get; set; }
        }
    }
}
