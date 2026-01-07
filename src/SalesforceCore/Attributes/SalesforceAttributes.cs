namespace SalesforceCore.Attributes;

/// <summary>
/// Specifies the Salesforce object name for a class.
/// Use this when the class name differs from the Salesforce object API name.
/// </summary>
/// <example>
/// [SalesforceObject("Account")]
/// public class CustomerAccount { }
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class SalesforceObjectAttribute : Attribute
{
    /// <summary>
    /// Gets the Salesforce object API name.
    /// </summary>
    public string ObjectName { get; }

    /// <summary>
    /// Gets or sets whether this object is queryable.
    /// </summary>
    public bool Queryable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this object is createable.
    /// </summary>
    public bool Createable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this object is updateable.
    /// </summary>
    public bool Updateable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this object is deletable.
    /// </summary>
    public bool Deletable { get; set; } = true;

    /// <summary>
    /// Creates a new SalesforceObject attribute.
    /// </summary>
    /// <param name="objectName">The Salesforce object API name (e.g., "Account", "Contact__c").</param>
    public SalesforceObjectAttribute(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        ObjectName = objectName;
    }
}

/// <summary>
/// Maps a C# property to a Salesforce field.
/// Use this when the property name differs from the Salesforce field API name.
/// </summary>
/// <example>
/// [SalesforceField("Account_Number__c")]
/// public string AccountNumber { get; set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceFieldAttribute : Attribute
{
    /// <summary>
    /// Gets the Salesforce field API name.
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Gets or sets whether this field is read-only (not sent in create/update).
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets whether this field is createable.
    /// </summary>
    public bool Createable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this field is updateable.
    /// </summary>
    public bool Updateable { get; set; } = true;

    /// <summary>
    /// Gets or sets the Salesforce field type (for documentation/generation purposes).
    /// </summary>
    public string? FieldType { get; set; }

    /// <summary>
    /// Gets or sets the maximum length for string fields.
    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the precision for decimal fields.
    /// </summary>
    public int Precision { get; set; }

    /// <summary>
    /// Gets or sets the scale for decimal fields.
    /// </summary>
    public int Scale { get; set; }

    /// <summary>
    /// Gets or sets whether this is a required field.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the relationship name for lookup fields.
    /// </summary>
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Gets or sets the reference object for lookup fields.
    /// </summary>
    public string? ReferenceTo { get; set; }

    /// <summary>
    /// Creates a new SalesforceField attribute.
    /// </summary>
    /// <param name="fieldName">The Salesforce field API name (e.g., "Name", "Account_Number__c").</param>
    public SalesforceFieldAttribute(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required", nameof(fieldName));

        FieldName = fieldName;
    }
}

/// <summary>
/// Marks a property to be ignored during Salesforce serialization/deserialization.
/// Use this for properties that exist only in C# and have no Salesforce equivalent.
/// </summary>
/// <example>
/// [SalesforceIgnore]
/// public string ComputedValue { get; set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a property as the external ID field for upsert operations.
/// </summary>
/// <example>
/// [SalesforceExternalId]
/// [SalesforceField("External_ID__c")]
/// public string ExternalId { get; set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceExternalIdAttribute : Attribute
{
}

/// <summary>
/// Marks a property as the Id field (primary key).
/// This is typically not needed as "Id" is the default.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceIdAttribute : Attribute
{
}

/// <summary>
/// Marks a property as a lookup/reference field.
/// </summary>
/// <example>
/// [SalesforceLookup("Account", RelationshipName = "Account")]
/// public string AccountId { get; set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceLookupAttribute : Attribute
{
    /// <summary>
    /// Gets the target object name for this lookup.
    /// </summary>
    public string ReferenceTo { get; }

    /// <summary>
    /// Gets or sets the relationship name.
    /// </summary>
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Gets or sets whether this is a polymorphic lookup.
    /// </summary>
    public bool Polymorphic { get; set; }

    /// <summary>
    /// Creates a new SalesforceLookup attribute.
    /// </summary>
    /// <param name="referenceTo">The target object API name.</param>
    public SalesforceLookupAttribute(string referenceTo)
    {
        if (string.IsNullOrWhiteSpace(referenceTo))
            throw new ArgumentException("Reference object name is required", nameof(referenceTo));

        ReferenceTo = referenceTo;
    }
}

/// <summary>
/// Marks a property as a picklist field with specified values.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforcePicklistAttribute : Attribute
{
    /// <summary>
    /// Gets the valid picklist values.
    /// </summary>
    public string[] Values { get; }

    /// <summary>
    /// Gets or sets whether this is a multi-select picklist.
    /// </summary>
    public bool MultiSelect { get; set; }

    /// <summary>
    /// Creates a new SalesforcePicklist attribute.
    /// </summary>
    /// <param name="values">The valid picklist values.</param>
    public SalesforcePicklistAttribute(params string[] values)
    {
        Values = values ?? Array.Empty<string>();
    }
}

