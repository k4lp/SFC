using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Security;
using SalesforceCore.Services.Caching;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Token provider using OAuth 2.0 Client Credentials flow for server-to-server authentication.
/// </summary>
public class ClientCredentialsTokenProvider : ITokenProvider
{
    private readonly ClientCredentialsOptions _options;
    private readonly SalesforceOptions _salesforceOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheProvider _cacheProvider;
    private readonly ISynchronizationService _synchronizationService;
    private readonly ILogger<ClientCredentialsTokenProvider> _logger;

    private const string CacheKey = "sf_client_cred_token";
    private readonly TimeSpan _expiryBuffer = TimeSpan.FromMinutes(5);
    
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new ClientCredentialsTokenProvider.
    /// </summary>
    public ClientCredentialsTokenProvider(
        IOptions<ClientCredentialsOptions> options,
        IOptions<SalesforceOptions> salesforceOptions,
        IHttpClientFactory httpClientFactory,
        ICacheProvider cacheProvider,
        ISynchronizationService synchronizationService,
        ILogger<ClientCredentialsTokenProvider> logger)
    {
        _options = options.Value;
        _salesforceOptions = salesforceOptions.Value;
        _httpClientFactory = httpClientFactory;
        _cacheProvider = cacheProvider;
        _synchronizationService = synchronizationService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _cacheProvider.GetAsync<CachedToken>(CacheKey, cancellationToken);

        if (token == null || token.IsExpired(_expiryBuffer))
        {
            _logger.LogDebug("Client Credentials token missing or expiring soon. Initiating refresh.");
            return await RefreshTokenAsync(cancellationToken);
        }

        return token.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
    {
        var token = await _cacheProvider.GetAsync<CachedToken>(CacheKey, cancellationToken);
        return token?.InstanceUrl;
    }

    /// <inheritdoc/>
    public async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var semaphore = _synchronizationService.GetLock(CacheKey);
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var cachedToken = await _cacheProvider.GetAsync<CachedToken>(CacheKey, cancellationToken);
            if (cachedToken != null && !cachedToken.IsExpired(_expiryBuffer))
            {
                _logger.LogDebug("Token already refreshed by another thread.");
                return cachedToken.AccessToken;
            }

            return await AuthenticateAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RevokeTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _cacheProvider.GetAsync<CachedToken>(CacheKey, cancellationToken);
        if (token == null) return;

        try
        {
            var httpClient = _httpClientFactory.CreateClient("SalesforceClientCredentials");
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token.AccessToken
            });

            await httpClient.PostAsync($"{token.InstanceUrl}{SalesforceConstants.Paths.OAuthRevoke}", content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke access token");
        }
        finally
        {
            await _cacheProvider.RemoveAsync(CacheKey, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var token = await _cacheProvider.GetAsync<CachedToken>(CacheKey, cancellationToken);
        return token != null && !token.IsExpired(TimeSpan.Zero);
    }

    private async Task<string> AuthenticateAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Authenticating with Client Credentials Flow");

        var clientId = _options.ClientId ?? _salesforceOptions.ClientId;
        var clientSecret = _options.ClientSecret ?? _salesforceOptions.ClientSecret;

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Client ID and Client Secret must be configured for Client Credentials flow");
        }

        var httpClient = _httpClientFactory.CreateClient("SalesforceClientCredentials");
        var tokenEndpoint = $"{_salesforceOptions.Domain.TrimEnd('/')}{SalesforceConstants.Paths.OAuthToken}";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Client Credentials authentication failed: {StatusCode} - {Response}", response.StatusCode, responseContent);
            throw new SalesforceAuthException($"Client Credentials authentication failed: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<ClientCredentialsTokenResponse>(responseContent, _jsonSerializerOptions);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new SalesforceAuthException("Client Credentials authentication returned invalid response");
        }

        // Calculate expiry
        // Salesforce usually returns expires_in for this flow
        var expiresInSeconds = tokenResponse.ExpiresIn ?? 7200; // Default to 2 hours if missing
        var expiry = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        var cachedToken = new CachedToken
        {
            AccessToken = tokenResponse.AccessToken,
            InstanceUrl = tokenResponse.InstanceUrl ?? string.Empty,
            ExpiresAt = expiry
        };

        // Cache the token
        await _cacheProvider.SetAsync(CacheKey, cachedToken, TimeSpan.FromSeconds(expiresInSeconds), cancellationToken);

        _logger.LogInformation("Client Credentials authentication successful, instance URL: {InstanceUrl}. Expires in {Seconds}s",
            cachedToken.InstanceUrl, expiresInSeconds);

        return cachedToken.AccessToken;
    }

    private class ClientCredentialsTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("instance_url")]
        public string? InstanceUrl { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}

/// <summary>
/// Configuration options for OAuth 2.0 Client Credentials flow.
/// </summary>
public class ClientCredentialsOptions
{
    public const string SectionName = "SalesforceClientCredentials";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
