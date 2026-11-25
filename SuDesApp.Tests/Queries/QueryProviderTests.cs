using Dapper;
using Microsoft.Data.Sqlite;
using SuDesApp.Data.Queries;
using SuDesApp.Data.Repositories;
using Xunit;

namespace SuDesApp.Tests.Queries;

/// <summary>
/// Berkas query memakai baris <c>-- NamaQuery</c> sebagai penanda. Komentar
/// biasa di dalam query juga berawalan <c>--</c>, dan dulu setiap baris seperti
/// itu dianggap penanda baru — query dengan komentar lanjutan tersimpan kosong
/// lalu gagal saat dipakai (halaman Sinkron Android menolak dibuka).
///
/// Uji ini menjaga <c>UrutTerakhirSurat</c>, satu-satunya query yang punya
/// komentar lanjutan, dan memastikan komentar itu tidak berubah menjadi nama
/// query.
/// </summary>
public sealed class QueryProviderTests
{
    private static readonly string JalurQuery = CariQuery();

    private static QueryProvider Muat() => new(JalurQuery);

    private static string CariQuery()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
            {
                var jalur = Path.Combine(
                    folder.FullName, "SuDesApp.Core", "Data", "Queries", "SuratQueries.sql");
                Assert.True(File.Exists(jalur), $"Berkas query tidak ditemukan: {jalur}");
                return jalur;
            }

            folder = folder.Parent;
        }

        throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Query_dengan_komentar_lanjutan_tetap_terbaca()
    {
        var query = Muat().GetQuery("UrutTerakhirSurat");

        Assert.False(string.IsNullOrWhiteSpace(query));
        Assert.Contains("MAX", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Year", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_setelah_query_berkomentar_masih_terbaca()
    {
        var query = Muat().GetQuery("GetSuratStatisticsByMonth");

        Assert.False(string.IsNullOrWhiteSpace(query));
        Assert.Contains("GROUP BY", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InsertSurat_menyimpan_kolom_sinkron()
    {
        // Dulu definisi InsertSurat ganda: versi sederhana yang terakhir terbaca
        // diam-diam membuang AdditionalData, Status, dan IdAntreanSinkron.
        var query = Muat().GetQuery("InsertSurat");

        Assert.Contains("IdAntreanSinkron", query, StringComparison.Ordinal);
        Assert.Contains("AdditionalData", query, StringComparison.Ordinal);
        Assert.Contains("Status", query, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSuratByAntrean_mengembalikan_id_dan_nomor_surat()
    {
        // Dipakai impor ulang untuk idempotensi: nomor resmi yang sudah ada harus
        // ikut dikembalikan, bukan hanya tanda "sudah ada".
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        conn.Execute("CREATE TABLE Surat (ID_Surat INTEGER PRIMARY KEY, NomorSurat TEXT NULL, IdAntreanSinkron TEXT NULL);");
        conn.Execute("INSERT INTO Surat (NomorSurat, IdAntreanSinkron) VALUES ('470/001/Ds/2026', 'uuid-1');");
        var query = Muat().GetQuery("GetSuratByAntrean");

        var info = conn.QueryFirstOrDefault<SuratAntreanInfo>(query, new { IdAntreanSinkron = "uuid-1" });

        Assert.NotNull(info);
        Assert.Equal(1, info.IdSurat);
        Assert.Equal("470/001/Ds/2026", info.NomorSurat);
        Assert.Null(conn.QueryFirstOrDefault<SuratAntreanInfo>(query, new { IdAntreanSinkron = "tidak-ada" }));
    }

    [Fact]
    public void Definisi_query_ganda_menolak_dimuat()
    {
        var jalur = Path.Combine(Path.GetTempPath(), $"query-ganda-{Guid.NewGuid():N}.sql");
        try
        {
            File.WriteAllText(jalur, "-- Dup\nSELECT 1;\n\n-- Dup\nSELECT 2;\n");

            Assert.Throws<InvalidOperationException>(() => new QueryProvider(jalur));
        }
        finally
        {
            File.Delete(jalur);
        }
    }

    [Fact]
    public void Komentar_lanjutan_bukan_nama_query()
    {
        var provider = Muat();

        // Kalau komentar diperlakukan sebagai penanda, teks ini akan terdaftar
        // sebagai nama query.
        Assert.Throws<KeyNotFoundException>(() => provider.GetQuery(
            "segmen kedua nomor surat (470/001/Ds/2026 -> 001). Nomor yang bentuknya"));
    }
}
