using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuDesApp.Utilities;

namespace SuDesApp
{
    public class AppConfig
    {
        private readonly ILogger<AppConfig> _logger;

        // Konfigurasi koneksi dan direktori aplikasi
        public string DatabaseConnectionString { get; set; }

        /// <summary>
        /// Lokasi berkas database yang sedang dipakai (absolut). Bila pengguna memilih
        /// lokasi lain lewat Pengaturan Aplikasi → Database, nilainya adalah lokasi
        /// pilihan itu; selain itu sama dengan <see cref="DatabasePathBawaan"/>.
        /// </summary>
        public string DatabasePath { get; private set; } = string.Empty;

        /// <summary>
        /// Lokasi database bawaan dari appsettings.json (desa.db di folder aplikasi).
        /// Dipakai halaman pengaturan untuk menawarkan "kembali ke bawaan".
        /// </summary>
        public string DatabasePathBawaan { get; private set; } = string.Empty;

        /// <summary>True bila lokasi database berasal dari pilihan pengguna, bukan appsettings.</summary>
        public bool DatabaseKustom { get; private set; }

        public string TemplateFolder { get; set; }
        public string TempPdfFolder { get; set; }
        public string PdfOutputPath { get; set; }
        public string FontFolder { get; set; }
        public string LogoPath { get; set; }
        public string UpdateCheckUrl { get; set; }
        public string? GithubRepo { get; set; }
        public string? GithubPersonalAccessToken { get; set; }

        // Daftar jenis surat & format nomor: SATU SUMBER KEBENARAN = Configuration/JenisSuratConfig.json
        // (dulu duplikat di appsettings.json bagian suratNumberFormats/templateNames — sudah dihapus).
        public Dictionary<string, string> SuratNumberFormats { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> TemplateNames { get; private set; } = new();

        // Model baris JenisSuratConfig.json (subset properti yang dibutuhkan AppConfig)
        private sealed class JenisSuratEntry
        {
            [JsonPropertyName("NamaJenis")]
            public string NamaJenis { get; set; } = string.Empty;

            [JsonPropertyName("NomorFormat")]
            public string NomorFormat { get; set; } = string.Empty;

            [JsonPropertyName("IsSharedNumbering")]
            public bool IsSharedNumbering { get; set; }
        }

        // Nama jenis surat (UPPER) yang memakai penomoran bersama (IsSharedNumbering=true).
        private HashSet<string> _sharedNumberingNames = new(StringComparer.OrdinalIgnoreCase);

        // Muat daftar jenis surat + format nomor dari Configuration/JenisSuratConfig.json.
        // Gagal baca/parse tidak mematikan aplikasi: dicatat sebagai warning dan dibiarkan kosong
        // (pemanggil GetSuratNumberFormat akan melempar pesan jelas bila format dibutuhkan).
        private void LoadSuratKindsFromConfigFile()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "JenisSuratConfig.json");
                if (!File.Exists(path))
                {
                    _logger.LogWarning("JenisSuratConfig.json tidak ditemukan: {Path}", path);
                    return;
                }

                var entries = JsonSerializer.Deserialize<List<JenisSuratEntry>>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

                if (entries == null || entries.Count == 0)
                {
                    _logger.LogWarning("JenisSuratConfig.json kosong: {Path}", path);
                    return;
                }

                var formats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var names = new List<string>();
                var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries)
                {
                    if (string.IsNullOrWhiteSpace(e.NamaJenis) || string.IsNullOrWhiteSpace(e.NomorFormat))
                    {
                        _logger.LogWarning("Entri JenisSuratConfig tidak lengkap (NamaJenis/NomorFormat kosong), dilewati.");
                        continue;
                    }
                    formats[e.NamaJenis] = e.NomorFormat;
                    names.Add(e.NamaJenis.ToUpperInvariant());
                    if (e.IsSharedNumbering) shared.Add(e.NamaJenis.ToUpperInvariant());
                }

                // Penyesuaian penomoran yang diubah pengguna di halaman Pengaturan
                // Aplikasi menempel di atas format bawaan berkas, supaya seluruh
                // pemakai GetSuratNumberFormat langsung memakai awalan yang berlaku.
                foreach (var penyesuaian in SuDesApp.Configuration.PenomoranOverrideStore.Muat())
                {
                    if (formats.ContainsKey(penyesuaian.Key)) formats[penyesuaian.Key] = penyesuaian.Value;
                }

