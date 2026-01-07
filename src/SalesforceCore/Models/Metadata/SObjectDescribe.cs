using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Metadata;

/// <summary>
/// Represents the complete metadata description of a Salesforce SObject.
/// Retrieved from the /sobjects/{SObject}/describe endpoint.
/// </summary>
public class SObjectDescribe
{
    /// <summary>
    /// API name of the object (e.g., "Account", "Contact", "CustomObject__c").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly label for the object (e.g., "Account", "Contact").
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Plural form of the label (e.g., "Accounts", "Contacts").
    /// </summary>
    [JsonPropertyName("labelPlural")]
    public string LabelPlural { get; set; } = string.Empty;

    /// <summary>
    /// Three-character prefix used in record IDs (e.g., "001" for Account).
    /// </summary>
    [JsonPropertyName("keyPrefix")]
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Whether this object is a custom object (ends with __c).
    /// </summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; }

    /// <summary>
    /// Whether records can be created for this object.
    /// </summary>
    [JsonPropertyName("createable")]
    public bool Createable { get; set; }

    /// <summary>
    /// Whether records can be updated for this object.
    /// </summary>
    [JsonPropertyName("updateable")]
    public bool Updateable { get; set; }

    /// <summary>
    /// Whether records can be deleted from this object.
    /// </summary>
    [JsonPropertyName("deletable")]
    public bool Deletable { get; set; }

    /// <summary>
    /// Whether records can be queried using SOQL.
    /// </summary>
    [JsonPropertyName("queryable")]
    public bool Queryable { get; set; }

    /// <summary>
    /// Whether individual records can be retrieved by ID.
    /// </summary>
    [JsonPropertyName("retrieveable")]
    public bool Retrieveable { get; set; }

    /// <summary>
    /// Whether the object supports search.
    /// </summary>
    [JsonPropertyName("searchable")]
    public bool Searchable { get; set; }

    /// <summary>
    /// Whether the object has triggers.
    /// </summary>
    [JsonPropertyName("triggerable")]
    public bool Triggerable { get; set; }

    /// <summary>
    /// Whether the object can be undeleted.
    /// </summary>
    [JsonPropertyName("undeletable")]
    public bool Undeletable { get; set; }

    /// <summary>
    /// Whether the object can be merged.
    /// </summary>
    [JsonPropertyName("mergeable")]
    public bool Mergeable { get; set; }

    /// <summary>
    /// Whether the object supports feed tracking.
    /// </summary>
    [JsonPropertyName("feedEnabled")]
    public bool FeedEnabled { get; set; }

    /// <summary>
    /// Whether the object has a custom help page.
    /// </summary>
    [JsonPropertyName("hasSubtypes")]
    public bool HasSubtypes { get; set; }

    /// <summary>
    /// Whether the object is a subtype of another object.
    /// </summary>
    [JsonPropertyName("isSubtype")]
    public bool IsSubtype { get; set; }

    /// <summary>
    /// Collection of all fields on this object.
    /// </summary>
    [JsonPropertyName("fields")]
    public List<SObjectField> Fields { get; set; } = new();

    /// <summary>
    /// Collection of child relationships from this object.
    /// </summary>
    [JsonPropertyName("childRelationships")]
    public List<ChildRelationship> ChildRelationships { get; set; } = new();

    /// <summary>
    /// Collection of record types available for this object.
    /// </summary>
    [JsonPropertyName("recordTypeInfos")]
    public List<RecordTypeInfo> RecordTypeInfos { get; set; } = new();

    /// <summary>
    /// URLs for various operations on this object.
    /// </summary>
    [JsonPropertyName("urls")]
    public Dictionary<string, string> Urls { get; set; } = new();
}

/// <summary>
/// Represents a child relationship from a parent object.
/// </summary>
public class ChildRelationship
{
    /// <summary>
    /// API name of the child object.
    /// </summary>
    [JsonPropertyName("childSObject")]
    public string ChildSObject { get; set; } = string.Empty;

    /// <summary>
    /// API name of the lookup field on the child object.
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Relationship name used in SOQL subqueries.
    /// </summary>
    [JsonPropertyName("relationshipName")]
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Whether this relationship is deprecated.
    /// </summary>
    [JsonPropertyName("deprecatedAndHidden")]
    public bool DeprecatedAndHidden { get; set; }

    /// <summary>
    /// Whether cascade delete is enabled.
    /// </summary>
    [JsonPropertyName("cascadeDelete")]
    public bool CascadeDelete { get; set; }

    /// <summary>
    /// Whether restricted delete is enabled.
    /// </summary>
    [JsonPropertyName("restrictedDelete")]
    public bool RestrictedDelete { get; set; }
}

/// <summary>
/// Represents a record type available for an object.
/// </summary>
public class RecordTypeInfo
{
    /// <summary>
    /// The record type ID.
    /// </summary>
    [JsonPropertyName("recordTypeId")]
    public string RecordTypeId { get; set; } = string.Empty;

    /// <summary>
    /// The record type name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The developer name of the record type.
    /// </summary>
    [JsonPropertyName("developerName")]
    public string DeveloperName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default record type.
    /// </summary>
    [JsonPropertyName("defaultRecordTypeMapping")]
    public bool DefaultRecordTypeMapping { get; set; }

    /// <summary>
    /// Whether this record type is available to the current user.
    /// </summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    /// <summary>
    /// Whether this is the master record type.
    /// </summary>
    [JsonPropertyName("master")]
    public bool Master { get; set; }
}
