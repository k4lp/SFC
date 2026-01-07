using System.Net.Http.Json;
using System.Text.Json;
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
        DistributedCacheTokenProviderOptions? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _salesforceOptions = salesforceOptions?.Value ?? throw new ArgumentNullException(nameof(salesforceOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationService = synchronizationService ?? throw new ArgumentNullException(nameof(synchronizationService));
        _distributedLockProvider = distributedLockProvider;
        _options = options ?? new DistributedCacheTokenProviderOptions();
    }

    private string GetCacheKey()
    {
        // Use session ID or a custom user identifier as the cache key
        var sessionId = _httpContextAccessor.HttpContext?.Session?.Id;
        if (string.IsNullOrEmpty(sessionId))
        {
            // Fallback to user identity if session is not available
            var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                throw new InvalidOperationException(
                    "Unable to determine cache key. Ensure session or user authentication is configured.");
            }
            return $"{CacheKeyPrefix}{userId}";
        }
        return $"{CacheKeyPrefix}{sessionId}";
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
            await _cache.SetStringAsync(key, json, cacheOptions, cancellationToken);
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
                    afterWait.ExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
                {
                    return afterWait.AccessToken;
                }
            }

            // Re-check token validity after acquiring lock
            var currentTokens = await GetTokensAsync(cancellationToken);
            if (currentTokens != null && currentTokens.ExpiresAt.HasValue &&
                currentTokens.ExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
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

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(requestData)
            };

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Token refresh failed. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            var newAccessToken = json.GetProperty("access_token").GetString();
            var instanceUrl = json.TryGetProperty("instance_url", out var instUrl) ? instUrl.GetString() : tokens.InstanceUrl;

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
            var json = await _cache.GetStringAsync(key, cancellationToken);
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
}
