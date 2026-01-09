namespace SalesforceCore.Models.Authorization;

/// <summary>
/// Result of evaluating permission guard requirements.
/// Contains success status and detailed violation information.
/// </summary>
public class PermissionGuardResult
{
    /// <summary>
    /// Whether all requirements were satisfied.
    /// </summary>
    public bool IsAllowed { get; private set; }
    
    /// <summary>
    /// List of permission violations when IsAllowed is false.
    /// </summary>
    public IReadOnlyList<PermissionViolation> Violations { get; private set; } = Array.Empty<PermissionViolation>();
    
    /// <summary>
    /// Timestamp when the evaluation was performed.
    /// </summary>
    public DateTimeOffset EvaluatedAt { get; private set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Creates a successful permission guard result.
    /// </summary>
    public static PermissionGuardResult Success()
    {
        return new PermissionGuardResult
        {
            IsAllowed = true,
            Violations = Array.Empty<PermissionViolation>()
        };
    }
    
    /// <summary>
    /// Creates a failed permission guard result with violations.
    /// </summary>
    /// <param name="violations">List of permission violations.</param>
    public static PermissionGuardResult Denied(IEnumerable<PermissionViolation> violations)
    {
        return new PermissionGuardResult
        {
            IsAllowed = false,
            Violations = violations.ToList()
        };
    }
    
    /// <summary>
    /// Creates a failed permission guard result with a single violation.
    /// </summary>
    /// <param name="objectName">Object that failed permission check.</param>
    /// <param name="action">Action that was denied.</param>
    /// <param name="reason">Reason for denial.</param>
    /// <param name="fieldName">Optional field that was denied.</param>
    public static PermissionGuardResult Denied(
        string objectName,
        PermissionAction action,
        string reason,
        string? fieldName = null)
    {
        return new PermissionGuardResult
        {
            IsAllowed = false,
            Violations = new List<PermissionViolation>
            {
                new PermissionViolation
                {
                    ObjectName = objectName,
                    FieldName = fieldName,
                    Action = action,
                    Reason = reason
                }
            }
        };
    }
}

/// <summary>
/// Represents a single permission violation in a guard evaluation.
/// </summary>
public class PermissionViolation
{
    /// <summary>
    /// Object where permission was denied.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field where permission was denied (null for object-level violations).
    /// </summary>
    public string? FieldName { get; set; }
    
    /// <summary>
    /// Action that was denied.
    /// </summary>
    public PermissionAction Action { get; set; }
    
    /// <summary>
    /// Human-readable reason for the denial.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets a formatted description of the violation.
    /// </summary>
    public override string ToString()
    {
        return FieldName != null
            ? $"{ObjectName}.{FieldName}: {Action} denied - {Reason}"
            : $"{ObjectName}: {Action} denied - {Reason}";
    }
}
