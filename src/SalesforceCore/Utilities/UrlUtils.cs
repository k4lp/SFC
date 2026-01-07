namespace SalesforceCore.Utilities;

/// <summary>
/// Utility for handling URL encoding consistently across the library.
/// Standardizes on RFC 3986 (Uri.EscapeDataString) over HttpUtility.UrlEncode.
/// </summary>
public static class UrlUtils
{
    /// <summary>
    /// Escapes a string for use in a URL query parameter or path segment.
    /// Uses Uri.EscapeDataString which encodes spaces as %20 (RFC 3986).
    /// </summary>
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return Uri.EscapeDataString(value);
    }
}
