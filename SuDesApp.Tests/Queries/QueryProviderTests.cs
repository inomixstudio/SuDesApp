using SuDesApp.Data.Queries;
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
    public void Komentar_lanjutan_bukan_nama_query()
    {
        var provider = Muat();

        // Kalau komentar diperlakukan sebagai penanda, teks ini akan terdaftar
        // sebagai nama query.
        Assert.Throws<KeyNotFoundException>(() => provider.GetQuery(
            "segmen kedua nomor surat (470/001/Ds/2026 -> 001). Nomor yang bentuknya"));
    }
}
