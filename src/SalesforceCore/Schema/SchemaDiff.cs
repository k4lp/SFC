using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Schema;

/// <summary>
/// Represents the difference between two schema versions.
/// </summary>
public class SchemaDiff
{
    /// <summary>
    /// Object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the comparison.
    /// </summary>
    public DateTime ComparedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Source schema version/label.
    /// </summary>
    public string? SourceLabel { get; set; }

    /// <summary>
    /// Target schema version/label.
    /// </summary>
    public string? TargetLabel { get; set; }

    /// <summary>
    /// Fields that exist in source but not in target.
    /// </summary>
    public List<SObjectField> RemovedFields { get; set; } = new();

    /// <summary>
    /// Fields that exist in target but not in source.
    /// </summary>
    public List<SObjectField> AddedFields { get; set; } = new();

    /// <summary>
    /// Fields that exist in both but have changed.
    /// </summary>
    public List<FieldDiff> ModifiedFields { get; set; } = new();

    /// <summary>
    /// Child relationships that exist in source but not in target.
    /// </summary>
    public List<ChildRelationship> RemovedRelationships { get; set; } = new();

    /// <summary>
    /// Child relationships that exist in target but not in source.
    /// </summary>
    public List<ChildRelationship> AddedRelationships { get; set; } = new();

    /// <summary>
    /// Record types that exist in source but not in target.
    /// </summary>
    public List<RecordTypeInfo> RemovedRecordTypes { get; set; } = new();

    /// <summary>
    /// Record types that exist in target but not in source.
    /// </summary>
    public List<RecordTypeInfo> AddedRecordTypes { get; set; } = new();

    /// <summary>
    /// Whether there are any differences.
    /// </summary>
    public bool HasChanges =>
        RemovedFields.Count > 0 ||
        AddedFields.Count > 0 ||
        ModifiedFields.Count > 0 ||
        RemovedRelationships.Count > 0 ||
        AddedRelationships.Count > 0 ||
        RemovedRecordTypes.Count > 0 ||
        AddedRecordTypes.Count > 0;

    /// <summary>
    /// Gets a summary of all changes.
    /// </summary>
    public string GetSummary()
    {
        var parts = new List<string>();

        if (AddedFields.Count > 0)
            parts.Add($"{AddedFields.Count} field(s) added");
        if (RemovedFields.Count > 0)
            parts.Add($"{RemovedFields.Count} field(s) removed");
        if (ModifiedFields.Count > 0)
            parts.Add($"{ModifiedFields.Count} field(s) modified");
        if (AddedRelationships.Count > 0)
            parts.Add($"{AddedRelationships.Count} relationship(s) added");
        if (RemovedRelationships.Count > 0)
            parts.Add($"{RemovedRelationships.Count} relationship(s) removed");
        if (AddedRecordTypes.Count > 0)
            parts.Add($"{AddedRecordTypes.Count} record type(s) added");
        if (RemovedRecordTypes.Count > 0)
            parts.Add($"{RemovedRecordTypes.Count} record type(s) removed");

        return parts.Count > 0
            ? string.Join(", ", parts)
            : "No changes detected";
    }
}

/// <summary>
/// Represents differences in a single field between two schemas.
/// </summary>
public class FieldDiff
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The field from the source schema.
    /// </summary>
    public SObjectField SourceField { get; set; } = null!;

    /// <summary>
    /// The field from the target schema.
    /// </summary>
    public SObjectField TargetField { get; set; } = null!;

    /// <summary>
    /// List of property changes.
    /// </summary>
    public List<PropertyChange> Changes { get; set; } = new();

    /// <summary>
    /// Picklist values that were added.
    /// </summary>
    public List<PicklistEntry> AddedPicklistValues { get; set; } = new();

    /// <summary>
    /// Picklist values that were removed.
    /// </summary>
    public List<PicklistEntry> RemovedPicklistValues { get; set; } = new();

    /// <summary>
    /// Whether this is a breaking change.
    /// </summary>
    public bool IsBreakingChange =>
        Changes.Any(c => c.IsBreaking) ||
        RemovedPicklistValues.Any(p => p.Active);
}

/// <summary>
/// Represents a change to a single property.
/// </summary>
public class PropertyChange
{
    /// <summary>
    /// Property name.
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Old value.
    /// </summary>
    public object? OldValue { get; set; }

    /// <summary>
    /// New value.
    /// </summary>
    public object? NewValue { get; set; }

