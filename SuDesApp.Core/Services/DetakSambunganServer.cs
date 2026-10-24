using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Services
{
    /// <summary>
    /// Denyut sambungan server lisensi: menanyakan <c>/healthz</c> secara berkala
    /// supaya <see cref="SambunganServerLisensi"/> tidak hanya diperbarui saat
    /// pengguna membuka halaman Langganan atau menekan tombol.
    ///
    /// Kenapa perlu: sebelumnya chip "Belum terjangkau" baru berubah setelah
    /// halaman dibuka/diuji ulang, sehingga server yang menyala belakangan tetap
    /// terlihat mati sampai pengguna membuka ulang halaman. Denyut ini membuat
    /// status pulih sendiri — tiap 30 detik selama belum tersambung, lalu tiap
    /// 5 menit setelah tersambung supaya jaringan desa tidak dibebani.
    ///
    /// Aturan kejujuran sama dengan <see cref="LanggananKlien"/>: balasan apa pun
    /// (termasuk 5xx) berarti server menjawab sehingga Terhubung; hanya kegagalan
    /// jaringan sungguhan yang menandai TidakTerjangkau. Gagal lokal (mis. alamat
    /// tidak sah) hanya dicatat ke log dan tidak mengubah keadaan.
    /// </summary>
    public sealed class DetakSambunganServer : IDisposable
    {
        /// <summary>Jeda pemeriksaan selama server belum terbukti tersambung.</summary>
        public static readonly TimeSpan JedaSaatBelumTerhubung = TimeSpan.FromSeconds(30);

        /// <summary>Jeda pemeriksaan setelah server terbukti menjawab.</summary>
        public static readonly TimeSpan JedaSaatTerhubung = TimeSpan.FromMinutes(5);

        private readonly LisensiManager _lisensi;
        private readonly HttpClient _http;
        private readonly ILogger<DetakSambunganServer> _logger;
        private readonly SambunganServerLisensi _sambungan;
        private readonly object _kunci = new();

        private CancellationTokenSource? _berhenti;
        private Task? _tugas;

        public DetakSambunganServer(LisensiManager lisensi, HttpClient http,
            ILogger<DetakSambunganServer> logger, SambunganServerLisensi sambungan)
        {
            _lisensi = lisensi ?? throw new ArgumentNullException(nameof(lisensi));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sambungan = sambungan ?? throw new ArgumentNullException(nameof(sambungan));
        }

        /// <summary>Jeda berikutnya menurut keadaan sambungan terakhir.</summary>
        public static TimeSpan Jeda(KeadaanSambunganServer keadaan) =>
            keadaan == KeadaanSambunganServer.Terhubung ? JedaSaatTerhubung : JedaSaatBelumTerhubung;

        /// <summary>Mulai denyut di latar belakang. Aman dipanggil berkali-kali.</summary>
        public void Mulai()
        {
            lock (_kunci)
            {
                if (_tugas != null) return;
                _berhenti = new CancellationTokenSource();
                _tugas = JalankanAsync(_berhenti.Token);
            }
        }

        /// <summary>Hentikan denyut. Aman dipanggil kapan saja, termasuk sebelum Mulai.</summary>
        public void Berhenti()
        {
            lock (_kunci)
            {
                if (_berhenti == null) return;
                try { _berhenti.Cancel(); } catch { }
                _berhenti.Dispose();
                _berhenti = null;
                _tugas = null;
            }
        }

        public void Dispose() => Berhenti();

        /// <summary>
        /// Satu putaran pemeriksaan: tanya <c>/healthz</c>, lalu catat hasilnya.
        /// Tidak pernah melempar untuk kegagalan jaringan; hanya pembatalan
        /// pemanggil yang diteruskan. Dipisah dari loop supaya bisa diuji
        /// langsung tanpa menunggu timer.
        /// </summary>
        public async Task CekSekaliAsync(CancellationToken cancellationToken = default)
        {
            var alamat = _lisensi.AlamatEndpoint("healthz");
            if (alamat.Length == 0)
            {
                // Belum ada alamat untuk dihubungi — bukan kegagalan server.
                return;
            }

            try
            {
                using var resp = await _http.GetAsync(alamat, cancellationToken).ConfigureAwait(false);

                // Balasan apa pun — termasuk 5xx — berarti server hidup dan
                // alamatnya benar; yang gagal adalah layanannya, bukan koneksi.
                _sambungan.CatatTerhubung();
                _logger.LogDebug("Healthz server lisensi menjawab HTTP {Status}.", (int)resp.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Satu-satunya kegagalan yang berarti "tidak terjangkau":
                // permintaan tidak pernah sampai (DNS, koneksi ditolak, waktu habis).
                _sambungan.CatatTidakTerjangkau();
                _logger.LogDebug(ex, "Healthz server lisensi tidak terjangkau.");
            }
            catch (Exception ex)
            {
                // Gagal lokal (mis. alamat tidak sah) tidak membuktikan server mati.
                _logger.LogWarning(ex, "Pemeriksaan healthz server lisensi gagal; keadaan tidak diubah.");
            }
        }

        private async Task JalankanAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CekSekaliAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await Task.Delay(Jeda(_sambungan.Sekarang), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
