using Microsoft.Data.Sqlite;
using SuDesApp.Configuration;
using Xunit;

namespace SuDesApp.Tests.Configuration;

/// <summary>
/// Skenario pembaruan aplikasi: database yang sudah ada di komputer desa bisa
/// berupa SQLite biasa (dibuat versi ini) atau database terenkripsi SQLCipher
/// (dibuat versi lama). AppConfig harus menyusun connection string yang sesuai
/// dengan BERKASNYA tanpa campur tangan pengguna — kalau tidak, aplikasi
/// berhenti di layar splash setelah pembaruan.
/// </summary>
[Collection("BerkasDatabase")]
public sealed class PembaruanDatabaseTests : IDisposable
{
    private const string KunciUji = "kunci-pembaruan-uji";

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "sudes-pembaruan-" + Guid.NewGuid().ToString("N"));

    private string? _kunci;

    public PembaruanDatabaseTests()
    {
        Directory.CreateDirectory(_folder);
        BerkasDatabase.MuatKunci = () => _kunci;
    }

    public void Dispose()
    {
        BerkasDatabase.MuatKunci = KunciDatabase.Muat;
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

    private string BuatDatabase(string nama, string? kunci, string namaDesa)
    {
        var jalur = Path.Combine(_folder, nama);
        using (var conn = new SqliteConnection(Koneksi(jalur, kunci)))
        {
            conn.Open();
            using var cmd = new SqliteCommand(
                "CREATE TABLE InfoDesa (NamaDesa TEXT);" +
                "INSERT INTO InfoDesa (NamaDesa) VALUES (@nama);", conn);
            cmd.Parameters.AddWithValue("@nama", namaDesa);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return jalur;
    }

    private static string Koneksi(string jalur, string? kunci = null)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = jalur };
        if (!string.IsNullOrWhiteSpace(kunci)) builder.Password = kunci;
        return builder.ToString();
    }

    /// <summary>AppConfig dibaca dari appsettings persis seperti saat aplikasi dibuka.</summary>
    private AppConfig MuatAppConfig(string jalurDatabase)
    {
        string berkasConfig = Path.Combine(_folder, "appsettings.json");
        string jalurJson = jalurDatabase.Replace("\\", "/");

        File.WriteAllText(berkasConfig, $$"""
        {
          "AppConfig": {
            "databaseConnectionString": "Data Source={{jalurJson}}",
            "updateCheckUrl": null
          }
        }
        """);

        return new AppConfig(berkasConfig);
    }

    private static string BacaNamaDesa(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("SELECT NamaDesa FROM InfoDesa LIMIT 1;", conn);
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private static bool HeaderPolos(string jalur)
    {
        var header = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        var isi = File.ReadAllBytes(jalur);
        return isi.Length >= header.Length && isi.AsSpan(0, header.Length).SequenceEqual(header);
    }

    [Fact]
    public void AppConfig_PadaDatabaseTerenkripsi_MenyusunKoneksiBerKunci()
    {
        _kunci = KunciUji;
        var jalur = BuatDatabase("desa-terenkripsi.db", KunciUji, "Desa Lama");

        var config = MuatAppConfig(jalur);

        Assert.True(BerkasDatabase.Terkunci(config.DatabaseConnectionString));
        Assert.Equal("Desa Lama", BacaNamaDesa(config.DatabaseConnectionString));

        // Setelah dibuka, aplikasi harus bisa menulis ke database lama itu —
        // bukan hanya membacanya — dan bentuk berkasnya tetap terenkripsi.
        using (var conn = new SqliteConnection(config.DatabaseConnectionString))
        {
            conn.Open();
            using var cmd = new SqliteCommand(
                "INSERT INTO InfoDesa (NamaDesa) VALUES ('Setelah Pembaruan');", conn);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Assert.False(HeaderPolos(jalur));
        Assert.Equal(StatusBerkasDatabase.Terenkripsi, BerkasDatabase.Periksa(jalur));
    }

    [Fact]
    public void AppConfig_PadaDatabaseBiasa_TetapTanpaKunci()
    {
        var jalur = BuatDatabase("desa.db", null, "Desa Baru");

        var config = MuatAppConfig(jalur);

        Assert.False(BerkasDatabase.Terkunci(config.DatabaseConnectionString));
        Assert.Equal("Desa Baru", BacaNamaDesa(config.DatabaseConnectionString));
        Assert.True(HeaderPolos(jalur));
    }

    [Fact]
    public void AppConfig_PadaBerkasBelumAda_MenyiapkanDatabaseBaruTanpaKunci()
    {
        var jalur = Path.Combine(_folder, "desa-baru.db");

        var config = MuatAppConfig(jalur);

        Assert.False(BerkasDatabase.Terkunci(config.DatabaseConnectionString));
        Assert.Equal(Path.GetFullPath(jalur), config.DatabasePath);
    }
}
