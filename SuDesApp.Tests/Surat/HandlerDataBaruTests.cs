using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Uji round-trip handler data dua jenis surat baru (SK_PINDAH dan
/// IZIN_KERAMAIAN) terhadap SQLite sungguhan: skema tabel diambil dari
/// desa.db.sql (bukan salinan manual), lalu insert → load → delete lewat
/// handler asli dipastikan konsisten.
/// </summary>
public sealed class HandlerDataBaruTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TidakAdaCache _cache = new();

    public HandlerDataBaruTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Orang tua (tabel Surat) tidak ikut dibuat — yang diuji SQL handler,
        // bukan perilaku foreign key SQLite, jadi pengecekan FK dimatikan saja.
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            pragma.ExecuteNonQuery();
        }

        // Ambil DDL kedua tabel langsung dari desa.db.sql supaya skema uji
        // selalu sama dengan skema yang dipakai aplikasi.
        string root = CariRootRepo() ?? throw new InvalidOperationException("Folder repo tidak ditemukan.");
        string skema = File.ReadAllText(Path.Combine(root, "desa.db.sql"));

        foreach (string namaTabel in new[] { "Pindah", "Keramaian" })
        {
            var cocok = Regex.Match(skema, $"CREATE TABLE IF NOT EXISTS \"{namaTabel}\"[\\s\\S]*?\\);");
            Assert.True(cocok.Success, $"CREATE TABLE {namaTabel} tidak ditemukan di desa.db.sql.");
            using var perintah = _connection.CreateCommand();
            perintah.CommandText = cocok.Value;
            perintah.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();

    private static SuratData SuratValid(int idSurat) => new()
    {
        ID_Surat = idSurat,
        NomorSurat = "470/001/Ds/2026",
        TanggalSurat = new DateTime(2026, 8, 14),
        NamaJenis = "SK_PINDAH"
    };

    [Fact]
    public async Task Pindah_InsertLoadDelete_RoundTrip()
    {
        var handler = new PindahDataHandler(NullLogger<PindahDataHandler>.Instance, _cache);
        var surat = SuratValid(1);
        surat.AlamatTujuanPindah = "Jl. Kenanga No. 5, Desa Waringin Jaya";
        surat.AlasanPindah = "Mengikuti tempat kerja";

        await handler.InsertRelatedDataAsync(1, surat, _connection, null!);

        var muat = new SuratData { ID_Surat = 1 };
        await handler.LoadRelatedDataAsync(muat, _connection);
        Assert.Equal(surat.AlamatTujuanPindah, muat.AlamatTujuanPindah);
        Assert.Equal(surat.AlasanPindah, muat.AlasanPindah);

        await handler.DeleteRelatedDataAsync(1, _connection, null!);
        var kosong = new SuratData { ID_Surat = 1 };
        await handler.LoadRelatedDataAsync(kosong, _connection);
        Assert.Null(kosong.AlamatTujuanPindah);
    }

    [Fact]
    public async Task Keramaian_InsertLoadDelete_RoundTrip()
    {
        var handler = new KeramaianDataHandler(NullLogger<KeramaianDataHandler>.Instance, _cache);
        var surat = SuratValid(2);
        surat.NamaJenis = SuratConstants.IZIN_KERAMAIAN;
        surat.JenisKegiatan = "Pengajian Rutin Malam Jumat";
        surat.TanggalKegiatan = "20-08-2026";
        surat.LokasiKegiatan = "Balai Desa Sukamaju";

        await handler.InsertRelatedDataAsync(2, surat, _connection, null!);

        var muat = new SuratData { ID_Surat = 2 };
        await handler.LoadRelatedDataAsync(muat, _connection);
        Assert.Equal(surat.JenisKegiatan, muat.JenisKegiatan);
        Assert.Equal(surat.TanggalKegiatan, muat.TanggalKegiatan);
        Assert.Equal(surat.LokasiKegiatan, muat.LokasiKegiatan);

        await handler.DeleteRelatedDataAsync(2, _connection, null!);
        var kosong = new SuratData { ID_Surat = 2 };
        await handler.LoadRelatedDataAsync(kosong, _connection);
        Assert.Null(kosong.JenisKegiatan);
    }

    // ===== Cache palsu tanpa-apa-apa: handler hanya memanggil RemoveAsync. =====
    private sealed class TidakAdaCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default) => factory();
        public Task SetAsync<T>(string key, T value, MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync<T>(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public bool TryGetValue<T>(string key, out T? value) { value = default; return false; }
        public void Set<T>(string key, T value, MemoryCacheEntryOptions? options = null) { }
        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public int GetCacheCount() => 0;
        public Task RemoveWargaAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static string? CariRootRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
