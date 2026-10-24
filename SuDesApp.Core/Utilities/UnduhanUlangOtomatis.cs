using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Kabar satu percobaan unduhan yang gagal, baik yang akan diulang maupun yang
    /// membuat unduhan menyerah. Sudah berisi teks siap tampil supaya halaman
    /// pembaruan tidak menyusun kalimatnya sendiri.
    /// </summary>
    public sealed record PercobaanUnduhanUlang(
        int Percobaan,
        int MaksPercobaan,
        TimeSpan Jeda,
        string Alasan,
        bool Menyerah)
    {
        /// <summary>Jeda dalam kata, mis. "12 dtk" atau "1 menit".</summary>
        public string TeksJeda => Jeda.TotalSeconds >= 60
            ? $"{Jeda.TotalMinutes:0.#} menit"
            : $"{Jeda.TotalSeconds:0} dtk";

        /// <summary>Keterangan siap tampil untuk statusbar/kartu halaman.</summary>
        public string Teks => Menyerah
            ? $"Koneksi bermasalah dan percobaan sudah habis ({MaksPercobaan}×): {Alasan}"
            : $"Koneksi terputus ({Alasan}) — mencoba lagi dalam {TeksJeda} " +
              $"(percobaan {Percobaan} dari {MaksPercobaan})";

        /// <summary>Percobaan yang menyerah (tidak diulang lagi).</summary>
        public static PercobaanUnduhanUlang Habis(int percobaan, int maks, string alasan)
            => new(percobaan, maks, TimeSpan.Zero, alasan, Menyerah: true);
    }

    /// <summary>
    /// Kebijakan coba ulang unduhan: berapa kali boleh dicoba, jeda yang bertambah
    /// setiap kegagalan, batas jeda tertinggi, dan galat mana yang layak diulang.
    ///
    /// Dipisah dari pelaksananya supaya aturannya bisa diperiksa uji tanpa menunggu
    /// jeda sungguhan.
    /// </summary>
    public sealed class KebijakanUnduhanUlang
    {
        /// <summary>Jumlah maksimal percobaan (termasuk percobaan pertama).</summary>
        public int MaksPercobaan { get; init; } = 6;

        /// <summary>Jeda sebelum percobaan ulang pertama.</summary>
        public TimeSpan JedaAwal { get; init; } = TimeSpan.FromSeconds(3);

        /// <summary>Pengali jeda setiap kegagalan berikutnya (3 dtk → 6 dtk → 12 dtk …).</summary>
        public double Pengali { get; init; } = 2;

        /// <summary>Batas jeda tertinggi — supaya unduhan lama tidak menunggu tak wajar.</summary>
        public TimeSpan JedaMaksimum { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>Jeda sebelum percobaan ulang ke-<paramref name="percobaanYangGagal"/>+1.</summary>
        public TimeSpan Jeda(int percobaanYangGagal)
        {
            int langkah = Math.Max(0, percobaanYangGagal - 1);
            double milidetik = JedaAwal.TotalMilliseconds * Math.Pow(Pengali, langkah);

            if (double.IsNaN(milidetik) || double.IsInfinity(milidetik))
            {
                return JedaMaksimum;
            }

            return TimeSpan.FromMilliseconds(Math.Min(milidetik, JedaMaksimum.TotalMilliseconds));
        }

        /// <summary>
        /// Apakah galat ini layak dicoba lagi? Koneksi putus, DNS tidak terjawab, dan
        /// unduhan yang terhenti di tengah jalan layak diulang; kegagalan yang pasti
        /// (mis. sidik jari berkas tidak cocok) tidak — mengulanginya hanya membuang waktu.
        /// </summary>
        public bool LayakDiulang(Exception? galat) => galat switch
        {
            null => false,

            // Timeout koneksi: TaskCanceledException dari HttpClient TANPA permintaan
            // pembatalan pengguna — layak diulang (batal pengguna sudah disaring lebih dulu).
            TaskCanceledException => true,
            OperationCanceledException => false,

            HttpRequestException => true,
            System.Net.Sockets.SocketException => true,

            // Berkas sisa yang sedang dipakai proses lain bukan gangguan koneksi:
            // mengulanginya tidak akan pernah berhasil.
            IOException io when BerkasSedangDipakai(io) => false,
            IOException => true, // termasuk "unduhan terhenti di 42%" dari UnduhanBerlanjut
            _ => false
        };

        /// <summary>
        /// True bila galat I/O ini sebenarnya berkas sisa unduhan yang dikunci proses lain
        /// (mis. aplikasi dibuka dua kali atau unduhan yang sama sedang berjalan). Bukan
        /// gangguan koneksi, jadi tidak perlu diulang berulang kali.
        /// </summary>
        private static bool BerkasSedangDipakai(IOException galat)
        {
            const int PelanggaranBerbagi = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
            const int PelanggaranKunci = unchecked((int)0x80070021);   // ERROR_LOCK_VIOLATION

            if (galat.HResult == PelanggaranBerbagi || galat.HResult == PelanggaranKunci)
            {
                return true;
            }

            var pesan = galat.Message ?? string.Empty;
            return pesan.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                   pesan.Contains("sedang digunakan oleh proses lain", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Menjalankan sebuah unduhan yang bisa dilanjutkan dan mengulanginya SENDIRI bila
    /// koneksi terputus: setiap percobaan melanjutkan dari byte yang sudah tersimpan
    /// (berkas <c>.part</c>), jedanya bertambah, dan pengguna tidak perlu menekan apa pun.
    ///
    /// Jeda & batal dari pengguna BUKAN gangguan koneksi: token yang dibatalkan
    /// dilaporkan sebagai <see cref="OperationCanceledException"/> ke pemanggil, sehingga
    /// unduhan tetap berhenti seketika dan sisanya tersimpan (bisa dilanjutkan kapan saja).
    /// </summary>
    public sealed class UnduhanUlangOtomatis
    {
        private readonly KebijakanUnduhanUlang _kebijakan;
        private readonly ILogger? _logger;
        private readonly Func<TimeSpan, CancellationToken, Task> _tunggu;

        public UnduhanUlangOtomatis(
            KebijakanUnduhanUlang? kebijakan = null,
            ILogger? logger = null,
            Func<TimeSpan, CancellationToken, Task>? tunggu = null)
        {
            _kebijakan = kebijakan ?? new KebijakanUnduhanUlang();
            _logger = logger;
            _tunggu = tunggu ?? ((jeda, ct) => Task.Delay(jeda, ct));
        }

        /// <summary>Berapa kali percobaan diulang pada unduhan yang baru saja dijalankan.</summary>
        public int PercobaanUlang { get; private set; }

        /// <summary>Berapa percobaan yang benar-benar dipakai (1 = langsung berhasil).</summary>
        public int PercobaanDipakai { get; private set; }

        /// <summary>Galat terakhir; null bila unduhan berhasil.</summary>
        public Exception? GalatTerakhir { get; private set; }

        /// <summary>
        /// Jalankan <paramref name="percobaan"/> sampai berhasil, sampai galatnya tidak
        /// layak diulang, atau sampai percobaan habis.
        /// </summary>
        /// <returns>True bila unduhan berhasil; false bila menyerah (lihat <see cref="GalatTerakhir"/>).</returns>
        public async Task<bool> JalankanAsync(
            Func<CancellationToken, Task> percobaan,
            IProgress<PercobaanUnduhanUlang>? lapor = null,
            CancellationToken ct = default)
        {
            if (percobaan == null) throw new ArgumentNullException(nameof(percobaan));

            PercobaanUlang = 0;
            PercobaanDipakai = 0;
            GalatTerakhir = null;

            for (int nomor = 1; ; nomor++)
            {
                ct.ThrowIfCancellationRequested();
                PercobaanDipakai = nomor;

                try
                {
                    await percobaan(ct);
                    GalatTerakhir = null;
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Jeda/Batal pengguna: bukan gangguan koneksi — jangan diulang.
                    throw;
                }
                catch (Exception ex)
                {
                    GalatTerakhir = ex;

                    bool masihBoleh = _kebijakan.LayakDiulang(ex) && nomor < _kebijakan.MaksPercobaan;
                    if (!masihBoleh)
                    {
                        _logger?.LogWarning(ex,
                            "Unduhan menyerah setelah {Percobaan} percobaan.", nomor);
                        lapor?.Report(PercobaanUnduhanUlang.Habis(nomor, _kebijakan.MaksPercobaan, Ringkas(ex.Message)));
                        return false;
                    }

                    var jeda = _kebijakan.Jeda(nomor);
                    PercobaanUlang++;

                    _logger?.LogWarning(ex,
                        "Unduhan gagal pada percobaan {Percobaan}; diulang otomatis dalam {Jeda} detik.",
                        nomor, jeda.TotalSeconds);

                    lapor?.Report(new PercobaanUnduhanUlang(
                        nomor + 1, _kebijakan.MaksPercobaan, jeda, Ringkas(ex.Message), Menyerah: false));

                    await _tunggu(jeda, ct);
                }
            }
        }

        /// <summary>Ringkas pesan galat agar muat di statusbar/kartu.</summary>
        private static string Ringkas(string? pesan)
        {
            var bersih = (pesan ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (bersih.Length == 0)
            {
                return "koneksi terputus";
            }

            return bersih.Length <= 120 ? bersih : bersih[..120] + "…";
        }
    }
}
