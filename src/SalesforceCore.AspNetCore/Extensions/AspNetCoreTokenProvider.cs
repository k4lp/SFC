using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore;
using SalesforceCore.Infrastructure.Locking;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;

namespace SalesforceCore.AspNetCore.Extensions;

/// <summary>
/// Token provider implementation for ASP.NET Core using cookie authentication.
/// Handles token storage, retrieval, and refresh for PKCE OAuth 2.0 flow.
/// </summary>
public class AspNetCoreTokenProvider : ITokenProvider
{
    private const string SessionIdPropertyKey = "sf_session_id";
    private const string RefreshCoordinatorCacheKeyPrefix = "SalesforceCore:TokenRefresh:";
    private static readonly TimeSpan RefreshCoordinatorTtl = TimeSpan.FromMinutes(2);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache? _distributedCache;
    private readonly IDistributedLockProvider? _distributedLockProvider;
    private readonly IDataProtector? _tokenProtector;
    private readonly SalesforceOptions _options;
    private readonly ILogger<AspNetCoreTokenProvider> _logger;

    /// <summary>
    /// Semaphore to prevent concurrent token refresh operations.
    /// This ensures that when multiple requests hit 401 simultaneously,
    /// only one refresh operation occurs rather than multiple parallel refreshes.
    /// </summary>
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Creates a new AspNetCoreTokenProvider.
    /// </summary>
    public AspNetCoreTokenProvider(
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        IOptions<SalesforceOptions> options,
        ILogger<AspNetCoreTokenProvider> logger,
        IDistributedCache? distributedCache = null,
        IDistributedLockProvider? distributedLockProvider = null,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _distributedCache = distributedCache;
        _distributedLockProvider = distributedLockProvider;
        _tokenProtector = dataProtectionProvider?.CreateProtector($"{nameof(AspNetCoreTokenProvider)}:TokenRefreshCoordinator:v1");
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        try
        {
            var token = await HttpContext.GetTokenAsync("access_token");
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get access token");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        try
        {
            // PRIORITY 1: Get from authentication properties (updated during token refresh)
            // This is the most reliable source as it's updated when tokens are refreshed
            // and handles org migrations (e.g., na1.salesforce.com -> na2.salesforce.com)
            var authenticateResult = await HttpContext.AuthenticateAsync();
            if (authenticateResult?.Properties?.Items != null &&
                authenticateResult.Properties.Items.TryGetValue("instance_url", out var instanceUrl) &&
                !string.IsNullOrEmpty(instanceUrl))
            {
                return instanceUrl;
            }

            // PRIORITY 2: Try to get from tokens stored in authentication properties
            // Salesforce may return instance_url as a token
            var instanceUrlToken = await HttpContext.GetTokenAsync("instance_url");
            if (!string.IsNullOrEmpty(instanceUrlToken))
            {
                return instanceUrlToken;
            }

            // PRIORITY 3: Fall back to claim (may be stale if org was migrated)
            // Claims are read-only during a request and only updated on next login
            var instanceUrlClaim = HttpContext.User.FindFirst(SalesforceConstants.Claims.InstanceUrl);
            if (instanceUrlClaim != null)
            {
                _logger.LogDebug("Using instance URL from claim (may be stale if org was migrated)");
                return instanceUrlClaim.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get instance URL");
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task RevokeTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext == null)
        {
            return;
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            var instanceUrl = await GetInstanceUrlAsync(cancellationToken);

            if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(instanceUrl))
            {
                // Call Salesforce revoke endpoint using IHttpClientFactory for proper socket management
                var httpClient = _httpClientFactory.CreateClient("SalesforceTokenRevoke");
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("token", accessToken)
                });

                await httpClient.PostAsync($"{instanceUrl}{SalesforceConstants.Paths.OAuthRevoke}", content, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke token");
        }

        // Sign out locally
        await HttpContext.SignOutAsync();
    }

    /// <inheritdoc/>
    public async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext == null)
        {
            _logger.LogDebug("Cannot refresh token: HttpContext is null");
            return null;
        }

