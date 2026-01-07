namespace SalesforceCore.Infrastructure.Processing;

public sealed class ChannelBatchProcessorOptions
{
    /// <summary>
    /// Maximum number of queued items.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of items processed in a single batch flush.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Maximum time to wait before flushing a non-empty batch.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When a batch handler fails, wait this long before continuing to avoid tight error loops.
    /// </summary>
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(1);
}

