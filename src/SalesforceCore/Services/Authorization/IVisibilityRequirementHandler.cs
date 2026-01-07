using System.Text.Json.Nodes;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Defines a handler for a specific type of atomic visibility requirement.
/// </summary>
public interface IVisibilityRequirementHandler
{
    /// <summary>
    /// The unique type identifier for this requirement (e.g., "Role", "SalesforcePermission").
    /// This must match the 'Type' property in the configuration.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Evaluates the requirement against the provided settings and user context.
    /// </summary>
    /// <param name="settings">The specific configuration for this requirement instance.</param>
    /// <param name="user">The current user context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the requirement is met; otherwise, false.</returns>
    Task<bool> HandleAsync(JsonObject settings, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
