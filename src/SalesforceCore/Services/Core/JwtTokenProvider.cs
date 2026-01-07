using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Security;
using SalesforceCore.Services.Caching;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Token provider for server-to-server authentication using JWT Bearer Flow.
/// Use this for background jobs, workers, and service accounts that don't have user interaction.
/// </summary>
public class JwtTokenProvider : ITokenProvider
{
    private readonly JwtTokenProviderOptions _jwtOptions;
    private readonly SalesforceOptions _salesforceOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheProvider _cacheProvider;
    private readonly ISynchronizationService _synchronizationService;
    private readonly ILogger<JwtTokenProvider> _logger;

    private const string CacheKey = "sf_jwt_token";
    private readonly TimeSpan _expiryBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new JwtTokenProvider.
    /// </summary>
    public JwtTokenProvider(
        IOptions<JwtTokenProviderOptions> jwtOptions,
        IOptions<SalesforceOptions> salesforceOptions,
        IHttpClientFactory httpClientFactory,
        ICacheProvider cacheProvider,
        ISynchronizationService synchronizationService,
        ILogger<JwtTokenProvider> logger)
    {
        _jwtOptions = jwtOptions.Value;
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

        // Proactive refresh if expired or close to expiry
        if (token == null || token.IsExpired(_expiryBuffer))
        {
            _logger.LogDebug("JWT token missing or expiring soon. Initiating refresh.");
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
        // Use synchronization service to prevent thundering herd
        var semaphore = _synchronizationService.GetLock(CacheKey);
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            // Double-check cache after acquiring lock
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
            var httpClient = _httpClientFactory.CreateClient("SalesforceJwt");
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token.AccessToken
            });

            await httpClient.PostAsync($"{token.InstanceUrl}{SalesforceConstants.Paths.OAuthRevoke}", content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke JWT token");
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
        _logger.LogDebug("Authenticating with JWT Bearer Flow");

        var assertion = CreateJwtAssertion();
        var httpClient = _httpClientFactory.CreateClient("SalesforceJwt");
        var tokenEndpoint = $"{_salesforceOptions.Domain.TrimEnd('/')}{SalesforceConstants.Paths.OAuthToken}";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = SalesforceConstants.GrantTypes.JwtBearer,
            ["assertion"] = assertion
        });

        var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("JWT authentication failed: {StatusCode} - {Response}", response.StatusCode, responseContent);
            throw new SalesforceAuthException($"JWT authentication failed: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JwtTokenResponse>(responseContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new SalesforceAuthException("JWT authentication returned invalid response");
        }

        // Salesforce JWT flow usually returns an access token but NO refresh token and NO explicit expiry in seconds.
        // It relies on the session timeout configured in Salesforce (usually 15m to 24h).
        // We will assume a safe default of 1 hour if not specified, but this should be configured to match the Connected App policy.
        // Or better, we can introspect if we want, but for now we use a configuration or default.
        // Ideally _jwtOptions should have a TokenLifetime property, but we'll default to 1 hour to match previous behavior but safer.

        var expiry = DateTime.UtcNow.Add(_jwtOptions.TokenExpiration);

        var cachedToken = new CachedToken
        {
            AccessToken = tokenResponse.AccessToken,
            InstanceUrl = tokenResponse.InstanceUrl ?? string.Empty,
            ExpiresAt = expiry,
            Scopes = tokenResponse.Scope
        };

        // Cache the token
        await _cacheProvider.SetAsync(CacheKey, cachedToken, _jwtOptions.TokenExpiration, cancellationToken);

        _logger.LogInformation("JWT authentication successful, instance URL: {InstanceUrl}. Expires at: {ExpiresAt}",
            cachedToken.InstanceUrl, cachedToken.ExpiresAt);

        return cachedToken.AccessToken;
    }

    private string CreateJwtAssertion()
    {
        if (string.IsNullOrEmpty(_jwtOptions.PrivateKey) && string.IsNullOrEmpty(_jwtOptions.PrivateKeyPath))
        {
            throw new InvalidOperationException("JWT private key or private key path must be configured");
        }

        if (string.IsNullOrEmpty(_jwtOptions.Username))
        {
            throw new InvalidOperationException("JWT username must be configured");
        }

        var now = DateTime.UtcNow;
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new Claim("iss", _salesforceOptions.ClientId),
            new Claim("sub", _jwtOptions.Username),
            new Claim("aud", _jwtOptions.Audience ?? _salesforceOptions.Domain)
        };

        var privateKey = GetPrivateKey();

        var credentials = new SigningCredentials(
            new RsaSecurityKey(privateKey),
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _salesforceOptions.ClientId,
            audience: _jwtOptions.Audience ?? _salesforceOptions.Domain,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(3), // JWT assertion valid for 3 minutes
            signingCredentials: credentials);

        return tokenHandler.WriteToken(token);
    }

    private RSA GetPrivateKey()
    {
        string keyContent;

        if (!string.IsNullOrEmpty(_jwtOptions.PrivateKey))
        {
            keyContent = _jwtOptions.PrivateKey;
        }
        else if (!string.IsNullOrEmpty(_jwtOptions.PrivateKeyPath))
        {
            keyContent = File.ReadAllText(_jwtOptions.PrivateKeyPath);
        }
        else
        {
            throw new InvalidOperationException("Private key not configured");
        }

        var rsa = RSA.Create();

        if (keyContent.Contains("-----BEGIN"))
        {
            rsa.ImportFromPem(keyContent.AsSpan());
        }
        else
        {
            var keyBytes = Convert.FromBase64String(keyContent);
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        }

        return rsa;
    }

    private class JwtTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("instance_url")]
        public string? InstanceUrl { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}

/// <summary>
/// Configuration options for JWT Bearer Flow authentication.
/// </summary>
public class JwtTokenProviderOptions
{
    public const string SectionName = SalesforceConstants.ConfigKeys.JwtSection;

    public string Username { get; set; } = string.Empty;
    public string? PrivateKey { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? Audience { get; set; }

    /// <summary>
    /// Expected lifetime of the access token.
    /// Should match the Session Timeout setting in Salesforce (e.g. 15 minutes, 2 hours).
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan TokenExpiration { get; set; } = TimeSpan.FromHours(1);
}
