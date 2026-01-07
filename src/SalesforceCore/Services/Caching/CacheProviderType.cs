namespace SalesforceCore.Services.Caching;

/// <summary>
/// Specifies the cache provider implementation to use.
/// This is an explicit configuration option - no automatic fallback.
/// </summary>
public enum CacheProviderType
{
    /// <summary>
    /// In-memory cache using IMemoryCache.
    /// Best for single-instance deployments.
    /// Default option.
    /// </summary>
    Memory = 0,

    /// <summary>
    /// Distributed cache using IDistributedCache.
    /// Requires external registration (e.g., AddStackExchangeRedisCache).
    /// Use when you have Redis or another distributed cache configured.
    /// </summary>
    Distributed = 1,

    /// <summary>
    /// SQL Server-based distributed cache with built-in encryption.
    /// Uses Entity Framework Core with AES-256-GCM encryption.
    /// All cached data is encrypted at rest - no option to disable.
    /// Full audit logging of all cache operations.
    /// Ideal for government deployments and environments without Redis.
    /// </summary>
    SqlServer = 2
}
