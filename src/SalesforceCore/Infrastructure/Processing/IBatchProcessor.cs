namespace SalesforceCore.Infrastructure.Processing;

/// <summary>
/// Minimal abstraction for enqueueing items into an asynchronous batch processor.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public interface IBatchProcessor<in T>
{
    /// <summary>
    /// Attempts to enqueue an item for batched background processing.
    /// </summary>
    /// <param name="item">Item to enqueue.</param>
    /// <returns>True if the item was accepted for processing; false if it could not be enqueued.</returns>
    bool TryEnqueue(T item);
}

