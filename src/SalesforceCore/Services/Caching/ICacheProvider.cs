namespace SalesforceCore.Services.Caching;

/// <summary>
/// Abstraction layer for caching to decouple the library from specific caching implementations.
/// This enables seamless switching between IMemoryCache (single-instance) and
/// IDistributedCache (Redis, SQL Server, etc.) for enterprise-scale deployments.
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Gets a value from the cache.
    /// </summary>
    /// <typeparam name="T">Type of the cached value.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached value or default if not found.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in the cache.
    /// </summary>
    /// <typeparam name="T">Type of the value to cache.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <param name="value">Value to cache.</param>
    /// <param name="expiration">Cache entry expiration (sliding). If null, uses default from options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a value from the cache.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value from cache, or creates and caches it using the factory if not found.
    /// </summary>
    /// <typeparam name="T">Type of the cached value.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <param name="factory">Factory function to create the value if not cached.</param>
    /// <param name="expiration">Cache entry expiration. If null, uses default from options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached or newly created value.</returns>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);
}
