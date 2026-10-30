using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using SalesforceCore.Services.Authorization;

namespace SalesforceCore.Services.Caching;

/// <summary>
/// Computes a stable, non-reversible cache-key suffix for the current user so caches whose contents depend on
/// the caller (describe flags, sharing-filtered query results) are never served across users.
/// </summary>
public static class UserCachePartition
{
    /// <summary>Returns "_u" + 16 hex chars for an authenticated user with an id claim; otherwise an empty string.</summary>
    public static string For(IUserContextProvider? userContextProvider)
    {
        var user = userContextProvider?.GetUser();
        if (user?.Identity?.IsAuthenticated != true)
        {
            return string.Empty;
        }

        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        return "_u" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 8);
    }
}
