using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Sinkron;
using SuDesApp.Supabase;

namespace SuDesApp.Wpf.Services;

/// <summary>
/// Menerbitkan kop &amp; skema formulir ke Supabase untuk Android secara
/// otomatis: saat aplikasi mulai, saat pengaturan desa disimpan, dan saat
/// <c>SkemaFormulir.json</c> berubah di disk. Operator tidak perlu lagi
/// menekan tombol "Terbitkan" di halaman Sinkron Android.
///
/// <para>
/// Timer 60 detik memeriksa waktu ubah berkas skema; penerbitan memakai
/// scope DI sendiri dan tidak menumpuk (satu penerbitan pada satu waktu).
/// Tombol manual tetap ada sebagai cadangan/pemeriksaan.
/// </para>
/// </summary>
public sealed class PenerbitanSinkronLatar : IDisposable
{
    private static readonly TimeSpan JedaAwal = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JedaAntar = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PenerbitanSinkronLatar> _logger;
    private readonly System.Threading.Timer _timer;
    private int _sedangTerbit;
    private bool _berhenti;
    private DateTimeOffset _ubahSkemaTerakhir;

    public PenerbitanSinkronLatar(
        IServiceScopeFactory scopeFactory,
        ILogger<PenerbitanSinkronLatar> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timer = new System.Threading.Timer(_ => _ = TerbitkanBilaPerluAsync(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ubahSkemaTerakhir = WaktuUbahSkema();
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

    /// <summary>
    /// Terbitkan sekarang (dipanggil saat pengaturan desa disimpan).
    /// Fire-and-forget dari pemanggil UI; kegagalan hanya dicatat.
    /// </summary>
    public void MintaTerbit() => _ = TerbitkanBilaPerluAsync(terpaksa: true);

    private async Task TerbitkanBilaPerluAsync(bool terpaksa = false)
    {
        if (_berhenti || Interlocked.Exchange(ref _sedangTerbit, 1) == 1) return;

        try
        {
            var ubah = WaktuUbahSkema();
            var berubah = ubah != _ubahSkemaTerakhir;
            if (!terpaksa && !berubah) return;
            _ubahSkemaTerakhir = ubah;

            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var pengaturan = sp.GetRequiredService<SupabasePengaturan>();
            if (!pengaturan.Lengkap) return;

            var penjaga = sp.GetService<SuDesApp.Services.PenjagaLisensi>();
            if (penjaga is { SinkronTerkunci: true }) return;

            var penerbitSkema = sp.GetRequiredService<PenerbitSkemaFormulir>();
            var penerbitKop = sp.GetRequiredService<PenerbitPengaturanDesa>();

            var jumlah = await penerbitSkema.TerbitkanAsync().ConfigureAwait(false);
            if (jumlah < 0) return;

            await penerbitKop.TerbitkanAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "Penerbitan sinkron otomatis: {Jenis} jenis + kop diterbitkan.", jumlah);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Penerbitan sinkron otomatis dilewati.");
        }
        finally
        {
            Interlocked.Exchange(ref _sedangTerbit, 0);
        }
    }

    private static DateTimeOffset WaktuUbahSkema()
    {
        try
        {
            var path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Configuration", "SkemaFormulir.json");
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTimeOffset.MinValue;
        }
        catch
        {
            // Berkas tidak terbaca: kembalikan minimal supaya pemicu
            // berikutnya bekerja seperti biasanya.
            return DateTimeOffset.MinValue;
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
