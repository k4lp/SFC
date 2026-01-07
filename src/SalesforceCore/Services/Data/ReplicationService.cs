using System.Globalization;
using Microsoft.Extensions.Logging;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Implementation of data replication/synchronization operations.
/// Provides efficient incremental sync capabilities using Salesforce's
/// getUpdated and getDeleted endpoints.
/// </summary>
public class ReplicationService : IReplicationService
{
    private readonly ISalesforceClient _client;
    private readonly ILogger<ReplicationService> _logger;

    /// <summary>
    /// ISO 8601 date format required by Salesforce.
    /// </summary>
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    /// <summary>
    /// Maximum time window allowed by Salesforce (30 days).
    /// </summary>
    private static readonly TimeSpan MaxTimeWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Creates a new ReplicationService.
    /// </summary>
    public ReplicationService(
        ISalesforceClient client,
        ILogger<ReplicationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UpdatedRecordsResult> GetUpdatedAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(sObject, start, end);

        var startStr = FormatDateForSalesforce(start);
        var endStr = FormatDateForSalesforce(end);

        _logger.LogDebug(
            "Getting updated records for {SObject} from {Start} to {End}",
            sObject, startStr, endStr);

        var endpoint = $"/sobjects/{sObject}/updated/?start={UrlUtils.Escape(startStr)}&end={UrlUtils.Escape(endStr)}";

        try
        {
            var result = await _client.GetAsync<UpdatedRecordsResult>(endpoint, cancellationToken);

            _logger.LogDebug(
                "Found {Count} updated records for {SObject}",
                result.Ids.Count, sObject);

            return result;
        }
        catch (SalesforceException ex) when (ex.Message.Contains("INVALID_REPLICATION_DATE"))
        {
            _logger.LogWarning(
                "Invalid replication date range for {SObject}. " +
                "The time window may be too large (max 30 days) or dates may be invalid.",
                sObject);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<UpdatedRecordsResult> GetUpdatedAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return GetUpdatedAsync(sObject, options.StartDate, options.EndDate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeletedRecordsResult> GetDeletedAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(sObject, start, end);

        var startStr = FormatDateForSalesforce(start);
        var endStr = FormatDateForSalesforce(end);

        _logger.LogDebug(
            "Getting deleted records for {SObject} from {Start} to {End}",
            sObject, startStr, endStr);

        var endpoint = $"/sobjects/{sObject}/deleted/?start={UrlUtils.Escape(startStr)}&end={UrlUtils.Escape(endStr)}";

        try
        {
            var result = await _client.GetAsync<DeletedRecordsResult>(endpoint, cancellationToken);

            _logger.LogDebug(
                "Found {Count} deleted records for {SObject}",
                result.DeletedRecords.Count, sObject);

            return result;
        }
        catch (SalesforceException ex) when (ex.Message.Contains("INVALID_REPLICATION_DATE"))
        {
            _logger.LogWarning(
                "Invalid replication date range for {SObject}. " +
                "The time window may be too large (max 30 days) or dates may be invalid.",
                sObject);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<DeletedRecordsResult> GetDeletedAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return GetDeletedAsync(sObject, options.StartDate, options.EndDate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SyncSummary> GetSyncSummaryAsync(
        string sObject,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default)
    {
        // Execute both requests in parallel for efficiency
        var updatedTask = GetUpdatedAsync(sObject, start, end, cancellationToken);
        var deletedTask = GetDeletedAsync(sObject, start, end, cancellationToken);

        await Task.WhenAll(updatedTask, deletedTask);

        var updated = await updatedTask;
        var deleted = await deletedTask;

        return new SyncSummary
        {
            SObjectType = sObject,
            StartDate = start,
            EndDate = end,
            UpdatedCount = updated.Ids.Count,
            DeletedCount = deleted.DeletedRecords.Count,
            UpdatedIds = updated.Ids,
            DeletedIds = deleted.DeletedRecords.Select(d => d.Id).ToList(),
            LatestDateCoveredUpdates = updated.GetLatestDateCoveredAsDateTime(),
            LatestDateCoveredDeletes = deleted.GetLatestDateCoveredAsDateTime()
        };
    }

    /// <inheritdoc/>
    public Task<SyncSummary> GetSyncSummaryAsync(
        string sObject,
        ReplicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return GetSyncSummaryAsync(sObject, options.StartDate, options.EndDate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, SyncSummary>> GetSyncSummariesAsync(
        IEnumerable<string> sObjects,
        ReplicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (sObjects == null)
        {
            throw new ArgumentNullException(nameof(sObjects));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var objectList = sObjects.ToList();
        if (objectList.Count == 0)
        {
            return new Dictionary<string, SyncSummary>();
        }

        _logger.LogDebug(
            "Getting sync summaries for {Count} objects",
            objectList.Count);

        // Execute all sync summaries in parallel
        var tasks = objectList.Select(async sObject =>
        {
            try
            {
                var summary = await GetSyncSummaryAsync(sObject, options, cancellationToken);
                return (sObject, summary, error: (Exception?)null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get sync summary for {SObject}", sObject);
                return (sObject, summary: (SyncSummary?)null, error: ex);
            }
        });

        var results = await Task.WhenAll(tasks);

        var summaries = new Dictionary<string, SyncSummary>();
        foreach (var (sObject, summary, error) in results)
        {
            if (summary != null)
            {
                summaries[sObject] = summary;
            }
            // Optionally, we could include failed objects with empty summaries
        }

        return summaries;
    }

    /// <summary>
    /// Validates input parameters for replication operations.
    /// </summary>
    private static void ValidateInputs(string sObject, DateTime start, DateTime end)
    {
        if (string.IsNullOrWhiteSpace(sObject))
        {
            throw new ArgumentException("SObject type cannot be empty.", nameof(sObject));
        }

        // Validate object name format to prevent injection attacks
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            throw new ArgumentException($"Invalid SObject name format: {sObject}", nameof(sObject));
        }

        if (start >= end)
        {
            throw new ArgumentException("Start date must be before end date.", nameof(start));
        }

        if (end > DateTime.UtcNow.AddMinutes(5)) // Small buffer for clock skew
        {
            throw new ArgumentException("End date cannot be in the future.", nameof(end));
        }

        var timeWindow = end - start;
        if (timeWindow > MaxTimeWindow)
        {
            throw new ArgumentException(
                $"Time window ({timeWindow.TotalDays:F1} days) exceeds maximum allowed (30 days). " +
                "Use multiple smaller windows for larger time ranges.",
                nameof(end));
        }
    }

    /// <summary>
    /// Formats a DateTime for Salesforce API consumption.
    /// Uses ISO 8601 format with Z suffix for UTC.
    /// </summary>
    private static string FormatDateForSalesforce(DateTime dateTime)
    {
        // Ensure UTC
        var utcDate = dateTime.Kind == DateTimeKind.Utc
            ? dateTime
            : dateTime.ToUniversalTime();

        // Format as ISO 8601 with explicit Z suffix for UTC
        // Using "o" format would produce +00:00, but Salesforce prefers Z
        // Using explicit format ensures consistent output regardless of timezone
        return utcDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
