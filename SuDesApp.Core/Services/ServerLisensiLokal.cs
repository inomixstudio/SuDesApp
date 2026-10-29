using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>
    /// Server lisensi yang dijalankan DI KOMPUTER INI untuk administrasi lokal
    /// (pusat administrasi + data langganan yang menetap).
    ///
    /// Siapa yang memiliki siklus hidupnya: APLIKASI. Sebelumnya server ini
    /// dinyalakan skrip bantu (<c>Tools/SiapkanServerLisensi.ps1</c>) dan
    /// dibiarkan hidup, sehingga proses server tertinggal setelah aplikasi
    /// ditutup. Sekarang aplikasi yang menyalakannya, dan aplikasi juga yang
    /// mematikannya saat keluar (lihat <c>App.OnExit</c> dan <c>Matikan</c>) —
    /// termasuk bila aplikasi mati mendadak, lewat pembersihan sisa proses pada
    /// pembukaan berikutnya (<see cref="BersihkanSisa"/>).
    ///
    /// Batas tanggung jawabnya: kelas ini hanya menjalankan proses, menunggu
    /// server benar-benar menjawab <c>/healthz</c>, dan mematikannya. Isi
    /// server — harga paket, tanda tangan lisensi, penyimpanan langganan —
    /// tetap milik proyek <c>SuDesApp.Server</c>.
    /// </summary>
    public sealed class ServerLisensiLokal : IDisposable
    {
        /// <summary>Batas tunggu server menjawab <c>/healthz</c> (build pertama dotnet run bisa lama).</summary>
        private const int BatasTungguSiapDetik = 120;

        /// <summary>Toleransi selisih waktu mulai proses saat mencocokkan penanda (detik).</summary>
        private const double ToleransiSelisihMulaiDetik = 2.0;

        /// <summary>Batas satu berkas server.log; lebih dari ini diputar ke arsip.</summary>
        internal const long BatasUkuranLogByte = 5 * 1024 * 1024;

        /// <summary>Jumlah arsip server.log yang disimpan (yang tertua dihapus).</summary>
        internal const int JumlahArsipLog = 3;

        private static readonly JsonSerializerOptions OpsiPenanda = new() { WriteIndented = true };

        private readonly ILogger<ServerLisensiLokal> _logger;
        private readonly OpsiServerLokal _opsi;
        private readonly object _kunci = new();
        private readonly object _kunciLog = new();
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

        /// <summary>Ukuran server.log terakhir yang diketahui; -1 = belum dibaca.</summary>
        private long _ukuranLog = -1;

        /// <summary>
        /// Gerbang menyalakan: dua permintaan yang berdekatan (mis. operator
        /// mengklik ulang kotak centang saat server masih menyala) tidak boleh
        /// menjalankan dua proses yang berebut port yang sama.
        /// </summary>
        private readonly SemaphoreSlim _gerbangMulai = new(1, 1);

        private Process? _proses;

        /// <summary>
        /// Diminta berhenti SAAT menyalakan. Tanpa ini, mematikan server ketika
        /// prosesnya belum terpasang ke <see cref="_proses"/> tidak membunuh apa
        /// pun — server tetap hidup padahal operator sudah mematikannya.
        /// </summary>
        private volatile bool _batal;

        public ServerLisensiLokal(ILogger<ServerLisensiLokal> logger)
            : this(logger, OpsiServerLokal.Bawaan())
        {
        }

        /// <param name="opsi">Jalur &amp; port yang dipakai; disuntikkan agar dapat diuji tanpa menyentuh profil pengguna.</param>
        internal ServerLisensiLokal(ILogger<ServerLisensiLokal> logger, OpsiServerLokal opsi)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(opsi);

            _logger = logger;
            _opsi = opsi;
        }

        /// <summary>True bila proses server yang kita nyalakan masih hidup.</summary>
        public bool Berjalan
        {
            get
            {
                lock (_kunci)
                {
                    try
                    {
                        return _proses is { HasExited: false };
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Alamat dasar server lokal, mis. <c>http://127.0.0.1:5110</c>.
        ///
        /// 127.0.0.1 (bukan <c>localhost</c>) karena itulah host yang dipakai
        /// <c>Lisensi:UrlServer</c>, launchSettings server, dan skrip lokal —
        /// alamat yang dijalankan aplikasi harus sama dengan alamat yang sudah
        /// tersimpan, bukan versi lain dari host yang sama.
        /// </summary>
        public string Alamat => $"http://127.0.0.1:{_opsi.Port}";

        /// <summary>Alasan kegagalan terakhir saat menyalakan, atau null bila tidak ada.</summary>
        public string? GalatTerakhir { get; private set; }

        /// <summary>Berkas log keluaran server (stdout + stderr) untuk diperiksa operator.</summary>
        public string JalurLog => _opsi.JalurLog;

        /// <summary>Port server lokal.</summary>
        public int Port => _opsi.Port;

        /// <summary>
        /// Nyalakan server lisensi lokal dan tunggu sampai menjawab
        /// <c>/healthz</c>. Mengembalikan true bila server siap dipakai. Aman
        /// dipanggil berkali-kali: server yang sudah berjalan tidak dinyalakan
        /// dua kali.
        /// </summary>
        public async Task<bool> NyalakanAsync(CancellationToken ct = default)
        {
            await _gerbangMulai.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await NyalakanIntiAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gerbangMulai.Release();
            }
        }

        private async Task<bool> NyalakanIntiAsync(CancellationToken ct)
        {
            lock (_kunci)
            {
                if (_proses is { HasExited: false })
                {
                    GalatTerakhir = null;
                    return true;
                }
            }

            // Permintaan menyalakan baru membatalkan permintaan berhenti lama.
            _batal = false;

            var target = CariTarget(
                AppPreferenceStore.GetJalurServerLisensiLokal(),
                _opsi.FolderAplikasi);

            if (target is null)
            {
                GalatTerakhir =
                    "Berkas server lisensi tidak ditemukan. Letakkan hasil publish SuDesApp.Server di folder " +
                    $"\"server\" di samping aplikasi, atau isi lokasi proyeknya di halaman Langganan. Dicari dari: {_opsi.FolderAplikasi}";
                _logger.LogWarning("Server lisensi lokal tidak dinyalakan: {Alasan}", GalatTerakhir);
                return false;
            }

            Directory.CreateDirectory(_opsi.FolderData);

            try
            {
                _logger.LogInformation(
                    "Menyalakan server lisensi lokal: {Berkas} {Argumen} (port {Port})",
                    target.Value.Berkas, target.Value.Argumen, _opsi.Port);

                var info = new ProcessStartInfo
                {
                    FileName = target.Value.Berkas,
                    Arguments = target.Value.Argumen,
                    WorkingDirectory = target.Value.FolderKerja,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Lingkungan pengembangan: server memakai penyimpanan berkas
                // (data langganan menetap) dan memuat appsettings.lokal.json.
                info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
                info.Environment["ASPNETCORE_URLS"] = Alamat;
                info.Environment["PenyimpananLokal__Path"] = _opsi.JalurData;

                // Server lokal SELALU memakai berkas milik aplikasi ini, walau
                // appsettings.lokal.json di mesin pengembang menyimpan kredensial
                // Supabase: tanpa ini, "server lokal" diam-diam menulis langganan
                // ke database produksi, data yang dijanjikan tersimpan di komputer
                // desa tidak ada di sana, dan uji lokal bisa mengubah data nyata.
                // Program.cs membaca env ini lebih dulu daripada konfigurasi, jadi
                // nilai kosong berarti "tanpa database".
                info.Environment["SUPABASE_HOST"] = string.Empty;
                info.Environment["SUPABASE_DB_PASSWORD"] = string.Empty;

                var proses = new Process { StartInfo = info, EnableRaisingEvents = true };
                proses.OutputDataReceived += (_, e) => TulisLog(e.Data);
                proses.ErrorDataReceived += (_, e) => TulisLog(e.Data);

                if (!proses.Start())
                {
                    proses.Dispose();
                    GalatTerakhir = "Proses server lisensi tidak bisa dijalankan.";
                    return false;
                }

                proses.BeginOutputReadLine();
                proses.BeginErrorReadLine();

                lock (_kunci)
                {
                    _proses?.Dispose();
                    _proses = proses;
                }

                // Operator mematikan server selagi prosesnya menyala: jangan
                // biarkan proses itu hidup hanya karena penantian siap belum
                // melihatnya.
                if (_batal)
                {
                    GalatTerakhir = "Server lisensi lokal dibatalkan sebelum siap.";
                    Matikan();
                    return false;
                }

                if (await TungguSiapAsync(proses, ct).ConfigureAwait(false))
                {
                    TulisPenanda(proses);
                    GalatTerakhir = null;
                    _logger.LogInformation("Server lisensi lokal siap di {Alamat}. Log: {Log}", Alamat, JalurLog);
                    return true;
                }

                // Gagal siap: proses yang tidak menjawab jangan dibiarkan hidup —
                // itu justru server "tertinggal" yang ingin dihindari.
                Matikan();
                return false;
            }
            catch (Exception ex)
            {
                GalatTerakhir = "Gagal menyalakan server lisensi lokal: " + ex.Message;
                _logger.LogError(ex, "Gagal menyalakan server lisensi lokal");
                Matikan();
                return false;
            }
        }

        /// <summary>
        /// Nyalakan server lokal, lalu jadikan alamatnya alamat lisensi aplikasi.
        ///
        /// Satu salinan kebijakan untuk dua jalur — pembukaan aplikasi
        /// (<c>App</c>) dan tombol di halaman Langganan — supaya server yang
        /// menyala dan alamat yang dipakai Cek Status, tagihan, serta Pusat
        /// Administrasi tidak pernah berbeda. Alamat disimpan sebagai preferensi
        /// agar sesi berikutnya (sebelum server sempat menyala) sudah menunjuk
        /// tempat yang benar.
        /// </summary>
        public async Task<bool> NyalakanDanPakaiAsync(LisensiManager lisensi, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(lisensi);

            // Rilis yang terkunci pada server produksi tidak boleh diarahkan ke
            // server lokal: alamat itu tidak bisa diganti, jadi menyalakan server
            // ini hanya memboroskan port dan menyembunyikan kesalahan alamat.
            if (lisensi.UrlServerTerkunci)
            {
                GalatTerakhir =
                    "Aplikasi ini terkunci pada server lisensi produksi, jadi server lokal tidak dinyalakan.";
                return false;
            }

            if (!await NyalakanAsync(ct).ConfigureAwait(false)) return false;

            lisensi.UrlServer = Alamat;
            AppPreferenceStore.SetUrlServerLisensi(Alamat);
            return true;
        }

        /// <summary>
        /// Matikan server lokal beserta SELURUH proses turunannya (mode
        /// <c>dotnet run</c> menjalankan server sebagai proses anak) dan buang
        /// penanda sisa proses. Aman dipanggil walau server tidak pernah
        /// dinyalakan.
        /// </summary>
        public void Matikan()
        {
            // Penanda dibaca oleh penantian siap: proses yang sedang menyala
            // akan berhenti sendiri di titik pemeriksaan berikutnya.
            _batal = true;

            Process? proses;
            lock (_kunci)
            {
                proses = _proses;
                _proses = null;
            }

            if (proses is null)
            {
                BersihkanSisa(_opsi.JalurPenanda, _logger, _opsi.Port);
                return;
            }

            try
            {
                if (!proses.HasExited)
                {
                    proses.Kill(entireProcessTree: true);
                    if (!proses.WaitForExit(10_000))
                    {
                        _logger.LogWarning("Server lisensi lokal tidak berhenti dalam 10 detik.");
                    }
                    else
                    {
                        _logger.LogInformation("Server lisensi lokal dimatikan.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mematikan server lisensi lokal");
            }
            finally
            {
                try { proses.Dispose(); } catch { /* proses sudah tidak dipakai */ }
                HapusPenanda(_opsi.JalurPenanda);
            }
        }

        /// <summary>
        /// Bunuh server lokal yang tertinggal dari sesi yang mati mendadak
        /// (aplikasi ditutup paksa, komputer dimatikan paksa). HANYA proses yang
        /// cocok dengan penanda — pid yang sudah dipakai ulang oleh program lain
        /// tidak boleh ikut dimatikan.
        /// </summary>
        public static void BersihkanSisa()
        {
            string penanda = JalurPenandaBawaan();
            BersihkanSisa(penanda, logger: null, port: null);
        }

        /// <param name="jalurPenanda">Berkas penanda proses server.</param>
        /// <param name="logger">Null = tanpa log (pemanggilan sangat awal saat aplikasi dibuka).</param>
        /// <param name="port">Hanya untuk pesan; proses yang dibunuh ditentukan penanda.</param>
        internal static void BersihkanSisa(string jalurPenanda, ILogger? logger, int? port)
        {
            if (string.IsNullOrWhiteSpace(jalurPenanda) || !File.Exists(jalurPenanda)) return;

            try
            {
                string isi = File.ReadAllText(jalurPenanda);
                var penanda = JsonSerializer.Deserialize<PenandaServer>(isi, OpsiPenanda);

                if (penanda is null || penanda.Pid <= 0)
                {
                    HapusPenanda(jalurPenanda);
                    return;
                }

                Process? proses = null;
                try
                {
                    proses = Process.GetProcessById(penanda.Pid);
                }
                catch (ArgumentException)
                {
                    // Proses sudah tidak ada — sisa catatan saja.
                }

                if (proses is null)
                {
                    HapusPenanda(jalurPenanda);
                    return;
                }

                using (proses)
                {
                    double selisih = Math.Abs((proses.StartTime.ToUniversalTime() - penanda.Mulai).TotalSeconds);
                    if (selisih > ToleransiSelisihMulaiDetik)
                    {
                        logger?.LogWarning(
                            "Penanda server lisensi lokal menunjuk pid {Pid}, tetapi proses itu bukan server yang ditinggalkan (mulai {Mulai}); tidak dimatikan.",
                            penanda.Pid, proses.StartTime);
                    }
                    else if (proses.HasExited)
                    {
                        logger?.LogInformation("Server lisensi lokal dari sesi sebelumnya sudah berhenti sendiri.");
                    }
                    else
                    {
                        proses.Kill(entireProcessTree: true);
                        proses.WaitForExit(10_000);
                        logger?.LogWarning(
                            "Server lisensi lokal dari sesi sebelumnya (pid {Pid}{Port}) dimatikan saat aplikasi dibuka kembali.",
                            penanda.Pid,
                            port is null ? string.Empty : $", port {port}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Pemeriksaan sisa proses server lisensi lokal gagal");
            }
            finally
            {
                HapusPenanda(jalurPenanda);
            }
        }

        public void Dispose()
        {
            Matikan();
            _http.Dispose();
        }

        /// <summary>
        /// Tentukan berkas yang dijalankan: exe (jalur langsung), dll (lewat
        /// <c>dotnet</c>), atau proyek .csproj (lewat <c>dotnet run</c>).
        /// Urutan pencarian: jalur pilihan operator → folder <c>server</c> di
        /// samping aplikasi → proyek SuDesApp.Server pada salinan repositori.
        /// </summary>
        internal static (string Berkas, string Argumen, string FolderKerja)? CariTarget(
            string? jalurPreferensi, string folderAplikasi)
        {
            var dariPreferensi = DariJalur(jalurPreferensi);
            if (dariPreferensi is not null) return dariPreferensi;

            if (!string.IsNullOrWhiteSpace(folderAplikasi))
            {
                var dariFolderServer = DariFolder(Path.Combine(folderAplikasi, "server"));
                if (dariFolderServer is not null) return dariFolderServer;

                // Salinan repositori: aplikasi dijalankan dari bin/Debug saat
                // pengembangan, jadi proyek server dicari ke atas sampai ketemu.
                var dir = new DirectoryInfo(folderAplikasi);
                while (dir is not null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
                    {
                        var dariRepo = DariFolder(Path.Combine(dir.FullName, "SuDesApp.Server"));
                        if (dariRepo is not null) return dariRepo;
                        break;
                    }

                    dir = dir.Parent;
                }
            }

            return null;
        }

        private static (string Berkas, string Argumen, string FolderKerja)? DariJalur(string? jalur)
        {
            if (string.IsNullOrWhiteSpace(jalur)) return null;
            string bersih = jalur.Trim().Trim('"');

            if (File.Exists(bersih)) return DariBerkas(bersih);
            if (Directory.Exists(bersih)) return DariFolder(bersih);
            return null;
        }

        private static (string Berkas, string Argumen, string FolderKerja)? DariFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

            string exe = Path.Combine(folder, "SuDesApp.Server.exe");
            if (File.Exists(exe)) return DariBerkas(exe);

            string dll = Path.Combine(folder, "SuDesApp.Server.dll");
            if (File.Exists(dll)) return DariBerkas(dll);

            string csproj = Path.Combine(folder, "SuDesApp.Server.csproj");
            if (File.Exists(csproj)) return DariBerkas(csproj);

            return null;
        }

        private static (string Berkas, string Argumen, string FolderKerja) DariBerkas(string berkas)
        {
            string folder = Path.GetDirectoryName(berkas) ?? AppContext.BaseDirectory;

            return Path.GetExtension(berkas).ToLowerInvariant() switch
            {
                ".dll" => ("dotnet", $"\"{berkas}\"", folder),
                ".csproj" => ("dotnet", $"run --project \"{berkas}\" --no-launch-profile", folder),
                _ => (berkas, string.Empty, folder)
            };
        }

        private async Task<bool> TungguSiapAsync(Process proses, CancellationToken ct)
        {
            string url = Alamat + "/healthz";
            var batas = DateTime.UtcNow.AddSeconds(BatasTungguSiapDetik);

            while (DateTime.UtcNow < batas)
            {
                ct.ThrowIfCancellationRequested();

                if (_batal)
                {
                    GalatTerakhir = "Server lisensi lokal dibatalkan sebelum siap.";
                    return false;
                }

                if (proses.HasExited)
                {
                    GalatTerakhir =
                        $"Server lisensi lokal berhenti sendiri (kode {proses.ExitCode}) tanpa sempat melayani. " +
                        $"Periksa log: {JalurLog}";
                    _logger.LogError("Server lisensi lokal keluar dengan kode {Kode}. Log: {Log}", proses.ExitCode, JalurLog);
                    return false;
                }

                try
                {
                    using var jawaban = await _http.GetAsync(url, ct).ConfigureAwait(false);
                    if (jawaban.IsSuccessStatusCode) return true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    // Belum siap: server masih membangun/menyalakan. Ditunggu lagi.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            GalatTerakhir =
                $"Server lisensi lokal tidak menjawab di {url} dalam {BatasTungguSiapDetik} detik. Periksa log: {JalurLog}";
            _logger.LogError("Server lisensi lokal tidak siap setelah {Detik} detik", BatasTungguSiapDetik);
            return false;
        }

        private void TulisLog(string? baris)
        {
            if (baris is null) return;

            // stdout dan stderr server datang dari dua thread berbeda; tanpa
            // kunci, baris bisa saling menyela dan penghitungan ukuran kacau.
            lock (_kunciLog)
            {
                try
                {
                    if (_ukuranLog < 0)
                    {
                        _ukuranLog = File.Exists(JalurLog) ? new FileInfo(JalurLog).Length : 0;
                    }

                    PutarLogBilaBesar();

                    // FileShare.ReadWrite: operator boleh membuka lognya saat server
                    // masih berjalan.
                    using (var aliran = new FileStream(
                        JalurLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var penulis = new StreamWriter(aliran))
                    {
                        penulis.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {baris}");
                    }

                    _ukuranLog = new FileInfo(JalurLog).Length;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Baris log server lisensi lokal tidak bisa ditulis");
                }
            }
        }

        /// <summary>
        /// Pindahkan server.log ke arsip bila sudah melewati batas, lalu buang
        /// arsip di luar jumlah yang disimpan. Kegagalan memutar (mis. berkas
        /// sedang dibuka dengan kunci tulis) tidak boleh membuang baris log yang
        /// sedang ditulis: pemanggil melanjutkan penulisan ke berkas aktif.
        /// </summary>
        private void PutarLogBilaBesar()
        {
            if (_ukuranLog < BatasUkuranLogByte || !File.Exists(JalurLog)) return;

            try
            {
                string? folder = Path.GetDirectoryName(JalurLog);
                if (string.IsNullOrEmpty(folder)) return;

                string arsip = Path.Combine(
                    folder,
                    $"server-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
                File.Move(JalurLog, arsip, overwrite: true);
                _ukuranLog = 0;

                var arsipLama = Directory.EnumerateFiles(folder, "server-*.log")
                    .Select(jalur => new FileInfo(jalur))
                    .OrderByDescending(berkas => berkas.LastWriteTimeUtc)
                    .Skip(JumlahArsipLog)
                    .ToArray();
                foreach (var berkas in arsipLama)
                {
                    try { berkas.Delete(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Arsip sedang dibuka — dicoba lagi pada pemutaran berikutnya.
                    }
                }
            }
            catch (Exception ex)
            {
                // Biarkan berkas aktif apa adanya; baris berikutnya tetap ditulis.
                // Kegagalan apa pun di sini tidak boleh membuang baris log server.
                _logger.LogDebug(ex, "server.log tidak bisa diputar; berkas aktif tetap dipakai.");
            }
        }

        private void TulisPenanda(Process proses)
        {
            try
            {
                var penanda = new PenandaServer(proses.Id, proses.StartTime.ToUniversalTime(), Alamat);
                File.WriteAllText(_opsi.JalurPenanda, JsonSerializer.Serialize(penanda, OpsiPenanda));
            }
            catch (Exception ex)
            {
                // Penanda hanya untuk pembersihan sesi berikutnya; kegagalannya
                // tidak boleh menggagalkan server yang sudah berjalan.
                _logger.LogDebug(ex, "Penanda proses server lisensi lokal tidak bisa ditulis");
            }
        }

        private static void HapusPenanda(string jalurPenanda)
        {
            try
            {
                if (File.Exists(jalurPenanda)) File.Delete(jalurPenanda);
            }
            catch
            {
                // Sisa berkas penanda tidak berbahaya: pemeriksaan berikutnya
                // tetap mencocokkan pid + waktu mulai sebelum mematikan proses.
            }
        }

        /// <summary>Catatan proses server yang sedang berjalan, untuk pembersihan sesi berikutnya.</summary>
        internal sealed record PenandaServer(int Pid, DateTime Mulai, string Alamat);

        /// <summary>Jalur bawaan berkas penanda proses.</summary>
        internal static string JalurPenandaBawaan() => Path.Combine(OpsiServerLokal.FolderDataBawaan(), "server.pid.json");

        /// <summary>Jalur bawaan berkas data langganan server lokal.</summary>
        internal static string JalurDataBawaan() => Path.Combine(OpsiServerLokal.FolderDataBawaan(), "langganan.json");
    }

    /// <summary>
    /// Jalur dan port yang dipakai <see cref="ServerLisensiLokal"/>. Dipisah
    /// sebagai data supaya uji bisa memakai folder sementara, dan supaya
    /// halaman Langganan bisa menampilkan lokasi yang sama dengan yang dipakai.
    /// </summary>
    public sealed record OpsiServerLokal(
        int Port,
        string FolderAplikasi,
        string FolderData,
        string JalurData,
        string JalurLog,
        string JalurPenanda)
    {
        /// <summary>Opsi bawaan dari preferensi aplikasi dan profil pengguna.</summary>
        public static OpsiServerLokal Bawaan()
        {
            string folderData = FolderDataBawaan();
            return new OpsiServerLokal(
                AppPreferenceStore.GetPortServerLisensiLokal(),
                AppContext.BaseDirectory,
                folderData,
                Path.Combine(folderData, "langganan.json"),
                Path.Combine(folderData, "server.log"),
                Path.Combine(folderData, "server.pid.json"));
        }

        /// <summary>%LOCALAPPDATA%\SuDesApp\server-lisensi (folder aplikasi bila LOCALAPPDATA kosong).</summary>
        public static string FolderDataBawaan()
        {
            string akar = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(akar)) akar = AppContext.BaseDirectory;
            return Path.Combine(akar, "SuDesApp", "server-lisensi");
        }
    }
}
