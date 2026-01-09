namespace SalesforceCore.Models.Authorization;

/// <summary>
/// Defines expected permissions for an application.
/// Used to validate that a user has all required permissions.
/// </summary>
public class PermissionManifest
{
    /// <summary>
    /// Object-level permission requirements.
    /// </summary>
    public List<ObjectPermissionRequirement> ObjectRequirements { get; set; } = new();
    
    /// <summary>
    /// Field-level permission requirements.
    /// </summary>
    public List<FieldPermissionRequirement> FieldRequirements { get; set; } = new();
    
    /// <summary>
    /// Creates a new manifest builder.
    /// </summary>
    public static PermissionManifestBuilder Create() => new();
    
    /// <summary>
    /// Creates a manifest from an existing set of requirements.
    /// </summary>
    public static PermissionManifest FromRequirements(
        IEnumerable<ObjectPermissionRequirement> objectRequirements,
        IEnumerable<FieldPermissionRequirement>? fieldRequirements = null)
    {
        return new PermissionManifest
        {
            ObjectRequirements = objectRequirements.ToList(),
            FieldRequirements = fieldRequirements?.ToList() ?? new List<FieldPermissionRequirement>()
        };
    }
}

/// <summary>
/// Object-level permission requirement for manifest.
/// </summary>
public class ObjectPermissionRequirement
{
    /// <summary>
    /// Salesforce object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// Required actions on the object.
    /// </summary>
    public List<PermissionAction> Actions { get; set; } = new();
    
    /// <summary>
    /// Description of why this permission is needed.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Field-level permission requirement for manifest.
/// </summary>
public class FieldPermissionRequirement
{
    /// <summary>
    /// Salesforce object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Required action on the field.
    /// </summary>
    public PermissionAction Action { get; set; }
    
    /// <summary>
    /// Description of why this permission is needed.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Builder for creating permission manifests fluently.
/// </summary>
public class PermissionManifestBuilder
{
    private readonly List<ObjectPermissionRequirement> _objectRequirements = new();
    private readonly List<FieldPermissionRequirement> _fieldRequirements = new();

    /// <summary>
    /// Adds a required object permission.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="actions">Required actions.</param>
    public PermissionManifestBuilder RequireObject(string objectName, params PermissionAction[] actions)
    {
        _objectRequirements.Add(new ObjectPermissionRequirement
        {
            ObjectName = objectName,
            Actions = actions.ToList()
        });
        return this;
    }
    
    /// <summary>
    /// Adds a required object permission with description.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="description">Why this permission is needed.</param>
    /// <param name="actions">Required actions.</param>
    public PermissionManifestBuilder RequireObject(string objectName, string description, params PermissionAction[] actions)
    {
        _objectRequirements.Add(new ObjectPermissionRequirement
        {
            ObjectName = objectName,
            Actions = actions.ToList(),
            Description = description
        });
        return this;
    }
    
    /// <summary>
    /// Adds a required field permission.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="action">Required action.</param>
    public PermissionManifestBuilder RequireField(string objectName, string fieldName, PermissionAction action)
    {
        _fieldRequirements.Add(new FieldPermissionRequirement
        {
            ObjectName = objectName,
            FieldName = fieldName,
            Action = action
        });
        return this;
    }
    
    /// <summary>
    /// Adds a required field permission with description.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="action">Required action.</param>
    /// <param name="description">Why this permission is needed.</param>
    public PermissionManifestBuilder RequireField(string objectName, string fieldName, PermissionAction action, string description)
    {
        _fieldRequirements.Add(new FieldPermissionRequirement
        {
            ObjectName = objectName,
            FieldName = fieldName,
            Action = action,
            Description = description
        });
        return this;
    }
    
    /// <summary>
    /// Builds the permission manifest.
    /// </summary>
    public PermissionManifest Build()
    {
        return new PermissionManifest
        {
            ObjectRequirements = _objectRequirements.ToList(),
            FieldRequirements = _fieldRequirements.ToList()
        };
    }
}
