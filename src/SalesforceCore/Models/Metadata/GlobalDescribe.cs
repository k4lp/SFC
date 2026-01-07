using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Metadata;

/// <summary>
/// Represents the result of a Global Describe call.
/// Contains a list of all accessible SObjects in the org.
/// </summary>
public class GlobalDescribeResult
{
    /// <summary>
    /// Encoding type used.
    /// </summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    /// <summary>
    /// Maximum batch size for this org.
    /// </summary>
    [JsonPropertyName("maxBatchSize")]
    public int MaxBatchSize { get; set; }

    /// <summary>
    /// List of all accessible SObjects.
    /// </summary>
    [JsonPropertyName("sobjects")]
    public List<SObjectInfo> SObjects { get; set; } = new();
}

/// <summary>
/// Basic information about an SObject from Global Describe.
/// </summary>
public class SObjectInfo
{
    /// <summary>
    /// API name of the object.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly label.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Plural form of the label.
    /// </summary>
    [JsonPropertyName("labelPlural")]
    public string LabelPlural { get; set; } = string.Empty;

    /// <summary>
    /// Three-character prefix for record IDs.
    /// </summary>
    [JsonPropertyName("keyPrefix")]
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Whether this is a custom object.
    /// </summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; }

    /// <summary>
    /// Whether records can be created.
    /// </summary>
    [JsonPropertyName("createable")]
    public bool Createable { get; set; }

    /// <summary>
    /// Whether records can be updated.
    /// </summary>
    [JsonPropertyName("updateable")]
    public bool Updateable { get; set; }

    /// <summary>
    /// Whether records can be deleted.
    /// </summary>
    [JsonPropertyName("deletable")]
    public bool Deletable { get; set; }

    /// <summary>
    /// Whether records can be queried.
    /// </summary>
    [JsonPropertyName("queryable")]
    public bool Queryable { get; set; }

    /// <summary>
    /// Whether individual records can be retrieved.
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
    /// Whether feed is enabled for this object.
    /// </summary>
    [JsonPropertyName("feedEnabled")]
    public bool FeedEnabled { get; set; }

    /// <summary>
    /// Whether this is a layout-enabled object.
    /// </summary>
    [JsonPropertyName("layoutable")]
    public bool Layoutable { get; set; }

    /// <summary>
    /// Whether activities can be logged against this object.
    /// </summary>
    [JsonPropertyName("activateable")]
    public bool Activateable { get; set; }

    /// <summary>
    /// URLs for various operations.
    /// </summary>
    [JsonPropertyName("urls")]
    public Dictionary<string, string> Urls { get; set; } = new();

    /// <summary>
    /// Deprecated and hidden flag.
    /// </summary>
    [JsonPropertyName("deprecatedAndHidden")]
    public bool DeprecatedAndHidden { get; set; }

    /// <summary>
    /// Whether this is a MRU (Most Recently Used) enabled object.
    /// </summary>
    [JsonPropertyName("mruEnabled")]
    public bool MruEnabled { get; set; }

    /// <summary>
    /// Whether has subtypes.
    /// </summary>
    [JsonPropertyName("hasSubtypes")]
    public bool HasSubtypes { get; set; }

    /// <summary>
    /// Whether is subtype.
    /// </summary>
    [JsonPropertyName("isSubtype")]
    public bool IsSubtype { get; set; }
}
