using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    /// <summary>Akses definisi surat buatan pengguna (menu Template Surat).</summary>
    public interface ITemplateSuratRepository
    {
        Task<List<TemplateSuratKustom>> GetAllAsync();
        Task<TemplateSuratKustom?> GetByIdAsync(int id);
        Task<TemplateSuratKustom> AddAsync(TemplateSuratKustom template);
        Task UpdateAsync(TemplateSuratKustom template);
        Task DeleteAsync(int id);

        /// <summary>Benar bila nama template sudah dipakai template lain (abaikan abjad) selain id dikecualikan.</summary>
        Task<bool> NamaTerpakaiAsync(string nama, int kecualiId);
    }

    /// <summary>
    /// Penyimpanan template surat buatan pengguna pada tabel TemplateSuratKustom
    /// (database desa.db). Seluruh definisi — elemen surat, blok teks, kolom isian,
    /// kaki surat, dan penomoran — disimpan sebagai satu kolom JSON, sehingga
    /// penambahan pilihan baru tidak memerlukan perubahan skema.
    ///
    /// Nomor urut terakhir dan tahunnya disimpan juga sebagai kolom tersendiri
    /// supaya penomoran tetap terbaca walau JSON tidak dapat dibaca (rusak).
    /// </summary>
    public class TemplateSuratRepository : ITemplateSuratRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<TemplateSuratRepository> _logger;
        private readonly ActivityLogService? _activityLog;

        /// <summary>
        /// Inisialisasi tabel hanya sekali per instance repository. Penandanya sengaja
        /// BUKAN statis: satu proses bisa memakai lebih dari satu database (mis. berkas
        /// desa.db yang berbeda saat impor atau pengujian), dan tiap database tetap
        /// memerlukan tabelnya sendiri.
        /// </summary>
        private bool _initialized;
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            WriteIndented = false
        };

        public TemplateSuratRepository(
            SqliteConnection connection,
            ILogger<TemplateSuratRepository> logger,
            ActivityLogService? activityLog = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityLog = activityLog;
        }

        // =====================================================================
        // Inisialisasi skema
        // =====================================================================

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized) return;
                await EnsureOpenAsync().ConfigureAwait(false);
                await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS TemplateSuratKustom (
                    Id            INTEGER PRIMARY KEY,
                    Nama          TEXT NOT NULL,
                    Deskripsi     TEXT,
                    Definisi      TEXT NOT NULL,
                    NomorTerakhir INTEGER NOT NULL DEFAULT 0,
                    TahunNomor    INTEGER NOT NULL DEFAULT 0,
                    DibuatAt      TEXT,
                    DiubahAt      TEXT);
                    CREATE INDEX IF NOT EXISTS idx_templatesurat_nama ON TemplateSuratKustom(Nama);")
                    .ConfigureAwait(false);
                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task EnsureOpenAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync().ConfigureAwait(false);
        }

        /// <summary>Eksekusi aksi tulis dengan retry bila database sempat terkunci.</summary>
        private async Task<T> WriteWithRetryAsync<T>(Func<Task<T>> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await EnsureOpenAsync().ConfigureAwait(false);
                    return await action().ConfigureAwait(false);
                }
                catch (SqliteException ex) when (attempt < 3 && (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6))
                {
                    _logger.LogWarning(ex, "Database terkunci (percobaan {Attempt}/3), retry...", attempt);
                    await Task.Delay(200 * attempt).ConfigureAwait(false);
                }
            }
        }

        // =====================================================================
        // Operasi data
        // =====================================================================

        public async Task<List<TemplateSuratKustom>> GetAllAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            const string sql = @"SELECT Id, Nama, Deskripsi, Definisi, NomorTerakhir, TahunNomor, DibuatAt, DiubahAt
                                 FROM TemplateSuratKustom
                                 ORDER BY Nama COLLATE NOCASE, Id";
            var rows = await _connection.QueryAsync<Baris>(sql).ConfigureAwait(false);
            return rows.Select(Petakan).Where(t => t != null).Select(t => t!).ToList();
        }

        public async Task<TemplateSuratKustom?> GetByIdAsync(int id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            var row = await _connection.QuerySingleOrDefaultAsync<Baris>(
                @"SELECT Id, Nama, Deskripsi, Definisi, NomorTerakhir, TahunNomor, DibuatAt, DiubahAt
                  FROM TemplateSuratKustom WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
            return row == null ? null : Petakan(row);
        }

        public async Task<TemplateSuratKustom> AddAsync(TemplateSuratKustom template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            await EnsureInitializedAsync().ConfigureAwait(false);

            template.Dibuat = template.Dibuat == default ? DateTime.Now : template.Dibuat;
            template.Diubah = DateTime.Now;

            long id = await WriteWithRetryAsync(async () =>
                await _connection.ExecuteScalarAsync<long>(
                    @"INSERT INTO TemplateSuratKustom (Nama, Deskripsi, Definisi, NomorTerakhir, TahunNomor, DibuatAt, DiubahAt)
                      VALUES (@Nama, @Deskripsi, @Definisi, @NomorTerakhir, @TahunNomor, @DibuatAt, @DiubahAt);
                      SELECT last_insert_rowid();",
                    new
                    {
                        Nama = template.NamaTampil,
                        Deskripsi = template.Deskripsi ?? string.Empty,
                        Definisi = Serialisasi(template),
                        template.NomorTerakhir,
                        template.TahunNomor,
                        DibuatAt = template.Dibuat.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        DiubahAt = template.Diubah.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    }).ConfigureAwait(false)).ConfigureAwait(false);

            template.Id = (int)id;
            _logger.LogInformation("Template surat ditambahkan: Id={Id} Nama={Nama}", template.Id, template.Nama);
            _activityLog?.Log("Template Surat", template.NamaTampil, "Buat", template.RingkasanSusunan);
            return template;
        }

        public async Task UpdateAsync(TemplateSuratKustom template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            await EnsureInitializedAsync().ConfigureAwait(false);
            template.Diubah = DateTime.Now;

            int affected = await WriteWithRetryAsync(async () =>
                await _connection.ExecuteAsync(
                    @"UPDATE TemplateSuratKustom
                      SET Nama = @Nama, Deskripsi = @Deskripsi, Definisi = @Definisi,
                          NomorTerakhir = @NomorTerakhir, TahunNomor = @TahunNomor, DiubahAt = @DiubahAt
                      WHERE Id = @Id",
                    new
                    {
                        Nama = template.NamaTampil,
                        Deskripsi = template.Deskripsi ?? string.Empty,
                        Definisi = Serialisasi(template),
                        template.NomorTerakhir,
                        template.TahunNomor,
                        DiubahAt = template.Diubah.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        template.Id
                    }).ConfigureAwait(false)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException($"Template surat id={template.Id} tidak ditemukan di database.");
            }
            _logger.LogInformation("Template surat diperbarui: Id={Id}", template.Id);
            _activityLog?.Log("Template Surat", template.NamaTampil, "Edit", template.RingkasanSusunan);
        }

        public async Task DeleteAsync(int id)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await WriteWithRetryAsync(async () =>
                await _connection.ExecuteAsync("DELETE FROM TemplateSuratKustom WHERE Id = @Id", new { Id = id })
                    .ConfigureAwait(false)).ConfigureAwait(false);
            _logger.LogInformation("Template surat dihapus: Id={Id}", id);
            _activityLog?.Log("Template Surat", $"ID {id}", "Hapus");
        }

        public async Task<bool> NamaTerpakaiAsync(string nama, int kecualiId)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            const string sql = @"SELECT COUNT(1) FROM TemplateSuratKustom
                                 WHERE Nama = @Nama COLLATE NOCASE AND Id != @Kecuali";
            long jumlah = await _connection.ExecuteScalarAsync<long>(sql,
                new { Nama = (nama ?? string.Empty).Trim(), Kecuali = kecualiId }).ConfigureAwait(false);
            return jumlah > 0;
        }

        // =====================================================================
        // Pemetaan baris ↔ model
        // =====================================================================

        private class Baris
        {
            public int Id { get; set; }
            public string Nama { get; set; } = string.Empty;
            public string? Deskripsi { get; set; }
            public string? Definisi { get; set; }
            public int NomorTerakhir { get; set; }
            public int TahunNomor { get; set; }
            public string? DibuatAt { get; set; }
            public string? DiubahAt { get; set; }
        }

        private static string Serialisasi(TemplateSuratKustom template) =>
            JsonSerializer.Serialize(template, OpsiJson);

        /// <summary>
        /// Ubah baris database menjadi definisi template. JSON yang rusak tidak
        /// menggagalkan pemuatan daftar: baris tersebut tetap dikembalikan dengan
        /// definisi kosong agar bisa diperbaiki/dihapus pengguna.
        /// </summary>
        private TemplateSuratKustom? Petakan(Baris baris)
        {
            if (baris == null) return null;

            TemplateSuratKustom? template = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(baris.Definisi))
                {
                    template = JsonSerializer.Deserialize<TemplateSuratKustom>(baris.Definisi);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Definisi JSON template surat id={Id} tidak dapat dibaca; memakai kerangka kosong.", baris.Id);
            }

            template ??= new TemplateSuratKustom();
            template.Id = baris.Id;
            template.Nama = string.IsNullOrWhiteSpace(template.Nama) ? (baris.Nama ?? string.Empty) : template.Nama;
            template.Deskripsi = string.IsNullOrWhiteSpace(template.Deskripsi) ? (baris.Deskripsi ?? string.Empty) : template.Deskripsi;

            // Kolom penomoran & tanggal dari database selalu menang atas isi JSON.
            template.NomorTerakhir = baris.NomorTerakhir;
            template.TahunNomor = baris.TahunNomor;
            template.Dibuat = BacaTanggal(baris.DibuatAt);
            template.Diubah = BacaTanggal(baris.DiubahAt);

            template.Blok ??= new List<BlokTeksTemplateSurat>();
            template.Kolom ??= new List<KolomTemplateSurat>();
            foreach (var kolom in template.Kolom)
            {
                kolom.Pilihan ??= new List<string>();
                if (string.IsNullOrWhiteSpace(kolom.Kunci)) kolom.Kunci = TemplateSuratKunci.Unik(kolom.Label, null);
            }

            return template;
        }

        private static DateTime BacaTanggal(string? teks) =>
            DateTime.TryParse(teks, CultureInfo.InvariantCulture, DateTimeStyles.None, out var nilai) ? nilai : default;
    }
}
