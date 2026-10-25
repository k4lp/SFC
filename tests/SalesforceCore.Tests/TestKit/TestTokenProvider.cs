using SalesforceCore.Services.Core;

namespace SalesforceCore.Tests.TestKit;

/// <summary>Deterministic token provider for tests. Each refresh issues <c>tok-{n+1}</c>.</summary>
public sealed class TestTokenProvider : ITokenProvider
{
    private int _refreshes;

    /// <summary>Current access token (null = not authenticated).</summary>
    public string? AccessToken { get; set; } = "tok-1";

    /// <summary>Instance URL (null = unknown until refresh).</summary>
    public string? InstanceUrl { get; set; } = FakeSalesforceServer.InstanceUrl;

    /// <summary>When false, refresh fails (returns null).</summary>
    public bool CanRefresh { get; set; } = true;

    /// <summary>Number of refreshes performed.</summary>
    public int RefreshCount => _refreshes;

    /// <summary>Number of revocations.</summary>
    public int RevokeCount { get; private set; }

    /// <inheritdoc/>
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(AccessToken);
    /// <inheritdoc/>
    public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default) => Task.FromResult(InstanceUrl);
    /// <inheritdoc/>
    public Task RevokeTokenAsync(CancellationToken cancellationToken = default) { RevokeCount++; AccessToken = null; return Task.CompletedTask; }
    /// <inheritdoc/>
    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(AccessToken != null);

    /// <inheritdoc/>
    public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRefresh) return Task.FromResult<string?>(null);
        var n = Interlocked.Increment(ref _refreshes);
        AccessToken = $"tok-{n + 1}";
        InstanceUrl ??= FakeSalesforceServer.InstanceUrl;
        return Task.FromResult<string?>(AccessToken);
    }
}
