using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface ITutupBukuTahunRepository
    {
        Task<TutupBukuTahun?> GetAsync(int tahun, CancellationToken cancellationToken = default);
        Task<List<TutupBukuTahun>> GetAllAsync(CancellationToken cancellationToken = default);
        Task UpsertAsync(TutupBukuTahun data, CancellationToken cancellationToken = default);
        Task EnsureTableAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Simpan status tutup buku per tahun. Penulisannya satu baris per tahun
    /// (Tahun UNIQUE) memakai UPSERT, jadi menutup ulang tahun yang sama tidak
    /// menumpuk riwayat dan tidak bisa gagal karena baris sudah ada.
    /// </summary>
    public class TutupBukuTahunRepository : ITutupBukuTahunRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<TutupBukuTahunRepository> _logger;

        public TutupBukuTahunRepository(
            SqliteConnection connection,
            ILogger<TutupBukuTahunRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TutupBukuTahun?> GetAsync(int tahun, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT ID, Tahun, Status, TanggalTutup, JumlahSurat, JumlahDeret,
                       Snapshot, Catatan, DitutupOleh, DitutupPada,
                       DibukaOleh, DibukaPada, CreatedAt, UpdatedAt
                  FROM TutupBukuTahun
                 WHERE Tahun = @Tahun
                 LIMIT 1";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            return await _connection.QuerySingleOrDefaultAsync<TutupBukuTahun>(
                sql, new { Tahun = tahun }).ConfigureAwait(false);
        }

        public async Task<List<TutupBukuTahun>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT ID, Tahun, Status, TanggalTutup, JumlahSurat, JumlahDeret,
                       Snapshot, Catatan, DitutupOleh, DitutupPada,
                       DibukaOleh, DibukaPada, CreatedAt, UpdatedAt
                  FROM TutupBukuTahun
                 ORDER BY Tahun DESC";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            var rows = await _connection.QueryAsync<TutupBukuTahun>(sql).ConfigureAwait(false);

            return rows.AsList();
        }

        public async Task UpsertAsync(TutupBukuTahun data, CancellationToken cancellationToken = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (!StatusTutupBuku.IsValid(data.Status))
                throw new ArgumentException($"Status '{data.Status}' tidak dikenal.", nameof(data));

            const string sql = @"
                INSERT INTO TutupBukuTahun
                    (Tahun, Status, TanggalTutup, JumlahSurat, JumlahDeret, Snapshot,
                     Catatan, DitutupOleh, DitutupPada, DibukaOleh, DibukaPada,
                     CreatedAt, UpdatedAt)
                VALUES
                    (@Tahun, @Status, @TanggalTutup, @JumlahSurat, @JumlahDeret, @Snapshot,
                     @Catatan, @DitutupOleh, @DitutupPada, @DibukaOleh, @DibukaPada,
                     COALESCE(@CreatedAt, CURRENT_TIMESTAMP), CURRENT_TIMESTAMP)
                ON CONFLICT(Tahun) DO UPDATE SET
                    Status       = excluded.Status,
                    TanggalTutup = excluded.TanggalTutup,
                    JumlahSurat  = excluded.JumlahSurat,
                    JumlahDeret  = excluded.JumlahDeret,
                    Snapshot     = excluded.Snapshot,
                    Catatan      = excluded.Catatan,
                    DitutupOleh  = excluded.DitutupOleh,
                    DitutupPada  = excluded.DitutupPada,
                    DibukaOleh   = excluded.DibukaOleh,
                    DibukaPada   = excluded.DibukaPada,
                    UpdatedAt    = CURRENT_TIMESTAMP";

            await BukaAsync(cancellationToken).ConfigureAwait(false);
            await _connection.ExecuteAsync(sql, data).ConfigureAwait(false);
            _logger.LogInformation("Status buku tahun {Tahun} disimpan sebagai {Status}.",
                data.Tahun, data.Status);
        }

        /// <summary>
        /// DDL tabel tutup buku tahunan — satu-satunya sumber skemanya.
        /// Dipakai lewat <see cref="EnsureTableAsync"/> (oleh pengujian) dan
        /// lewat <see cref="EnsureAsync"/> oleh DatabaseInitializer setiap startup.
        /// </summary>
        private const string Ddl = @"
            CREATE TABLE IF NOT EXISTS TutupBukuTahun (
                ID           INTEGER PRIMARY KEY AUTOINCREMENT,
                Tahun        INTEGER NOT NULL UNIQUE,
                Status       TEXT    NOT NULL DEFAULT 'TERBUKA'
                            CHECK (Status IN ('TERBUKA', 'TERTUTUP')),
                TanggalTutup TEXT,
                JumlahSurat  INTEGER NOT NULL DEFAULT 0,
                JumlahDeret  INTEGER NOT NULL DEFAULT 0,
                Snapshot     TEXT,
                Catatan      TEXT,
                DitutupOleh  TEXT,
                DitutupPada  TEXT,
                DibukaOleh   TEXT,
                DibukaPada   TEXT,
                CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_TutupBukuTahun_Status ON TutupBukuTahun(Status);";

        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            await BukaAsync(cancellationToken).ConfigureAwait(false);
            await _connection.ExecuteAsync(Ddl).ConfigureAwait(false);
        }

        /// <summary>
        /// Pastikan tabel ada pada koneksi yang diberikan. Dipanggil
        /// DatabaseInitializer pada setiap startup: desa.db.sql versi awal tidak
        /// memuat tabel ini, jadi database yang sudah berjalan harus ditambahi
        /// di sini — tanpa itu setiap kueri halaman Tutup Buku Tahunan (dan
        /// arsip register tahunan) gagal dengan "no such table".
        /// </summary>
        public static async Task EnsureAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        private async Task BukaAsync(CancellationToken cancellationToken)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
