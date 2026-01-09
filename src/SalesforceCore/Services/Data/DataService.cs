using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Security;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query; // Ensure SoqlBuilder is found
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Implementation of data operations for Salesforce.
/// </summary>
public class DataService : IDataService
{
    private readonly ISalesforceClient _client;
    private readonly ISchemaService _schemaService;
    private readonly IBulkService _bulkService;
    private readonly ICacheProvider _cache;
    private readonly SalesforceOptions _options;
    private readonly ILogger<DataService> _logger;

    private const string EntityPrefixCacheKey = "EntityDefinition_Prefixes";

    /// <summary>
    /// Creates a new DataService.
    /// </summary>
    public DataService(
        ISalesforceClient client,
        ISchemaService schemaService,
        IBulkService bulkService,
        ICacheProvider cache,
        IOptions<SalesforceOptions> options,
        ILogger<DataService> logger)
    {
        _client = client;
        _schemaService = schemaService;
        _bulkService = bulkService;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    #region Query Operations

    /// <inheritdoc/>
    public async Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default)
    {
        if (_options.ValidateSoqlInputs)
        {
            if (!SecurityUtils.TryValidateSoqlQuery(soql, out var validationError))
            {
                _logger.LogWarning("SOQL validation failed: {Error}", validationError);
                if (_options.EnableDebugLogging)
                {
                    _logger.LogDebug("Rejected SOQL query: {Soql}", soql);
                }
                throw new ArgumentException(validationError ?? "Invalid SOQL query.", nameof(soql));
            }
        }

        var encodedSoql = UrlUtils.Escape(soql);
        var result = await _client.GetAsync<QueryResult>($"/query/?q={encodedSoql}", cancellationToken);
        return result;
    }

    /// <inheritdoc/>
    public async Task<QueryResult> QueryNextAsync(string nextRecordsUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(nextRecordsUrl))
            throw new ArgumentException("nextRecordsUrl is required", nameof(nextRecordsUrl));

        // The nextRecordsUrl is relative to the instance, extract just the path
        var path = nextRecordsUrl;
        if (nextRecordsUrl.Contains("/services/data/"))
        {
            var idx = nextRecordsUrl.IndexOf("/services/data/", StringComparison.OrdinalIgnoreCase);
            path = nextRecordsUrl.Substring(idx);
        }

