using SalesforceCore.Models.Metadata;
using ChildRelationshipModel = SalesforceCore.Models.Metadata.ChildRelationship;
using RecordTypeInfoModel = SalesforceCore.Models.Metadata.RecordTypeInfo;

namespace SalesforceCore.Services.Metadata;

/// <summary>
/// Service for retrieving and caching Salesforce object metadata.
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// Gets the full describe metadata for an SObject.
    /// Results are cached for performance.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object describe or null if not found.</returns>
    Task<SObjectDescribe?> GetDescribeAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all fields for an SObject.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all fields.</returns>
    Task<List<SObjectField>> GetFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields as a dictionary for O(1) lookup.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of field name to field metadata.</returns>
    Task<Dictionary<string, SObjectField>> GetFieldMapAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields that can be created.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of createable fields.</returns>
    Task<List<SObjectField>> GetCreateableFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields that can be updated.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of updateable fields.</returns>
    Task<List<SObjectField>> GetUpdateableFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields that can be queried (not compound types).
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of queryable fields.</returns>
    Task<List<SObjectField>> GetQueryableFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the display name field for an SObject.
    /// Uses intelligent resolution for objects without standard Name field.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Field name to use as display name.</returns>
    Task<string> GetNameFieldAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and sanitizes a list of field names.
    /// Removes invalid fields and ensures minimum required fields.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="requestedFields">Fields requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated list of field names.</returns>
    Task<List<string>> SanitizeFieldListAsync(string sObject, IEnumerable<string> requestedFields, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets lookup fields that reference the specified object.
    /// </summary>
    /// <param name="sObject">API name of the object to find lookups for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of field name to referencing object.</returns>
    Task<List<SObjectField>> GetLookupFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all accessible SObjects in the org.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of accessible objects.</returns>
    Task<List<SObjectInfo>> GetAllObjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the schema cache for a specific object or all objects.
    /// </summary>
    /// <param name="sObject">Optional object to clear. Null clears all.</param>
    Task InvalidateCacheAsync(string? sObject = null);

    /// <summary>
    /// Gets fields that are accessible for read operations (FLS-enforced).
    /// Only returns fields where the current user has read access.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of accessible fields.</returns>
    Task<List<SObjectField>> GetAccessibleFieldsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sanitizes a field list ensuring only accessible fields are included (FLS-enforced).
    /// More restrictive than SanitizeFieldListAsync as it also checks read access.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="requestedFields">Fields requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated list of accessible field names.</returns>
    Task<List<string>> SanitizeFieldListWithFlsAsync(string sObject, IEnumerable<string> requestedFields, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the picklist values for a field, including dependent picklist logic.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="fieldName">API name of the picklist field.</param>
    /// <param name="recordTypeId">Optional record type id to scope picklist values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Picklist values with dependency information.</returns>
    Task<PicklistValuesResult> GetPicklistValuesAsync(string sObject, string fieldName, string? recordTypeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the dependent picklist values filtered by the controlling field value.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="fieldName">API name of the dependent picklist field.</param>
    /// <param name="controllingValue">Value of the controlling field.</param>
    /// <param name="recordTypeId">Optional record type id to scope picklist values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered picklist values valid for the controlling value.</returns>
    Task<List<PicklistEntry>> GetDependentPicklistValuesAsync(
        string sObject,
        string fieldName,
        string controllingValue,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets relationship metadata for a field (lookup/master-detail).
    /// Includes reference object, relationship name, and polymorphic info.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="fieldName">API name of the relationship field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relationship metadata.</returns>
    Task<RelationshipMetadata?> GetRelationshipMetadataAsync(string sObject, string fieldName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all child relationships for an object.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of child relationship definitions.</returns>
    Task<List<ChildRelationshipModel>> GetChildRelationshipsAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the record types available for an object.
    /// </summary>
    /// <param name="sObject">API name of the object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of record types.</returns>
    Task<List<RecordTypeInfoModel>> GetRecordTypesAsync(string sObject, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a picklist values query, including dependency information.
/// </summary>
public class PicklistValuesResult
{
    /// <summary>
    /// All active picklist values.
    /// </summary>
    public List<PicklistEntry> Values { get; set; } = new();

    /// <summary>
    /// Default value if set.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Whether this is a restricted picklist.
    /// </summary>
    public bool IsRestricted { get; set; }

    /// <summary>
    /// Whether this is a dependent picklist.
    /// </summary>
    public bool IsDependentPicklist { get; set; }

    /// <summary>
    /// Name of the controlling field (if dependent).
    /// </summary>
    public string? ControllerName { get; set; }

    /// <summary>
    /// Dependency map: controlling value -> list of valid dependent values.
    /// Only populated for dependent picklists.
    /// </summary>
    public Dictionary<string, List<string>> DependencyMap { get; set; } = new();
}

/// <summary>
/// Metadata about a lookup/master-detail relationship.
/// </summary>
public class RelationshipMetadata
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Relationship API name (for queries).
    /// </summary>
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Primary target object.
    /// </summary>
    public string? ReferenceTo { get; set; }

    /// <summary>
    /// All target objects (for polymorphic lookups).
    /// </summary>
    public List<string> ReferenceToTypes { get; set; } = new();

    /// <summary>
    /// Whether this is a polymorphic lookup.
    /// </summary>
    public bool IsPolymorphic { get; set; }

    /// <summary>
    /// Whether this is a lookup (true) or master-detail (false).
    /// </summary>
    public bool IsLookup { get; set; }

    /// <summary>
    /// Help text for the field.
    /// </summary>
    public string? InlineHelpText { get; set; }
}
