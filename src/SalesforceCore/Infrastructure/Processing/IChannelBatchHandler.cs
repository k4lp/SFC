namespace SalesforceCore.Infrastructure.Processing;

/// <summary>
/// Handles a flushed batch from <see cref="ChannelBatchProcessor{T}"/>.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public interface IChannelBatchHandler<T>
{
    /// <summary>
    /// Handles a batch of items.
    /// </summary>
    Task HandleBatchAsync(IReadOnlyList<T> items, CancellationToken cancellationToken);
}

