using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Wilayah;

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

        /// <summary>Format nomor lengkap; null memakai jalur kompatibilitas Awalan.</summary>
        public string? Format { get; set; }

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

        /// <summary>Wilayah aktif dan status acuan regulasi yang tersedia untuk profil penomoran.</summary>
        public string CakupanAturanTampil
        {
            get
            {
                var kode = PenomoranOverrideStore.KodeDaerah;
                if (string.IsNullOrWhiteSpace(kode))
                {
                    return "Kabupaten/kota belum tersimpan. Pola yang tampil belum diverifikasi terhadap peraturan daerah.";
                }

                var wilayah = new WilayahDataStore().Ambil(kode.Trim());
                var namaWilayah = wilayah?.NamaLengkap ?? $"kode wilayah {kode}";
                if (string.Equals(kode.Trim(), "32.15", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Wilayah aktif: {namaWilayah}. Acuan tersedia: Perbup Karawang Nomor 74 Tahun 2020 tentang Tata Naskah Dinas Pemerintah Desa. Contoh lampiran mendukung kode 470 untuk beberapa surat keterangan. Penerapan 470 pada semua jenis dan deret bersama adalah kebijakan bawaan aplikasi, bukan ketentuan yang dinyatakan eksplisit oleh Perbup; tinjau dan konfirmasi sebelum menyimpan.";
                }

                if (string.Equals(kode.Trim(), "32.16", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Wilayah aktif: {namaWilayah}. Acuan tersedia: Perbup Bekasi Nomor 215 Tahun 2022 tentang Tata Naskah Dinas Pemerintah Desa (berstatus berlaku pada JDIH Kabupaten Bekasi). Contoh lampiran mendukung awalan 470 untuk surat keterangan umum, kelahiran, kematian, dan ahli waris. Kode Ds, urutan tiga digit, penerapan ke jenis lain, serta deret bersama belum ditetapkan secara eksplisit; tinjau dan konfirmasi sebelum menyimpan.";
                }

                return $"Wilayah aktif: {namaWilayah}. Acuan regulasi penomoran untuk wilayah ini belum tersedia di aplikasi. Pola yang tampil belum diverifikasi untuk wilayah ini; periksa Perbup/Perwali dan aturan sektoral setempat sebelum menyimpan.";
            }
        }

        /// <summary>Versi pola aktif dan jumlah snapshot lama pada profil desa ini.</summary>
        public string RiwayatVersiTampil
        {
            get
            {
                try
                {
                    var path = PenomoranOverrideStore.Path;
                    if (!File.Exists(path)) return "Pola bawaan aplikasi · versi 1; belum ada penyesuaian lokal.";
                    var folder = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "Backup");
                    int lama = Directory.Exists(folder)
                        ? Directory.GetFiles(folder, "penomoran-surat-*.bak").Length
                        : 0;
                    var tanggal = File.GetLastWriteTime(path).ToString("dd MMM yyyy HH:mm");
                    return $"Pola lokal versi {lama + 1} · disimpan {tanggal} · {lama} versi lama dicadangkan.";
                }
                catch
                {
                    return "Riwayat versi pola nomor belum dapat dibaca.";
                }
            }
        }

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

                    if (item.Format != null)
                    {
                        var format = item.Format.Trim();
                        if (format.Length == 0)
                        {
                            if (peta.Remove(entri.NamaJenis)) jumlahDikembalikan++;
                            continue;
                        }

                        if (!FormatValid(format, out var pesanFormat))
                            return (false, $"{entri.DisplayName}: {pesanFormat}");

                        if (string.Equals(format, entri.FormatBawaan, StringComparison.Ordinal))
                        {
                            if (peta.Remove(entri.NamaJenis)) jumlahDikembalikan++;
                            continue;
                        }

                        if (peta.TryGetValue(entri.NamaJenis, out var formatLama) &&
                            string.Equals(formatLama, format, StringComparison.Ordinal))
                            continue;

                        peta[entri.NamaJenis] = format;
                        jumlahDiubah++;
                        continue;
                    }

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

        /// <summary>Validasi pola nomor lengkap dengan token .NET yang dipakai aplikasi.</summary>
        public static bool FormatValid(string? format, out string pesan)
        {
            var bersih = (format ?? string.Empty).Trim();
            if (bersih.Length == 0)
            {
                pesan = "Pola nomor tidak boleh kosong.";
                return false;
            }
            if (bersih.Length > 120)
            {
                pesan = "Pola nomor maksimal 120 karakter.";
                return false;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(bersih, @"\{0(?::[^{}]+)?\}"))
            {
                pesan = "Pola wajib memuat {0} atau {0:D3} sebagai nomor urut.";
                return false;
            }
            var awalan = AmbilAwalan(bersih);
            if (!AwalanValid(awalan, out var pesanAwalan))
            {
                pesan = "Bagian sebelum garis miring pertama tidak valid: " + pesanAwalan;
                return false;
            }
            try
            {
                _ = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    bersih, 1, string.Empty, DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
                pesan = string.Empty;
                return true;
            }
            catch (FormatException)
            {
                pesan = "Pola nomor tidak valid. Gunakan {0:D3} untuk urutan dan {2:yyyy} untuk tahun.";
                return false;
            }
        }

        /// <summary>
        /// Baca nomor urut dari nomor terbit memakai pola aktif. Nomor dari pola
        /// lain, tahun lain, atau segmen lain tidak ikut deret tahun berjalan.
        /// </summary>
        public static int? UrutDariNomor(string? format, string? nomor, string tahun)
        {
            if (string.IsNullOrWhiteSpace(format) || string.IsNullOrWhiteSpace(nomor)) return null;
            try
            {
                var pola = format.Trim();
                pola = System.Text.RegularExpressions.Regex.Replace(pola, @"\{0(?::[^{}]+)?\}", "(?<urut>\\d+)");
                pola = System.Text.RegularExpressions.Regex.Replace(pola, @"\{1(?::[^{}]+)?\}", "[^/]+");
                pola = System.Text.RegularExpressions.Regex.Replace(pola, @"\{2(?::[^{}]+)?\}",
                    _ => System.Text.RegularExpressions.Regex.Escape(tahun));
                pola = System.Text.RegularExpressions.Regex.Escape(pola)
                    .Replace(System.Text.RegularExpressions.Regex.Escape("(?<urut>\\d+)"), "(?<urut>\\d+)")
                    .Replace(System.Text.RegularExpressions.Regex.Escape("[^/]+"), "[^/]+");
                var cocok = System.Text.RegularExpressions.Regex.Match(nomor.Trim(), "^" + pola + "$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                return cocok.Success && int.TryParse(cocok.Groups["urut"].Value, out var urut) ? urut : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Susun nomor surat dari sebuah pola. Argumennya persis sama dengan
        /// penomoran sungguhan: <c>{0}</c> = nomor urut, <c>{1}</c> = kode jenis
        /// surat, <c>{2}</c> = tahun (dikirim sebagai teks, sebab format
        /// <c>{2:yyyy}</c> hanya berlaku untuk argumen bertipe teks).
        ///
        /// Semua tempat yang menyusun nomor (repository saat menerbitkan surat,
        /// generator PDF saat nomor belum ada, pratinjau di pengaturan) memakai
        /// helper ini supaya bentuk nomornya tidak pernah berbeda antar jalur.
        /// </summary>
        /// <exception cref="FormatException">Pola nomornya tidak valid.</exception>
        public static string SusunNomor(string? format, int urut, string? kodeJenis, string? tahun)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Pola nomor tidak boleh kosong.", nameof(format));

            return string.Format(format, urut, kodeJenis ?? string.Empty, tahun ?? string.Empty);
        }

        /// <summary>
        /// Contoh nomor surat dari sebuah format, mis. "471/001/Ds/2026".
        /// Argumennya sama dengan penomoran sungguhan: urutan, kode jenis, dan
        /// tahun sebagai teks — supaya contoh yang tampil sama dengan nomor
        /// yang akan terbit.
        /// </summary>
        /// <param name="format">Pola nomor, mis. <c>470/{0:D3}/Ds/{2:yyyy}</c>.</param>
        /// <param name="urut">Nomor urut pada contoh.</param>
        /// <param name="tahun">Tahun pada contoh; null = tahun berjalan.</param>
        /// <param name="kodeJenis">
        /// Kode jenis surat untuk token <c>{1}</c>. Wajib diisi bila pola
        /// memakai <c>{1}</c>; dikosongkan berarti nomor terbit dengan slot
        /// kosong, sama seperti penomoran sungguhan.
        /// </param>
        public static string Contoh(string? format, int urut = 1, int? tahun = null, string? kodeJenis = null)
        {
            if (string.IsNullOrWhiteSpace(format)) return "-";

            try
            {
                return SusunNomor(format, urut, kodeJenis, (tahun ?? DateTime.Now.Year).ToString());
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

                var namaCadangan = $"penomoran-surat-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.bak";
                File.Copy(sumber, Path.Combine(folderCadangan, namaCadangan), overwrite: false);
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
