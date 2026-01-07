using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Utilities;
using ChildRelationship = SalesforceCore.Models.Metadata.ChildRelationship;
using RecordTypeInfo = SalesforceCore.Models.Metadata.RecordTypeInfo;

namespace SalesforceCore.Services.Metadata;

/// <summary>
/// Service for retrieving and caching Salesforce object metadata.
/// Uses ICacheProvider for flexible caching (memory or distributed).
/// </summary>
public class SchemaService : ISchemaService
{
    private readonly ISalesforceClient _client;
    private readonly ICacheProvider _cache;
    private readonly SalesforceOptions _options;
    private readonly ILogger<SchemaService> _logger;

    private const string SchemaCachePrefix = "Schema_";
    private const string GlobalDescribeCacheKey = "GlobalDescribe";
    private const string PicklistCachePrefix = "Picklist_";

    /// <summary>
    /// Creates a new SchemaService.
    /// </summary>
    public SchemaService(
        ISalesforceClient client,
        ICacheProvider cache,
        IOptions<SalesforceOptions> options,
        ILogger<SchemaService> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SObjectDescribe?> GetDescribeAsync(string sObject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sObject))
        {
            return null;
        }

        // Validate object name to prevent path traversal or injection
        // Object names should be alphanumeric with underscores
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            _logger.LogWarning("Invalid sObject name format: {SObject}", sObject);
            return null;
        }

        var cacheKey = $"{SchemaCachePrefix}{sObject}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                try
                {
                    return await _client.GetAsync<SObjectDescribe>(
                        $"/sobjects/{sObject}/describe",
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get describe for {SObject}", sObject);
                    return null;
                }
            },
            _options.SchemaCacheDuration,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var describe = await GetDescribeAsync(sObject, cancellationToken);
        return describe?.Fields ?? new List<SObjectField>();
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, SObjectField>> GetFieldMapAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetCreateableFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields
            .Where(f => f.Createable && !f.DeprecatedAndHidden && !IsExcludedFieldType(f.Type))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetUpdateableFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields
            .Where(f => f.Updateable && !f.DeprecatedAndHidden && !IsExcludedFieldType(f.Type))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetQueryableFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields
            .Where(f => !f.DeprecatedAndHidden && !SalesforceConventions.NonQueryableFieldTypes.Contains(f.Type.ToLowerInvariant()))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<string> GetNameFieldAsync(string sObject, CancellationToken cancellationToken = default)
    {
        // Check for known overrides first
        if (SalesforceConventions.ObjectNameFieldOverrides.TryGetValue(sObject, out var overrideField))
        {
            return overrideField;
        }

        var fields = await GetFieldsAsync(sObject, cancellationToken);
        if (fields.Count == 0)
        {
            return "Id";
        }

        // Check for fields marked as name field
        var nameField = fields.FirstOrDefault(f => f.NameField);
        if (nameField != null)
        {
            return nameField.Name;
        }

        // Try candidate field names in priority order
        foreach (var candidate in SalesforceConventions.NameFieldCandidates)
        {
            if (fields.Any(f => f.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return "Id";
    }

    /// <inheritdoc/>
    public async Task<List<string>> SanitizeFieldListAsync(string sObject, IEnumerable<string> requestedFields, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        var fieldMap = fields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
        var relationshipMap = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.RelationshipName))
            .GroupBy(f => f.RelationshipName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var validFields = new List<string>();
        var requestedList = requestedFields.ToList();

        foreach (var field in requestedList)
        {
            // Validate field format first to prevent injection
            // SecurityUtils.IsValidFieldName supports dot notation (e.g., Account.Name)
            if (!SecurityUtils.IsValidFieldName(field))
            {
                _logger.LogWarning("Invalid field format rejected: {Field}", field);
                continue;
            }

            // Handle relationship notation (e.g., Account.Name)
            var baseName = field.Split('.')[0];

            if (fieldMap.TryGetValue(baseName, out var fieldDef) ||
                relationshipMap.TryGetValue(baseName, out fieldDef))
            {
                // Skip non-queryable compound types in direct queries
                if (!SalesforceConventions.NonQueryableFieldTypes.Contains(fieldDef.Type.ToLowerInvariant()))
                {
                    validFields.Add(field);
                }
            }
        }

        // Ensure minimum fields: Id and CreatedDate
        if (!validFields.Any(f => f.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            validFields.Insert(0, "Id");
        }

        // Handle Name field substitution
        if (requestedList.Any(f => f.Equals("Name", StringComparison.OrdinalIgnoreCase)) &&
            !validFields.Any(f => f.Equals("Name", StringComparison.OrdinalIgnoreCase)))
        {
            var nameField = await GetNameFieldAsync(sObject, cancellationToken);
            if (!validFields.Any(f => f.Equals(nameField, StringComparison.OrdinalIgnoreCase)))
            {
                validFields.Add(nameField);
            }
        }

        return validFields.Distinct().ToList();
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetLookupFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields
            .Where(f => f.Type.Equals("reference", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<SObjectInfo>> GetAllObjectsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _cache.GetOrCreateAsync(
            GlobalDescribeCacheKey,
            async ct =>
            {
                try
                {
                    var response = await _client.GetAsync<GlobalDescribeResult>("/sobjects/", ct);
                    return response?.SObjects;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get global describe");
                    return null;
                }
            },
            _options.SchemaCacheDuration,
            cancellationToken);

        return result ?? new List<SObjectInfo>();
    }

    /// <inheritdoc/>
    public async Task InvalidateCacheAsync(string? sObject = null)
    {
        if (string.IsNullOrWhiteSpace(sObject))
        {
            await _cache.RemoveAsync(GlobalDescribeCacheKey);
            _logger.LogInformation("Schema cache invalidated for global describe");
        }
        else
        {
            await _cache.RemoveAsync($"{SchemaCachePrefix}{sObject}");
            _logger.LogInformation("Schema cache invalidated for {SObject}", sObject);
        }
    }

    private static bool IsExcludedFieldType(string type)
    {
        var lowerType = type.ToLowerInvariant();
        return lowerType == "address" ||
               lowerType == "location" ||
               lowerType == "complexvalue" ||
               lowerType == "anytype";
    }

    /// <inheritdoc/>
    public async Task<List<SObjectField>> GetAccessibleFieldsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(sObject, cancellationToken);
        return fields
            .Where(f => f.Accessible && !f.DeprecatedAndHidden && !SalesforceConventions.NonQueryableFieldTypes.Contains(f.Type.ToLowerInvariant()))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<string>> SanitizeFieldListWithFlsAsync(string sObject, IEnumerable<string> requestedFields, CancellationToken cancellationToken = default)
    {
        var accessibleFields = await GetAccessibleFieldsAsync(sObject, cancellationToken);
        var accessibleFieldNames = accessibleFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accessibleRelationshipNames = accessibleFields
            .Where(f => !string.IsNullOrWhiteSpace(f.RelationshipName))
            .GroupBy(f => f.RelationshipName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var validFields = new List<string>();
        var requestedList = requestedFields.ToList();

        foreach (var field in requestedList)
        {
            if (!SecurityUtils.IsValidFieldName(field))
            {
                _logger.LogWarning("Invalid field format rejected: {Field}", field);
                continue;
            }

            // Handle relationship notation (e.g., Account.Name)
            var baseName = field.Split('.')[0];

            if (accessibleFieldNames.Contains(baseName) ||
                accessibleRelationshipNames.ContainsKey(baseName))
            {
                validFields.Add(field);
            }
            else
            {
                _logger.LogDebug("Field {Field} on {SObject} is not accessible (FLS)", field, sObject);
            }
        }

        // Ensure Id is always included
        if (!validFields.Any(f => f.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            validFields.Insert(0, "Id");
        }

        // Handle Name field substitution with FLS check
        if (requestedList.Any(f => f.Equals("Name", StringComparison.OrdinalIgnoreCase)) &&
            !validFields.Any(f => f.Equals("Name", StringComparison.OrdinalIgnoreCase)))
        {
            var nameField = await GetNameFieldAsync(sObject, cancellationToken);
            if (accessibleFieldNames.Contains(nameField) &&
                !validFields.Any(f => f.Equals(nameField, StringComparison.OrdinalIgnoreCase)))
            {
                validFields.Add(nameField);
            }
        }

        return validFields.Distinct().ToList();
    }

    /// <inheritdoc/>
    public async Task<PicklistValuesResult> GetPicklistValuesAsync(string sObject, string fieldName, string? recordTypeId = null, CancellationToken cancellationToken = default)
    {
        var fieldMap = await GetFieldMapAsync(sObject, cancellationToken);

        if (!fieldMap.TryGetValue(fieldName, out var field))
        {
            return new PicklistValuesResult();
        }

        if (!field.IsPicklist)
        {
            _logger.LogWarning("Field {Field} on {SObject} is not a picklist", fieldName, sObject);
            return new PicklistValuesResult();
        }

        // If record type specified, try to fetch record-type-specific values and filter base metadata.
        if (!string.IsNullOrEmpty(recordTypeId))
        {
            var recordTypeResult = await GetPicklistValuesForRecordTypeAsync(
                sObject,
                fieldName,
                field,
                recordTypeId,
                fieldMap,
                cancellationToken);
            if (recordTypeResult.Values.Count > 0)
            {
                return recordTypeResult;
            }
        }

        var result = BuildDefaultPicklistResult(field, fieldMap);

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<PicklistEntry>> GetDependentPicklistValuesAsync(
        string sObject,
        string fieldName,
        string controllingValue,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default)
    {
        var picklistResult = await GetPicklistValuesAsync(sObject, fieldName, recordTypeId, cancellationToken);

        if (!picklistResult.IsDependentPicklist)
        {
            return picklistResult.Values;
        }

        if (picklistResult.DependencyMap.TryGetValue(controllingValue, out var validValues))
        {
            return picklistResult.Values
                .Where(v => validValues.Contains(v.Value, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return new List<PicklistEntry>();
    }

    /// <inheritdoc/>
    public async Task<RelationshipMetadata?> GetRelationshipMetadataAsync(string sObject, string fieldName, CancellationToken cancellationToken = default)
    {
        var fieldMap = await GetFieldMapAsync(sObject, cancellationToken);

        if (!fieldMap.TryGetValue(fieldName, out var field))
        {
            return null;
        }

        if (!field.IsLookup)
        {
            return null;
        }

        return new RelationshipMetadata
        {
            FieldName = field.Name,
            RelationshipName = field.RelationshipName,
            ReferenceTo = field.PrimaryReferenceTo,
            ReferenceToTypes = field.ReferenceTo ?? new List<string>(),
            IsPolymorphic = field.PolymorphicForeignKey || (field.ReferenceTo?.Count > 1),
            IsLookup = !field.WriteRequiresMasterRead,
            InlineHelpText = field.InlineHelpText
        };
    }

    /// <inheritdoc/>
    public async Task<List<ChildRelationship>> GetChildRelationshipsAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var describe = await GetDescribeAsync(sObject, cancellationToken);
        if (describe == null)
        {
            return new List<ChildRelationship>();
        }

        return describe.ChildRelationships
            .Where(r => !r.DeprecatedAndHidden && !string.IsNullOrEmpty(r.RelationshipName))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<RecordTypeInfo>> GetRecordTypesAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var describe = await GetDescribeAsync(sObject, cancellationToken);
        if (describe == null)
        {
            return new List<RecordTypeInfo>();
        }

        return describe.RecordTypeInfos
            .Where(r => r.Available)
            .ToList();
    }

    private PicklistValuesResult BuildDefaultPicklistResult(SObjectField field, Dictionary<string, SObjectField> fieldMap)
    {
        var result = new PicklistValuesResult
        {
            Values = field.PicklistValues.Where(p => p.Active).ToList(),
            DefaultValue = field.PicklistValues.FirstOrDefault(p => p.DefaultValue)?.Value,
            IsRestricted = field.RestrictedPicklist,
            IsDependentPicklist = field.DependentPicklist,
            ControllerName = field.ControllerName,
            DependencyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        };

        if (field.DependentPicklist && !string.IsNullOrEmpty(field.ControllerName))
        {
            if (fieldMap.TryGetValue(field.ControllerName, out var controllerField))
            {
                result.DependencyMap = BuildDependencyMap(field.PicklistValues, controllerField.PicklistValues);
            }
        }

        return result;
    }

    private async Task<PicklistValuesResult> GetPicklistValuesForRecordTypeAsync(
        string sObject,
        string fieldName,
        SObjectField fieldMetadata,
        string recordTypeId,
        Dictionary<string, SObjectField> fieldMap,
        CancellationToken cancellationToken)
        {
            var cacheKey = $"{PicklistCachePrefix}{sObject.ToLowerInvariant()}_{fieldName.ToLowerInvariant()}_{recordTypeId}";
        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var allowedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string? defaultValue = null;

                try
                {
                    var endpoint = $"ui-api/object-info/{sObject}/picklist-values/{recordTypeId}/{fieldName}";
                    var doc = await _client.GetAsync<JsonDocument>(endpoint, cancellationToken);
                    if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("values", out var valuesElement) &&
                        valuesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var valueElement in valuesElement.EnumerateArray())
                        {
                            if (valueElement.TryGetProperty("value", out var valProp))
                            {
                                var val = valProp.GetString() ?? string.Empty;
                                allowedValues.Add(val);

                                if (defaultValue == null &&
                                    valueElement.TryGetProperty("defaultValue", out var defProp) &&
                                    defProp.ValueKind == System.Text.Json.JsonValueKind.True)
                                {
                                    defaultValue = val;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch record-type picklist values for {Object}.{Field} ({RecordTypeId})", sObject, fieldName, recordTypeId);
                }

                if (allowedValues.Count == 0)
                {
                    // Fall back to default metadata-based values
                    return BuildDefaultPicklistResult(fieldMetadata, fieldMap);
                }

                var filtered = fieldMetadata.PicklistValues
                    .Where(p => p.Active && allowedValues.Contains(p.Value))
                    .ToList();

                var result = new PicklistValuesResult
                {
                    Values = filtered,
                    DefaultValue = defaultValue ?? filtered.FirstOrDefault(p => p.DefaultValue)?.Value,
                    IsRestricted = fieldMetadata.RestrictedPicklist,
                    IsDependentPicklist = fieldMetadata.DependentPicklist,
                    ControllerName = fieldMetadata.ControllerName,
                    DependencyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                };

                if (fieldMetadata.DependentPicklist && !string.IsNullOrEmpty(fieldMetadata.ControllerName) &&
                    fieldMap.TryGetValue(fieldMetadata.ControllerName, out var controllerField))
                {
                    result.DependencyMap = BuildDependencyMap(filtered, controllerField.PicklistValues);
                }

                return result;
            },
            _options.SchemaCacheDuration,
            cancellationToken);
        return result ?? BuildDefaultPicklistResult(fieldMetadata, fieldMap);
    }

    /// <summary>
    /// Builds a dependency map from ValidFor bitmaps.
    /// </summary>
    private static Dictionary<string, List<string>> BuildDependencyMap(
        List<PicklistEntry> dependentValues,
        List<PicklistEntry> controllerValues)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize map with all controller values
        for (int i = 0; i < controllerValues.Count; i++)
        {
            if (controllerValues[i].Active)
            {
                map[controllerValues[i].Value] = new List<string>();
            }
        }

        // Parse ValidFor bitmap for each dependent value
        foreach (var dependentValue in dependentValues.Where(v => v.Active))
        {
            if (string.IsNullOrEmpty(dependentValue.ValidFor))
            {
                continue;
            }

            try
            {
                // ValidFor is a Base64-encoded bitmap
                var validForBytes = Convert.FromBase64String(dependentValue.ValidFor);
                var bitArray = new System.Collections.BitArray(validForBytes);

                // Each bit corresponds to a controller picklist value by index
                for (int i = 0; i < controllerValues.Count && i < bitArray.Length * 8; i++)
                {
                    // Bits are in network byte order (big-endian)
                    int byteIndex = i / 8;
                    int bitIndex = 7 - (i % 8);

                    if (byteIndex < validForBytes.Length)
                    {
                        bool isValid = (validForBytes[byteIndex] & (1 << bitIndex)) != 0;
                        if (isValid && controllerValues[i].Active)
                        {
                            var controllerValue = controllerValues[i].Value;
                            if (map.TryGetValue(controllerValue, out var validDependents))
                            {
                                validDependents.Add(dependentValue.Value);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Invalid ValidFor format, skip this entry
            }
        }

        return map;
    }
}
