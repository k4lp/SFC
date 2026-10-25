using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Infrastructure.Locking;
using SalesforceCore.Services.Core;

namespace SalesforceCore.AspNetCore.TokenProviders;

/// <summary>
/// Token provider that stores Salesforce tokens in a distributed cache.
/// Use this for production deployments with multiple servers.
/// </summary>
/// <remarks>
/// Before using this provider, configure a distributed cache:
/// <code>
/// // For development:
/// builder.Services.AddDistributedMemoryCache();
///
/// // For production (Redis):
/// builder.Services.AddStackExchangeRedisCache(options =>
/// {
///     options.Configuration = "localhost:6379";
/// });
/// </code>
///
/// Register the provider:
/// <code>
/// builder.Services.AddSalesforceDistributedCacheTokenStorage();
/// </code>
/// </remarks>
public class DistributedCacheTokenProvider : ITokenProvider
{
    private const string CacheKeyPrefix = "sf_tokens_";
    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SalesforceOptions _salesforceOptions;
    private readonly ILogger<DistributedCacheTokenProvider> _logger;
    private readonly DistributedCacheTokenProviderOptions _options;
    private readonly ISynchronizationService _synchronizationService;
    private readonly IDistributedLockProvider? _distributedLockProvider;

    /// <summary>
    /// Creates a new DistributedCacheTokenProvider.
    /// </summary>
    public DistributedCacheTokenProvider(
        IDistributedCache cache,
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        IOptions<SalesforceOptions> salesforceOptions,
        ILogger<DistributedCacheTokenProvider> logger,
        ISynchronizationService synchronizationService,
        IDistributedLockProvider? distributedLockProvider = null,
        DistributedCacheTokenProviderOptions? options = null,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _salesforceOptions = salesforceOptions?.Value ?? throw new ArgumentNullException(nameof(salesforceOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationService = synchronizationService ?? throw new ArgumentNullException(nameof(synchronizationService));
        _distributedLockProvider = distributedLockProvider;
        _options = options ?? new DistributedCacheTokenProviderOptions();
        _protector = dataProtectionProvider?.CreateProtector("SalesforceCore.DistributedCacheTokenProvider.Tokens.v1");
    }

    private readonly IDataProtector? _protector;

    private string GetCacheKey()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        // Prefer NameIdentifier/sub over Name: display names are not guaranteed unique and
        // a collision would hand one user's Salesforce tokens to another.
        var userId = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? user?.FindFirst("sub")?.Value
                     ?? (_options.AllowUserNameAsCacheKey ? user?.Identity?.Name : null);

        // Session IDs are not rotated on sign-in in ASP.NET Core, so keying tokens on the session ID
        // alone allowed session fixation (SFC-04): an attacker who planted a session cookie could read
        // the victim's tokens. The key now always includes the authenticated user identity.
        string? sessionId = null;
        try
        {
            sessionId = _httpContextAccessor.HttpContext?.Session?.Id;
        }
        catch (InvalidOperationException)
        {
            // Session middleware not configured.
        }

        if (string.IsNullOrEmpty(userId))
        {
            if (!string.IsNullOrEmpty(sessionId) && _options.AllowAnonymousSessionCacheKey)
            {
                return $"{CacheKeyPrefix}{sessionId}";
            }

            throw new InvalidOperationException(
                "Unable to determine cache key. Ensure the user is authenticated (NameIdentifier or sub claim).");
        }

        var userPart = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId)));
        return string.IsNullOrEmpty(sessionId)
            ? $"{CacheKeyPrefix}u:{userPart}"
            : $"{CacheKeyPrefix}u:{userPart}:s:{sessionId}";
    }

    internal string GetCacheKeyForTesting() => GetCacheKey();

    private string Protect(string json)
    {
        if (_protector != null)
        {
            return "dp1:" + _protector.Protect(json);
        }

        if (!_options.AllowUnprotectedTokenStorage)
        {
            throw new InvalidOperationException(
                "Refusing to store Salesforce tokens unencrypted in the distributed cache. Register ASP.NET Core Data Protection " +
                "(services.AddDataProtection(), with persisted keys for multi-server deployments) or, for development only, " +
                "set DistributedCacheTokenProviderOptions.AllowUnprotectedTokenStorage = true.");
        }

        return json;
    }

    private string? Unprotect(string stored)
    {
        if (stored.StartsWith("dp1:", StringComparison.Ordinal))
        {
            return _protector?.Unprotect(stored.Substring(4));
        }

        // Legacy/plaintext entry: only accepted when unprotected storage is explicitly allowed.
        return _options.AllowUnprotectedTokenStorage ? stored : null;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);

        // Proactively refresh if token is about to expire (within 5 minutes)
        if (tokens != null && tokens.ExpiresAt.HasValue &&
            tokens.ExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("Token is about to expire, refreshing proactively.");
            // RefreshTokenAsync handles updating the cache
            return await RefreshTokenAsync(cancellationToken);
        }

        return tokens?.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        return tokens?.InstanceUrl;
    }

    /// <summary>
    /// Gets the refresh token from cache.
    /// </summary>
    public async Task<string?> GetRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        return tokens?.RefreshToken;
    }

    /// <summary>
    /// Stores tokens in the distributed cache.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="refreshToken">The refresh token (optional).</param>
    /// <param name="expiresIn">Token expiration in seconds (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetTokensAsync(
        string accessToken,
        string instanceUrl,
        string? refreshToken = null,
        int? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        var tokens = new CachedTokens
        {
            AccessToken = accessToken,
            InstanceUrl = instanceUrl,
            RefreshToken = refreshToken,
            ExpiresAt = expiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(expiresIn.Value)
                : null
        };

        var json = JsonSerializer.Serialize(tokens);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.TokenExpiration
        };

        try
        {
            var key = GetCacheKey();
            await _cache.SetStringAsync(key, Protect(json), cacheOptions, cancellationToken);
            _logger.LogDebug("Stored tokens in distributed cache with key {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store tokens in distributed cache");
            throw;
        }
    }

    /// <summary>
    /// Clears tokens from the distributed cache (logout).
    /// </summary>
    public async Task ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetCacheKey();
            await _cache.RemoveAsync(key, cancellationToken);
            _logger.LogDebug("Cleared tokens from distributed cache with key {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear tokens from distributed cache");
            throw;
        }
    }

    /// <summary>
    /// Checks if the current token is expired or about to expire.
    /// </summary>
    /// <param name="bufferMinutes">Buffer time before actual expiry to consider token expired.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsTokenExpiredAsync(int bufferMinutes = 5, CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        if (tokens?.ExpiresAt == null)
            return true; // No expiry info, assume expired

        return DateTime.UtcNow.AddMinutes(bufferMinutes) >= tokens.ExpiresAt.Value;
    }

    /// <inheritdoc/>
    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        return tokens != null &&
               !string.IsNullOrEmpty(tokens.AccessToken) &&
               !string.IsNullOrEmpty(tokens.InstanceUrl);
    }

    /// <inheritdoc/>
    public async Task RevokeTokenAsync(CancellationToken cancellationToken = default)
    {
        await ClearTokensAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        var lockResource = $"sf_token_refresh:{cacheKey}";

        // Remember which access token the caller was using. After acquiring the lock we only short-circuit
        // when a *different* token is now cached (i.e. someone else really refreshed). Otherwise a 401 on a
        // revoked-but-not-yet-expired token would be "refreshed" to the same rejected token.
        var tokenBeforeLock = (await GetTokensAsync(cancellationToken))?.AccessToken;

        // Best-effort local lock (prevents duplicate refresh within a single process).
        var semaphore = _synchronizationService.GetLock(lockResource);
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            // Cross-server lock if available. If we can't acquire, another server may be refreshing.
            await using var distributedLock = _distributedLockProvider == null
                ? null
                : await _distributedLockProvider.TryAcquireAsync(lockResource, TimeSpan.FromSeconds(30), cancellationToken);

            if (_distributedLockProvider != null && distributedLock == null)
            {
                _logger.LogWarning("Could not acquire distributed token refresh lock within timeout. Resource={Resource}", lockResource);

                // Another server may have refreshed; re-check cache and return if valid.
                var afterWait = await GetTokensAsync(cancellationToken);
                if (afterWait != null && afterWait.ExpiresAt.HasValue &&
                    afterWait.ExpiresAt.Value > DateTime.UtcNow.AddMinutes(5) &&
                    !string.Equals(afterWait.AccessToken, tokenBeforeLock, StringComparison.Ordinal))
                {
                    return afterWait.AccessToken;
                }
            }

            // Re-check token validity after acquiring lock
            var currentTokens = await GetTokensAsync(cancellationToken);
            if (currentTokens != null && currentTokens.ExpiresAt.HasValue &&
                currentTokens.ExpiresAt.Value > DateTime.UtcNow.AddMinutes(5) &&
                !string.Equals(currentTokens.AccessToken, tokenBeforeLock, StringComparison.Ordinal))
            {
                // Token was refreshed by another thread while we waited
                return currentTokens.AccessToken;
            }

            var tokens = currentTokens;
            if (string.IsNullOrEmpty(tokens?.RefreshToken))
            {
                _logger.LogWarning("Cannot refresh token: No refresh token available in cache.");
                return null;
            }

            var client = _httpClientFactory.CreateClient("SalesforceTokenRefresh");
            var domain = _salesforceOptions.Domain.TrimEnd('/');
            var tokenEndpoint = $"{domain}{SalesforceConstants.Paths.OAuthToken}";

            var requestData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _salesforceOptions.ClientId,
                ["refresh_token"] = tokens.RefreshToken
            };

            if (!string.IsNullOrEmpty(_salesforceOptions.ClientSecret))
            {
                requestData["client_secret"] = _salesforceOptions.ClientSecret;
            }

            if (_salesforceOptions.ValidateInstanceUrl &&
                !SalesforceCore.Security.SalesforceUrlValidator.TryValidate(domain, _salesforceOptions.AdditionalAllowedHosts, out var domainReason))
            {
                _logger.LogError("Refusing to send refresh token to untrusted login domain: {Reason}", domainReason);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(requestData)
            };

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Token refresh failed. Status: {StatusCode}, Content: {Content}", response.StatusCode,
                    SalesforceCore.Security.SensitiveDataRedactor.ForDiagnostics(errorContent, _salesforceOptions.MaxErrorBodyLength));
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            var newAccessToken = json.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (string.IsNullOrEmpty(newAccessToken))
            {
                _logger.LogError("Token refresh response did not contain an access token.");
                return null;
            }

            var instanceUrl = json.TryGetProperty("instance_url", out var instUrl) ? instUrl.GetString() : tokens.InstanceUrl;
            if (_salesforceOptions.ValidateInstanceUrl &&
                !SalesforceCore.Security.SalesforceUrlValidator.TryValidate(instanceUrl, _salesforceOptions.AdditionalAllowedHosts, out var instReason))
            {
                _logger.LogError("Token refresh returned an untrusted instance URL; discarding tokens. {Reason}", instReason);
                return null;
            }

            // Note: Refresh token might not be rotated, so keep existing if not returned
            // Usually Salesforce does not rotate refresh tokens unless configured
            var newRefreshToken = json.TryGetProperty("refresh_token", out var refToken) ? refToken.GetString() : tokens.RefreshToken;

            // Update cache with new tokens
            // Default to 8 hours or whatever is configured if response doesn't give expiration?
            // Salesforce response usually doesn't include "expires_in" for refresh flows in standard output?
            // Actually it does standard OAuth JSON.
            // Let's rely on configured session timeout for expiration if not provided, or parsing 'issued_at' if available.
            // But simplifying: just refresh expiry to now + session timeout.

            await SetTokensAsync(
                newAccessToken!,
                instanceUrl!,
                newRefreshToken,
                (int)_options.TokenExpiration.TotalSeconds,
                cancellationToken);

            _logger.LogInformation("Successfully refreshed Salesforce access token.");
            return newAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token refresh.");
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<CachedTokens?> GetTokensAsync(CancellationToken cancellationToken)
    {
        try
        {
            var key = GetCacheKey();
            var stored = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(stored))
                return null;

            var json = Unprotect(stored);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<CachedTokens>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve tokens from distributed cache");
            return null;
        }
    }

    private class CachedTokens
    {
        public string? AccessToken { get; set; }
        public string? InstanceUrl { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}

/// <summary>
/// Configuration options for DistributedCacheTokenProvider.
/// </summary>
public class DistributedCacheTokenProviderOptions
{
    /// <summary>
    /// How long tokens should be cached. Default: 8 hours.
    /// </summary>
    public TimeSpan TokenExpiration { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// When no session is available, allow falling back to <c>User.Identity.Name</c> as the token cache key.
    /// Default false: names are not guaranteed unique, so only NameIdentifier/<c>sub</c> claims are used.
    /// </summary>
    public bool AllowUserNameAsCacheKey { get; set; } = false;

    /// <summary>
    /// SFC-03: allow tokens to be written to the distributed cache without Data Protection encryption.
    /// Default false. Only enable for local development with an in-memory cache.
    /// </summary>
    public bool AllowUnprotectedTokenStorage { get; set; } = false;

    /// <summary>
    /// SFC-04: allow keying tokens on the ASP.NET Core session ID alone when there is no authenticated user.
    /// Default false because session IDs are not rotated on sign-in (session fixation risk).
    /// </summary>
    public bool AllowAnonymousSessionCacheKey { get; set; } = false;
}
