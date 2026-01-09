using SalesforceCore.Services.Authorization;

namespace SalesforceCore.Extensions;

/// <summary>
/// Extension methods for IPermissionService to provide fluent permission guard access.
/// </summary>
public static class PermissionServiceExtensions
{
    /// <summary>
    /// Creates a fluent permission guard for declarative permission checks.
    /// </summary>
    /// <param name="permissionService">The permission service.</param>
    /// <returns>A new permission guard instance.</returns>
    /// <example>
    /// <code>
    /// var result = await permissionService.Guard()
    ///     .Require("Account", PermissionAction.Read)
    ///     .RequireField("Account", "AnnualRevenue", PermissionAction.Read)
    ///     .EvaluateAsync();
    /// </code>
    /// </example>
    public static IPermissionGuard Guard(this IPermissionService permissionService)
    {
        return new PermissionGuard(permissionService);
    }
}
