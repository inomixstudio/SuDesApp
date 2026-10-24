using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Services
{
    /// <summary>
    /// Hasil satu langkah alur persetujuan surat. Kegagalan aturan (mis. surat
    /// belum berstatus Aktif) dikembalikan sebagai pesan berbahasa Indonesia,
    /// bukan exception, supaya pemanggil dapat menampilkannya apa adanya.
    /// </summary>
    public sealed class HasilPersetujuanSurat
    {
        public bool Berhasil { get; init; }

        /// <summary>Pesan untuk pengguna (alasan gagal, atau ringkasan perubahan).</summary>
        public string Pesan { get; init; } = string.Empty;

        /// <summary>Status persetujuan sesudah perubahan (null bila alur dikosongkan).</summary>
        public string? Status { get; init; }

        public static HasilPersetujuanSurat Sukses(string pesan, string? status = null)
            => new() { Berhasil = true, Pesan = pesan, Status = status };

        public static HasilPersetujuanSurat Gagal(string pesan)
            => new() { Berhasil = false, Pesan = pesan };
    }

    /// <summary>
    /// Alur persetujuan surat — <b>opsional per surat</b>.
    ///
    /// Surat yang dibuat seperti biasa (tanpa memanggil layanan ini) tidak punya
    /// status persetujuan sama sekali dan tetap bisa dicetak, diarsipkan, dan
    /// diverifikasi seperti sebelumnya; jadi kebiasaan operator tidak berubah.
    /// Alur hanya berjalan bila operator secara sadar memilih
    /// "Ajukan Verifikasi" pada satu surat:
    ///
    ///   (kosong) ──ajukan──► DIAJUKAN ──verifikasi──► DIVERIFIKASI ──tanda tangan──► TERBIT
    ///                            │                        │
    ///                            └────────tolak───────────┴──────────► DITOLAK
    ///
    /// Surat yang belum selesai alurnya tetap bisa dicetak, tetapi kakinya diberi
    /// tanda jelas lewat <see cref="StatusPersetujuanSurat.LabelCetak"/> supaya
    /// kertas setengah jadi tidak beredar sebagai surat resmi. Karena itu nomor
    /// surat dan kode verifikasi tidak pernah ditahan oleh alur ini.
    /// </summary>
    public interface IPersetujuanSuratService
    {
        /// <summary>Ajukan satu surat (status Aktif) ke alur persetujuan.</summary>
        Task<HasilPersetujuanSurat> AjukanVerifikasiAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default);

        /// <summary>Catat pemeriksaan (Sekdes/pejabat): DIAJUKAN → DIVERIFIKASI.</summary>
        Task<HasilPersetujuanSurat> VerifikasiAsync(int idSurat, string? catatan = null, string? oleh = null, CancellationToken cancellationToken = default);

        /// <summary>Tanda tangani surat (Kades): DIVERIFIKASI → TERBIT.</summary>
        Task<HasilPersetujuanSurat> TandatanganiAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default);

        /// <summary>Tolak surat beserta alasannya: DIAJUKAN/DIVERIFIKASI → DITOLAK.</summary>
        Task<HasilPersetujuanSurat> TolakAsync(int idSurat, string catatan, string? oleh = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Kembalikan surat ke keadaan "tanpa persetujuan" — jalan keluar bila
        /// alur tidak diperlukan. Hanya boleh sebelum surat terbit/ditolak.
        /// </summary>
        Task<HasilPersetujuanSurat> BatalkanPengajuanAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default);

        /// <summary>Lampirkan berkas scan surat yang sudah ditandatangani (PDF/gambar).</summary>
        Task<HasilPersetujuanSurat> LampirkanScanAsync(int idSurat, string sumberBerkas, CancellationToken cancellationToken = default);

        /// <summary>Lepas berkas scan surat (berkas fisik ikut dihapus).</summary>
        Task<HasilPersetujuanSurat> LepasScanAsync(int idSurat, CancellationToken cancellationToken = default);

        /// <summary>
        /// Catat satu cetakan surat dan kembalikan jumlah cetakan sesudahnya.
        /// Pencatatan GAGAL tidak boleh menghalangi pencetakan — pemanggil cukup
        /// mengabaikan kegagalannya.
        /// </summary>
        Task<int> CatatCetakAsync(int idSurat, CancellationToken cancellationToken = default);

        /// <summary>Path lengkap berkas scan tersimpan, atau null bila tidak ada.</summary>
        string? ResolveScanFullPath(string? namaBerkas);

        /// <summary>Folder penyimpanan berkas scan surat (SuratScan).</summary>
        string FolderScan { get; }
    }

    public class PersetujuanSuratService : IPersetujuanSuratService
    {
        /// <summary>
        /// Gerbang proses: Microsoft.Data.Sqlite tidak aman untuk perintah
        /// bersamaan pada satu koneksi, jadi seluruh perubahan alur/cetakan dari
        /// layanan ini dijalankan berurutan.
        /// </summary>
        private static readonly SemaphoreSlim _gerbang = new(1, 1);

        /// <summary>Format berkas scan yang diterima (PDF atau gambar hasil pindai/foto).</summary>
        public static readonly IReadOnlyList<string> EkstensiScanDidukung = new[] { ".pdf", ".jpg", ".jpeg", ".png" };

        private readonly ISuratRepository _surat;
        private readonly IVerifikasiSuratService _verifikasiSurat;
        private readonly string _folderScan;
        private readonly ILogger<PersetujuanSuratService> _logger;
        private readonly ActivityLogService? _activityLog;

        /// <param name="verifikasiSurat">
        /// Sumber tunggal kode verifikasi surat. Kosong = dibuat sendiri di sini,
        /// supaya layanan tetap bisa dipakai tanpa wadah DI lengkap (mis. pengujian).
        /// </param>
        public PersetujuanSuratService(
            ISuratRepository suratRepository,
            AppConfig appConfig,
            ILogger<PersetujuanSuratService>? logger = null,
            ActivityLogService? activityLog = null,
            IVerifikasiSuratService? verifikasiSurat = null,
            FileService? fileService = null)
        {
            _surat = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            if (appConfig == null) throw new ArgumentNullException(nameof(appConfig));

            _verifikasiSurat = verifikasiSurat ?? new VerifikasiSuratService(suratRepository);
            var dasarFolder = Path.Combine(appConfig.TemplateFolder, "SuratScan");
            _folderScan = fileService?.SanitizePath(dasarFolder) ?? dasarFolder;
            _logger = logger ?? NullLogger<PersetujuanSuratService>.Instance;
            _activityLog = activityLog;

            if (!Directory.Exists(_folderScan)) Directory.CreateDirectory(_folderScan);
        }

        public string FolderScan => _folderScan;

        // =====================================================================
        // Langkah alur
        // =====================================================================

        public async Task<HasilPersetujuanSurat> AjukanVerifikasiAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default)
        {
            // Mengajukan surat ke alur = tindakan pembuat surat, bukan pemeriksa.
            SessionContext.Wajib(IzinAplikasi.BuatSurat);

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                if (!Aktif(surat))
                    return HasilPersetujuanSurat.Gagal("Hanya surat berstatus Aktif yang bisa diajukan verifikasi. Aktifkan suratnya lebih dulu (Ubah Status ke Aktif ✓).");

                var status = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan);
                if (StatusPersetujuanSurat.MasihDalamAlur(status))
                    return HasilPersetujuanSurat.Gagal($"Surat ini sudah ada di alur persetujuan: {StatusPersetujuanSurat.Tampilan(status)}.");

                if (status == StatusPersetujuanSurat.Terbit)
                    return HasilPersetujuanSurat.Gagal("Surat ini sudah ditandatangani, jadi tidak perlu diajukan lagi.");

                // DITOLAK boleh diajukan ulang setelah diperbaiki; catatan lama dibuang
                // supaya alasan penolakan yang sudah tidak berlaku tidak tertinggal.
                surat.StatusPersetujuan = StatusPersetujuanSurat.Diajukan;
                surat.VerifikasiOleh = null;
                surat.VerifikasiPada = null;
                surat.DitandatanganiOleh = null;
                surat.DitandatanganiPada = null;
                surat.CatatanPersetujuan = null;

                return await SimpanAsync(surat, "Ajukan verifikasi",
                    "Menunggu verifikasi Sekdes/pejabat yang berwenang.", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengajukan verifikasi surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Pengajuan tidak tersimpan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<HasilPersetujuanSurat> VerifikasiAsync(int idSurat, string? catatan = null, string? oleh = null, CancellationToken cancellationToken = default)
        {
            SessionContext.Wajib(IzinAplikasi.TandaTanganSurat);

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                var status = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan);
                if (status != StatusPersetujuanSurat.Diajukan)
                    return HasilPersetujuanSurat.Gagal($"Verifikasi hanya berlaku untuk surat yang menunggu verifikasi (sekarang: {StatusPersetujuanSurat.Tampilan(status)}).");

                surat.StatusPersetujuan = StatusPersetujuanSurat.Diverifikasi;
                surat.VerifikasiOleh = NamaPengguna(oleh);
                surat.VerifikasiPada = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(catatan))
                    surat.CatatanPersetujuan = catatan.Trim();

                return await SimpanAsync(surat, "Verifikasi surat",
                    StatusPersetujuanSurat.Tampilan(surat.StatusPersetujuan), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memverifikasi surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Verifikasi tidak tersimpan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<HasilPersetujuanSurat> TandatanganiAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default)
        {
            SessionContext.Wajib(IzinAplikasi.TandaTanganSurat);

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                var status = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan);
                if (status != StatusPersetujuanSurat.Diverifikasi)
                    return HasilPersetujuanSurat.Gagal($"Surat harus sudah diverifikasi sebelum ditandatangani (sekarang: {StatusPersetujuanSurat.Tampilan(status)}).");

                surat.StatusPersetujuan = StatusPersetujuanSurat.Terbit;
                surat.DitandatanganiOleh = NamaPengguna(oleh);
                surat.DitandatanganiPada = DateTime.Now;

                var hasil = await SimpanAsync(surat, "Tanda tangan surat",
                    "Surat terbit setelah ditandatangani.", cancellationToken).ConfigureAwait(false);
                if (!hasil.Berhasil)
                    return hasil;

                // Surat resmi wajib membawa kode + cap verifikasi; alur persetujuan
                // sepenuhnya opsional, jadi kode disusulkan di sini bila belum ada
                // (surat yang bukan lewat alur sudah mendapatkannya saat disimpan).
                try
                {
                    var kode = await _verifikasiSurat.PastikanTerdaftarAsync(surat, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Kode verifikasi surat #{Id} siap setelah tanda tangan: {Kode}", idSurat, kode);
                }
                catch (Exception ex)
                {
                    // Tanda tangan sudah tersimpan; kode bisa disusulkan lagi dari
                    // menu Verifikasi Surat, jadi kegagalan ini tidak menggagalkan alur.
                    _logger.LogWarning(ex, "Kode verifikasi surat #{Id} belum bisa disusulkan.", idSurat);
                }

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menandatangani surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Tanda tangan tidak tersimpan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<HasilPersetujuanSurat> TolakAsync(int idSurat, string catatan, string? oleh = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(catatan))
                return HasilPersetujuanSurat.Gagal("Alasan penolakan wajib diisi supaya pembuat surat tahu apa yang harus diperbaiki.");

            SessionContext.Wajib(IzinAplikasi.TandaTanganSurat);

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                var status = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan);
                if (!StatusPersetujuanSurat.MasihDalamAlur(status))
                    return HasilPersetujuanSurat.Gagal($"Hanya surat yang masih dalam alur yang bisa ditolak (sekarang: {StatusPersetujuanSurat.Tampilan(status)}).");

                surat.StatusPersetujuan = StatusPersetujuanSurat.Ditolak;
                surat.CatatanPersetujuan = catatan.Trim();
                surat.DitandatanganiOleh = null;
                surat.DitandatanganiPada = null;

                return await SimpanAsync(surat, "Tolak surat", surat.CatatanPersetujuan, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menolak surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Penolakan tidak tersimpan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<HasilPersetujuanSurat> BatalkanPengajuanAsync(int idSurat, string? oleh = null, CancellationToken cancellationToken = default)
        {
            // Pembatalan mengembalikan surat ke pembuatnya — tindakan pembuat surat.
            SessionContext.Wajib(IzinAplikasi.BuatSurat);

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                var status = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan);
                if (status == null)
                    return HasilPersetujuanSurat.Gagal("Surat ini memang tidak memakai alur persetujuan.");

                if (!StatusPersetujuanSurat.MasihDalamAlur(status))
                    return HasilPersetujuanSurat.Gagal($"Alur yang sudah selesai (terbit/ditolak) tidak bisa dikembalikan agar jejaknya tetap ada (sekarang: {StatusPersetujuanSurat.Tampilan(status)}).");

                surat.StatusPersetujuan = null;
                surat.VerifikasiOleh = null;
                surat.VerifikasiPada = null;
                surat.DitandatanganiOleh = null;
                surat.DitandatanganiPada = null;
                surat.CatatanPersetujuan = null;

                return await SimpanAsync(surat, "Batalkan pengajuan",
                    "Surat kembali tanpa alur persetujuan (kebiasaan biasa).", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membatalkan pengajuan surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Pengajuan tidak bisa dibatalkan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        // =====================================================================
        // Berkas scan surat
        // =====================================================================

        public async Task<HasilPersetujuanSurat> LampirkanScanAsync(int idSurat, string sumberBerkas, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sumberBerkas) || !File.Exists(sumberBerkas))
                return HasilPersetujuanSurat.Gagal("Berkas scan tidak ditemukan. Pilih ulang berkasnya.");

            var ext = Path.GetExtension(sumberBerkas).ToLowerInvariant();
            if (!EkstensiScanDidukung.Contains(ext))
                return HasilPersetujuanSurat.Gagal("Berkas scan harus PDF atau gambar (.pdf, .jpg, .jpeg, .png).");

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                if (string.Equals(surat.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    return HasilPersetujuanSurat.Gagal("Surat yang sudah dibatalkan tidak perlu dilampiri scan.");

                if (!Directory.Exists(_folderScan)) Directory.CreateDirectory(_folderScan);

                var namaBaru = NamaBerkasScan(surat.ID_Surat, surat.NomorSurat, ext);
                var tujuan = Path.Combine(_folderScan, namaBaru);
                var lama = ResolveScanFullPath(surat.FileScanSurat);

                File.Copy(sumberBerkas, tujuan, overwrite: true);

                if (surat.FileScanSurat != null &&
                    !string.Equals(surat.FileScanSurat, namaBaru, StringComparison.OrdinalIgnoreCase) &&
                    lama != null)
                {
                    try { File.Delete(lama); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Berkas scan lama surat #{Id} tidak terhapus.", idSurat); }
                }

                if (!await _surat.SimpanScanSuratAsync(idSurat, namaBaru, cancellationToken).ConfigureAwait(false))
                    return HasilPersetujuanSurat.Gagal("Berkas sudah disalin, tetapi catatannya tidak tersimpan. Coba lampirkan lagi.");

                surat.FileScanSurat = namaBaru;
                _activityLog?.Log(surat.NamaJenis ?? "Surat", surat.NomorSurat ?? $"#{idSurat}", "Lampirkan scan",
                    Path.GetFileName(sumberBerkas));
                _logger.LogInformation("Scan surat #{Id} disimpan sebagai {Berkas}.", idSurat, namaBaru);

                return HasilPersetujuanSurat.Sukses($"Scan surat tersimpan sebagai {namaBaru}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal melampirkan scan surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Scan gagal dilampirkan: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<HasilPersetujuanSurat> LepasScanAsync(int idSurat, CancellationToken cancellationToken = default)
        {
            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var surat = await _surat.GetByIdAsync(idSurat, null, cancellationToken).ConfigureAwait(false);
                if (surat == null)
                    return HasilPersetujuanSurat.Gagal($"Surat #{idSurat} tidak ditemukan di register.");

                var path = ResolveScanFullPath(surat.FileScanSurat);
                if (!await _surat.SimpanScanSuratAsync(idSurat, null, cancellationToken).ConfigureAwait(false))
                    return HasilPersetujuanSurat.Gagal("Lampiran scan tidak bisa dilepas dari catatan surat.");

                if (path != null)
                {
                    try { File.Delete(path); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Berkas scan surat #{Id} tidak terhapus.", idSurat); }
                }

                _activityLog?.Log(surat.NamaJenis ?? "Surat", surat.NomorSurat ?? $"#{idSurat}", "Lepas scan");
                return HasilPersetujuanSurat.Sukses("Lampiran scan surat dilepas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal melepas scan surat #{Id}", idSurat);
                return HasilPersetujuanSurat.Gagal($"Lampiran scan tidak bisa dilepas: {ex.Message}");
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public async Task<int> CatatCetakAsync(int idSurat, CancellationToken cancellationToken = default)
        {
            if (idSurat <= 0) return 0;

            await _gerbang.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _surat.CatatCetakAsync(idSurat, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Pencatatan cetakan hanya pelengkap label SALINAN: kegagalannya
                // tidak boleh menggagalkan atau membatalkan pencetakan surat.
                _logger.LogWarning(ex, "Jumlah cetak surat #{Id} tidak tercatat.", idSurat);
                return 0;
            }
            finally
            {
                _gerbang.Release();
            }
        }

        public string? ResolveScanFullPath(string? namaBerkas)
        {
            if (string.IsNullOrWhiteSpace(namaBerkas)) return null;
            // Jaga agar tidak lolos direktori (nama berkas saja, tanpa jalur).
            if (Path.IsPathRooted(namaBerkas) || namaBerkas.Contains("..")) return null;

            var full = Path.Combine(_folderScan, namaBerkas);
            return File.Exists(full) ? full : null;
        }

        // =====================================================================
        // Pembantu
        // =====================================================================

        /// <summary>Nama berkas scan baku satu surat (jenis berkas mengikuti sumbernya).</summary>
        public static string NamaBerkasScan(int idSurat, string? nomorSurat, string ekstensi)
        {
            var ext = ekstensi.StartsWith('.') ? ekstensi.ToLowerInvariant() : "." + ekstensi.ToLowerInvariant();

            // Nomor yang belum nyata (kosong atau masih berpola "XXX/{0:D3}/…") tidak
            // dipakai sebagai nama berkas; id surat sudah cukup unik.
            var nomor = (nomorSurat ?? string.Empty).Trim();
            if (nomor.Length == 0 || nomor.Contains('{'))
                return $"Surat_{idSurat}{ext}";

            var bersih = string.Join("_", nomor.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return $"Surat_{idSurat}_{bersih}{ext}";
        }

        /// <summary>Nama pengguna yang tercatat pada jejak persetujuan.</summary>
        private static string NamaPengguna(string? oleh) =>
            string.IsNullOrWhiteSpace(oleh) ? SessionContext.NamaPanggil : oleh.Trim();

        private static bool Aktif(SuratData surat) =>
            string.Equals(surat.Status, "Active", StringComparison.OrdinalIgnoreCase);

        /// <summary>Simpan keadaan alur + catat riwayat aktivitas.</summary>
        private async Task<HasilPersetujuanSurat> SimpanAsync(SuratData surat, string aksi, string pesan, CancellationToken cancellationToken)
        {
            if (!await _surat.SimpanPersetujuanAsync(surat, cancellationToken).ConfigureAwait(false))
                return HasilPersetujuanSurat.Gagal("Perubahan alur persetujuan tidak tersimpan di database. Coba ulangi sebentar lagi.");

            _activityLog?.Log(surat.NamaJenis ?? "Surat", surat.NomorSurat ?? $"#{surat.ID_Surat}", aksi,
                StatusPersetujuanSurat.Tampilan(surat.StatusPersetujuan));

            return HasilPersetujuanSurat.Sukses(pesan, StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan));
        }
    }
}
