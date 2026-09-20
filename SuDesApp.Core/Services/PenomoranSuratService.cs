using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu jenis surat pada pengaturan penomoran: nama yang ditampilkan, awalan
    /// nomor yang sedang dipakai, dan awalan bawaan aplikasi untuk tombol
    /// "kembalikan bawaan".
    /// </summary>
    public class PenomoranSuratEntri
    {
        /// <summary>Nama jenis surat di konfigurasi, mis. <c>SKD_UMUM</c>.</summary>
        public string NamaJenis { get; set; } = string.Empty;

        /// <summary>Kode jenis surat di database, mis. <c>SKD</c>.</summary>
        public string KodeJenis { get; set; } = string.Empty;

        /// <summary>Nama jenis surat yang ramah dibaca, mis. "SKD Umum".</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Format penuh yang sedang dipakai, mis. <c>471/{0:D3}/Ds/{2:yyyy}</c>.</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>Format bawaan aplikasi (dari berkas konfigurasi bawaan).</summary>
        public string FormatBawaan { get; set; } = string.Empty;

        /// <summary>Awalan nomor yang sedang dipakai (bagian sebelum '/' pertama).</summary>
        public string Awalan { get; set; } = string.Empty;

        /// <summary>Awalan bawaan aplikasi.</summary>
        public string AwalanBawaan { get; set; } = string.Empty;

        /// <summary>Apakah surat ini ikut penomoran bersama (satu urutan dengan SKD dkk.).</summary>
        public bool IsSharedNumbering { get; set; }

        /// <summary>Apakah nilai saat ini berbeda dari bawaan aplikasi.</summary>
        public bool Disesuaikan => !string.Equals(Format, FormatBawaan, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Permintaan perubahan penomoran satu jenis surat.</summary>
    public class PerubahanPenomoran
    {
        public string NamaJenis { get; set; } = string.Empty;

        /// <summary>
        /// Awalan baru. Kosong/null = kembalikan ke bawaan aplikasi (penyesuaian
        /// dihapus, bukan ditulis sebagai nilai baru).
        /// </summary>
        public string? Awalan { get; set; }
    }

    /// <summary>
    /// Pengaturan penomoran surat: membaca, mengubah, dan menyimpan awalan nomor
    /// setiap jenis surat (mis. SKD 470 → 471) supaya kantor desa bisa membetulkan
    /// kode klasifikasi bawaan tanpa menunggu pembaruan aplikasi.
    ///
    /// Daftar jenis surat dan nilai bawaannya dibaca dari
    /// <c>Configuration/JenisSuratConfig.json</c>; perubahan pengguna disimpan
    /// terpisah di <see cref="PenomoranOverrideStore"/> agar tidak hilang saat
    /// aplikasi diperbarui. Setelah menyimpan, seluruh cache konfigurasi dan
    /// penomoran disegarkan sehingga nomor surat berikutnya langsung memakai
    /// awalan baru tanpa menutup aplikasi.
    /// </summary>
    public class PenomoranSuratService
    {
        /// <summary>Awalan bawaan bila berkas konfigurasi bawaan tidak memuat format.</summary>
        public const string AwalanCadangan = "470";

        private const int PanjangAwalanMaksimal = 16;

        private readonly string _configPath;
        private readonly IJenisSuratConfigLoader? _configLoader;
        private readonly AppConfig? _appConfig;
        private readonly ILogger<PenomoranSuratService> _logger;

        public PenomoranSuratService(
            ILogger<PenomoranSuratService> logger,
            IJenisSuratConfigLoader? configLoader = null,
            AppConfig? appConfig = null,
            string? configPath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configLoader = configLoader;
            _appConfig = appConfig;
            _configPath = configPath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "JenisSuratConfig.json");
        }

        /// <summary>Berkas konfigurasi bawaan (daftar jenis surat + format default).</summary>
        public string BerkasBawaan => _configPath;

        /// <summary>Berkas penyesuaian pengguna (hanya jenis yang diubah).</summary>
        public string BerkasPenyesuaian => PenomoranOverrideStore.Path;

        /// <summary>
        /// Daftar seluruh jenis surat beserta penomoran yang sedang berlaku —
        /// gabungan format bawaan dan penyesuaian pengguna.
        /// </summary>
        public List<PenomoranSuratEntri> MuatSemua()
        {
            var bawaan = BacaBerkasBawaan();
            var penyesuaian = PenomoranOverrideStore.Muat();

            foreach (var entri in bawaan)
            {
                if (penyesuaian.TryGetValue(entri.NamaJenis, out var formatPengguna) &&
                    !string.IsNullOrWhiteSpace(formatPengguna))
                {
                    entri.Format = formatPengguna;
                }

                entri.Awalan = AmbilAwalan(entri.Format);
                entri.AwalanBawaan = AmbilAwalan(entri.FormatBawaan);
            }

            return bawaan;
        }

        /// <summary>
        /// Simpan perubahan penomoran sekaligus. Kegagalan validasi membatalkan
        /// SELURUH perubahan (tidak ada yang tersimpan sebagian) dan berkas lama
        /// dicadangkan lebih dulu.
        /// </summary>
        public async Task<(bool Ok, string Pesan)> SimpanAsync(IEnumerable<PerubahanPenomoran> perubahan)
        {
            var permintaan = (perubahan ?? Enumerable.Empty<PerubahanPenomoran>())
                .Where(p => !string.IsNullOrWhiteSpace(p.NamaJenis))
                .ToList();

            if (permintaan.Count == 0)
                return (false, "Tidak ada perubahan penomoran yang perlu disimpan.");

            var bawaan = BacaBerkasBawaan();
            var peta = PenomoranOverrideStore.Muat();

            try
            {
                int jumlahDiubah = 0;
                int jumlahDikembalikan = 0;

                foreach (var item in permintaan)
                {
                    var entri = bawaan.FirstOrDefault(
                        e => string.Equals(e.NamaJenis, item.NamaJenis, StringComparison.OrdinalIgnoreCase));

                    if (entri == null)
                        return (false, $"Jenis surat '{item.NamaJenis}' tidak dikenal.");

                    var awalan = (item.Awalan ?? string.Empty).Trim();

                    // Awalan kosong = kembalikan ke bawaan aplikasi.
                    if (awalan.Length == 0)
                    {
                        if (peta.Remove(entri.NamaJenis)) jumlahDikembalikan++;
                        continue;
                    }

                    if (!AwalanValid(awalan, out var pesan))
                        return (false, $"{entri.DisplayName}: {pesan}");

                    var formatBaru = BangunFormat(entri.FormatBawaan, awalan);

                    if (string.Equals(formatBaru, entri.FormatBawaan, StringComparison.OrdinalIgnoreCase))
                    {
                        // Sama dengan bawaan → tidak perlu penyesuaian.
                        if (peta.Remove(entri.NamaJenis)) jumlahDikembalikan++;
                        continue;
                    }

                    if (peta.TryGetValue(entri.NamaJenis, out var lama) &&
                        string.Equals(lama, formatBaru, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // sudah sama, tidak ada perubahan
                    }

                    peta[entri.NamaJenis] = formatBaru;
                    jumlahDiubah++;
                }

                // Penyesuaian untuk jenis surat yang sudah tidak ada (mis. dihapus di
                // versi baru) dibuang agar berkas tidak menumpuk sisa lama.
                foreach (var namaJenis in peta.Keys.ToList())
                {
                    if (!bawaan.Any(e => string.Equals(e.NamaJenis, namaJenis, StringComparison.OrdinalIgnoreCase)))
                        peta.Remove(namaJenis);
                }

                if (jumlahDiubah == 0 && jumlahDikembalikan == 0)
                    return (false, "Tidak ada perubahan penomoran yang perlu disimpan.");

                CadangkanBerkasPenyesuaian();
                PenomoranOverrideStore.Simpan(peta);

                var pesanSegarkan = await SegarkanKonfigurasiAsync();

                var ringkasan = new List<string>();
                if (jumlahDiubah > 0) ringkasan.Add($"{jumlahDiubah} awalan diubah");
                if (jumlahDikembalikan > 0) ringkasan.Add($"{jumlahDikembalikan} dikembalikan ke bawaan");

                return (true,
                    $"Penomoran surat disimpan ({string.Join(", ", ringkasan)}). {pesanSegarkan}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan penomoran surat");
                return (false, "Gagal menyimpan penomoran surat: " + ex.Message);
            }
        }

        /// <summary>
        /// Segarkan seluruh cache penomoran (berkas konfigurasi, nilai di memori
        /// AppConfig, dan cache repository) agar nomor surat berikutnya langsung
        /// memakai awalan baru.
        /// </summary>
        private async Task<string> SegarkanKonfigurasiAsync()
        {
            try
            {
                _appConfig?.ReloadSuratKindsFromConfig();

                // Loader memegang cache format per kode jenis surat — sumber yang dipakai
                // IUnitOfWork.JenisSuratRepository.GenerateNomorSuratAsync saat membuat
                // nomor. Membersihkannya membuat awalan baru langsung berlaku.
                if (_configLoader != null) await _configLoader.RefreshConfigAsync();

                return "Nomor surat berikutnya sudah memakai awalan baru.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Penomoran tersimpan, tetapi penyegaran cache konfigurasi gagal");
                return "Nomor surat berikutnya memakai awalan baru setelah aplikasi dibuka ulang.";
            }
        }

        /// <summary>Format penuh hasil awalan baru, mempertahankan bentuk bawaan.</summary>
        public static string BangunFormat(string formatBawaan, string awalan)
        {
            var bentuk = formatBawaan ?? string.Empty;
            if (bentuk.Length == 0) bentuk = AwalanCadangan + "/{0:D3}/Ds/{2:yyyy}";

            int posisiGarisMiring = bentuk.IndexOf('/');
            var ekor = posisiGarisMiring >= 0 ? bentuk.Substring(posisiGarisMiring) : "/{0:D3}/Ds/{2:yyyy}";

            return awalan.Trim() + ekor;
        }

        /// <summary>Ambil awalan nomor (bagian sebelum '/' pertama) dari sebuah format.</summary>
        public static string AmbilAwalan(string? format)
        {
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;

            int posisiGarisMiring = format.IndexOf('/');
            var awalan = posisiGarisMiring > 0 ? format.Substring(0, posisiGarisMiring) : format;
            return awalan.Trim();
        }

        /// <summary>
        /// Aturan awalan nomor: hanya huruf, angka, titik, tanda hubung, dan garis
        /// bawah. Tanda '/' sengaja ditolak karena nomor surat diurai berdasarkan
        /// tanda itu (segmen pertama = kode klasifikasi, segmen kedua = nomor urut).
        /// </summary>
        public static bool AwalanValid(string? awalan, out string pesan)
        {
            var bersih = (awalan ?? string.Empty).Trim();

            if (bersih.Length == 0)
            {
                pesan = "Awalan nomor tidak boleh kosong.";
                return false;
            }

            if (bersih.Length > PanjangAwalanMaksimal)
            {
                pesan = $"Awalan nomor maksimal {PanjangAwalanMaksimal} karakter.";
                return false;
            }

            foreach (var c in bersih)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
                {
                    pesan = $"Awalan nomor tidak boleh memuat '{c}'. Pakai huruf, angka, titik, tanda hubung, atau garis bawah (contoh: 470 atau 471.1).";
                    return false;
                }
            }

            pesan = string.Empty;
            return true;
        }

        /// <summary>Contoh nomor surat dari sebuah format, mis. "471/001/Ds/2026".</summary>
        public static string Contoh(string? format, int urut = 1, int? tahun = null)
        {
            if (string.IsNullOrWhiteSpace(format)) return "-";

            try
            {
                // Tahun dikirim sebagai teks, sama seperti penomoran sungguhan
                // (format {2:yyyy} hanya berlaku untuk argumen bertipe teks).
                return string.Format(format, urut, string.Empty, (tahun ?? DateTime.Now.Year).ToString());
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        /// Cari awalan yang berpotensi membuat nomor kembar: dua jenis surat dengan
        /// awalan sama yang TIDAK berada dalam satu grup penomoran bersama (nomor
        /// urutnya dihitung sendiri-sendiri sehingga bisa bertabrakan).
        /// </summary>
        public static List<string> CariBentrok(IEnumerable<PenomoranSuratEntri> entri)
        {
            var hasil = new List<string>();

            var kelompok = (entri ?? Enumerable.Empty<PenomoranSuratEntri>())
                .Where(e => !string.IsNullOrWhiteSpace(e.Awalan))
                .GroupBy(e => e.Awalan, StringComparer.OrdinalIgnoreCase);

            foreach (var grup in kelompok)
            {
                if (grup.Count() < 2) continue;

                // Setiap jenis yang TIDAK ikut penomoran bersama punya urutan sendiri,
                // sedangkan seluruh jenis yang ikut penomoran bersama berbagi satu
                // urutan. Bila sebuah awalan dipakai oleh lebih dari satu urutan,
                // nomornya bisa kembar.
                int jumlahUrutan = grup.Count(e => !e.IsSharedNumbering)
                                   + (grup.Any(e => e.IsSharedNumbering) ? 1 : 0);

                if (jumlahUrutan < 2) continue;

                var nama = string.Join(", ", grup.Select(e => e.DisplayName));
                hasil.Add($"Awalan '{grup.Key}' dipakai {nama} yang nomor urutnya dihitung sendiri-sendiri " +
                          "— nomor bisa kembar. Bedakan awalannya atau samakan penomorannya.");
            }

            return hasil.Distinct().ToList();
        }

        /// <summary>Cadangkan berkas penyesuaian lama sebelum ditimpa.</summary>
        private void CadangkanBerkasPenyesuaian()
        {
            try
            {
                var sumber = PenomoranOverrideStore.Path;
                if (!File.Exists(sumber)) return;

                var folder = Path.GetDirectoryName(sumber);
                if (string.IsNullOrEmpty(folder)) return;

                var folderCadangan = Path.Combine(folder, "Backup");
                if (!Directory.Exists(folderCadangan)) Directory.CreateDirectory(folderCadangan);

                var namaCadangan = $"penomoran-surat-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(sumber, Path.Combine(folderCadangan, namaCadangan), overwrite: true);
                _logger.LogInformation("Cadangan penomoran surat dibuat: {Berkas}", namaCadangan);
            }
            catch (Exception ex)
            {
                // Cadangan bersifat pencegahan; kegagalannya tidak membatalkan penyimpanan.
                _logger.LogWarning(ex, "Gagal membuat cadangan berkas penomoran surat");
            }
        }

        /// <summary>Baca daftar jenis surat + format bawaan dari berkas konfigurasi.</summary>
        private List<PenomoranSuratEntri> BacaBerkasBawaan()
        {
            var hasil = new List<PenomoranSuratEntri>();

            try
            {
                if (!File.Exists(_configPath))
                {
                    _logger.LogWarning("Berkas konfigurasi jenis surat tidak ditemukan: {Path}", _configPath);
                    return hasil;
                }

                var entri = JsonSerializer.Deserialize<List<EntriKonfigurasi>>(
                    File.ReadAllText(_configPath),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                foreach (var e in entri ?? new List<EntriKonfigurasi>())
                {
                    if (string.IsNullOrWhiteSpace(e.NamaJenis) || string.IsNullOrWhiteSpace(e.NomorFormat))
                        continue;

                    hasil.Add(new PenomoranSuratEntri
                    {
                        NamaJenis = e.NamaJenis,
                        KodeJenis = e.KodeJenis,
                        DisplayName = string.IsNullOrWhiteSpace(e.DisplayName) ? e.NamaJenis : e.DisplayName,
                        Format = e.NomorFormat,
                        FormatBawaan = e.NomorFormat,
                        IsSharedNumbering = e.IsSharedNumbering
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca berkas konfigurasi jenis surat {Path}", _configPath);
            }

            return hasil;
        }

        /// <summary>Subset properti JenisSuratConfig.json yang dibutuhkan pengaturan ini.</summary>
        private sealed class EntriKonfigurasi
        {
            [JsonPropertyName("NamaJenis")]
            public string NamaJenis { get; set; } = string.Empty;

            [JsonPropertyName("KodeJenis")]
            public string KodeJenis { get; set; } = string.Empty;

            [JsonPropertyName("DisplayName")]
            public string DisplayName { get; set; } = string.Empty;

            [JsonPropertyName("NomorFormat")]
            public string NomorFormat { get; set; } = string.Empty;

            [JsonPropertyName("IsSharedNumbering")]
            public bool IsSharedNumbering { get; set; }
        }
    }
}
