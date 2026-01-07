using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Tracking;

/// <summary>
/// Service for querying Salesforce field history tracking records.
/// </summary>
public interface IFieldHistoryService
{
    /// <summary>
    /// Gets the field history for a record.
    /// </summary>
    /// <param name="objectName">Object API name (e.g., "Account").</param>
    /// <param name="recordId">Record ID.</param>
    /// <param name="fieldNames">Optional specific fields to retrieve history for.</param>
    /// <param name="limit">Maximum number of history records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of field history entries.</returns>
    Task<List<FieldHistoryEntry>> GetHistoryAsync(
        string objectName,
        string recordId,
        IEnumerable<string>? fieldNames = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all changes made to a record in a date range.
    /// </summary>
    Task<List<FieldHistoryEntry>> GetHistoryInRangeAsync(
        string objectName,
        string recordId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets changes made by a specific user.
    /// </summary>
    Task<List<FieldHistoryEntry>> GetHistoryByUserAsync(
        string objectName,
        string recordId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of changes grouped by field.
    /// </summary>
    Task<Dictionary<string, List<FieldHistoryEntry>>> GetHistoryByFieldAsync(
        string objectName,
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last value a field had before a specific date.
    /// </summary>
    Task<object?> GetFieldValueAtAsync(
        string objectName,
        string recordId,
        string fieldName,
        DateTime asOfDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if history tracking is enabled for an object.
    /// </summary>
    Task<bool> IsHistoryEnabledAsync(string objectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of fields being tracked for an object.
    /// </summary>
    Task<List<string>> GetTrackedFieldsAsync(string objectName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single field history entry.
/// </summary>
public class FieldHistoryEntry
{
    /// <summary>
    /// History record ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Parent record ID.
    /// </summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>
    /// Field API name that was changed.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable field label.
    /// </summary>
    public string? FieldLabel { get; set; }

    /// <summary>
    /// Previous value (may be null for text > 255 chars).
    /// </summary>
    public object? OldValue { get; set; }

    /// <summary>
    /// New value (may be null for text > 255 chars).
    /// </summary>
    public object? NewValue { get; set; }

    /// <summary>
    /// When the change was made.
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// ID of user who made the change.
    /// </summary>
    public string CreatedById { get; set; } = string.Empty;

    /// <summary>
    /// Name of user who made the change.
    /// </summary>
    public string? CreatedByName { get; set; }

    /// <summary>
    /// Data type of the field.
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    /// Whether the old/new values were truncated due to length.
    /// </summary>
    public bool ValuesTruncated { get; set; }
}

/// <summary>
/// Implementation of field history service.
/// </summary>
public class FieldHistoryService : IFieldHistoryService
{
    private readonly IDataService _dataService;
    private readonly ISchemaService _schemaService;

    // Standard objects that support history tracking
    private static readonly HashSet<string> StandardHistoryObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account", "Asset", "Case", "Contact", "Contract", "ContractLineItem",
        "Lead", "Opportunity", "Order", "OrderItem", "Product2", "Quote",
        "QuoteLineItem", "ServiceContract", "Solution", "User", "WorkOrder",
        "WorkOrderLineItem"
    };

    public FieldHistoryService(IDataService dataService, ISchemaService schemaService)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    }

    /// <inheritdoc/>
    public async Task<List<FieldHistoryEntry>> GetHistoryAsync(
        string objectName,
        string recordId,
        IEnumerable<string>? fieldNames = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);
        ValidateRecordId(recordId);

        var historyObjectName = GetHistoryObjectName(objectName);

        var query = SoqlBuilder.From(historyObjectName)
            .Select("Id", "ParentId", "Field", "OldValue", "NewValue", "CreatedDate", "CreatedById", "DataType")
            .SelectSubQuery("CreatedBy", sub => sub.Select("Name"))
            .WhereEquals(GetParentIdField(objectName), recordId)
            .OrderByDescending("CreatedDate")
            .Limit(limit);

        if (fieldNames?.Any() == true)
        {
            query.WhereIn("Field", fieldNames.Cast<object?>());
        }

        var result = await _dataService.QueryAsync(query.Build(), cancellationToken);
        return MapHistoryResults(result);
    }

    /// <inheritdoc/>
    public async Task<List<FieldHistoryEntry>> GetHistoryInRangeAsync(
        string objectName,
        string recordId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);
        ValidateRecordId(recordId);

        var historyObjectName = GetHistoryObjectName(objectName);

        var query = SoqlBuilder.From(historyObjectName)
            .Select("Id", "ParentId", "Field", "OldValue", "NewValue", "CreatedDate", "CreatedById", "DataType")
            .SelectSubQuery("CreatedBy", sub => sub.Select("Name"))
            .WhereEquals(GetParentIdField(objectName), recordId)
            .WhereDateBetween("CreatedDate", startDate, endDate)
            .OrderByDescending("CreatedDate");

        var result = await _dataService.QueryAsync(query.Build(), cancellationToken);
        return MapHistoryResults(result);
    }

    /// <inheritdoc/>
    public async Task<List<FieldHistoryEntry>> GetHistoryByUserAsync(
        string objectName,
        string recordId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);
        ValidateRecordId(recordId);
        ValidateRecordId(userId, nameof(userId));

        var historyObjectName = GetHistoryObjectName(objectName);

        var query = SoqlBuilder.From(historyObjectName)
            .Select("Id", "ParentId", "Field", "OldValue", "NewValue", "CreatedDate", "CreatedById", "DataType")
            .SelectSubQuery("CreatedBy", sub => sub.Select("Name"))
            .WhereEquals(GetParentIdField(objectName), recordId)
            .WhereEquals("CreatedById", userId)
            .OrderByDescending("CreatedDate");

        var result = await _dataService.QueryAsync(query.Build(), cancellationToken);
        return MapHistoryResults(result);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, List<FieldHistoryEntry>>> GetHistoryByFieldAsync(
        string objectName,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        // Validation performed by GetHistoryAsync
        var history = await GetHistoryAsync(objectName, recordId, limit: 500, cancellationToken: cancellationToken);

        return history
            .GroupBy(h => h.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.CreatedDate).ToList());
    }

    /// <inheritdoc/>
    public async Task<object?> GetFieldValueAtAsync(
        string objectName,
        string recordId,
        string fieldName,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);
        ValidateRecordId(recordId);
        ValidateFieldName(fieldName);

        var historyObjectName = GetHistoryObjectName(objectName);

        // Get the most recent change before the specified date
        var query = SoqlBuilder.From(historyObjectName)
            .Select("OldValue", "NewValue", "CreatedDate")
            .WhereEquals(GetParentIdField(objectName), recordId)
            .WhereEquals("Field", fieldName)
            .WhereLessThanOrEqual("CreatedDate", asOfDate)
            .OrderByDescending("CreatedDate")
            .Limit(1);

        var result = await _dataService.QueryAsync(query.Build(), cancellationToken);

        if (result.TotalSize == 0)
        {
            // No history before that date - get the first change and return OldValue
            var firstQuery = SoqlBuilder.From(historyObjectName)
                .Select("OldValue")
                .WhereEquals(GetParentIdField(objectName), recordId)
                .WhereEquals("Field", fieldName)
                .OrderBy("CreatedDate")
                .Limit(1);

            var firstResult = await _dataService.QueryAsync(firstQuery.Build(), cancellationToken);

            if (firstResult.TotalSize > 0)
            {
                return firstResult.Records[0]["OldValue"];
            }

            // No history at all - get current value using type-safe query builder
            var currentQuery = SoqlBuilder.From(objectName)
                .Select(fieldName)
                .WhereEquals("Id", recordId)
                .Build();
            var currentResult = await _dataService.QueryAsync(currentQuery, cancellationToken);

            if (currentResult.TotalSize > 0)
            {
                return currentResult.Records[0][fieldName];
            }

            return null;
        }

        // Return the NewValue from the most recent change before asOfDate
        return result.Records[0]["NewValue"];
    }

    /// <inheritdoc/>
    public async Task<bool> IsHistoryEnabledAsync(string objectName, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);

        var historyObjectName = GetHistoryObjectName(objectName);

        try
        {
            // Try to describe the history object
            var describeResult = await _schemaService.GetDescribeAsync(historyObjectName, cancellationToken);
            return describeResult != null;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetTrackedFieldsAsync(string objectName, CancellationToken cancellationToken = default)
    {
        ValidateObjectName(objectName);

        var historyObjectName = GetHistoryObjectName(objectName);

        // Query distinct Field values from history using type-safe query builder
        var query = SoqlBuilder.From(historyObjectName)
            .Select("Field")
            .GroupBy("Field")
            .Limit(200)
            .Build();

        try
        {
            var result = await _dataService.QueryAsync(query, cancellationToken);

            return result.Records
                .Select(r => r["Field"]?.ToString() ?? "")
                .Where(f => !string.IsNullOrEmpty(f) && !f.StartsWith("_")) // Exclude system fields
                .Distinct()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string GetHistoryObjectName(string objectName)
    {
        // Standard objects use [Object]History, custom objects use [Object]__History
        if (objectName.EndsWith("__c", StringComparison.OrdinalIgnoreCase))
        {
            // Custom object: Account__c -> Account__History
            return objectName[..^3] + "__History";
        }

        // Standard object: Account -> AccountHistory
        return objectName + "History";
    }

    private static string GetParentIdField(string objectName)
    {
        // Standard objects use [Object]Id, custom objects use ParentId
        if (objectName.EndsWith("__c", StringComparison.OrdinalIgnoreCase))
        {
            return "ParentId";
        }

        return objectName + "Id";
    }

    private static List<FieldHistoryEntry> MapHistoryResults(QueryResult result)
    {
        var entries = new List<FieldHistoryEntry>();

        foreach (var record in result.Records)
        {
            var entry = new FieldHistoryEntry
            {
                Id = record["Id"]?.ToString() ?? "",
                ParentId = record["ParentId"]?.ToString() ?? "",
                Field = record["Field"]?.ToString() ?? "",
                OldValue = GetHistoryValue(record["OldValue"]),
                NewValue = GetHistoryValue(record["NewValue"]),
                CreatedById = record["CreatedById"]?.ToString() ?? "",
                DataType = record["DataType"]?.ToString()
            };

            // Parse CreatedDate
            if (record["CreatedDate"] != null)
            {
                if (DateTime.TryParse(record["CreatedDate"]!.ToString(), out var createdDate))
                    entry.CreatedDate = createdDate;
            }

            // Get CreatedBy name from subquery
            if (record["CreatedBy"] is JsonObject createdBy)
            {
                entry.CreatedByName = createdBy["Name"]?.ToString();
            }

            // Check if values might be truncated (long text fields)
            if (entry.DataType == "TextArea" &&
                (entry.OldValue == null || entry.NewValue == null))
            {
                entry.ValuesTruncated = true;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static object? GetHistoryValue(JsonNode? token)
    {
        if (token == null || token.GetValueKind() == JsonValueKind.Null)
            return null;

        // For reference fields, Salesforce might return an object with Name
        if (token is JsonObject obj && obj.ContainsKey("Name"))
        {
            return obj["Name"]?.ToString();
        }

        return JsonSerializer.Deserialize<object>(token);
    }

    #region Input Validation

    private static void ValidateObjectName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));
    }

    private static void ValidateRecordId(string recordId, string paramName = "recordId")
    {
        if (string.IsNullOrWhiteSpace(recordId))
            throw new ArgumentException("Record ID is required", paramName);

        if (!SecurityUtils.IsValidSalesforceId(recordId))
            throw new ArgumentException($"Invalid Salesforce ID: {recordId}", paramName);
    }

    private static void ValidateFieldName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required", nameof(fieldName));

        if (!SecurityUtils.IsValidFieldName(fieldName))
            throw new ArgumentException($"Invalid field name: {fieldName}", nameof(fieldName));
    }

    #endregion
}
