using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System.Collections.Concurrent;

namespace SuDesApp.Configuration
{
    public static class CacheKeys
    {
        public static string Surat(int idSurat) => $"surat_{idSurat}";
        public static string SuratPrefix => "surat_";
        public static string DesaInfo => "desa_info";
        public static string DesaPrefix => "desa_";
    }

    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default);
        Task SetAsync<T>(string key, T value, MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default);
        Task RemoveAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync<T>(string key, CancellationToken cancellationToken = default);
        bool TryGetValue<T>(string key, out T? value);
        void Set<T>(string key, T value, MemoryCacheEntryOptions? options = null);
        Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
        Task ClearAsync(CancellationToken cancellationToken = default);
        int GetCacheCount();
        Task RemoveWargaAsync(string key, CancellationToken cancellationToken = default);
    }

    public class MemoryCacheService : ICacheService, IDisposable
    {
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheService> _logger;
    private readonly ConcurrentDictionary<string, byte> _cacheKeys; // Use byte instead of object
    private readonly string _keyPrefix;
    private volatile bool _disposed;

    public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheKeys = new ConcurrentDictionary<string, byte>();
        _keyPrefix = "SuDesApp"; // Centralized prefix
    }

    private MemoryCacheEntryOptions GetDefaultOptions()
    {
        return new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(15))
            .SetAbsoluteExpiration(TimeSpan.FromHours(1))
            .SetSize(1)
            .SetPriority(CacheItemPriority.Normal)
            .RegisterPostEvictionCallback(EvictionCallback);
    }

    private string GetPrefixedKey<T>(string key) => $"{_keyPrefix}:{typeof(T).Name}:{key}";

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            if (_cache.TryGetValue(prefixedKey, out T? value))
            {
                _logger.LogDebug("Cache hit for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
                return Task.FromResult(value);
            }

            _logger.LogDebug("Cache miss for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
            return Task.FromResult<T?>(default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key {Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory,
        MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);

            // Double-check pattern for thread safety
            if (_cache.TryGetValue(prefixedKey, out T? cachedValue))
            {
                _logger.LogDebug("Cache hit for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
                return cachedValue!;
            }

            // Use semaphore or lock if needed for expensive operations
            var value = await factory().ConfigureAwait(false);

            options ??= GetDefaultOptions();

            // Only cache non-null values
            if (value != null)
            {
                _cache.Set(prefixedKey, value, options);
                _cacheKeys.TryAdd(prefixedKey, 0);
                _logger.LogDebug("Cache set for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
            }
            else
            {
                _logger.LogWarning("Factory returned null for key: {Key}", prefixedKey);
            }

            return value!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateAsync for key {Key}", key);
            throw;
        }
    }

    private void EvictionCallback(object key, object? value, EvictionReason reason, object? state)
    {
        if (key is string cacheKey)
        {
            _cacheKeys.TryRemove(cacheKey, out _);
            _logger.LogDebug("Cache evicted: {Key}, Reason: {Reason}", cacheKey, reason);
        }
    }

    public Task SetAsync<T>(string key, T value, MemoryCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (value == null)
        {
            _logger.LogWarning("Attempted to cache null value for key: {Key}", key);
            return Task.CompletedTask;
        }

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            options ??= GetDefaultOptions();

            _cache.Set(prefixedKey, value, options);
            _cacheKeys.TryAdd(prefixedKey, 0);
            _logger.LogDebug("Cache set for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key {Key}", key);
            // Don't throw, just log the error
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            _cache.Remove(prefixedKey);
            _cacheKeys.TryRemove(prefixedKey, out _);
            _logger.LogDebug("Cache removed for key: {Key}", prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key {Key}", key);
        }

        return Task.CompletedTask;
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var prefixToMatch = $"{_keyPrefix}:{prefix}";
            // Kunci tersimpan berbentuk "SuDesApp:{Tipe}:{kunci}" — cocokkan juga
            // ":{prefix}" setelah segmen tipe, kalau tidak pembatalan cache
            // berdasarkan prefix diam-diam tidak pernah menghapus apa pun.
            var dalamSegmenTipe = $":{prefix}";
            var keysToRemove = _cacheKeys.Keys
                .Where(k => k.StartsWith(prefixToMatch, StringComparison.OrdinalIgnoreCase)
                            || k.Contains(dalamSegmenTipe, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var removeTask = Task.Run(() =>
            {
                Parallel.ForEach(keysToRemove, key =>
                {
                    _cache.Remove(key);
                    _cacheKeys.TryRemove(key, out _);
                    _logger.LogDebug("Cache removed for key: {Key} by prefix: {Prefix}", key, prefix);
                });
            }, cancellationToken);

            await removeTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache values by prefix {Prefix}", prefix);
        }
    }

    public Task<bool> ExistsAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            return Task.FromResult(_cache.TryGetValue(prefixedKey, out _));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key {Key}", key);
            return Task.FromResult(false);
        }
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        ThrowIfDisposed();

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            bool result = _cache.TryGetValue(prefixedKey, out value);
            _logger.LogDebug("TryGetValue for key: {Key}, Type: {Type}, Result: {Result}",
                prefixedKey, typeof(T).Name, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TryGetValue for key {Key}", key);
            value = default;
            return false;
        }
    }

    public void Set<T>(string key, T value, MemoryCacheEntryOptions? options = null)
    {
        ThrowIfDisposed();

        if (value == null)
        {
            _logger.LogWarning("Attempted to cache null value for key: {Key}", key);
            return;
        }

        try
        {
            string prefixedKey = GetPrefixedKey<T>(key);
            options ??= GetDefaultOptions();

            _cache.Set(prefixedKey, value, options);
            _cacheKeys.TryAdd(prefixedKey, 0);
            _logger.LogDebug("Cache set for key: {Key}, Type: {Type}", prefixedKey, typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key {Key}", key);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var keysToRemove = _cacheKeys.Keys.ToList();
            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _cacheKeys.TryRemove(key, out _);
            }
            _logger.LogInformation("Cache cleared, removed {Count} items", keysToRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }

        return Task.CompletedTask;
    }

    public int GetCacheCount()
    {
        ThrowIfDisposed();
        return _cacheKeys.Count;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryCacheService));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cacheKeys.Clear();
            _disposed = true;
            _logger.LogDebug("MemoryCacheService disposed");
        }
    }

    public Task RemoveWargaAsync(string key, CancellationToken cancellationToken = default)
    {
        return RemoveAsync<WargaData>(key, cancellationToken);
    }
}
}
