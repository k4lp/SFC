using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// Background service that periodically removes expired cache entries from SQL Server.
/// Only active when CacheProvider = SqlServer.
/// </summary>
/// <remarks>
/// <para>
/// This service performs the following maintenance tasks:
/// - Deletes expired cache entries in batches to avoid lock escalation
/// - Logs cleanup statistics for monitoring and capacity planning
/// - Respects cancellation tokens for graceful shutdown
/// </para>
/// <para>
/// Audit logging:
/// - Logs start/stop of cleanup cycles
/// - Logs number of entries deleted per cycle
/// - Logs any errors encountered during cleanup
/// </para>
/// </remarks>
public class EncryptedSqlServerCacheCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EncryptedSqlServerCacheCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval;
    private readonly TimeSpan _cleanupGracePeriod;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Batch size for deletion operations.
    /// Smaller batches prevent lock escalation and reduce blocking.
    /// </summary>
    private const int BatchSize = 1000;

    /// <summary>
    /// Delay between batches to yield to other operations.
    /// </summary>
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Creates a new EncryptedSqlServerCacheCleanupService.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for DbContext creation.</param>
    /// <param name="options">Salesforce options containing cleanup interval.</param>
    /// <param name="logger">Logger for audit and diagnostic logging.</param>
    /// <param name="timeProvider">Optional time provider for testing.</param>
    public EncryptedSqlServerCacheCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<SalesforceOptions> options,
        ILogger<EncryptedSqlServerCacheCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var sfOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _cleanupInterval = sfOptions.CacheCleanupInterval;
        _cleanupGracePeriod = sfOptions.SqlCacheWriteBehind.Enabled
            ? sfOptions.SqlCacheWriteBehind.CleanupGracePeriod
            : TimeSpan.Zero;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes the cleanup loop.
    /// </summary>
    /// <param name="stoppingToken">Token to signal shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CACHE_AUDIT: EncryptedSqlServerCacheCleanupService started. " +
            "CleanupInterval={CleanupInterval}, CleanupGracePeriod={CleanupGracePeriod}",
            _cleanupInterval,
            _cleanupGracePeriod);

        // Initial delay to let the application start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStartTime = _timeProvider.GetTimestamp();

            try
            {
                var deletedCount = await CleanupExpiredEntriesAsync(stoppingToken);

                var elapsed = _timeProvider.GetElapsedTime(cycleStartTime);
                _logger.LogInformation(
                    "CACHE_AUDIT: Cleanup cycle completed. " +
                    "DeletedCount={DeletedCount}, ElapsedMs={ElapsedMs:F2}",
                    deletedCount,
                    elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CACHE_AUDIT: Error during cache cleanup cycle. " +
                    "Error={Error}",
                    ex.Message);
            }

            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("CACHE_AUDIT: EncryptedSqlServerCacheCleanupService stopped");
    }

    /// <summary>
    /// Removes expired cache entries in batches.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of entries deleted.</returns>
    private async Task<int> CleanupExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

        var now = _timeProvider.GetUtcNow();
        var cutoff = _cleanupGracePeriod > TimeSpan.Zero
            ? now.Subtract(_cleanupGracePeriod)
            : now;
        int deletedTotal = 0;
        int deletedBatch;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Delete expired entries in batches
            deletedBatch = await context.CacheEntries
                .Where(e => e.ExpiresAtTime <= cutoff)
                .OrderBy(e => e.ExpiresAtTime)  // Delete oldest first
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            deletedTotal += deletedBatch;

            if (deletedBatch > 0)
            {
                _logger.LogDebug(
                    "CACHE_AUDIT: Deleted batch of expired entries. " +
                    "BatchSize={BatchSize}, TotalDeleted={TotalDeleted}",
                    deletedBatch,
                    deletedTotal);
            }

            if (deletedBatch == BatchSize)
            {
                // More entries to delete, yield to other operations
                await Task.Delay(BatchDelay, cancellationToken);
            }
        }
        while (deletedBatch == BatchSize);

        return deletedTotal;
    }

    /// <summary>
    /// Gets statistics about the cache for monitoring purposes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cache statistics.</returns>
    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

        var now = _timeProvider.GetUtcNow();

        var totalEntries = await context.CacheEntries.CountAsync(cancellationToken);
        var expiredEntries = await context.CacheEntries.CountAsync(e => e.ExpiresAtTime <= now, cancellationToken);
        var activeEntries = totalEntries - expiredEntries;

        var totalSize = await context.CacheEntries.SumAsync(e => e.OriginalSize, cancellationToken);
        var totalAccesses = await context.CacheEntries.SumAsync(e => e.AccessCount, cancellationToken);

        var oldestEntry = await context.CacheEntries
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var newestEntry = await context.CacheEntries
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new CacheStatistics
        {
            TotalEntries = totalEntries,
            ActiveEntries = activeEntries,
            ExpiredEntries = expiredEntries,
            TotalSizeBytes = totalSize,
            TotalAccessCount = totalAccesses,
            OldestEntryCreated = oldestEntry == default ? null : oldestEntry,
            NewestEntryCreated = newestEntry == default ? null : newestEntry,
            Timestamp = now
        };
    }
}

/// <summary>
/// Statistics about the encrypted SQL Server cache.
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cache entries (including expired).
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Number of active (non-expired) cache entries.
    /// </summary>
    public int ActiveEntries { get; init; }

    /// <summary>
    /// Number of expired cache entries awaiting cleanup.
    /// </summary>
    public int ExpiredEntries { get; init; }

    /// <summary>
    /// Total size of original (unencrypted) cached data in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Total number of cache accesses across all entries.
    /// </summary>
    public long TotalAccessCount { get; init; }

    /// <summary>
    /// Creation timestamp of the oldest cache entry.
    /// </summary>
    public DateTimeOffset? OldestEntryCreated { get; init; }

    /// <summary>
    /// Creation timestamp of the newest cache entry.
    /// </summary>
    public DateTimeOffset? NewestEntryCreated { get; init; }

    /// <summary>
    /// Timestamp when these statistics were collected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
