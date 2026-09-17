using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IPermintaanWaRepository
    {
        Task<int> InsertAsync(PermintaanWa permintaan, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(PermintaanWa permintaan, CancellationToken cancellationToken = default);
        Task<bool> UpdateStatusAsync(int id, string status, string? catatan, int? idSurat, string? pesanBalasan, CancellationToken cancellationToken = default);
        Task<bool> MarkReadAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> MarkReadAllAsync(CancellationToken cancellationToken = default);
        Task<PermintaanWa?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<List<PermintaanWa>> GetAllAsync(string? statusFilter = null, CancellationToken cancellationToken = default);
        Task<List<PermintaanWa>> GetUnreadAsync(CancellationToken cancellationToken = default);
        Task<int> CountByStatusAsync(string status, CancellationToken cancellationToken = default);
        Task<int> CountUnreadAsync(CancellationToken cancellationToken = default);
        Task<int> CountByKodeYearAsync(int year, CancellationToken cancellationToken = default);

        /// <summary>
        /// Kode permintaan berikutnya yang belum terpakai (mis. PMT-2026-0007).
        /// Dihitung dari nomor urut TERBESAR yang sudah ada, bukan dari jumlah
        /// baris — sehingga penghapusan data atau permintaan dari dua jalur
        /// (percakapan &amp; Google Sheet) tidak menghasilkan kode kembar.
        /// </summary>
        Task<string> NextKodePermintaanAsync(int year, CancellationToken cancellationToken = default);

        Task InitializeTableAsync(CancellationToken cancellationToken = default);

        /// <summary>Menjamin kolom kolom mode Google Sheet tersedia (migrasi aman dijalankan berulang).</summary>
        Task MigrateSheetColumnsAsync(CancellationToken cancellationToken = default);

        /// <summary>Mengambil permintaan berdasarkan token prefill yang diberikan ke warga.</summary>
        Task<PermintaanWa?> GetBySheetTokenAsync(string token, CancellationToken cancellationToken = default);

        /// <summary>Permintaan dari baris Google Sheet tertentu (mencegah duplikasi saat ingest).</summary>
        Task<PermintaanWa?> GetBySheetRowAsync(string sheetId, int rowNumber, CancellationToken cancellationToken = default);

        /// <summary>Menautkan token &amp; baris Sheet ke permintaan yang sudah ada.</summary>
        Task<bool> UpdateSheetLinkAsync(int id, string sheetToken, int? sheetRowId, CancellationToken cancellationToken = default);
    }

    public class PermintaanWaRepository : IPermintaanWaRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<PermintaanWaRepository> _logger;
        private bool _sheetColumnsEnsured;

        public PermintaanWaRepository(SqliteConnection connection, ILogger<PermintaanWaRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeTableAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS PermintaanWa (
                    ID_Permintaan    INTEGER PRIMARY KEY AUTOINCREMENT,
                    KodePermintaan   TEXT NOT NULL,
                    NomorWA          TEXT NOT NULL,
                    NamaWarga        TEXT,
                    NIK              TEXT,
                    NamaJenis        TEXT NOT NULL,
                    PesanMentah      TEXT,
                    DataJson         TEXT,
                    Status           TEXT NOT NULL DEFAULT 'BARU',
                    IdSurat          INTEGER,
                    IsRead           INTEGER NOT NULL DEFAULT 0,
                    TanggalPermintaan TEXT NOT NULL,
                    TanggalDiproses  TEXT,
                    Catatan          TEXT,
                    PesanBalasan     TEXT,
                    Sumber           TEXT NOT NULL DEFAULT 'WA',
                    SheetToken       TEXT,
                    SheetRowId       INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_status ON PermintaanWa(Status);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_wa ON PermintaanWa(NomorWA);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_isread ON PermintaanWa(IsRead);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_token ON PermintaanWa(SheetToken);
                CREATE INDEX IF NOT EXISTS idx_permintaanwa_row ON PermintaanWa(SheetRowId);";

            try
            {
                if (_connection.State != System.Data.ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);
                await _connection.ExecuteAsync(sql);
                _logger.LogInformation("Tabel PermintaanWa siap.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal inisialisasi tabel PermintaanWa");
                throw;
            }
        }

        /// <summary>
        /// Migrasi kolom mode Google Sheet (Sumber, SheetToken, SheetRowId) untuk
        /// database yang sudah ada. Aman dipanggil berulang — kolom yang sudah ada
        /// diabaikan (SQLite ADD COLUMN tidak mendukung IF NOT EXISTS).
        /// </summary>
        public async Task MigrateSheetColumnsAsync(CancellationToken cancellationToken = default)
        {
            const string sqlTambah = "ALTER TABLE PermintaanWa ADD COLUMN {0};";
            // CATATAN: SQLite tidak punya IF NOT EXISTS untuk ADD COLUMN — duplikat
            // ditangani lewat pengecekan pragma + penangkapan error "duplicate".
            try
            {
                if (_connection.State != System.Data.ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var kolomBaru = new[]
                {
                    "Sumber TEXT NOT NULL DEFAULT 'WA'",
                    "SheetToken TEXT",
                    "SheetRowId INTEGER"
                };

                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rows = await _connection.QueryAsync<(int cid, string name)>(
                    "SELECT cid, name FROM pragma_table_info('PermintaanWa');");
                foreach (var row in rows) existing.Add(row.name);

                foreach (var def in kolomBaru)
                {
                    var nama = def.Split(' ')[0];
                    if (existing.Contains(nama)) continue;
                    try
                    {
                        await _connection.ExecuteAsync(string.Format(sqlTambah, def));
                        _logger.LogInformation("Kolom PermintaanWa ditambahkan: {Kolom}", nama);
                    }
                    catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
                    {
                        // Kolom sudah ada (race dengan startup lain) — aman diabaikan.
                    }
                }
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Migrasi kolom PermintaanWa: kolom sudah ada.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Gagal migrasi kolom Sheet pada tabel PermintaanWa");
                throw;
            }
        }

        public async Task<int> InsertAsync(PermintaanWa p, CancellationToken cancellationToken = default)
        {
            // Jaminan kolom mode Sheet tersedia (database lama yang migrasi
            // start-upnya belum jalan tetap aman untuk INSERT). Idempoten &
            // sekali per instance repository — biaya pragma sangat murah.
            if (!_sheetColumnsEnsured)
            {
                await MigrateSheetColumnsAsync(cancellationToken);
                _sheetColumnsEnsured = true;
            }

            const string sql = @"
                INSERT INTO PermintaanWa
                    (KodePermintaan, NomorWA, NamaWarga, NIK, NamaJenis, PesanMentah, DataJson, Status, IdSurat, IsRead, TanggalPermintaan, TanggalDiproses, Catatan, PesanBalasan, Sumber, SheetToken, SheetRowId)
                VALUES
                    (@KodePermintaan, @NomorWA, @NamaWarga, @NIK, @NamaJenis, @PesanMentah, @DataJson, @Status, @IdSurat, @IsRead, @TanggalPermintaan, @TanggalDiproses, @Catatan, @PesanBalasan, @Sumber, @SheetToken, @SheetRowId);
                SELECT last_insert_rowid();";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            p.ID_Permintaan = await _connection.ExecuteScalarAsync<int>(sql, new
            {
                p.KodePermintaan,
                p.NomorWA,
                p.NamaWarga,
                p.NIK,
                p.NamaJenis,
                p.PesanMentah,
                p.DataJson,
                p.Status,
                p.IdSurat,
                p.IsRead,
                TanggalPermintaan = p.TanggalPermintaan.ToString("yyyy-MM-dd HH:mm:ss"),
                TanggalDiproses = p.TanggalDiproses?.ToString("yyyy-MM-dd HH:mm:ss"),
                p.Catatan,
                p.PesanBalasan,
                p.Sumber,
                p.SheetToken,
                p.SheetRowId
            });
            return p.ID_Permintaan;
        }

        public async Task<bool> UpdateAsync(PermintaanWa p, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE PermintaanWa SET
                    Status = @Status,
                    IdSurat = @IdSurat,
                    IsRead = @IsRead,
                    TanggalDiproses = @TanggalDiproses,
                    Catatan = @Catatan,
                    PesanBalasan = @PesanBalasan
                WHERE ID_Permintaan = @ID_Permintaan;";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            var rows = await _connection.ExecuteAsync(sql, new
            {
                p.Status,
                p.IdSurat,
                p.IsRead,
                TanggalDiproses = p.TanggalDiproses?.ToString("yyyy-MM-dd HH:mm:ss"),
                p.Catatan,
                p.PesanBalasan,
                p.ID_Permintaan
            });
            return rows > 0;
        }

        /// <summary>
        /// Memperbarui token &amp; baris Sheet milik permintaan (dipakai saat
        /// ingest Google Sheet menautkan baris jawaban ke permintaan warga).
        /// </summary>
        public async Task<bool> UpdateSheetLinkAsync(int id, string sheetToken, int? sheetRowId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE PermintaanWa SET
                    SheetToken = @sheetToken,
                    SheetRowId = @sheetRowId
                WHERE ID_Permintaan = @id;";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            var rows = await _connection.ExecuteAsync(sql, new { id, sheetToken, sheetRowId });
            return rows > 0;
        }

        public async Task<bool> UpdateStatusAsync(int id, string status, string? catatan, int? idSurat, string? pesanBalasan, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE PermintaanWa SET
                    Status = @status,
                    Catatan = @catatan,
                    IdSurat = @idSurat,
                    PesanBalasan = @pesanBalasan,
                    TanggalDiproses = COALESCE(@tglDiproses, TanggalDiproses),
                    IsRead = 1
                WHERE ID_Permintaan = @id;";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            var rows = await _connection.ExecuteAsync(sql, new
            {
                id,
                status,
                catatan,
                idSurat,
                pesanBalasan,
                tglDiproses = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            return rows > 0;
        }

        public async Task<bool> MarkReadAsync(int id, CancellationToken cancellationToken = default)
        {
            const string sql = "UPDATE PermintaanWa SET IsRead = 1 WHERE ID_Permintaan = @id;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.ExecuteAsync(sql, new { id }) > 0;
        }

        public async Task<bool> MarkReadAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "UPDATE PermintaanWa SET IsRead = 1 WHERE IsRead = 0;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.ExecuteAsync(sql) >= 0;
        }

        public async Task<PermintaanWa?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM PermintaanWa WHERE ID_Permintaan = @id;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.QuerySingleOrDefaultAsync<PermintaanWa>(sql, new { id });
        }

        public async Task<PermintaanWa?> GetBySheetTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT * FROM PermintaanWa
                WHERE SheetToken = @token
                ORDER BY datetime(TanggalPermintaan) DESC, ID_Permintaan DESC
                LIMIT 1;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.QuerySingleOrDefaultAsync<PermintaanWa>(sql, new { token });
        }

        public async Task<PermintaanWa?> GetBySheetRowAsync(string sheetId, int rowNumber, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT * FROM PermintaanWa
                WHERE SheetRowId = @rowNumber AND DataJson LIKE @pattern
                ORDER BY ID_Permintaan DESC
                LIMIT 1;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.QuerySingleOrDefaultAsync<PermintaanWa>(
                sql, new { rowNumber, pattern = "%\"SheetId\":\"" + sheetId + "\"%" });
        }

        public async Task<List<PermintaanWa>> GetAllAsync(string? statusFilter = null, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT * FROM PermintaanWa
                {0}
                ORDER BY datetime(TanggalPermintaan) DESC, ID_Permintaan DESC;";

            var where = string.IsNullOrWhiteSpace(statusFilter)
                ? string.Empty
                : "WHERE Status = @status";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            var result = await _connection.QueryAsync<PermintaanWa>(
                string.Format(sql, where),
                string.IsNullOrWhiteSpace(statusFilter) ? null : new { status = statusFilter });
            return result.AsList();
        }

        public async Task<List<PermintaanWa>> GetUnreadAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM PermintaanWa WHERE IsRead = 0 ORDER BY datetime(TanggalPermintaan) DESC;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            var result = await _connection.QueryAsync<PermintaanWa>(sql);
            return result.AsList();
        }

        public async Task<int> CountByStatusAsync(string status, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT COUNT(*) FROM PermintaanWa WHERE Status = @status;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.ExecuteScalarAsync<int>(sql, new { status });
        }

        public async Task<int> CountUnreadAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT COUNT(*) FROM PermintaanWa WHERE IsRead = 0;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.ExecuteScalarAsync<int>(sql);
        }

        public async Task<int> CountByKodeYearAsync(int year, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT COUNT(*) FROM PermintaanWa WHERE KodePermintaan LIKE @pattern;";
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
            return await _connection.ExecuteScalarAsync<int>(sql, new { pattern = $"PMT-{year}-%" });
        }

        public async Task<string> NextKodePermintaanAsync(int year, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT KodePermintaan FROM PermintaanWa WHERE KodePermintaan LIKE @pattern;";
            var prefix = $"PMT-{year}-";

            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);

            var kodes = await _connection.QueryAsync<string>(sql, new { pattern = prefix + "%" });

            int maks = 0;
            foreach (var kode in kodes)
            {
                if (string.IsNullOrWhiteSpace(kode)) continue;
                var nomor = kode.StartsWith(prefix, StringComparison.Ordinal) ? kode[prefix.Length..] : kode;
                if (int.TryParse(nomor, out var n) && n > maks) maks = n;
            }

            return $"{prefix}{(maks + 1):D4}";
        }
    }
}
