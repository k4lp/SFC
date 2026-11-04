using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Caching;

/// <summary>
/// In-memory cache provider implementation using IMemoryCache.
/// Best for single-instance deployments or development environments.
/// This is the default cache provider when UseDistributedCache is false.
/// </summary>
/// <remarks>
/// Thread-safety is achieved with process-wide per-key locks (KeyedAsyncLock):
/// - A lock exists only while a key is being created or awaited (no growth over time)
/// - Only callers of the same key wait for each other, so nested GetOrCreateAsync calls cannot deadlock
/// </remarks>
public class MemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly SalesforceOptions _options;
    private readonly ILogger<MemoryCacheProvider> _logger;
    private readonly string _keyPrefix;

    /// <summary>Process-wide per-key locks for stampede prevention (see <see cref="KeyedAsyncLock"/>).</summary>
    internal static readonly KeyedAsyncLock KeyLocks = new();

    /// <summary>
    /// Creates a new MemoryCacheProvider.
    /// </summary>
    public MemoryCacheProvider(
        IMemoryCache cache,
        IOptions<SalesforceOptions> options,
        ILogger<MemoryCacheProvider> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _keyPrefix = _options.CacheKeyPrefix;
    }

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        if (_cache.TryGetValue(fullKey, out T? value))
        {
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Cache hit for key {Key}", fullKey);
            }
            return Task.FromResult(value);
        }

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Cache miss for key {Key}", fullKey);
        }
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? _options.SchemaCacheDuration
        };

        _cache.Set(fullKey, value, cacheOptions);

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Cached value for key {Key} with expiration {Expiration}", fullKey, expiration ?? _options.SchemaCacheDuration);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        _cache.Remove(fullKey);

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Removed cache entry for key {Key}", fullKey);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a process-wide per-key lock to prevent cache stampede; factories may safely call GetOrCreateAsync for other keys.
    /// </remarks>
    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        // Fast path: check if value exists before acquiring lock
        if (_cache.TryGetValue(fullKey, out T? cached))
        {
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Cache hit for key {Key}", fullKey);
            }
            return cached;
        }

        // Cache miss - acquire the per-key lock to prevent cache stampede
        using (await KeyLocks.AcquireAsync(fullKey, cancellationToken).ConfigureAwait(false))
        {
            // Double-check after acquiring lock (another thread may have populated the cache)
            if (_cache.TryGetValue(fullKey, out cached))
            {
                if (_options.EnableDebugLogging)
                {
                    _logger.LogDebug("Cache hit for key {Key} after lock acquisition", fullKey);
                }
                return cached;
            }

            // Cache miss confirmed - create value
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Cache miss for key {Key}, invoking factory", fullKey);
            }

            var value = await factory(cancellationToken);

            if (value != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? _options.SchemaCacheDuration
                };
                _cache.Set(fullKey, value, cacheOptions);
            }

            return value;
        }
    }

    private string GetFullKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}{key}";
    }
}
