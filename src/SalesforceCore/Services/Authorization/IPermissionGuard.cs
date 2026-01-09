using SalesforceCore.Models.Authorization;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Fluent permission guard for declarative, chainable permission checks.
/// Entry point for building batch-optimized permission requirements.
/// </summary>
/// <example>
/// <code>
/// var result = await permissionService.Guard()
///     .Require("Account", PermissionAction.Read)
///     .RequireField("Account", "AnnualRevenue", PermissionAction.Read)
///     .EvaluateAsync();
/// 
/// if (!result.IsAllowed)
///     throw new PermissionDeniedException(result.Violations);
/// </code>
/// </example>
public interface IPermissionGuard
{
    /// <summary>
    /// Starts a permission guard builder with an object-level requirement.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="action">Required permission action.</param>
    /// <returns>Builder for chaining additional requirements.</returns>
    IPermissionGuardBuilder Require(string objectName, PermissionAction action);
    
    /// <summary>
    /// Starts a permission guard builder with a field-level requirement.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="action">Required permission action.</param>
    /// <returns>Builder for chaining additional requirements.</returns>
    IPermissionGuardBuilder RequireField(string objectName, string fieldName, PermissionAction action);
    
    /// <summary>
    /// Starts a permission guard builder requiring at least one of the specified actions.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="actions">Actions where at least one must be allowed.</param>
    /// <returns>Builder for chaining additional requirements.</returns>
    IPermissionGuardBuilder RequireAny(string objectName, params PermissionAction[] actions);
    
    /// <summary>
    /// Starts a permission guard builder requiring all specified actions.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="actions">Actions that must all be allowed.</param>
    /// <returns>Builder for chaining additional requirements.</returns>
    IPermissionGuardBuilder RequireAll(string objectName, params PermissionAction[] actions);
}
