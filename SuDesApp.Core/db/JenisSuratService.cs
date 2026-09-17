using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SuDesApp.db
{
    // Kelas untuk operasi jenis surat
    public class JenisSuratService
    {
        private readonly AppConfig _config;
        private readonly ILogger<JenisSuratService> _logger;

        public JenisSuratService(AppConfig config, ILogger<JenisSuratService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Ambil jenis surat berdasarkan nama
        public async Task<JenisSurat> GetJenisSuratByNamaAsync(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
            {
                _logger.LogError("NamaJenis kosong di GetJenisSuratByNamaAsync");
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));
            }

            _logger.LogDebug("Mengambil JenisSurat: NamaJenis={NamaJenis}", namaJenis);
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            try
            {
                var jenisSurat = await connection.QueryFirstOrDefaultAsync<JenisSurat>(
                    "SELECT ID_Jenis, NamaJenis, KodeJenis FROM JenisSurat WHERE UPPER(NamaJenis) = @NamaJenis",
                    new { NamaJenis = namaJenis.ToUpperInvariant() });

                if (jenisSurat == null)
                {
                    _logger.LogWarning("Jenis surat {NamaJenis} tidak ditemukan.", namaJenis);
                    throw new DataRetrievalException($"Jenis surat '{namaJenis}' tidak ditemukan.");
                }

                _logger.LogDebug("JenisSurat diambil: NamaJenis={NamaJenis}, ID_Jenis={ID_Jenis}, KodeJenis={KodeJenis}",
                    jenisSurat.NamaJenis, jenisSurat.ID_Jenis, jenisSurat.KodeJenis);
                return jenisSurat;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil JenisSurat: NamaJenis={NamaJenis}", namaJenis);
                throw new DataRetrievalException($"Gagal mengambil JenisSurat untuk {namaJenis}.", ex);
            }
        }

        // Di dalam JenisSuratService.cs
        public async Task<string> GenerateNomorSuratAsync(string kodeJenis)
        {
            // Validasi input
            if (string.IsNullOrWhiteSpace(kodeJenis))
            {
                _logger.LogError("KodeJenis parameter is null or empty in GenerateNomorSuratAsync");
                throw new ArgumentNullException(nameof(kodeJenis), "Kode jenis surat tidak boleh kosong.");
            }

            _logger.LogInformation("Memulai generate nomor surat untuk KodeJenis: {KodeJenis}", kodeJenis);

            // Grup penomoran bersama & anggotanya kini dibaca dari JenisSuratConfig.json
            // (properti IsSharedNumbering) — bukan lagi daftar hardcode.

            // Establish database connection
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            try
            {
                await connection.OpenAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka koneksi database untuk generate nomor surat");
                throw new DataAccessException("Gagal terhubung ke database saat generate nomor surat", ex);
            }

            // Tentukan template dan cek apakah termasuk grup
            string templateName;
            bool isInGroup = false;
            try
            {
                templateName = kodeJenis.ToUpperInvariant() switch
                {
                    "SKD" => SuratTemplateNames.SKD_UMUM,
                    "DOM_WRG" => SuratTemplateNames.DOMISILI_WARGA,
                    "DOM_INS" => SuratTemplateNames.INSTANSI,
                    "SKU" => SuratTemplateNames.SKU,
                    "SKCK" => SuratTemplateNames.PENGANTAR_SKCK,
                    "IZIN" => SuratTemplateNames.IZIN_ORTU,
                    "SKTM" => SuratTemplateNames.SKTM,
                    "GRP_SAW" => SuratTemplateNames.GARAPAN_SAWAH,
                    "KEM" => SuratTemplateNames.KEMATIAN,
                    "BEDANAMA" => SuratTemplateNames.BEDANAMA,
                    "KENAL_LAHIR" => SuratTemplateNames.KENAL_LAHIR,
                    "AHLI_WARIS" => SuratTemplateNames.AHLI_WARIS,
                    "IJT" => SuratTemplateNames.IJIN_TINGGAL,
                    _ => throw new ArgumentException($"Kode jenis '{kodeJenis}' tidak dikenali")
                };
                isInGroup = _config.IsSharedNumbering(templateName);
                _logger.LogDebug("TemplateName: {TemplateName}, isInGroup: {IsInGroup}", templateName, isInGroup);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Kode jenis surat tidak valid: {KodeJenis}", kodeJenis);
                throw new ArgumentException($"Kode jenis surat '{kodeJenis}' tidak valid.", ex);
            }

            // Ambil format nomor
            string format;
            try
            {
                format = _config.GetSuratNumberFormat(templateName); // anggota grup bersama punya NomorFormat identik di config
                if (string.IsNullOrWhiteSpace(format))
                {
                    throw new InvalidOperationException($"Format nomor surat untuk template '{templateName}' tidak ditemukan");
                }
                _logger.LogDebug("Format nomor surat: {Format}", format);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan format nomor surat untuk template: {TemplateName}", templateName);
                throw new InvalidOperationException("Gagal mendapatkan format penomoran surat", ex);
            }

            // Tentukan nomor urut berikutnya untuk tahun saat ini
            int nextNumber = 1;
            string currentYear = DateTime.Now.Year.ToString();
            try
            {
                if (isInGroup)
                {
                    // Query untuk mencari nomor urut terakhir untuk semua jenis surat dalam grup.
                    // Anggota grup dinamis dari JenisSuratConfig.json → klausa IN dibangun runtime.
                    var anggota = _config.GetSharedNumberingNames().ToArray();
                    var placeholders = new List<string>(anggota.Length);
                    var dp = new DynamicParameters();
                    for (int i = 0; i < anggota.Length; i++)
                    {
                        placeholders.Add($"@j{i}");
                        dp.Add($"@j{i}", anggota[i]);
                    }
                    dp.Add("Tahun", currentYear);

                    var lastNumber = await connection.QueryFirstOrDefaultAsync<int?>(
                        $@"SELECT MAX(CAST(
                    SUBSTR(NomorSurat, 
                           INSTR(NomorSurat, '/') + 1, 
                           INSTR(SUBSTR(NomorSurat, INSTR(NomorSurat, '/') + 1), '/') - 1
                    ) AS INTEGER))
                  FROM Surat s
                  INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE js.NamaJenis IN ({string.Join(", ", placeholders)})
                      AND NomorSurat LIKE '%' || @Tahun",
                        dp);

                    if (lastNumber.HasValue)
                    {
                        nextNumber = lastNumber.Value + 1;
                        _logger.LogDebug("Nomor terakhir grup ditemukan: {LastNumber}, akan menggunakan: {NextNumber}", lastNumber, nextNumber);
                    }
                    else
                    {
                        _logger.LogDebug("Tidak ditemukan nomor surat sebelumnya untuk grup, mulai dari 1");
                    }
                }
                else
                {
                    // Query untuk mencari nomor urut terakhir berdasarkan jenis surat
                    var lastNumber = await connection.QueryFirstOrDefaultAsync<int?>(
                        @"SELECT MAX(CAST(
                    SUBSTR(NomorSurat, 
                           INSTR(NomorSurat, '/') + 1, 
                           INSTR(SUBSTR(NomorSurat, INSTR(NomorSurat, '/') + 1), '/') - 1
                    ) AS INTEGER))
                  FROM Surat s
                  INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                  WHERE js.KodeJenis = @KodeJenis 
                    AND NomorSurat LIKE '%' || @Tahun",
                        new { KodeJenis = kodeJenis, Tahun = currentYear });

                    if (lastNumber.HasValue)
                    {
                        nextNumber = lastNumber.Value + 1;
                        _logger.LogDebug("Nomor terakhir ditemukan: {LastNumber}, akan menggunakan: {NextNumber} untuk tahun {Tahun}", lastNumber, nextNumber, currentYear);
                    }
                    else
                    {
                        _logger.LogDebug("Tidak ditemukan nomor surat sebelumnya untuk KodeJenis: {KodeJenis} dan tahun {Tahun}, mulai dari 1", kodeJenis, currentYear);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mendapatkan nomor urut terakhir untuk KodeJenis: {KodeJenis}, tahun: {Tahun}", kodeJenis, currentYear);
                throw new DataRetrievalException("Gagal mendapatkan nomor surat terakhir", ex);
            }

            // Generate nomor surat dengan retry untuk memastikan unik
            const int maxRetries = 10;
            int retryCount = 0;
            string newNomor = string.Empty;

            while (retryCount < maxRetries)
            {
                try
                {
                    // Format nomor dengan nomor urut 3 digit dan tahun
                    newNomor = string.Format(format, nextNumber, "", DateTime.Now);
                    _logger.LogDebug("Mencoba nomor surat: {NomorSurat} (percobaan ke-{RetryCount})", newNomor, retryCount + 1);

                    // Cek apakah nomor sudah ada
                    var exists = await connection.QueryFirstOrDefaultAsync<int>(
                        "SELECT COUNT(*) FROM Surat WHERE NomorSurat = @NomorSurat",
                        new { NomorSurat = newNomor });

                    if (exists == 0)
                    {
                        _logger.LogInformation("Nomor surat unik berhasil digenerate: {NomorSurat}", newNomor);
                        return newNomor;
                    }

                    _logger.LogDebug("Nomor surat {NomorSurat} sudah ada, mencoba nomor berikutnya", newNomor);
                    nextNumber++;
                    retryCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saat generate nomor surat (percobaan ke-{RetryCount})", retryCount + 1);
                    retryCount++;
                    if (retryCount >= maxRetries)
                        break;
                    await Task.Delay(100);
                }
            }

            _logger.LogError("Gagal membuat nomor surat unik setelah {MaxRetries} percobaan", maxRetries);
            throw new InvalidOperationException($"Gagal menghasilkan nomor surat unik setelah {maxRetries} percobaan.");
        }

        // Ambil ID surat terakhir berdasarkan jenis surat
        public async Task<int?> GetLastSuratIdByTypeAsync(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
            {
                _logger.LogError("NamaJenis kosong di GetLastSuratIdByTypeAsync");
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));
            }

            _logger.LogDebug("Mengambil Last Surat ID: NamaJenis={NamaJenis}", namaJenis);
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            try
            {
                var result = await connection.QueryFirstOrDefaultAsync<int?>(
                    @"SELECT s.ID_Surat
                      FROM Surat s
                      INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                      WHERE UPPER(js.NamaJenis) = @NamaJenis
                      ORDER BY s.ID_Surat DESC
                      LIMIT 1",
                    new { NamaJenis = namaJenis.ToUpperInvariant() });

                _logger.LogDebug("Last Surat ID: NamaJenis={NamaJenis}, ID_Surat={ID_Surat}", namaJenis, result ?? null);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil ID surat terakhir: NamaJenis={NamaJenis}", namaJenis);
                throw new DataRetrievalException($"Gagal mengambil ID surat terakhir untuk jenis {namaJenis}.", ex);
            }
        }

        // Cek apakah nomor surat sudah ada
        public async Task<bool> IsNomorSuratExistsAsync(string nomorSurat)
        {
            if (string.IsNullOrWhiteSpace(nomorSurat))
                throw new ArgumentException("Nomor surat tidak boleh kosong.", nameof(nomorSurat));

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            try
            {
                var exists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Surat WHERE NomorSurat = @NomorSurat",
                    new { NomorSurat = nomorSurat });

                return exists > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa keberadaan nomor surat {NomorSurat}.", nomorSurat);
                throw new DataRetrievalException($"Gagal memeriksa keberadaan nomor surat {nomorSurat}.", ex);
            }
        }
    }

    // Model untuk JenisSurat
    public class JenisSurat
    {
        public int ID_Jenis { get; set; }
        public string NamaJenis { get; set; }
        public string KodeJenis { get; set; }
    }
}
