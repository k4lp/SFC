using System.Security.Claims;
using System.Threading;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Default user context provider for non-ASP.NET hosts. Uses Thread.CurrentPrincipal.
/// </summary>
public class DefaultUserContextProvider : IUserContextProvider
{
    public ClaimsPrincipal? GetUser()
    {
        var principal = Thread.CurrentPrincipal;
        if (principal is ClaimsPrincipal claimsPrincipal)
        {
            return claimsPrincipal;
        }

        return principal != null ? new ClaimsPrincipal(principal) : null;
    }
}
