namespace SalesforceCore.Services.Caching;

/// <summary>
/// One async lock per cache key, created on demand and removed when the last holder/waiter leaves.
/// </summary>
/// <remarks>
/// Replaces the 2.x striped locks (32 semaphores selected by <c>key.GetHashCode()</c>). Striping deadlocked when a
/// <c>GetOrCreateAsync</c> factory called <c>GetOrCreateAsync</c> for another key on the same stripe (layout and
/// permission factories load describes through the same provider): the second wait could never complete. Because
/// string hash codes are randomised per process, the hang appeared on some process starts and not others. Per-key
/// locks only block callers of the same key. The lock table is process-wide, so concurrent requests (each with its
/// own scoped provider) also share one factory call per key.
/// </remarks>
internal sealed class KeyedAsyncLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Number of keys currently locked or awaited (for tests).</summary>
    internal int Count { get { lock (_entries) return _entries.Count; } }

    /// <summary>True while the key is locked or awaited (for tests; the table is process-wide, so tests must check
    /// their own key rather than the global count).</summary>
    internal bool IsTracked(string key) { lock (_entries) return _entries.ContainsKey(key); }

    /// <summary>Snapshot of tracked keys (diagnostics/tests).</summary>
    internal string[] SnapshotKeys() { lock (_entries) return _entries.Keys.ToArray(); }

    /// <summary>Waits for the key's lock; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.RefCount == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Releaser(KeyedAsyncLock owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            entry.Semaphore.Release();
            owner.Leave(key, entry);
        }
    }
}
