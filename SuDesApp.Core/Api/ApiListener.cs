using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Api
{
    /// <summary>
    /// Listener HTTP lokal untuk API desa, memakai <see cref="HttpListener"/> —
    /// kelas yang sama dengan listener webhook WhatsApp yang sudah dipakai
    /// aplikasi. Alasannya sederhana: tidak menambah dependensi apa pun,
    /// aplikasi tetap satu berkas .exe, dan kodenya mengikuti pola yang sudah
    /// teruji di aplikasi.
    ///
    /// Batas Keamanan yang dijaga:
    /// - Hanya localhost secara default. Membuka untuk jaringan harus diminta
    ///   operator secara sadar lewat pengaturan.
    /// - Setiap permintaan harus membawa kunci API yang cocok (lihat
    ///   <see cref="ApiAutentikasi"/>). Tanpa kunci, semua permintaan ditolak —
    ///   KECUALI endpoint verifikasi surat, yang memang dipanggil pihak pemegang
    ///   surat dan jawabannya dibatasi pada data yang sudah tercetak di surat.
    /// - Ada batas laju per alamat pengirim.
    /// - Badan permintaan dibatasi 64 KB supaya tidak bisa dipakai membanjiri
    ///   memori.
    /// - Setiap panggilan dicatat ke log aplikasi dan (bila diaktifkan) ke
    ///   Riwayat Aktivitas, TANPA isi badan permintaan — badan permintaan bisa
    ///   memuat NIK warga.
    /// </summary>
    public sealed class ApiListener : IDisposable
    {
        /// <summary>Batas ukuran badan permintaan (64 KB).</summary>
        private const int BatasBadan = 64 * 1024;

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Func<IServiceProvider> _provider;
        private readonly ILogger<ApiListener> _logger;
        private readonly ApiPembatas _pembatas;
        private readonly object _gapu = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _dibuang;

        public ApiListener(
            Func<IServiceProvider> provider,
            ILogger<ApiListener> logger)
            : this(provider, logger, null)
        {
        }

        /// <param name="pembatas">
        /// Pembatas laju; null memakai batas bawaan dari pengaturan. Disuntikkan
        /// agar pengujian bisa memakai jendela waktu sendiri.
        /// </param>
        internal ApiListener(
            Func<IServiceProvider> provider,
            ILogger<ApiListener> logger,
            ApiPembatas? pembatas)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pembatas = pembatas ?? new ApiPembatas(
                ApiPengaturan.BatasPermintaan, TimeSpan.FromMinutes(1));
        }

        /// <summary>True bila listener sedang melayani permintaan.</summary>
        public bool Berjalan
        {
            get { lock (_gapu) { return _listener != null; } }
        }

        /// <summary>
        /// Alasan listener tidak bisa dijalankan (mis. belum ada kunci API),
        /// atau null bila berhasil. Pesan ini ditampilkan di halaman API.
        /// </summary>
        public string? GalatTerakhir { get; private set; }

        /// <summary>Alamat dasar yang sedang dilayani.</summary>
        public string Alamat { get; private set; } = string.Empty;

        /// <summary>
        /// Nyalakan listener. Aman dipanggil berkali-kali. Mengembalikan true
        /// bila listener aktif setelah pemanggilan.
        /// </summary>
        public bool Mulai()
        {
            lock (_gapu)
            {
                if (_listener != null) return true;
                if (_dibuang) return false;

                if (!ApiPengaturan.SiapDijalankan(out var alasan))
                {
                    GalatTerakhir = alasan;
                    _logger.LogWarning("API tidak dinyalakan: {Alasan}", alasan);
                    return false;
                }

                var port = ApiPengaturan.Port;
                var host = ApiPengaturan.IzinkanJaringan ? "+" : "localhost";
                var prefiks = $"http://{host}:{port}{ApiRute.Prefix}/";

                var listener = new HttpListener();
                listener.Prefixes.Add(prefiks);

                try
                {
                    listener.Start();
                }
                catch (Exception ex)
                {
                    GalatTerakhir = ex is HttpListenerException
                        ? $"Port {port} tidak bisa dipakai ({ex.Message}). " +
                          " Jalankan aplikasi sebagai administrator, atau balikkan " +
                          "pengaturan 'Izinkan akses jaringan' bila hanya dipakai di komputer ini."
                        : ex.Message;
                    _logger.LogError(ex, "Gagal menyalakan listener API di {Prefiks}", prefiks);

                    try { listener.Close(); } catch { /* abaikan */ }
                    return false;
                }

                _listener = listener;
                _cts = new CancellationTokenSource();
                Alamat = ApiPengaturan.AlamatPrefix(port);
                GalatTerakhir = null;
                _pembatas.Bersihkan();

                _ = Task.Run(() => LoopAsync(listener, _cts!.Token));
                _logger.LogInformation("API desa aktif di {Alamat}", Alamat);
                return true;
            }
        }

        /// <summary>Matikan listener. Aman dipanggil walau belum pernah dinyalakan.</summary>
        public void Berhenti()
        {
            CancellationTokenSource? cts;
            HttpListener? listener;
            lock (_gapu)
            {
                cts = _cts;
                listener = _listener;
                _cts = null;
                _listener = null;
            }

            try { cts?.Cancel(); } catch { /* abaikan */ }
            try { listener?.Stop(); } catch { /* abaikan */ }
            try { listener?.Close(); } catch { /* abaikan */ }
            try { cts?.Dispose(); } catch { /* abaikan */ }

            _logger.LogInformation("API desa dimatikan.");
        }

        private async Task LoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Kesalahan pada loop listener API");
                    continue;
                }

                _ = Task.Run(() => LayaniAsync(ctx, ct), CancellationToken.None);
            }
        }

        private async Task LayaniAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var metode = ctx.Request.HttpMethod ?? "GET";
            var jalur = ctx.Request.Url?.AbsolutePath ?? "/";
            var asal = AlamatAsal(ctx);

            try
            {
                // 1) Batas laju diperiksa lebih dulu, supaya percobaan menebak
                //    kunci API tidak bisa membebani aplikasi.
                if (!_pembatas.Izinkan(asal))
                {
                    var tunggu = (int)Math.Ceiling(_pembatas.TungguBerikutnya(asal).TotalSeconds);
                    Tulis(ctx, 429, ApiAmplop.Gagal(
                        ApiKodeGalat.TerlaluBanyak,
                        "Terlalu banyak permintaan. Coba lagi beberapa saat lagi."),
                        RetryAfter: tunggu > 0 ? tunggu.ToString() : "1");
                    CatatAktivitas(metode, jalur, asal, 429);
                    return;
                }

                // 2) Rute dicocokkan lebih dulu: endpoint verifikasi surat boleh
                //    dipanggil tanpa kunci API (dipakai pihak luar yang memegang
                //    surat), sedangkan endpoint lain tetap wajib berkunci.
                var cocok = ApiRute.Cocok(metode, jalur);

                // 3) Kunci API wajib cocok, kecuali untuk endpoint publik.
                bool perluKunci = cocok.Endpoint == null || !cocok.Endpoint.TanpaKunci;
                if (perluKunci)
                {
                    var kunciDiberikan = ApiAutentikasi.AmbilKunci(nama => ctx.Request.Headers[nama]);
                    var semuaKunci = ApiKunci.MuatSemua();

                    if (semuaKunci.Count == 0)
                    {
                        Tulis(ctx, 503, ApiAmplop.Gagal(
                            ApiKodeGalat.ApiKeyKosong, "Kunci API belum dibuat di komputer ini."));
                        CatatAktivitas(metode, jalur, asal, 503);
                        return;
                    }

                    // Cocokkan terhadap kunci utama + kunci tambahan; kunci yang
                    // cocok lalu dicek cakupannya terhadap endpoint yang diminta.
                    var cocokKunci = ApiAutentikasi.CariCocok(kunciDiberikan, semuaKunci);
                    if (cocokKunci == null)
                    {
                        Tulis(ctx, 401, ApiAmplop.Gagal(
                            ApiKodeGalat.ApiKeySalah,
                            "Kunci API salah atau tidak dikirim. Kirim header X-Api-Key."));
                        CatatAktivitas(metode, jalur, asal, 401);
                        return;
                    }

                    if (cocok.Endpoint != null &&
                        !ApiAutentikasi.CakupanCukup(cocokKunci.Cakupan, cocok.Endpoint))
                    {
                        Tulis(ctx, 403, ApiAmplop.Gagal(
                            ApiKodeGalat.ApiCakupanKurang,
                            "Kunci API sah, tetapi cakupannya tidak mencakup endpoint ini."));
                        CatatAktivitas(metode, jalur, asal, 403);
                        return;
                    }
                }

                // Method salah diperiksa lebih dulu: ApiRute tetap mengembalikan
                // endpoint yang cocok jalur supaya bisa isi header Allow, jadi
                // Ditemukan bernilai true dan tidak boleh dipakai sebagai
                // penentu "jalur tidak dikenal".
                if (cocok.MetodeTidakDiizinkan)
                {
                    var endpoint = cocok.Endpoint!;
                    Tulis(ctx, 405, ApiAmplop.Gagal(
                        ApiKodeGalat.MetodeTidakDiizinkan,
                        $"Endpoint {endpoint.BarisTampil} tidak menerima metode {metode}."),
                        Allow: endpoint.Metode);
                    CatatAktivitas(metode, jalur, asal, 405);
                    return;
                }

                if (!cocok.Ditemukan)
                {
                    Tulis(ctx, 404, ApiAmplop.Gagal(
                        ApiKodeGalat.EndpointTidakAda,
                        "Endpoint tidak dikenal. Lihat daftar di halaman API."));
                    CatatAktivitas(metode, jalur, asal, 404);
                    return;
                }

                var hasil = await JalankanAsync(cocok, ctx, ct).ConfigureAwait(false);
                CatatAktivitas(metode, jalur, asal, hasil.KodeHttp);
            }
            catch (OperationCanceledException)
            {
                // Aplikasi sedang keluar — tidak perlu membalas.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal melayani permintaan API {Metode} {Jalur}", metode, jalur);
                Tulis(ctx, 500, ApiAmplop.Gagal(
                    ApiKodeGalat.GagalDiServer, "Permintaan gagal diproses di server."));
                CatatAktivitas(metode, jalur, asal, 500);
            }
        }

        private sealed class HasilJalankan
        {
            public int KodeHttp { get; init; }
        }

        private async Task<HasilJalankan> JalankanAsync(
            ApiCocok cocok, HttpListenerContext ctx, CancellationToken ct)
        {
            var endpoint = cocok.Endpoint!;

            // Seluruh handler API berjalan sebagai SESI SISTEM: kunci API bukan
            // sesi pengguna aplikasi, jadi guard izin lapisan data tidak boleh
            // mewarisi peran operator yang sedang masuk (atau peran bawaan).
            // Endpoint dibatasi oleh tabel cakupannya sendiri (ApiRute).
            using var _sesiSistemApi = SessionContext.SesiSistem();

            switch (endpoint.Jalur)
            {
                case "/status":
                {
                    using var scope = _provider().CreateScope();
                    var ringkasan = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
                    var status = await ringkasan.AmbilAsync(ct).ConfigureAwait(false);

                    Tulis(ctx, 200, ApiAmplop.Berhasil(new
                    {
                        aplikasi = "SuDesApp",
                        versi = VersiAplikasi,
                        waktu = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        antrean = status.PermintaanPerStatus,
                        belumDibaca = status.PermintaanBelumDibaca
                    }));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/statistik":
                {
                    using var scope = _provider().CreateScope();
                    var ringkasan = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
                    var hasil = await ringkasan.AmbilAsync(ct).ConfigureAwait(false);
                    Tulis(ctx, 200, ApiAmplop.Berhasil(hasil));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/perangkat-desa":
                {
                    using var scope = _provider().CreateScope();
                    var ringkasan = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
                    var daftar = await ringkasan
                        .AmbilPerangkatAsync(hanyaAktif: true, ct).ConfigureAwait(false);
                    Tulis(ctx, 200, ApiAmplop.Berhasil(daftar));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/verifikasi/{kode}":
                {
                    using var scope = _provider().CreateScope();
                    var layanan = scope.ServiceProvider.GetRequiredService<SuDesApp.Services.IVerifikasiSuratService>();
                    var hasil = await layanan.VerifikasiAsync(cocok.NilaiParameter!, ct).ConfigureAwait(false);

                    if (!hasil.Ditemukan)
                    {
                        Tulis(ctx, 404, ApiAmplop.Gagal(
                            ApiKodeGalat.KodeTidakDikenal, hasil.Pesan));
                        return new HasilJalankan { KodeHttp = 404 };
                    }

                    var jawaban = new ApiHasilVerifikasi
                    {
                        Kode = hasil.Kode ?? string.Empty,
                        Sah = hasil.Sah,
                        NomorSurat = hasil.NomorSurat,
                        JenisSurat = hasil.NamaJenis,
                        TanggalSurat = hasil.TanggalSurat,
                        PemohonTersamar = SamarkanNama(hasil.NamaPemohon),
                        DataBerubah = hasil.DataBerubah,
                        StatusPersetujuan = hasil.StatusPersetujuan
                    };

                    Tulis(ctx, 200, ApiAmplop.Berhasil(jawaban, hasil.Pesan));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/jenis-surat":
                {
                    using var scope = _provider().CreateScope();
                    var ringkasan = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
                    Tulis(ctx, 200, ApiAmplop.Berhasil(new { jenis = ringkasan.AmbilJenisSuratOnline() }));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/rekap-bulanan":
                {
                    // Parameter tahun lewat query string (jalur rute tidak
                    // memuat parameter): ?tahun=2026. Tanpa parameter, memakai
                    // tahun berjalan supaya pemanggil malas punya default.
                    var tahunMentah = ctx.Request.QueryString["tahun"];
                    int? tahun = null;
                    if (!string.IsNullOrWhiteSpace(tahunMentah))
                    {
                        if (!int.TryParse(tahunMentah, out var tahunNilai)
                            || tahunNilai < 1900 || tahunNilai > 2100)
                        {
                            Tulis(ctx, 400, ApiAmplop.Gagal(
                                ApiKodeGalat.PermintaanTidakSah,
                                "Parameter tahun harus angka antara 1900 dan 2100, mis. ?tahun=2026."));
                            return new HasilJalankan { KodeHttp = 400 };
                        }
                        tahun = tahunNilai;
                    }

                    using var scope = _provider().CreateScope();
                    var ringkasan = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
                    var rekap = await ringkasan
                        .AmbilRekapBulananAsync(tahun, ct).ConfigureAwait(false);

                    Tulis(ctx, 200, ApiAmplop.Berhasil(rekap));
                    return new HasilJalankan { KodeHttp = 200 };
                }

                case "/permintaan":
                {
                    var (isi, galat) = BacaBadan(ctx.Request);
                    if (galat != null)
                    {
                        Tulis(ctx, 413, ApiAmplop.Gagal(ApiKodeGalat.BadanTerlaluBesar, galat));
                        return new HasilJalankan { KodeHttp = 413 };
                    }

                    if (string.IsNullOrWhiteSpace(isi))
                    {
                        Tulis(ctx, 400, ApiAmplop.Gagal(
                            ApiKodeGalat.PermintaanTidakSah, "Badan permintaan kosong."));
                        return new HasilJalankan { KodeHttp = 400 };
                    }

                    ApiPermintaanMasuk? masukan;
                    try
                    {
                        masukan = JsonSerializer.Deserialize<ApiPermintaanMasuk>(isi!, OpsiJson);
                    }
                    catch (JsonException ex)
                    {
                        Tulis(ctx, 400, ApiAmplop.Gagal(
                            ApiKodeGalat.PermintaanTidakSah, "JSON tidak terbaca: " + ex.Message));
                        return new HasilJalankan { KodeHttp = 400 };
                    }

                    using var scope = _provider().CreateScope();
                    var layanan = scope.ServiceProvider.GetRequiredService<IApiPermintaanService>();
                    var hasil = await layanan.BuatAsync(masukan!, ct).ConfigureAwait(false);

                    if (!hasil.Berhasil)
                    {
                        Tulis(ctx, 400, ApiAmplop.Gagal(
                            ApiKodeGalat.PermintaanTidakSah,
                            hasil.Pesan ?? "Permintaan ditolak."));
                        return new HasilJalankan { KodeHttp = 400 };
                    }

                    Tulis(ctx, hasil.SudahAda ? 200 : 201, ApiAmplop.Berhasil(
                        new { kode = hasil.Kode, sudahAda = hasil.SudahAda }, hasil.Pesan));
                    return new HasilJalankan { KodeHttp = hasil.SudahAda ? 200 : 201 };
                }

                default:
                {
                    // Endpoint /permintaan/{kode} — satu-satunya yang punya parameter.
                    if (endpoint.Metode == "GET" && !string.IsNullOrWhiteSpace(cocok.NilaiParameter))
                    {
                        using var scope = _provider().CreateScope();
                        var layanan = scope.ServiceProvider.GetRequiredService<IApiPermintaanService>();
                        var status = await layanan
                            .AmbilStatusAsync(cocok.NilaiParameter!, ct).ConfigureAwait(false);

                        if (status is null)
                        {
                            Tulis(ctx, 404, ApiAmplop.Gagal(
                                ApiKodeGalat.EndpointTidakAda,
                                $"Permintaan dengan kode {cocok.NilaiParameter} tidak ditemukan."));
                            return new HasilJalankan { KodeHttp = 404 };
                        }

                        Tulis(ctx, 200, ApiAmplop.Berhasil(status));
                        return new HasilJalankan { KodeHttp = 200 };
                    }

                    Tulis(ctx, 404, ApiAmplop.Gagal(
                        ApiKodeGalat.EndpointTidakAda, "Endpoint tidak dikenal."));
                    return new HasilJalankan { KodeHttp = 404 };
                }
            }
        }

        /// <summary>
        /// Baca badan permintaan dengan batas ukuran. Mengembalikan isi, atau
        /// pesan galat bila kelewat besar.
        /// </summary>
        private static (string? Isi, string? Galat) BacaBadan(HttpListenerRequest request)
        {
            if (request.ContentLength64 > BatasBadan)
                return (null, "Badan permintaan lebih besar dari 64 KB.");

            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);

            // StreamReader.Read tidak menjamin membaca seluruh isi dalam satu
            // panggilan (bisa berhenti pada batas buffer), jadi badan dibaca
            // berulang sampai habis. Buffer diberi satu karakter lebih supaya
            // badan tepat di atas batas terdeteksi, bukan terpotong diam-diam.
            var buffer = new char[BatasBadan + 1];
            var jumlah = 0;
            int dibaca;
            while ((dibaca = reader.Read(buffer, jumlah, buffer.Length - jumlah)) > 0)
            {
                jumlah += dibaca;
                if (jumlah > BatasBadan)
                    return (null, "Badan permintaan lebih besar dari 64 KB.");
            }

            return (new string(buffer, 0, jumlah), null);
        }

        private static string AlamatAsal(HttpListenerContext ctx)
        {
            var alamat = ctx.Request.RemoteEndPoint?.Address.ToString();
            if (string.IsNullOrWhiteSpace(alamat)) return "lokal";

            // IPv6 loopback & unspecified dinormalkan supaya tidak terhitung
            // sebagai beberapa klien berbeda.
            return alamat switch
            {
                "::1" or "127.0.0.1" or "0.0.0.0" or "::" => "lokal",
                _ => alamat
            };
        }

        private void Tulis(
            HttpListenerContext ctx, int kode, ApiAmplop amplop,
            string? RetryAfter = null, string? Allow = null)
        {
            try
            {
                var json = JsonSerializer.Serialize(amplop, OpsiJson);
                var byteJson = Encoding.UTF8.GetBytes(json);

                ctx.Response.StatusCode = kode;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = byteJson.Length;
                ctx.Response.Headers["X-SudesApp-Api"] = "v1";

                // Aplikasi desktop: tidak perlu menahan koneksi terbuka.
                ctx.Response.KeepAlive = false;

                if (RetryAfter != null) ctx.Response.Headers["Retry-After"] = RetryAfter;
                if (Allow != null) ctx.Response.Headers["Allow"] = Allow;

                ctx.Response.OutputStream.Write(byteJson, 0, byteJson.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menulis respons API");
            }
            finally
            {
                try { ctx.Response.Close(); } catch { /* abaikan */ }
            }
        }

        /// <summary>
        /// Catat pemanggilan API. Isi badan permintaan SENGAJA tidak dicatat
        /// karena bisa memuat NIK dan data warga; yang dicatat hanya endpoint,
        /// asal, dan kode jawabannya.
        /// </summary>
        private void CatatAktivitas(string metode, string jalur, string asal, int kode)
        {
            _logger.LogInformation("API {Metode} {Jalur} dari {Asal} — {Kode}", metode, jalur, asal, kode);

            if (!ApiPengaturan.CatatAktivitas) return;

            try
            {
                _provider().GetRequiredService<ActivityLogService>().Log(
                    "API", $"{metode} {ApiRute.NormalisasiJalur(jalur)}", "Akses",
                    $" asal {asal} → HTTP {kode}");
            }
            catch (Exception ex)
            {
                // Pencatatan tidak boleh menggagalkan permintaan.
                _logger.LogWarning(ex, "Gagal mencatat aktivitas API");
            }
        }

        /// <summary>
        /// Nama pemohon disamarkan sebelum keluar lewat endpoint verifikasi
        /// ("Budi Santoso" → "BUDI S.") — cukup untuk mencocokkan dengan nama
        /// yang tercetak di surat, tanpa menyalin nama lengkap ke sistem luar.
        /// </summary>
        internal static string? SamarkanNama(string? nama)
        {
            if (string.IsNullOrWhiteSpace(nama)) return null;

            var bagian = nama!.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bagian.Length == 0) return null;

            string pertama = bagian[0].ToUpperInvariant();
            if (bagian.Length == 1) return pertama;

            var inisial = string.Join(" ", bagian.Skip(1)
                .Where(b => b.Length > 0)
                .Select(b => char.ToUpperInvariant(b[0]) + "."));

            return string.IsNullOrEmpty(inisial) ? pertama : $"{pertama} {inisial}";
        }

        private static string VersiAplikasi =>
            typeof(ApiListener).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        public void Dispose()
        {
            if (_dibuang) return;
            _dibuang = true;
            Berhenti();
        }
    }
}
