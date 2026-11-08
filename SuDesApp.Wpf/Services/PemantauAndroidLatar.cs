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
/// Selain membaca ringkasan, pemantau ini mengimpor antrean otomatis saat
/// ada baris menunggu (saklar <c>Supabase:SinkronOtomatis</c>, bawaan aktif)
/// supaya slot antrean 10 surat tidak penuh menunggu klik operator. Impor
/// manual di halaman Sinkron tetap tersedia untuk memeriksa ulang.
/// Pemeriksaan impor dan pembacaan ringkasan memakai scope DI sendiri;
/// putaran yang lambat tidak menumpuk (satu pemeriksaan pada satu waktu).
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

    /// <summary>
    /// Dipanggil di thread latar setelah impor otomatis berhasil mengimpor
    /// satu surat atau lebih; pelanggan wajib memindahkannya ke UI sendiri.
    /// </summary>
    public event Action<SinkronHasil>? ImporOtomatisBerhasil;

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

            if (!_berhenti && ringkasan.Terkonfigurasi && ringkasan.JumlahAntrean > 0
                && PengaturanOtomatisSinkron.Aktif && !SinkronTerkunci(scope.ServiceProvider))
            {
                // Impor otomatis: pakai layanan sinkron dari scope yang sama
                // supaya tidak ada koneksi/modal yang bocor.
                var layanan = scope.ServiceProvider.GetRequiredService<SinkronSuratService>();
                var hasil = await layanan.SinkronkanAsync().ConfigureAwait(false);
                if (hasil.Berhasil > 0) ImporOtomatisBerhasil?.Invoke(hasil);
                if (hasil.AdaYangDiproses)
                {
                    _logger.LogInformation(
                        "Sinkron otomatis: {Berhasil} masuk, {Gagal} gagal, {Dilewati} dilewati.",
                        hasil.Berhasil, hasil.Gagal, hasil.Dilewati);
                    ringkasan = await pemantau.AmbilAsync().ConfigureAwait(false);
                }
            }

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

    /// <summary>
    /// Bila lisensi mengunci sinkron, impor otomatis tidak jalan — operator
    /// harus melihat pesan kunci yang sama seperti tombol manual.
    /// </summary>
    private static bool SinkronTerkunci(IServiceProvider sp)
    {
        try
        {
            var penjaga = sp.GetService<SuDesApp.Services.PenjagaLisensi>();
            return penjaga is { SinkronTerkunci: true };
        }
        catch
        {
            // Penjaga belum tersedia (mis. pada uji): anggap tidak terkunci.
            return false;
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
