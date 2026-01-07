namespace SalesforceCore.Infrastructure.Locking;

/// <summary>
/// Abstraction for acquiring locks that coordinate work across processes and servers.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire an exclusive lock for a resource within the specified timeout.
    /// </summary>
    /// <param name="resourceName">Resource name (must be stable across servers for correct coordination).</param>
    /// <param name="timeout">Maximum time to wait for lock acquisition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A lock handle if acquired; otherwise <c>null</c> (timeout/no acquisition).
    /// </returns>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

