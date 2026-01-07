using SalesforceCore.Services.Core;

namespace SalesforceCore.ModelGenerator;

/// <summary>
/// Token provider that uses a static access token provided at construction.
/// Designed for CLI tools where the token is passed as an argument or environment variable.
/// </summary>
public sealed class StaticTokenProvider : ITokenProvider
{
    private readonly string _accessToken;
    private readonly string _instanceUrl;

    /// <summary>
    /// Creates a new StaticTokenProvider.
    /// </summary>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    public StaticTokenProvider(string accessToken, string instanceUrl)
    {
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        _instanceUrl = instanceUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(instanceUrl));
    }

    /// <inheritdoc/>
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(_accessToken);

    /// <inheritdoc/>
    public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(_instanceUrl);

    /// <inheritdoc/>
    public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(_accessToken); // CLI tokens don't refresh

    /// <inheritdoc/>
    public Task RevokeTokenAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask; // No-op for static tokens

    /// <inheritdoc/>
    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(!string.IsNullOrEmpty(_accessToken));
}
