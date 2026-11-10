using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SuDesApp.Configuration
{
    public interface IDatabaseInitializer
    {
        Task InitializeAsync();
        Task<DesaData?> GetInfoDesaAsync();
        Task<bool> ValidateSchemaAsync();
        Task<Dictionary<string, object>> GetDatabaseStatsAsync();
        Task CreateBackupAsync(string backupPath);
        Task RestoreBackupAsync(string backupPath);
    }

    public partial class DatabaseInitializer : IDatabaseInitializer
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseInitializer> _logger;
        private readonly IDesaRepository _desaRepository;
        private readonly IJenisSuratRepository _jenisSuratRepository;
        private readonly IWargaRepository _wargaRepository;
        private readonly ISuratRepository _suratRepository;

        // Backup/statistik dipindahkan ke DatabaseBackupService. Tiga method di
        // bawah ada hanya sebagai penerus supaya pemakil lama tidak perlu
        // diubah; logikanya tinggal di kelas itu.
        private readonly IDatabaseBackupService _backupService;

        // SCHEMA VERSION TRACKING
        private const string CURRENT_SCHEMA_VERSION = "2.0.0";

        public DatabaseInitializer(
            AppConfig config,
            ILogger<DatabaseInitializer> logger,
            IDesaRepository desaRepository,
            IJenisSuratRepository jenisSuratRepository,
            IWargaRepository wargaRepository,
            ISuratRepository suratRepository,
            IDatabaseBackupService backupService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Memulai inisialisasi database v{Version}...", CURRENT_SCHEMA_VERSION);

            try
            {
                using var connection = new SqliteConnection(_config.DatabaseConnectionString);
                await connection.OpenAsync();

                // 1. Buat tabel versi schema jika belum ada
                await CreateSchemaVersionTableAsync(connection);

                // 2. Get current version before any schema operations
                var currentVersion = await GetCurrentSchemaVersionAsync(connection);
                _logger.LogInformation("Current schema version: {CurrentVersion}, Target: {TargetVersion}",
                    currentVersion, CURRENT_SCHEMA_VERSION);

                // 3. ALWAYS execute base schema first to ensure tables exist
                await ExecuteDatabaseSchemaAsync(connection);

                // 4. Check if this is a fresh install (no previous version records or default version)
                var isFirstRun = await IsFirstRunAsync(connection, currentVersion);

                // 5. Perform migration only if needed and not a fresh install
                if (!isFirstRun && await ShouldMigrateSchema(currentVersion))
                {
                    await MigrateSchemaAsync(connection, currentVersion);
                }
                else if (isFirstRun)
                {
                    _logger.LogInformation("Fresh installation detected, skipping migrations");
                }

                // 6. Verify required columns exist (non-blocking)
                await EnsureRequiredColumnsAsync(connection);

                // 7. Inisialisasi tabel-tabel utama
                await _desaRepository.InitializeAsync();
                await _jenisSuratRepository.InitializeJenisSuratDataAsync();
                await _wargaRepository.InitializeWargaTableAsync();
                await _suratRepository.InitializeSuratIndexesAsync();

                // 8. Inisialisasi komponen baru
                await InitializeNewFeaturesAsync(connection);

                // 9. Validasi schema final - dengan retry jika gagal
                bool isValid = await ValidateSchemaWithRetryAsync(connection);
                if (!isValid)
                {
                    throw new InvalidOperationException("Schema validation failed after initialization and retry attempts");
                }

                // 10. Update versi schema hanya jika berhasil
                await UpdateSchemaVersionAsync(connection, CURRENT_SCHEMA_VERSION);

                _logger.LogInformation("Inisialisasi database berhasil v{Version}", CURRENT_SCHEMA_VERSION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal inisialisasi database");
                throw;
            }
        }

    }
}
