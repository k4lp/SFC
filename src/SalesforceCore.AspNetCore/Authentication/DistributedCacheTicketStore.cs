using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace SalesforceCore.AspNetCore.Authentication;

/// <summary>
/// Implements ITicketStore using IDistributedCache for server-side session storage.
/// This prevents cookie size limits (4KB) from being exceeded when storing OAuth tokens.
///
/// Usage:
/// - For in-memory caching: services.AddDistributedMemoryCache()
/// - For Redis: services.AddStackExchangeRedisCache(...)
/// - For SQL Server: services.AddDistributedSqlServerCache(...)
/// </summary>
public class DistributedCacheTicketStore : ITicketStore
{
    private const string KeyPrefix = "SalesforceAuth:";
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheTicketStore> _logger;
    private readonly DistributedCacheEntryOptions _cacheOptions;
    private readonly IDataProtector? _protector;

    /// <summary>
    /// Creates a new DistributedCacheTicketStore.
    /// </summary>
    /// <param name="cache">Distributed cache implementation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="sessionTimeout">Session timeout duration (default: 8 hours).</param>
    /// <param name="dataProtectionProvider">
    /// Optional data protection provider used to protect ticket bytes before storing them in the cache.
    /// Strongly recommended for production deployments because tickets can contain OAuth tokens.
    /// </param>
    public DistributedCacheTicketStore(
        IDistributedCache cache,
        ILogger<DistributedCacheTicketStore> logger,
        TimeSpan? sessionTimeout = null,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        _cache = cache;
        _logger = logger;
        _cacheOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = sessionTimeout ?? TimeSpan.FromHours(8)
        };

        _protector = dataProtectionProvider?.CreateProtector($"{nameof(DistributedCacheTicketStore)}:v1");
        if (_protector == null)
        {
            _logger.LogWarning(
                "{TicketStore} was created without an IDataProtectionProvider; authentication tickets will be stored unprotected in IDistributedCache. " +
                "This can expose OAuth tokens if the cache is compromised. Prefer passing IDataProtectionProvider from DI.",
                nameof(DistributedCacheTicketStore));
        }
    }

    /// <inheritdoc/>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = GenerateKey();
        await RenewAsync(key, ticket);
        _logger.LogDebug("Stored authentication ticket with key {Key}", key);
        return key;
    }

    /// <inheritdoc/>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var ticketData = TicketSerializer.Default.Serialize(ticket);
        if (_protector != null)
        {
            ticketData = _protector.Protect(ticketData);
        }

        await _cache.SetAsync(KeyPrefix + key, ticketData, _cacheOptions);
        _logger.LogDebug("Renewed authentication ticket with key {Key}", key);
    }

    /// <inheritdoc/>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var ticketData = await _cache.GetAsync(KeyPrefix + key);
        if (ticketData == null)
        {
            _logger.LogDebug("Authentication ticket not found for key {Key}", key);
            return null;
        }

        if (_protector != null)
        {
            try
            {
                ticketData = _protector.Unprotect(ticketData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect authentication ticket for key {Key}", key);
                return null;
            }
        }

        var ticket = TicketSerializer.Default.Deserialize(ticketData);
        _logger.LogDebug("Retrieved authentication ticket for key {Key}", key);
        return ticket;
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(KeyPrefix + key);
        _logger.LogDebug("Removed authentication ticket with key {Key}", key);
    }

    /// <summary>
    /// Generates a unique key for the authentication ticket.
    /// </summary>
    private static string GenerateKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
