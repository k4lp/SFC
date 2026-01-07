using System.Security.Claims;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Abstraction for retrieving the current user context.
/// Allows core services to access the user principal without depending on HTTP context.
/// </summary>
public interface IUserContextProvider
{
    /// <summary>
    /// Gets the current user principal.
    /// </summary>
    /// <returns>The current user, or null if not authenticated.</returns>
    ClaimsPrincipal? GetUser();
}
