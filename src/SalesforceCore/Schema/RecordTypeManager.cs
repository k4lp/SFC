using System.Text.Json;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Metadata;
using RecordTypeInfoModel = SalesforceCore.Models.Metadata.RecordTypeInfo;

namespace SalesforceCore.Schema;

/// <summary>
/// Service for managing record types and their picklist value restrictions.
/// </summary>
public interface IRecordTypeManager
{
    /// <summary>
    /// Gets all available record types for an object.
    /// </summary>
    Task<IReadOnlyList<RecordTypeInfoModel>> GetRecordTypesAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default record type for an object.
    /// </summary>
    Task<RecordTypeInfoModel?> GetDefaultRecordTypeAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a record type by ID.
    /// </summary>
    Task<RecordTypeInfoModel?> GetRecordTypeByIdAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a record type by developer name.
    /// </summary>
    Task<RecordTypeInfoModel?> GetRecordTypeByNameAsync(
        string objectName,
        string developerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets picklist values available for a specific record type.
    /// </summary>
    Task<IReadOnlyList<PicklistEntry>> GetPicklistValuesForRecordTypeAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default picklist value for a field and record type.
    /// </summary>
    Task<string?> GetDefaultPicklistValueAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a picklist value is valid for a record type.
    /// </summary>
    Task<bool> IsValidPicklistValueAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all field default values for a record type.
    /// </summary>
    Task<IDictionary<string, object?>> GetRecordTypeDefaultsAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the page layout ID associated with a record type.
    /// </summary>
    Task<string?> GetLayoutIdAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of record type manager.
/// </summary>
public class RecordTypeManager : IRecordTypeManager
{
    private readonly ISchemaService _schemaService;
    private readonly Services.Core.ISalesforceClient _client;

    public RecordTypeManager(ISchemaService schemaService, Services.Core.ISalesforceClient client)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecordTypeInfoModel>> GetRecordTypesAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        return await _schemaService.GetRecordTypesAsync(objectName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RecordTypeInfoModel?> GetDefaultRecordTypeAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var recordTypes = await GetRecordTypesAsync(objectName, cancellationToken);
        return recordTypes.FirstOrDefault(rt => rt.DefaultRecordTypeMapping) ??
               recordTypes.FirstOrDefault(rt => rt.Master);
    }

    /// <inheritdoc/>
    public async Task<RecordTypeInfoModel?> GetRecordTypeByIdAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default)
    {
        var recordTypes = await GetRecordTypesAsync(objectName, cancellationToken);
        return recordTypes.FirstOrDefault(rt =>
            rt.RecordTypeId?.Equals(recordTypeId, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <inheritdoc/>
    public async Task<RecordTypeInfoModel?> GetRecordTypeByNameAsync(
        string objectName,
        string developerName,
        CancellationToken cancellationToken = default)
    {
        var recordTypes = await GetRecordTypesAsync(objectName, cancellationToken);
        return recordTypes.FirstOrDefault(rt =>
            rt.DeveloperName?.Equals(developerName, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PicklistEntry>> GetPicklistValuesForRecordTypeAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        // Use the UI API to get record type specific picklist values
        var endpoint = $"ui-api/object-info/{objectName}/picklist-values/{recordTypeId}/{fieldName}";

        try
        {
            var result = await _client.GetAsync<JsonObject>(endpoint, cancellationToken);

            if (result?["values"] is JsonArray values)
            {
                return values.Select(v => new PicklistEntry
                {
                    Value = v?["value"]?.ToString() ?? "",
                    Label = v?["label"]?.ToString() ?? "",
                    DefaultValue = v?["defaultValue"]?.GetValue<bool>() ?? false,
                    Active = true
                }).ToList();
            }
        }
        catch
        {
            // Fall back to full picklist values
            var fields = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
            if (fields.TryGetValue(fieldName, out var field))
            {
                return field.PicklistValues.Where(p => p.Active).ToList();
            }
        }

        return Array.Empty<PicklistEntry>();
    }

    /// <inheritdoc/>
    public async Task<string?> GetDefaultPicklistValueAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var values = await GetPicklistValuesForRecordTypeAsync(
            objectName, recordTypeId, fieldName, cancellationToken);
        return values.FirstOrDefault(v => v.DefaultValue)?.Value;
    }

    /// <inheritdoc/>
    public async Task<bool> IsValidPicklistValueAsync(
        string objectName,
        string recordTypeId,
        string fieldName,
        string value,
        CancellationToken cancellationToken = default)
    {
        var values = await GetPicklistValuesForRecordTypeAsync(
            objectName, recordTypeId, fieldName, cancellationToken);
        return values.Any(v => v.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object?>> GetRecordTypeDefaultsAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Try to get defaults from UI API
        var endpoint = $"ui-api/record-defaults/create/{objectName}?recordTypeId={recordTypeId}";

        try
        {
            var result = await _client.GetAsync<JsonObject>(endpoint, cancellationToken);

            if (result?["record"]?["fields"] is JsonObject fields)
            {
                foreach (var prop in fields)
                {
                    var fieldValue = prop.Value?["value"];
                    if (fieldValue != null)
                    {
                        defaults[prop.Key] = JsonSerializer.Deserialize<object>(fieldValue);
                    }
                }
            }
        }
        catch
        {
            // Fall back to field-level defaults
            var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);

            foreach (var (name, field) in fieldMap)
            {
                if (field.DefaultValue != null)
                {
                    defaults[name] = field.DefaultValue;
                }
                else if (field.IsPicklist)
                {
                    var defaultValue = field.PicklistValues.FirstOrDefault(p => p.DefaultValue)?.Value;
                    if (!string.IsNullOrEmpty(defaultValue))
                    {
                        defaults[name] = defaultValue;
                    }
                }
            }
        }

        return defaults;
    }

    /// <inheritdoc/>
    public async Task<string?> GetLayoutIdAsync(
        string objectName,
        string recordTypeId,
        CancellationToken cancellationToken = default)
    {
        // Get layout from UI API
        var endpoint = $"ui-api/layout/{objectName}?recordTypeId={recordTypeId}";

        try
        {
            var result = await _client.GetAsync<JsonObject>(endpoint, cancellationToken);
            return result?["id"]?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Extension methods for record type operations.
/// </summary>
public static class RecordTypeExtensions
{
    /// <summary>
    /// Gets the record type ID from a record dictionary.
    /// </summary>
    public static string? GetRecordTypeId(this IDictionary<string, object?> record)
    {
        if (record.TryGetValue("RecordTypeId", out var value))
            return value?.ToString();
        return null;
    }

    /// <summary>
    /// Sets the record type ID on a record dictionary.
    /// </summary>
    public static void SetRecordTypeId(this IDictionary<string, object?> record, string recordTypeId)
    {
        record["RecordTypeId"] = recordTypeId;
    }

    /// <summary>
    /// Checks if the record has a record type set.
    /// </summary>
    public static bool HasRecordType(this IDictionary<string, object?> record)
    {
        return !string.IsNullOrEmpty(record.GetRecordTypeId());
    }
}
