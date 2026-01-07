using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Models.Authorization;

/// <summary>
/// Snapshot of all permissions for a specific object.
/// Contains CRUD flags and field-level permissions for the current user.
/// </summary>
public class ObjectPermissionSnapshot
{
    /// <summary>
    /// Object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Object display label.
    /// </summary>
    public string ObjectLabel { get; set; } = string.Empty;

    /// <summary>
    /// Whether the current user can create records.
    /// </summary>
    public bool CanCreate { get; set; }

    /// <summary>
    /// Whether the current user can read records.
    /// </summary>
    public bool CanRead { get; set; }

    /// <summary>
    /// Whether the current user can update records.
    /// </summary>
    public bool CanUpdate { get; set; }

    /// <summary>
    /// Whether the current user can delete records.
    /// </summary>
    public bool CanDelete { get; set; }

    /// <summary>
    /// Whether the object is queryable via SOQL.
    /// </summary>
    public bool IsQueryable { get; set; }

    /// <summary>
    /// Whether the object is searchable via SOSL.
    /// </summary>
    public bool IsSearchable { get; set; }

    /// <summary>
    /// Field-level permissions keyed by field API name.
    /// </summary>
    public Dictionary<string, FieldPermission> FieldPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Record types available for this object.
    /// </summary>
    public List<RecordTypePermission> RecordTypes { get; set; } = new();

    /// <summary>
    /// Timestamp when this snapshot was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a snapshot from object describe metadata.
    /// </summary>
    public static ObjectPermissionSnapshot FromDescribe(SObjectDescribe describe)
    {
        var snapshot = new ObjectPermissionSnapshot
        {
            ObjectName = describe.Name,
            ObjectLabel = describe.Label,
            CanCreate = describe.Createable,
            CanRead = describe.Retrieveable,
            CanUpdate = describe.Updateable,
            CanDelete = describe.Deletable,
            IsQueryable = describe.Queryable,
            IsSearchable = describe.Searchable
        };

        foreach (var field in describe.Fields)
        {
            snapshot.FieldPermissions[field.Name] = FieldPermission.FromField(field);
        }

        foreach (var rt in describe.RecordTypeInfos.Where(r => r.Available))
        {
            snapshot.RecordTypes.Add(new RecordTypePermission
            {
                RecordTypeId = rt.RecordTypeId,
                Name = rt.Name,
                DeveloperName = rt.DeveloperName,
                IsDefault = rt.DefaultRecordTypeMapping,
                IsAvailable = rt.Available
            });
        }

        return snapshot;
    }

    /// <summary>
    /// Creates a snapshot that denies all access (fail-secure fallback).
    /// Used when permission fetch times out or fails.
    /// </summary>
    public static ObjectPermissionSnapshot DenyAll(string objectName)
    {
        return new ObjectPermissionSnapshot
        {
            ObjectName = objectName,
            ObjectLabel = objectName,
            CanCreate = false,
            CanRead = false,
            CanUpdate = false,
            CanDelete = false,
            IsQueryable = false,
            IsSearchable = false
        };
    }

    /// <summary>
    /// Creates a snapshot that allows read-only access (graceful degradation fallback).
    /// Used when permission fetch times out but read access should be allowed.
    /// </summary>
    public static ObjectPermissionSnapshot ReadOnly(string objectName)
    {
        return new ObjectPermissionSnapshot
        {
            ObjectName = objectName,
            ObjectLabel = objectName,
            CanCreate = false,
            CanRead = true,
            CanUpdate = false,
            CanDelete = false,
            IsQueryable = true,
            IsSearchable = true
        };
    }
}

/// <summary>
/// Permission information for a single field.
/// </summary>
public class FieldPermission
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Field display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Alias for <see cref="Label"/> to align with docs and UI bindings.
    /// </summary>
    public string FieldLabel
    {
        get => Label;
        set => Label = value;
    }

    /// <summary>
    /// Whether the user can read this field.
    /// </summary>
    public bool CanRead { get; set; }

    /// <summary>
    /// Whether the user can set this field on create.
    /// </summary>
    public bool CanCreate { get; set; }

    /// <summary>
    /// Whether the user can update this field.
    /// </summary>
    public bool CanUpdate { get; set; }

    /// <summary>
    /// Whether the field is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Salesforce field type.
    /// </summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>
    /// Max length for string/textarea fields when available.
    /// </summary>
    public int? Length { get; set; }

    /// <summary>
    /// Creates a field permission from field metadata.
    /// </summary>
    public static FieldPermission FromField(SObjectField field)
    {
        return new FieldPermission
        {
            FieldName = field.Name,
            Label = field.Label,
            CanRead = field.Accessible,
            CanCreate = field.Createable,
            CanUpdate = field.Updateable,
            IsRequired = field.IsRequired,
            FieldType = field.Type,
            Length = field.Length > 0 ? field.Length : null
        };
    }
}