        // Use semaphore to prevent concurrent refresh operations
        // This prevents multiple parallel 401 responses from triggering multiple refresh attempts
        var lockAcquired = await _refreshLock.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (!lockAcquired)
        {
            _logger.LogWarning("Could not acquire refresh lock within timeout - another refresh may be in progress");
            // Try to get the current token which may have been refreshed by another request
            return await GetAccessTokenAsync(cancellationToken);
        }

        try
        {
            var authenticateResult = await HttpContext.AuthenticateAsync();
            var refreshSessionKey = GetRefreshSessionKey(authenticateResult) ?? GetFallbackRefreshSessionKey();

            // Cross-server serialization (optional): if no provider is registered, we fall back to process-only locking.
            await using var distributedLock = _distributedLockProvider == null || string.IsNullOrWhiteSpace(refreshSessionKey)
                ? null
                : await _distributedLockProvider.TryAcquireAsync(
                    resourceName: $"sf_token_refresh:{refreshSessionKey}",
                    timeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cancellationToken);

            if (_distributedLockProvider != null && distributedLock == null && !string.IsNullOrWhiteSpace(refreshSessionKey))
            {
                _logger.LogWarning("Could not acquire distributed token refresh lock within timeout. SessionKey={SessionKey}", refreshSessionKey);

                // Another server may have completed refresh; try to read the latest snapshot from the coordinator cache.
                var snapshot = await TryGetRefreshSnapshotAsync(refreshSessionKey, cancellationToken);
                if (snapshot != null && authenticateResult?.Principal != null && authenticateResult.Properties != null)
                {
                    await ApplyRefreshSnapshotAsync(authenticateResult, snapshot, cancellationToken);
                    return snapshot.AccessToken;
                }

                return await GetAccessTokenAsync(cancellationToken);
            }

            // If another server refreshed while we were waiting, use that token instead of refreshing again.
            if (!string.IsNullOrWhiteSpace(refreshSessionKey))
            {
                var snapshot = await TryGetRefreshSnapshotAsync(refreshSessionKey, cancellationToken);
                if (snapshot != null && authenticateResult?.Principal != null && authenticateResult.Properties != null)
                {
                    await ApplyRefreshSnapshotAsync(authenticateResult, snapshot, cancellationToken);
                    return snapshot.AccessToken;
                }
            }

            var refreshToken = await HttpContext.GetTokenAsync("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogDebug("Cannot refresh token: No refresh token available");
                return null;
            }

            _logger.LogDebug("Attempting to refresh access token");

            // Call Salesforce token endpoint with grant_type=refresh_token
            var httpClient = _httpClientFactory.CreateClient("SalesforceTokenRefresh");
            var tokenEndpoint = $"{_options.Domain.TrimEnd('/')}{SalesforceConstants.Paths.OAuthToken}";

            var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = SalesforceConstants.GrantTypes.RefreshToken,
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId
            });

            var response = await httpClient.PostAsync(tokenEndpoint, requestContent, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed: {StatusCode} - {Response}",
                    response.StatusCode, responseContent);
                return null;
            }

            // Parse the token response
            var tokenResponse = JsonSerializer.Deserialize<TokenRefreshResponse>(responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogWarning("Token refresh returned invalid response");
                return null;
            }

            _logger.LogDebug("Token refresh successful, updating authentication properties");

            // Update the authentication properties with new tokens
            if (authenticateResult?.Principal == null || authenticateResult.Properties == null)
            {
                _logger.LogWarning("Cannot update tokens: No authentication result available");
                return tokenResponse.AccessToken;
            }

            var properties = authenticateResult.Properties;

            // Update tokens in the authentication properties
            properties.UpdateTokenValue("access_token", tokenResponse.AccessToken);

            // Update refresh token if a new one was provided
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                properties.UpdateTokenValue("refresh_token", tokenResponse.RefreshToken);
            }

            // Update instance URL if provided
            if (!string.IsNullOrEmpty(tokenResponse.InstanceUrl))
            {
                properties.Items["instance_url"] = tokenResponse.InstanceUrl;
                properties.UpdateTokenValue("instance_url", tokenResponse.InstanceUrl);
            }

            // Calculate token expiration if provided
            if (tokenResponse.IssuedAt.HasValue)
            {
                var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(tokenResponse.IssuedAt.Value);
                properties.IssuedUtc = issuedAt;
            }

            // Re-sign in to persist the updated tokens to the cookie
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authenticateResult.Principal,
                properties);

            // Publish the refreshed token to the coordinator cache so other servers handling
            // concurrent in-flight requests can pick it up without attempting a second refresh.
            if (!string.IsNullOrWhiteSpace(refreshSessionKey))
            {
                var snapshot = new RefreshSnapshot(
                    AccessToken: tokenResponse.AccessToken,
                    RefreshToken: tokenResponse.RefreshToken,
                    InstanceUrl: tokenResponse.InstanceUrl);

                await TrySetRefreshSnapshotAsync(refreshSessionKey, snapshot, cancellationToken);
            }

            _logger.LogInformation("Access token refreshed successfully");
            return tokenResponse.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh token");
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private string? GetRefreshSessionKey(AuthenticateResult? authenticateResult)
    {
        if (authenticateResult?.Properties?.Items == null)
        {
            return null;
        }

        if (authenticateResult.Properties.Items.TryGetValue(SessionIdPropertyKey, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId;
        }

        return null;
    }

    private string? GetFallbackRefreshSessionKey()
    {
        var user = HttpContext?.User;
        if (user == null)
        {
            return null;
        }

        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name;
    }

    private async Task<RefreshSnapshot?> TryGetRefreshSnapshotAsync(string sessionKey, CancellationToken cancellationToken)
    {
        if (_distributedCache == null || !_options.EnableServerSideTokenRefreshCoordinator)
        {
            return null;
        }

        try
        {
            var bytes = await _distributedCache.GetAsync(GetRefreshCoordinatorCacheKey(sessionKey), cancellationToken);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            if (_tokenProtector != null)
            {
                bytes = _tokenProtector.Unprotect(bytes);
            }

            return JsonSerializer.Deserialize<RefreshSnapshot>(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read refresh coordinator snapshot");
            return null;
        }
    }

    private async Task TrySetRefreshSnapshotAsync(string sessionKey, RefreshSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_distributedCache == null || !_options.EnableServerSideTokenRefreshCoordinator)
        {
            return;
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            if (_tokenProtector != null)
            {
                bytes = _tokenProtector.Protect(bytes);
            }

            await _distributedCache.SetAsync(
                GetRefreshCoordinatorCacheKey(sessionKey),
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = RefreshCoordinatorTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish refresh coordinator snapshot");
        }
    }

    private async Task ApplyRefreshSnapshotAsync(
        AuthenticateResult authenticateResult,
        RefreshSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (HttpContext == null || authenticateResult.Properties == null || authenticateResult.Principal == null)
        {
            return;
        }

        var properties = authenticateResult.Properties;
        properties.UpdateTokenValue("access_token", snapshot.AccessToken);

        if (!string.IsNullOrWhiteSpace(snapshot.RefreshToken))
        {
            properties.UpdateTokenValue("refresh_token", snapshot.RefreshToken);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.InstanceUrl))
        {
            properties.Items["instance_url"] = snapshot.InstanceUrl;
            properties.UpdateTokenValue("instance_url", snapshot.InstanceUrl);
        }

        // Persist to the auth ticket so subsequent operations in this request (and later requests)
        // see the refreshed token values.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            authenticateResult.Principal,
            properties);
    }

    private static string GetRefreshCoordinatorCacheKey(string sessionKey)
        => $"{RefreshCoordinatorCacheKeyPrefix}{sessionKey}";

    private sealed record RefreshSnapshot(
        string AccessToken,
        string? RefreshToken,
        string? InstanceUrl);

    /// <summary>
    /// Response from Salesforce token refresh endpoint.
    /// Salesforce returns snake_case JSON properties, so we use JsonPropertyName attributes
    /// for reliable deserialization with both case-sensitive and case-insensitive settings.
    /// </summary>
    private class TokenRefreshResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("instance_url")]
        public string? InstanceUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("issued_at")]
        public long? IssuedAt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    /// <inheritdoc/>
    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var isAuthenticated = HttpContext?.User?.Identity?.IsAuthenticated == true;
        return Task.FromResult(isAuthenticated);
    }
}