        // Remove the API version prefix if present since the client will add it
        if (path.StartsWith($"/services/data/{_options.ApiVersion}", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring($"/services/data/{_options.ApiVersion}".Length);
        }

        return await _client.GetAsync<QueryResult>(path, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default)
    {
        var allRecords = new List<JsonObject>();
        var result = await QueryAsync(soql, cancellationToken);
        allRecords.AddRange(result.Records);

        while (!result.Done && !string.IsNullOrEmpty(result.NextRecordsUrl))
        {
            result = await QueryNextAsync(result.NextRecordsUrl, cancellationToken);
            allRecords.AddRange(result.Records);
        }

        return allRecords;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(
        string soql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(soql, cancellationToken);

        foreach (var record in result.Records)
        {
            yield return record;
        }

        while (!result.Done && !string.IsNullOrEmpty(result.NextRecordsUrl))
        {
            result = await QueryNextAsync(result.NextRecordsUrl, cancellationToken);

            foreach (var record in result.Records)
            {
                yield return record;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult> QueryPagedAsync(
        string sObject,
        IEnumerable<string> fields,
        SoqlCondition? filter = null,
        string? orderBy = null,
        bool descending = false,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        // Validate sObject
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        // Validate and sanitize fields
        var fieldList = fields?.ToList() ?? new List<string>();
        var validFields = await SanitizeReadableFieldsAsync(sObject, fieldList, cancellationToken);
        if (validFields.Count == 0)
            throw new ArgumentException("At least one valid field must be provided.", nameof(fields));

        // Ensure valid page size
        pageSize = Math.Min(Math.Max(pageSize, 1), _options!.MaxPageSize);
        page = Math.Max(page, 1);
        var offset = (page - 1) * pageSize;

        // Build SOQL query using SoqlBuilder
        var builder = new SoqlBuilder(sObject)
            .Select(validFields);

        // Apply type-safe filter condition if provided
        if (filter != null)
        {
            builder.WhereCondition(filter);
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            var parts = orderBy.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                var fieldName = tokens[0];
                if (!SecurityUtils.IsValidFieldName(fieldName))
                    throw new ArgumentException($"Invalid field in orderBy: {fieldName}", nameof(orderBy));

                bool isDesc = false;
                if (tokens.Length > 1)
                {
                    var dir = tokens[1].ToUpperInvariant();
                    if (dir != "ASC" && dir != "DESC")
                        throw new ArgumentException($"Invalid sort direction in orderBy: {dir}", nameof(orderBy));
                    isDesc = dir == "DESC";
                }
                else
                {
                    // Fallback to method parameter if not specified in string
                    isDesc = descending;
                }

                if (isDesc)
                    builder.OrderByNullsLast(fieldName, true);
                else
                    builder.OrderByNullsLast(fieldName, false);
            }
        }

        builder.Limit(pageSize + 1);
        builder.Offset(offset);

        var result = await QueryAsync(builder.Build(), cancellationToken);

        var hasNextPage = result.Records.Count > pageSize;
        var records = hasNextPage ? result.Records.Take(pageSize).ToList() : result.Records;

        return new PagedResult
        {
            Records = records,
            CurrentPage = page,
            PageSize = pageSize,
            HasNextPage = hasNextPage,
            TotalCount = result.TotalSize
        };
    }

    #endregion

    #region CRUD Operations

    /// <inheritdoc/>
    public async Task<JsonNode> GetRecordAsync(
        string sObject,
        string id,
        IEnumerable<string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        var endpoint = $"/sobjects/{sObject}/{id}";

        if (fields != null)
        {
            var validFields = await SanitizeReadableFieldsAsync(sObject, fields, cancellationToken);
            if (validFields.Count > 0)
            {
                endpoint += $"?fields={string.Join(",", validFields)}";
            }
        }

        return await _client.GetAsync(endpoint, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> CreateRecordAsync(
        string sObject,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: false,
            cancellationToken);

        var payload = FilterPayload(
            sObject,
            data,
            allowedFields,
            operation: "create",
            dropNullValues: true);

        var result = await _client.PostAsync<CreateResult>($"/sobjects/{sObject}/", payload, cancellationToken);

        if (!result.Success && result.Errors.Count > 0)
        {
            throw Models.Errors.SalesforceException.FromErrors(result.Errors);
        }

        return result.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateRecordAsync(
        string sObject,
        string id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: false,
            includeUpdateable: true,
            cancellationToken);

        var payload = FilterPayload(
            sObject,
            data,
            allowedFields,
            operation: "update",
            dropNullValues: false);

        await _client.PatchAsync($"/sobjects/{sObject}/{id}", payload, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteRecordAsync(
        string sObject,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        await _client.DeleteAsync($"/sobjects/{sObject}/{id}", cancellationToken);
    }

    #endregion

    #region Lookup Operations

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> HydrateLookupsAsync(
        JsonNode record,
        IEnumerable<SObjectField> lookupFields,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        var lookupsByTarget = new Dictionary<string, List<(string FieldName, string Id)>>();

        foreach (var field in lookupFields.Where(f => f.IsLookup))
        {
            var id = record[field.Name]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            // Resolve target object for polymorphic lookups
            string? targetObject;
            if (field.PolymorphicForeignKey)
            {
                targetObject = SalesforceConventions.GetObjectTypeFromId(id);
                if (targetObject == null) continue;
            }
            else
            {
                targetObject = field.PrimaryReferenceTo;
                if (string.IsNullOrEmpty(targetObject)) continue;
            }

            if (!lookupsByTarget.TryGetValue(targetObject, out var list))
            {
                list = new List<(string, string)>();
                lookupsByTarget[targetObject] = list;
            }
            list.Add((field.Name, id));
        }

        // Batch query each target object
        foreach (var (targetObject, lookups) in lookupsByTarget)
        {
            var ids = lookups.Select(l => l.Id).Distinct().ToList();
            var resolved = await BatchResolveLookupAsync(targetObject, ids, cancellationToken);

            foreach (var (fieldName, id) in lookups)
            {
                if (resolved.TryGetValue(id, out var displayName))
                {
                    result[fieldName] = displayName;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<string?> ResolveLookupAsync(
        string targetObject,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        var results = await BatchResolveLookupAsync(targetObject, new[] { recordId }, cancellationToken);
        return results.TryGetValue(recordId, out var name) ? name : null;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> BatchResolveLookupAsync(
        string targetObject,
        IEnumerable<string> recordIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        var idList = recordIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();

        if (idList.Count == 0) return result;

        if (!SecurityUtils.IsValidObjectName(targetObject))
        {
            _logger.LogWarning("Invalid target object name in lookup resolve: {TargetObject}", targetObject);
            return result;
        }

        var nameField = await _schemaService.GetNameFieldAsync(targetObject, cancellationToken);

        // Use SoqlBuilder for safe query construction
        var builder = new SoqlBuilder(targetObject)
            .Select("Id", nameField)
            .WhereIn("Id", idList);

        var queryResult = await QueryAsync(builder.Build(), cancellationToken);

        foreach (var record in queryResult.Records)
        {
            var id = record["Id"]?.ToString();
            var name = record[nameField]?.ToString();

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
            {
                result[id] = name;
            }
        }

        return result;
    }

    #endregion

    #region File Operations

    /// <inheritdoc/>
    public async Task<List<AttachedFile>> GetAttachedFilesAsync(
        string linkedEntityId,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidSalesforceId(linkedEntityId))
        {
            throw new ArgumentException("Invalid Salesforce ID format.", nameof(linkedEntityId));
        }

        var soql = SoqlBuilder.From("ContentDocumentLink")
            .Select(
                "ContentDocumentId",
                "ContentDocument.Title",
                "ContentDocument.FileExtension",
                "ContentDocument.ContentSize",
                "ContentDocument.CreatedDate",
                "ContentDocument.LatestPublishedVersionId")
            .WhereEquals("LinkedEntityId", linkedEntityId)
            .OrderByDescending("ContentDocument.CreatedDate")
            .Build();

        var result = await QueryAsync(soql, cancellationToken);
        var files = new List<AttachedFile>();

        foreach (var record in result.Records)
        {
            var doc = record["ContentDocument"] as JsonObject;
            if (doc == null) continue;

            files.Add(new AttachedFile
            {
                Id = record["ContentDocumentId"]?.ToString() ?? "",
                ContentDocumentId = record["ContentDocumentId"]?.ToString() ?? "",
                ContentVersionId = doc["LatestPublishedVersionId"]?.ToString() ?? "",
                Title = doc["Title"]?.ToString() ?? "",
                FileExtension = doc["FileExtension"]?.ToString() ?? "",
                ContentSize = doc["ContentSize"]?.GetValue<long>() ?? 0,
                CreatedDate = doc["CreatedDate"].ParseDateTimeOrDefault(DateTime.MinValue)
            });
        }

        return files;
    }

    /// <inheritdoc/>
    public async Task<string> UploadFileAsync(
        string linkedEntityId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var base64Content = Convert.ToBase64String(content);
        var title = Path.GetFileNameWithoutExtension(fileName);

        var payload = new
        {
            Title = title,
            PathOnClient = fileName,
            VersionData = base64Content,
            FirstPublishLocationId = linkedEntityId
        };

        var result = await _client.PostAsync<CreateResult>("/sobjects/ContentVersion/", payload, cancellationToken);

        if (!result.Success && result.Errors.Count > 0)
        {
            throw Models.Errors.SalesforceException.FromErrors(result.Errors);
        }

        return result.Id;
    }

    /// <inheritdoc/>
    public async Task<string> UploadFileAsync(
        string linkedEntityId,
        string fileName,
        Stream contentStream,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        // Read stream directly into a buffer sized to the content length
        // This avoids the double-copy when using MemoryStream.ToArray()
        var buffer = new byte[contentLength];
        var totalRead = 0;
        while (totalRead < contentLength)
        {
            var bytesRead = await contentStream.ReadAsync(
                buffer.AsMemory(totalRead, (int)(contentLength - totalRead)),
                cancellationToken);

            if (bytesRead == 0)
                break;

            totalRead += bytesRead;
        }

        return await UploadFileAsync(linkedEntityId, fileName, buffer, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetFileContentAsync(
        string contentVersionId,
        CancellationToken cancellationToken = default)
    {
        return await _client.GetBytesAsync(
            $"/sobjects/ContentVersion/{contentVersionId}/VersionData",
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteFileAsync(
        string contentDocumentId,
        CancellationToken cancellationToken = default)
    {
        await _client.DeleteAsync($"/sobjects/ContentDocument/{contentDocumentId}", cancellationToken);
    }

    #endregion

    #region User Operations

    /// <inheritdoc/>
    public async Task<JsonNode> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        return await _client.GetAsync("/services/oauth2/userinfo", cancellationToken);
    }

    #endregion

    #region Recent Items

    /// <inheritdoc/>
    public async Task<List<RecentItem>> GetRecentItemsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        // Enforce valid limit range (1-200)
        limit = Math.Max(1, Math.Min(limit, 200));

        var endpoint = $"{SalesforceConstants.Paths.Recent}?limit={limit}";
        var response = await _client.GetAsync(endpoint, cancellationToken);
        var responseArray = response.AsArray();

        if (responseArray == null)
        {
            _logger.LogWarning("Recent items response did not contain an array. Returning empty list.");
            return new List<RecentItem>();
        }

        var items = new List<RecentItem>();

        foreach (var itemNode in responseArray)
        {
            if (itemNode is not JsonObject item)
            {
                _logger.LogWarning("Skipping malformed recent item entry in response.");
                continue;
            }

            var recentItem = new RecentItem
            {
                Id = item["Id"]?.ToString() ?? string.Empty,
                Name = item["Name"]?.ToString() ?? string.Empty
            };

            var attributes = item["attributes"] as JsonObject;
            if (attributes != null)
            {
                recentItem.Type = attributes["type"]?.ToString() ?? string.Empty;
                recentItem.Attributes = new RecordAttributes
                {
                    Type = attributes["type"]?.ToString() ?? string.Empty,
                    Url = attributes["url"]?.ToString() ?? string.Empty
                };
            }

            items.Add(recentItem);
        }

        return items;
    }

    #endregion

    #region Upsert Operations

    /// <inheritdoc/>
    public async Task<UpsertResult> UpsertRecordAsync(
        string sObject,
        string externalIdField,
        string externalIdValue,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sObject))
            throw new ArgumentException("sObject is required", nameof(sObject));
        if (!SecurityUtils.IsValidObjectName(sObject))
            throw new ArgumentException($"Invalid object name: {sObject}", nameof(sObject));

        if (string.IsNullOrWhiteSpace(externalIdField))
            throw new ArgumentException("externalIdField is required", nameof(externalIdField));
        if (!SecurityUtils.IsValidFieldName(externalIdField))
            throw new ArgumentException($"Invalid external ID field: {externalIdField}", nameof(externalIdField));

        if (string.IsNullOrWhiteSpace(externalIdValue))
            throw new ArgumentException("externalIdValue is required", nameof(externalIdValue));

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: true,
            cancellationToken);

        var excludedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            externalIdField
        };

        var payload = FilterPayload(
            sObject,
            data,
            allowedFields,
            operation: "upsert",
            dropNullValues: false,
            excludedFields: excludedFields);

        var endpoint = $"/sobjects/{sObject}/{externalIdField}/{Uri.EscapeDataString(externalIdValue)}";

        try
        {
            var response = await _client.PatchAsync<JsonNode>(endpoint, payload, cancellationToken);

            return new UpsertResult
            {
                Id = response["id"]?.ToString() ?? string.Empty,
                Created = response["created"]?.GetValue<bool>() ?? false,
                Success = true
            };
        }
        catch (Models.Errors.SalesforceException ex)
        {
            return new UpsertResult
            {
                Success = false,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    #endregion

    #region Batch Operations

    /// <inheritdoc/>
    public async Task<BatchResult> BatchCreateAsync(
        string sObject,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new BatchResult { SuccessCount = 0, FailureCount = 0 };

        // Use Bulk API for large datasets
        if (recordList.Count > bulkThreshold)
        {
            return await ExecuteBulkInsertAsync(sObject, recordList, cancellationToken);
        }

        // Use sObject Collections for smaller datasets
        return await ExecuteCompositeCreateAsync(sObject, recordList, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BatchResult> BatchUpdateAsync(
        string sObject,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new BatchResult { SuccessCount = 0, FailureCount = 0 };

        if (recordList.Count > bulkThreshold)
        {
            return await ExecuteBulkUpdateAsync(sObject, recordList, cancellationToken);
        }

        return await ExecuteCompositeUpdateAsync(sObject, recordList, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BatchResult> BatchUpsertAsync(
        string sObject,
        string externalIdField,
        IEnumerable<IDictionary<string, object?>> records,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new BatchResult { SuccessCount = 0, FailureCount = 0 };

        if (recordList.Count > bulkThreshold)
        {
            return await ExecuteBulkUpsertAsync(sObject, externalIdField, recordList, cancellationToken);
        }

        return await ExecuteCompositeUpsertAsync(sObject, externalIdField, recordList, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BatchResult> BatchDeleteAsync(
        string sObject,
        IEnumerable<string> ids,
        int bulkThreshold = 200,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return new BatchResult { SuccessCount = 0, FailureCount = 0 };

        if (idList.Count > bulkThreshold)
        {
            return await ExecuteBulkDeleteAsync(sObject, idList, cancellationToken);
        }

        return await ExecuteCompositeDeleteAsync(sObject, idList, cancellationToken);
    }

    #region Bulk API Helpers

    private async Task<BatchResult> ExecuteBulkInsertAsync(
        string sObject,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using Bulk API to insert {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: false,
            cancellationToken);

        var filteredRecords = FilterRecordBatch(
            sObject,
            records,
            allowedFields,
            operation: "bulk insert",
            dropNullValues: true);

        var bulkResult = await _bulkService.InsertAsync(sObject, filteredRecords, cancellationToken: cancellationToken);
        return ConvertBulkResult(bulkResult, true);
    }

    private async Task<BatchResult> ExecuteBulkUpdateAsync(
        string sObject,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using Bulk API to update {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: false,
            includeUpdateable: true,
            cancellationToken);
        allowedFields.Add("Id"); // Id is required for updates

        var filteredRecords = FilterRecordBatch(
            sObject,
            records,
            allowedFields,
            operation: "bulk update",
            dropNullValues: false);

        var bulkResult = await _bulkService.UpdateAsync(sObject, filteredRecords, cancellationToken: cancellationToken);
        return ConvertBulkResult(bulkResult, true);
    }

    private async Task<BatchResult> ExecuteBulkUpsertAsync(
        string sObject,
        string externalIdField,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using Bulk API to upsert {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: true,
            cancellationToken);
        allowedFields.Add(externalIdField);

        var filteredRecords = FilterRecordBatch(
            sObject,
            records,
            allowedFields,
            operation: "bulk upsert",
            dropNullValues: false);

        var bulkResult = await _bulkService.UpsertAsync(sObject, externalIdField, filteredRecords, cancellationToken: cancellationToken);
        return ConvertBulkResult(bulkResult, true);
    }

    private async Task<BatchResult> ExecuteBulkDeleteAsync(
        string sObject,
        List<string> ids,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using Bulk API to delete {Count} {Object} records", ids.Count, sObject);

        var bulkResult = await _bulkService.DeleteAsync(sObject, ids, cancellationToken: cancellationToken);
        return ConvertBulkResult(bulkResult, true);
    }

    private static BatchResult ConvertBulkResult(BulkJobResults bulkResult, bool usedBulk)
    {
        var result = new BatchResult
        {
            UsedBulkApi = usedBulk,
            SuccessCount = bulkResult.SuccessfulRecords?.Count ?? 0,
            FailureCount = bulkResult.FailedRecords?.Count ?? 0
        };

        if (bulkResult.SuccessfulRecords != null)
        {
            result.SuccessfulIds = bulkResult.SuccessfulRecords
                .Where(r => !string.IsNullOrEmpty(r.Id))
                .Select(r => r.Id!)
                .ToList();
        }

        if (bulkResult.FailedRecords != null)
        {
            result.FailedRecords = bulkResult.FailedRecords.Select(r => new BatchRecordError
            {
                Index = r.RowNumber,
                Message = r.Error ?? "Unknown error"
            }).ToList();
        }

        return result;
    }

    #endregion

    #region Composite API Helpers

    private async Task<BatchResult> ExecuteCompositeCreateAsync(
        string sObject,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using sObject Collections to create {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: false,
            cancellationToken);

        var payload = new
        {
            allOrNone = false,
            records = FilterRecordBatch(
                sObject,
                records,
                allowedFields,
                operation: "composite create",
                dropNullValues: true)
                .Select(filtered =>
                {
                    filtered["attributes"] = new Dictionary<string, string> { { "type", sObject } };
                    return filtered;
                }).ToList()
        };

        var response = await _client.PostAsync<JsonArray>("/composite/sobjects", payload, cancellationToken);
        return ParseCompositeResponse(response, records.Count);
    }

    private async Task<BatchResult> ExecuteCompositeUpdateAsync(
        string sObject,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using sObject Collections to update {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: false,
            includeUpdateable: true,
            cancellationToken);
        allowedFields.Add("Id");

        var payload = new
        {
            allOrNone = false,
            records = FilterRecordBatch(
                sObject,
                records,
                allowedFields,
                operation: "composite update",
                dropNullValues: false)
                .Select(filtered =>
                {
                    filtered["attributes"] = new Dictionary<string, string> { { "type", sObject } };
                    return filtered;
                }).ToList()
        };

        var response = await _client.PatchAsync<JsonArray>("/composite/sobjects", payload, cancellationToken);
        return ParseCompositeResponse(response, records.Count);
    }

    private async Task<BatchResult> ExecuteCompositeUpsertAsync(
        string sObject,
        string externalIdField,
        List<IDictionary<string, object?>> records,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using Composite API to upsert {Count} {Object} records", records.Count, sObject);

        var allowedFields = await GetAllowedWriteFieldNamesAsync(
            sObject,
            includeCreateable: true,
            includeUpdateable: true,
            cancellationToken);

        // Composite API Request Builder
        var compositeRequest = new Models.Data.CompositeRequest
        {
            AllOrNone = false,
            CollateSubrequests = false
        };

        var recordIndices = new Dictionary<string, int>();

        foreach (var (record, index) in records.Select((r, i) => (r, i)))
        {
            if (!record.TryGetValue(externalIdField, out var extIdValue) || extIdValue == null)
            {
                // Cannot include in batch if no external ID
                continue;
            }

            var extId = extIdValue.ToString()!;
            var refId = $"ref{index}";
            recordIndices[refId] = index;

            // Filter payload
            var payload = FilterPayload(
                sObject,
                record,
                allowedFields,
                operation: "composite upsert",
                dropNullValues: false,
                excludedFields: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { externalIdField });

            compositeRequest.CompositeSubRequests.Add(new Models.Data.CompositeSubRequest
            {
                Method = "PATCH",
                Url = $"/services/data/{_options.ApiVersion}/sobjects/{sObject}/{externalIdField}/{Uri.EscapeDataString(extId)}",
                ReferenceId = refId,
                Body = payload
            });
        }

        if (compositeRequest.CompositeSubRequests.Count == 0)
        {
             // All records were missing external ID
             var emptyResult = new BatchResult { UsedBulkApi = false, FailureCount = records.Count };
             for(int i=0; i<records.Count; i++)
             {
                 emptyResult.FailedRecords.Add(new BatchRecordError { Index = i, Message = $"Missing external ID field: {externalIdField}" });
             }
             return emptyResult;
        }

        // Execute Composite Request
        // Note: Composite API limit is 25 subrequests. If records > 25, we must loop in batches of 25.
        // The calling method checks 'bulkThreshold' (default 200). If bulkThreshold > 25, we still need to chunk here.

        var result = new BatchResult { UsedBulkApi = false };
        var chunks = compositeRequest.CompositeSubRequests.Chunk(25);

        foreach (var chunk in chunks)
        {
            var chunkRequest = new Models.Data.CompositeRequest
            {
                AllOrNone = false,
                CompositeSubRequests = chunk.ToList()
            };

            try
            {
                var chunkResponse = await _client.PostAsync<Models.Data.CompositeResponse>("/composite", chunkRequest, cancellationToken);

                foreach (var subResponse in chunkResponse.CompositeSubResponses)
                {
                    if (recordIndices.TryGetValue(subResponse.ReferenceId, out var originalIndex))
                    {
                        bool success = subResponse.HttpStatusCode >= 200 && subResponse.HttpStatusCode < 300;
                        if (success)
                        {
                            result.SuccessCount++;
                            if (subResponse.Body != null)
                            {
                                var id = subResponse.Body["id"]?.ToString();
                                if (!string.IsNullOrEmpty(id)) result.SuccessfulIds.Add(id);
                            }
                        }
                        else
                        {
                            result.FailureCount++;
                            string errorMsg = "Unknown error";
                            string? errorCode = null;
                            List<string> fields = new();

                            if (subResponse.Body is JsonArray errorsArray && errorsArray.Count > 0)
                            {
                                var firstError = errorsArray[0];
                                errorMsg = firstError?["message"]?.ToString() ?? errorMsg;
                                errorCode = firstError?["errorCode"]?.ToString();
                                var fieldsNode = firstError?["fields"];
                                fields = fieldsNode != null ? JsonSerializer.Deserialize<List<string>>(fieldsNode) ?? new List<string>() : new List<string>();
                            }

                            result.FailedRecords.Add(new BatchRecordError
                            {
                                Index = originalIndex,
                                Message = errorMsg,
                                ErrorCode = errorCode,
                                Fields = fields
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Entire chunk failed (e.g. network)
                _logger.LogError(ex, "Composite upsert chunk failed");
                foreach (var req in chunk)
                {
                    if (recordIndices.TryGetValue(req.ReferenceId, out var originalIndex))
                    {
                        result.FailureCount++;
                        result.FailedRecords.Add(new BatchRecordError { Index = originalIndex, Message = ex.Message });
                    }
                }
            }
        }

        // Add failures for records that were skipped initially
        for(int i=0; i<records.Count; i++)
        {
             // Check if this index was processed
             bool processed = recordIndices.Values.Contains(i);
             if (!processed)
             {
                 result.FailureCount++;
                 result.FailedRecords.Add(new BatchRecordError { Index = i, Message = $"Missing external ID field: {externalIdField}" });
             }
        }

        return result;
    }

    private async Task<BatchResult> ExecuteCompositeDeleteAsync(
        string sObject,
        List<string> ids,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Using sObject Collections to delete {Count} {Object} records", ids.Count, sObject);

        // Salesforce composite delete endpoint
        var idsParam = string.Join(",", ids);
        var endpoint = $"/composite/sobjects?ids={idsParam}&allOrNone=false";

        var response = await _client.DeleteAsync<JsonArray>(endpoint, cancellationToken);
        return ParseCompositeResponse(response, ids.Count);
    }

    private static BatchResult ParseCompositeResponse(JsonArray response, int totalCount)
    {
        var result = new BatchResult { UsedBulkApi = false };

        for (int i = 0; i < response.Count; i++)
        {
            var item = response[i] as JsonObject;
            if (item == null) continue;

            var success = item["success"]?.GetValue<bool>() ?? false;
            if (success)
            {
                result.SuccessCount++;
                var id = item["id"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                    result.SuccessfulIds.Add(id);
            }
            else
            {
                result.FailureCount++;
                var errors = item["errors"] as JsonArray;
                var errorMessages = errors?.Select(e => e?["message"]?.ToString() ?? "Unknown error").ToList()
                    ?? new List<string> { "Unknown error" };

                var fieldsNode = errors?.FirstOrDefault()?["fields"];
                result.FailedRecords.Add(new BatchRecordError
                {
                    Index = i,
                    ErrorCode = errors?.FirstOrDefault()?["statusCode"]?.ToString(),
                    Message = string.Join("; ", errorMessages),
                    Fields = fieldsNode != null ? JsonSerializer.Deserialize<List<string>>(fieldsNode) ?? new List<string>() : new List<string>()
                });
            }
        }

        return result;
    }

    #endregion

    #endregion

    #region Helpers

    private async Task<List<string>> SanitizeReadableFieldsAsync(
        string sObject,
        IEnumerable<string> fields,
        CancellationToken cancellationToken)
    {
        var sanitizedFields = await _schemaService.SanitizeFieldListAsync(sObject, fields, cancellationToken)
            ?? new List<string>();

        if (!_options.EnforceFieldLevelSecurity)
        {
            return sanitizedFields;
        }

        var flsFields = await _schemaService.SanitizeFieldListWithFlsAsync(sObject, sanitizedFields, cancellationToken);
        var droppedFields = sanitizedFields.Except(flsFields, StringComparer.OrdinalIgnoreCase).ToList();
        LogDroppedFields(sObject, droppedFields, "read");
        return flsFields;
    }

    private async Task<HashSet<string>> GetAllowedWriteFieldNamesAsync(
        string sObject,
        bool includeCreateable,
        bool includeUpdateable,
        CancellationToken cancellationToken)
    {
        if (_options.EnforceFieldLevelSecurity)
        {
            var fields = new List<SObjectField>();

            if (includeCreateable)
            {
                fields.AddRange(await _schemaService.GetCreateableFieldsAsync(sObject, cancellationToken));
            }

            if (includeUpdateable)
            {
                fields.AddRange(await _schemaService.GetUpdateableFieldsAsync(sObject, cancellationToken));
            }

            return fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var allFields = await _schemaService.GetFieldsAsync(sObject, cancellationToken);
        return allFields
            .Where(IsWriteableField)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsWriteableField(SObjectField field)
    {
        if (field.DeprecatedAndHidden)
        {
            return false;
        }

        if (field.Calculated || field.AutoNumber)
        {
            return false;
        }

        return !SalesforceConventions.NonCreateableFieldTypes.Contains(field.Type);
    }

    private Dictionary<string, object?> FilterPayload(
        string sObject,
        IDictionary<string, object?> data,
        HashSet<string> allowedFields,
        string operation,
        bool dropNullValues,
        HashSet<string>? excludedFields = null)
    {
        // If FLS is disabled or mode is None, bypass filtering entirely
        if (!_options.EnforceFieldLevelSecurity || _options.FlsEnforcementMode == FlsEnforcementMode.None)
        {
            var bypass = new Dictionary<string, object?>();
            foreach (var (key, value) in data)
            {
                if (excludedFields != null && excludedFields.Contains(key))
                    continue;
                if (dropNullValues && value == null)
                    continue;
                bypass[key] = value;
            }
            return bypass;
        }

        var payload = new Dictionary<string, object?>();
        var droppedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in data)
        {
            if (excludedFields != null && excludedFields.Contains(key))
            {
                continue;
            }

            if (!allowedFields.Contains(key))
            {
                droppedFields.Add(key);
                continue;
            }

            if (dropNullValues && value == null)
            {
                continue;
            }

            payload[key] = value;
        }

        // In Strict mode, throw an exception if any fields were dropped
        if (_options.FlsEnforcementMode == FlsEnforcementMode.Strict && droppedFields.Count > 0)
        {
            var flsOperation = operation.Equals("create", StringComparison.OrdinalIgnoreCase)
                ? FlsOperation.Create
                : FlsOperation.Update;

            var violations = droppedFields.Select(f => new FlsViolation(
                f,
                f,
                flsOperation,
                $"Field '{f}' is not accessible for {operation} on {sObject}.")).ToList();

            throw new FlsException(violations);
        }

        LogDroppedFields(sObject, droppedFields, operation);
        return payload;
    }

    private List<Dictionary<string, object?>> FilterRecordBatch(
        string sObject,
        IEnumerable<IDictionary<string, object?>> records,
        HashSet<string> allowedFields,
        string operation,
        bool dropNullValues,
        HashSet<string>? excludedFields = null)
    {
        var result = new List<Dictionary<string, object?>>();

        // If FLS is disabled or mode is None, bypass filtering entirely
        if (!_options.EnforceFieldLevelSecurity || _options.FlsEnforcementMode == FlsEnforcementMode.None)
        {
            foreach (var record in records)
            {
                var bypass = new Dictionary<string, object?>();
                foreach (var (key, value) in record)
                {
                    if (excludedFields != null && excludedFields.Contains(key))
                        continue;
                    if (dropNullValues && value == null)
                        continue;
                    bypass[key] = value;
                }
                result.Add(bypass);
            }
            return result;
        }

        var droppedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var payload = new Dictionary<string, object?>();

            foreach (var (key, value) in record)
            {
                if (excludedFields != null && excludedFields.Contains(key))
                {
                    continue;
                }

                if (!allowedFields.Contains(key))
                {
                    droppedFields.Add(key);
                    continue;
                }

                if (dropNullValues && value == null)
                {
                    continue;
                }

                payload[key] = value;
            }

            result.Add(payload);
        }

        // In Strict mode, throw an exception if any fields were dropped
        if (_options.FlsEnforcementMode == FlsEnforcementMode.Strict && droppedFields.Count > 0)
        {
            var flsOperation = operation.Contains("insert", StringComparison.OrdinalIgnoreCase)
                ? FlsOperation.Create
                : FlsOperation.Update;

            var violations = droppedFields.Select(f => new FlsViolation(
                f,
                f,
                flsOperation,
                $"Field '{f}' is not accessible for {operation} on {sObject}.")).ToList();

            throw new FlsException(violations);
        }

        LogDroppedFields(sObject, droppedFields, operation);
        return result;
    }

    private void LogDroppedFields(string sObject, IEnumerable<string> droppedFields, string operation)
    {
        var droppedList = droppedFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (droppedList.Count == 0)
        {
            return;
        }

        var reason = _options.EnforceFieldLevelSecurity ? "FLS enforcement" : "schema validation";
        _logger.LogWarning(
            "Fields dropped during {Operation} for {SObject} due to {Reason}: {Fields}",
            operation,
            sObject,
            reason,
            string.Join(", ", droppedList));
    }

    #endregion

    #region Polymorphic Lookup Operations

    /// <inheritdoc/>
    public async Task<string?> ResolvePolymorphicTypeAsync(string recordId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recordId) || recordId.Length < 3)
            return null;

        // First try the static prefix map for standard objects
        var staticResult = SalesforceConventions.GetObjectTypeFromId(recordId);
        if (staticResult != null)
            return staticResult;

        // For custom objects or unknown prefixes, use EntityDefinition
        var prefix = recordId.Substring(0, 3);
        var prefixMap = await GetEntityPrefixMapAsync(cancellationToken);

        return prefixMap.TryGetValue(prefix, out var objectType) ? objectType : null;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> BatchResolvePolymorphicTypesAsync(
        IEnumerable<string> recordIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();
        var idsToResolve = new HashSet<string>();

        // First pass: resolve using static map
        foreach (var id in recordIds.Where(id => !string.IsNullOrEmpty(id) && id.Length >= 3))
        {
            var staticResult = SalesforceConventions.GetObjectTypeFromId(id);
            if (staticResult != null)
            {
                result[id] = staticResult;
            }
            else
            {
                idsToResolve.Add(id);
            }
        }

        // Second pass: resolve remaining using EntityDefinition
        if (idsToResolve.Count > 0)
        {
            var prefixMap = await GetEntityPrefixMapAsync(cancellationToken);
            foreach (var id in idsToResolve)
            {
                var prefix = id.Substring(0, 3);
                if (prefixMap.TryGetValue(prefix, out var objectType))
                {
                    result[id] = objectType;
                }
            }
        }

        return result;
    }

    private async Task<Dictionary<string, string>> GetEntityPrefixMapAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(
            EntityPrefixCacheKey,
            async ct =>
            {
                try
                {
                    // Query EntityDefinition for all objects and their key prefixes
                    var soql = SoqlBuilder.From("EntityDefinition")
                        .Select("QualifiedApiName", "KeyPrefix")
                        .WhereNotNull("KeyPrefix")
                        .Build();
                    var records = await QueryAllAsync(soql, ct);

                    var prefixMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var record in records)
                    {
                        var prefix = record["KeyPrefix"]?.ToString();
                        var apiName = record["QualifiedApiName"]?.ToString();
                        if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(apiName))
                        {
                            prefixMap[prefix] = apiName;
                        }
                    }

                    return prefixMap;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch EntityDefinition prefixes");
                    return new Dictionary<string, string>();
                }
            },
            _options.SchemaCacheDuration,
            cancellationToken) ?? new Dictionary<string, string>();
    }

    #endregion
}
