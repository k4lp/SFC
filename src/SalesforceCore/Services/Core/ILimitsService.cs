namespace SalesforceCore.Services.Core;

/// <summary>
/// Service for querying Salesforce API usage limits.
/// Provides proactive visibility into API consumption before hitting rate limits.
/// </summary>
public interface ILimitsService
{
    /// <summary>
    /// Gets all current API usage limits for the organization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of limit names to their usage information.</returns>
    Task<Dictionary<string, LimitInfo>> GetLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific limit by name.
    /// </summary>
    /// <param name="limitName">The limit name (e.g., "DailyApiRequests", "DailyBulkApiBatches").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Limit information or null if not found.</returns>
    Task<LimitInfo?> GetLimitAsync(string limitName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if API limits are approaching threshold.
    /// </summary>
    /// <param name="thresholdPercentage">Warning threshold percentage (default 80%).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of limits that are at or above the threshold.</returns>
    Task<List<LimitWarning>> CheckLimitsAsync(int thresholdPercentage = 80, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the percentage of a specific limit used.
    /// </summary>
    /// <param name="limitName">The limit name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Usage percentage (0-100) or null if limit not found.</returns>
    Task<double?> GetUsagePercentageAsync(string limitName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single API limit with its current usage.
/// </summary>
public class LimitInfo
{
    /// <summary>
    /// Maximum allowed value for this limit.
    /// </summary>
    public int Max { get; set; }

    /// <summary>
    /// Remaining capacity for this limit.
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// Current usage (Max - Remaining).
    /// </summary>
    public int Used => Max - Remaining;

    /// <summary>
    /// Usage percentage (0-100).
    /// </summary>
    public double UsagePercentage => Max > 0 ? ((double)Used / Max) * 100 : 0;

    /// <summary>
    /// Whether this limit has been exceeded.
    /// </summary>
    public bool IsExceeded => Remaining <= 0;
}

/// <summary>
/// Represents a limit that is approaching or has exceeded its threshold.
/// </summary>
public class LimitWarning
{
    /// <summary>
    /// Name of the limit.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Limit information.
    /// </summary>
    public LimitInfo Limit { get; set; } = new();

    /// <summary>
    /// Warning message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Severity level (Warning, Critical, Exceeded).
    /// </summary>
    public LimitSeverity Severity { get; set; }
}

/// <summary>
/// Severity levels for limit warnings.
/// </summary>
public enum LimitSeverity
{
    /// <summary>
    /// Limit is approaching threshold (80-90% used).
    /// </summary>
    Warning,

    /// <summary>
    /// Limit is critically high (90-99% used).
    /// </summary>
    Critical,

    /// <summary>
    /// Limit has been exceeded (100% used).
    /// </summary>
    Exceeded
}

/// <summary>
/// Common Salesforce API limit names for convenience.
/// </summary>
public static class LimitNames
{
    /// <summary>
    /// Daily API requests limit.
    /// </summary>
    public const string DailyApiRequests = "DailyApiRequests";

    /// <summary>
    /// Daily Bulk API batches limit.
    /// </summary>
    public const string DailyBulkApiBatches = "DailyBulkApiBatches";

    /// <summary>
    /// Daily Bulk API V2 requests limit.
    /// </summary>
    public const string DailyBulkV2QueryJobs = "DailyBulkV2QueryJobs";

    /// <summary>
    /// Daily streaming API events limit.
    /// </summary>
    public const string DailyStreamingApiEvents = "DailyStreamingApiEvents";

    /// <summary>
    /// Daily generic streaming API events limit.
    /// </summary>
    public const string DailyGenericStreamingApiEvents = "DailyGenericStreamingApiEvents";

    /// <summary>
    /// Hourly Dashboard refreshes limit.
    /// </summary>
    public const string HourlyDashboardRefreshes = "HourlyDashboardRefreshes";

    /// <summary>
    /// Hourly time-based workflow limit.
    /// </summary>
    public const string HourlyTimeBasedWorkflow = "HourlyTimeBasedWorkflow";

    /// <summary>
    /// Concurrent API requests limit.
    /// </summary>
    public const string ConcurrentAsyncGetReportInstances = "ConcurrentAsyncGetReportInstances";

    /// <summary>
    /// Data storage limit (in MB).
    /// </summary>
    public const string DataStorageMB = "DataStorageMB";

    /// <summary>
    /// File storage limit (in MB).
    /// </summary>
    public const string FileStorageMB = "FileStorageMB";

    /// <summary>
    /// Daily workflow emails limit.
    /// </summary>
    public const string DailyWorkflowEmails = "DailyWorkflowEmails";

    /// <summary>
    /// Single email limit.
    /// </summary>
    public const string SingleEmail = "SingleEmail";

    /// <summary>
    /// Mass email limit.
    /// </summary>
    public const string MassEmail = "MassEmail";

    /// <summary>
    /// Hourly published platform events limit.
    /// </summary>
    public const string HourlyPublishedPlatformEvents = "HourlyPublishedPlatformEvents";

    /// <summary>
    /// Hourly published standard volume platform events limit.
    /// </summary>
    public const string HourlyPublishedStandardVolumePlatformEvents = "HourlyPublishedStandardVolumePlatformEvents";
}
