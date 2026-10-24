using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Kemajuan unduhan berkas: byte yang sudah terkumpul, totalnya, dan persentase.
    /// Dipakai halaman Pembaruan untuk menampilkan "6,4 MB dari 15,2 MB (42%)".
    /// </summary>
    public sealed class KemajuanUnduhan
    {
        public KemajuanUnduhan(long terunduh, long total)
        {
            Terunduh = terunduh < 0 ? 0 : terunduh;
            Total = total < 0 ? 0 : total;
        }

        /// <summary>Byte yang sudah tersimpan (termasuk bagian dari sesi sebelumnya).</summary>
        public long Terunduh { get; }

        /// <summary>Ukuran berkas penuh; 0 bila server tidak menyebutkannya.</summary>
        public long Total { get; }

        /// <summary>Persentase 0-100 (0 bila total belum diketahui).</summary>
        public int Persen => Total > 0 ? (int)Math.Clamp(Terunduh * 100 / Total, 0, 100) : 0;

        /// <summary>Ringkasan siap tampil, mis. "6,4 MB dari 15,2 MB (42%)".</summary>
        public string Teks => Total > 0
            ? $"{Ukuran(Terunduh)} dari {Ukuran(Total)} ({Persen}%)"
            : $"{Ukuran(Terunduh)} terunduh";

        /// <summary>Ubah jumlah byte menjadi teks singkat ("480 B", "1,2 MB").</summary>
        public static string Ukuran(long byteCount)
        {
            if (byteCount < 1024) return $"{byteCount} B";
            double kb = byteCount / 1024d;
            if (kb < 1024) return kb.ToString("0", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024d;
            if (mb < 1024) return mb.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024d).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }

    /// <summary>
    /// Unduhan satu berkas yang bisa <b>dijeda dan dilanjutkan</b> tanpa mengunduh
    /// ulang bagian yang sudah ada.
    ///
    /// Bagian yang sudah terunduh disimpan di berkas sementara (<c>&lt;tujuan&gt;.part</c>),
    /// bukan di memori, sehingga tidak hilang bila unduhan dijeda, koneksi putus,
    /// atau aplikasi ditutup. Saat dilanjutkan, permintaan dikirim dengan header
    /// <c>Range</c> mulai dari panjang berkas sementara, dan sisanya disambung ke
    /// berkas yang sama.
    ///
    /// Bila server menolak lanjutan (tidak mendukung <c>Range</c> atau berkasnya
    /// sudah berbeda), unduhan dimulai ulang dari awal supaya berkas hasilnya tidak
    /// bercampur dua versi. Bila <c>sha256Harapan</c> diberikan, sidik jari diperiksa
    /// sebelum berkas dipindahkan ke tujuan akhir; ketidakcocokan menghapus hasil
    /// unduhan dan melaporkan galat — jadi berkas rusak tidak pernah dipakai.
    ///
    /// Pembatalan token sengaja <b>tidak</b> menghapus berkas sementara (itulah
    /// makna "dijeda"); pemanggil yang ingin membuang sisa unduhan memanggil
    /// <see cref="Bersihkan"/>.
    /// </summary>
    public sealed class UnduhanBerkasBerlanjut
    {
        /// <summary>Akhiran berkas sementara yang menyimpan bagian yang sudah terunduh.</summary>
        public const string AkhiranSementara = ".part";

        private const int UkuranBuffer = 81920;
        private const int JedaLaporMilidetik = 200;

        private readonly string _url;
        private readonly string? _sha256Harapan;
        private readonly ILogger _logger;
        private readonly Action<HttpRequestMessage>? _siapkanPermintaan;
        private readonly Func<HttpClient> _buatKlien;

        private int _persenTerakhir = -1;
        private DateTime _laporTerakhir = DateTime.MinValue;

        public UnduhanBerkasBerlanjut(
            string url,
            string tujuan,
            string? sha256Harapan = null,
            ILogger? logger = null,
            Action<HttpRequestMessage>? siapkanPermintaan = null,
            Func<HttpClient>? buatKlien = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Tautan unduhan tidak boleh kosong.", nameof(url));
            if (string.IsNullOrWhiteSpace(tujuan))
                throw new ArgumentException("Jalur tujuan unduhan tidak boleh kosong.", nameof(tujuan));

            _url = url;
            Tujuan = tujuan;
            _sha256Harapan = sha256Harapan;
            _logger = logger ?? NullLogger.Instance;
            _siapkanPermintaan = siapkanPermintaan;
            _buatKlien = buatKlien ?? (() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        }

        /// <summary>Jalur berkas akhir yang dipakai setelah unduhan tuntas.</summary>
        public string Tujuan { get; }

        /// <summary>Berkas sementara yang menyimpan bagian yang sudah terunduh.</summary>
        public string JalurSementara => Tujuan + AkhiranSementara;

        /// <summary>Byte yang sudah tersimpan (termasuk dari sesi sebelumnya).</summary>
        public long Terunduh { get; private set; }

        /// <summary>Ukuran berkas penuh menurut server; 0 bila belum diketahui.</summary>
        public long Total { get; private set; }

        /// <summary>True setelah berkas selesai diverifikasi dan dipindahkan ke tujuan.</summary>
        public bool Selesai { get; private set; }

        /// <summary>
        /// True bila server menyediakan unduhan lanjutan (Range) — diketahui setelah
        /// percobaan pertama; sebelum itu nilainya false.
        /// </summary>
        public bool BisaDilanjutkan { get; private set; }

        /// <summary>Persentase 0-100 (0 bila total belum diketahui).</summary>
        public int Persen => Total > 0 ? (int)Math.Clamp(Terunduh * 100 / Total, 0, 100) : (Selesai ? 100 : 0);

        /// <summary>True bila masih ada bagian yang bisa dilanjutkan (belum selesai, sudah ada byte).</summary>
        public bool AdaSisa => !Selesai && Terunduh > 0;

        /// <summary>Ringkasan sisa unduhan siap tampil, mis. "6,4 MB dari 15,2 MB (42%)".</summary>
        public string Ringkasan => Total > 0
            ? $"{KemajuanUnduhan.Ukuran(Terunduh)} dari {KemajuanUnduhan.Ukuran(Total)} ({Persen}%)"
            : $"{KemajuanUnduhan.Ukuran(Terunduh)} terunduh";

        /// <summary>
        /// Selaraskan keadaan dengan isi disk: dipakai setelah aplikasi dibuka lagi
        /// untuk mengetahui sudah berapa byte yang tersimpan dari percobaan sebelumnya.
        /// </summary>
        public long SelarasDenganDisk()
        {
            try
            {
                Terunduh = File.Exists(JalurSementara) ? new FileInfo(JalurSementara).Length : 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ukuran berkas sementara unduhan tidak terbaca.");
                Terunduh = 0;
            }

            return Terunduh;
        }

        /// <summary>
        /// Unduh atau lanjutkan sampai tuntas. Bila token dibatalkan (jeda), bagian
        /// yang sudah terunduh tetap tersimpan dan unduhan bisa dilanjutkan kapan saja.
        /// </summary>
        /// <returns>True bila berkas selesai (opsional sudah terverifikasi).</returns>
        public async Task<bool> UnduhAsync(IProgress<KemajuanUnduhan>? progress = null, CancellationToken ct = default)
        {
            SiapkanFolder();
            SelarasDenganDisk();

            // Berkas sementara dari percobaan sebelumnya sudah lengkap (mis. aplikasi
            // ditutup tepat sebelum dipindahkan): cukup diverifikasi, tanpa unduh ulang.
            if (Terunduh > 0 && SelesaikanDariSisaLengkap())
            {
                progress?.Report(new KemajuanUnduhan(Terunduh, Total));
                return true;
            }

            using var http = _buatKlien();
            long mulaiDari = Terunduh;

            var hasil = await UnduhSekaliAsync(http, mulaiDari, progress, ct);

            if (hasil.PerluUlangDariAwal)
            {
                _logger.LogInformation(
                    "Lanjutan unduhan tidak bisa dipakai ({Alasan}); unduhan dimulai ulang dari awal.", hasil.Alasan);
                Bersihkan();
                await UnduhSekaliAsync(http, 0, progress, ct);
            }

            if (Total > 0 && Terunduh < Total)
            {
                // Koneksi putus sebelum tuntas. Sisanya tetap tersimpan supaya bisa
                // dilanjutkan; pemanggil boleh mencoba lagi kapan saja.
                throw new IOException(
                    $"Unduhan terhenti di {Persen}% ({Ringkasan}). Sisa unduhan tetap tersimpan dan bisa dilanjutkan.");
            }

            SelesaikanKeTujuan(progress);
            return true;
        }

        /// <summary>Buang sisa unduhan yang tersimpan (dipakai bila pengguna membatalkan).</summary>
        public void Bersihkan()
        {
            TryDelete(JalurSementara);
            Terunduh = 0;
            Total = 0;
            Selesai = false;
            _persenTerakhir = -1;
        }

        /// <summary>
        /// Satu permintaan HTTP: menyambung dari <paramref name="mulaiDari"/> byte.
        /// Mengembalikan penanda bahwa permintaan perlu diulang dari awal beserta alasannya.
        /// </summary>
        private async Task<(bool PerluUlangDariAwal, string? Alasan)> UnduhSekaliAsync(
            HttpClient http, long mulaiDari, IProgress<KemajuanUnduhan>? progress, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.UserAgent.ParseAdd("SuDesApp-Updater");
            _siapkanPermintaan?.Invoke(request);
            if (mulaiDari > 0)
            {
                request.Headers.Range = new RangeHeaderValue(mulaiDari, null);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (mulaiDari > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                return (true, "sisa berkas lebih panjang daripada berkas di server");
            }

            response.EnsureSuccessStatusCode();

            bool lanjutan = response.StatusCode == HttpStatusCode.PartialContent;
            if (mulaiDari > 0 && !lanjutan)
            {
                return (true, "server tidak melayani unduhan lanjutan");
            }

            BisaDilanjutkan = lanjutan ||
                (response.Headers.AcceptRanges?.Contains("bytes", StringComparer.OrdinalIgnoreCase) ?? false);
            Total = HitungTotal(response, mulaiDari);

            {
                // Lingkup terpisah: kedua aliran harus tertutup sebelum berkas
                // sementara dipindahkan (Windows mengunci berkas yang masih terbuka).
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(
                    JalurSementara,
                    mulaiDari > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    UkuranBuffer,
                    useAsync: true);

                var buffer = new byte[UkuranBuffer];
                long terunduhSesi = 0;
                int dibaca;

                while ((dibaca = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, dibaca), ct);
                    terunduhSesi += dibaca;
                    Terunduh = mulaiDari + terunduhSesi;
                    LaporkanKemajuan(progress);
                }

                await target.FlushAsync(ct);
            }

            return (false, null);
        }

        /// <summary>Ukuran berkas penuh dari header Content-Range atau Content-Length.</summary>
        private static long HitungTotal(HttpResponseMessage response, long mulaiDari)
        {
            if (response.Content.Headers.ContentRange?.Length is long panjang && panjang > 0)
            {
                return panjang;
            }

            if (response.Content.Headers.ContentLength is long isi && isi > 0)
            {
                return mulaiDari + isi;
            }

            return 0;
        }

        /// <summary>
        /// Sisa unduhan sebelumnya ternyata sudah lengkap (sidik jarinya cocok):
        /// pindahkan ke tujuan tanpa meminta apa pun lagi ke server.
        /// </summary>
        private bool SelesaikanDariSisaLengkap()
        {
            // Tanpa sidik jari resmi kelengkapan tidak bisa dipastikan, jadi unduhan
            // tetap dilanjutkan sampai server menyatakan selesai.
            if (string.IsNullOrWhiteSpace(_sha256Harapan))
            {
                return false;
            }

            if (!UpdateService.VerifySha256(JalurSementara, _sha256Harapan!))
            {
                return false;
            }

            Total = Terunduh;
            PindahkanKeTujuan();
            return true;
        }

        /// <summary>Verifikasi (bila ada sidik jari) lalu pindahkan berkas sementara ke tujuan.</summary>
        private void SelesaikanKeTujuan(IProgress<KemajuanUnduhan>? progress)
        {
            if (!string.IsNullOrWhiteSpace(_sha256Harapan) &&
                !UpdateService.VerifySha256(JalurSementara, _sha256Harapan!))
            {
                Bersihkan();
                throw new InvalidOperationException(
                    "Verifikasi berkas gagal: SHA-256 tidak cocok. Unduhan dibatalkan dan berkas sisa dibuang.");
            }

            PindahkanKeTujuan();
            progress?.Report(new KemajuanUnduhan(Total > 0 ? Total : Terunduh, Total));
        }

        private void PindahkanKeTujuan()
        {
            SiapkanFolder();
            File.Move(JalurSementara, Tujuan, overwrite: true);
            Selesai = true;
            _logger.LogInformation("Unduhan selesai: {Tujuan}", Tujuan);
        }

        private void SiapkanFolder()
        {
            var folder = Path.GetDirectoryName(Tujuan);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        /// <summary>
        /// Laporkan kemajuan tanpa membanjiri UI: satu laporan per perubahan persen,
        /// dan paling sering setiap 200 ms.
        /// </summary>
        private void LaporkanKemajuan(IProgress<KemajuanUnduhan>? progress)
        {
            if (progress == null)
            {
                return;
            }

            var sekarang = DateTime.UtcNow;
            if (Persen == _persenTerakhir &&
                (sekarang - _laporTerakhir).TotalMilliseconds < JedaLaporMilidetik)
            {
                return;
            }

            _persenTerakhir = Persen;
            _laporTerakhir = sekarang;
            progress.Report(new KemajuanUnduhan(Terunduh, Total));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Pembersihan terbaik: berkas bisa sedang dipakai proses lain.
            }
        }
    }
}
