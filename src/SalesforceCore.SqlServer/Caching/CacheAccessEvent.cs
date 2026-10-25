namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// Represents a cache access event that can be batched and flushed asynchronously.
/// </summary>
/// <param name="EntryId">Cache entry identifier (including any configured key prefix).</param>
/// <param name="AccessedAt">Timestamp of the access (UTC recommended).</param>
/// <param name="AccessCountDelta">Increment to apply to the access counter.</param>
/// <param name="NewExpiresAtTime">Optional new expiry time (for sliding expiration extension).</param>
public readonly record struct CacheAccessEvent(
    string EntryId,
    DateTimeOffset AccessedAt,
    long AccessCountDelta,
    DateTimeOffset? NewExpiresAtTime);
