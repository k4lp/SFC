namespace SalesforceCore.Security;

/// <summary>
/// 2.7.0: one chokepoint that keeps every instance-relative request path (REST, Apex, Tooling, Analytics, and
/// composite sub-request URLs) inside the route the caller asked for.
/// </summary>
/// <remarks>
/// <para>A value such as a record ID, report ID or external ID that reaches a URL path can carry <c>..</c>,
/// <c>%2e%2e</c>, a backslash or a control character. Servers and proxies normalise those, so
/// <c>/sobjects/Account/../../apexrest/Admin</c> would run a different endpoint with the caller's token (confused deputy).
/// The guard rejects such paths before anything is sent. It never decides <em>whether</em> a caller may call a route:
/// Salesforce enforces access.</para>
/// <para>Only the path part is checked; the query string (for example an encoded SOQL statement) is left alone.</para>
/// </remarks>
public static class SalesforcePathGuard
{
    /// <summary>Returns false (with a reason) when the path would escape its route or carries unsafe characters.</summary>
    public static bool TryValidate(string? path, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(path))
        {
            reason = "path is empty";
            return false;
        }

        foreach (var c in path)
        {
            if (char.IsControl(c))
            {
                reason = "path contains a control character (CR, LF, NUL...)";
                return false;
            }
        }

        var q = path.IndexOfAny(new[] { '?', '#' });
        var pathPart = q >= 0 ? path[..q] : path;

        if (pathPart.Contains('\\'))
        {
            reason = "path contains a backslash";
            return false;
        }

        // Decode percent-encoded dots and slashes so %2e%2e and %2f.. are caught too (decoded twice for double encoding).
        var decoded = pathPart;
        for (var i = 0; i < 2 && decoded.Contains('%'); i++)
        {
            try { decoded = Uri.UnescapeDataString(decoded); }
            catch (UriFormatException) { break; }
        }

        if (decoded.Contains('\\') || decoded.Any(char.IsControl))
        {
            reason = "path contains an encoded backslash or control character";
            return false;
        }

        // A literal scheme ("https://") in the unencoded path could make the URL absolute. An encoded one
        // ("https%3A%2F%2F...") cannot: it stays inside its segment, which is how legitimate external ID values such
        // as product URLs travel (unreleased: the decoded form used to be checked, rejecting those values).
        if (pathPart.StartsWith("//", StringComparison.Ordinal) || pathPart.Contains("://", StringComparison.Ordinal)
            || decoded.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "path must be instance-relative";
            return false;
        }

        foreach (var segment in decoded.Split('/'))
        {
            var s = segment.Trim();
            if (s == "." || s == "..")
            {
                reason = "path contains a '.' or '..' segment";
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws <see cref="ArgumentException"/> when <see cref="TryValidate"/> fails; returns the path unchanged.</summary>
    public static string Ensure(string? path, string paramName = "path")
    {
        if (!TryValidate(path, out var reason))
        {
            throw new ArgumentException($"Unsafe Salesforce request path rejected: {reason}.", paramName);
        }
        return path!;
    }

    /// <summary>
    /// Escapes one value for use as a single path segment (external ID values, names). Unlike
    /// <see cref="Uri.EscapeDataString(string)"/> alone, it also rejects the <c>.</c> and <c>..</c> segments that
    /// escaping leaves untouched.
    /// </summary>
    public static string Segment(string? value, string paramName = "value")
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Path segment value is required.", paramName);
        }
        if (value.Trim() is "." or "..")
        {
            throw new ArgumentException("Path segment value must not be '.' or '..'.", paramName);
        }
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Path segment value must not contain control characters.", paramName);
        }
        return Uri.EscapeDataString(value);
    }
}
