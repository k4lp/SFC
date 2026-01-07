using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Metadata;

/// <summary>
/// Represents complete metadata for a Salesforce field.
/// Contains all properties needed for dynamic form generation and data handling.
/// </summary>
public class SObjectField
{
    /// <summary>
    /// API name of the field (e.g., "AccountId", "Name", "Custom_Field__c").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly label for the field.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Salesforce field type (e.g., "string", "picklist", "reference", "boolean").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Maximum length for string/textarea fields.
    /// </summary>
    [JsonPropertyName("length")]
    public int Length { get; set; }

    /// <summary>
    /// Number of digits for numeric fields.
    /// </summary>
    [JsonPropertyName("precision")]
    public int Precision { get; set; }

    /// <summary>
    /// Number of decimal places for numeric fields.
    /// </summary>
    [JsonPropertyName("scale")]
    public int Scale { get; set; }

    /// <summary>
    /// Number of visible lines for textarea fields.
    /// </summary>
    [JsonPropertyName("byteLength")]
    public int ByteLength { get; set; }

    /// <summary>
    /// Whether the field accepts null values.
    /// </summary>
    [JsonPropertyName("nillable")]
    public bool Nillable { get; set; }

    /// <summary>
    /// Whether the field can be created.
    /// </summary>
    [JsonPropertyName("createable")]
    public bool Createable { get; set; }

    /// <summary>
    /// Whether the field can be updated.
    /// </summary>
    [JsonPropertyName("updateable")]
    public bool Updateable { get; set; }

    /// <summary>
    /// Whether the field is accessible for read operations (FLS).
    /// This indicates if the current user can see this field's value.
    /// </summary>
    [JsonPropertyName("accessible")]
    public bool Accessible { get; set; } = true;

    /// <summary>
    /// Whether the field can be included in SOQL WHERE clauses.
    /// </summary>
    [JsonPropertyName("filterable")]
    public bool Filterable { get; set; }

    /// <summary>
    /// Whether the field can be used in ORDER BY clauses.
    /// </summary>
    [JsonPropertyName("sortable")]
    public bool Sortable { get; set; }

    /// <summary>
    /// Whether the field can be used in GROUP BY clauses.
    /// </summary>
    [JsonPropertyName("groupable")]
    public bool Groupable { get; set; }

    /// <summary>
    /// Whether the field is unique.
    /// </summary>
    [JsonPropertyName("unique")]
    public bool Unique { get; set; }

    /// <summary>
    /// Whether the field is a name field (used for record display).
    /// </summary>
    [JsonPropertyName("nameField")]
    public bool NameField { get; set; }

    /// <summary>
    /// Whether the field is an ID lookup field.
    /// </summary>
    [JsonPropertyName("idLookup")]
    public bool IdLookup { get; set; }

    /// <summary>
    /// Whether the field is a calculated formula field.
    /// </summary>
    [JsonPropertyName("calculated")]
    public bool Calculated { get; set; }

    /// <summary>
    /// Whether the field is auto-generated (like Id, CreatedDate).
    /// </summary>
    [JsonPropertyName("autoNumber")]
    public bool AutoNumber { get; set; }

    /// <summary>
    /// Default value for the field.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Default value formula for the field.
    /// </summary>
    [JsonPropertyName("defaultValueFormula")]
    public string? DefaultValueFormula { get; set; }

    /// <summary>
    /// Whether the field is a custom field.
    /// </summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; }

    /// <summary>
    /// Whether the field is deprecated and hidden.
    /// </summary>
    [JsonPropertyName("deprecatedAndHidden")]
    public bool DeprecatedAndHidden { get; set; }

    /// <summary>
    /// Whether the field is a compound field (like Address).
    /// </summary>
    [JsonPropertyName("compoundFieldName")]
    public string? CompoundFieldName { get; set; }

    /// <summary>
    /// Whether the field is an external ID.
    /// </summary>
    [JsonPropertyName("externalId")]
    public bool ExternalId { get; set; }

