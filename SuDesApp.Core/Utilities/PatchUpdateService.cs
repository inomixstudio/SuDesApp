using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuDesApp.Utilities
{
    /// <summary>Hasil pemeriksaan kesiapan sebuah tambalan untuk aplikasi yang terpasang.</summary>
    public class RencanaTambalan
    {
        /// <summary>Berkas yang benar-benar berubah dan akan diganti (jalur relatif).</summary>
        public List<BerkasTambalan> BerkasDiganti { get; set; } = new();

        /// <summary>Berkas yang isinya sudah sama dengan rilis baru (tidak perlu diganti).</summary>
        public int BerkasSudahSama { get; set; }

        /// <summary>Berkas di paket tambalan yang sengaja tidak diganti (data pengguna).</summary>
        public List<string> BerkasDilindungi { get; set; } = new();

        /// <summary>Berkas yang harus dihapus dari folder aplikasi.</summary>
        public List<string> BerkasDihapus { get; set; } = new();

        /// <summary>Alasan tambalan tidak bisa dipakai (kosong = siap dipasang).</summary>
        public string AlasanTidakBisa { get; set; } = string.Empty;

        public bool BisaDipakai => AlasanTidakBisa.Length == 0;

        /// <summary>Total ukuran berkas yang akan diganti (bukan ukuran unduhan zip).</summary>
        public long TotalByte => BerkasDiganti.Sum(b => b.Ukuran);

        public static RencanaTambalan Ditolak(string alasan) => new() { AlasanTidakBisa = alasan };
    }

    /// <summary>Tambalan yang sudah diunduh, diverifikasi, dan disiapkan untuk dipasang.</summary>
    public class TambalanSiap
    {
        /// <summary>Folder kerja tambalan (di dalam folder sementara aplikasi).</summary>
        public string FolderKerja { get; set; } = string.Empty;

        /// <summary>Folder berisi berkas baru hasil ekstraksi (siap disalin).</summary>
        public string FolderBerkas { get; set; } = string.Empty;

        /// <summary>Skrip PowerShell yang menunggu aplikasi tertutup lalu menyalin berkas.</summary>
        public string SkripPenerapan { get; set; } = string.Empty;

        /// <summary>Daftar berkas relatif yang akan disalin.</summary>
        public List<string> DaftarBerkas { get; set; } = new();

        public List<string> DaftarHapus { get; set; } = new();

        public string Versi { get; set; } = string.Empty;

        public int JumlahBerkas => DaftarBerkas.Count;

        public long TotalByte { get; set; }
    }

    /// <summary>Hasil penerapan tambalan yang ditulis di disk dan dibaca lagi saat aplikasi mulai.</summary>
    public class HasilTambalan
    {
        public string Versi { get; set; } = string.Empty;
        public bool Berhasil { get; set; }
        public string Pesan { get; set; } = string.Empty;
        public int JumlahBerkas { get; set; }
        public DateTime Waktu { get; set; } = DateTime.Now;

        private static readonly JsonSerializerOptions OpsiJson = new() { WriteIndented = true };

        public static string PathDefault => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SuDesApp", "hasil-tambalan.json");

        public string ToJson() => JsonSerializer.Serialize(this, OpsiJson);

        public static HasilTambalan? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<HasilTambalan>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Pembaruan kecil (tambalan berkas) tanpa installer penuh.
    ///
    /// Alurnya:
    /// 1. <see cref="AmbilPenawaranAsync"/> membaca <c>patch.json</c> pada rilis; rilis
    ///    yang tidak menyertakannya berarti harus memakai installer penuh.
    /// 2. <see cref="SusunRencana"/> membandingkan sidik jari SHA-256 berkas lokal
    ///    dengan daftar pada patch.json, sehingga hanya berkas yang benar-benar
    ///    berbeda yang diganti (dan berkas data pengguna tidak pernah ikut diganti).
    /// 3. <see cref="UnduhDanSiapkanAsync"/> mengunduh zip tambalan, memverifikasi
    ///    SHA-256 zip DAN setiap berkasnya, lalu mengekstraknya ke folder sementara
    ///    beserta skrip penerap.
    /// 4. <see cref="JalankanPenerapan"/> menjalankan skrip yang menunggu aplikasi
    ///    tertutup, menyalin berkas, menuliskan hasilnya, dan menjalankan aplikasi lagi.
    /// </summary>
    public class PatchUpdateService
    {
        private readonly UpdateService _updateService;
        private readonly ILogger<PatchUpdateService> _logger;
        private readonly Func<string> _folderAplikasi;
        private readonly Func<string> _versiTerpasang;

        public PatchUpdateService(
            UpdateService updateService,
            ILogger<PatchUpdateService>? logger = null,
            Func<string>? folderAplikasi = null,
            Func<string>? versiTerpasang = null)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _logger = logger ?? NullLogger<PatchUpdateService>.Instance;
            _folderAplikasi = folderAplikasi ?? (() => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            _versiTerpasang = versiTerpasang ?? (() =>
                (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0)).ToString());
        }

        /// <summary>Folder aplikasi yang dipasang pengguna (target penerapan tambalan).</summary>
        public string FolderAplikasi => _folderAplikasi();

        /// <summary>
        /// Jalur berkas data pengguna yang TIDAK PERNAH boleh ditimpa pembaruan:
        /// database, templat, hasil PDF, log, dan pengaturan milik pengguna.
        /// </summary>
        public static readonly string[] AwalanDilindungi =
        {
            "database/", "templates/", "template/", "temppdf/", "temp/", "output/", "logs/",
            "backup/", "resources/sudesapp.json"
        };

        private static readonly string[] NamaDilindungi =
        {
            "appsettings.json", "penomoran-surat.json", "template-bawaan.json",
            "pengaturan-cetak.json", "data-pengguna.json"
        };

        private static readonly string[] EkstensiDilindungi =
        {
            ".db", ".db-wal", ".db-shm", ".log", ".bak", ".sqlite"
        };

        // =====================================================================
        // 1. Penawaran tambalan dari rilis
        // =====================================================================

        /// <summary>
        /// Baca <c>patch.json</c> dari aset rilis. Mengembalikan null bila rilis
        /// tidak menyediakannya atau isinya tidak dapat dipakai — pemanggil lalu
        /// memakai jalur installer penuh.
        /// </summary>
        public async Task<TambalanInfo?> AmbilPenawaranAsync(UpdateInfo rilis, CancellationToken cancellationToken = default)
        {
            if (rilis == null)
            {
                _logger.LogWarning("Penawaran tambalan diminta tanpa informasi rilis.");
                return null;
            }

            var aset = rilis.CariAset("patch.json");
            if (aset == null)
            {
                _logger.LogInformation("Rilis {Versi} tidak menyertakan patch.json; memakai installer penuh.", rilis.Version);
                return null;
            }

            string sementara = Path.Combine(Path.GetTempPath(), $"SuDesApp_patch_{Guid.NewGuid():N}.json");
            try
            {
                var url = _updateService.TautanAset(aset);
                // patch.json tidak punya digest sendiri di sebagian rilis; hash opsional.
                await _updateService.UnduhKeBerkasAsync(url, sementara, null, cancellationToken, aset.Sha256);

                var json = await File.ReadAllTextAsync(sementara, cancellationToken);
                var info = TambalanInfo.FromJson(json);
                if (info == null)
                {
                    _logger.LogWarning("patch.json rilis {Versi} tidak dapat dibaca.", rilis.Version);
                    return null;
                }

                _logger.LogInformation("Penawaran tambalan {Versi}: {Jenis}, {Jumlah} berkas.", info.Versi, info.Jenis, info.JumlahBerkas);
                return info;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca patch.json rilis {Versi}; memakai installer penuh.", rilis.Version);
                return null;
            }
            finally
            {
                TryHapusBerkas(sementara);
            }
        }

        // =====================================================================
        // 2. Rencana: berkas mana yang perlu diganti
        // =====================================================================

        /// <summary>
        /// Susun rencana penerapan: periksa kecocokan versi, keamanan jalur, dan
        /// bandingkan sidik jari berkas lokal dengan patch.json.
        /// </summary>
        public RencanaTambalan SusunRencana(TambalanInfo? tambalan, string? versiTerpasang = null)
        {
            if (tambalan == null)
                return RencanaTambalan.Ditolak("Rilis ini tidak menyediakan pembaruan kecil; gunakan installer penuh.");

            if (!tambalan.BisaDitambal)
                return RencanaTambalan.Ditolak(
                    "Rilis ini ditandai sebagai pembaruan besar sehingga memerlukan installer penuh.");

            string terpasang = versiTerpasang ?? _versiTerpasang();
            if (!tambalan.CocokDenganVersiTerpasang(terpasang))
            {
                string asal = tambalan.DariVersi == null || tambalan.DariVersi.Count == 0
                    ? "-"
                    : string.Join(", ", tambalan.DariVersi);
                return RencanaTambalan.Ditolak(
                    $"Pembaruan kecil hanya untuk versi {asal}; versi terpasang {TambalanInfo.VersiSama(terpasang)}. " +
                    "Gunakan installer penuh agar seluruh berkas pasti cocok.");
            }

            if (string.IsNullOrWhiteSpace(tambalan.Sha256Patch) || !TambalanInfo.IsSha256Valid(tambalan.Sha256Patch))
                return RencanaTambalan.Ditolak("Paket pembaruan kecil tidak menyertakan sidik jari SHA-256; installer penuh diperlukan.");

            var rencana = new RencanaTambalan();
            string folder = FolderAplikasi;

            // Folder aplikasi harus bisa ditulis proses ini; kalau tidak (mis. dipasang
            // di lokasi hanya-baca), installer penuh yang dipakai.
            if (!FolderBisaDitulis(folder))
            {
                return RencanaTambalan.Ditolak(
                    "Folder aplikasi tidak dapat ditulis oleh pengguna ini, sehingga pembaruan kecil tidak bisa dipasang. " +
                    "Gunakan installer penuh.");
            }

            foreach (var berkas in tambalan.Berkas)
            {
                string relatif;
                try
                {
                    relatif = NormalisasiJalur(berkas.Path);
                }
                catch (InvalidOperationException)
                {
                    _logger.LogWarning("Jalur berkas tambalan ditolak: {Path}", berkas.Path);
                    return RencanaTambalan.Ditolak("Paket pembaruan memuat jalur berkas yang tidak sah; installer penuh diperlukan.");
                }

                if (Dilindungi(relatif))
                {
                    rencana.BerkasDilindungi.Add(relatif);
                    continue;
                }

                string lokal = Path.Combine(folder, relatif.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(lokal) &&
                    string.Equals(HashBerkas(lokal), berkas.Sha256.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
                {
                    rencana.BerkasSudahSama++;
                    continue;
                }

                rencana.BerkasDiganti.Add(new BerkasTambalan
                {
                    Path = relatif,
                    Sha256 = berkas.Sha256,
                    Ukuran = berkas.Ukuran
                });
            }

            foreach (var jalur in tambalan.BerkasDihapus ?? new List<string>())
            {
                try
                {
                    var relatif = NormalisasiJalur(jalur);
                    if (!Dilindungi(relatif)) rencana.BerkasDihapus.Add(relatif);
                }
                catch (InvalidOperationException)
                {
                    // Jalur tidak sah cukup diabaikan; berkas tidak akan dihapus.
                }
            }

            if (rencana.BerkasDiganti.Count == 0 && rencana.BerkasDihapus.Count == 0)
            {
                return RencanaTambalan.Ditolak("Seluruh berkas sudah sama dengan versi terbaru (tidak ada yang perlu diganti).");
            }

            _logger.LogInformation(
                "Rencana tambalan {Versi}: {Ganti} berkas diganti, {Sama} sudah sama, {Lindungi} dilindungi.",
                tambalan.Versi, rencana.BerkasDiganti.Count, rencana.BerkasSudahSama, rencana.BerkasDilindungi.Count);

            return rencana;
        }

        // =====================================================================
        // 3. Unduh, verifikasi, siapkan
        // =====================================================================

        /// <summary>
        /// Unduh zip tambalan, verifikasi SHA-256 zip dan tiap berkasnya, ekstrak ke
        /// folder sementara, lalu tulis skrip penerap. Tidak ada berkas aplikasi yang
        /// disentuh pada tahap ini.
        /// </summary>
        public async Task<TambalanSiap> UnduhDanSiapkanAsync(
            UpdateInfo rilis,
            TambalanInfo tambalan,
            RencanaTambalan rencana,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (rilis == null) throw new ArgumentNullException(nameof(rilis));
            if (tambalan == null) throw new ArgumentNullException(nameof(tambalan));
            if (rencana == null || !rencana.BisaDipakai)
                throw new InvalidOperationException(rencana?.AlasanTidakBisa ?? "Rencana tambalan tidak siap.");

            var asetZip = rilis.CariAset(tambalan.BerkasPatch);
            if (asetZip == null)
                throw new InvalidOperationException($"Aset paket pembaruan '{tambalan.BerkasPatch}' tidak ditemukan pada rilis {rilis.Version}.");

            string folderKerja = Path.Combine(Path.GetTempPath(), $"SuDesApp_tambalan_{tambalan.Versi}_{Guid.NewGuid():N}");
            string folderBerkas = Path.Combine(folderKerja, "berkas");
            string zipPath = Path.Combine(folderKerja, tambalan.BerkasPatch);

            Directory.CreateDirectory(folderBerkas);

            // Verifikasi memakai digest resmi GitHub bila ada, kalau tidak memakai
            // yang tertulis di patch.json.
            string hashZip = !string.IsNullOrWhiteSpace(asetZip.Sha256) ? asetZip.Sha256! : tambalan.Sha256Patch;

            _logger.LogInformation("Mengunduh tambalan {Versi} dari {Url}", tambalan.Versi, asetZip.Url);
            await _updateService.UnduhKeBerkasAsync(_updateService.TautanAset(asetZip), zipPath, progress, cancellationToken, hashZip);

            EkstrakAman(zipPath, folderBerkas);
            VerifikasiHasilEkstrak(folderBerkas, rencana);

            var siap = new TambalanSiap
            {
                FolderKerja = folderKerja,
                FolderBerkas = folderBerkas,
                Versi = tambalan.Versi,
                DaftarBerkas = rencana.BerkasDiganti.Select(b => b.Path).ToList(),
                DaftarHapus = rencana.BerkasDihapus,
                TotalByte = rencana.TotalByte
            };

            siap.SkripPenerapan = Path.Combine(folderKerja, "terapkan-tambalan.ps1");
            await File.WriteAllTextAsync(
                siap.SkripPenerapan,
                SkripPenerapan(),
                new UTF8Encoding(false),
                cancellationToken);

            _logger.LogInformation("Tambalan {Versi} siap dipasang ({Jumlah} berkas) di {Folder}.",
                tambalan.Versi, siap.JumlahBerkas, folderKerja);

            return siap;
        }

        /// <summary>
        /// Jalankan skrip penerap secara terpisah dari aplikasi: skrip menunggu proses
        /// ini benar-benar tertutup, menyalin berkas, menulis hasil, lalu menjalankan
        /// aplikasi kembali.
        /// </summary>
        public void JalankanPenerapan(TambalanSiap siap, string? exeAplikasi = null, int? prosesId = null)
        {
            if (siap == null) throw new ArgumentNullException(nameof(siap));
            if (!File.Exists(siap.SkripPenerapan))
                throw new InvalidOperationException("Skrip penerapan tambalan tidak ditemukan.");

            string exe = exeAplikasi ?? Path.Combine(FolderAplikasi, "SuDesApp.exe");
            int pid = prosesId ?? Environment.ProcessId;

            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = string.Join(' ', new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", KunciArgumen(siap.SkripPenerapan),
                    "-ProsesId", pid.ToString(),
                    "-Aplikasi", KunciArgumen(FolderAplikasi),
                    "-Sumber", KunciArgumen(siap.FolderBerkas),
                    "-Daftar", KunciArgumen(Path.Combine(siap.FolderKerja, "daftar-berkas.txt")),
                    "-Hapus", KunciArgumen(Path.Combine(siap.FolderKerja, "daftar-hapus.txt")),
                    "-Hasil", KunciArgumen(HasilTambalan.PathDefault),
                    "-Versi", KunciArgumen(siap.Versi),
                    "-Mulai", KunciArgumen(exe)
                })
            };

            // Daftar berkas ditulis terpisah supaya skrip tidak perlu membaca JSON.
            File.WriteAllLines(
                Path.Combine(siap.FolderKerja, "daftar-berkas.txt"),
                siap.DaftarBerkas.Where(b => !string.IsNullOrWhiteSpace(b)),
                new UTF8Encoding(false));
            File.WriteAllLines(
                Path.Combine(siap.FolderKerja, "daftar-hapus.txt"),
                siap.DaftarHapus.Where(b => !string.IsNullOrWhiteSpace(b)),
                new UTF8Encoding(false));

            Process.Start(info);
            _logger.LogInformation("Skrip penerapan tambalan {Versi} dijalankan (menunggu proses {Pid}).", siap.Versi, pid);
        }

        // =====================================================================
        // 4. Pemasangan otomatis saat aplikasi ditutup (tanpa tanya pengguna)
        // =====================================================================

        /// <summary>
        /// Unduh + verifikasi paket tambalan TANPA UI sama sekali, dipakai pemasangan
        /// otomatis saat aplikasi ditutup (bila pengesahan membolehkannya lewat
        /// Pengaturan Aplikasi). Rencana boleh ditolak (mis. versi tidak cocok) —
        /// pemanggil cukup membatalkan rencananya tanpa mengganggu penutupan.
        /// Tidak ada berkas aplikasi yang disentuh pada tahap ini.
        /// </summary>
        public async Task<TambalanSiap?> SiapkanPembaruanOtomatisAsync(
            UpdateInfo rilis,
            CancellationToken cancellationToken = default)
        {
            if (rilis == null) return null;

            try
            {
                var tambalan = await AmbilPenawaranAsync(rilis, cancellationToken);
                if (tambalan == null) return null;

                var rencana = SusunRencana(tambalan, _versiTerpasang());
                if (!rencana.BisaDipakai) return null;

                return await UnduhDanSiapkanAsync(rilis, tambalan, rencana, null, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Pemasangan otomatis tambalan {Versi} gagal disiapkan; pemasangan dilewati.", rilis.Version);
                return null;
            }
        }

        // =====================================================================
        // Hasil penerapan (dibaca saat aplikasi dijalankan lagi)
        // =====================================================================

        /// <summary>
        /// Baca hasil penerapan tambalan yang ditulis skrip penerap, lalu hapus
        /// berkasnya supaya pemberitahuannya hanya muncul sekali.
        /// </summary>
        public static HasilTambalan? AmbilHasilTerakhir(string? path = null)
        {
            string berkas = path ?? HasilTambalan.PathDefault;
            try
            {
                if (!File.Exists(berkas)) return null;

                var hasil = HasilTambalan.FromJson(File.ReadAllText(berkas));
                File.Delete(berkas);
                return hasil;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // =====================================================================
        // Helper yang dipakai bersama pengujian
        // =====================================================================

        /// <summary>
        /// Rapikan jalur relatif berkas dari paket tambalan: garis miring depan
        /// diseragamkan, jalur absolut/naik ke atas folder aplikasi ditolak.
        /// </summary>
        public static string NormalisasiJalur(string? jalur)
        {
            if (string.IsNullOrWhiteSpace(jalur)) throw new InvalidOperationException("Jalur berkas kosong.");

            var bersih = jalur.Trim().Replace('\\', '/');
            if (bersih.StartsWith('/')) throw new InvalidOperationException($"Jalur absolut ditolak: {jalur}");
            if (bersih.Length > 1 && bersih[1] == ':') throw new InvalidOperationException($"Jalur absolut ditolak: {jalur}");

            var bagian = bersih.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (bagian.Count == 0) throw new InvalidOperationException("Jalur berkas kosong.");
            if (bagian.Any(b => b == ".." || b == "."))
                throw new InvalidOperationException($"Jalur keluar folder aplikasi ditolak: {jalur}");

            return string.Join('/', bagian);
        }

        /// <summary>
        /// Benar bila berkas relatif tersebut adalah data pengguna/pengaturan yang
        /// tidak boleh ditimpa pembaruan.
        /// </summary>
        public static bool Dilindungi(string? relatif)
        {
            if (string.IsNullOrWhiteSpace(relatif)) return true;

            var bersih = relatif.Trim().Replace('\\', '/').ToLowerInvariant();
            if (AwalanDilindungi.Any(a => bersih.StartsWith(a, StringComparison.Ordinal))) return true;

            var nama = bersih.Split('/')[^1];
            if (NamaDilindungi.Contains(nama)) return true;

            return EkstensiDilindungi.Any(e => nama.EndsWith(e, StringComparison.Ordinal));
        }

        /// <summary>
        /// Benar bila folder aplikasi dapat ditulis proses ini (dipakai untuk menolak
        /// tambalan pada pemasangan yang hanya-baca, mis. di Program Files).
        /// </summary>
        public static bool FolderBisaDitulis(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;

            string uji = Path.Combine(folder, $".sudesapp-uji-tulis-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(uji, "uji");
                File.Delete(uji);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>SHA-256 heksadesimal (huruf besar) isi sebuah berkas.</summary>
        public static string HashBerkas(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        /// <summary>
        /// Ekstrak zip tambalan dengan aman: setiap entri harus berada di dalam
        /// folder tujuan (tanpa <c>..</c> maupun jalur absolut).
        /// </summary>
        public static void EkstrakAman(string zipPath, string folderTujuan)
        {
            Directory.CreateDirectory(folderTujuan);
            string akar = Path.GetFullPath(folderTujuan + Path.DirectorySeparatorChar);

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entri in zip.Entries)
            {
                if (string.IsNullOrWhiteSpace(entri.Name)) continue; // entri folder

                var relatif = NormalisasiJalur(entri.FullName);
                string tujuan = Path.GetFullPath(Path.Combine(folderTujuan, relatif.Replace('/', Path.DirectorySeparatorChar)));
                if (!tujuan.StartsWith(akar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Entri zip keluar folder tujuan: {entri.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(tujuan)!);
                entri.ExtractToFile(tujuan, overwrite: true);
            }
        }

        /// <summary>
        /// Pastikan setiap berkas hasil ekstraksi benar-benar ada dan sidik jarinya
        /// cocok dengan patch.json sebelum aplikasi ditutup untuk penerapan.
        /// </summary>
        public static void VerifikasiHasilEkstrak(string folderBerkas, RencanaTambalan rencana)
        {
            foreach (var berkas in rencana.BerkasDiganti)
            {
                string lokal = Path.Combine(folderBerkas, berkas.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(lokal))
                    throw new InvalidOperationException($"Berkas '{berkas.Path}' tidak ada di dalam paket pembaruan.");

                if (!string.Equals(HashBerkas(lokal), berkas.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Berkas '{berkas.Path}' tidak cocok dengan sidik jarinya di patch.json.");
            }
        }

        /// <summary>
        /// Skrip PowerShell penerap: menunggu aplikasi tertutup (berdasarkan PID),
        /// menyalin berkas baru, menghapus berkas lama, menulis hasil, menjalankan
        /// aplikasi kembali, lalu membersihkan folder kerja.
        ///
        /// Seluruh nilai (folder aplikasi, daftar berkas, tujuan hasil) dikirim sebagai
        /// parameter supaya skrip ini sama persis dengan yang dibuat saat rilis.
        /// </summary>
        public static string SkripPenerapan()
        {
            var teks = new StringBuilder();
            teks.AppendLine("# SuDesApp - penerap pembaruan kecil (dibuat otomatis).");
            teks.AppendLine("# Menunggu aplikasi tertutup, mengganti berkas yang berubah, lalu membuka aplikasi lagi.");
            teks.AppendLine("param(");
            teks.AppendLine("    [int]$ProsesId = 0,");
            teks.AppendLine("    [string]$Aplikasi = '',");
            teks.AppendLine("    [string]$Sumber = '',");
            teks.AppendLine("    [string]$Daftar = '',");
            teks.AppendLine("    [string]$Hapus = '',");
            teks.AppendLine("    [string]$Hasil = '',");
            teks.AppendLine("    [string]$Versi = '',");
            teks.AppendLine("    [string]$Mulai = ''");
            teks.AppendLine(")");
            teks.AppendLine(string.Empty);
            teks.AppendLine("$ErrorActionPreference = 'Stop'");
            teks.AppendLine("$log = Join-Path (Split-Path -Parent $Daftar) 'penerapan.log'");
            teks.AppendLine(string.Empty);
            teks.AppendLine("function Tulis-Hasil([bool]$berhasil, [string]$pesan, [int]$jumlah) {");
            teks.AppendLine("    if ([string]::IsNullOrWhiteSpace($Hasil)) { return }");
            teks.AppendLine("    $objek = [ordered]@{");
            teks.AppendLine("        Versi = $Versi");
            teks.AppendLine("        Berhasil = $berhasil");
            teks.AppendLine("        Pesan = $pesan");
            teks.AppendLine("        JumlahBerkas = $jumlah");
            teks.AppendLine("        Waktu = (Get-Date).ToString('s')");
            teks.AppendLine("    }");
            teks.AppendLine("    try {");
            teks.AppendLine("        $folder = Split-Path -Parent $Hasil");
            teks.AppendLine("        if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }");
            teks.AppendLine("        ($objek | ConvertTo-Json) | Set-Content -LiteralPath $Hasil -Encoding UTF8");
            teks.AppendLine("    } catch { }");
            teks.AppendLine("}");
            teks.AppendLine(string.Empty);
            teks.AppendLine("try {");
            teks.AppendLine("    # Tunggu sampai aplikasi benar-benar tertutup (berkas .exe tidak terkunci).");
            teks.AppendLine("    if ($ProsesId -gt 0) {");
            teks.AppendLine("        for ($i = 0; $i -lt 600; $i++) {");
            teks.AppendLine("            $proses = Get-Process -Id $ProsesId -ErrorAction SilentlyContinue");
            teks.AppendLine("            if (-not $proses) { break }");
            teks.AppendLine("            Start-Sleep -Milliseconds 500");
            teks.AppendLine("        }");
            teks.AppendLine("        Start-Sleep -Milliseconds 800");
            teks.AppendLine("    }");
            teks.AppendLine(string.Empty);
            teks.AppendLine("    $daftarBerkas = @(Get-Content -LiteralPath $Daftar | Where-Object { $_ -and $_.Trim().Length -gt 0 })");
            teks.AppendLine("    $jumlah = 0");
            teks.AppendLine("    foreach ($relatif in $daftarBerkas) {");
            teks.AppendLine("        $asal = Join-Path $Sumber $relatif");
            teks.AppendLine("        $tujuan = Join-Path $Aplikasi $relatif");
            teks.AppendLine("        $folderTujuan = Split-Path -Parent $tujuan");
            teks.AppendLine("        if (-not (Test-Path $folderTujuan)) { New-Item -ItemType Directory -Force -Path $folderTujuan | Out-Null }");
            teks.AppendLine("        Copy-Item -LiteralPath $asal -Destination $tujuan -Force");
            teks.AppendLine("        $jumlah++");
            teks.AppendLine("        Add-Content -LiteralPath $log -Value (\"GANTI  \" + $relatif) -Encoding UTF8");
            teks.AppendLine("    }");
            teks.AppendLine(string.Empty);
            teks.AppendLine("    if (-not [string]::IsNullOrWhiteSpace($Hapus) -and (Test-Path -LiteralPath $Hapus)) {");
            teks.AppendLine("        foreach ($relatif in @(Get-Content -LiteralPath $Hapus | Where-Object { $_ -and $_.Trim().Length -gt 0 })) {");
            teks.AppendLine("            $lama = Join-Path $Aplikasi $relatif");
            teks.AppendLine("            if (Test-Path -LiteralPath $lama) {");
            teks.AppendLine("                Remove-Item -LiteralPath $lama -Force");
            teks.AppendLine("                Add-Content -LiteralPath $log -Value (\"HAPUS  \" + $relatif) -Encoding UTF8");
            teks.AppendLine("            }");
            teks.AppendLine("        }");
            teks.AppendLine("    }");
            teks.AppendLine(string.Empty);
            teks.AppendLine("    Tulis-Hasil $true (\"Pembaruan $Versi berhasil dipasang ($jumlah berkas).\") $jumlah");
            teks.AppendLine("} catch {");
            teks.AppendLine("    Add-Content -LiteralPath $log -Value (\"GAGAL  \" + $_.Exception.Message) -Encoding UTF8");
            teks.AppendLine("    Tulis-Hasil $false ($_.Exception.Message) 0");
            teks.AppendLine("} finally {");
            teks.AppendLine("    # Buka aplikasi lagi walaupun ada berkas yang gagal diganti.");
            teks.AppendLine("    if (-not [string]::IsNullOrWhiteSpace($Mulai) -and (Test-Path -LiteralPath $Mulai)) {");
            teks.AppendLine("        Start-Process -FilePath $Mulai");
            teks.AppendLine("    }");
            teks.AppendLine("    Start-Sleep -Seconds 2");
            teks.AppendLine("    $kerja = Split-Path -Parent $Daftar");
            teks.AppendLine("    try { Remove-Item -LiteralPath $kerja -Recurse -Force -ErrorAction SilentlyContinue } catch { }");
            teks.AppendLine("}");

            return teks.ToString();
        }

        private static string KunciArgumen(string nilai) => "\"" + (nilai ?? string.Empty).Replace("\"", "\\\"") + "\"";

        private static void TryHapusBerkas(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* pembersihan terbaik */ }
        }
    }
}
