namespace SalesforceCore.Models.Security;

/// <summary>
/// Represents a cached Salesforce authentication token.
/// Used for sharing token state across multiple provider instances via distributed cache.
/// </summary>
public class CachedToken
{
    /// <summary>
    /// The OAuth 2.0 access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The Salesforce instance URL (e.g., https://na1.salesforce.com).
    /// </summary>
    public string InstanceUrl { get; set; } = string.Empty;

    /// <summary>
    /// The refresh token, if available.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// The exact UTC time when this token expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The scopes granted to this token.
    /// </summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// Checks if the token is expired or close to expiring.
    /// </summary>
    /// <param name="buffer">Time buffer to consider token expired before actual expiry.</param>
    /// <returns>True if expired or expiring soon.</returns>
    public bool IsExpired(TimeSpan buffer)
    {
        return DateTime.UtcNow.Add(buffer) >= ExpiresAt;
    }
}
