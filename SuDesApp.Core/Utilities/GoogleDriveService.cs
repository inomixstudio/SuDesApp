using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    public sealed class GoogleDriveOptions
    {
        public string ?CredentialsPath { get; set; }
        public string ?RootFolderId { get; set; }
        public string ?AuthMode { get; set; }
        public string ?ClientId { get; set; }
        public string ?ClientSecret { get; set; }
        public string ?TokenFolder { get; set; }
    }

    public sealed class DriveItem
    {
        public string ?Id { get; set; }
        public string ?Name { get; set; }
        public string ?MimeType { get; set; }
        public long? Size { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public string ?WebViewLink { get; set; }
        public string ?ParentId { get; set; }

        public bool IsFolder => MimeType == GoogleDriveService.FolderMimeType;
        public bool IsPdf => MimeType == "application/pdf";
        public bool IsGoogleDoc => MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal);

        public string TypeDisplay
        {
            get
            {
                if (IsFolder) return "Folder";
                if (IsPdf) return "PDF";
                if (MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document") return "Word";
                if (MimeType == "application/msword") return "Word";
                if (MimeType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") return "Excel";
                if (MimeType == "application/vnd.ms-excel") return "Excel";
                if (MimeType == "image/png" || MimeType == "image/jpeg") return "Gambar";
                if (MimeType == "text/plain") return "Teks";
                if (string.IsNullOrWhiteSpace(MimeType)) return "File";
                var parts = MimeType.Split('/');
                return parts.Length > 1 ? parts[1].ToUpperInvariant() : MimeType;
            }
        }

        public string SizeDisplay => Size.HasValue ? FormatSize(Size.Value) : "";

        public string ModifiedDisplay => ModifiedTime?.ToString("dd.MM.yyyy HH:mm") ?? "";

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }
    }

    public class GoogleDriveService : IDisposable
    {
        public const string FolderMimeType = "application/vnd.google-apps.folder";

        private readonly ILogger<GoogleDriveService> _logger;
        private readonly string _rootFolderId;
        private readonly string _authMode;
        // Tidak readonly: dapat diperbarui via SaveClientCredentials (setup awal).
        private string _clientId;
        private string _clientSecret;
        private readonly string _tokenFolder;
        private DriveService _service;

        /// <summary>
        /// Serialisasi pembuatan klien OAuth: dua pemanggil bersamaan (mis. badge
        /// sidebar saat startup + halaman login) tidak boleh sama-sama membangun
        /// klien, karena masing-masing akan membuka jendela browser login.
        /// </summary>
        private readonly SemaphoreSlim _clientLock = new(1, 1);

        /// <summary>
        /// Dipicu setiap kali klien Google dibuang (token dihapus / ganti akun /
        /// kredensial klien berubah). Layanan lain yang men-cache klien turunan
        /// — mis. <see cref="GoogleSheetsService"/> — wajib membuang cache-nya
        /// agar tidak terus memakai akun lama.
        /// </summary>
        public event Action? ClientInvalidated;

        public GoogleDriveService(IConfiguration configuration, ILogger<GoogleDriveService>? logger = null)
        {
            _logger = logger ?? NullLogger<GoogleDriveService>.Instance;

            var section = configuration.GetSection("GoogleDrive");
            _rootFolderId = section["rootFolderId"] ?? "";
            _authMode = section["authMode"] ?? "";

            // Sumber kredensial (urutan):
            // 1. File terenkripsi DPAPI per-user (GoogleClientCredentials) — jalur utama;
            // 2. appsettings.json (kompatibilitas instalasi lama) — sekret dulu ditulis di sini,
            //    kini section-nya boleh kosong/dihapus.
            var stored = GoogleClientCredentials.Load();
            if (!string.IsNullOrWhiteSpace(stored?.ClientId) && !string.IsNullOrWhiteSpace(stored.ClientSecret))
            {
                _clientId = stored.ClientId;
                _clientSecret = stored.ClientSecret;
            }
            else
            {
                _clientId = section["clientId"] ?? "";
                _clientSecret = section["clientSecret"] ?? "";
            }

            var configuredTokenFolder = string.IsNullOrWhiteSpace(section["tokenFolder"])
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp", "GoogleDriveToken")
                : section["tokenFolder"];
            _tokenFolder = PerUserTokenFolder(configuredTokenFolder!);
        }

        /// <summary>
        /// Token OAuth disimpan terpisah per user aplikasi (berdasarkan nama
        /// user sistem operasi), sehingga setiap user login otomatis ke akun
        /// Google-nya sendiri tanpa mengganggu user lain di komputer yang sama.
        /// Kredensial klien OAuth cukup diisi satu kali.
        /// </summary>
        private static string PerUserTokenFolder(string baseFolder)
        {
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user)) return baseFolder;

            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var safe = string.Concat(user.Select(c => invalid.Contains(c) ? '_' : c));
            return Path.Combine(baseFolder, safe);
        }

        public string RootFolderId => _rootFolderId;

        public bool IsOAuthEnabled =>
            _authMode.Equals("OAuth", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_clientId)
            && !string.IsNullOrWhiteSpace(_clientSecret);

        public string AuthModeDisplay =>
            IsOAuthEnabled ? "Akun Google masing-masing (OAuth)" : "Login Google belum dikonfigurasi";

        public string TokenFolder => _tokenFolder;

        public bool HasStoredToken() =>
            IsOAuthEnabled
            && Directory.Exists(_tokenFolder)
            && Directory.GetFiles(_tokenFolder).Any();

        /// <summary>
        /// Simpan kredensial klien OAuth (clientId + clientSecret) terenkripsi DPAPI
        /// per user Windows. Dipakai alur setup awal oleh teknisi/admin; setelah ini
        /// IsOAuthEnabled aktif tanpa menyentuh appsettings.json.
        /// </summary>
        public void SaveClientCredentials(string clientId, string clientSecret)
        {
            GoogleClientCredentials.Save(clientId, clientSecret);
            _clientId = clientId.Trim();
            _clientSecret = clientSecret.Trim();

            // Klien lama dibangun dengan kredensial lama — buang agar percobaan
            // berikutnya memakai kredensial baru tanpa perlu menjalankan ulang.
            ResetClient();
            ClientInvalidated?.Invoke();
        }

        /// <summary>Lokasi file penyimpanan kredensial (untuk pesan petunjuk setup).</summary>
        public static string CredentialsStorePath => GoogleClientCredentials.StoreDisplayPath;

        public async Task<DriveService> GetClientAsync(CancellationToken ct = default)
        {
            if (_service != null) return _service;

            await _clientLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check: pemanggil lain mungkin sudah selesai membangun klien
                // selama kita menunggu gembok.
                if (_service != null) return _service;

                var drive = await Task.Run(() => BuildClient(ct: ct), ct).ConfigureAwait(false);
                _service = drive;
                return drive;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async Task ClearTokenAsync(CancellationToken ct = default)
        {
            ResetClient();

            if (Directory.Exists(_tokenFolder))
            {
                foreach (var file in Directory.GetFiles(_tokenFolder))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal menghapus token: {Path}", file);
                    }
                }
            }

            // Beri tahu konsumen (mis. GoogleSheetsService) agar membuang klien
            // turunan yang masih memakai akun/token lama.
            ClientInvalidated?.Invoke();
            await Task.CompletedTask;
        }

        public async Task<string> GetAccountEmailAsync(CancellationToken ct = default)
        {
            var info = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return info?.Email!;
        }

        /// <summary>
        /// Ambil profil akun Google yang sedang login (email, nama tampilan,
        /// dan foto profil). Token tersimpan di-refresh senyap bila perlu.
        /// </summary>
        public async Task<GoogleAccountInfo?> GetAccountInfoAsync(CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            try
            {
                return await GoogleUserInfoService.GetAccountInfoAsync(client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Google Drive API not enabled or access forbidden: {Message}", gex.Message);
                throw new Exception(
                    "Akses ke Google Drive ditolak (403 Forbidden).\n\n" +
                    "Kemungkinan penyebab:\n" +
                    "1. Google Drive API belum diaktifkan pada proyek Google Cloud yang dipakai Client ID ini.\n" +
                    "2. OAuth consent screen masih berstatus \"Testing\" dan akun ini belum didaftarkan sebagai test user.\n\n" +
                    "Detail teknis: " + gex.Message, gex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil profil akun Google");
                throw new Exception(
                    "Gagal mengambil profil akun Google.\n\n" +
                    "Kemungkinan besar consent screen belum terselesaikan untuk scope Google Drive " +
                    "(masih \"Testing\" atau verifikasi tertunda). Detail teknis:\n" + ex.Message,
                    ex);
            }
        }

        private DriveService BuildClient(CancellationToken ct = default)
        {
            if (!IsOAuthEnabled)
            {
                throw new InvalidOperationException(
                    "Login Google belum dikonfigurasi.\n\n" +
                    "Kredensial klien OAuth (Client ID & Client Secret) belum tersimpan di komputer ini. " +
                    "Simpan sekali lewat setup teknisi (tersimpan terenkripsi di " + CredentialsStorePath + ") " +
                    "atau isi bagian GoogleDrive pada appsettings.json, agar setiap pengguna bisa login " +
                    "ke akun Google Drive miliknya sendiri.");
            }

            return BuildOAuthClient(ct);
        }

        public const string FormsBodyScope = "https://www.googleapis.com/auth/forms.body";
        public const string FormsResponsesScope = "https://www.googleapis.com/auth/forms.responses.readonly";

        private DriveService BuildOAuthClient(CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(_tokenFolder);
                var secrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret };
                var dataStore = new FileDataStore(_tokenFolder, true);

                var scopes = new[]
                {
                    DriveService.Scope.Drive,
                    FormsBodyScope,
                    FormsResponsesScope
                };

                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = secrets,
                    Scopes = scopes,
                    DataStore = dataStore,
                    // Selalu tampilkan pemilih akun: bila browser masih menyimpan
                    // sesi akun Google lama, Google tidak boleh otomatis memakai
                    // akun itu — pengguna harus memilih akun secara eksplisit.
                    Prompt = "select_account"
                });

                var credential = AuthorizeWithScopesAsync(flow, dataStore, "sudesapp", scopes, ct)
                    .GetAwaiter().GetResult();

                return new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "SuDesApp"
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal otorisasi OAuth ke Google Drive");
                throw new Exception(
                    "Gagal masuk ke akun Google.\n\n" +
                    "Periksa hal berikut:\n" +
                    "1. clientId & clientSecret klien OAuth sudah disimpan (terenkripsi di " + CredentialsStorePath + ").\n" +
                    "2. OAuth consent screen di Google Cloud Console berstatus \"In production\" (bila di \"Testing\", hanya akun yang terdaftar sebagai test user yang boleh login).\n" +
                    "3. Google Drive API sudah diaktifkan pada proyek Cloud.",
                    ex);
            }
        }

        /// <summary>
        /// Otorisasi dengan memeriksa cakupan (scope) token tersimpan: bila token
        /// lama belum memuat cakupan Forms, token dihapus agar Google menampilkan
        /// persetujuan ulang — mencegah kegagalan 403 "insufficient scope".
        /// </summary>
        private static async Task<UserCredential> AuthorizeWithScopesAsync(
            GoogleAuthorizationCodeFlow flow, FileDataStore dataStore,
            string userId, string[] scopes, CancellationToken ct)
        {
            var token = await flow.LoadTokenAsync(userId, ct).ConfigureAwait(false);
            if (token != null && !HasAllScopes(token.Scope, scopes))
            {
                await dataStore.DeleteAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>(userId).ConfigureAwait(false);
                token = null;
            }

            if (token == null)
            {
                var installedApp = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
                return await installedApp.AuthorizeAsync(userId, ct).ConfigureAwait(false);
            }

            return new UserCredential(flow, userId, token);
        }

        private static bool HasAllScopes(string? granted, IEnumerable<string> required)
        {
            var dimiliki = new HashSet<string>(
                (granted ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            return required.All(dimiliki.Contains);
        }

        public string GetRootFolderIdOrRoot() => string.IsNullOrWhiteSpace(_rootFolderId) ? "root" : _rootFolderId;

        /// <summary>Buang klien Drive yang di-cache (dipanggil saat token/kredensial berubah).</summary>
        private void ResetClient()
        {
            var old = _service;
            _service = null;
            old?.Dispose();
        }

        /// <summary>
        /// Membangun initializer klien Google API (OAuth credential + ApplicationName)
        /// untuk layanan Google lain di luar Drive — saat ini dipakai GoogleSheetsService
        /// (mode layanan online Google Sheet/Form) supaya warga cukup login Google sekali.
        /// </summary>
        public async Task<BaseClientService.Initializer> BuildSharedInitializerAsync(CancellationToken ct = default)
        {
            var drive = await GetClientAsync(ct).ConfigureAwait(false);
            return new BaseClientService.Initializer
            {
                HttpClientInitializer = drive.HttpClientInitializer,
                ApplicationName = "SuDesApp"
            };
        }

        public async Task<IList<DriveItem>> ListItemsAsync(string? folderId = null, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var effectiveFolder = string.IsNullOrWhiteSpace(folderId) ? GetRootFolderIdOrRoot() : folderId;

            var request = client.Files.List();
            request.Q = $"'{effectiveFolder}' in parents and trashed = false";
            request.Fields = "nextPageToken,files(id,name,mimeType,size,modifiedTime,webViewLink,parents)";
            request.PageSize = 200;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var items = new List<DriveItem>();
            do
            {
                var page = await request.ExecuteAsync(ct);
                if (page.Files != null)
                {
                    items.AddRange(page.Files.Select(f => ToDriveItem(f, effectiveFolder)));
                }
                request.PageToken = page.NextPageToken;
            } while (request.PageToken != null);

            return items.OrderByDescending(f => f.IsFolder).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IList<DriveItem>> SearchAsync(string query, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var request = client.Files.List();
            request.Q = $"trashed = false and (name contains '{EscapeQuery(query)}' or fullText contains '{EscapeQuery(query)}')";
            request.Fields = "nextPageToken,files(id,name,mimeType,size,modifiedTime,webViewLink,parents)";
            request.PageSize = 200;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var items = new List<DriveItem>();
            do
            {
                var page = await request.ExecuteAsync(ct);
                if (page.Files != null)
                {
                    items.AddRange(page.Files.Select(f => ToDriveItem(f, null)));
                }
                request.PageToken = page.NextPageToken;
            } while (request.PageToken != null);

            return items.OrderByDescending(f => f.IsFolder).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<DriveItem> GetItemAsync(string fileId, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var request = client.Files.Get(fileId);
            request.Fields = "id,name,mimeType,size,modifiedTime,webViewLink,parents";
            request.SupportsAllDrives = true;
            var file = await request.ExecuteAsync(ct);
            return ToDriveItem(file, null);
        }

        public async Task<string> FindOrCreateFolderAsync(string name, string? parentId = null, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var effectiveParent = string.IsNullOrWhiteSpace(parentId) ? GetRootFolderIdOrRoot() : parentId;

            var listRequest = client.Files.List();
            listRequest.Q = $"mimeType = '{FolderMimeType}' and trashed = false and name = '{EscapeQuery(name)}' and '{effectiveParent}' in parents";
            listRequest.Fields = "files(id,name,mimeType)";
            listRequest.SupportsAllDrives = true;
            var existing = await listRequest.ExecuteAsync(ct);
            if (existing.Files != null && existing.Files.Count > 0)
            {
                return existing.Files[0].Id;
            }

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                MimeType = FolderMimeType,
                Parents = new List<string> { effectiveParent }
            };
            var createRequest = client.Files.Create(meta);
            createRequest.SupportsAllDrives = true;
            var created = await createRequest.ExecuteAsync(ct);
            _logger.LogInformation("Folder Drive dibuat: {Name} (ID: {Id})", name, created.Id);
            return created.Id;
        }

        public async Task<DriveItem> UploadFileAsync(string filePath, string? parentId = null, string fileName = null,
            IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File tidak ditemukan: {filePath}");

            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var effectiveParent = string.IsNullOrWhiteSpace(parentId) ? GetRootFolderIdOrRoot() : parentId;
            var name = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(filePath) : fileName;
            var mimeType = GuessMimeType(filePath);

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = new List<string> { effectiveParent }
            };

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
            var request = client.Files.Create(meta, stream, mimeType);
            request.Fields = "id,name,mimeType,size,modifiedTime,webViewLink,parents";
            request.SupportsAllDrives = true;

            if (progress != null && stream.Length > 0)
            {
                request.ProgressChanged += p =>
                {
                    if (p.Status == UploadStatus.Uploading)
                    {
                        progress.Report(Math.Min(100, p.BytesSent * 100.0 / stream.Length));
                    }
                };
            }

            var upload = await request.UploadAsync(ct);
            if (upload.Status != UploadStatus.Completed)
                throw upload.Exception ?? new Exception($"Upload gagal dengan status {upload.Status}");

            if (request.ResponseBody == null)
                throw new Exception("Upload selesai tapi respons API kosong.");

            var result = ToDriveItem(request.ResponseBody, effectiveParent);
            _logger.LogInformation("File diunggah ke Drive: {Name} (ID: {Id})", result.Name, result.Id);
            return result;
        }

        public async Task<DriveItem> UploadBytesAsync(byte[] content, string mimeType, string fileName, string parentId = default,
            IProgress<double>? progress = null, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var effectiveParent = string.IsNullOrWhiteSpace(parentId) ? GetRootFolderIdOrRoot() : parentId;

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { effectiveParent }
            };

            await using var stream = new MemoryStream(content);
            var request = client.Files.Create(meta, stream, mimeType);
            request.Fields = "id,name,mimeType,size,modifiedTime,webViewLink,parents";
            request.SupportsAllDrives = true;

            if (progress != null && stream.Length > 0)
            {
                request.ProgressChanged += p =>
                {
                    if (p.Status == UploadStatus.Uploading)
                    {
                        progress.Report(Math.Min(100, p.BytesSent * 100.0 / stream.Length));
                    }
                };
            }

            var upload = await request.UploadAsync(ct);
            if (upload.Status != UploadStatus.Completed)
                throw upload.Exception ?? new Exception($"Upload gagal dengan status {upload.Status}");

            return ToDriveItem(request.ResponseBody, effectiveParent);
        }

        public async Task<byte[]> DownloadToBytesAsync(string fileId, CancellationToken ct = default)
        {
            await using var stream = new MemoryStream();
            await DownloadToStreamAsync(fileId, stream, ct);
            return stream.ToArray();
        }

        public async Task DownloadToStreamAsync(string fileId, Stream target, CancellationToken ct = default)
        {
            var request = (await GetClientAsync(ct).ConfigureAwait(false)).Files.Get(fileId);
            request.SupportsAllDrives = true;
            await request.DownloadAsync(target, ct);
        }

        public async Task DownloadToFileAsync(string fileId, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var metaRequest = client.Files.Get(fileId);
            metaRequest.Fields = "id,name,mimeType,size";
            metaRequest.SupportsAllDrives = true;
            var meta = await metaRequest.ExecuteAsync(ct);

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await using var fileStream = File.Create(targetPath);
            var request = client.Files.Get(fileId);
            request.SupportsAllDrives = true;

            if (progress != null && meta.Size.HasValue && meta.Size.Value > 0)
            {
                long total = meta.Size.Value;
                long last = 0;
                request.MediaDownloader.ProgressChanged += p =>
                {
                    if (p.Status == Google.Apis.Download.DownloadStatus.Downloading)
                    {
                        last = Math.Max(last, p.BytesDownloaded);
                        progress.Report(Math.Min(100, last * 100.0 / total));
                    }
                };
            }

            await request.DownloadAsync(fileStream, ct);
        }

        public async Task DeleteItemAsync(string fileId, CancellationToken ct = default)
        {
            var request = (await GetClientAsync(ct).ConfigureAwait(false)).Files.Delete(fileId);
            request.SupportsAllDrives = true;
            await request.ExecuteAsync(ct);
            _logger.LogInformation("File Drive dihapus (ID: {Id})", fileId);
        }

        public string ToWebViewLink(string fileId) => $"https://drive.google.com/file/d/{fileId}/view";

        /// <summary>
        /// Mengizinkan siapa saja yang memiliki tautan membaca berkas (dipakai
        /// agar warga tanpa akun Google bisa membuka formulir layanan surat).
        /// </summary>
        public async Task SetAnyoneReaderAsync(string fileId, CancellationToken ct = default)
        {
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = "anyone",
                Role = "reader"
            };
            var request = (await GetClientAsync(ct).ConfigureAwait(false)).Permissions.Create(permission, fileId);
            request.Fields = "id";
            await request.ExecuteAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Izin 'siapa saja dengan tautan' diberikan untuk berkas {Id}", fileId);
        }

        private static DriveItem ToDriveItem(Google.Apis.Drive.v3.Data.File f, string parentId)
        {
            return new DriveItem
            {
                Id = f.Id,
                Name = f.Name,
                MimeType = f.MimeType,
                Size = f.Size,
                ModifiedTime = f.ModifiedTime,
                WebViewLink = f.WebViewLink,
                ParentId = parentId ?? f.Parents?.FirstOrDefault()!
            };
        }

        private static string EscapeQuery(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        }

        internal static string GuessMimeType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".odt" => "application/vnd.oasis.opendocument.text",
                ".rtf" => "application/rtf",
                _ => "application/octet-stream"
            };
        }

        public void Dispose()
        {
            ResetClient();
        }
    }
}