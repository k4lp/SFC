namespace SalesforceCore.Models.Authorization;

/// <summary>
/// Result of auditing a permission manifest against current user permissions.
/// </summary>
public class PermissionAuditResult
{
    /// <summary>
    /// Whether all manifest requirements are satisfied.
    /// </summary>
    public bool IsComplete => MissingObjectPermissions.Count == 0 && MissingFieldPermissions.Count == 0;
    
    /// <summary>
    /// Object-level permissions that are missing.
    /// Format: "ObjectName.Action"
    /// </summary>
    public IReadOnlyList<MissingPermission> MissingObjectPermissions { get; set; } = Array.Empty<MissingPermission>();
    
    /// <summary>
    /// Field-level permissions that are missing.
    /// Format: "ObjectName.FieldName.Action"
    /// </summary>
    public IReadOnlyList<MissingPermission> MissingFieldPermissions { get; set; } = Array.Empty<MissingPermission>();
    
    /// <summary>
    /// Requirements that were satisfied.
    /// </summary>
    public IReadOnlyList<string> SatisfiedRequirements { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// When the audit was performed.
    /// </summary>
    public DateTimeOffset AuditedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Total requirements in the manifest.
    /// </summary>
    public int TotalRequirements => SatisfiedRequirements.Count + MissingObjectPermissions.Count + MissingFieldPermissions.Count;
    
    /// <summary>
    /// Percentage of requirements satisfied (0-100).
    /// </summary>
    public int CompletionPercentage => TotalRequirements == 0 
        ? 100 
        : (int)((SatisfiedRequirements.Count * 100.0) / TotalRequirements);
    
    /// <summary>
    /// Creates a successful audit result (all permissions satisfied).
    /// </summary>
    public static PermissionAuditResult Complete(IEnumerable<string> satisfied)
    {
        return new PermissionAuditResult
        {
            SatisfiedRequirements = satisfied.ToList(),
            MissingObjectPermissions = Array.Empty<MissingPermission>(),
            MissingFieldPermissions = Array.Empty<MissingPermission>()
        };
    }
    
    /// <summary>
    /// Creates a partial audit result (some permissions missing).
    /// </summary>
    public static PermissionAuditResult Partial(
        IEnumerable<string> satisfied,
        IEnumerable<MissingPermission> missingObjects,
        IEnumerable<MissingPermission> missingFields)
    {
        return new PermissionAuditResult
        {
            SatisfiedRequirements = satisfied.ToList(),
            MissingObjectPermissions = missingObjects.ToList(),
            MissingFieldPermissions = missingFields.ToList()
        };
    }
}

/// <summary>
/// Represents a missing permission in an audit.
/// </summary>
public class MissingPermission
{
    /// <summary>
    /// Object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field API name (null for object-level permissions).
    /// </summary>
    public string? FieldName { get; set; }
    
    /// <summary>
    /// Missing action.
    /// </summary>
    public PermissionAction Action { get; set; }
    
    /// <summary>
    /// Description from the manifest (if provided).
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Formatted string representation.
    /// </summary>
    public override string ToString()
    {
        return FieldName != null
            ? $"{ObjectName}.{FieldName}.{Action}"
            : $"{ObjectName}.{Action}";
    }
}
