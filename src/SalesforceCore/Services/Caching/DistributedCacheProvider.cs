using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Infrastructure.Locking;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Caching;

/// <summary>
/// Distributed cache provider implementation using IDistributedCache.
/// Enables Redis, SQL Server, or NCache support for enterprise-scale deployments.
/// </summary>
/// <remarks>
/// <para>
/// Benefits over in-memory caching:
/// - Shared cache across multiple web servers (load-balanced environments)
/// - Reduces redundant Salesforce API calls (schema metadata only fetched once per cluster)
/// - Consistent state across all instances (no stale cache issues)
/// - Survives application restarts
/// </para>
/// <para>
/// Thread-safety strategy:
/// - Local striped locking prevents cache stampede within each process
/// - Optional distributed lock (via <see cref="IDistributedLockProvider"/>) prevents stampede across processes
/// - Graceful degradation if a distributed lock provider is unavailable or a lock cannot be acquired
/// </para>
/// </remarks>
public class DistributedCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;
    private readonly SalesforceOptions _options;
    private readonly ILogger<DistributedCacheProvider> _logger;
    private readonly string _keyPrefix;
    private readonly IDistributedLockProvider? _distributedLockProvider;

    /// <summary>
    /// Striped lock array for local cache stampede prevention.
    /// </summary>
    private const int LockStripesCount = 32;
    private const int LockStripesMask = LockStripesCount - 1;
    private readonly SemaphoreSlim[] _lockStripes;

    /// <summary>
    /// Distributed lock settings.
    /// </summary>
    private static readonly TimeSpan DistributedLockTimeout = TimeSpan.FromSeconds(SalesforceConstants.Defaults.DistributedLockTimeoutSeconds);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Creates a new DistributedCacheProvider.
    /// </summary>
    public DistributedCacheProvider(
        IDistributedCache cache,
        IOptions<SalesforceOptions> options,
        ILogger<DistributedCacheProvider> logger,
        IDistributedLockProvider? distributedLockProvider = null)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _keyPrefix = _options.CacheKeyPrefix;
        _distributedLockProvider = distributedLockProvider;

        // Pre-allocate striped locks
        _lockStripes = new SemaphoreSlim[LockStripesCount];
        for (var i = 0; i < LockStripesCount; i++)
        {
            _lockStripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// Gets the lock stripe for a given key.
    /// </summary>
    private SemaphoreSlim GetLockForKey(string key)
    {
        var hash = (uint)key.GetHashCode();
        var index = (int)(hash & LockStripesMask);
        return _lockStripes[index];
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        try
        {
            var bytes = await _cache.GetAsync(fullKey, cancellationToken);
            if (bytes == null || bytes.Length == 0)
            {
                if (_options.EnableDebugLogging)
                {
                    _logger.LogDebug("Distributed cache miss for key {Key}", fullKey);
                }
                return default;
            }

            var value = JsonSerializer.Deserialize<T>(bytes, _jsonOptions);

            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Distributed cache hit for key {Key}", fullKey);
            }

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get value from distributed cache for key {Key}", fullKey);
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? _options.SchemaCacheDuration
            };

            await _cache.SetAsync(fullKey, bytes, cacheOptions, cancellationToken);

            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Cached value in distributed cache for key {Key} ({Size} bytes)", fullKey, bytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set value in distributed cache for key {Key}", fullKey);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        try
        {
            await _cache.RemoveAsync(fullKey, cancellationToken);

            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Removed distributed cache entry for key {Key}", fullKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove value from distributed cache for key {Key}", fullKey);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Uses a two-level locking strategy for cache stampede prevention:
    /// </para>
    /// <list type="number">
    /// <item>Local striped lock - prevents multiple threads in same process from invoking factory</item>
    /// <item>Distributed lock - prevents multiple processes/servers from invoking factory simultaneously</item>
    /// </list>
    /// <para>
    /// The distributed lock uses optimistic locking with verification. If the lock cannot be acquired
    /// after retries, the factory proceeds anyway (graceful degradation) to avoid deadlocks.
    /// </para>
    /// </remarks>
    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);

        // Fast path: try to get from cache before acquiring any locks
        var cachedValue = await TryGetFromCacheAsync<T>(fullKey, cancellationToken);
        if (cachedValue.Found)
        {
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Distributed cache hit for key {Key}", fullKey);
            }
            return cachedValue.Value;
        }

        // Cache miss - acquire local striped lock first
        var stripeLock = GetLockForKey(fullKey);

        await stripeLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring local lock
            cachedValue = await TryGetFromCacheAsync<T>(fullKey, cancellationToken);
            if (cachedValue.Found)
            {
                if (_options.EnableDebugLogging)
                {
                    _logger.LogDebug("Distributed cache hit for key {Key} after local lock", fullKey);
                }
                return cachedValue.Value;
            }

            // Try to acquire a real distributed lock if one is available.
            // If none is registered, we fall back to in-process stampede prevention only.
            var lockResource = $"sf_cache:{fullKey}";
            await using var distributedLock = _distributedLockProvider == null
                ? null
                : await _distributedLockProvider.TryAcquireAsync(lockResource, DistributedLockTimeout, cancellationToken);

            if (distributedLock != null)
            {
                // We have the distributed lock - double-check cache one more time
                cachedValue = await TryGetFromCacheAsync<T>(fullKey, cancellationToken);
                if (cachedValue.Found)
                {
                    if (_options.EnableDebugLogging)
                    {
                        _logger.LogDebug("Distributed cache hit for key {Key} after distributed lock", fullKey);
                    }
                    return cachedValue.Value;
                }
            }

            // Cache miss confirmed - invoke factory
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Distributed cache miss for key {Key}, invoking factory (distributed lock: {HasLock})",
                    fullKey, distributedLock != null);
            }

            var value = await factory(cancellationToken);

            if (value != null)
            {
                await SetAsync(key, value, expiration, cancellationToken);
            }

            return value;
        }
        finally
        {
            stripeLock.Release();
        }
    }

    /// <summary>
    /// Result of a cache lookup attempt.
    /// </summary>
    private readonly struct CacheResult<T>
    {
        public bool Found { get; init; }
        public T? Value { get; init; }
    }

    /// <summary>
    /// Attempts to get a value from the distributed cache.
    /// </summary>
    private async Task<CacheResult<T>> TryGetFromCacheAsync<T>(string fullKey, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache.GetAsync(fullKey, cancellationToken);
            if (bytes != null && bytes.Length > 0)
            {
                var value = JsonSerializer.Deserialize<T>(bytes, _jsonOptions);
                if (value != null)
                {
                    return new CacheResult<T> { Found = true, Value = value };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get value from distributed cache for key {Key}", fullKey);
        }

        return new CacheResult<T> { Found = false, Value = default };
    }

    private string GetFullKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}{key}";
    }
}
