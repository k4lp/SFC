using System.Text.Json.Serialization;
using SalesforceCore.Utilities;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Bulk API 2.0 job operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulkOperation
{
    /// <summary>Insert new records.</summary>
    insert,
    /// <summary>Update existing records.</summary>
    update,
    /// <summary>Upsert records (insert or update).</summary>
    upsert,
    /// <summary>Delete records.</summary>
    delete,
    /// <summary>Hard delete records (bypass recycle bin).</summary>
    hardDelete,
    /// <summary>Query records.</summary>
    query,
    /// <summary>Query all records including deleted.</summary>
    queryAll
}

/// <summary>
/// Bulk job state.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulkJobState
{
    /// <summary>Job is open for data upload.</summary>
    Open,
    /// <summary>Data upload is complete, ready for processing.</summary>
    UploadComplete,
    /// <summary>Job is being processed.</summary>
    InProgress,
    /// <summary>Job completed successfully.</summary>
    JobComplete,
    /// <summary>Job was aborted.</summary>
    Aborted,
    /// <summary>Job failed.</summary>
    Failed
}

/// <summary>
/// Content type for bulk data.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulkContentType
{
    /// <summary>CSV format.</summary>
    CSV,
    /// <summary>JSON format (query results only).</summary>
    JSON
}

/// <summary>
/// Request to create a bulk ingest job.
/// </summary>
public class CreateBulkJobRequest
{
    /// <summary>
    /// The object to process.
    /// </summary>
    [JsonPropertyName("object")]
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// The operation to perform.
    /// </summary>
    [JsonPropertyName("operation")]
    public BulkOperation Operation { get; set; }

    /// <summary>
    /// External ID field name for upsert operations.
    /// </summary>
    [JsonPropertyName("externalIdFieldName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalIdFieldName { get; set; }

    /// <summary>
    /// Content type for the data.
    /// </summary>
    [JsonPropertyName("contentType")]
    public BulkContentType ContentType { get; set; } = BulkContentType.CSV;

    /// <summary>
    /// Column delimiter for CSV data.
    /// </summary>
    [JsonPropertyName("columnDelimiter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColumnDelimiter { get; set; }

    /// <summary>
    /// Line ending type.
    /// </summary>
    [JsonPropertyName("lineEnding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LineEnding { get; set; }
}

/// <summary>
/// Bulk job information.
/// </summary>
public class BulkJobInfo
{
    /// <summary>
    /// The job ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The operation being performed.
    /// </summary>
    [JsonPropertyName("operation")]
    public BulkOperation Operation { get; set; }

    /// <summary>
    /// The object being processed.
    /// </summary>
    [JsonPropertyName("object")]
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// User who created the job.
    /// </summary>
    [JsonPropertyName("createdById")]
    public string CreatedById { get; set; } = string.Empty;

    /// <summary>
    /// Job creation timestamp.
    /// </summary>
    [JsonPropertyName("createdDate")]
    [JsonConverter(typeof(SalesforceDateTimeConverter))]
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Job completion timestamp.
    /// </summary>
    [JsonPropertyName("systemModstamp")]
    [JsonConverter(typeof(SalesforceDateTimeConverter))]
    public DateTime SystemModstamp { get; set; }

    /// <summary>
    /// Current job state.
    /// </summary>
    [JsonPropertyName("state")]
    public BulkJobState State { get; set; }

    /// <summary>
    /// External ID field for upsert.
    /// </summary>
    [JsonPropertyName("externalIdFieldName")]
    public string? ExternalIdFieldName { get; set; }

    /// <summary>
    /// Content type.
    /// </summary>
    [JsonPropertyName("contentType")]
    public BulkContentType ContentType { get; set; }

    /// <summary>
    /// API version.
    /// </summary>
    [JsonPropertyName("apiVersion")]
    public double ApiVersion { get; set; }

    /// <summary>
    /// Content URL for data upload.
    /// </summary>
    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }

    /// <summary>
    /// Number of records processed.
    /// </summary>
    [JsonPropertyName("numberRecordsProcessed")]
    public int NumberRecordsProcessed { get; set; }

    /// <summary>
    /// Number of records that failed.
    /// </summary>
    [JsonPropertyName("numberRecordsFailed")]
    public int NumberRecordsFailed { get; set; }

    /// <summary>
    /// Total processing time in milliseconds.
    /// </summary>
    [JsonPropertyName("totalProcessingTime")]
    public long TotalProcessingTime { get; set; }

    /// <summary>
    /// Retries performed.
    /// </summary>
    [JsonPropertyName("retries")]
    public int Retries { get; set; }

    /// <summary>
    /// Error message if job failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the job has completed (successfully or not).
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => State is BulkJobState.JobComplete or BulkJobState.Aborted or BulkJobState.Failed;

    /// <summary>
    /// Whether the job was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => State == BulkJobState.JobComplete && NumberRecordsFailed == 0;
}

/// <summary>
/// Request to create a bulk query job.
/// </summary>
public class CreateBulkQueryRequest
{
    /// <summary>
    /// The SOQL query to execute.
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// The operation type (query or queryAll).
    /// </summary>
    [JsonPropertyName("operation")]
    public BulkOperation Operation { get; set; } = BulkOperation.query;

    /// <summary>
    /// Column delimiter for CSV results.
    /// </summary>
    [JsonPropertyName("columnDelimiter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColumnDelimiter { get; set; }

    /// <summary>
    /// Line ending type.
    /// </summary>
    [JsonPropertyName("lineEnding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LineEnding { get; set; }
}

/// <summary>
/// Result of a bulk operation for a single record.
/// </summary>
public class BulkRecordResult
{
    /// <summary>
    /// Whether this record was processed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether a new record was created (for upsert).
    /// </summary>
    public bool Created { get; set; }

    /// <summary>
    /// The Salesforce ID of the record.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Row number in the original CSV.
    /// </summary>
    public int RowNumber { get; set; }

    /// <summary>
    /// Original record data.
    /// </summary>
    public Dictionary<string, string?>? OriginalData { get; set; }
}

/// <summary>
/// Summary of bulk job results.
/// </summary>
public class BulkJobResults
{
    /// <summary>
    /// The job information.
    /// </summary>
    public BulkJobInfo Job { get; set; } = new();

    /// <summary>
    /// Successfully processed records.
    /// </summary>
    public List<BulkRecordResult> SuccessfulRecords { get; set; } = new();

    /// <summary>
    /// Failed records.
    /// </summary>
    public List<BulkRecordResult> FailedRecords { get; set; } = new();

    /// <summary>
    /// Unprocessed records (if job was aborted).
    /// </summary>
    public List<BulkRecordResult> UnprocessedRecords { get; set; } = new();

    /// <summary>
    /// Total records submitted.
    /// </summary>
    public int TotalRecords => SuccessfulRecords.Count + FailedRecords.Count + UnprocessedRecords.Count;

    /// <summary>
    /// Whether the job completed successfully.
    /// </summary>
    public bool IsSuccess => Job.IsSuccess && FailedRecords.Count == 0;
}
