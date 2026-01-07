namespace SalesforceCore.Models.Configuration;

/// <summary>
/// Controls write-behind buffering for the encrypted SQL Server cache.
/// </summary>
/// <remarks>
/// The SQL cache updates access tracking and sliding expiration metadata. Performing those
/// updates synchronously on every read can create a write-on-read self-DoS under load.
/// These settings enable batching and delayed persistence while preserving correctness.
/// </remarks>
public sealed class SqlCacheWriteBehindOptions
{
    /// <summary>
    /// Enables write-behind buffering for read-path metadata updates (access count, last accessed,
    /// and sliding expiration refresh).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of queued access events before backpressure/overflow behavior kicks in.
    /// </summary>
    public int Capacity { get; set; } = 50_000;

    /// <summary>
    /// Maximum number of distinct cache keys updated in a single database batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Maximum time to wait before flushing buffered access updates to SQL Server.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Minimum extension (in seconds) required before persisting a sliding expiration refresh.
    /// This reduces write amplification for frequently accessed keys.
    /// </summary>
    public int SlidingExpirationRefreshThresholdSeconds { get; set; } = 60;

    /// <summary>
    /// Grace period for the cleanup job to avoid deleting entries that were accessed recently
    /// but whose sliding expiration refresh has not yet been flushed.
    /// </summary>
    public TimeSpan CleanupGracePeriod { get; set; } = TimeSpan.FromSeconds(10);
}

