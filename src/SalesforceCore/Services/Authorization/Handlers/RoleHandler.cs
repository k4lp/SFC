using System.Text.Json.Nodes;
using System.Security.Claims;
using System.Threading;

namespace SalesforceCore.Services.Authorization.Handlers;

/// <summary>
/// Handles 'Role' visibility requirements.
/// Checks if the user is in a specific .NET Identity Role.
/// Configuration: { "Role": "Admin" }
/// </summary>
public class RoleHandler : IVisibilityRequirementHandler
{
    public string Type => "Role";

    public Task<bool> HandleAsync(JsonObject settings, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var role = settings["Role"]?.GetValue<string>();
        if (string.IsNullOrEmpty(role))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(user.IsInRole(role));
    }
}