/// <summary>
/// Marks a property as a child relationship (one-to-many).
/// Used for relationship sub-queries like Account.Contacts.
/// </summary>
/// <example>
/// [SalesforceChildRelationship("Contact", "Contacts")]
/// public List&lt;Contact&gt; Contacts { get; set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforceChildRelationshipAttribute : Attribute
{
    /// <summary>
    /// Gets the child object API name.
    /// </summary>
    public string ChildObject { get; }

    /// <summary>
    /// Gets the relationship name (as defined in Salesforce).
    /// </summary>
    public string RelationshipName { get; }

    /// <summary>
    /// Gets the foreign key field on the child object that points back to the parent.
    /// This is optional metadata and is not required for Salesforce SOQL subquery syntax,
    /// but is useful for validation, documentation, and for non-subquery relationship loading.
    /// Example: "AccountId" for Contact -> Account.
    /// </summary>
    public string? ForeignKeyField { get; }

    /// <summary>
    /// Gets or sets the fields to include in sub-queries.
    /// If null, all queryable fields of the child type are included.
    /// </summary>
    public string[]? Fields { get; set; }

    /// <summary>
    /// Creates a new SalesforceChildRelationship attribute.
    /// </summary>
    /// <param name="childObject">The child object API name (e.g., "Contact").</param>
    /// <param name="relationshipName">The relationship name (e.g., "Contacts").</param>
    public SalesforceChildRelationshipAttribute(string childObject, string relationshipName)
    {
        if (string.IsNullOrWhiteSpace(childObject))
            throw new ArgumentException("Child object name is required", nameof(childObject));
        if (string.IsNullOrWhiteSpace(relationshipName))
            throw new ArgumentException("Relationship name is required", nameof(relationshipName));

        ChildObject = childObject;
        RelationshipName = relationshipName;
    }

    /// <summary>
    /// Creates a new SalesforceChildRelationship attribute using the documented 3-parameter form.
    /// </summary>
    /// <param name="relationshipName">The relationship name (e.g., "Contacts").</param>
    /// <param name="childObject">The child object API name (e.g., "Contact").</param>
    /// <param name="foreignKeyField">The foreign key field on the child object (e.g., "AccountId").</param>
    public SalesforceChildRelationshipAttribute(string relationshipName, string childObject, string foreignKeyField)
        : this(childObject, relationshipName)
    {
        if (string.IsNullOrWhiteSpace(foreignKeyField))
            throw new ArgumentException("Foreign key field is required", nameof(foreignKeyField));

        ForeignKeyField = foreignKeyField;
    }
}

/// <summary>
/// Marks a property as a polymorphic lookup field (can reference multiple object types).
/// Used for fields like Task.WhoId (Contact or Lead) or Task.WhatId (Account, Opportunity, etc.).
/// </summary>
/// <example>
/// [SalesforcePolymorphicLookup("Contact", "Lead")]
/// [SalesforceField("WhoId")]
/// public string? WhoId { get; set; }
///
/// [SalesforceIgnore]
/// public string? WhoType { get; set; } // Populated from Who.Type
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SalesforcePolymorphicLookupAttribute : Attribute
{
    /// <summary>
    /// Gets the possible target object types.
    /// </summary>
    public string[] ReferenceToTypes { get; }

    /// <summary>
    /// Gets or sets the relationship name for accessing polymorphic object fields.
    /// </summary>
    public string? RelationshipName { get; set; }

    /// <summary>
    /// Gets or sets the property name to populate with the resolved object type.
    /// </summary>
    public string? TypeProperty { get; set; }

    /// <summary>
    /// Gets or sets the property name to populate with the resolved display name.
    /// </summary>
    public string? NameProperty { get; set; }

    /// <summary>
    /// Creates a new SalesforcePolymorphicLookup attribute.
    /// </summary>
    /// <param name="referenceToTypes">The possible target object types.</param>
    public SalesforcePolymorphicLookupAttribute(params string[] referenceToTypes)
    {
        if (referenceToTypes == null || referenceToTypes.Length == 0)
            throw new ArgumentException("At least one reference type is required", nameof(referenceToTypes));

        ReferenceToTypes = referenceToTypes;
    }
}

/// <summary>
/// Specifies the Salesforce API value for an enum member.
/// Use this when C# enum names differ from Salesforce picklist values.
/// </summary>
/// <example>
/// public enum LeadStatus
/// {
///     [SalesforceValue("Open - Not Contacted")]
///     OpenNotContacted,
///
///     [SalesforceValue("Working - Contacted")]
///     WorkingContacted,
///
///     [SalesforceValue("Closed - Converted")]
///     ClosedConverted
/// }
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public class SalesforceValueAttribute : Attribute
{
    /// <summary>
    /// Gets the Salesforce API value for this enum member.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new SalesforceValue attribute.
    /// </summary>
    /// <param name="value">The Salesforce API value (e.g., "Open - Not Contacted").</param>
    public SalesforceValueAttribute(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}
