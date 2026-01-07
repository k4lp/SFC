namespace SalesforceCore.Infrastructure.Locking;

/// <summary>
/// Represents an acquired distributed lock. Disposing the handle releases the lock.
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>
    /// Logical resource name this lock protects.
    /// </summary>
    string ResourceName { get; }
}

