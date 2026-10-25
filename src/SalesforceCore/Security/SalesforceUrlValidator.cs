namespace SalesforceCore.Security;

/// <summary>
/// Validates Salesforce instance and login URLs so that access tokens are never sent to
/// non-Salesforce hosts (e.g. a tampered <c>instance_url</c> in a token response or cache entry).
/// </summary>
public static class SalesforceUrlValidator
{
    /// <summary>
    /// Host suffixes operated by Salesforce (commercial, government, China/Alibaba, sandboxes, Experience Cloud).
    /// </summary>
    public static IReadOnlyList<string> DefaultAllowedHostSuffixes { get; } = new[]
    {
        ".salesforce.com",
        ".force.com",
        ".salesforce.mil",
        ".cloudforce.com",
        ".cloudforce.mil",
        ".database.com",
        ".salesforce-setup.com",
        ".salesforce-sites.com",
        ".sfcrmapps.cn",
        ".sfcrmproducts.cn",
        ".force.mil",
    };

    /// <summary>
    /// Returns true if <paramref name="url"/> is an absolute HTTPS URL (no user-info) whose host is a
    /// Salesforce host or matches one of <paramref name="additionalAllowedHosts"/>.
    /// Additional entries may be exact hosts (<c>my.proxy.example</c>) or suffixes starting with a dot (<c>.example.com</c>).
    /// </summary>
    public static bool IsAllowed(string? url, IEnumerable<string>? additionalAllowedHosts = null)
        => TryValidate(url, additionalAllowedHosts, out _);

    /// <summary>
    /// Validates the URL and returns a reason when it is rejected.
    /// </summary>
    public static bool TryValidate(string? url, IEnumerable<string>? additionalAllowedHosts, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            reason = "URL is not an absolute URI.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            reason = "URL must use HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "URL must not contain user info.";
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();

        if (additionalAllowedHosts != null)
        {
            foreach (var raw in additionalAllowedHosts)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var entry = raw.Trim().TrimEnd('.').ToLowerInvariant();
                if (entry.StartsWith('.') ? host.EndsWith(entry, StringComparison.Ordinal) && host.Length > entry.Length
                                          : host == entry)
                {
                    return true;
                }
            }
        }

        foreach (var suffix in DefaultAllowedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.Ordinal) && host.Length > suffix.Length)
            {
                return true;
            }
        }

        reason = $"Host '{host}' is not a recognised Salesforce host. Add it to SalesforceOptions.AdditionalAllowedHosts if it is legitimate.";
        return false;
    }

    /// <summary>
    /// Throws <see cref="SalesforceCore.Models.Errors.SalesforceAuthException"/> when the URL is not allowed.
    /// </summary>
    public static string EnsureAllowed(string? url, IEnumerable<string>? additionalAllowedHosts, string what = "instance URL")
    {
        if (!TryValidate(url, additionalAllowedHosts, out var reason))
        {
            throw new SalesforceCore.Models.Errors.SalesforceAuthException($"Rejected Salesforce {what}: {reason}");
        }
        return url!.Trim();
    }
}
