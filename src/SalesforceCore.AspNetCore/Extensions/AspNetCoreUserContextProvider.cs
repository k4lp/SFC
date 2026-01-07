using Microsoft.AspNetCore.Http;
using SalesforceCore.Services.Authorization;
using System.Security.Claims;

namespace SalesforceCore.AspNetCore.Extensions;

/// <summary>
/// Implementation of IUserContextProvider using IHttpContextAccessor.
/// </summary>
public class AspNetCoreUserContextProvider : IUserContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AspNetCoreUserContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal? GetUser()
    {
        return _httpContextAccessor.HttpContext?.User;
    }
}
