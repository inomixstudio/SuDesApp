using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IWargaRepository
    {
        Task InitializeWargaTableAsync();
        Task<int> AddOrUpdateWargaAsync(WargaData wargaData, SqliteConnection? existingConnection = null, IDbTransaction? existingTransaction = null);
        Task<WargaData> GetWargaByIdAsync(int idWarga);
        Task<WargaData> GetWargaByNikAsync(string nik, SqliteConnection? connection = null, IDbTransaction? transaction = null);

        /// <summary>
        /// Ambil banyak warga sekaligus lewat daftar NIK dengan satu query
        /// (bukan satu query per NIK), dipakai penyusunan lampiran SK yang
        /// bisa memuat puluhan nama. NIK yang tidak ada dilewati diam-diam.
        /// </summary>
        Task<List<WargaData>> AmbilDaftarByNikAsync(IEnumerable<string> daftarNik, CancellationToken ct = default);

        Task<IEnumerable<WargaData>> GetAllWargaAsync();

        /// <summary>
        /// Jumlah warga terdata — dihitung di database (COUNT) tanpa memuat seluruh
        /// baris, dipakai oleh ringkasan Beranda.
        /// </summary>
        Task<int> CountWargaAsync();
        Task<IEnumerable<WargaData>> SearchWargaAsync(string searchTerm);
        Task<IReadOnlyList<WargaData>> CariWargaUntukPerangkatAsync(string searchTerm, int batas = 20);
        Task<bool> DeleteWargaAsync(int id);
        Task<string> GetAlamatByNikAsync(string nik);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData);
        Task<int> GetOrCreateDummyWargaAsync(SqliteConnection connection, IDbTransaction transaction);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection connection, IDbTransaction transaction);

        /// <summary>
        /// Ringkasan angka kependudukan dalam satu perhitungan (status, jenis
        /// kelamin, KK, per RT). Dipakai API /statistik dan laporan penduduk.
        /// </summary>
        Task<WargaStatistikRingkasan> GetStatistikWargaAsync(CancellationToken cancellationToken = default);

        /// <summary>Daftar warga satu halaman; baris dummy (Instansi/Kematian) otomatis tersaring.</summary>
        Task<WargaPagedResult> GetWargaPageAsync(WargaFilter filter, CancellationToken cancellationToken = default);

        /// <summary>Seluruh warga yang cocok filter, tanpa paginasi — untuk ekspor.</summary>
        Task<List<WargaData>> GetWargaForExportAsync(WargaFilter filter, CancellationToken cancellationToken = default);

        /// <summary>Ubah status warga beserta tanggal &amp; keterangan mutasinya. Status tidak dikenal ditolak.</summary>
        Task<bool> UbahStatusWargaAsync(int idWarga, string status, string? tanggalStatus, string? keterangan, CancellationToken cancellationToken = default);

        /// <summary>Simpan (insert/update) kepala keluarga &amp; alamat satu Kartu Keluarga.</summary>
        Task SaveKartuKeluargaAsync(KartuKeluargaData kartuKeluarga, CancellationToken cancellationToken = default);

        /// <summary>Baca satu Kartu Keluarga (JumlahAnggota dihitung dari tabel Warga); null bila belum ada.</summary>
        Task<KartuKeluargaData?> GetKartuKeluargaAsync(string noKk, CancellationToken cancellationToken = default);

        /// <summary>Anggota satu Kartu Keluarga, urut nama; baris dummy tersaring.</summary>
        Task<List<WargaData>> GetAnggotaKeluargaAsync(string noKk, CancellationToken cancellationToken = default);

        /// <summary>Daftar nilai unik RT/RW/dusun untuk dropdown filter wilayah halaman Data Warga.</summary>
        Task<IEnumerable<string>> GetDaftarNilaiWilayahAsync(CancellationToken cancellationToken = default);

        /// <summary>Jumlah surat per warga (ID_Warga → jumlah) — kolom "Jumlah Surat" pada ekspor.</summary>
        Task<Dictionary<int, int>> GetJumlahSuratSemuaWargaAsync(CancellationToken cancellationToken = default);
    }

    public partial class WargaRepository : IWargaRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ICacheService _cacheService;
        private readonly ILogger<WargaRepository> _logger;
        private readonly AppConfig _config;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private const string DummyNikInstansi = "9999999999999999";
        private const string DummyNikKematian = "0000000000000000";

        public WargaRepository(
            SqliteConnection connection,
            ICacheService cacheService,
            AppConfig config,
            ILogger<WargaRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeWargaTableAsync()
        {
            _logger.LogInformation("Memulai inisialisasi tabel Warga...");
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync();

                await EnsureWargaSchemaAsync();

                var wargaTableCount = await _connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Warga'");

                if (wargaTableCount == 0)
                {
                    await _connection.ExecuteAsync(@"
                        CREATE TABLE Warga (
                            ID_Warga INTEGER PRIMARY KEY AUTOINCREMENT,
                            NIK TEXT UNIQUE,
                            Nama TEXT NOT NULL,
                            TempatLahir TEXT,
                            TanggalLahir TEXT,
                            JenisKelamin TEXT,
                            Agama TEXT,
                            StatusPerkawinan TEXT,
                            Pekerjaan TEXT,
                            Dusun TEXT,
                            RT TEXT,
                            RW TEXT,
                            Desa TEXT,
                            Kecamatan TEXT,
                            Kabupaten TEXT,
                            Pendidikan TEXT,
                            Kewarganegaraan TEXT,
                            GolonganDarah TEXT,
                            NomorHP TEXT,
                            StatusWarga TEXT,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        )");
                    _logger.LogInformation("Tabel Warga dibuat.");

                    await _connection.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK)");
                    _logger.LogInformation("Indeks idx_warga_nik dibuat.");

                    await _connection.ExecuteAsync(
                        "CREATE TRIGGER IF NOT EXISTS tr_warga_updated AFTER UPDATE ON Warga " +
                        "FOR EACH ROW BEGIN " +
                        "UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga; " +
                        "END;");
                    _logger.LogInformation("Trigger update timestamp dibuat.");
                }

                await EnsureKartuKeluargaTableAsync();

                _logger.LogInformation("Inisialisasi tabel Warga selesai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menginisialisasi tabel Warga");
                throw new DataAccessException("Gagal menginisialisasi tabel Warga", ex);
            }
        }

        // Migrasi skema: pastikan kolom yang dibutuhkan model baru ada di tabel Warga legacy
        private async Task EnsureWargaSchemaAsync()
        {
            var tableExists = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Warga';");
            if (tableExists == 0) return;

            var existingColumns = new List<string>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Warga);";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            var migrations = new (string Name, string Ddl)[]
            {
                ("Dusun", "ALTER TABLE Warga ADD COLUMN Dusun TEXT NULL;"),
                ("Desa", "ALTER TABLE Warga ADD COLUMN Desa TEXT NULL;"),
                ("Kecamatan", "ALTER TABLE Warga ADD COLUMN Kecamatan TEXT NULL;"),
                ("Kabupaten", "ALTER TABLE Warga ADD COLUMN Kabupaten TEXT NULL;"),
                ("RT", "ALTER TABLE Warga ADD COLUMN RT TEXT NULL;"),
                ("RW", "ALTER TABLE Warga ADD COLUMN RW TEXT NULL;"),
                ("StatusWarga", "ALTER TABLE Warga ADD COLUMN StatusWarga TEXT NULL;"),
                ("CreatedAt", "ALTER TABLE Warga ADD COLUMN CreatedAt TEXT NULL;"),
                ("UpdatedAt", "ALTER TABLE Warga ADD COLUMN UpdatedAt TEXT NULL;"),
                ("GolonganDarah", "ALTER TABLE Warga ADD COLUMN GolonganDarah TEXT NULL;"),
                ("NomorHP", "ALTER TABLE Warga ADD COLUMN NomorHP TEXT NULL;"),
                ("NamaAyah", "ALTER TABLE Warga ADD COLUMN NamaAyah TEXT NULL;"),
                ("NamaIbu", "ALTER TABLE Warga ADD COLUMN NamaIbu TEXT NULL;"),
                ("NoKK", "ALTER TABLE Warga ADD COLUMN NoKK TEXT NULL;"),
                ("AlamatDetail", "ALTER TABLE Warga ADD COLUMN AlamatDetail TEXT NULL;"),
                ("TanggalStatus", "ALTER TABLE Warga ADD COLUMN TanggalStatus TEXT NULL;"),
                ("KeteranganWarga", "ALTER TABLE Warga ADD COLUMN KeteranganWarga TEXT NULL;"),
                ("StatusKeluarga", "ALTER TABLE Warga ADD COLUMN StatusKeluarga TEXT NULL;")
            };

            foreach (var migration in migrations)
            {
                if (existingColumns.Any(c => c.Equals(migration.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                await _connection.ExecuteAsync(migration.Ddl, transaction: null);
                _logger.LogInformation("Migrasi Warga: kolom {Column} ditambahkan.", migration.Name);
            }

            // Baris lama tidak punya nilai status bertanggal; isi kosong agar
            // pembaca model baru tidak pernah mendapat null pada data lama.
            // Status warga yang belum pernah diisi berarti warga tetap tinggal.
            await _connection.ExecuteAsync("UPDATE Warga SET TanggalStatus = '' WHERE TanggalStatus IS NULL");
            await _connection.ExecuteAsync("UPDATE Warga SET KeteranganWarga = '' WHERE KeteranganWarga IS NULL");
            await _connection.ExecuteAsync(
                "UPDATE Warga SET StatusWarga = 'AKTIF' WHERE StatusWarga IS NULL OR StatusWarga = ''");

            await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK)");
            await _connection.ExecuteAsync(
                "CREATE TRIGGER IF NOT EXISTS tr_warga_updated AFTER UPDATE ON Warga " +
                "FOR EACH ROW BEGIN " +
                "UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga; " +
                "END;");
        }


    }
}
