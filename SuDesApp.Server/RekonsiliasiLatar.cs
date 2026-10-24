namespace SuDesApp.Server;

/// <summary>
/// Penjadwal rekonsiliasi: menjalankan <see cref="LayananRekonsiliasi"/> secara
/// berkala selama server hidup.
///
/// Sengaja hanya "pemicu tipis": seluruh aturannya ada di layanan, sehingga
/// perilaku pembayaran tidak bergantung pada apakah ia dipicu timer, endpoint
/// admin, atau uji. Galat apa pun ditelan di sini — pekerja latar yang mati
/// karena satu pengecualian berarti tidak ada backstop pembayaran sama sekali.
/// </summary>
public sealed class RekonsiliasiLatar : BackgroundService
{
    private readonly LayananRekonsiliasi _layanan;
    private readonly OpsiRekonsiliasi _opsi;
    private readonly ILogger<RekonsiliasiLatar> _logger;

    public RekonsiliasiLatar(
        LayananRekonsiliasi layanan,
        OpsiRekonsiliasi opsi,
        ILogger<RekonsiliasiLatar> logger)
    {
        _layanan = layanan;
        _opsi = opsi;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opsi.Terjadwal)
        {
            ServerLog.RekonsiliasiLatarMati(_logger);
            return;
        }

        // Angka ditulis dalam satuan yang jelas (menit/hari), bukan TimeSpan
        // mentah: "00:15:00 menit" sulit dibaca dan mudah disalahartikan saat
        // operator memeriksa apakah pengaturannya benar-benar diterapkan.
        ServerLog.RekonsiliasiLatarAktif(
            _logger, _opsi.Interval.TotalMinutes, _opsi.MinimalUmur.TotalMinutes,
            _opsi.MaksimalUmur.TotalDays, _opsi.Batas);

        // Beri jalan bagi start server (migrasi skema, verifikasi RLS, dan
        // permintaan web yang mungkin menumpuk) sebelum menambah beban.
        if (_opsi.JedaAwal > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(_opsi.JedaAwal, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        using var timer = new PeriodicTimer(_opsi.Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await JalankanSatuPutaranAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Satu putaran, tanpa pernah melempar — dipakai juga oleh endpoint admin.</summary>
    public async Task JalankanSatuPutaranAsync(CancellationToken ct = default)
    {
        try
        {
            await _layanan.JalankanAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Server sedang berhenti — bukan kegagalan.
        }
        catch (Exception ex)
        {
            ServerLog.RekonsiliasiPutaranGagal(_logger, ex);
        }
    }
}
