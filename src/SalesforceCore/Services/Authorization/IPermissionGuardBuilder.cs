using SalesforceCore.Models.Authorization;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Builder for chaining permission requirements with AND/OR logic.
/// Supports batch-optimized evaluation against Salesforce permissions.
/// </summary>
public interface IPermissionGuardBuilder
{
    /// <summary>
    /// Adds an object-level requirement (AND logic with previous requirements).
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="action">Required permission action.</param>
    /// <returns>This builder for chaining.</returns>
    IPermissionGuardBuilder Require(string objectName, PermissionAction action);
    
    /// <summary>
    /// Adds a field-level requirement (AND logic with previous requirements).
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="action">Required permission action.</param>
    /// <returns>This builder for chaining.</returns>
    IPermissionGuardBuilder RequireField(string objectName, string fieldName, PermissionAction action);
    
    /// <summary>
    /// Adds a requirement where at least one action must be allowed.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="actions">Actions where at least one must be allowed.</param>
    /// <returns>This builder for chaining.</returns>
    IPermissionGuardBuilder RequireAny(string objectName, params PermissionAction[] actions);
    
    /// <summary>
    /// Adds a requirement where all actions must be allowed.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="actions">Actions that must all be allowed.</param>
    /// <returns>This builder for chaining.</returns>
    IPermissionGuardBuilder RequireAll(string objectName, params PermissionAction[] actions);
    
    /// <summary>
    /// Switches to OR mode for the next requirement group.
    /// Requirements after this call form a new group that is OR'd with previous groups.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    IPermissionGuardBuilder Or();
    
    /// <summary>
    /// Evaluates all accumulated requirements against current user permissions.
    /// Requirements are batch-optimized by object to minimize API calls.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure with detailed violations.</returns>
    Task<PermissionGuardResult> EvaluateAsync(CancellationToken cancellationToken = default);
}
