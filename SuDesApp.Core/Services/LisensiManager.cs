using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>
    /// Status lisensi yang ditampilkan aplikasi. SEMUA nilai di sini berasal
    /// dari klaim token yang tanda tangannya sudah diverifikasi — tidak ada
    /// lagi status yang dipercaya begitu saja dari berkas cache.
    /// </summary>
    public class StatusLisensi
    {
        /// <summary>Aktif = langganan sah menurut token bertanda tangan.</summary>
        public bool Aktif { get; set; }

        /// <summary>Jatuh tempo langganan (UTC) dari klaim token.</summary>
        public DateTime? BerlakuSampai { get; set; }

        /// <summary>Waktu server terakhir mengonfirmasi (UTC) dari klaim token.</summary>
        public DateTime CekTerakhir { get; set; }

        public string? NamaDesa { get; set; }

        /// <summary>Paket langganan aktif (mis. "bulanan").</summary>
        public string Paket { get; set; } = "";

        /// <summary>Sisa hari sebelum aplikasi berhenti sah bila tak ada koneksi.</summary>
        public int SisaHariTenggang { get; set; }

        /// <summary>
        /// Tanggal (UTC) aplikasi benar-benar berhenti sah: yang lebih akhir dari
        /// tanggal bayar (<see cref="BerlakuSampai"/>) dan masa tenggang
        /// (<c>cek + 7 hari</c>). Dipakai tampilan untuk menjawab "sah sampai
        /// kapan" — tanpa ini halaman hanya punya tanggal bayar, dan saat
        /// langganan sudah lewat tapi masih di masa tenggang, tanggal itu sudah
        /// di masa lalu sehingga terbaca seolah statusnya tidak aktif.
        /// Diisi <see cref="LisensiManager.HitungStatus"/> dari rumus yang sama
        /// dengan gerbang fitur, jadi angka di layar tidak bisa menyimpang.
        /// </summary>
        public DateTime? SahSampai { get; set; }

        /// <summary>
        /// Token bertanda tangan yang mendasari status ini. Disimpan agar bisa
        /// ditulis ke cache tanpa encode ulang. <c>JsonIgnore</c> supaya tidak
        /// ikut bocor kalau objek ini tak sengaja diserialisasi ke log/UI.
        /// </summary>
        [JsonIgnore]
        public string? Token { get; set; }
    }

    /// <summary>
    /// Sumber kebenaran lisensi: server penandatangan + cache token lokal.
    ///
    /// MODEL KERJA
    /// -----------
    /// Client menyimpan HANYA token bertanda tangan (lihat
    /// <see cref="VerifikasiLisensi"/>). Setiap kali dibutuhkan, token itu
    /// diverifikasi terhadap kunci publik yang ditanam di binary.(Status yang
    /// dulu disimpan terpisah sebagai field JSON — <c>aktif</c>,
    /// <c>cekTerakhir</c> — sudah dihapus karena field itulah yang bisa
    /// disunting bebas untuk memperoleh lisensi gratis selamanya.)
    ///
    /// Aturan kelayakan (semua diturunkan dari klaim yang terverifikasi):
    /// sah sampai <c>max(exp, cek + masa tenggang)</c>. Artinya langganan yang
    /// sudah dibayar flushed hingga tanggal tertentu tetap sah walau offline
    /// jauh lebih lama dari masa tenggang, sedangkan masa tenggang hanya
    /// menjembatani masa tenggang setelah tanggal pembayaran lewat.
    /// </summary>
    public class LisensiManager
    {
        /// <summary>Berapa hari boleh jalan tanpa berhasil cek server (grace period).</summary>
        public const int MASA_TENGGANG_HARI = 7;

        /// <summary>
        /// Penunjuk folder cache lisensi untuk pengujian. Null = lokasi bawaan
        /// (%LocalAppData%\SuDesApp). Pola sama dengan
        /// <c>LogoKabupaten.FolderOverride</c> dan
        /// <c>AppPreferenceStore.LokasiOverride</c>: tanpa ini, uji tidak bisa
        /// membuktikan alur "cache hilang → bukti diambil dari database" karena
        /// cache milik pengguna yang sedang berjalan ikut terbaca.
        /// </summary>
        internal static string? FolderCacheOverride { get; set; }

        private static string FolderLocal => FolderCacheOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp");

        /// <summary>
        /// Cache berisi token bertanda tangan. Isi berkas tidak diransom —
        /// <see cref="VerifikasiLisensi"/> yang memastikan sah. Menempel
        /// ulang bersama token lama tetap aman karena masa tenggang dihitung
        /// dari <c>cek</c> yang ada DI DALAM tanda tangan, bukan dari jam
        /// sistem berkas (jam sistem bisa dimundurkan,jam berkas bisa diedit).
        /// </summary>
        private static string LokasiCache => Path.Combine(FolderLocal, "lisensi-cache.json");

        private readonly ILogger<LisensiManager> _logger;
        private readonly HttpClient _http;
        private readonly SambunganServerLisensi? _sambungan;
        private readonly object _kunciCache = new();

        /// <param name="sambungan">
        /// Perekam keadaan sambungan bersama (dipakai baris menu Pusat
        /// Administrasi di sidebar). Opsional supaya pengujian tidak perlu
        /// menyiapkannya; tanpa itu panggilan tetap berjalan seperti biasa.
        /// </param>
        public LisensiManager(ILogger<LisensiManager> logger, HttpClient? http = null,
            SambunganServerLisensi? sambungan = null)
        {
            _logger = logger;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _sambungan = sambungan;
        }

        private string _urlServer = "http://localhost:5000";

        /// <summary>
        /// Server lisensi dapat diarahkan lewat URL ini.
        ///
        /// Perubahan diberitahukan lewat <see cref="AlamatBerubah"/> supaya
        /// tampilan yang menyebut alamat — mis. baris menu Pusat Administrasi di
        /// sidebar — tidak pernah menampilkan server lama setelah alamat diganti.
        /// </summary>
        public string UrlServer
        {
            get => _urlServer;
            set
            {
                if (string.Equals(_urlServer, value, StringComparison.Ordinal))
                {
                    return;
                }

                _urlServer = value;
                AlamatBerubah?.Invoke();
            }
        }

        /// <summary>
        /// Terjadi setelah <see cref="UrlServer"/> benar-benar berubah. Hanya
        /// untuk tampilan: tidak ada aturan lisensi yang bergantung padanya, dan
        /// pelanggannya wajib aman dipanggil dari utas mana pun pembaca memilih
        /// menyegarkan layar.
        /// </summary>
        public event Action? AlamatBerubah;

        /// <summary>
        /// True bila alamat server berasal dari konfigurasi rilis dan tidak boleh
        /// diganti dari preferensi pengguna/halaman Langganan. Dipasang oleh
        /// <c>PenentuAlamatLisensi</c> saat startup: URL produksi di appsettings
        /// mengunci alamat, sedangkan loopback (server uji) tetap bisa diganti.
        /// </summary>
        public bool UrlServerTerkunci { get; set; }

        /// <summary>
        /// Alamat lengkap satu endpoint server lisensi ("aktivasi", "paket", …).
        /// Kelas ini PEMILIK alamat server, jadi hanya di sini alamat itu disusun —
        /// klien lain (mis. <see cref="LanggananKlien"/>) memakai metode ini supaya
        /// tidak ada dua tempat yang bisa menyimpang soal server mana yang dituju.
        /// Kosong bila <see cref="UrlServer"/> belum diisi.
        /// </summary>
        public string AlamatEndpoint(string jalur) => string.IsNullOrWhiteSpace(UrlServer)
            ? string.Empty
            : $"{UrlServer.Trim().TrimEnd('/')}/{jalur.TrimStart('/')}";

        /// <summary>
        /// Alamat halaman pusat administrasi server lisensi (rute <c>/admin</c>) —
        /// halaman operator untuk melihat pelanggan yang terdaftar beserta
        /// perangkat dan pembayarannya. Halaman itu hidup di server (wwwroot/admin.html)
        /// dan meminta ID serta token pribadi operator di peramban
        /// (<c>Lisensi:AdminOperators</c> di server), jadi klien hanya perlu tahu
        /// alamatnya — rahasianya tidak pernah ikut ke aplikasi desa.
        ///
        /// Kosong bila alamat server belum diatur; pemanggil harus memeriksanya
        /// lebih dulu, bukan membuka peramban ke alamat kosong.
        /// </summary>
        public string AlamatPusatAdministrasi() => AlamatEndpoint("admin");

        /// <summary>
        /// Kunci publik penandatangan (base64url SPKI). Diisi dari
        /// appsettings <c>Lisensi:KunciPublik</c> saat startup; bila kosong,
        /// diambil sekali dari <c>/kunci-publik</c> lalu disimpan lokal.
        /// </summary>
        public string? KunciPublik { get; set; }

        /// <summary>Kode aktivasi yang dimasukkan pengguna pada halaman pengaturan.</summary>
        public string? KodeAktivasi { get; set; }

        /// <summary>
        /// String koneksi database desa. Bukti langganan (token bertanda
        /// tangan) ikut disimpan ke tabel LisensiAktivasi lewat nilai ini,
        /// supaya kode aktivasi terhubung ke data desa dan ikut terbawa saat
        /// pencadangan. Kosong = bukti hanya hidup di cache berkas (perilaku
        /// lama), dan aplikasi tetap berfungsi penuh.
        /// </summary>
        public string? KoneksiDatabase { get; set; }

        /// <summary>Nomor seri perangkat, dikirim saat cek agar server bisa membatasi jumlah device.</summary>
        public string IdPerangkat => AmbilIdPerangkat();

        /// <summary>
        /// Muat cache → cek server bila ada kode aktivasi → simpan token baru.
        /// Tidak pernah melempar: kegagalan jaringan memakai token cache yang
        /// masih sah (di dalam masa tenggang).
        /// </summary>
        public async Task<StatusLisensi> CekAsync(CancellationToken cancellationToken = default)
        {
            var hasil = BacaCache();

            // Cache berkas kosong (mis. file terhapus atau DB laptop baru) →
            // coba bukti langganan dari database desa sebelum menyerah. Ini
            // yang membuat kode aktivasi benar-benar "terhubung ke database":
            // pasang ulang aplikasi di komputer lain dengan salinan data desa
            // masih Producekan akses penuh selama tokennya sah.
            if (string.IsNullOrWhiteSpace(hasil.Token))
            {
                var dariDb = await BacaBuktiDariDatabaseAsync(cancellationToken).ConfigureAwait(false);
                if (dariDb != null) hasil = dariDb;
            }

            if (!string.IsNullOrWhiteSpace(KodeAktivasi))
            {
                try
                {
                    var kunci = await DapatkanKunciPublikAsync(cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(kunci))
                    {
                        _logger.LogError(
                            "Kunci publik lisensi tidak tersedia — server tidak bisa diverifikasi. " +
                            "Status memakai cache terakhir; tanpa kunci, lisensi tidak dapat diaktifkan.");
                    }
                    else
                    {
                        var resp = await _http.PostAsJsonAsync(
                            AlamatEndpoint("aktivasi"),
                            new { kodeAktivasi = KodeAktivasi!.Trim(), idPerangkat = IdPerangkat },
                            cancellationToken).ConfigureAwait(false);

                        // Balasan diterima (apa pun statusnya) = server benar-benar
                        // bisa dihubungi; inilah bukti yang dicatat untuk sidebar.
                        _sambungan?.CatatTerhubung();

                        if (resp.IsSuccessStatusCode)
                        {
                            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            var hasilServer = BacaStatusServer(json, kunci, _logger);

                            if (hasilServer != null)
                            {
                                hasil = hasilServer;
                                SimpanCache(new CacheLisensi { Token = hasilServer.Token! });
                                await SimpanBuktiKeDatabaseAsync(hasilServer, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        else if ((int)resp.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                        {
                            // Kode tidak dikenal → hapus token lama supaya desertase
                            // tidak dibuat-buat oleh token basi milik kode lain.
                            hasil = new StatusLisensi();
                            HapusCache();
                            await HapusBuktiDariDatabaseAsync(cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogWarning("Cek lisensi ke server gagal: HTTP {Status}", (int)resp.StatusCode);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Kegagalan jaringan sungguhan — satu-satunya jenis kegagalan
                    // yang boleh menandai server TIDAK terjangkau. Gagal lokal
                    // (mis. database desa tidak bisa ditulis) tidak menyentuh
                    // keadaan sambungan: servernya belum tentu bermasalah.
                    _sambungan?.CatatTidakTerjangkau();
                    _logger.LogWarning(ex, "Tidak bisa menghubungi server lisensi; pakai cache terakhir");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menerapkan hasil cek lisensi; pakai cache terakhir");
                }
            }

            return hasil;
        }

        /// <summary>
        /// Status dari cache saja — tanpa jaringan. Dipakai untuk tampil seketika
        /// di Pengaturan dan untuk deciding cepat sebelum cek online selesai.
        /// </summary>
        public StatusLisensi BacaCachePublik() => BacaCache();

        /// <summary>
        /// True bila lisensi saat ini sah. Semua inputnya berasal dari klaim
        /// token terverifikasi, jadi tidak bisa dipalsukan dari sisi klien.
        /// </summary>
        public bool MasihSah(StatusLisensi? status = null)
        {
            var s = status ?? BacaCache();
            return s != null && s.Aktif;
        }

        /// <summary>
        /// Ambil kunci publik: dari konfigurasi dulu; bila belum ada, ambil dari
        /// server sekali lalu simpan. Persisten di <c>%LocalAppData%</c> agar
        /// tidak diambil ulang tiap cek dan tidak bisa diganti di tengah jalan
        /// oleh pihak lain (TOFU/pin).
        /// </summary>
        private static readonly TimeSpan JedaCobaLagiKunci = TimeSpan.FromMinutes(5);

        private DateTime _kunciGagalPada = DateTime.MinValue;
        private async Task<string?> DapatkanKunciPublikAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(KunciPublik))
            {
                // BacaCache dapat memuat pin lama sebelum cek online. Versi
                // sebelumnya menyimpan seluruh objek JSON /kunci-publik, jadi
                // normalisasikan juga nilai yang sudah berada di memori.
                var terpasang = BacaKunciPublik(KunciPublik.Trim());
                if (!string.IsNullOrWhiteSpace(terpasang))
                {
                    KunciPublik = terpasang;
                    return terpasang;
                }
                KunciPublik = null;
            }

            lock (_kunciCache)
            {
                if (!string.IsNullOrWhiteSpace(KunciPublik)) return KunciPublik;

                // Tanpa kunci publik, setiap cek lisensi akan mencoba
                // menghubungi server /kunci-publik. Bila server lisensi belum
                // disetel (kondisi paling sering saat install baru), itu
                // berarti percobaan koneksi berulang setiap 6 jam — dan saat
                // pengguna membuka halaman Langganan berulang kali, jauh lebih
                // sering. Jeda pendek setelah kegagalan menjaga log tetap
                // terbaca tanpa menahan cek yang memang dijadwalkan.
                if (_kunciGagalPada != DateTime.MinValue &&
                    DateTime.UtcNow - _kunciGagalPada < JedaCobaLagiKunci)
                {
                    return null;
                }
            }

            var jalurKunci = Path.Combine(FolderLocal, "lisensi-pubkey.txt");
            try
            {
                if (File.Exists(jalurKunci))
                {
                    var isiLokal = (await File.ReadAllTextAsync(jalurKunci, cancellationToken).ConfigureAwait(false)).Trim();
                    var kunciLokal = BacaKunciPublik(isiLokal);
                    if (!string.IsNullOrWhiteSpace(kunciLokal))
                    {
                        KunciPublik = kunciLokal;
                        if (!string.Equals(isiLokal, kunciLokal, StringComparison.Ordinal))
                        {
                            await File.WriteAllTextAsync(jalurKunci, kunciLokal, cancellationToken).ConfigureAwait(false);
                        }
                        return kunciLokal;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca kunci publik lisensi lokal");
            }

            try
            {
                var resp = await _http.GetAsync(AlamatEndpoint("kunci-publik"), cancellationToken)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var isi = (await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
                    var kunci = BacaKunciPublik(isi);
                    if (!string.IsNullOrWhiteSpace(kunci))
                    {
                        Directory.CreateDirectory(FolderLocal);
                        await File.WriteAllTextAsync(jalurKunci, kunci, cancellationToken).ConfigureAwait(false);
                        KunciPublik = kunci;
                        _logger.LogInformation("Kunci publik lisensi diambil dari server dan dipin lokal.");
                        return kunci;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _kunciGagalPada = DateTime.UtcNow;
                _logger.LogWarning(ex, "Gagal mengambil kunci publik lisensi dari server");
            }

            return null;
        }

        /// <summary>
        /// Endpoint server saat ini mengembalikan objek JSON berisi <c>kunci</c>
        /// dan <c>algoritma</c>. Terima juga respons string polos untuk menjaga
        /// kompatibilitas dengan server lama yang mengirim base64url langsung.
        /// </summary>
        private static string? BacaKunciPublik(string isi)
        {
            try
            {
                using var doc = JsonDocument.Parse(isi);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("kunci", out var kunci) &&
                    kunci.ValueKind == JsonValueKind.String)
                {
                    return kunci.GetString()?.Trim();
                }

                if (root.ValueKind == JsonValueKind.String)
                {
                    return root.GetString()?.Trim();
                }
            }
            catch (JsonException)
            {
                // Kompatibilitas dengan server lama yang mengirim kunci polos.
                return isi;
            }

            return null;
        }

        // =====================================================================
        // Bukti langganan di database desa
        //
        // Cache berkas (lisensi-cache.json) tetap jadi sumber utama supaya
        // LisensiManager tidak bergantung pada database sama sekali. Database
        // adalah tempat kedua: token yang sama ditulis ulang ke tabel
        // LisensiAktivasi supaya ikut tercadangkan bersama data desa, dan
        // dibaca kembali hanya bila cache berkas kosong (installer baru,
        // berkas cache terhapus).
        //
        // Yang disimpan ke database tetap TOKEN BERTANDATANGAN — bukan status
        // dalam bentuk angka/teks. Status SELALU dihitung ulang lewat
        // HitungStatus() yang memverifikasi tanda tangan, sehingga mengedit
        // baris tabel secara manual tidak memberi akses apa pun.
        // =====================================================================

        /// <summary>Tulis bukti langganan ke tabel LisensiAktivasi. Gagal diam-diam.</summary>
        private async Task SimpanBuktiKeDatabaseAsync(StatusLisensi status, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(KodeAktivasi) ||
                string.IsNullOrWhiteSpace(KoneksiDatabase) ||
                string.IsNullOrWhiteSpace(status.Token))
            {
                return;
            }

            try
            {
                var tersimpan = await LisensiAktivasiRepository.SimpanDenganKoneksiAsync(
                    KoneksiDatabase!,
                    new LisensiAktivasi
                    {
                        KodeAktivasi = KodeAktivasi!.Trim(),
                        NamaDesa = status.NamaDesa,
                        Paket = status.Paket,
                        Token = status.Token,
                        IdPerangkat = IdPerangkat,
                        CekTerakhir = status.CekTerakhir.ToString("o")
                    },
                    ct).ConfigureAwait(false);

                if (tersimpan)
                {
                    _logger.LogInformation("Bukti langganan disimpan ke database desa.");
                }
            }
            catch (Exception ex)
            {
                // Tidak boleh menggagalkan pengecekan: cache berkas sudah
                // menyimpan token yang sama.
                _logger.LogWarning(ex, "Gagal menyimpan bukti langganan ke database desa");
            }
        }

        /// <summary>
        /// Baca bukti langganan dari database dan verifikasi tanda tangannya.
        /// Null bila tidak ada bukti / tokennya tidak sah — pemanggil lalu
        /// memakai cache berkas seperti biasa.
        /// </summary>
        private async Task<StatusLisensi?> BacaBuktiDariDatabaseAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(KodeAktivasi) || string.IsNullOrWhiteSpace(KoneksiDatabase))
            {
                return null;
            }

            try
            {
                var bukti = await LisensiAktivasiRepository.AmbilDenganKoneksiAsync(
                    KoneksiDatabase!, KodeAktivasi!, ct).ConfigureAwait(false);

                if (bukti is null || string.IsNullOrWhiteSpace(bukti.Token))
                {
                    return null;
                }

                var kunci = KunciPublik ?? BacaKunciPublikLokal();
                var status = HitungStatus(bukti.Token!, kunci, _logger);
                if (status != null)
                {
                    _logger.LogInformation("Bukti langganan diambil dari database desa.");
                }
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca bukti langganan dari database desa");
                return null;
            }
        }

        /// <summary>Buang bukti langganan dari database (kode ditolak server).</summary>
        private async Task HapusBuktiDariDatabaseAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(KodeAktivasi) || string.IsNullOrWhiteSpace(KoneksiDatabase)) return;

            try
            {
                await LisensiAktivasiRepository.HapusDenganKoneksiAsync(
                    KoneksiDatabase!, KodeAktivasi!, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghapus bukti langganan dari database desa");
            }
        }

        /// <summary>
        /// Baca dan verifikasi token di cache. Berkas rusak / tanda tangan
        /// tidak cocok → dianggap tidak aktif (fail closed).
        /// </summary>
        private StatusLisensi BacaCache()
        {
            try
            {
                if (File.Exists(LokasiCache))
                {
                    var json = File.ReadAllText(LokasiCache);
                    var cache = JsonSerializer.Deserialize<CacheLisensi>(json);
                    if (cache != null && !string.IsNullOrWhiteSpace(cache.Token))
                    {
                        var kunci = KunciPublik ?? BacaKunciPublikLokal();
                        var status = HitungStatus(cache.Token, kunci, _logger);
                        if (status != null) return status;

                        _logger.LogWarning("Token lisensi di cache tidak sah (tanda tangan gagal) — diperlakukan tidak aktif.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca cache lisensi; anggap tidak aktif");
            }
            return new StatusLisensi();
        }

        private string? BacaKunciPublikLokal()
        {
            try
            {
                var jalurKunci = Path.Combine(FolderLocal, "lisensi-pubkey.txt");
                if (File.Exists(jalurKunci))
                {
                    var isi = File.ReadAllText(jalurKunci).Trim();
                    KunciPublik = BacaKunciPublik(isi);
                    if (!string.IsNullOrWhiteSpace(KunciPublik) &&
                        !string.Equals(isi, KunciPublik, StringComparison.Ordinal))
                    {
                        // Migrasikan pin lama yang menyimpan seluruh objek JSON
                        // agar cache berikutnya berisi kunci SPKI kanonis saja.
                        File.WriteAllText(jalurKunci, KunciPublik);
                    }
                    return KunciPublik;
                }
            }
            catch { /* diam — kunci opsional */ }
            return null;
        }

        /// <summary>Terjemahkan JSON balasan server <c>/aktivasi</c> menjadi status.</summary>
        private static StatusLisensi? BacaStatusServer(string json, string kunci, ILogger logger)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
                {
                    // Server tahu langganan tidak aktif → tanpa token. Status
                    // kosong berarti tidak aktif; cache lama tetap dipertahankan
                    // supaya masa tenggang offline habis secara wajar.
                    return null;
                }

                return HitungStatus(tokenEl.GetString()!, kunci, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Balasan server lisensi tidak bisa dibaca");
                return null;
            }
        }

        /// <summary>
        /// Verifikasi token lalu hitung status kelayakan.
        /// </summary>
        private static StatusLisensi? HitungStatus(string token, string? kunciPublik, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(kunciPublik))
            {
                logger.LogWarning("Lisensi tidak dapat diverifikasi tanpa kunci publik.");
                return null;
            }

            if (!VerifikasiLisensi.CobaVerifikasi(token, kunciPublik, out var klaim) || klaim == null)
            {
                logger.LogWarning("Verifikasi tanda tangan token lisensi gagal.");
                return null;
            }

            var exp = KeWaktuUtc(klaim.Exp);
            var cek = KeWaktuUtc(klaim.Cek);
            // Sah sampai yang lebih akhir dari (tanggal bayar) atau (tenggang).
            var sampai = BatasSah(exp, cek);

            var sekarang = DateTime.UtcNow;
            var aktif = sampai.HasValue && sekarang <= sampai.Value;
            var sisaHari = aktif && sampai.HasValue ? (int)Math.Ceiling((sampai.Value - sekarang).TotalDays) : 0;

            return new StatusLisensi
            {
                Aktif = aktif,
                BerlakuSampai = exp,
                SahSampai = sampai,
                CekTerakhir = cek ?? DateTime.MinValue,
                NamaDesa = klaim.Desa,
                Paket = klaim.Paket,
                SisaHariTenggang = Math.Max(0, sisaHari),
                Token = token
            };
        }

        /// <summary>
        /// Batas sah efektif: yang lebih akhir dari (tanggal bayar) atau
        /// (waktu cek terakhir + masa tenggang). null berarti tidak ada batas
        /// yang bisa dibaca.
        ///
        /// Publik &amp; statis supaya rumus ini — satu-satunya yang menentukan
        /// kapan aplikasi berhenti sah — bisa diuji langsung tanpa token, dan
        /// supaya halaman Langganan menampilkan tanggal yang sama persis dengan
        /// yang dipakai gerbang fitur.
        /// </summary>
        public static DateTime? BatasSah(DateTime? berlakuSampai, DateTime? cekTerakhir)
        {
            DateTime? tenggang = cekTerakhir is { } cek && cek > DateTime.MinValue
                ? cek.AddDays(MASA_TENGGANG_HARI)
                : null;

            if (!berlakuSampai.HasValue) return tenggang;
            if (!tenggang.HasValue) return berlakuSampai;

            return berlakuSampai.Value > tenggang.Value ? berlakuSampai.Value : tenggang;
        }

        /// <summary>
        /// Unix detik → UTC, atau null bila nilainya di luar rentang yang
        /// masuk akal (token rusak) — bukan DateTime.MinValue, supaya
        /// "tidak bisa dibaca" tidak tertukar dengan "sangat lama lalu".
        /// </summary>
        private static DateTime? KeWaktuUtc(long unix)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private void SimpanCache(CacheLisensi cache)
        {
            try
            {
                Directory.CreateDirectory(FolderLocal);
                File.WriteAllText(LokasiCache, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyimpan cache lisensi");
            }
        }

        private void HapusCache()
        {
            try { if (File.Exists(LokasiCache)) File.Delete(LokasiCache); }
            catch (Exception ex) { _logger.LogWarning(ex, "Gagal menghapus cache lisensi"); }
        }

        /// <summary>
        /// Nomor seri perangkat yang stabil (bukan hardware serial yang bisa
        /// berubah di reinstall). Dipakai server untuk membatasi jumlah
        /// perangkat aktif per langganan.
        ///
        /// Sengaja HANYA dari nama mesin + nama pengguna, TANPA jalur berkas
        /// aplikasi. Versi sebelumnya ikut menyertakan
        /// <c>MainModule.FileVersionInfo.FileName</c>, sehingga SATU komputer
        /// menghasilkan id berbeda untuk Debug vs Release vs hasil publish
        /// (jalur exe-nya beda) — tiap kali dihitung sebagai "perangkat baru"
        /// dan perlahan menghabiskan kuota perangkat desa sampai aktivasi
        /// ditolak 403, padahal hanya ada satu PC. Nama mesin + pengguna tetap
        /// sama di semua build, jadi satu PC = satu perangkat.
        /// </summary>
        private static string AmbilIdPerangkat()
        {
            try
            {
                var material = string.Join("|", Environment.MachineName, Environment.UserName);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                return B64Url.Encode(hash)[..22]; // ~128 bit
            }
            catch
            {
                return "tidak-diketahui";
            }
        }

        /// <summary>Bentuk cache lisensi di disk: token bertanda tangan saja.
        /// Kode aktivasi disimpan di <see cref="SuDesApp.Utilities.AppPreferenceStore"/>,
        /// bukan di sini — satu tempat saja supaya tidak bisa saling menyimpang.</summary>
        private sealed class CacheLisensi
        {
            public string Token { get; set; } = "";
        }
    }
}
