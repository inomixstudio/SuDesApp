using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using Xunit;

namespace SuDesApp.Tests.Configuration;

/// <summary>
/// Pemulihan database harus memakai backup API SQLite, bukan mengganti berkas
/// di level OS: database berjalan dalam mode WAL, dan berkas -wal/-shm lama
/// tidak boleh ikut terpakai pada database hasil restore.
/// </summary>
public sealed class PemulihanDatabaseTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "sudes-restore-" + Guid.NewGuid().ToString("N"));

    private readonly AppConfig _config;
    private readonly DatabaseBackupService _layanan;
    private readonly string _aktif;

    public PemulihanDatabaseTests()
    {
        Directory.CreateDirectory(_folder);
        _aktif = Path.Combine(_folder, "desa.db");

        string berkasConfig = Path.Combine(_folder, "appsettings.json");
        string jalurJson = _aktif.Replace("\\", "/");
        File.WriteAllText(berkasConfig, $$"""
        {
          "AppConfig": {
            "databaseConnectionString": "Data Source={{jalurJson}}",
            "updateCheckUrl": null
          }
        }
        """);

        _config = new AppConfig(berkasConfig);
        _layanan = new DatabaseBackupService(_config, NullLogger<DatabaseBackupService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Pembersihan uji tidak boleh menutupi hasil asersi.
        }
    }

    [Fact]
    public async Task Pulihkan_MenggantiIsiDatabase_TetapiCadanganPraRestoreMemuatDataLama()
    {
        await BuatDatabaseAsync(_aktif, "aktif");

        // Tahan koneksi aktif dan tambahkan data yang masih berada di WAL saat
        // pemulihan berjalan — persis keadaan aplikasi yang sedang dipakai.
        await using var koneksiAktif = new SqliteConnection($"Data Source={_aktif}");
        await koneksiAktif.OpenAsync();
        await JalankanAsync(koneksiAktif, "PRAGMA journal_mode=WAL;");
        await JalankanAsync(koneksiAktif, "INSERT INTO Catatan (Isi) VALUES ('masih-di-wal');");

        string berkasCadangan = Path.Combine(_folder, "cadangan.db");
        await BuatDatabaseAsync(berkasCadangan, "dari-cadangan");

        string? praRestore = await _layanan.PulihkanBackupAsync(berkasCadangan);

        Assert.NotNull(praRestore);
        Assert.True(File.Exists(praRestore));

        // Snapshot pra-restore adalah database utuh (VACUUM INTO), termasuk isi WAL.
        Assert.Contains("masih-di-wal", await IsiAsync(praRestore!));

        // Database aktif kini hanya berisi data cadangan, dan tetap sehat.
        Assert.Equal(new[] { "dari-cadangan" }, await IsiAsync(_aktif));
        Assert.Equal("ok", await IntegritasAsync(_aktif));
    }

    [Fact]
    public async Task Pulihkan_CadanganRusak_DitolakTanpaMengubahDatabaseAktif()
    {
        await BuatDatabaseAsync(_aktif, "aktif");

        string rusak = Path.Combine(_folder, "rusak.db");
        await File.WriteAllBytesAsync(rusak, "bukan database sqlite"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _layanan.PulihkanBackupAsync(rusak));

        Assert.Equal(new[] { "aktif" }, await IsiAsync(_aktif));
        Assert.Equal("ok", await IntegritasAsync(_aktif));
    }

    [Fact]
    public async Task Pulihkan_BerkasSamaDenganDatabaseAktif_Ditolak()
    {
        await BuatDatabaseAsync(_aktif, "aktif");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _layanan.PulihkanBackupAsync(_aktif));

        Assert.Equal(new[] { "aktif" }, await IsiAsync(_aktif));
    }

    [Fact]
    public async Task Pulihkan_BerkasTidakAda_GalatJelas()
    {
        await BuatDatabaseAsync(_aktif, "aktif");
        string hilang = Path.Combine(_folder, "tidak-ada.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _layanan.PulihkanBackupAsync(hilang));
    }

    private static async Task BuatDatabaseAsync(string jalur, params string[] isi)
    {
        await using var koneksi = new SqliteConnection($"Data Source={jalur}");
        await koneksi.OpenAsync();

        await JalankanAsync(koneksi, "PRAGMA journal_mode=WAL;");
        await JalankanAsync(koneksi, "CREATE TABLE Catatan (Isi TEXT);");

        foreach (string baris in isi)
        {
            using var cmd = koneksi.CreateCommand();
            cmd.CommandText = "INSERT INTO Catatan (Isi) VALUES (@isi);";
            cmd.Parameters.AddWithValue("@isi", baris);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task JalankanAsync(SqliteConnection koneksi, string sql)
    {
        using var cmd = koneksi.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> IsiAsync(string jalur)
    {
        await using var koneksi = new SqliteConnection($"Data Source={jalur}");
        await koneksi.OpenAsync();

        using var cmd = koneksi.CreateCommand();
        cmd.CommandText = "SELECT Isi FROM Catatan ORDER BY rowid;";

        var hasil = new List<string>();
        await using var pembaca = await cmd.ExecuteReaderAsync();
        while (await pembaca.ReadAsync())
        {
            hasil.Add(pembaca.GetString(0));
        }

        return hasil;
    }

    private static async Task<string> IntegritasAsync(string jalur)
    {
        await using var koneksi = new SqliteConnection($"Data Source={jalur}");
        await koneksi.OpenAsync();

        using var cmd = koneksi.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? string.Empty;
    }
}
