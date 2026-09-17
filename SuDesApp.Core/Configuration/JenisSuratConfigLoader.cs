using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Configuration
{
    public class JenisSuratConfig
    {
        [JsonPropertyName("NamaJenis")]
        public string NamaJenis { get; set; } = string.Empty;

        [JsonPropertyName("KodeJenis")]
        public string KodeJenis { get; set; } = string.Empty;

        [JsonPropertyName("TemplateName")]
        public string TemplateName { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("NomorFormat")]
        public string NomorFormat { get; set; } = string.Empty;

        [JsonPropertyName("IsSharedNumbering")]
        public bool IsSharedNumbering { get; set; }

        [JsonPropertyName("IsKeteranganDesa")]
        public bool IsKeteranganDesa { get; set; }

        // Validation method
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(NamaJenis) &&
                   !string.IsNullOrWhiteSpace(KodeJenis) &&
                   !string.IsNullOrWhiteSpace(TemplateName) &&
                   !string.IsNullOrWhiteSpace(DisplayName) &&
                   !string.IsNullOrWhiteSpace(NomorFormat);
        }
    }

    // PERBAIKAN: Tambahkan event untuk notifikasi perubahan konfigurasi
    public class ConfigurationChangedEventArgs : EventArgs
    {
        public List<JenisSuratConfig> NewConfigurations { get; }
        public DateTime ChangeTime { get; }
        public string ChangeReason { get; }

        public ConfigurationChangedEventArgs(List<JenisSuratConfig> newConfigurations, string changeReason = "File changed")
        {
            NewConfigurations = newConfigurations;
            ChangeTime = DateTime.UtcNow;
            ChangeReason = changeReason;
        }
    }

    public interface IJenisSuratConfigLoader : IDisposable
    {
        // PERBAIKAN: Tambahkan event untuk notifikasi perubahan
        event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

        Task<List<JenisSuratConfig>> LoadConfigAsync(CancellationToken cancellationToken = default);
        Task<string> GetNomorFormatAsync(string namaJenis, CancellationToken cancellationToken = default);
        Task<JenisSuratConfig> GetConfigByKodeJenisAsync(string kodeJenis, CancellationToken cancellationToken = default);
        Task<JenisSuratConfig> GetConfigByNamaJenisAsync(string namaJenis, CancellationToken cancellationToken = default);
        Task<bool> IsSharedNumberingAsync(string namaJenis, CancellationToken cancellationToken = default);
        Task RefreshConfigAsync(CancellationToken cancellationToken = default);
        Task<List<JenisSuratConfig>> GetConfigsAsync(CancellationToken cancellationToken = default);

        // PERBAIKAN: Method baru untuk mendapatkan grup berdasarkan property
        Task<List<JenisSuratConfig>> GetConfigsBySharedNumberingAsync(bool isSharedNumbering, CancellationToken cancellationToken = default);
        Task<List<JenisSuratConfig>> GetConfigsByKeteranganDesaAsync(bool isKeteranganDesa, CancellationToken cancellationToken = default);
        Task<Dictionary<string, List<JenisSuratConfig>>> GetConfigGroupsAsync(CancellationToken cancellationToken = default);
    }

    public class JenisSuratConfigLoader : IJenisSuratConfigLoader
    {
        private readonly string _configPath;
        private readonly ICacheService _cacheService;
        private readonly ILogger<JenisSuratConfigLoader> _logger;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, JenisSuratConfig> _configCache;

        private const string CacheKey = "JenisSuratConfig";
        private const string ConfigByKodePrefix = "ConfigByKode";
        private const string ConfigByNamaPrefix = "ConfigByNama";
        private const string SharedNumberingCacheKey = "SharedNumberingConfigs";
        private const string KeteranganDesaCacheKey = "KeteranganDesaConfigs";
        private const string ConfigGroupsCacheKey = "ConfigGroups";

        private FileSystemWatcher? _fileWatcher;
        private volatile bool _disposed;
        private DateTime _lastFileChangeTime = DateTime.MinValue;
        private readonly TimeSpan _fileChangeDebounceTime = TimeSpan.FromMilliseconds(500);
        private readonly object _fileWatcherLock = new object();

        // PERBAIKAN: Event untuk notifikasi perubahan konfigurasi
        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public JenisSuratConfigLoader(
            ICacheService cacheService,
            IConfiguration configuration,
            ILogger<JenisSuratConfigLoader> logger,
            string? configPath = null)
        {
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            // Parameter configuration dipertahankan demi kompatibilitas signature DI,
            // namun tidak lagi dipakai: JSON adalah satu-satunya sumber konfigurasi jenis surat.
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configPath = configPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "JenisSuratConfig.json");
            _semaphore = new SemaphoreSlim(1, 1);
            _configCache = new ConcurrentDictionary<string, JenisSuratConfig>();

            InitializeFileWatcher();
        }

        private void InitializeFileWatcher()
        {
            try
            {
                var fullPath = Path.GetFullPath(_configPath);
                var directory = Path.GetDirectoryName(fullPath);
                var fileName = Path.GetFileName(fullPath);

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                {
                    _logger.LogError("Invalid config path: {ConfigPath}", _configPath);
                    return;
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created config directory: {Directory}", directory);
                }

                lock (_fileWatcherLock)
                {
                    _fileWatcher?.Dispose();
                    _fileWatcher = new FileSystemWatcher
                    {
                        Path = directory,
                        Filter = fileName,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    _fileWatcher.Changed += OnConfigFileChanged;
                    _fileWatcher.Error += OnFileWatcherError;
                }

                _logger.LogDebug("FileSystemWatcher initialized for: {ConfigPath}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FileSystemWatcher for {ConfigPath}", _configPath);
            }
        }

        private async void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastFileChangeTime < _fileChangeDebounceTime)
                {
                    return; // Debounce multiple file system events
                }
                _lastFileChangeTime = now;

                _logger.LogInformation("Config file changed: {FilePath}", e.FullPath);

                // Wait a bit to ensure file write is complete
                await Task.Delay(200);

                await RefreshConfigAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling config file change");
            }
        }

        private void OnFileWatcherError(object sender, ErrorEventArgs e)
        {
            if (_disposed) return;

            _logger.LogError(e.GetException(), "FileSystemWatcher error occurred");

            // Try to reinitialize the watcher
            try
            {
                InitializeFileWatcher();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reinitialize FileSystemWatcher");
            }
        }

        public async Task<List<JenisSuratConfig>> LoadConfigAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                return await _cacheService.GetOrCreateAsync(
                    CacheKey,
                    async () => await LoadConfigFromFileAsync(cancellationToken),
                    new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(30),
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
                        Priority = CacheItemPriority.High
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration");
                throw;
            }
        }

        private async Task<List<JenisSuratConfig>> LoadConfigFromFileAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogDebug("Loading JenisSurat configuration from file: {ConfigPath}", _configPath);

                if (!File.Exists(_configPath))
                {
                    _logger.LogWarning("Configuration file not found: {ConfigPath}. Creating default file.", _configPath);
                    await CreateDefaultConfigAsync(cancellationToken);
                }

                string json = await ReadFileWithRetryAsync(cancellationToken);
                var configs = JsonSerializer.Deserialize<List<JenisSuratConfig>>(json, JsonOptions);

                if (configs == null || configs.Count == 0)
                {
                    throw new InvalidOperationException("Configuration file is empty or invalid");
                }

                ValidateAndSyncConfig(configs);
                CacheIndividualConfigs(configs);

                _logger.LogInformation("Successfully loaded {Count} configurations", configs.Count);
                return configs;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // PERBAIKAN: Method untuk membuat konfigurasi default yang lebih lengkap
        private async Task CreateDefaultConfigAsync(CancellationToken cancellationToken)
        {
            var defaultConfigs = new List<JenisSuratConfig>
            {
                new JenisSuratConfig
                {
                    NamaJenis = "SKD_UMUM",
                    KodeJenis = "SKD",
                    TemplateName = "SKD_UMUM",
                    DisplayName = "SKD Umum",
                    NomorFormat = "470/{0:D3}/Ds/{2:yyyy}",
                    IsSharedNumbering = true,
                    IsKeteranganDesa = true
                },
                new JenisSuratConfig
                {
                    NamaJenis = "DOMISILI_WARGA",
                    KodeJenis = "DOM_WRG",
                    TemplateName = "DOMISILI_WARGA",
                    DisplayName = "Domisili Warga",
                    NomorFormat = "470/{0:D3}/Ds/{2:yyyy}",
                    IsSharedNumbering = true,
                    IsKeteranganDesa = true
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            await File.WriteAllTextAsync(_configPath, JsonSerializer.Serialize(defaultConfigs, JsonOptions), cancellationToken);
            _logger.LogInformation("Created default configuration file with {Count} entries", defaultConfigs.Count);
        }

        private async Task<string> ReadFileWithRetryAsync(CancellationToken cancellationToken)
        {
            const int maxRetries = 3;
            const int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await File.ReadAllTextAsync(_configPath, cancellationToken);
                }
                catch (IOException ex) when (i < maxRetries - 1)
                {
                    _logger.LogWarning(ex, "File read attempt {Attempt} failed, retrying...", i + 1);
                    await Task.Delay(delayMs * (i + 1), cancellationToken);
                }
            }

            throw new IOException($"Failed to read configuration file after {maxRetries} attempts");
        }

        private void ValidateAndSyncConfig(List<JenisSuratConfig> configs)
        {
            // Validate individual configs
            var invalidConfigs = configs.Where(c => !c.IsValid()).ToList();
            if (invalidConfigs.Any())
            {
                var invalidNames = string.Join(", ", invalidConfigs.Select(c => c.NamaJenis));
                throw new InvalidOperationException($"Invalid configurations found: {invalidNames}");
            }

            // Check for duplicates
            var duplicatesByNama = configs
                .GroupBy(c => c.NamaJenis, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            var duplicatesByKode = configs
                .GroupBy(c => c.KodeJenis, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatesByNama.Any() || duplicatesByKode.Any())
            {
                var duplicates = string.Join(", ", duplicatesByNama.Concat(duplicatesByKode));
                throw new InvalidOperationException($"Duplicate configurations found: {duplicates}");
            }

            // Log configuration summary
            LogConfigurationSummary(configs);
        }

        // PERBAIKAN: Method untuk log summary konfigurasi
        private void LogConfigurationSummary(List<JenisSuratConfig> configs)
        {
            var sharedNumberingCount = configs.Count(c => c.IsSharedNumbering);
            var keteranganDesaCount = configs.Count(c => c.IsKeteranganDesa);

            _logger.LogInformation("Configuration summary: Total={Total}, SharedNumbering={Shared}, KeteranganDesa={KetDesa}",
                configs.Count, sharedNumberingCount, keteranganDesaCount);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var config in configs)
                {
                    _logger.LogDebug("Config: {Nama} -> Shared:{Shared}, KetDesa:{KetDesa}, Format:{Format}",
                        config.NamaJenis, config.IsSharedNumbering, config.IsKeteranganDesa, config.NomorFormat);
                }
            }
        }

        private void CacheIndividualConfigs(List<JenisSuratConfig> configs)
        {
            _configCache.Clear();

            foreach (var config in configs)
            {
                _configCache.TryAdd($"{ConfigByKodePrefix}:{config.KodeJenis.ToUpperInvariant()}", config);
                _configCache.TryAdd($"{ConfigByNamaPrefix}:{config.NamaJenis.ToUpperInvariant()}", config);
            }
        }

        public async Task<string> GetNomorFormatAsync(string namaJenis, CancellationToken cancellationToken = default)
        {
            var config = await GetConfigByNamaJenisAsync(namaJenis, cancellationToken);
            return config.NomorFormat;
        }

        public async Task<JenisSuratConfig> GetConfigByKodeJenisAsync(string kodeJenis, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(kodeJenis))
                throw new ArgumentException("Kode jenis cannot be null or empty", nameof(kodeJenis));

            ThrowIfDisposed();

            var cacheKey = $"{ConfigByKodePrefix}:{kodeJenis.ToUpperInvariant()}";

            if (_configCache.TryGetValue(cacheKey, out var cachedConfig))
            {
                return cachedConfig;
            }

            var configs = await LoadConfigAsync(cancellationToken);
            var config = configs.FirstOrDefault(c =>
                string.Equals(c.KodeJenis, kodeJenis, StringComparison.OrdinalIgnoreCase));

            return config ?? throw new ArgumentException($"Configuration with code '{kodeJenis}' not found");
        }

        public async Task<JenisSuratConfig> GetConfigByNamaJenisAsync(string namaJenis, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                throw new ArgumentException("Nama jenis cannot be null or empty", nameof(namaJenis));

            ThrowIfDisposed();

            var cacheKey = $"{ConfigByNamaPrefix}:{namaJenis.ToUpperInvariant()}";

            if (_configCache.TryGetValue(cacheKey, out var cachedConfig))
            {
                return cachedConfig;
            }

            var configs = await LoadConfigAsync(cancellationToken);
            var config = configs.FirstOrDefault(c =>
                string.Equals(c.NamaJenis, namaJenis, StringComparison.OrdinalIgnoreCase));

            return config ?? throw new ArgumentException($"Configuration with name '{namaJenis}' not found");
        }

        public async Task<bool> IsSharedNumberingAsync(string namaJenis, CancellationToken cancellationToken = default)
        {
            var config = await GetConfigByNamaJenisAsync(namaJenis, cancellationToken);
            return config.IsSharedNumbering;
        }

        // PERBAIKAN: Method baru untuk mendapatkan configs berdasarkan IsSharedNumbering
        public async Task<List<JenisSuratConfig>> GetConfigsBySharedNumberingAsync(bool isSharedNumbering, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"{SharedNumberingCacheKey}_{isSharedNumbering}";
            return await _cacheService.GetOrCreateAsync(
                cacheKey,
                async () =>
                {
                    var configs = await LoadConfigAsync(cancellationToken);
                    return configs.Where(c => c.IsSharedNumbering == isSharedNumbering).ToList();
                },
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(15),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                },
                cancellationToken);
        }

        // PERBAIKAN: Method baru untuk mendapatkan configs berdasarkan IsKeteranganDesa  
        public async Task<List<JenisSuratConfig>> GetConfigsByKeteranganDesaAsync(bool isKeteranganDesa, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"{KeteranganDesaCacheKey}_{isKeteranganDesa}";
            return await _cacheService.GetOrCreateAsync(
                cacheKey,
                async () =>
                {
                    var configs = await LoadConfigAsync(cancellationToken);
                    return configs.Where(c => c.IsKeteranganDesa == isKeteranganDesa).ToList();
                },
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(15),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                },
                cancellationToken);
        }

        // PERBAIKAN: Method baru untuk mendapatkan semua grup konfigurasi
        public async Task<Dictionary<string, List<JenisSuratConfig>>> GetConfigGroupsAsync(CancellationToken cancellationToken = default)
        {
            return await _cacheService.GetOrCreateAsync(
                ConfigGroupsCacheKey,
                async () =>
                {
                    var configs = await LoadConfigAsync(cancellationToken);
                    return new Dictionary<string, List<JenisSuratConfig>>
                    {
                        ["SharedNumbering"] = configs.Where(c => c.IsSharedNumbering).ToList(),
                        ["NonSharedNumbering"] = configs.Where(c => !c.IsSharedNumbering).ToList(),
                        ["KeteranganDesa"] = configs.Where(c => c.IsKeteranganDesa).ToList(),
                        ["NonKeteranganDesa"] = configs.Where(c => !c.IsKeteranganDesa).ToList(),
                        ["All"] = configs
                    };
                },
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(15),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                },
                cancellationToken);
        }

        public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                // Clear all related caches
                await ClearAllCachesAsync(cancellationToken);

                // Preload the config
                var newConfigs = await LoadConfigAsync(cancellationToken);

                // PERBAIKAN: Fire event untuk notifikasi perubahan
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(newConfigs, "Manual refresh"));

                _logger.LogInformation("Configuration refreshed successfully with {Count} configs", newConfigs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh configuration");
                throw;
            }
        }

        // PERBAIKAN: Method untuk clear semua cache terkait
        private async Task ClearAllCachesAsync(CancellationToken cancellationToken = default)
        {
            await _cacheService.RemoveAsync<List<JenisSuratConfig>>(CacheKey, cancellationToken);
            await _cacheService.RemoveByPrefixAsync(ConfigByKodePrefix);
            await _cacheService.RemoveByPrefixAsync(ConfigByNamaPrefix);
            await _cacheService.RemoveByPrefixAsync(SharedNumberingCacheKey);
            await _cacheService.RemoveByPrefixAsync(KeteranganDesaCacheKey);
            await _cacheService.RemoveAsync<Dictionary<string, List<JenisSuratConfig>>>(ConfigGroupsCacheKey, cancellationToken);

            _configCache.Clear();
        }

        public Task<List<JenisSuratConfig>> GetConfigsAsync(CancellationToken cancellationToken = default)
        {
            return LoadConfigAsync(cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JenisSuratConfigLoader));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _disposed = true;

                    lock (_fileWatcherLock)
                    {
                        if (_fileWatcher != null)
                        {
                            _fileWatcher.Changed -= OnConfigFileChanged;
                            _fileWatcher.Error -= OnFileWatcherError;
                            _fileWatcher.Dispose();
                            _fileWatcher = null;
                        }
                    }

                    _semaphore?.Dispose();
                    _configCache.Clear();

                    // Clear event handlers
                    ConfigurationChanged = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during disposal");
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
