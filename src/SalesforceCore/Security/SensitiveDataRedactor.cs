using System.Text.RegularExpressions;

namespace SalesforceCore.Security;

/// <summary>
/// Helpers that keep secrets (access/refresh tokens, client secrets, passwords) out of logs and exception messages.
/// </summary>
public static partial class SensitiveDataRedactor
{
    /// <summary>Placeholder written in place of a redacted value.</summary>
    public const string Redacted = "[REDACTED]";

    [GeneratedRegex(@"(?i)(""?(?:access_token|refresh_token|id_token|client_secret|password|assertion|code_verifier|code|token)""?\s*[:=]\s*""?)([^""&\s,}]+)")]
    private static partial Regex SecretPairRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9!._\-~+/=]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"00D[A-Za-z0-9]{12,15}![A-Za-z0-9._\-]{20,}")]
    private static partial Regex SalesforceSessionIdRegex();

    /// <summary>
    /// Masks token-like values (JSON fields, form fields, Bearer headers, Salesforce session ids) in free text.
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = SecretPairRegex().Replace(text, m => m.Groups[1].Value + Redacted);
        result = BearerRegex().Replace(result, m => m.Groups[1].Value + Redacted);
        result = SalesforceSessionIdRegex().Replace(result, Redacted);
        return result;
    }

    /// <summary>Redacts and truncates a response body for safe inclusion in logs/exceptions.</summary>
    public static string ForDiagnostics(string? body, int maxLength = 2000)
    {
        var redacted = Redact(body);
        if (maxLength <= 0) return string.Empty;
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + "…[truncated]";
    }

    /// <summary>Returns a short non-reversible fingerprint suitable for correlating a token in logs.</summary>
    public static string Fingerprint(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "(none)";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        return "sha256:" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
