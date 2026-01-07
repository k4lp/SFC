using System.Text.Json.Nodes;
using SalesforceCore.Models.Authorization;
using System.Security.Claims;
using System.Threading;

namespace SalesforceCore.Services.Authorization.Handlers;

/// <summary>
/// Handles 'SalesforcePermission' visibility requirements.
/// Checks object or field-level permissions via IPermissionService.
/// Configuration:
///   { "Object": "Account", "Action": "Create" }
///   OR
///   { "Object": "Account", "Field": "Amount", "Action": "Read" }
/// </summary>
public class SalesforcePermissionHandler : IVisibilityRequirementHandler
{
    private readonly IPermissionService _permissionService;

    public string Type => "SalesforcePermission";

    public SalesforcePermissionHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task<bool> HandleAsync(JsonObject settings, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var objectName = settings["Object"]?.GetValue<string>();
        var actionStr = settings["Action"]?.GetValue<string>();
        var fieldName = settings["Field"]?.GetValue<string>();

        if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(actionStr))
        {
            return false;
        }

        if (!Enum.TryParse<PermissionAction>(actionStr, true, out var action))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(fieldName))
        {
            // Field Check
            return await _permissionService.CanAccessFieldAsync(objectName, fieldName, action, cancellationToken);
        }
        else
        {
            // Object Check
            return await _permissionService.CanPerformActionAsync(objectName, action, cancellationToken);
        }
    }
}
