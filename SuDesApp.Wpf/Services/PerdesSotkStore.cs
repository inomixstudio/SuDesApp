using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SuDesApp;

namespace SuDesApp.Wpf.Services;

/// <summary>Data isian naskah Perdes SOTK yang disimpan pada database desa aktif.</summary>
public sealed class PerdesSotkStore
{
    private readonly string _connectionString;

    public PerdesSotkStore(AppConfig config)
    {
        _connectionString = config?.DatabaseConnectionString
            ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Isian Perdes SOTK beserta tautan ke baris Buku Perdes (bila sudah disimpan).</summary>
    public sealed record PerdesSotkTersimpan(
        string Nomor,
        string Tanggal,
        string RujukanDaerah,
        int? ArsipId);

    public async Task<PerdesSotkTersimpan> AmbilAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await PastikanTabelAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Nomor, Tanggal, RujukanDaerah, ArsipId FROM PengaturanPerdesSotk WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new PerdesSotkTersimpan(string.Empty, string.Empty, string.Empty, null);

        return new PerdesSotkTersimpan(
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3));
    }

    /// <param name="arsipId">
    /// Id baris Buku Perdes yang menjadi rumah naskah ini. Bila null, tautan
    /// lama dipertahankan supaya menyimpan draf tidak memutus hubungan dengan
    /// arsip yang sudah ada.
    /// </param>
    public async Task SimpanAsync(string nomor, string tanggal, string rujukanDaerah, int? arsipId = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await PastikanTabelAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PengaturanPerdesSotk (Id, Nomor, Tanggal, RujukanDaerah, ArsipId)
            VALUES (1, $nomor, $tanggal, $rujukanDaerah, $arsipId)
            ON CONFLICT(Id) DO UPDATE SET
                Nomor = excluded.Nomor,
                Tanggal = excluded.Tanggal,
                RujukanDaerah = excluded.RujukanDaerah,
                ArsipId = COALESCE(excluded.ArsipId, PengaturanPerdesSotk.ArsipId)";
        command.Parameters.AddWithValue("$nomor", nomor?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$tanggal", tanggal?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$rujukanDaerah", rujukanDaerah?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$arsipId", (object?)arsipId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PastikanTabelAsync(SqliteConnection connection)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS PengaturanPerdesSotk (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Nomor TEXT NOT NULL DEFAULT '',
                    Tanggal TEXT NOT NULL DEFAULT '',
                    RujukanDaerah TEXT NOT NULL DEFAULT '',
                    ArsipId INTEGER)";
            await command.ExecuteNonQueryAsync();
        }

        // Database desa yang dibuat sebelum tautan arsip ada belum memiliki
        // kolom ArsipId; tambahkan sekali saja tanpa menghapus isian lama.
        if (await KolomAdaAsync(connection, "ArsipId")) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE PengaturanPerdesSotk ADD COLUMN ArsipId INTEGER";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task<bool> KolomAdaAsync(SqliteConnection connection, string namaKolom)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(PengaturanPerdesSotk)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), namaKolom, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
