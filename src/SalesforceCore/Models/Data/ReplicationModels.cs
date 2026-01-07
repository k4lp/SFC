using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Result of a getUpdated API call for data replication/sync.
/// </summary>
public class UpdatedRecordsResult
{
    /// <summary>
    /// List of IDs for records that have been updated.
    /// </summary>
    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = new();

    /// <summary>
    /// The latest date/time covered by this result (ISO8601 format).
    /// Use this as the start date for subsequent sync calls.
    /// </summary>
    [JsonPropertyName("latestDateCovered")]
    public string LatestDateCovered { get; set; } = string.Empty;

    /// <summary>
    /// Gets the latest date covered as a DateTime.
    /// </summary>
    public DateTime? GetLatestDateCoveredAsDateTime()
    {
        if (DateTime.TryParse(LatestDateCovered, out var result))
        {
            return result;
        }
        return null;
    }
}

/// <summary>
/// Result of a getDeleted API call for data replication/sync.
/// </summary>
public class DeletedRecordsResult
{
    /// <summary>
    /// List of deleted record information.
    /// </summary>
    [JsonPropertyName("deletedRecords")]
    public List<DeletedRecordInfo> DeletedRecords { get; set; } = new();

    /// <summary>
    /// The earliest date available in the recycle bin.
    /// Records deleted before this date cannot be retrieved.
    /// </summary>
    [JsonPropertyName("earliestDateAvailable")]
    public string EarliestDateAvailable { get; set; } = string.Empty;

    /// <summary>
    /// The latest date/time covered by this result (ISO8601 format).
    /// Use this as the start date for subsequent sync calls.
    /// </summary>
    [JsonPropertyName("latestDateCovered")]
    public string LatestDateCovered { get; set; } = string.Empty;

    /// <summary>
    /// Gets the earliest date available as a DateTime.
    /// </summary>
    public DateTime? GetEarliestDateAvailableAsDateTime()
    {
        if (DateTime.TryParse(EarliestDateAvailable, out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Gets the latest date covered as a DateTime.
    /// </summary>
    public DateTime? GetLatestDateCoveredAsDateTime()
    {
        if (DateTime.TryParse(LatestDateCovered, out var result))
        {
            return result;
        }
        return null;
    }
}

/// <summary>
/// Information about a single deleted record.
/// </summary>
public class DeletedRecordInfo
{
    /// <summary>
    /// The ID of the deleted record.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The date/time when the record was deleted (ISO8601 format).
    /// </summary>
    [JsonPropertyName("deletedDate")]
    public string DeletedDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets the deleted date as a DateTime.
    /// </summary>
    public DateTime? GetDeletedDateAsDateTime()
    {
        if (DateTime.TryParse(DeletedDate, out var result))
        {
            return result;
        }
        return null;
    }
}

/// <summary>
/// Options for replication sync operations.
/// </summary>
public class ReplicationOptions
{
    /// <summary>
    /// The start date/time for the sync window.
    /// For getUpdated, records modified at or after this time.
    /// For getDeleted, records deleted at or after this time.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// The end date/time for the sync window.
    /// For getUpdated, records modified at or before this time.
    /// For getDeleted, records deleted at or before this time.
    /// Must be no later than the current time.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Creates options with the default window (last 30 days).
    /// </summary>
    public static ReplicationOptions Default => new()
    {
        StartDate = DateTime.UtcNow.AddDays(-30),
        EndDate = DateTime.UtcNow
    };

    /// <summary>
    /// Creates options for the last N hours.
    /// </summary>
    /// <param name="hours">Number of hours to look back.</param>
    public static ReplicationOptions LastHours(int hours) => new()
    {
        StartDate = DateTime.UtcNow.AddHours(-hours),
        EndDate = DateTime.UtcNow
    };

    /// <summary>
    /// Creates options for the last N days.
    /// </summary>
    /// <param name="days">Number of days to look back.</param>
    public static ReplicationOptions LastDays(int days) => new()
    {
        StartDate = DateTime.UtcNow.AddDays(-days),
        EndDate = DateTime.UtcNow
    };

    /// <summary>
    /// Creates options since a specific date.
    /// </summary>
    /// <param name="since">Start date.</param>
    public static ReplicationOptions Since(DateTime since) => new()
    {
        StartDate = since,
        EndDate = DateTime.UtcNow
    };

    /// <summary>
    /// Creates options for a specific date range.
    /// </summary>
    /// <param name="start">Start date.</param>
    /// <param name="end">End date.</param>
    public static ReplicationOptions Between(DateTime start, DateTime end) => new()
    {
        StartDate = start,
        EndDate = end
    };
}

/// <summary>
/// Summary of a sync operation.
/// </summary>
public class SyncSummary
{
    /// <summary>
    /// The SObject type that was synced.
    /// </summary>
    public string SObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Number of updated records found.
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Number of deleted records found.
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// The start date of the sync window.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// The end date of the sync window.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// The latest date covered by the updated records result.
    /// Use this as the start date for the next sync.
    /// </summary>
    public DateTime? LatestDateCoveredUpdates { get; set; }

    /// <summary>
    /// The latest date covered by the deleted records result.
    /// Use this as the start date for the next sync.
    /// </summary>
    public DateTime? LatestDateCoveredDeletes { get; set; }

    /// <summary>
    /// IDs of updated records.
    /// </summary>
    public List<string> UpdatedIds { get; set; } = new();

    /// <summary>
    /// IDs of deleted records.
    /// </summary>
    public List<string> DeletedIds { get; set; } = new();

    /// <summary>
    /// Whether there are any changes (updates or deletes).
    /// </summary>
    public bool HasChanges => UpdatedCount > 0 || DeletedCount > 0;

    /// <summary>
    /// Gets the recommended start date for the next sync operation.
    /// </summary>
    public DateTime? GetNextSyncStartDate()
    {
        var dates = new List<DateTime?> { LatestDateCoveredUpdates, LatestDateCoveredDeletes }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        return dates.Count > 0 ? dates.Min() : null;
    }
}