    /// <summary>
    /// Whether this is a breaking change.
    /// </summary>
    public bool IsBreaking { get; set; }

    /// <summary>
    /// Severity of the change.
    /// </summary>
    public ChangeSeverity Severity { get; set; }

    /// <summary>
    /// Human-readable description of the change.
    /// </summary>
    public string Description =>
        $"{PropertyName}: {FormatValue(OldValue)} -> {FormatValue(NewValue)}";

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is bool b) return b.ToString().ToLowerInvariant();
        if (value is string s) return $"\"{s}\"";
        return value.ToString() ?? "null";
    }
}

/// <summary>
/// Severity level of a schema change.
/// </summary>
public enum ChangeSeverity
{
    /// <summary>Informational change with no impact.</summary>
    Info,
    /// <summary>Minor change that shouldn't affect functionality.</summary>
    Minor,
    /// <summary>Moderate change that may require attention.</summary>
    Moderate,
    /// <summary>Major change that likely requires code changes.</summary>
    Major,
    /// <summary>Breaking change that will cause failures.</summary>
    Breaking
}

/// <summary>
/// Service for comparing Salesforce schemas.
/// </summary>
public interface ISchemaDiffService
{
    /// <summary>
    /// Compares two object describe metadata.
    /// </summary>
    SchemaDiff Compare(SObjectDescribe source, SObjectDescribe target);

    /// <summary>
    /// Compares a saved schema snapshot with the current schema.
    /// </summary>
    Task<SchemaDiff> CompareWithCurrentAsync(
        string objectName,
        SObjectDescribe savedSchema,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets breaking changes between two schemas.
    /// </summary>
    IEnumerable<FieldDiff> GetBreakingChanges(SchemaDiff diff);

    /// <summary>
    /// Generates a migration report from a diff.
    /// </summary>
    string GenerateMigrationReport(SchemaDiff diff);
}

/// <summary>
/// Implementation of schema diff service.
/// </summary>
public class SchemaDiffService : ISchemaDiffService
{
    private readonly Services.Metadata.ISchemaService? _schemaService;

    // Properties that are considered important for diff detection
    private static readonly HashSet<string> ImportantProperties = new()
    {
        "Type", "Length", "Precision", "Scale", "Nillable", "Createable",
        "Updateable", "Unique", "ExternalId", "DefaultValue", "ReferenceTo"
    };

    // Properties where changes are breaking
    private static readonly HashSet<string> BreakingProperties = new()
    {
        "Type", "Createable", "Updateable", "ReferenceTo", "RestrictedPicklist"
    };

    public SchemaDiffService() { }

    public SchemaDiffService(Services.Metadata.ISchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    /// <inheritdoc/>
    public SchemaDiff Compare(SObjectDescribe source, SObjectDescribe target)
    {
        var diff = new SchemaDiff
        {
            ObjectName = target.Name,
            SourceLabel = source.Label,
            TargetLabel = target.Label
        };

        // Compare fields
        var sourceFields = source.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var targetFields = target.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        // Find removed fields
        foreach (var (name, field) in sourceFields)
        {
            if (!targetFields.ContainsKey(name))
            {
                diff.RemovedFields.Add(field);
            }
        }

        // Find added fields
        foreach (var (name, field) in targetFields)
        {
            if (!sourceFields.ContainsKey(name))
            {
                diff.AddedFields.Add(field);
            }
        }

        // Find modified fields
        foreach (var (name, sourceField) in sourceFields)
        {
            if (targetFields.TryGetValue(name, out var targetField))
            {
                var fieldDiff = CompareFields(sourceField, targetField);
                if (fieldDiff.Changes.Count > 0 ||
                    fieldDiff.AddedPicklistValues.Count > 0 ||
                    fieldDiff.RemovedPicklistValues.Count > 0)
                {
                    diff.ModifiedFields.Add(fieldDiff);
                }
            }
        }

        // Compare child relationships
        var sourceRels = source.ChildRelationships
            .Where(r => !string.IsNullOrEmpty(r.RelationshipName))
            .ToDictionary(r => r.RelationshipName!, StringComparer.OrdinalIgnoreCase);
        var targetRels = target.ChildRelationships
            .Where(r => !string.IsNullOrEmpty(r.RelationshipName))
            .ToDictionary(r => r.RelationshipName!, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rel) in sourceRels)
        {
            if (!targetRels.ContainsKey(name))
                diff.RemovedRelationships.Add(rel);
        }

        foreach (var (name, rel) in targetRels)
        {
            if (!sourceRels.ContainsKey(name))
                diff.AddedRelationships.Add(rel);
        }

        // Compare record types
        var sourceTypes = source.RecordTypeInfos
            .Where(r => !string.IsNullOrEmpty(r.DeveloperName))
            .ToDictionary(r => r.DeveloperName!, StringComparer.OrdinalIgnoreCase);
        var targetTypes = target.RecordTypeInfos
            .Where(r => !string.IsNullOrEmpty(r.DeveloperName))
            .ToDictionary(r => r.DeveloperName!, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rt) in sourceTypes)
        {
            if (!targetTypes.ContainsKey(name))
                diff.RemovedRecordTypes.Add(rt);
        }

