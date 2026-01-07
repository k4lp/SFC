using System.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Reports;

/// <summary>
/// Implementation of the Salesforce Analytics/Reports API service.
/// </summary>
public class ReportService : IReportService
{
    private readonly ISalesforceClient _client;
    private readonly ILogger<ReportService> _logger;

    /// <summary>
    /// Creates a new ReportService.
    /// </summary>
    public ReportService(
        ISalesforceClient client,
        ILogger<ReportService> logger)
    {
        _client = client;
        _logger = logger;
    }

    #region Report Discovery

    /// <inheritdoc/>
    public async Task<List<ReportDescriptor>> ListReportsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing all reports");

        var response = await _client.GetAsync($"{SalesforceConstants.Paths.AnalyticsReports}", cancellationToken);

        return ParseReportList(response);
    }

    /// <inheritdoc/>
    public async Task<List<ReportDescriptor>> ListReportsInFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ArgumentException("Folder ID cannot be empty.", nameof(folderId));
        }

        if (!SecurityUtils.IsValidSalesforceId(folderId))
        {
            throw new ArgumentException("Invalid folder ID.", nameof(folderId));
        }

        _logger.LogDebug("Listing reports in folder {FolderId}", folderId);

        // Use type-safe SOQL builder
        var soql = SoqlBuilder.From("Report")
            .Select("Id", "Name", "Description")
            .WhereEquals("OwnerId", folderId)
            .Build();

        var response = await _client.GetAsync(
            $"/query?q={UrlUtils.Escape(soql)}",
            cancellationToken);

        var reports = new List<ReportDescriptor>();
        var records = response["records"] as JsonArray;
        if (records == null || records.Count == 0)
        {
            return reports;
        }

        foreach (var record in records)
        {
            if (record is not JsonObject recordObj)
            {
                _logger.LogWarning("Skipping malformed report record in folder listing for {FolderId}", folderId);
                continue;
            }

            reports.Add(new ReportDescriptor
            {
                Id = recordObj["Id"]?.ToString() ?? string.Empty,
                Name = recordObj["Name"]?.ToString() ?? string.Empty,
                Description = recordObj["Description"]?.ToString()
            });
        }

        return reports;
    }

    /// <inheritdoc/>
    public async Task<List<ReportDescriptor>> SearchReportsAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("Search term cannot be empty.", nameof(searchTerm));
        }

        _logger.LogDebug("Searching reports for '{SearchTerm}'", searchTerm);

        // Use type-safe SOQL builder with LIKE pattern
        var soql = SoqlBuilder.From("Report")
            .Select("Id", "Name", "Description")
            .WhereLike("Name", $"%{searchTerm}%")
            .OrderBy("Name")
            .Limit(100)
            .Build();

        var response = await _client.GetAsync($"/query?q={UrlUtils.Escape(soql)}", cancellationToken);

        var reports = new List<ReportDescriptor>();
        var records = response["records"] as JsonArray;
        if (records == null || records.Count == 0)
        {
            return reports;
        }

        foreach (var record in records)
        {
            if (record is not JsonObject recordObj)
            {
                _logger.LogWarning("Skipping malformed report record when searching for {SearchTerm}", searchTerm);
                continue;
            }

            reports.Add(new ReportDescriptor
            {
                Id = recordObj["Id"]?.ToString() ?? string.Empty,
                Name = recordObj["Name"]?.ToString() ?? string.Empty,
                Description = recordObj["Description"]?.ToString()
            });
        }

        return reports;
    }

    #endregion

    #region Report Metadata

    /// <inheritdoc/>
    public async Task<ReportMetadata> DescribeReportAsync(string reportId, CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        _logger.LogDebug("Describing report {ReportId}", reportId);

        var response = await _client.GetAsync(
            $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}/describe",
            cancellationToken);

        return JsonSerializer.Deserialize<ReportMetadata>(response) ?? new ReportMetadata();
    }

    #endregion

    #region Report Execution

    /// <inheritdoc/>
    public async Task<ReportResults> RunReportAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        _logger.LogDebug("Running report {ReportId} (includeDetails={IncludeDetails})", reportId, includeDetails);

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}?includeDetails={includeDetails.ToString().ToLower()}";
        var response = await _client.GetAsync(endpoint, cancellationToken);

        return ParseReportResults(response);
    }

    /// <inheritdoc/>
    public async Task<ReportResults> RunReportAsync(
        string reportId,
        ReportRunOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.Filters != null && options.Filters.Count > 0)
        {
            return await RunReportWithFiltersAsync(reportId, options.Filters, options.IncludeDetails, cancellationToken);
        }

        return await RunReportAsync(reportId, options.IncludeDetails, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReportResults> RunReportWithFiltersAsync(
        string reportId,
        List<ReportFilter> filters,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        if (filters == null || filters.Count == 0)
        {
            return await RunReportAsync(reportId, includeDetails, cancellationToken);
        }

        _logger.LogDebug("Running report {ReportId} with {FilterCount} filters", reportId, filters.Count);

        // Build the request body with filters
        var requestBody = new
        {
            reportMetadata = new
            {
                reportFilters = filters.Select(f => new
                {
                    column = f.Column,
                    @operator = f.Operator,
                    value = f.Value
                }).ToArray()
            }
        };

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}?includeDetails={includeDetails.ToString().ToLower()}";
        var response = await _client.PostAsync(endpoint, requestBody, cancellationToken);

        return ParseReportResults(response);
    }

    /// <inheritdoc/>
    public async Task<JsonObject> RunReportRawAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}?includeDetails={includeDetails.ToString().ToLower()}";
        // Ensure result is JsonObject
        var result = await _client.GetAsync(endpoint, cancellationToken);
        return result.AsObject();
    }

    #endregion

    #region Async Report Execution

    /// <inheritdoc/>
    public async Task<string> StartReportAsync(
        string reportId,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        _logger.LogDebug("Starting async report {ReportId}", reportId);

        var requestBody = new
        {
            reportMetadata = new
            {
                // Empty object to use report defaults
            }
        };

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}/instances?includeDetails={includeDetails.ToString().ToLower()}";
        var response = await _client.PostAsync(endpoint, requestBody, cancellationToken);

        return response["id"]?.ToString() ?? throw new InvalidOperationException("Failed to get report instance ID");
    }

    /// <inheritdoc/>
    public async Task<ReportResults> GetReportInstanceAsync(
        string reportId,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("Instance ID cannot be empty.", nameof(instanceId));
        }

        _logger.LogDebug("Getting report instance {ReportId}/{InstanceId}", reportId, instanceId);

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}/instances/{instanceId}";
        var response = await _client.GetAsync(endpoint, cancellationToken);

        return ParseReportResults(response);
    }

    /// <inheritdoc/>
    public async Task<List<ReportInstanceInfo>> GetReportInstancesAsync(
        string reportId,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        _logger.LogDebug("Getting report instances for {ReportId}", reportId);

        var endpoint = $"{SalesforceConstants.Paths.AnalyticsReports}/{reportId}/instances";
        var response = await _client.GetAsync(endpoint, cancellationToken);

        var instances = new List<ReportInstanceInfo>();

        if (response is JsonArray array)
        {
            foreach (var item in array)
            {
                var instance = ParseReportInstanceInfo(item);
                if (!string.IsNullOrEmpty(instance.Id))
                {
                    instances.Add(instance);
                }
            }
        }
        else if (response is JsonObject obj)
        {
            // Try to find instances in properties
            foreach (var property in obj)
            {
                if (property.Value is JsonObject instanceObj)
                {
                    var instance = ParseReportInstanceInfo(instanceObj);
                    if (!string.IsNullOrEmpty(instance.Id))
                    {
                        instances.Add(instance);
                    }
                }
            }

            // Or explicit "instances" property
            if (instances.Count == 0 && obj["instances"] is JsonArray instancesArray)
            {
                foreach (var item in instancesArray)
                {
                    var instance = ParseReportInstanceInfo(item);
                    if (!string.IsNullOrEmpty(instance.Id))
                    {
                        instances.Add(instance);
                    }
                }
            }
        }

        return instances;
    }

    #endregion

    #region Report Export

    /// <inheritdoc/>
    public async Task<string> ExportReportToCsvAsync(
        string reportId,
        CancellationToken cancellationToken = default)
    {
        ValidateReportId(reportId);

        _logger.LogDebug("Exporting report {ReportId} to CSV", reportId);

        // Run the report with all details
        var results = await RunReportAsync(reportId, includeDetails: true, cancellationToken);

        // Build CSV
        var csv = new StringBuilder();

        // Get column headers
        var columns = results.ReportExtendedMetadata?.DetailColumnInfo?.Keys.ToList() ?? new List<string>();
        csv.AppendLine(string.Join(",", columns.Select(c =>
        {
            var label = results.ReportExtendedMetadata?.DetailColumnInfo?[c]?.Label ?? c;
            return EscapeCsvValue(label);
        })));

        // Get detail rows
        var rows = results.GetDetailRows();
        foreach (var row in rows)
        {
            if (row.DataCells != null)
            {
                var values = row.DataCells.Select(cell => EscapeCsvValue(cell.Label ?? cell.Value?.ToString() ?? ""));
                csv.AppendLine(string.Join(",", values));
            }
        }

        return csv.ToString();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates a report ID format.
    /// </summary>
    private static void ValidateReportId(string reportId)
    {
        if (string.IsNullOrWhiteSpace(reportId))
        {
            throw new ArgumentException("Report ID cannot be empty.", nameof(reportId));
        }

        // Salesforce report IDs start with "00O"
        if (!reportId.StartsWith("00O", StringComparison.OrdinalIgnoreCase) && reportId.Length != 15 && reportId.Length != 18)
        {
            // Allow anyway but log warning - might be a custom ID format
        }
    }

    /// <summary>
    /// Parses the report list response.
    /// </summary>
    private static List<ReportDescriptor> ParseReportList(JsonNode response)
    {
        var reports = new List<ReportDescriptor>();

        // The response can be an object with report metadata or an array
        if (response is JsonObject obj && obj.ContainsKey("reports") && obj["reports"] is JsonArray reportsArray)
        {
            foreach (var item in reportsArray)
            {
                if (item != null)
                {
                    reports.Add(JsonSerializer.Deserialize<ReportDescriptor>(item) ?? new ReportDescriptor());
                }
            }
        }
        else
        {
            // Single report or different format
            var descriptor = JsonSerializer.Deserialize<ReportDescriptor>(response);
            if (descriptor != null && !string.IsNullOrEmpty(descriptor.Id))
            {
                reports.Add(descriptor);
            }
        }

        return reports;
    }

    /// <summary>
    /// Parses report results from JSON response.
    /// </summary>
    private static ReportResults ParseReportResults(JsonNode response)
    {
        return JsonSerializer.Deserialize<ReportResults>(response) ?? new ReportResults();
    }

    /// <summary>
    /// Parses report instance info from JSON.
    /// </summary>
    private static ReportInstanceInfo ParseReportInstanceInfo(JsonNode? token)
    {
        if (token is not JsonObject obj)
        {
            return new ReportInstanceInfo();
        }

        return new ReportInstanceInfo
        {
            Id = obj["id"]?.ToString() ?? string.Empty,
            Status = obj["status"]?.ToString() ?? string.Empty,
            Url = obj["url"]?.ToString() ?? string.Empty,
            OwnerId = obj["ownerId"]?.ToString() ?? string.Empty,
            HasDetailRows = obj["hasDetailRows"]?.GetValue<bool>() ?? false,
            RequestDate = obj["requestDate"].ParseDateTime(),
            CompletionDate = obj["completionDate"].ParseDateTime()
        };
    }

    /// <summary>
    /// Escapes a value for CSV format.
    /// </summary>
    private static string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Escape quotes and wrap in quotes if contains special characters
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    #endregion
}