                SuratNumberFormats = formats;
                TemplateNames = names;
                _sharedNumberingNames = shared;
                _logger.LogInformation("Daftar jenis surat dimuat dari JenisSuratConfig.json: {Count} entri, {Shared} penomoran bersama", names.Count, shared.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat JenisSuratConfig.json; format nomor surat mengikuti nilai kosong");
            }
        }

        /// <summary>
        /// Muat ulang daftar jenis surat + format nomor dari JenisSuratConfig.json.
        /// Dipakai setelah pengaturan penomoran surat diubah dari halaman Pengaturan
        /// Aplikasi, agar nilai di memori (dibaca GetSuratNumberFormat) ikut berubah
        /// tanpa perlu menutup aplikasi.
        /// </summary>
        public void ReloadSuratKindsFromConfig() => LoadSuratKindsFromConfigFile();

        // Inisialisasi konfigurasi dari IConfiguration (untuk ASP.NET Core/Host Builder)
        public AppConfig(IConfiguration configuration, ILogger<AppConfig>? logger = null)
        {
            _logger = logger ?? NullLogger<AppConfig>.Instance; // Default logger jika null

            try
            {
                var section = configuration.GetSection("AppConfig") ??
                              throw new InvalidOperationException("Bagian AppConfig tidak ditemukan.");

                DatabaseConnectionString = section["databaseConnectionString"] ??
                                          throw new InvalidOperationException("DatabaseConnectionString diperlukan.");
                TemplateFolder = ResolvePath(section["templateFolder"], "Templates");
                TempPdfFolder = ResolvePath(section["tempPdfFolder"], "TempPDF");
                PdfOutputPath = ResolvePath(section["pdfOutputPath"], "Output/PDF");
                FontFolder = ResolvePath(section["fontFolder"], "Resources/Fonts");
                LogoPath = ResolvePath(section["logoPath"], "Resources/logo.png");
                UpdateCheckUrl = ValidateUpdateCheckUrl(section["updateCheckUrl"] ?? "");
                GithubRepo = section["githubRepo"];
                GithubPersonalAccessToken = section["githubPersonalAccessToken"];

                LoadSuratKindsFromConfigFile();

                ValidatePaths();
                CreateDirectories();

                _logger.LogInformation("Konfigurasi aplikasi berhasil dimuat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat konfigurasi"); // Log error
                throw new InvalidOperationException("Gagal memuat konfigurasi aplikasi", ex);
            }
        }

        // Inisialisasi konfigurasi dari file appsettings.json (untuk WinForms)
        public AppConfig(string? configFilePath = null, ILogger<AppConfig>? logger = null)
        {
            _logger = logger ?? NullLogger<AppConfig>.Instance; // Default logger jika null

            try
            {
                configFilePath = configFilePath ??
                                 Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (!File.Exists(configFilePath))
                    throw new FileNotFoundException($"File konfigurasi tidak ditemukan: {configFilePath}");

                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                var configuration = builder.Build();
                var section = configuration.GetSection("AppConfig") ??
                              throw new InvalidOperationException("Bagian AppConfig tidak ditemukan.");

                DatabaseConnectionString = section["databaseConnectionString"] ??
                                          throw new InvalidOperationException("DatabaseConnectionString diperlukan.");
                TemplateFolder = ResolvePath(section["templateFolder"], "Templates");
                TempPdfFolder = ResolvePath(section["tempPdfFolder"], "TempPDF");
                PdfOutputPath = ResolvePath(section["pdfOutputPath"], "Output/PDF");
                FontFolder = ResolvePath(section["fontFolder"], "Resources/Fonts");
                LogoPath = ResolvePath(section["logoPath"], "Resources/logo.png");
                UpdateCheckUrl = ValidateUpdateCheckUrl(section["updateCheckUrl"] ?? "");
                GithubRepo = section["githubRepo"];
                GithubPersonalAccessToken = section["githubPersonalAccessToken"];

                LoadSuratKindsFromConfigFile();

                ValidatePaths();
                CreateDirectories();

                _logger.LogInformation("Konfigurasi aplikasi berhasil dimuat dari {ConfigFilePath}", configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat konfigurasi dari {ConfigFilePath}", configFilePath); // Log error
                throw new InvalidOperationException("Gagal memuat konfigurasi aplikasi", ex);
            }
        }

        // Validasi dan kembalikan URL untuk pembaruan
        private string ValidateUpdateCheckUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return ""; // Biarkan kosong jika tidak diisi
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Scheme.StartsWith("http"))
                _logger.LogWarning("UpdateCheckUrl tidak valid: {Url}", url); // Log jika URL invalid
            return url;
        }

        // Jadikan path absolut terhadap BaseDirectory (tidak bergantung pada working directory)
        private string ResolvePath(string? path, string defaultFolder)
        {
            return GetFullPath(string.IsNullOrWhiteSpace(path) ? defaultFolder : path);
        }

        // Kembalikan path absolut untuk file atau folder
        private string GetFullPath(string path, string? defaultFolder = null, bool optional = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) && defaultFolder != null)
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, defaultFolder); // Gunakan default

                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path ?? "");
                return optional ? fullPath : Path.GetFullPath(fullPath); // Bersihkan path
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses path: {Path}", path); // Log error
                throw new InvalidOperationException($"Path tidak valid: {path}", ex);
            }
        }

        // Validasi semua path yang diperlukan
        private void ValidatePaths()
        {
            ValidateFileExists(LogoPath, "Logo"); // Cek logo
            ValidateDatabasePath(); // Cek database
        }

        // Validasi dan buat path database jika belum ada
        private void ValidateDatabasePath()
        {
            var sumber = DatabaseConnectionString.Replace("Data Source=", "").Trim();
            var dbPath = Path.IsPathRooted(sumber)
                ? sumber
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, sumber); // Jadikan absolut

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir); // Buat direktori
                    _logger.LogInformation($"Direktori database dibuat: {dir}");
                }
            }
            else
            {
                // Jika direktori kosong, gunakan base directory
                dir = AppDomain.CurrentDomain.BaseDirectory;
                dbPath = Path.Combine(dir, Path.GetFileName(dbPath));
                _logger.LogInformation($"Database path di-set ke base directory: {dbPath}");
            }

            DatabasePathBawaan = dbPath;
            DatabaseKustom = false;

            // Pengguna boleh memilih lokasi database sendiri (Pengaturan Aplikasi →
            // Database) — mis. folder data desa di drive lain, sehingga database desa
            // nyata tidak perlu disalin ke folder aplikasi. Pilihannya disimpan di
            // preferensi (bukan appsettings) supaya tidak hilang saat aplikasi
            // diperbarui. Sumber non-berkas (:memory:, file:) tidak pernah ditimpa
            // karena tidak punya "lokasi berkas" untuk dipindahkan.
            if (ApakahSumberBerkas(sumber))
            {
                var pilihan = AppPreferenceStore.GetJalurDatabase();
                if (pilihan != null)
                {
                    try
                    {
                        var jalurPilihan = Path.GetFullPath(pilihan);

                        // Folder tujuan disiapkan LEBIH DAHULU dan lokasi baru hanya
                        // dipakai bila persiapan itu berhasil. Preferensi yang menunjuk
                        // jalur salah tidak boleh membuat aplikasi gagal membuka
                        // database — lebih baik kembali ke lokasi bawaan.
                        var dirPilihan = Path.GetDirectoryName(jalurPilihan);
                        if (!string.IsNullOrEmpty(dirPilihan) && !Directory.Exists(dirPilihan))
                        {
                            Directory.CreateDirectory(dirPilihan);
                            _logger.LogInformation($"Direktori database dibuat: {dirPilihan}");
                        }

                        if (!jalurPilihan.Equals(dbPath, StringComparison.OrdinalIgnoreCase))
                        {
                            dbPath = jalurPilihan;
                            DatabaseKustom = true;
                            if (File.Exists(dbPath))
                                _logger.LogInformation("Database memakai lokasi pilihan pengguna: {Path}", dbPath);
                            else
                                _logger.LogWarning("Database pilihan pengguna belum ada, akan dibuat baru: {Path}", dbPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Lokasi pilihan tidak sah (nilai rusak, folder tidak bisa dibuat,
                        // dsb.) — jangan batalkan startup hanya karena preferensi.
                        dbPath = DatabasePathBawaan;
                        DatabaseKustom = false;
                        _logger.LogWarning(ex,
                            "Lokasi database pilihan pengguna tidak sah, kembali ke bawaan: {Path}", pilihan);
                    }
                }
            }

            // Jangan buat file database otomatis - biarkan DatabaseInitializer menangani ini
            // Pastikan path database absolut agar konsisten terlepas dari working directory
            DatabasePath = dbPath;
            DatabaseConnectionString = $"Data Source={dbPath}";
            _logger.LogInformation($"Database path valid: {dbPath}");
        }

        /// <summary>
        /// True bila nilai Data Source merujuk berkas sungguhan (bukan :memory: atau URI
        /// file:) — hanya sumber seperti itu yang boleh dipindahkan ke lokasi pilihan
        /// pengguna.
        /// </summary>
        private static bool ApakahSumberBerkas(string dataSource) =>
            !string.IsNullOrWhiteSpace(dataSource) &&
            !dataSource.Trim().Equals(":memory:", StringComparison.OrdinalIgnoreCase) &&
            !dataSource.Trim().StartsWith("file:", StringComparison.OrdinalIgnoreCase);

        // Buat direktori yang diperlukan
        private void CreateDirectories()
        {
            CreateDirectory(GetFullPath(TemplateFolder, "Templates")); // Buat folder template
            CreateDirectory(GetFullPath(TempPdfFolder, "TempPDF")); // Buat folder PDF sementara
            CreateDirectory(GetFullPath(PdfOutputPath, "Output/PDF")); // Buat folder output PDF
            CreateDirectory(GetFullPath(FontFolder, "Resources/Fonts")); // Buat folder font
        }

        // Buat direktori jika belum ada
        private void CreateDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path); // Buat direktori
                _logger.LogInformation($"Direktori dibuat: {path}");
            }
        }

        // Validasi keberadaan file
        private void ValidateFileExists(string path, string name)
        {
            var fullPath = GetFullPath(path, optional: true); // Dapatkan path absolut
            if (!string.IsNullOrWhiteSpace(fullPath) && !File.Exists(fullPath))
                _logger.LogWarning($"File {name} tidak ditemukan: {fullPath}"); // Log warning
        }

        // Ambil format nomor surat berdasarkan nama template/jenis surat.
        // Pencarian case-insensitive karena sumber (JenisSuratConfig.json) memakai NamaJenis bergaya PascalCase.
        public string GetSuratNumberFormat(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                throw new ArgumentException("TemplateName tidak boleh kosong.", nameof(templateName));

            if (SuratNumberFormats.TryGetValue(templateName, out var format))
                return format; // Kembalikan format

            throw new InvalidOperationException($"Format nomor surat untuk {templateName} tidak ditemukan.");
        }

        // Cek apakah template valid
        public bool IsValidTemplate(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return false; // Template kosong tidak valid

            return TemplateNames.Contains(templateName.ToUpperInvariant()); // Cek daftar template
        }

        // Cek apakah jenis surat memakai grup penomoran bersama (IsSharedNumbering di JenisSuratConfig.json)
        public bool IsSharedNumbering(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                return false;

            return _sharedNumberingNames.Contains(namaJenis.Trim());
        }

        // Daftar (UPPER) semua jenis surat penomoran bersama — untuk klausa SQL IN dinamis
        public IReadOnlyCollection<string> GetSharedNumberingNames() => _sharedNumberingNames.ToArray();
    }

    public static class SuratTemplateNames
    {
        // Daftar nama template surat yang didukung
        public const string SKD_UMUM = "SKD_UMUM";
        public const string DOMISILI_WARGA = "DOMISILI_WARGA";
        public const string INSTANSI = "INSTANSI";
        public const string SKU = "SKU";
        public const string PENGANTAR_SKCK = "PENGANTAR_SKCK";
        public const string IZIN_ORTU = "IZIN_ORTU";
        public const string SKTM = "SKTM";
        public const string GARAPAN_SAWAH = "GARAPAN_SAWAH";
        public const string KEMATIAN = "KEMATIAN";
        public const string BEDANAMA = "BEDANAMA";
        public const string KENAL_LAHIR = "KENAL_LAHIR";
        public const string AHLI_WARIS = "AHLI_WARIS";
        public const string IJIN_TINGGAL = "IJIN_TINGGAL";
        public const string NTCR_N1 = "NTCR_N1";
        public const string NTCR_N2 = "NTCR_N2";
        public const string NTCR_N3 = "NTCR_N3";
        public const string NTCR_N4 = "NTCR_N4";
        public const string NTCR_N5 = "NTCR_N5";
        public const string NTCR_N6 = "NTCR_N6";
    }
}
