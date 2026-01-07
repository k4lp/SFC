using System.Collections.Concurrent;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Service for handling synchronization locks using striped locking to prevent memory leaks.
/// </summary>
public interface ISynchronizationService
{
    /// <summary>
    /// gets a semaphore for a specific key.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>A semaphore slim instance.</returns>
    SemaphoreSlim GetLock(string key);
}

/// <summary>
/// Implementation of synchronization service using striped locking.
/// </summary>
public class SynchronizationService : ISynchronizationService, IDisposable
{
    // Use a fixed number of locks (striping) to avoid unbounded memory growth.
    // 1024 locks is sufficient for most concurrency scenarios without significant collision.
    private const int StripeCount = 1024;
    private readonly SemaphoreSlim[] _locks;
    private bool _disposed;

    public SynchronizationService()
    {
        _locks = new SemaphoreSlim[StripeCount];
        for (int i = 0; i < StripeCount; i++)
        {
            _locks[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <inheritdoc/>
    public SemaphoreSlim GetLock(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SynchronizationService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

        // Consistent hashing to map key to a lock index
        // Using unsigned int cast to handle negative hash codes
        uint hash = (uint)key.GetHashCode();
        uint index = hash % StripeCount;

        return _locks[index];
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var semaphore in _locks)
        {
            semaphore.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