    /// <summary>
    /// Whether the field is case-sensitive.
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Whether the field is encrypted.
    /// </summary>
    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; set; }

    /// <summary>
    /// Whether the field allows HTML formatting.
    /// </summary>
    [JsonPropertyName("htmlFormatted")]
    public bool HtmlFormatted { get; set; }

    /// <summary>
    /// Whether the field is a name pointing field.
    /// </summary>
    [JsonPropertyName("namePointing")]
    public bool NamePointing { get; set; }

    /// <summary>
    /// Whether the field is a polymorphic lookup (can reference multiple object types).
    /// </summary>
    [JsonPropertyName("polymorphicForeignKey")]
    public bool PolymorphicForeignKey { get; set; }

    /// <summary>
    /// Inline help text for the field.
    /// </summary>
    [JsonPropertyName("inlineHelpText")]
    public string? InlineHelpText { get; set; }

    /// <summary>
    /// Relationship name for lookup fields.
    /// </summary>
    [JsonPropertyName("relationshipName")]
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Order in which the relationship appears.
    /// </summary>
    [JsonPropertyName("relationshipOrder")]
    public int? RelationshipOrder { get; set; }

    /// <summary>
    /// Object types that this lookup field can reference.
    /// For polymorphic lookups, this contains multiple objects.
    /// </summary>
    [JsonPropertyName("referenceTo")]
    public List<string> ReferenceTo { get; set; } = new();

    /// <summary>
    /// Available picklist values for picklist fields.
    /// </summary>
    [JsonPropertyName("picklistValues")]
    public List<PicklistEntry> PicklistValues { get; set; } = new();

    /// <summary>
    /// Controller field name for dependent picklists.
    /// </summary>
    [JsonPropertyName("controllerName")]
    public string? ControllerName { get; set; }

    /// <summary>
    /// Whether this field depends on another field.
    /// </summary>
    [JsonPropertyName("dependentPicklist")]
    public bool DependentPicklist { get; set; }

    /// <summary>
    /// Whether this is a restricted picklist (values cannot be changed via API).
    /// </summary>
    [JsonPropertyName("restrictedPicklist")]
    public bool RestrictedPicklist { get; set; }

    /// <summary>
    /// Mask type for encrypted fields.
    /// </summary>
    [JsonPropertyName("mask")]
    public string? Mask { get; set; }

    /// <summary>
    /// Mask character for encrypted fields.
    /// </summary>
    [JsonPropertyName("maskType")]
    public string? MaskType { get; set; }

    /// <summary>
    /// Whether the field is write-only (required for creation, not visible after).
    /// </summary>
    [JsonPropertyName("writeRequiresMasterRead")]
    public bool WriteRequiresMasterRead { get; set; }

    /// <summary>
    /// Gets whether this field is a lookup/reference field.
    /// </summary>
    [JsonIgnore]
    public bool IsLookup => Type?.Equals("reference", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets whether this field is a picklist field.
    /// </summary>
    [JsonIgnore]
    public bool IsPicklist => Type?.Equals("picklist", StringComparison.OrdinalIgnoreCase) == true ||
                              Type?.Equals("multipicklist", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets whether this field is a compound field (address, location).
    /// </summary>
    [JsonIgnore]
    public bool IsCompound => Type?.Equals("address", StringComparison.OrdinalIgnoreCase) == true ||
                              Type?.Equals("location", StringComparison.OrdinalIgnoreCase) == true ||
                              !string.IsNullOrEmpty(CompoundFieldName);

    /// <summary>
    /// Gets whether this field is read-only.
    /// </summary>
    [JsonIgnore]
    public bool IsReadOnly => !Createable && !Updateable;

    /// <summary>
    /// Gets whether this field is required (not nillable and no default).
    /// </summary>
    [JsonIgnore]
    public bool IsRequired => !Nillable && DefaultValue == null && !AutoNumber && !Calculated;

    /// <summary>
    /// Gets the primary reference target for lookup fields.
    /// </summary>
    [JsonIgnore]
    public string? PrimaryReferenceTo => ReferenceTo?.FirstOrDefault();
}

/// <summary>
/// Represents a picklist value entry.
/// </summary>
public class PicklistEntry
{
    /// <summary>
    /// The API value stored in the database.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// The display label shown to users.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default value.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public bool DefaultValue { get; set; }

    /// <summary>
    /// Whether this value is active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// Validity bitmap for dependent picklists.
    /// </summary>
    [JsonPropertyName("validFor")]
    public string? ValidFor { get; set; }
}
