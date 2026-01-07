using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Service for data replication/synchronization operations.
/// Provides getUpdated and getDeleted methods for efficient incremental data sync.
/// </summary>
public interface IReplicationService
{
    /// <summary>
    /// Gets IDs of records that have been updated within the specified time window.
    /// The time window maximum is 30 days.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="start">Start of the time window.</param>
    /// <param name="end">End of the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing updated record IDs and latest date covered.</returns>
    /// <example>
    /// <code>
    /// var result = await replicationService.GetUpdatedAsync(
    ///     "Account",
    ///     DateTime.UtcNow.AddDays(-1),
    ///     DateTime.UtcNow);
    ///
    /// foreach (var id in result.Ids)
    /// {
    ///     // Fetch and sync the updated record
    /// }
    /// </code>
    /// </example>
    Task<UpdatedRecordsResult> GetUpdatedAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets IDs of records that have been updated using predefined options.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="options">Replication options defining the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing updated record IDs and latest date covered.</returns>
    Task<UpdatedRecordsResult> GetUpdatedAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about records that have been deleted within the specified time window.
    /// Deleted records are available for a limited time based on org settings.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="start">Start of the time window.</param>
    /// <param name="end">End of the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing deleted record information and latest date covered.</returns>
    /// <example>
    /// <code>
    /// var result = await replicationService.GetDeletedAsync(
    ///     "Account",
    ///     DateTime.UtcNow.AddDays(-7),
    ///     DateTime.UtcNow);
    ///
    /// foreach (var deleted in result.DeletedRecords)
    /// {
    ///     // Remove or mark as deleted in local system
    ///     Console.WriteLine($"Record {deleted.Id} deleted on {deleted.DeletedDate}");
    /// }
    /// </code>
    /// </example>
    Task<DeletedRecordsResult> GetDeletedAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about records that have been deleted using predefined options.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="options">Replication options defining the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing deleted record information and latest date covered.</returns>
    Task<DeletedRecordsResult> GetDeletedAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete sync summary including both updated and deleted records.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="start">Start of the time window.</param>
    /// <param name="end">End of the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of all changes.</returns>
    Task<SyncSummary> GetSyncSummaryAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete sync summary using predefined options.
    /// </summary>
    /// <param name="sObject">The SObject type name.</param>
    /// <param name="options">Replication options defining the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of all changes.</returns>
    Task<SyncSummary> GetSyncSummaryAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync summaries for multiple objects in parallel.
    /// </summary>
    /// <param name="sObjects">The SObject type names.</param>
    /// <param name="options">Replication options defining the time window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary mapping object names to sync summaries.</returns>
    Task<Dictionary<string, SyncSummary>> GetSyncSummariesAsync(
        IEnumerable<string> sObjects,
        ReplicationOptions options,
        CancellationToken cancellationToken = default);
}
