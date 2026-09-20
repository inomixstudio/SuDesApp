using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading.Tasks;

namespace SuDesApp.Data.Handlers
{
    public class KenalLahirDataHandler : ISuratDataHandler
    {
        private readonly ILogger<KenalLahirDataHandler> _logger;
        private readonly ICacheService _cacheService;
        private readonly QueryProvider _queryProvider;
        private readonly QueryInterceptor _queryInterceptor;
        private readonly IWargaRepository _wargaRepository;

        public KenalLahirDataHandler(
            ILogger<KenalLahirDataHandler> logger,
            ICacheService cacheService,
            QueryProvider queryProvider,
            QueryInterceptor queryInterceptor,
            IWargaRepository wargaRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _queryProvider = queryProvider ?? throw new ArgumentNullException(nameof(queryProvider));
            _queryInterceptor = queryInterceptor ?? throw new ArgumentNullException(nameof(queryInterceptor));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
        }

        public string NamaJenis => SuratConstants.KENAL_LAHIR;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting KenalLahir data for ID_Surat={ID_Surat}", idSurat);

            if (suratData?.KenalLahir == null)
            {
                _logger.LogWarning("SuratData or KenalLahir data is null for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentNullException(nameof(suratData), "SuratData or KenalLahir data is null.");
            }

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            var data = suratData.KenalLahir;

            // Validate data before processing
            var validationErrors = await ValidateKenalLahirDataAsync(data);
            if (validationErrors.Any())
            {
                _logger.LogWarning("Validation failed for KenalLahir: ID_Surat={ID_Surat}, Errors={Errors}",
                    idSurat, string.Join("; ", validationErrors));
                throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
            }

            try
            {
                await EnsureChildColumnsAsync(connection);

                var sqliteConnection = connection as SqliteConnection;
                if (sqliteConnection == null)
                {
                    throw new InvalidOperationException("Connection must be a SqliteConnection");
                }

                // Process parents data
                int? idAyah = await ProcessParentDataAsync(data.Ayah!, "Ayah", sqliteConnection, transaction);
                int? idIbu = await ProcessParentDataAsync(data.Ibu!, "Ibu", sqliteConnection, transaction);

                // Validate parent-child relationship
                if (idAyah.HasValue && idIbu.HasValue && idAyah.Value == idIbu.Value)
                {
                    throw new ValidationException("NIK Ayah dan Ibu tidak boleh sama.");
                }

                // Get child data
                var (namaAnak, tanggalLahirAnak, tempatLahirAnak, jenisKelaminAnak) = GetChildData(data);

                // Insert to KenalLahir table
                const string insertQuery = @"
                    INSERT INTO KenalLahir (
                        ID_Surat, 
                        ID_Ayah, 
                        ID_Ibu, 
                        NamaAnak, 
                        TanggalLahirAnak, 
                        TempatLahirAnak, 
                        JenisKelaminAnak,
                        AlamatLengkapAnak,
                        LahirDi
                    ) VALUES (
                        @ID_Surat, 
                        @ID_Ayah, 
                        @ID_Ibu, 
                        @NamaAnak, 
                        @TanggalLahirAnak, 
                        @TempatLahirAnak, 
                        @JenisKelaminAnak,
                        @AlamatLengkapAnak,
                        @LahirDi
                    )";

                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(insertQuery, new
                    {
                        ID_Surat = idSurat,
                        ID_Ayah = idAyah,
                        ID_Ibu = idIbu,
                        NamaAnak = namaAnak,
                        TanggalLahirAnak = tanggalLahirAnak.Value.ToString("yyyy-MM-dd"),
                        TempatLahirAnak = tempatLahirAnak,
                        JenisKelaminAnak = jenisKelaminAnak,
                        AlamatLengkapAnak = data.AlamatLengkapAnak,
                        LahirDi = data.LahirDi
                    }, transaction),
                    "InsertKenalLahir",
                    new { ID_Surat = idSurat }
                );

                // Invalidate relevant caches
                await ClearRelatedCaches(idSurat);

                _logger.LogInformation("Successfully inserted KenalLahir data for ID_Surat={ID_Surat}, Nama Anak={NamaAnak}",
                    idSurat, namaAnak);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting KenalLahir data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to insert KenalLahir data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting KenalLahir data for ID_Surat={ID_Surat}", idSurat);

            if (transaction == null || transaction.Connection == null)
            {
                _logger.LogError("Transaction is null or disposed for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException("Transaction is required and must be active.", nameof(transaction));
            }

            try
            {
                const string deleteQuery = "DELETE FROM KenalLahir WHERE ID_Surat = @ID_Surat";

                await _queryInterceptor.ExecuteWithLogging(
                    () => connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction),
                    "DeleteKenalLahir",
                    new { ID_Surat = idSurat }
                );

                await ClearRelatedCaches(idSurat);
                _logger.LogInformation("Successfully deleted KenalLahir data for ID_Surat={ID_Surat}", idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting KenalLahir data for ID_Surat={ID_Surat}", idSurat);
                throw new DataAccessException($"Failed to delete KenalLahir data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading KenalLahir data for ID_Surat={ID_Surat}", suratData.ID_Surat);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                await EnsureChildColumnsAsync(connection);

                const string loadQuery = @"
                    SELECT 
                        kl.ID_Ayah,
                        kl.ID_Ibu,
                        kl.NamaAnak,
                        kl.TanggalLahirAnak,
                        kl.TempatLahirAnak,
                        kl.JenisKelaminAnak,
                        kl.AlamatLengkapAnak,
                        kl.LahirDi,
                        wa.Nama as NamaAyah,
                        wa.NIK as NIKAyah,
                        wa.TanggalLahir as TanggalLahirAyah,
                        wa.TempatLahir as TempatLahirAyah,
                        wa.Agama as AgamaAyah,
                        wa.Pekerjaan as PekerjaanAyah,
                        wa.Dusun as DusunAyah,
                        wa.Desa as DesaAyah,
                        wa.Kecamatan as KecamatanAyah,
                        wa.Kabupaten as KabupatenAyah,
                        wa.JenisKelamin as JenisKelaminAyah,
                        wa.StatusPerkawinan as StatusPerkawinanAyah,
                        wa.Kewarganegaraan as KewarganegaraanAyah,
                        wi.Nama as NamaIbu,
                        wi.NIK as NIKIbu,
                        wi.TanggalLahir as TanggalLahirIbu,
                        wi.TempatLahir as TempatLahirIbu,
                        wi.Agama as AgamaIbu,
                        wi.Pekerjaan as PekerjaanIbu,
                        wi.Dusun as DusunIbu,
                        wi.Desa as DesaIbu,
                        wi.Kecamatan as KecamatanIbu,
                        wi.Kabupaten as KabupatenIbu,
                        wi.JenisKelamin as JenisKelaminIbu,
                        wi.StatusPerkawinan as StatusPerkawinanIbu,
                        wi.Kewarganegaraan as KewarganegaraanIbu
                    FROM KenalLahir kl
                    LEFT JOIN Warga wa ON kl.ID_Ayah = wa.ID_Warga
                    LEFT JOIN Warga wi ON kl.ID_Ibu = wi.ID_Warga
                    WHERE kl.ID_Surat = @ID_Surat";

                var data = await _queryInterceptor.ExecuteWithLogging(
                    () => connection.QueryFirstOrDefaultAsync<KenalLahirDto>(loadQuery, new { ID_Surat = suratData.ID_Surat }),
                    "LoadKenalLahir",
                    new { suratData.ID_Surat }
                );

                if (data == null)
                {
                    _logger.LogWarning("No KenalLahir data found for ID_Surat={ID_Surat}", suratData.ID_Surat);
                    suratData.KenalLahir = new KenalLahirData();
                    return;
                }

                suratData.KenalLahir = new KenalLahirData
                {
                    Ayah = CreateParentData(data.ID_Ayah, data.NamaAyah!, data.NIKAyah!, data.TanggalLahirAyah,
                        data.TempatLahirAyah!, data.AgamaAyah!, data.PekerjaanAyah!, data.JenisKelaminAyah!,
                        data.StatusPerkawinanAyah!, data.KewarganegaraanAyah!, data.DusunAyah!, data.DesaAyah!,
                        data.KecamatanAyah!, data.KabupatenAyah!),
                    Ibu = CreateParentData(data.ID_Ibu, data.NamaIbu!, data.NIKIbu!, data.TanggalLahirIbu,
                        data.TempatLahirIbu!, data.AgamaIbu!, data.PekerjaanIbu!, data.JenisKelaminIbu!,
                        data.StatusPerkawinanIbu!, data.KewarganegaraanIbu!, data.DusunIbu!, data.DesaIbu!,
                        data.KecamatanIbu!, data.KabupatenIbu!),
                    NamaAnak = data.NamaAnak,
                    TanggalLahirAnak = data.TanggalLahirAnak,
                    TempatLahirAnak = data.TempatLahirAnak,
                    AlamatLengkapAnak = data.AlamatLengkapAnak,
                    LahirDi = data.LahirDi,
                    Anak = new AnakData
                    {
                        NamaAnak = data.NamaAnak,
                        TanggalLahir = data.TanggalLahirAnak.ToString("yyyy-MM-dd"),
                        JenisKelamin = data.JenisKelaminAnak
                    }
                };

                _logger.LogInformation("Successfully loaded KenalLahir data for ID_Surat={ID_Surat}, Nama Anak={NamaAnak}",
                    suratData.ID_Surat, suratData.KenalLahir.NamaAnak);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading KenalLahir data for ID_Surat={ID_Surat}", suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load KenalLahir data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        #region Helper Methods

        private async Task EnsureChildColumnsAsync(IDbConnection connection)
        {
            try
            {
                var tableExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KenalLahir'");
                if (tableExists == 0)
                    return;

                foreach (var columnName in new[] { "AlamatLengkapAnak", "LahirDi" })
                {
                    var columnExists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('KenalLahir') WHERE name = @columnName COLLATE NOCASE",
                        new { columnName }) > 0;

                    if (!columnExists)
                    {
                        _logger.LogInformation("Menambahkan kolom {Column} ke tabel KenalLahir (self-heal).", columnName);
                        await connection.ExecuteAsync($"ALTER TABLE [KenalLahir] ADD COLUMN [{columnName}] TEXT");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memastikan kolom anak ada di tabel KenalLahir (self-heal).");
            }
        }

        private async Task<int?> ProcessParentDataAsync(WargaData parent, string parentType, SqliteConnection connection, IDbTransaction transaction)
        {
            if (parent == null || string.IsNullOrWhiteSpace(parent.NIK))
                return null;

            var existingParent = await _wargaRepository.GetWargaByNikAsync(parent.NIK);
            int? parentId;

            if (existingParent == null || existingParent.ID_Warga <= 0)
            {
                parentId = await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(parent, connection, transaction);
                _logger.LogInformation("Saved {ParentType} data: NIK={NIK}, Name={Nama}, ID_Warga={ID_Warga}",
                    parentType, parent.NIK, parent.Nama, parentId);
            }
            else
            {
                parentId = existingParent.ID_Warga;
                _logger.LogInformation("Using existing {ParentType} data: NIK={NIK}, ID_Warga={ID_Warga}",
                    parentType, parent.NIK, parentId);
            }

            // Invalidate cache
            await _cacheService.RemoveAsync<SuratData>($"Warga_{parent.NIK}");
            await _cacheService.RemoveAsync<SuratData>($"Warga_Alamat_{parent.NIK}");

            return parentId;
        }

        private (string namaAnak, DateTime? tanggalLahirAnak, string tempatLahirAnak, string jenisKelaminAnak) GetChildData(KenalLahirData data)
        {
            // Prioritize direct child data over Anak object
            string? namaAnak = !string.IsNullOrWhiteSpace(data.NamaAnak)
                ? data.NamaAnak.Trim()
                : data.Anak?.NamaAnak?.Trim();

            DateTime? tanggalLahirAnak = data.TanggalLahirAnak.HasValue
                ? data.TanggalLahirAnak
                : DateTime.TryParse(data.Anak?.TanggalLahir, out var parsedTanggalLahir) ? parsedTanggalLahir : null;

            string? tempatLahirAnak = !string.IsNullOrWhiteSpace(data.TempatLahirAnak)
                ? data.TempatLahirAnak.Trim()
                : null;

            string? jenisKelaminAnak = !string.IsNullOrWhiteSpace(data.Anak?.JenisKelamin)
                ? data.Anak.JenisKelamin.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(namaAnak))
                throw new ValidationException("Nama anak wajib diisi.");

            if (!tanggalLahirAnak.HasValue)
                throw new ValidationException("Tanggal lahir anak wajib diisi.");

            return (namaAnak, tanggalLahirAnak, tempatLahirAnak, jenisKelaminAnak);
        }

        private async Task ClearRelatedCaches(int idSurat)
        {
            await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
            await _cacheService.RemoveAsync<SuratData>("Warga_All");
            await _cacheService.RemoveByPrefixAsync("Warga_Search_");
        }

        private WargaData CreateParentData(int? idParent, string nama, string nik, DateTime? tanggalLahir,
            string tempatLahir, string agama, string pekerjaan, string jenisKelamin, string statusPerkawinan,
            string kewarganegaraan, string dusun, string desa, string kecamatan, string kabupaten)
        {
            if (string.IsNullOrWhiteSpace(nama))
                return null!;

            return new WargaData
            {
                ID_Warga = idParent ?? 0,
                Nama = nama,
                NIK = nik,
                TanggalLahir = tanggalLahir?.ToString("yyyy-MM-dd"),
                TempatLahir = tempatLahir,
                Agama = agama,
                Pekerjaan = pekerjaan,
                JenisKelamin = jenisKelamin,
                StatusPerkawinan = statusPerkawinan,
                Kewarganegaraan = kewarganegaraan,
                Dusun = dusun,
                Desa = desa,
                Kecamatan = kecamatan,
                Kabupaten = kabupaten,
                AlamatLengkap = string.Join(" ", new[] { dusun, desa, kecamatan, kabupaten }.Where(c => !string.IsNullOrWhiteSpace(c)))
            };
        }

        private async Task<List<string>> ValidateKenalLahirDataAsync(KenalLahirData data)
        {
            var errors = new List<string>();

            // Validate child data
            var (namaAnak, tanggalLahirAnak, _, jenisKelaminAnak) = GetChildData(data);

            if (string.IsNullOrWhiteSpace(namaAnak))
                errors.Add("Nama Anak wajib diisi.");

            if (!tanggalLahirAnak.HasValue)
            {
                errors.Add("Tanggal Lahir Anak wajib diisi.");
            }
            else if (tanggalLahirAnak.Value > DateTime.Now)
            {
                errors.Add("Tanggal Lahir Anak tidak boleh di masa depan.");
            }

            // Validate child gender
            if (!string.IsNullOrWhiteSpace(jenisKelaminAnak))
            {
                var validGenders = new[] { "L", "P", "Laki-laki", "Perempuan" };
                if (!validGenders.Contains(jenisKelaminAnak))
                {
                    errors.Add("Jenis Kelamin Anak harus 'L', 'P', 'Laki-laki', atau 'Perempuan'.");
                }
            }

            // Validate parents
            var hasAyah = data.Ayah != null && !string.IsNullOrWhiteSpace(data.Ayah.NIK);
            var hasIbu = data.Ibu != null && !string.IsNullOrWhiteSpace(data.Ibu.NIK);

            if (!hasAyah && !hasIbu)
            {
                errors.Add("Minimal harus ada data Ayah atau Ibu.");
            }

            if (hasAyah)
            {
                errors.AddRange(ValidateParentData(data.Ayah!, "Ayah"));
            }

            if (hasIbu)
            {
                errors.AddRange(ValidateParentData(data.Ibu!, "Ibu"));
            }

            // Validate parents not the same
            if (hasAyah && hasIbu && data.Ayah.NIK == data.Ibu.NIK)
            {
                errors.Add("NIK Ayah dan Ibu tidak boleh sama.");
            }

            return errors;
        }

        private List<string> ValidateParentData(WargaData parent, string parentType)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(parent.NIK))
            {
                errors.Add($"NIK {parentType} wajib diisi.");
            }
            else if (!IsValidNIK(parent.NIK))
            {
                errors.Add($"NIK {parentType} tidak valid: harus 16 digit angka.");
            }

            if (string.IsNullOrWhiteSpace(parent.Nama))
            {
                errors.Add($"Nama {parentType} wajib diisi.");
            }

            if (!string.IsNullOrWhiteSpace(parent.TanggalLahir))
            {
                if (!DateTime.TryParse(parent.TanggalLahir, out var tglLahir))
                {
                    errors.Add($"Format Tanggal Lahir {parentType} tidak valid.");
                }
                else if (tglLahir > DateTime.Now.AddYears(-15))
                {
                    errors.Add($"{parentType} terlalu muda (minimal 15 tahun).");
                }
            }

            return errors;
        }

        private bool IsValidNIK(string nik)
        {
            return SuDesApp.Utilities.Validator.ValidateNik(nik, out _);
        }

        #endregion

        #region DTO Classes

        private class KenalLahirDto
        {
            public int? ID_Ayah { get; set; }
            public int? ID_Ibu { get; set; }
            public string? NamaAnak { get; set; }
            public DateTime TanggalLahirAnak { get; set; }
            public string? TempatLahirAnak { get; set; }
            public string? JenisKelaminAnak { get; set; }
            public string? AlamatLengkapAnak { get; set; }
            public string? LahirDi { get; set; }
            public string? NamaAyah { get; set; }
            public string? NIKAyah { get; set; }
            public DateTime? TanggalLahirAyah { get; set; }
            public string? TempatLahirAyah { get; set; }
            public string? AgamaAyah { get; set; }
            public string? PekerjaanAyah { get; set; }
            public string? JenisKelaminAyah { get; set; }
            public string? StatusPerkawinanAyah { get; set; }
            public string? KewarganegaraanAyah { get; set; }
            public string? DusunAyah { get; set; }
            public string? DesaAyah { get; set; }
            public string? KecamatanAyah { get; set; }
            public string? KabupatenAyah { get; set; }
            public string? NamaIbu { get; set; }
            public string? NIKIbu { get; set; }
            public DateTime? TanggalLahirIbu { get; set; }
            public string? TempatLahirIbu { get; set; }
            public string? AgamaIbu { get; set; }
            public string? PekerjaanIbu { get; set; }
            public string? JenisKelaminIbu { get; set; }
            public string? StatusPerkawinanIbu { get; set; }
            public string? KewarganegaraanIbu { get; set; }
            public string? DusunIbu { get; set; }
            public string? DesaIbu { get; set; }
            public string? KecamatanIbu { get; set; }
            public string? KabupatenIbu { get; set; }
        }

        #endregion
    }
}
