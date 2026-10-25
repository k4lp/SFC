using System.Globalization;

namespace SalesforceCore.Http;

/// <summary>
/// Latest org API usage reported by Salesforce in the <c>Sforce-Limit-Info: api-usage=used/max</c> response header.
/// Updated by <see cref="SalesforceApiUsageHandler"/> on every API response at zero extra API cost.
/// </summary>
public interface ISalesforceApiUsage
{
    /// <summary>API requests used in the rolling 24h window (null until the first response).</summary>
    long? Used { get; }

    /// <summary>API request allocation for the rolling 24h window.</summary>
    long? Max { get; }

    /// <summary>Remaining requests, if known.</summary>
    long? Remaining { get; }

    /// <summary>Fraction used (0..1), if known.</summary>
    double? UsageRatio { get; }

    /// <summary>When the values were last updated (UTC).</summary>
    DateTimeOffset? LastUpdated { get; }

    /// <summary>Records a header value. Returns false if it could not be parsed.</summary>
    bool Update(string headerValue, DateTimeOffset? at = null);
}

/// <summary>Thread-safe default <see cref="ISalesforceApiUsage"/>.</summary>
public sealed class SalesforceApiUsage : ISalesforceApiUsage
{
    private readonly object _gate = new();
    private long? _used, _max;
    private DateTimeOffset? _at;

    /// <inheritdoc/>
    public long? Used { get { lock (_gate) return _used; } }
    /// <inheritdoc/>
    public long? Max { get { lock (_gate) return _max; } }
    /// <inheritdoc/>
    public long? Remaining { get { lock (_gate) return _used.HasValue && _max.HasValue ? Math.Max(0, _max.Value - _used.Value) : null; } }
    /// <inheritdoc/>
    public double? UsageRatio { get { lock (_gate) return _used.HasValue && _max is > 0 ? (double)_used.Value / _max.Value : null; } }
    /// <inheritdoc/>
    public DateTimeOffset? LastUpdated { get { lock (_gate) return _at; } }

    /// <inheritdoc/>
    public bool Update(string headerValue, DateTimeOffset? at = null)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return false;
        foreach (var part in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.StartsWith("api-usage=", StringComparison.OrdinalIgnoreCase)) continue;
            var nums = part["api-usage=".Length..].Split('/');
            if (nums.Length == 2
                && long.TryParse(nums[0], NumberStyles.None, CultureInfo.InvariantCulture, out var used)
                && long.TryParse(nums[1], NumberStyles.None, CultureInfo.InvariantCulture, out var max))
            {
                lock (_gate) { _used = used; _max = max; _at = at ?? DateTimeOffset.UtcNow; }
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Pipeline handler that records <c>Sforce-Limit-Info</c> into <see cref="ISalesforceApiUsage"/>.
/// </summary>
public sealed class SalesforceApiUsageHandler : DelegatingHandler
{
    private readonly ISalesforceApiUsage _usage;

    /// <summary>Creates the handler.</summary>
    public SalesforceApiUsageHandler(ISalesforceApiUsage usage) => _usage = usage ?? throw new ArgumentNullException(nameof(usage));

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Headers.TryGetValues("Sforce-Limit-Info", out var values))
        {
            foreach (var v in values) _usage.Update(v);
        }
        return response;
    }
}
