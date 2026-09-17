using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Layanan pembaruan aplikasi.
    ///
    /// Tiga sumber, dipilih otomatis berdasarkan konfigurasi:
    /// 1. <b>GitHub Releases</b> (disarankan) — isi <c>AppConfig.githubRepo</c>
    ///    (format <c>owner/repo</c>). Tanpa token untuk repo publik; token
    ///    opsional (<c>githubPersonalAccessToken</c>) untuk repo privat.
    /// 2. <b>version.json publik via HTTPS</b> — isi <c>AppConfig.updateCheckUrl</c>.
    /// 3. <b>Google Drive service account</b> (lama) — dipakai hanya bila keduanya
    ///    kosong DAN <c>Resources/sudesapp.json</c> masih ada. Berkas kunci itu
    ///    tidak lagi didistribusikan.
    ///
    /// Berkas pembaruan diverifikasi dengan SHA-256 (dari <c>digest</c> aset GitHub
    /// atau field <c>sha256</c> di version.json) sebelum dijalankan.
    /// </summary>
    public class UpdateService
    {
        private readonly ILogger<UpdateService> _logger;
        private readonly string _updateCheckFileId;
        private readonly string _credentialsPath;
        private readonly string _updateCheckUrl;
        private readonly string _githubRepo;
        private readonly string _githubToken;
        private readonly Exception? _initException;

        public bool IsAvailable => _initException == null;

        public UpdateService(AppConfig appConfig, ILogger<UpdateService> logger = null)
        {
            _logger = logger ?? NullLogger<UpdateService>.Instance;
            _updateCheckFileId = "1bAfykQQyNi6E0YF9ItpaHawFLgLOUAzb"; // ID version.json (mode Drive lama)
            _credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sudesapp.json");
            _updateCheckUrl = appConfig?.UpdateCheckUrl ?? string.Empty;
            _githubRepo = (appConfig?.GithubRepo ?? string.Empty).Trim().Trim('/');
            _githubToken = (appConfig?.GithubPersonalAccessToken ?? string.Empty).Trim();

            try
            {
                if (!UseGitHubSource &&
                    string.IsNullOrWhiteSpace(_updateCheckUrl) &&
                    !File.Exists(_credentialsPath))
                {
                    throw new InvalidOperationException(
                        "Sumber pembaruan belum dikonfigurasi. Isi 'AppConfig.githubRepo' (mis. \"pengguna/SuDesApp\") " +
                        "atau 'AppConfig.updateCheckUrl' di appsettings.json. Kunci service account Google tidak lagi didistribusikan.");
                }
            }
            catch (Exception ex)
            {
                _initException = ex;
                _logger.LogWarning(ex, "UpdateService initialization deferred: {Message}", ex.Message);
            }
        }

        private bool UseGitHubSource => !string.IsNullOrWhiteSpace(_githubRepo);
        private bool UseHttpSource => !string.IsNullOrWhiteSpace(_updateCheckUrl);

        public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            if (_initException != null) throw _initException;
            if (UseGitHubSource) return await CheckForUpdatesViaGitHubAsync(cancellationToken);
            if (UseHttpSource) return await CheckForUpdatesViaHttpAsync(cancellationToken);
            return await CheckForUpdatesViaDriveAsync();
        }

        // ===== Sumber 1: GitHub Releases =====

        private async Task<UpdateInfo> CheckForUpdatesViaGitHubAsync(CancellationToken cancellationToken)
        {
            var apiUrl = $"https://api.github.com/repos/{_githubRepo}/releases/latest";
            string json;
            try
            {
                using var http = CreateHttpClient(TimeSpan.FromSeconds(30));
                using var request = NewRequest(HttpMethod.Get, apiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new Exception($"Rilis GitHub tidak ditemukan untuk repo '{_githubRepo}'. Pastikan repo publik/benar.");

                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                _logger.LogError(ex, "Gagal mengunduh rilis GitHub dari {ApiUrl}", apiUrl);
                throw new Exception("Gagal cek pembaruan: Tidak bisa menghubungi GitHub (" + _githubRepo + ").", ex);
            }

            var updateInfo = ParseGitHubRelease(json);
            if (updateInfo == null || string.IsNullOrEmpty(updateInfo.Version))
            {
                _logger.LogWarning("Rilis GitHub tidak berisi tag versi yang bisa dibaca: {Repo}", _githubRepo);
                return null;
            }

            _logger.LogInformation("Versi GitHub ditemukan: {Version} ({Url})", updateInfo.Version, updateInfo.DownloadUrl);
            return updateInfo;
        }

        /// <summary>
        /// Ubah JSON rilis GitHub (<c>GET /repos/:owner/:repo/releases/latest</c>)
        /// menjadi <see cref="UpdateInfo"/>. Aset <c>.exe</c> pertama dipilih (atau
        /// aset pertama bila tidak ada). Tag <c>v2.2.0</c>/<c>2.2.0-rc1</c> dinormalkan
        /// ke <c>2.2.0</c>; <c>digest</c> <c>sha256:…</c> dipakai sebagai hash verifikasi.
        /// </summary>
        public static UpdateInfo? ParseGitHubRelease(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            GitHubRelease? release;
            try
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json);
            }
            catch (JsonException)
            {
                return null;
            }

            if (release == null) return null;

            var assets = release.Assets ?? new List<GitHubAsset>();
            var asset = assets.FirstOrDefault(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                        ?? assets.FirstOrDefault();
            if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)) return null;

            return new UpdateInfo
            {
                Version = NormalizeVersion(release.TagName),
                DownloadUrl = asset.BrowserDownloadUrl!,
                ReleaseNotes = release.Body ?? string.Empty,
                Sha256 = ExtractSha256(asset.Digest),
                GitHubApiAssetUrl = asset.ApiUrl,
            };
        }

        private static string NormalizeVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
            var trimmed = tag.Trim().TrimStart('v', 'V');
            var match = Regex.Match(trimmed, @"\d+(\.\d+)+");
            return match.Success ? match.Value : trimmed;
        }

        private static string? ExtractSha256(string? digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) return null;
            var value = digest.Trim();
            if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("sha256:".Length).Trim();
            return value.Length == 64 ? value : null;
        }

        // ===== Sumber 2: version.json via HTTPS =====

        private async Task<UpdateInfo> CheckForUpdatesViaHttpAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var http = CreateHttpClient(TimeSpan.FromSeconds(30));
                using var request = NewRequest(HttpMethod.Get, _updateCheckUrl);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var updateInfo = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream, cancellationToken: cancellationToken);
                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.Version))
                {
                    _logger.LogWarning("Data versi gak valid dari {Url}. Pastiin Version diisi di version.json.", _updateCheckUrl);
                    return null;
                }
                _logger.LogInformation("Versi ditemukan: {Version}", updateInfo.Version);
                return updateInfo;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Gagal parse version.json dari {Url}.", _updateCheckUrl);
                throw new Exception("Gagal cek pembaruan: Format version.json salah.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                _logger.LogError(ex, "Gagal mengunduh version.json dari {Url}", _updateCheckUrl);
                throw new Exception("Gagal cek pembaruan: Tidak bisa mengunduh version.json dari " + _updateCheckUrl, ex);
            }
        }

        // ===== Sumber 3: Google Drive (lama) =====

        private async Task<UpdateInfo> CheckForUpdatesViaDriveAsync()
        {
            if (_initException != null) throw _initException;
            try
            {
                var service = CreateDriveService();
                var fileId = _updateCheckFileId;
                var request = service.Files.Get(fileId);
                request.Fields = "id,name,mimeType";
                var file = await request.ExecuteAsync();
                _logger.LogInformation("Akses file: {FileName} (ID: {FileId})", file.Name, fileId);

                using var stream = new MemoryStream();
                await service.Files.Get(fileId).DownloadAsync(stream);
                stream.Position = 0;

                using var reader = new StreamReader(stream);
                stream.Position = 0;
                var jsonContent = await reader.ReadToEndAsync();
                _logger.LogDebug("Isi version.json: {JsonContent}", jsonContent);

                stream.Position = 0;
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(stream);
                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.Version))
                {
                    _logger.LogWarning("Data versi gak valid dari file ID: {FileId}. Pastiin Version diisi di version.json.", fileId);
                    return null;
                }
                _logger.LogInformation($"Versi ditemukan: {updateInfo.Version}");
                return updateInfo;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Gagal parse version.json dari file ID: {FileId}. Pastiin format JSON bener.", _updateCheckFileId);
                throw new Exception("Gagal cek pembaruan: Format version.json salah.", ex);
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Google Drive API not enabled or access forbidden: {Message}", gex.Message);
                throw new Exception("Google Drive API belum diaktifkan. Silakan aktifkan di Google Cloud Console.", gex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal cek pembaruan dari Google Drive file ID: {_updateCheckFileId}", _updateCheckFileId);
                throw new Exception("Gagal cek pembaruan: Gak bisa akses file version.json.", ex);
            }
        }

        // ===== Unduhan =====

        public async Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (_initException != null) throw _initException;

            if (IsHttpUrl(updateInfo?.DownloadUrl))
                return await DownloadUpdateViaHttpAsync(updateInfo!, progress, cancellationToken);

            // Validasi & ekstraksi ID dilakukan di luar try agar pesan kesalahan
            // yang spesifik (bukan "Gak bisa akses file") sampai ke pengguna.
            if (!TryExtractDriveFileId(updateInfo?.DownloadUrl, out var fileId))
            {
                _logger.LogWarning("URL download gak valid: {DownloadUrl}", updateInfo?.DownloadUrl);
                throw new ArgumentException("URL download harus berupa tautan HTTPS publik atau Google Drive!");
            }

            return await DownloadUpdateViaDriveAsync(updateInfo!, fileId, progress, cancellationToken);
        }

        private async Task<string> DownloadUpdateViaHttpAsync(UpdateInfo updateInfo, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"SuDesApp_{updateInfo.Version}.exe");
            try
            {
                var url = ChooseHttpDownloadUrl(updateInfo);
                using var http = CreateHttpClient();
                using var request = NewRequest(HttpMethod.Get, url);
                if (IsGitHubApiUrl(url)) request.Headers.Accept.ParseAdd("application/octet-stream");

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(fileName);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;
                    if (total > 0) progress?.Report((int)((totalRead * 100) / total));
                }

                VerifyHashOrThrow(fileName, updateInfo);
                progress?.Report(100);
                _logger.LogInformation("Pembaruan didownload ke: {FileName}", fileName);
                return fileName;
            }
            catch (OperationCanceledException)
            {
                TryDelete(fileName);
                throw;
            }
            catch (Exception ex)
            {
                TryDelete(fileName);
                _logger.LogError(ex, "Gagal download pembaruan dari {DownloadUrl}", updateInfo.DownloadUrl);
                throw new Exception("Gagal download pembaruan: " + ex.Message, ex);
            }
        }

        private string ChooseHttpDownloadUrl(UpdateInfo updateInfo)
        {
            // Repo privat: aset harus diunduh lewat API (butuh token & Accept octet-stream).
            if (!string.IsNullOrWhiteSpace(_githubToken) &&
                !string.IsNullOrWhiteSpace(updateInfo.GitHubApiAssetUrl))
            {
                return updateInfo.GitHubApiAssetUrl!;
            }
            return updateInfo.DownloadUrl;
        }

        private async Task<string> DownloadUpdateViaDriveAsync(UpdateInfo updateInfo, string fileId, IProgress<int> progress, CancellationToken cancellationToken)
        {
            try
            {
                var service = CreateDriveService();
                var request = service.Files.Get(fileId);
                request.Fields = "id,name,mimeType";
                var file = await request.ExecuteAsync();
                _logger.LogInformation("Download file: {FileName} (ID: {FileId})", file.Name, fileId);

                var fileName = Path.Combine(Path.GetTempPath(), $"SuDesApp_{updateInfo.Version}.exe");
                using var stream = new MemoryStream();
                await service.Files.Get(fileId).DownloadAsync(stream, cancellationToken);
                stream.Position = 0;

                using var fileStream = File.Create(fileName);
                var buffer = new byte[8192];
                int bytesRead;
                long totalRead = 0;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;
                    progress?.Report((int)((totalRead * 100) / stream.Length));
                }

                VerifyHashOrThrow(fileName, updateInfo);
                _logger.LogInformation($"Pembaruan didownload ke: {fileName}");
                return fileName;
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Google Drive API not enabled or access forbidden during download: {Message}", gex.Message);
                throw new Exception("Google Drive API belum diaktifkan. Silakan aktifkan di Google Cloud Console.", gex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal download pembaruan dari {DownloadUrl}", updateInfo.DownloadUrl);
                throw new Exception("Gagal download pembaruan: Gak bisa akses file.", ex);
            }
        }

        private void VerifyHashOrThrow(string filePath, UpdateInfo updateInfo)
        {
            if (string.IsNullOrWhiteSpace(updateInfo?.Sha256))
            {
                _logger.LogWarning("Sumber pembaruan tidak menyertakan sha256; verifikasi berkas dilewati.");
                return;
            }

            if (!VerifySha256(filePath, updateInfo.Sha256))
                throw new InvalidOperationException("Verifikasi berkas gagal: SHA-256 tidak cocok. Unduhan dibatalkan.");
        }

        /// <summary>
        /// Bandingkan SHA-256 berkas dengan nilai heksadesimal yang diharapkan
        /// (dari <c>digest</c> GitHub atau <c>sha256</c> di version.json). Panjang
        /// nilai harus 64 digit; pemisah spasi/tanda hubung diabaikan.
        /// </summary>
        public static bool VerifySha256(string filePath, string expectedHex)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            if (string.IsNullOrWhiteSpace(expectedHex)) return false;

            var expected = expectedHex.Trim().Replace(" ", "").Replace("-", "");
            if (expected.Length != 64) return false;

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var actual = Convert.ToHexString(sha.ComputeHash(stream));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHttpUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool IsGitHubApiUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url) &&
            url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase);

        private static HttpClient CreateHttpClient(TimeSpan? timeout = null) =>
            new() { Timeout = timeout ?? System.Threading.Timeout.InfiniteTimeSpan };

        private HttpRequestMessage NewRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.UserAgent.ParseAdd("SuDesApp-Updater");
            if (!string.IsNullOrWhiteSpace(_githubToken) && IsGitHubApiUrl(url))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _githubToken);
            return request;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* pembersihan terbaik; berkas bisa dikunci proses lain */ }
        }

        private DriveService CreateDriveService()
        {
            if (_initException != null) throw _initException;
            try
            {
                var credential = GoogleCredential.FromFile(_credentialsPath)
                    .CreateScoped(DriveService.Scope.DriveReadonly);
                return new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "SuDesApp"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal bikin DriveService pake credentials di {_credentialsPath}", _credentialsPath);
                throw new Exception("Gagal setup Google Drive API. Cek credentials.json.", ex);
            }
        }

        /// <summary>
        /// Ekstrak ID berkas Google Drive dari berbagai bentuk URL yang lazim:
        /// <c>drive.google.com/file/d/&lt;id&gt;/view</c>,
        /// <c>drive.google.com/uc?export=download&amp;id=&lt;id&gt;</c>,
        /// <c>drive.google.com/open?id=&lt;id&gt;</c>, dan
        /// <c>drive.usercontent.google.com/download?id=&lt;id&gt;&amp;export=download</c>.
        /// </summary>
        public static bool TryExtractDriveFileId(string? url, out string fileId)
        {
            fileId = string.Empty;
            if (string.IsNullOrWhiteSpace(url)) return false;

            bool isDriveHost =
                url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase);
            if (!isDriveHost) return false;

            var pathMatch = Regex.Match(url, @"file/d/([^/?#]+)");
            if (pathMatch.Success)
            {
                fileId = pathMatch.Groups[1].Value;
                return true;
            }

            var idMatch = Regex.Match(url, @"[?&]id=([^&]+)");
            if (idMatch.Success)
            {
                fileId = Uri.UnescapeDataString(idMatch.Groups[1].Value);
                return fileId.Length > 0;
            }

            return false;
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset>? Assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("url")]
            public string? ApiUrl { get; set; }

            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }
    }

    /// <summary>Informasi pembaruan dari version.json / rilis GitHub (porting dari WinForms).</summary>
    public class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        /// <summary>
        /// URL API aset GitHub untuk repo privat (dipakai saat ada token);
        /// tidak diisi untuk version.json biasa.
        /// </summary>
        [JsonIgnore]
        public string? GitHubApiAssetUrl { get; set; }
    }
}
