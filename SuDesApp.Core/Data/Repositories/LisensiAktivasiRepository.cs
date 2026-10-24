using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface ILisensiAktivasiRepository
    {
        Task<LisensiAktivasi?> AmbilAsync(string kodeAktivasi, CancellationToken ct = default);
        Task<LisensiAktivasi?> AmbilTerbaruAsync(CancellationToken ct = default);
        Task SimpanAsync(LisensiAktivasi data, CancellationToken ct = default);
        Task<bool> HapusAsync(string kodeAktivasi, CancellationToken ct = default);
        Task EnsureTableAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Menyimpan bukti langganan (token bertanda tangan) ke database desa.
    ///
    /// TUJUAN: kode aktivasi tidak lagi melayang sebagai teks di berkas
    /// preferensi saja — ia terhubung ke database, sehingga ikut terbawa
    /// saat aplikasi mencadangkan/merestore datanya dan tidak hilang bila
    /// berkas preferensi di %LocalAppData% dibersihkan.
    ///
    /// KEAMANAN: tabel ini tidak pernah menyimpan status langganan sebagai
    /// kolom biasa. Yang disimpan hanya token bertanda tangan server, dan
    /// kelayakan selalu dihitung ulang dengan memverifikasi tanda tangannya
    /// (lihat <c>VerifikasiLisensi</c>). Mengedit isi baris di sini tidak
    /// memberi akses apa pun — token hasil suntikan tidak akan lewat
    /// verifikasi. Fallback ke berkas <c>lisensi-cache.json</c> tetap
    /// dipertahankan supaya instalasi lama tidak ikut terkunci.
    /// </summary>
    public class LisensiAktivasiRepository : ILisensiAktivasiRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<LisensiAktivasiRepository> _logger;

        public LisensiAktivasiRepository(
            SqliteConnection connection,
            ILogger<LisensiAktivasiRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<LisensiAktivasi?> AmbilAsync(string kodeAktivasi, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(kodeAktivasi)) return null;

            const string sql = @"
                SELECT KodeAktivasi, NamaDesa, Paket, Token, IdPerangkat, CekTerakhir, CreatedAt, UpdatedAt
                  FROM LisensiAktivasi
                 WHERE KodeAktivasi = @KodeAktivasi COLLATE NOCASE
                 LIMIT 1";

            await BukaAsync(ct).ConfigureAwait(false);
            return await _connection.QuerySingleOrDefaultAsync<LisensiAktivasi>(
                new CommandDefinition(sql, new { KodeAktivasi = kodeAktivasi.Trim() }, cancellationToken: ct))
                .ConfigureAwait(false);
        }

        /// <summary>Baris yang paling baru diperbarui — dipakai saat kode tidak diketik.</summary>
        public async Task<LisensiAktivasi?> AmbilTerbaruAsync(CancellationToken ct = default)
        {
            // UpdatedAt hanya presisi detik, sedangkan dua penyimpanan bisa
            // terjadi pada detik yang sama. Tanpa penentu kedua, "terbaru"
            // jadi ambigu dan SQLite bebas mengembalikan baris mana saja.
            // rowid sebagai pemutus akhir membuat hasilnya pasti: baris yang
            // ditulis belakangan menang.
            const string sql = @"
                SELECT KodeAktivasi, NamaDesa, Paket, Token, IdPerangkat, CekTerakhir, CreatedAt, UpdatedAt
                  FROM LisensiAktivasi
                 WHERE Token IS NOT NULL AND Token <> ''
                 ORDER BY UpdatedAt DESC, rowid DESC
                 LIMIT 1";

            await BukaAsync(ct).ConfigureAwait(false);
            return await _connection.QuerySingleOrDefaultAsync<LisensiAktivasi>(
                new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        }

        /// <summary>
        /// Simpan/perbarui bukti langganan. Memakai UPSERT pada KodeAktivasi
        /// agar pengecekan berulang dari server tidak menumpuk baris.
        /// </summary>
        public async Task SimpanAsync(LisensiAktivasi data, CancellationToken ct = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.KodeAktivasi))
                throw new ArgumentException("Kode aktivasi wajib diisi.", nameof(data));

            const string sql = @"
                INSERT INTO LisensiAktivasi
                    (KodeAktivasi, NamaDesa, Paket, Token, IdPerangkat, CekTerakhir, CreatedAt, UpdatedAt)
                VALUES
                    (@KodeAktivasi, @NamaDesa, @Paket, @Token, @IdPerangkat, @CekTerakhir,
                     CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                ON CONFLICT(KodeAktivasi) DO UPDATE SET
                    NamaDesa    = excluded.NamaDesa,
                    Paket       = excluded.Paket,
                    Token       = excluded.Token,
                    IdPerangkat = excluded.IdPerangkat,
                    CekTerakhir = excluded.CekTerakhir,
                    UpdatedAt   = CURRENT_TIMESTAMP";

            await BukaAsync(ct).ConfigureAwait(false);
            await _connection.ExecuteAsync(new CommandDefinition(sql, data, cancellationToken: ct))
                .ConfigureAwait(false);

            _logger.LogInformation("Bukti langganan '{Kode}' tersimpan di database desa.", data.KodeAktivasi);
        }

        public async Task<bool> HapusAsync(string kodeAktivasi, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(kodeAktivasi)) return false;

            const string sql = "DELETE FROM LisensiAktivasi WHERE KodeAktivasi = @KodeAktivasi COLLATE NOCASE";

            await BukaAsync(ct).ConfigureAwait(false);
            int terhapus = await _connection.ExecuteAsync(new CommandDefinition(
                sql, new { KodeAktivasi = kodeAktivasi.Trim() }, cancellationToken: ct)).ConfigureAwait(false);
            return terhapus > 0;
        }

        public async Task EnsureTableAsync(CancellationToken ct = default)
        {
            await BukaAsync(ct).ConfigureAwait(false);
            await _connection.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: ct)).ConfigureAwait(false);
        }

        /// <summary>
        /// DDL tabel bukti langganan — satu-satunya sumber skemanya, sama
        /// seperti tabel TutupBukuTahun, supaya tidak ada du definisi yang
        /// bisa berbeda satu sama lain.
        /// </summary>
        private const string Ddl = @"
            CREATE TABLE IF NOT EXISTS LisensiAktivasi (
                KodeAktivasi TEXT PRIMARY KEY COLLATE NOCASE,
                NamaDesa     TEXT,
                Paket        TEXT,
                Token        TEXT,
                IdPerangkat  TEXT,
                CekTerakhir  TEXT,
                CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
            );";

        /// <summary>
        /// Pastikan tabel ada pada koneksi yang diberikan. Dipanggil
        /// DatabaseInitializer setiap startup: desa.db.sql versi awal tidak
        /// memuat tabel ini, jadi database yang sudah berjalan harus ditambahi
        /// di sini — tanpa itu penyimpanan bukti langganan gagal dengan
        /// "no such table" dan aplikasi kembali ke cache berkas saja.
        /// </summary>
        public static async Task EnsureAsync(SqliteConnection connection, CancellationToken ct = default)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(ct).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: ct)).ConfigureAwait(false);
        }

        private async Task BukaAsync(CancellationToken ct)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(ct).ConfigureAwait(false);
        }

        // =====================================================================
        // Bentuk tanpa DI — dipakai LisensiManager (singleton) yang jalan
        // SEBELUM scope navigasi ada, jadi tidak boleh bergantung pada
        // SqliteConnection scoped. String koneksi diberikan, repo membuka
        // koneksi sendiri sesaat lalu menutupnya.
        // =====================================================================

        /// <summary>
        /// Simpan bukti langganan lewat string koneksi. Tabel dibuat lebih
        /// dulu supaya panggilan dari LisensiManager tidak gagal pada
        /// database lama yang belum pernah menjalankan DatabaseInitializer.
        /// </summary>
        public static async Task<bool> SimpanDenganKoneksiAsync(
            string connectionString,
            LisensiAktivasi data,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return false;
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.KodeAktivasi)) return false;

            const string sql = @"
                INSERT INTO LisensiAktivasi
                    (KodeAktivasi, NamaDesa, Paket, Token, IdPerangkat, CekTerakhir, CreatedAt, UpdatedAt)
                VALUES
                    (@KodeAktivasi, @NamaDesa, @Paket, @Token, @IdPerangkat, @CekTerakhir,
                     CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                ON CONFLICT(KodeAktivasi) DO UPDATE SET
                    NamaDesa    = excluded.NamaDesa,
                    Paket       = excluded.Paket,
                    Token       = excluded.Token,
                    IdPerangkat = excluded.IdPerangkat,
                    CekTerakhir = excluded.CekTerakhir,
                    UpdatedAt   = CURRENT_TIMESTAMP";

            try
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await EnsureAsync(connection, ct).ConfigureAwait(false);

                await connection.ExecuteAsync(new CommandDefinition(sql, data, cancellationToken: ct))
                    .ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                // Database sedang terkunci proses lain / belum siap — bukti
                // tetap aman di cache berkas, jadi gagal simpan di sini tidak
                // boleh menggagalkan pengecekan lisensi.
                return false;
            }
        }

        /// <summary>
        /// Ambil bukti langganan untuk satu kode lewat string koneksi.
        /// Mengembalikan null bila tabel belum ada, kodenya tidak tercatat,
        /// atau database sedang tidak bisa dibuka — semuanya diperlakukan
        /// "tidak ada bukti", bukan error.
        /// </summary>
        public static async Task<LisensiAktivasi?> AmbilDenganKoneksiAsync(
            string connectionString,
            string kodeAktivasi,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return null;
            if (string.IsNullOrWhiteSpace(kodeAktivasi)) return null;

            const string sql = @"
                SELECT KodeAktivasi, NamaDesa, Paket, Token, IdPerangkat, CekTerakhir, CreatedAt, UpdatedAt
                  FROM LisensiAktivasi
                 WHERE KodeAktivasi = @KodeAktivasi COLLATE NOCASE
                 LIMIT 1";

            try
            {
                await using var connection = new SqliteConnection(connectionString);
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(ct).ConfigureAwait(false);

                return await connection.QuerySingleOrDefaultAsync<LisensiAktivasi>(
                    new CommandDefinition(sql, new { KodeAktivasi = kodeAktivasi.Trim() }, cancellationToken: ct))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>Hapus bukti langganan (kode ditolak server / dibersihkan admin).</summary>
        public static async Task<bool> HapusDenganKoneksiAsync(
            string connectionString,
            string kodeAktivasi,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return false;
            if (string.IsNullOrWhiteSpace(kodeAktivasi)) return false;

            try
            {
                await using var connection = new SqliteConnection(connectionString);
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(ct).ConfigureAwait(false);

                int terhapus = await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM LisensiAktivasi WHERE KodeAktivasi = @KodeAktivasi COLLATE NOCASE",
                    new { KodeAktivasi = kodeAktivasi.Trim() },
                    cancellationToken: ct)).ConfigureAwait(false);
                return terhapus > 0;
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                return false;
            }
        }
    }
}