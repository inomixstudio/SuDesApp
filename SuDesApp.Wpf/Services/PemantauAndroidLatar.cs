using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Sinkron;

namespace SuDesApp.Wpf.Services;

/// <summary>
/// Pemeriksa keadaan sinkron Android berkala untuk status bar. Berjalan sejak
/// jendela utama dibuat (aplikasi baru dijalankan) sehingga koneksi Supabase
/// dipakai otomatis, tanpa operator membuka halaman Sinkron lebih dulu.
///
/// <para>
/// Setiap putaran memakai scope DI sendiri: koneksi Npgsql dan SUPABASE
/// preference tidak ditahan sepanjang umur aplikasi, dan putaran yang lambat
/// tidak menumpuk (satu pemeriksaan pada satu waktu).
/// </para>
/// </summary>
public sealed class PemantauAndroidLatar : IDisposable
{
    private static readonly TimeSpan JedaAwal = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan JedaAntar = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PemantauAndroidLatar> _logger;
    private readonly System.Threading.Timer _timer;
    private int _sedangDiperiksa;
    private bool _berhenti;

    /// <summary>Dipanggil di thread latar; pelanggan wajib memindahkannya ke UI sendiri.</summary>
    public event Action<RingkasanAndroid>? Berubah;

    public PemantauAndroidLatar(
        IServiceScopeFactory scopeFactory,
        ILogger<PemantauAndroidLatar> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timer = new System.Threading.Timer(_ => _ = PeriksaAsync(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Mulai()
    {
        if (_berhenti) return;
        _timer.Change(JedaAwal, JedaAntar);
    }

    public void Berhenti()
    {
        _berhenti = true;
        try
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Sudah dibuang — tidak ada yang perlu dihentikan.
        }
    }

    private async Task PeriksaAsync()
    {
        if (_berhenti || Interlocked.Exchange(ref _sedangDiperiksa, 1) == 1) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pemantau = scope.ServiceProvider.GetRequiredService<PemantauAndroid>();
            var ringkasan = await pemantau.AmbilAsync().ConfigureAwait(false);
            if (!_berhenti) Berubah?.Invoke(ringkasan);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pemeriksaan sinkron Android dilewati.");
        }
        finally
        {
            Interlocked.Exchange(ref _sedangDiperiksa, 0);
        }
    }

    public void Dispose()
    {
        Berhenti();
        try
        {
            _timer.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Dispose ganda diabaikan.
        }
    }
}
