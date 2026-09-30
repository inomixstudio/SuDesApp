using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IPenggunaRepository
    {
        Task EnsureTableAsync(CancellationToken cancellationToken = default);

        Task<Pengguna?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

        Task<Pengguna?> GetAsync(int id, CancellationToken cancellationToken = default);

        Task<List<Pengguna>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<int> HitungAsync(CancellationToken cancellationToken = default);

        Task<int> InsertAsync(Pengguna data, CancellationToken cancellationToken = default);

        /// <summary>Perbarui nama, peran, dan status aktif (tanpa menyentuh kata sandi).</summary>
        Task<bool> UpdateAsync(Pengguna data, CancellationToken cancellationToken = default);

        /// <summary>Simpan salt/hash baru (ganti kata sandi atau reset oleh administrator).</summary>
        Task<bool> UpdateKataSandiAsync(
            int id, string salt, string hash, int iterasi, string? oleh,
            CancellationToken cancellationToken = default);

        /// <summary>Catat hasil percobaan masuk: jumlah gagal, waktu kunci, dan waktu login terakhir.</summary>
        Task<bool> CatatPercobaanMasukAsync(
            int id, int gagalLogin, DateTime? terkunciSampai, DateTime? loginTerakhir,
            CancellationToken cancellationToken = default);

        Task<bool> HapusAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Penyimpanan akun pengguna. Tabelnya dibuat lewat <see cref="Ddl"/> yang
    /// sama dipakai <c>DatabaseInitializer</c>, sehingga database lama
    /// mendapatkannya saat aplikasi dibuka tanpa menjalankan migrasi penuh.
    /// </summary>
    public class PenggunaRepository : IPenggunaRepository
    {
        private const string KolomSelect = @"
            ID, Username, NamaTampilan, Peran, Salt, Hash, Iterasi, Aktif,
            GagalLogin, TerkunciSampai, LoginTerakhir,
            DibuatOleh, DiperbaruiOleh, CreatedAt, UpdatedAt";

        private readonly SqliteConnection _connection;
        private readonly ILogger<PenggunaRepository> _logger;

        public PenggunaRepository(
            SqliteConnection connection,
            ILogger<PenggunaRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Self-heal: repository boleh dipakai di database lama yang belum
            // punya tabel Pengguna (mis. uji pakai skema dasar saja) — pastikan
            // tabel + indeksnya ada sebelum kueri pertama.
            EnsureTableAsync().GetAwaiter().GetResult();
        }

        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            await _connection.ExecuteAsync(new CommandDefinition(
                Ddl.CreateTable + Ddl.Index, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<Pengguna?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            var rapi = RapiUsername(username);
            if (rapi.Length == 0) return null;

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            return await _connection.QuerySingleOrDefaultAsync<Pengguna>(new CommandDefinition(
                $"SELECT {KolomSelect} FROM Pengguna WHERE Username = @Username COLLATE NOCASE LIMIT 1",
                new { Username = rapi }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<Pengguna?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            return await _connection.QuerySingleOrDefaultAsync<Pengguna>(new CommandDefinition(
                $"SELECT {KolomSelect} FROM Pengguna WHERE ID = @ID",
                new { ID = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<List<Pengguna>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            var rows = await _connection.QueryAsync<Pengguna>(new CommandDefinition(
                $"SELECT {KolomSelect} FROM Pengguna ORDER BY Aktif DESC, Peran, NamaTampilan COLLATE NOCASE, Username",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows.AsList();
        }

        public async Task<int> HitungAsync(CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            return await _connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM Pengguna", cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<int> InsertAsync(Pengguna data, CancellationToken cancellationToken = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            const string sql = @"
                INSERT INTO Pengguna
                    (Username, NamaTampilan, Peran, Salt, Hash, Iterasi, Aktif,
                     GagalLogin, TerkunciSampai, LoginTerakhir, DibuatOleh, DiperbaruiOleh,
                     CreatedAt, UpdatedAt)
                VALUES
                    (@Username, @NamaTampilan, @Peran, @Salt, @Hash, @Iterasi, @Aktif,
                     0, NULL, NULL, @DibuatOleh, @DiperbaruiOleh,
                     @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();";

            await BukaAsync(cancellationToken).ConfigureAwait(false);

            data.Username = RapiUsername(data.Username);
            data.Peran = PeranPengguna.Normalisasi(data.Peran);

            return await _connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql, data, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<bool> UpdateAsync(Pengguna data, CancellationToken cancellationToken = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.ID <= 0) throw new ArgumentException("ID pengguna belum terisi.", nameof(data));

            const string sql = @"
                UPDATE Pengguna
                   SET NamaTampilan = @NamaTampilan,
                       Peran = @Peran,
                       Aktif = @Aktif,
                       DiperbaruiOleh = @DiperbaruiOleh,
                       UpdatedAt = @UpdatedAt
                 WHERE ID = @ID";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            int baris = await _connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                data.ID,
                data.NamaTampilan,
                Peran = PeranPengguna.Normalisasi(data.Peran),
                Aktif = data.Aktif ? 1 : 0,
                data.DiperbaruiOleh,
                UpdatedAt = DateTime.Now
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return baris > 0;
        }

        public async Task<bool> UpdateKataSandiAsync(
            int id, string salt, string hash, int iterasi, string? oleh,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            const string sql = @"
                UPDATE Pengguna
                   SET Salt = @Salt,
                       Hash = @Hash,
                       Iterasi = @Iterasi,
                       GagalLogin = 0,
                       TerkunciSampai = NULL,
                       DiperbaruiOleh = @Oleh,
                       UpdatedAt = @UpdatedAt
                 WHERE ID = @ID";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            int baris = await _connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                ID = id,
                Salt = salt,
                Hash = hash,
                Iterasi = iterasi,
                Oleh = oleh,
                UpdatedAt = DateTime.Now
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return baris > 0;
        }

        public async Task<bool> CatatPercobaanMasukAsync(
            int id, int gagalLogin, DateTime? terkunciSampai, DateTime? loginTerakhir,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

            const string sql = @"
                UPDATE Pengguna
                   SET GagalLogin = @GagalLogin,
                       TerkunciSampai = @TerkunciSampai,
                       LoginTerakhir = COALESCE(@LoginTerakhir, LoginTerakhir)
                 WHERE ID = @ID";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            int baris = await _connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                ID = id,
                GagalLogin = gagalLogin,
                TerkunciSampai = terkunciSampai?.ToString("yyyy-MM-dd HH:mm:ss"),
                LoginTerakhir = loginTerakhir?.ToString("yyyy-MM-dd HH:mm:ss")
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return baris > 0;
        }

        public async Task<bool> HapusAsync(int id, CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            int baris = await _connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM Pengguna WHERE ID = @ID", new { ID = id },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return baris > 0;
        }

        /// <summary>Nama akun selalu huruf kecil supaya "Budi" dan "budi" tidak jadi dua akun.</summary>
        private static string RapiUsername(string? username) =>
            (username ?? string.Empty).Trim().ToLowerInvariant();

        private async Task BukaAsync(CancellationToken cancellationToken)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>DDL tabel pengguna, dipakai initializer maupun repository.</summary>
        public static class Ddl
        {
            public const string CreateTable = @"
                CREATE TABLE IF NOT EXISTS Pengguna (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    NamaTampilan TEXT NOT NULL DEFAULT '',
                    Peran TEXT NOT NULL DEFAULT 'OPERATOR',
                    Salt TEXT NOT NULL,
                    Hash TEXT NOT NULL,
                    Iterasi INTEGER NOT NULL DEFAULT 120000,
                    Aktif INTEGER NOT NULL DEFAULT 1,
                    GagalLogin INTEGER NOT NULL DEFAULT 0,
                    TerkunciSampai TEXT,
                    LoginTerakhir TEXT,
                    DibuatOleh TEXT,
                    DiperbaruiOleh TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            public const string Index = @"
                CREATE INDEX IF NOT EXISTS IX_Pengguna_Peran ON Pengguna(Peran);
                CREATE INDEX IF NOT EXISTS IX_Pengguna_Aktif ON Pengguna(Aktif);";
        }
    }
}