        foreach (var (name, rt) in targetTypes)
        {
            if (!sourceTypes.ContainsKey(name))
                diff.AddedRecordTypes.Add(rt);
        }

        return diff;
    }

    /// <inheritdoc/>
    public async Task<SchemaDiff> CompareWithCurrentAsync(
        string objectName,
        SObjectDescribe savedSchema,
        CancellationToken cancellationToken = default)
    {
        if (_schemaService == null)
            throw new InvalidOperationException("Schema service not available");

        var currentSchema = await _schemaService.GetDescribeAsync(objectName, cancellationToken);
        if (currentSchema == null)
            throw new InvalidOperationException($"Could not retrieve schema for {objectName}");

        return Compare(savedSchema, currentSchema);
    }

    /// <inheritdoc/>
    public IEnumerable<FieldDiff> GetBreakingChanges(SchemaDiff diff)
    {
        return diff.ModifiedFields.Where(f => f.IsBreakingChange);
    }

    /// <inheritdoc/>
    public string GenerateMigrationReport(SchemaDiff diff)
    {
        var lines = new List<string>
        {
            $"Schema Diff Report for {diff.ObjectName}",
            $"Generated: {diff.ComparedAt:yyyy-MM-dd HH:mm:ss} UTC",
            $"Source: {diff.SourceLabel}",
            $"Target: {diff.TargetLabel}",
            "",
            $"Summary: {diff.GetSummary()}",
            ""
        };

        if (diff.AddedFields.Count > 0)
        {
            lines.Add("=== Added Fields ===");
            foreach (var field in diff.AddedFields)
            {
                lines.Add($"  + {field.Name} ({field.Type}): {field.Label}");
            }
            lines.Add("");
        }

        if (diff.RemovedFields.Count > 0)
        {
            lines.Add("=== Removed Fields ===");
            foreach (var field in diff.RemovedFields)
            {
                lines.Add($"  - {field.Name} ({field.Type}): {field.Label}");
            }
            lines.Add("");
        }

        if (diff.ModifiedFields.Count > 0)
        {
            lines.Add("=== Modified Fields ===");
            foreach (var fieldDiff in diff.ModifiedFields)
            {
                var breakingFlag = fieldDiff.IsBreakingChange ? " [BREAKING]" : "";
                lines.Add($"  ~ {fieldDiff.FieldName}{breakingFlag}");

                foreach (var change in fieldDiff.Changes)
                {
                    var severityFlag = change.Severity >= ChangeSeverity.Major ? " [!]" : "";
                    lines.Add($"      {change.Description}{severityFlag}");
                }

                if (fieldDiff.AddedPicklistValues.Count > 0)
                {
                    lines.Add($"      Picklist values added: {string.Join(", ", fieldDiff.AddedPicklistValues.Select(p => p.Value))}");
                }
                if (fieldDiff.RemovedPicklistValues.Count > 0)
                {
                    lines.Add($"      Picklist values removed: {string.Join(", ", fieldDiff.RemovedPicklistValues.Select(p => p.Value))}");
                }
            }
            lines.Add("");
        }

        if (diff.AddedRelationships.Count > 0)
        {
            lines.Add("=== Added Relationships ===");
            foreach (var rel in diff.AddedRelationships)
            {
                lines.Add($"  + {rel.RelationshipName} -> {rel.ChildSObject}");
            }
            lines.Add("");
        }

        if (diff.RemovedRelationships.Count > 0)
        {
            lines.Add("=== Removed Relationships ===");
            foreach (var rel in diff.RemovedRelationships)
            {
                lines.Add($"  - {rel.RelationshipName} -> {rel.ChildSObject}");
            }
            lines.Add("");
        }

        if (diff.AddedRecordTypes.Count > 0)
        {
            lines.Add("=== Added Record Types ===");
            foreach (var rt in diff.AddedRecordTypes)
            {
                lines.Add($"  + {rt.DeveloperName}: {rt.Name}");
            }
            lines.Add("");
        }

        if (diff.RemovedRecordTypes.Count > 0)
        {
            lines.Add("=== Removed Record Types ===");
            foreach (var rt in diff.RemovedRecordTypes)
            {
                lines.Add($"  - {rt.DeveloperName}: {rt.Name}");
            }
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private FieldDiff CompareFields(SObjectField source, SObjectField target)
    {
        var diff = new FieldDiff
        {
            FieldName = source.Name,
            SourceField = source,
            TargetField = target
        };

        // Compare important properties
        CompareProperty(diff, "Type", source.Type, target.Type);
        CompareProperty(diff, "Length", source.Length, target.Length);
        CompareProperty(diff, "Precision", source.Precision, target.Precision);
        CompareProperty(diff, "Scale", source.Scale, target.Scale);
        CompareProperty(diff, "Nillable", source.Nillable, target.Nillable);
        CompareProperty(diff, "Createable", source.Createable, target.Createable);
        CompareProperty(diff, "Updateable", source.Updateable, target.Updateable);
        CompareProperty(diff, "Unique", source.Unique, target.Unique);
        CompareProperty(diff, "ExternalId", source.ExternalId, target.ExternalId);
        CompareProperty(diff, "RestrictedPicklist", source.RestrictedPicklist, target.RestrictedPicklist);
        CompareProperty(diff, "DefaultValue", source.DefaultValue, target.DefaultValue);

        // Compare reference targets
        if (source.ReferenceTo != null && target.ReferenceTo != null)
        {
            var sourceRefs = string.Join(",", source.ReferenceTo.OrderBy(r => r));
            var targetRefs = string.Join(",", target.ReferenceTo.OrderBy(r => r));
            if (sourceRefs != targetRefs)
            {
                diff.Changes.Add(new PropertyChange
                {
                    PropertyName = "ReferenceTo",
                    OldValue = sourceRefs,
                    NewValue = targetRefs,
                    IsBreaking = true,
                    Severity = ChangeSeverity.Breaking
                });
            }
        }

        // Compare picklist values
        if (source.IsPicklist && target.IsPicklist)
        {
            var sourceValues = source.PicklistValues
                .ToDictionary(p => p.Value, StringComparer.OrdinalIgnoreCase);
            var targetValues = target.PicklistValues
                .ToDictionary(p => p.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var (value, entry) in sourceValues)
            {
                if (!targetValues.ContainsKey(value))
                    diff.RemovedPicklistValues.Add(entry);
            }

            foreach (var (value, entry) in targetValues)
            {
                if (!sourceValues.ContainsKey(value))
                    diff.AddedPicklistValues.Add(entry);
            }
        }

        return diff;
    }

    private static void CompareProperty(FieldDiff diff, string name, object? source, object? target)
    {
        if (!Equals(source, target))
        {
            var isBreaking = BreakingProperties.Contains(name);
            var severity = DetermineSeverity(name, source, target);

            diff.Changes.Add(new PropertyChange
            {
                PropertyName = name,
                OldValue = source,
                NewValue = target,
                IsBreaking = isBreaking,
                Severity = severity
            });
        }
    }

    private static ChangeSeverity DetermineSeverity(string propName, object? oldValue, object? newValue)
    {
        // Type changes are always breaking
        if (propName == "Type")
            return ChangeSeverity.Breaking;

        // Making non-createable or non-updateable is breaking
        if ((propName == "Createable" || propName == "Updateable") &&
            oldValue is true && newValue is false)
            return ChangeSeverity.Breaking;

        // Reducing length is potentially breaking
        if (propName == "Length" && oldValue is int oldLen && newValue is int newLen && newLen < oldLen)
            return ChangeSeverity.Major;

        // Making non-nillable is breaking
        if (propName == "Nillable" && oldValue is true && newValue is false)
            return ChangeSeverity.Breaking;

        // Reducing precision/scale is breaking
        if ((propName == "Precision" || propName == "Scale") &&
            oldValue is int oldP && newValue is int newP && newP < oldP)
            return ChangeSeverity.Major;

        // Adding unique constraint
        if (propName == "Unique" && oldValue is false && newValue is true)
            return ChangeSeverity.Major;

        // Default to minor
        return ChangeSeverity.Minor;
    }
}
