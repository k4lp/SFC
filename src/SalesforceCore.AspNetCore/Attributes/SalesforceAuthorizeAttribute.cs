using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.AspNetCore.Attributes;

/// <summary>
/// Defines the type of object access level to check.
/// </summary>
public enum ObjectAccessLevel
{
    /// <summary>Read access.</summary>
    Read,
    /// <summary>Create access.</summary>
    Create,
    /// <summary>Edit access.</summary>
    Edit,
    /// <summary>Delete access.</summary>
    Delete,
    /// <summary>Full access (all CRUD).</summary>
    Full
}

/// <summary>
/// Authorization attribute that checks Salesforce permissions before allowing action execution.
/// Supports permission sets, profiles, custom permissions, and object-level permissions.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to controllers or actions that require specific Salesforce permissions.
/// </para>
/// <para>
/// Examples:
/// <code>
/// // Check for a specific permission set
/// [SalesforceAuthorize(PermissionSet = "Admin_Access")]
/// public class AdminController : Controller { }
///
/// // Check for object-level permission
/// [SalesforceAuthorize(ObjectPermission = "Account", AccessLevel = ObjectAccessLevel.Edit)]
/// public async Task&lt;IActionResult&gt; EditAccount(string id) { }
///
/// // Check for custom permission
/// [SalesforceAuthorize(CustomPermission = "ViewReports")]
/// public async Task&lt;IActionResult&gt; Reports() { }
///
/// // Check for specific profile
/// [SalesforceAuthorize(Profile = "System Administrator")]
/// public async Task&lt;IActionResult&gt; SystemSettings() { }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class SalesforceAuthorizeAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// The permission set API name to check.
    /// </summary>
    public string? PermissionSet { get; set; }

    /// <summary>
    /// The profile name to check.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// The custom permission API name to check.
    /// </summary>
    public string? CustomPermission { get; set; }

    /// <summary>
    /// The object API name for object-level permission check.
    /// </summary>
    public string? ObjectPermission { get; set; }

    /// <summary>
    /// The access level required for object permission.
    /// </summary>
    public ObjectAccessLevel AccessLevel { get; set; } = ObjectAccessLevel.Read;

    /// <summary>
    /// Error message to display when authorization fails.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <inheritdoc/>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.Filters.Any(f => f is IAllowAnonymousFilter))
        {
            return;
        }

        var logger = context.HttpContext.RequestServices.GetService<ILogger<SalesforceAuthorizeAttribute>>();
        var hostEnvironment = context.HttpContext.RequestServices.GetService<IHostEnvironment>();

        // Get data service for queries
        var dataService = context.HttpContext.RequestServices.GetService<IDataService>();
        if (dataService == null)
        {
            logger?.LogError("Salesforce authorization failed: IDataService not configured.");
            // If no data service is configured, fail authorization
            SetForbidResult(context, "Salesforce data service not configured.");
            return;
        }

        try
        {
            // Get current user ID from claims
            var userId = GetCurrentUserId(context);
            if (string.IsNullOrEmpty(userId))
            {
                logger?.LogWarning("Salesforce authorization failed: no user id claim found.");
                SetForbidResult(context, "User not authenticated.");
                return;
            }

            if (!SecurityUtils.IsValidSalesforceId(userId))
            {
                logger?.LogDebug("Salesforce authorization: user id claim is not a standard Salesforce Id format. UserId={UserId}", userId);
            }

            var cancellationToken = context.HttpContext.RequestAborted;

            // Check permission set
            if (!string.IsNullOrEmpty(PermissionSet))
            {
                var hasPermission = await CheckPermissionSetAsync(dataService, userId, PermissionSet, cancellationToken);
                if (!hasPermission)
                {
                    logger?.LogDebug("Salesforce authorization denied. UserId={UserId}, MissingPermissionSet={PermissionSet}", userId, PermissionSet);
                    SetForbidResult(context, ErrorMessage ?? $"Missing permission set: {PermissionSet}");
                    return;
                }
            }

            // Check profile
            if (!string.IsNullOrEmpty(Profile))
            {
                var hasProfile = await CheckProfileAsync(dataService, userId, Profile, cancellationToken);
                if (!hasProfile)
                {
                    logger?.LogDebug("Salesforce authorization denied. UserId={UserId}, RequiredProfile={Profile}", userId, Profile);
                    SetForbidResult(context, ErrorMessage ?? $"Access denied. Required profile: {Profile}");
                    return;
                }
            }

            // Check custom permission
            if (!string.IsNullOrEmpty(CustomPermission))
            {
                var hasCustomPerm = await CheckCustomPermissionAsync(dataService, userId, CustomPermission, cancellationToken);
                if (!hasCustomPerm)
                {
                    logger?.LogDebug("Salesforce authorization denied. UserId={UserId}, MissingCustomPermission={CustomPermission}", userId, CustomPermission);
                    SetForbidResult(context, ErrorMessage ?? $"Missing custom permission: {CustomPermission}");
                    return;
                }
            }

            // Check object permission
            if (!string.IsNullOrEmpty(ObjectPermission))
            {
                if (!SecurityUtils.IsValidObjectName(ObjectPermission))
                {
                    logger?.LogError("Salesforce authorization misconfigured: invalid ObjectPermission value: {ObjectPermission}", ObjectPermission);
                    SetForbidResult(context, ErrorMessage ?? "Authorization check misconfigured.");
                    return;
                }

                var hasObjectPerm = await CheckObjectPermissionAsync(dataService, userId, ObjectPermission, AccessLevel, cancellationToken);
                if (!hasObjectPerm)
                {
                    logger?.LogDebug("Salesforce authorization denied. UserId={UserId}, Object={ObjectPermission}, AccessLevel={AccessLevel}", userId, ObjectPermission, AccessLevel);
                    SetForbidResult(context, ErrorMessage ?? $"No {AccessLevel} access to {ObjectPermission}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Salesforce authorization check failed. PermissionSet={PermissionSet}, Profile={Profile}, CustomPermission={CustomPermission}, ObjectPermission={ObjectPermission}, AccessLevel={AccessLevel}",
                PermissionSet,
                Profile,
                CustomPermission,
                ObjectPermission,
                AccessLevel);

            var message = ErrorMessage ?? "Authorization check failed.";
            if (hostEnvironment?.IsDevelopment() == true)
            {
                message = ErrorMessage ?? $"Authorization check failed: {ex.Message}";
            }

            SetForbidResult(context, message);
        }
    }

    private static async Task<bool> CheckPermissionSetAsync(
        IDataService dataService,
        string userId,
        string permissionSetName,
        CancellationToken cancellationToken)
    {
        var query = SoqlBuilder.From("PermissionSetAssignment")
            .Select("Id")
            .WhereEquals("AssigneeId", userId)
            .WhereEquals("PermissionSet.Name", permissionSetName)
            .Limit(1)
            .Build();

        var result = await dataService.QueryAsync(query, cancellationToken);
        return result.Records.Count > 0;
    }

    private static async Task<bool> CheckProfileAsync(
        IDataService dataService,
        string userId,
        string profileName,
        CancellationToken cancellationToken)
    {
        var query = SoqlBuilder.From("User")
            .Select("Profile.Name")
            .WhereEquals("Id", userId)
            .Limit(1)
            .Build();

        var result = await dataService.QueryAsync(query, cancellationToken);
        if (result.Records.Count == 0)
        {
            return false;
        }

        var record = result.Records[0];

        var actualProfileName = record["Profile"] is JsonObject profile
            ? profile["Name"]?.ToString()
            : record["Profile.Name"]?.ToString();

        return string.Equals(actualProfileName, profileName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckCustomPermissionAsync(
        IDataService dataService,
        string userId,
        string customPermission,
        CancellationToken cancellationToken)
    {
        var setupEntityIdSubquery = SoqlBuilder.From("CustomPermission")
            .Select("Id")
            .WhereEquals("DeveloperName", customPermission);

        var parentIdSubquery = SoqlBuilder.From("PermissionSetAssignment")
            .Select("PermissionSetId")
            .WhereEquals("AssigneeId", userId);

        var query = SoqlBuilder.From("SetupEntityAccess")
            .Select("Id")
            .WhereInSubquery("SetupEntityId", setupEntityIdSubquery)
            .WhereInSubquery("ParentId", parentIdSubquery)
            .Limit(1)
            .Build();

        var result = await dataService.QueryAsync(query, cancellationToken);
        return result.Records.Count > 0;
    }

    private static async Task<bool> CheckObjectPermissionAsync(
        IDataService dataService,
        string userId,
        string objectApiName,
        ObjectAccessLevel accessLevel,
        CancellationToken cancellationToken)
    {
        var parentIdSubquery = SoqlBuilder.From("PermissionSetAssignment")
            .Select("PermissionSetId")
            .WhereEquals("AssigneeId", userId);

        var permissionCondition = accessLevel switch
        {
            ObjectAccessLevel.Full => SoqlCondition.And(
                SoqlCondition.Equals("PermissionsRead", true),
                SoqlCondition.Equals("PermissionsCreate", true),
                SoqlCondition.Equals("PermissionsEdit", true),
                SoqlCondition.Equals("PermissionsDelete", true)),
            ObjectAccessLevel.Create => SoqlCondition.Equals("PermissionsCreate", true),
            ObjectAccessLevel.Edit => SoqlCondition.Equals("PermissionsEdit", true),
            ObjectAccessLevel.Delete => SoqlCondition.Equals("PermissionsDelete", true),
            _ => SoqlCondition.Equals("PermissionsRead", true)
        };

        var query = SoqlBuilder.From("ObjectPermissions")
            .Select("Id")
            .WhereEquals("SobjectType", objectApiName)
            .WhereCondition(permissionCondition)
            .WhereInSubquery("ParentId", parentIdSubquery)
            .Limit(1)
            .Build();

        var result = await dataService.QueryAsync(query, cancellationToken);
        return result.Records.Count > 0;
    }

    private static string? GetCurrentUserId(AuthorizationFilterContext context)
    {
        // Try to get from claims
        var userIdClaim = context.HttpContext.User.FindFirst("sub")
            ?? context.HttpContext.User.FindFirst("user_id")
            ?? context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

        return NormalizeSalesforceId(userIdClaim?.Value);
    }

    private static string? NormalizeSalesforceId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (SecurityUtils.IsValidSalesforceId(trimmed))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            foreach (var segment in uri.Segments.Reverse())
            {
                var candidate = segment.Trim('/');
                if (SecurityUtils.IsValidSalesforceId(candidate))
                {
                    return candidate;
                }
            }
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (SecurityUtils.IsValidSalesforceId(parts[i]))
            {
                return parts[i];
            }
        }

        return trimmed;
    }

    private void SetForbidResult(AuthorizationFilterContext context, string message)
    {
        // Check if this is an API request
        var acceptHeader = context.HttpContext.Request.Headers["Accept"].ToString();
        var isApiRequest = acceptHeader.Contains("application/json") ||
                          context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (isApiRequest)
        {
            context.Result = new JsonResult(new { error = message })
            {
                StatusCode = 403
            };
        }
        else
        {
            // For web requests, return forbid result
            context.Result = new ForbidResult();
        }
    }
}
