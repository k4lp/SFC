using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Query;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Service for Salesforce data operations (CRUD, query, files).
/// </summary>
public interface IDataService
{
    #region Query Operations

    /// <summary>
    /// Executes a raw SOQL query.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with records.</returns>
    Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the next page of query results using the nextRecordsUrl.
    /// </summary>
    /// <param name="nextRecordsUrl">URL from previous QueryResult.NextRecordsUrl.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with next page of records.</returns>
    Task<QueryResult> QueryNextAsync(string nextRecordsUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and automatically fetches all pages, returning all records.
    /// Use with caution for large datasets - consider using QueryAllAsyncEnumerable instead.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All records from all pages.</returns>
    Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and streams records from all pages without loading everything into memory.
    /// Ideal for processing large datasets efficiently.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of records.</returns>
    IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a paged query for list views with a type-safe filter condition.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="fields">Fields to retrieve.</param>
    /// <param name="filter">Optional type-safe filter condition.</param>
    /// <param name="orderBy">Optional ORDER BY field.</param>
    /// <param name="descending">Sort descending.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Records per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged result.</returns>
    Task<PagedResult> QueryPagedAsync(
        string sObject,
        IEnumerable<string> fields,
        SoqlCondition? filter = null,
        string? orderBy = null,
        bool descending = false,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="id">Record ID.</param>
    /// <param name="fields">Optional fields to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record data.</returns>
    Task<JsonNode> GetRecordAsync(
        string sObject,
        string id,
        IEnumerable<string>? fields = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new record.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="data">Record data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created record ID.</returns>
    Task<string> CreateRecordAsync(
        string sObject,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="id">Record ID.</param>
    /// <param name="data">Fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateRecordAsync(
        string sObject,
        string id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a record.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="id">Record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteRecordAsync(
        string sObject,
        string id,
        CancellationToken cancellationToken = default);

    #endregion

    #region Lookup Operations

    /// <summary>
    /// Hydrates lookup fields with display names.
    /// Uses batch queries for performance.
    /// </summary>
    /// <param name="record">Record with lookup IDs.</param>
    /// <param name="lookupFields">Lookup fields to hydrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of field name to display name.</returns>
    Task<Dictionary<string, string>> HydrateLookupsAsync(
        JsonNode record,
        IEnumerable<SObjectField> lookupFields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single lookup ID to display name.
    /// </summary>
    /// <param name="targetObject">Target object type.</param>
    /// <param name="recordId">Record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Display name or null.</returns>
    Task<string?> ResolveLookupAsync(
        string targetObject,
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch resolves multiple lookup IDs.
    /// </summary>
    /// <param name="targetObject">Target object type.</param>
    /// <param name="recordIds">Record IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of ID to display name.</returns>
    Task<Dictionary<string, string>> BatchResolveLookupAsync(
        string targetObject,
        IEnumerable<string> recordIds,
        CancellationToken cancellationToken = default);

    #endregion

    #region File Operations

    /// <summary>
    /// Gets files attached to a record.
    /// </summary>
    /// <param name="linkedEntityId">Parent record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of attached files.</returns>
    Task<List<AttachedFile>> GetAttachedFilesAsync(
        string linkedEntityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file and links it to a record.
    /// </summary>
    /// <param name="linkedEntityId">Parent record ID.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="content">File content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ContentVersion ID.</returns>
    Task<string> UploadFileAsync(
        string linkedEntityId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file from a stream and links it to a record.
    /// More memory-efficient for large files as it avoids unnecessary copying.
    /// </summary>
    /// <param name="linkedEntityId">Parent record ID.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="contentStream">File content stream.</param>
    /// <param name="contentLength">Length of the content in bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ContentVersion ID.</returns>
    Task<string> UploadFileAsync(
        string linkedEntityId,
        string fileName,
        Stream contentStream,
        long contentLength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets file content by ContentVersion ID.
    /// </summary>
    /// <param name="contentVersionId">ContentVersion ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File bytes.</returns>
    Task<byte[]> GetFileContentAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an attached file.
    /// </summary>
    /// <param name="contentDocumentId">ContentDocument ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteFileAsync(
        string contentDocumentId,
        CancellationToken cancellationToken = default);

    #endregion

    #region User Operations

    /// <summary>
    /// Gets the current user's profile information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User profile data.</returns>
    Task<JsonNode> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Recent Items

    /// <summary>
    /// Gets the current user's recently viewed items.
    /// </summary>
    /// <param name="limit">Maximum number of items to return (default 10, max 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recently viewed items.</returns>
    /// <remarks>
    /// This uses the /recent API endpoint which returns items from the user's
    /// "Recently Viewed" list across all objects. For object-specific recent items,
    /// use <see cref="ILookupService.GetRecentItemsAsync"/>.
    /// </remarks>
    Task<List<RecentItem>> GetRecentItemsAsync(int limit = 10, CancellationToken cancellationToken = default);

    #endregion

    #region Upsert Operations

    /// <summary>
    /// Upserts a record using an external ID field.
    /// Creates a new record if the external ID doesn't exist, otherwise updates the existing record.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="externalIdField">The external ID field API name.</param>
    /// <param name="externalIdValue">The external ID value to match.</param>
    /// <param name="data">Record data (should not include the external ID field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upsert result containing record ID and whether it was created or updated.</returns>
    Task<UpsertResult> UpsertRecordAsync(
        string sObject,
        string externalIdField,
        string externalIdValue,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    #endregion

    #region Batch Operations

    /// <summary>
    /// Creates multiple records, automatically switching to Bulk API V2 for large datasets.
    /// For lists smaller than the threshold (default 200), uses sObject Collections.
    /// For larger lists, uses Bulk API V2 for optimal performance.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="records">Records to create.</param>
    /// <param name="bulkThreshold">Number of records above which to use Bulk API (default 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch operation result with success/failure details.</returns>
    Task<BatchResult> BatchCreateAsync(
        string sObject,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple records, automatically switching to Bulk API V2 for large datasets.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="records">Records to update (must include Id field).</param>
    /// <param name="bulkThreshold">Number of records above which to use Bulk API (default 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch operation result with success/failure details.</returns>
    Task<BatchResult> BatchUpdateAsync(
        string sObject,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple records, automatically switching to Bulk API V2 for large datasets.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="externalIdField">The external ID field API name.</param>
    /// <param name="records">Records to upsert.</param>
    /// <param name="bulkThreshold">Number of records above which to use Bulk API (default 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch operation result with success/failure details.</returns>
    Task<BatchResult> BatchUpsertAsync(
        string sObject,
        string externalIdField,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple records, automatically switching to Bulk API V2 for large datasets.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="ids">Record IDs to delete.</param>
    /// <param name="bulkThreshold">Number of records above which to use Bulk API (default 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch operation result with success/failure details.</returns>
    Task<BatchResult> BatchDeleteAsync(
        string sObject,
        IEnumerable<string> ids,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default);

    #endregion

    #region Polymorphic Lookup Operations

    /// <summary>
    /// Resolves the object type for a polymorphic lookup ID using EntityDefinition.
    /// More reliable than prefix-based resolution for custom objects.
    /// </summary>
    /// <param name="recordId">The Salesforce record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object type name, or null if not resolvable.</returns>
    Task<string?> ResolvePolymorphicTypeAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch resolves object types for multiple polymorphic lookup IDs.
    /// </summary>
    /// <param name="recordIds">Salesforce record IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of ID to object type name.</returns>
    Task<Dictionary<string, string>> BatchResolvePolymorphicTypesAsync(
        IEnumerable<string> recordIds,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Result of an upsert operation.
/// </summary>
public class UpsertResult
{
    /// <summary>
    /// The record ID (either newly created or existing).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// True if a new record was created, false if an existing record was updated.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error messages if the operation failed.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of a batch operation (create, update, upsert, or delete).
/// </summary>
public class BatchResult
{
    /// <summary>
    /// Number of records successfully processed.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of records that failed.
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Total number of records processed.
    /// </summary>
    public int TotalCount => SuccessCount + FailureCount;

    /// <summary>
    /// Whether all records were processed successfully.
    /// </summary>
    public bool AllSucceeded => FailureCount == 0;

    /// <summary>
    /// Whether the Bulk API was used (vs sObject Collections).
    /// </summary>
    public bool UsedBulkApi { get; set; }

    /// <summary>
    /// IDs of successfully created/updated records.
    /// </summary>
    public List<string> SuccessfulIds { get; set; } = new();

    /// <summary>
    /// Details of failed records.
    /// </summary>
    public List<BatchRecordError> FailedRecords { get; set; } = new();
}

/// <summary>
/// Error details for a failed record in a batch operation.
/// </summary>
public class BatchRecordError
{
    /// <summary>
    /// Index of the record in the original list (0-based).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Record ID if available (for update/delete operations).
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Error code from Salesforce.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Fields that caused the error.
    /// </summary>
    public List<string> Fields { get; set; } = new();
}

/// <summary>
/// Represents a recently viewed item.
/// </summary>
public class RecentItem
{
    /// <summary>
    /// Record ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the record.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Object type (e.g., "Account", "Contact").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Attributes of the record (including type and URL).
    /// </summary>
    public RecordAttributes? Attributes { get; set; }
}

/// <summary>
/// Attributes associated with a Salesforce record.
/// </summary>
public class RecordAttributes
{
    /// <summary>
    /// Object type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// REST API URL for the record.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Represents an attached file record.
/// </summary>
public class AttachedFile
{
    /// <summary>
    /// ContentDocumentLink ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// ContentDocument ID.
    /// </summary>
    public string ContentDocumentId { get; set; } = string.Empty;

    /// <summary>
    /// ContentVersion ID (latest version).
    /// </summary>
    public string ContentVersionId { get; set; } = string.Empty;

    /// <summary>
    /// File title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// File extension.
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long ContentSize { get; set; }

    /// <summary>
    /// Creation date.
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// MIME type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is an image file.
    /// </summary>
    public bool IsImage => new[] { "png", "jpg", "jpeg", "gif", "webp", "bmp" }
        .Contains(FileExtension?.ToLowerInvariant());
}