/// <summary>
/// Permission information for a record type.
/// </summary>
public class RecordTypePermission
{
    /// <summary>
    /// Record type ID.
    /// </summary>
    public string RecordTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Record type name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Developer name.
    /// </summary>
    public string DeveloperName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default record type.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this record type is available to the user.
    /// </summary>
    public bool IsAvailable { get; set; }
}

/// <summary>
/// Context for permission requests.
/// </summary>
public class PermissionRequestContext
{
    /// <summary>
    /// List of objects to get permissions for.
    /// </summary>
    public List<string> Objects { get; set; } = new();

    /// <summary>
    /// Whether to include field-level permissions.
    /// </summary>
    public bool IncludeFields { get; set; } = true;

    /// <summary>
    /// Whether to include record types.
    /// </summary>
    public bool IncludeRecordTypes { get; set; } = true;

    /// <summary>
    /// Specific fields to include (null = all fields).
    /// </summary>
    public List<string>? SpecificFields { get; set; }

    /// <summary>
    /// Creates a context for a single object.
    /// </summary>
    public static PermissionRequestContext ForObject(string objectName)
    {
        return new PermissionRequestContext
        {
            Objects = new List<string> { objectName }
        };
    }

    /// <summary>
    /// Creates a context for multiple objects.
    /// </summary>
    public static PermissionRequestContext ForObjects(params string[] objectNames)
    {
        return new PermissionRequestContext
        {
            Objects = objectNames.ToList()
        };
    }
}

/// <summary>
/// Result of a permission request containing snapshots for multiple objects.
/// </summary>
public class PermissionResult
{
    /// <summary>
    /// Permission snapshots keyed by object API name.
    /// </summary>
    public Dictionary<string, ObjectPermissionSnapshot> Snapshots { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Objects that failed to load.
    /// </summary>
    public List<PermissionError> Errors { get; set; } = new();

    /// <summary>
    /// Whether all requested objects were loaded successfully.
    /// </summary>
    public bool IsComplete => Errors.Count == 0;

    /// <summary>
    /// Gets a snapshot by object name.
    /// </summary>
    public ObjectPermissionSnapshot? GetSnapshot(string objectName)
    {
        return Snapshots.TryGetValue(objectName, out var snapshot) ? snapshot : null;
    }

    /// <summary>
    /// Checks if an action is allowed on an object.
    /// </summary>
    public bool CanPerformAction(string objectName, PermissionAction action)
    {
        if (!Snapshots.TryGetValue(objectName, out var snapshot))
            return false;

        return action switch
        {
            PermissionAction.Create => snapshot.CanCreate,
            PermissionAction.Read => snapshot.CanRead,
            PermissionAction.Update => snapshot.CanUpdate,
            PermissionAction.Delete => snapshot.CanDelete,
            _ => false
        };
    }
}

/// <summary>
/// Error information for failed permission lookups.
/// </summary>
public class PermissionError
{
    /// <summary>
    /// Object that failed.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error code.
    /// </summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// CRUD permission actions.
/// </summary>
public enum PermissionAction
{
    /// <summary>Create action.</summary>
    Create,
    /// <summary>Read action.</summary>
    Read,
    /// <summary>Update action.</summary>
    Update,
    /// <summary>Delete action.</summary>
    Delete
}

/// <summary>
/// Result of checking a specific permission.
/// </summary>
public class PermissionCheckResult
{
    /// <summary>
    /// Whether the action is allowed.
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// Reason if not allowed.
    /// </summary>
    public string? DenialReason { get; set; }

    /// <summary>
    /// Object the check was for.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Field the check was for (if applicable).
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// Action that was checked.
    /// </summary>
    public PermissionAction Action { get; set; }

    /// <summary>
    /// Creates an allowed result.
    /// </summary>
    public static PermissionCheckResult Allowed(string objectName, PermissionAction action, string? fieldName = null)
    {
        return new PermissionCheckResult
        {
            IsAllowed = true,
            ObjectName = objectName,
            FieldName = fieldName,
            Action = action
        };
    }

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static PermissionCheckResult Denied(string objectName, PermissionAction action, string reason, string? fieldName = null)
    {
        return new PermissionCheckResult
        {
            IsAllowed = false,
            DenialReason = reason,
            ObjectName = objectName,
            FieldName = fieldName,
            Action = action
        };
    }
}
