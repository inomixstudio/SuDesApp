using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace SuDesApp.Supabase;

/// <summary>
/// Kepemilikan koneksi Supabase untuk satu putaran sinkron. Data source dibuat
/// sekali per proses (pooling internal Npgsql) dan dilepas bersama aplikasi.
/// </summary>
public sealed class SupabaseKoneksi : IAsyncDisposable
{
    private readonly NpgsqlDataSource _sumber;

    public SupabaseKoneksi(SupabasePengaturan pengaturan)
    {
        if (pengaturan is null) throw new System.ArgumentNullException(nameof(pengaturan));
        _sumber = NpgsqlDataSource.Create(pengaturan.ConnectionString);
    }

    /// <summary>Buka koneksi baru dari pool. Pemanggil bertanggung jawab melepas.</summary>
    public async ValueTask<NpgsqlConnection> BukaAsync(CancellationToken ct = default)
    {
        var conn = await _sumber.OpenConnectionAsync(ct).ConfigureAwait(false);
        return conn;
    }

    public ValueTask DisposeAsync() => _sumber.DisposeAsync();
}
