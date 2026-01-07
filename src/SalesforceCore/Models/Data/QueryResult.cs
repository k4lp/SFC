using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Represents the result of a SOQL query.
/// </summary>
public class QueryResult
{
    /// <summary>
    /// Total number of records matching the query.
    /// </summary>
    [JsonPropertyName("totalSize")]
    public int TotalSize { get; set; }

    /// <summary>
    /// Whether the query is complete (no more pages).
    /// </summary>
    [JsonPropertyName("done")]
    public bool Done { get; set; }

    /// <summary>
    /// URL for the next page of results (if more exist).
    /// </summary>
    [JsonPropertyName("nextRecordsUrl")]
    public string? NextRecordsUrl { get; set; }

    /// <summary>
    /// Array of record objects.
    /// </summary>
    [JsonPropertyName("records")]
    public List<JsonObject> Records { get; set; } = new();
}

/// <summary>
/// Generic query result with typed records.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public class QueryResult<T> where T : class
{
    /// <summary>
    /// Total number of records matching the query.
    /// </summary>
    public int TotalSize { get; set; }

    /// <summary>
    /// Whether the query is complete (no more pages).
    /// </summary>
    public bool Done { get; set; }

    /// <summary>
    /// URL for the next page of results (if more exist).
    /// </summary>
    public string? NextRecordsUrl { get; set; }

    /// <summary>
    /// Typed array of record objects.
    /// </summary>
    public List<T> Records { get; set; } = new();
}

/// <summary>
/// Paged result for list views.
/// </summary>
public class PagedResult
{
    /// <summary>
    /// Records for the current page.
    /// </summary>
    public List<JsonObject> Records { get; set; } = new();

    /// <summary>
    /// Total number of records (if known).
    /// </summary>
    public int? TotalCount { get; set; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Number of records per page.
    /// </summary>
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Total number of pages (if known).
    /// </summary>
    public int? TotalPages { get; set; }

    /// <summary>
    /// Whether there are more pages.
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>
    /// The offset for the current page.
    /// </summary>
    public int Offset => (CurrentPage - 1) * PageSize;
}

/// <summary>
/// Result of a record creation operation.
/// </summary>
public class CreateResult
{
    /// <summary>
    /// ID of the created record.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Errors that occurred (if any).
    /// </summary>
    [JsonPropertyName("errors")]
    public List<SalesforceError> Errors { get; set; } = new();
}

/// <summary>
/// Result of a batch operation.
/// </summary>
public class BatchResult
{
    /// <summary>
    /// Whether all operations were successful.
    /// </summary>
    public bool AllSuccessful => !Results.Any(r => !r.Success);

    /// <summary>
    /// Number of successful operations.
    /// </summary>
    public int SuccessCount => Results.Count(r => r.Success);

    /// <summary>
    /// Number of failed operations.
    /// </summary>
    public int ErrorCount => Results.Count(r => !r.Success);

    /// <summary>
    /// Individual operation results.
    /// </summary>
    public List<BatchItemResult> Results { get; set; } = new();
}

/// <summary>
/// Result of a single batch operation item.
/// </summary>
public class BatchItemResult
{
    /// <summary>
    /// ID of the affected record.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Whether this operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Errors for this operation.
    /// </summary>
    public List<SalesforceError> Errors { get; set; } = new();
}

/// <summary>
/// Represents a Salesforce API error.
/// </summary>
public class SalesforceError
{
    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error code (e.g., "REQUIRED_FIELD_MISSING").
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Fields related to the error.
    /// </summary>
    [JsonPropertyName("fields")]
    public List<string>? Fields { get; set; }

    /// <summary>
    /// Status code for HTTP errors.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }
}
