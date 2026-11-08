using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Supabase;

namespace SuDesApp.Sinkron;

/// <summary>
/// Keadaan sinkronisasi Android yang ringkas — cukup untuk chip status bar:
/// apakah Supabase sudah terkonfigurasi, berapa surat menunggu di antrean, dan
/// perangkat mana yang baru melapor (online).
/// </summary>
public sealed record RingkasanAndroid(
    bool Terkonfigurasi,
    int JumlahAntrean,
    string? NamaPerangkat,
    DateTime? TerakhirOnline)
{
    /// <summary>Belum ada konfigurasi Supabase: status bar tidak menampilkan apa pun.</summary>
    public static RingkasanAndroid BelumDikonfigurasi { get; } = new(false, 0, null, null);

    public bool AdaPerangkatTerhubung => !string.IsNullOrWhiteSpace(NamaPerangkat);
}

/// <summary>
/// Membaca keadaan sinkronisasi dari Supabase untuk ditampilkan di desktop.
///
/// <para>
/// Perangkat dianggap terhubung bila ia melapor dalam <see cref="BatasOnline"/>
/// terakhir (RPC <c>tandai_perangkat_online</c> dari aplikasi Android).
/// Pemeriksaan ini hanya membaca; tidak pernah mengubah data surat.
/// </para>
/// </summary>
public sealed class PemantauAndroid
{
    /// <summary>Jendela waktu sebuah perangkat masih dianggap tersambung.</summary>
    public static readonly TimeSpan BatasOnline = TimeSpan.FromMinutes(3);

    private readonly SupabasePengaturan _pengaturan;
    private readonly PerangkaianPerangkat _perangkat;
    private readonly ILogger<PemantauAndroid> _logger;

    public PemantauAndroid(
        SupabasePengaturan pengaturan,
        PerangkaianPerangkat perangkat,
        ILogger<PemantauAndroid> logger)
    {
        _pengaturan = pengaturan ?? throw new ArgumentNullException(nameof(pengaturan));
        _perangkat = perangkat ?? throw new ArgumentNullException(nameof(perangkat));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Ambil ringkasan terkini. Tidak pernah melempar kecuali dibatalkan.</summary>
    public async Task<RingkasanAndroid> AmbilAsync(CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap) return RingkasanAndroid.BelumDikonfigurasi;

        try
        {
            var antrean = await HitungAntreanAsync(ct).ConfigureAwait(false);
            var daftar = await _perangkat.AmbilDaftarAsync(ct).ConfigureAwait(false);

            var batas = DateTime.UtcNow - BatasOnline;
            var online = daftar
                .Where(d => d.Aktif && d.TerakhirOnline is DateTime waktu)
                .Where(d => DateTime.SpecifyKind(d.TerakhirOnline!.Value, DateTimeKind.Utc) >= batas)
                .OrderByDescending(d => d.TerakhirOnline)
                .FirstOrDefault();

            return new RingkasanAndroid(true, antrean, online?.Nama, online?.TerakhirOnline);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline atau kredensial berubah: status bar cukup diam, halaman
            // Sinkron tetap menampilkan galatnya sendiri.
            _logger.LogDebug(ex, "Ringkasan sinkron Android tidak terbaca.");
            return new RingkasanAndroid(true, 0, null, null);
        }
    }

    private async Task<int> HitungAntreanAsync(CancellationToken ct)
    {
        await using var koneksi = new SupabaseKoneksi(_pengaturan);
        await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM antrean_surat WHERE tenant_id = @tenant AND status = 'menunggu'", conn);
        cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));

        var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return hasil is null or DBNull ? 0 : Convert.ToInt32(hasil);
    }
}
