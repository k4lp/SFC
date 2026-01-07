using Microsoft.AspNetCore.Http;
using System.Globalization;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;

namespace SalesforceCore.AspNetCore.TokenProviders;

/// <summary>
/// Token provider that stores Salesforce tokens in ASP.NET Core session.
/// Use this for simple deployments where server-side session storage is acceptable.
/// </summary>
/// <remarks>
/// Before using this provider, ensure session is configured:
/// <code>
/// builder.Services.AddDistributedMemoryCache(); // or Redis for production
/// builder.Services.AddSession(options =>
/// {
///     options.IdleTimeout = TimeSpan.FromHours(8);
///     options.Cookie.HttpOnly = true;
///     options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
/// });
///
/// // Then in middleware:
/// app.UseSession();
/// </code>
///
/// Register the provider:
/// <code>
/// builder.Services.AddSalesforceSessionTokenStorage();
/// </code>
/// </remarks>
public class SessionTokenProvider : ITokenProvider
{
    private const string AccessTokenKey = "sf_access_token";
    private const string InstanceUrlKey = "sf_instance_url";
    private const string RefreshTokenKey = "sf_refresh_token";
    private const string TokenExpiryKey = "sf_token_expiry";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Creates a new SessionTokenProvider.
    /// </summary>
    public SessionTokenProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    private ISession? Session => _httpContextAccessor.HttpContext?.Session;

    /// <inheritdoc/>
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = Session?.GetString(AccessTokenKey);
        return Task.FromResult(token);
    }

    /// <inheritdoc/>
    public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
    {
        var url = Session?.GetString(InstanceUrlKey);
        return Task.FromResult(url);
    }

    /// <summary>
    /// Gets the refresh token from session.
    /// </summary>
    public Task<string?> GetRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = Session?.GetString(RefreshTokenKey);
        return Task.FromResult(token);
    }

    /// <summary>
    /// Gets the token expiry time from session.
    /// </summary>
    public Task<DateTime?> GetTokenExpiryAsync(CancellationToken cancellationToken = default)
    {
        var expiryStr = Session?.GetString(TokenExpiryKey);
        if (string.IsNullOrEmpty(expiryStr))
            return Task.FromResult<DateTime?>(null);

        if (DateTime.TryParse(expiryStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry))
            return Task.FromResult<DateTime?>(expiry);

        return Task.FromResult<DateTime?>(null);
    }

    /// <summary>
    /// Stores tokens in session.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="refreshToken">The refresh token (optional).</param>
    /// <param name="expiresIn">Token expiration in seconds (optional).</param>
    public void SetTokens(
        string accessToken,
        string instanceUrl,
        string? refreshToken = null,
        int? expiresIn = null)
    {
        var session = Session;
        if (session == null)
            throw new InvalidOperationException("HTTP session is not available. Ensure session middleware is configured.");

        session.SetString(AccessTokenKey, accessToken);
        session.SetString(InstanceUrlKey, instanceUrl);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            session.SetString(RefreshTokenKey, refreshToken);
        }

        if (expiresIn.HasValue)
        {
            var expiry = DateTime.UtcNow.AddSeconds(expiresIn.Value);
            session.SetString(TokenExpiryKey, expiry.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Clears all tokens from session (logout).
    /// </summary>
    public void ClearTokens()
    {
        var session = Session;
        session?.Remove(AccessTokenKey);
        session?.Remove(InstanceUrlKey);
        session?.Remove(RefreshTokenKey);
        session?.Remove(TokenExpiryKey);
    }

    /// <summary>
    /// Checks if the current token is expired or about to expire.
    /// </summary>
    /// <param name="bufferMinutes">Buffer time before actual expiry to consider token expired.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsTokenExpiredAsync(int bufferMinutes = 5, CancellationToken cancellationToken = default)
    {
        var expiry = await GetTokenExpiryAsync(cancellationToken);
        if (!expiry.HasValue)
            return true; // No expiry info, assume expired

        return DateTime.UtcNow.AddMinutes(bufferMinutes) >= expiry.Value;
    }

    /// <inheritdoc/>
    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var hasToken = !string.IsNullOrEmpty(Session?.GetString(AccessTokenKey));
        var hasInstanceUrl = !string.IsNullOrEmpty(Session?.GetString(InstanceUrlKey));
        return Task.FromResult(hasToken && hasInstanceUrl);
    }

    /// <inheritdoc/>
    public Task RevokeTokenAsync(CancellationToken cancellationToken = default)
    {
        ClearTokens();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Session-based token provider does not support automatic token refresh.
    /// When a token expires, the user must re-authenticate through the normal login flow.
    /// This method throws SalesforceAuthException to signal that re-authentication is required.
    /// </remarks>
    /// <exception cref="SalesforceAuthException">Always thrown to indicate re-authentication is required.</exception>
    public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        // Session-based token provider does not support automatic refresh.
        // The SalesforceClient's retry logic calls this method expecting a fresh token.
        // Throwing an exception ensures proper handling and forces re-authentication
        // rather than silently returning a potentially expired token.
        throw new SalesforceAuthException(
            "Session-based authentication does not support automatic token refresh. Please log in again.",
            tokenExpired: true,
            requiresReauth: true);
    }
}
