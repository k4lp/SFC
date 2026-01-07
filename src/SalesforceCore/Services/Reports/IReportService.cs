using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Reports;

/// <summary>
/// Service for interacting with Salesforce Analytics/Reports API.
/// Provides access to list, describe, and execute reports.
/// </summary>
public interface IReportService
{
    #region Report Discovery

    /// <summary>
    /// Lists all reports accessible to the current user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of report descriptors.</returns>
    Task<List<ReportDescriptor>> ListReportsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists reports in a specific folder.
    /// </summary>
    /// <param name="folderId">The folder ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of report descriptors.</returns>
    Task<List<ReportDescriptor>> ListReportsInFolderAsync(string folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for reports by name.
    /// </summary>
    /// <param name="searchTerm">The search term.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching report descriptors.</returns>
    Task<List<ReportDescriptor>> SearchReportsAsync(string searchTerm, CancellationToken cancellationToken = default);

    #endregion

    #region Report Metadata

    /// <summary>
    /// Gets the metadata/describe for a report.
    /// Includes information about columns, filters, groupings, and chart settings.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Report metadata.</returns>
    Task<ReportMetadata> DescribeReportAsync(string reportId, CancellationToken cancellationToken = default);

    #endregion

    #region Report Execution

    /// <summary>
    /// Executes a report synchronously and returns the results.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="includeDetails">Whether to include detail rows (default true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Report execution results.</returns>
    /// <example>
    /// <code>
    /// var results = await reportService.RunReportAsync("00O1234567890ABC");
    /// var grandTotal = results.GetGrandTotal();
    /// var detailRows = results.GetDetailRows();
    /// </code>
    /// </example>
    Task<ReportResults> RunReportAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a report with custom options.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="options">Report run options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Report execution results.</returns>
    Task<ReportResults> RunReportAsync(
        string reportId,
        ReportRunOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a report with dynamic filters applied at runtime.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="filters">Dynamic filters to apply.</param>
    /// <param name="includeDetails">Whether to include detail rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Report execution results.</returns>
    /// <example>
    /// <code>
    /// var filters = new List&lt;ReportFilter&gt;
    /// {
    ///     new() { Column = "ACCOUNT_NAME", Operator = "contains", Value = "Acme" }
    /// };
    /// var results = await reportService.RunReportWithFiltersAsync("00O1234", filters);
    /// </code>
    /// </example>
    Task<ReportResults> RunReportWithFiltersAsync(
        string reportId,
        List<ReportFilter> filters,
        bool includeDetails = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a report and returns the raw JSON response.
    /// Useful when you need access to all response fields.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="includeDetails">Whether to include detail rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw JSON response.</returns>
    Task<JsonObject> RunReportRawAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default);

    #endregion

    #region Async Report Execution

    /// <summary>
    /// Starts an asynchronous report execution.
    /// Use for long-running reports or when you don't need immediate results.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="includeDetails">Whether to include detail rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report instance ID for polling.</returns>
    Task<string> StartReportAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status and results of an async report instance.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="instanceId">The report instance ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Report instance results (may be partial if still running).</returns>
    Task<ReportResults> GetReportInstanceAsync(
        string reportId,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recent instances of a report.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of report instance metadata.</returns>
    Task<List<ReportInstanceInfo>> GetReportInstancesAsync(
        string reportId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Report Export

    /// <summary>
    /// Exports a report to CSV format.
    /// </summary>
    /// <param name="reportId">The report ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV content as a string.</returns>
    Task<string> ExportReportToCsvAsync(
        string reportId,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Information about a report instance (async execution).
/// </summary>
public class ReportInstanceInfo
{
    /// <summary>
    /// The instance ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Status of the instance (New, Success, Running, Error).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// URL to retrieve the instance results.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// ID of the user who requested the report.
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the report has finished executing.
    /// </summary>
    public bool HasDetailRows { get; set; }

    /// <summary>
    /// When the instance was requested.
    /// </summary>
    public DateTime? RequestDate { get; set; }

    /// <summary>
    /// When the instance completed (if finished).
    /// </summary>
    public DateTime? CompletionDate { get; set; }
}
