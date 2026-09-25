using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Fixture per-kelas test: SQLite in-memory + skema asli dari desa.db.sql +
    /// tambalan kolom yang di aplikasi nyata ditambahkan oleh SuratRepository
    /// (Status, KodeJenis, AdditionalData, CreatedAt, UpdatedAt, dst.) + graf
    /// repositori penuh + SuratSaveService. Koneksi dibuat sekali per kelas test.
    /// </summary>
    public sealed class CoreTestFixture : IDisposable
    {
        /// <summary>KodeJenis blanko NTCR_N1 pada JenisSuratConfig.json (dipakai generate nomor).</summary>
        public const string KodeNtcrN1 = "N1T";

        public SqliteConnection Connection { get; }
        public UnitOfWork UnitOfWork { get; }
        public SuratSaveService SaveService { get; }
        public AppConfig AppConfig => _appConfig;
        public FileService FileService { get; private set; } = null!;
        public IDesaRepository DesaRepository { get; private set; } = null!;
        public ISuratRepository SuratRepository { get; private set; } = null!;
        public SettingsManager SettingsManager { get; private set; } = null!;

        private readonly ICacheService _cache;
        private readonly AppConfig _appConfig;
        private readonly string _jalurConfigJson;

        public CoreTestFixture()
        {
            _appConfig = new AppConfig(BuildKonfigurasi());
            _cache = new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance);

            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            // Skema dasar (16 tabel) lalu tambalan kolom yang oleh aplikasi ditambahkan
            // lewat SuratRepository.InitializeSuratIndexesAsync saat start-up.
            Connection.ExecuteScript(ReadProjectFile("desa.db.sql"));
            Connection.ExecuteScript(TambalanKolom);

            _jalurConfigJson = FindJenisSuratConfigJson();
            var configurationLoader = new JenisSuratConfigLoader(
                _cache, BuildKonfigurasi(), NullLogger<JenisSuratConfigLoader>.Instance,
                _jalurConfigJson);

            // UnitOfWork menyelesaikan repositori secara lazy lewat IServiceProvider,
            // sehingga layanan didaftarkan SETELAH UnitOfWork dibangun (dua fase).
            var provider = new TestServiceProvider();
            UnitOfWork = new UnitOfWork(Connection, NullLogger<UnitOfWork>.Instance, provider);

            var jenisSuratRepository = new JenisSuratRepository(
                UnitOfWork, NullLogger<JenisSuratRepository>.Instance, _cache, configurationLoader);
            var desaRepository = new DesaRepository(
                _appConfig, _cache, NullLogger<DesaRepository>.Instance, configurationLoader);
            var wargaRepository = new WargaRepository(
                Connection, _cache, _appConfig, NullLogger<WargaRepository>.Instance);
            var suratRepository = new SuratRepository(
                Connection,
                NullLogger<SuratRepository>.Instance,
                _cache,
                new QueryProvider(Path.Combine(
                    FindProjectRoot(), "SuDesApp.Core", "Data", "Queries", "SuratQueries.sql")),
                new QueryInterceptor(NullLogger<QueryInterceptor>.Instance),
                new ISuratDataHandler[] { new NtcrDataHandler("NTCR_N1", NullLogger<NtcrDataHandler>.Instance, _cache) },
                wargaRepository,
                jenisSuratRepository,
                desaRepository);

            FileService = new FileService(_appConfig, NullLogger<FileService>.Instance);
            DesaRepository = desaRepository;
            SuratRepository = suratRepository;
            SettingsManager = new SettingsManager(desaRepository, _appConfig, NullLogger<SettingsManager>.Instance);

            provider.Daftarkan(new Dictionary<Type, object>
            {
                [typeof(IUnitOfWork)] = UnitOfWork,
                [typeof(IJenisSuratRepository)] = jenisSuratRepository,
                [typeof(IDesaRepository)] = desaRepository,
                [typeof(IWargaRepository)] = wargaRepository,
                [typeof(ISuratRepository)] = suratRepository,
            });

            SaveService = new SuratSaveService(UnitOfWork, NullLogger<SuratSaveService>.Instance);
        }

        /// <summary>Data dasar yang diharapkan validator: info desa + jenis NTCR_N1. Idempoten.</summary>
        public void SiapkanDataDasar()
        {
            Connection.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO InfoDesa (NamaDesa, Kecamatan, Kabupaten, Alamat, Kodepos, KepalaDesa, SekretarisDesa)
                  VALUES ('Desa Uji', 'Kec. Uji', 'Kab. Uji', 'Jl. Uji No. 1', '12345', 'Kades Uji', 'Sekdes Uji')");
            Connection.ExecuteNonQuery(
                @"INSERT OR IGNORE INTO JenisSurat (NamaJenis, KodeJenis) VALUES ('NTCR_N1', '" + KodeNtcrN1 + "')");
        }

        /// <summary>Surat NTCR_N1 dengan seluruh isian yang diwajibkan validator (aktif).</summary>
        public SuratData BuatSuratLengkap(string namaJenis = "NTCR_N1")
        {
            var surat = new SuratData(
                UnitOfWork.SuratRepository, UnitOfWork.WargaRepository,
                UnitOfWork.DesaRepository, UnitOfWork.JenisSuratRepository)
            {
                NamaJenis = namaJenis,
                Status = "Active",
                TanggalSurat = new DateTime(2026, 9, 25),
                Keterangan = "Uji",
                Warga = new WargaData
                {
                    NIK = "3204010101800001",
                    Nama = "Budi Santoso",
                    TempatLahir = "Bandung",
                    TanggalLahir = "1980-01-01",
                    JenisKelamin = "Laki-laki",
                    Agama = "Islam",
                    Pekerjaan = "Petani",
                    StatusPerkawinan = "Belum Kawin",
                    Kewarganegaraan = "WNI",
                    Dusun = "Dusun Uji", Desa = "Desa Uji",
                    Kecamatan = "Kec. Uji", Kabupaten = "Kab. Uji"
                },
                Ntcr = new NtcrData
                {
                    NikIstri = "3204010101850002",
                    NamaIstri = "Siti Aminah",
                    TempatLahirIstri = "Bandung",
                    TanggalLahirIstri = "1985-01-01",
                    AgamaIstri = "Islam",
                    PekerjaanIstri = "Guru",
                    StatusPerkawinanIstri = "Belum Kawin",
                    AlamatIstri = "Dusun Uji, Desa Uji",
                    KewarganegaraanIstri = "WNI",
                    AyahCalonSuami = new NtcrOrangTua { Nama = "Hasan" },
                    IbuCalonSuami = new NtcrOrangTua { Nama = "Aminah" },
                    AyahCalonIstri = new NtcrOrangTua { Nama = "Rahmat" },
                    IbuCalonIstri = new NtcrOrangTua { Nama = "Fatimah" }
                }
            };
            surat.Desa = new DesaData
            {
                NamaDesa = "Desa Uji", Kecamatan = "Kec. Uji", Kabupaten = "Kab. Uji",
                Alamat = "Jl. Uji No. 1", Kodepos = "12345",
                KepalaDesa = "Kades Uji", SekretarisDesa = "Sekdes Uji"
            };
            return surat;
        }

        internal static string FindProjectRoot()
        {
            // Jalur repo: tests/SuDesApp.Core.Tests/bin/... → naik ke root proyek.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "desa.db.sql")))
                dir = dir.Parent!;
            if (dir == null)
                throw new FileNotFoundException("desa.db.sql tidak ditemukan dari " + AppContext.BaseDirectory);

            return dir.FullName;
        }

        internal static string ReadProjectFile(string relativePath) =>
            File.ReadAllText(Path.Combine(FindProjectRoot(), relativePath));

        private static IConfiguration BuildKonfigurasi() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppConfig:databaseConnectionString"] = "Data Source=:memory:",
            })
            .Build();

        private static string FindJenisSuratConfigJson()
        {
            var isi = ReadProjectFile(Path.Combine("SuDesApp.Core", "Configuration", "JenisSuratConfig.json"));
            var jalur = Path.Combine(Path.GetTempPath(), "uji-jenissurat-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(jalur, isi);
            return jalur;
        }

        /// <summary>
        /// Kolom yang di aplikasi ditambahkan saat start-up (lihat
        /// SuratRepository.InitializeSuratIndexesAsync dan
        /// WargaRepository.EnsureWargaSchemaAsync): skema dasar desa.db.sql
        /// belum memuat kolom-kolom status/timestamp/alamat ini.
        /// </summary>
        private const string TambalanKolom = @"
ALTER TABLE Surat ADD COLUMN Status TEXT NOT NULL DEFAULT 'Draft';
ALTER TABLE Surat ADD COLUMN KodeJenis TEXT NULL;
ALTER TABLE Surat ADD COLUMN AdditionalData TEXT NULL;
ALTER TABLE Surat ADD COLUMN CreatedAt TEXT NULL;
ALTER TABLE Surat ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE Warga ADD COLUMN Dusun TEXT NULL;
ALTER TABLE Warga ADD COLUMN Desa TEXT NULL;
ALTER TABLE Warga ADD COLUMN Kecamatan TEXT NULL;
ALTER TABLE Warga ADD COLUMN Kabupaten TEXT NULL;
ALTER TABLE Warga ADD COLUMN CreatedAt TEXT NULL;
ALTER TABLE Warga ADD COLUMN UpdatedAt TEXT NULL;
ALTER TABLE JenisSurat ADD COLUMN Deskripsi TEXT NULL;
ALTER TABLE SKU ADD COLUMN LokasiUsaha TEXT NULL;";

        public void Dispose()
        {
            UnitOfWork.Dispose();
            Connection.Dispose();
            try { File.Delete(_jalurConfigJson); } catch { /* sementara — biarkan OS membersihkan */ }
        }
    }

    /// <summary>Provider layanan minimum untuk UnitOfWork (lazy repository); isinya diisi belakangan.</summary>
    internal sealed class TestServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Daftarkan(Dictionary<Type, object> services)
        {
            foreach (var (jenis, layanan) in services)
                _services[jenis] = layanan;
        }

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var service) ? service : null;
    }

    internal static class SqliteExtensions
    {
        public static void ExecuteScript(this SqliteConnection connection, string script)
        {
            using var command = connection.CreateCommand();
            command.CommandText = script;
            command.ExecuteNonQuery();
        }

        public static void ExecuteNonQuery(this SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public static object? Scalar(this SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
    }
}
