using System.Text.Json;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Models.Errors;

/// <summary>
/// Maps Salesforce API failures (HTTP status + Salesforce error codes) to the typed
/// <see cref="SalesforceException"/> hierarchy. Every exception produced here carries the actual HTTP status,
/// all Salesforce errors, the (truncated) raw body and the Salesforce request id.
/// </summary>
/// <remarks>
/// The mapping is deliberately data-driven (<see cref="ValidationCodes"/>, <see cref="QueryCodes"/>, ...) so callers can
/// reason about it and tests can enumerate it. Unknown codes fall back to <see cref="SalesforceException"/> (4xx) or
/// <see cref="SalesforceServerException"/> (5xx).
/// </remarks>
public static class SalesforceErrorMapper
{
    /// <summary>Codes mapped to <see cref="SalesforceAuthException"/>.</summary>
    public static readonly IReadOnlySet<string> AuthCodes = Set("INVALID_SESSION_ID", "INVALID_AUTH_HEADER", "invalid_grant", "invalid_client", "invalid_token");

    /// <summary>Codes mapped to <see cref="SalesforceNotFoundException"/>.</summary>
    public static readonly IReadOnlySet<string> NotFoundCodes = Set("NOT_FOUND", "ENTITY_IS_DELETED", "INVALID_CROSS_REFERENCE_KEY");

    /// <summary>Codes mapped to <see cref="SalesforcePermissionException"/>.</summary>
    public static readonly IReadOnlySet<string> PermissionCodes = Set(
        "INSUFFICIENT_ACCESS", "INSUFFICIENT_ACCESS_OR_READONLY", "INSUFFICIENT_ACCESS_ON_CROSS_REFERENCE_ENTITY",
        "INSUFFICIENT_PRIVILEGES", "API_DISABLED_FOR_ORG", "API_CURRENTLY_DISABLED", "FUNCTIONALITY_NOT_ENABLED");

    /// <summary>Codes mapped to <see cref="SalesforceFieldSecurityException"/> (Salesforce-side field-level security).</summary>
    public static readonly IReadOnlySet<string> FieldSecurityCodes = Set("INVALID_FIELD_FOR_INSERT_UPDATE", "FIELD_INTEGRITY_EXCEPTION");

    /// <summary>Codes mapped to <see cref="SalesforceQueryException"/>.</summary>
    public static readonly IReadOnlySet<string> QueryCodes = Set(
        "MALFORMED_QUERY", "INVALID_FIELD", "INVALID_TYPE", "INVALID_QUERY_FILTER_OPERATOR", "INVALID_QUERY_LOCATOR",
        "QUERY_TIMEOUT", "MALFORMED_SEARCH", "INVALID_SEARCH", "EXCEEDED_ID_LIMIT", "QUERY_TOO_COMPLICATED");

    /// <summary>Codes mapped to <see cref="SalesforceValidationException"/>.</summary>
    public static readonly IReadOnlySet<string> ValidationCodes = Set(
        "FIELD_CUSTOM_VALIDATION_EXCEPTION", "REQUIRED_FIELD_MISSING", "STRING_TOO_LONG", "INVALID_EMAIL_ADDRESS",
        "FIELD_FILTER_VALIDATION_EXCEPTION", "INVALID_OR_NULL_FOR_RESTRICTED_PICKLIST", "NUMBER_OUTSIDE_VALID_RANGE",
        "INVALID_TYPE_ON_FIELD_IN_RECORD", "MALFORMED_ID", "INVALID_ID_FIELD", "JSON_PARSER_ERROR", "INVALID_FIELD_WHEN_USING_TEMPLATE",
        "DUPLICATE_VALUE", "CANNOT_INSERT_UPDATE_ACTIVATE_ENTITY", "INVALID_INPUT", "STORAGE_LIMIT_EXCEEDED");

    /// <summary>Codes mapped to <see cref="SalesforceRateLimitException"/>.</summary>
    public static readonly IReadOnlySet<string> RateLimitCodes = Set("REQUEST_LIMIT_EXCEEDED", "TXN_SECURITY_METERING_ERROR", "ConcurrentPerOrgLongTxn");

    /// <summary>Codes mapped to <see cref="SalesforceRecordLockedException"/>.</summary>
    public static readonly IReadOnlySet<string> LockCodes = Set("UNABLE_TO_LOCK_ROW", "ENTITY_IS_LOCKED");

    /// <summary>Codes mapped to <see cref="SalesforceDuplicateRuleException"/>.</summary>
    public static readonly IReadOnlySet<string> DuplicateCodes = Set("DUPLICATES_DETECTED");

    /// <summary>
    /// Builds the typed exception for an HTTP error response.
    /// </summary>
    /// <param name="status">HTTP status code.</param>
    /// <param name="body">Response body (JSON array/object or text).</param>
    /// <param name="requestId">Salesforce request id header, if any.</param>
    /// <param name="retryAfter">Parsed <c>Retry-After</c>, if any.</param>
    /// <param name="maxBodyLength">Raw body is truncated to this many characters (≤ 0 = no truncation).</param>
    public static SalesforceException FromResponse(int status, string? body, string? requestId = null, TimeSpan? retryAfter = null, int maxBodyLength = 2000)
    {
        var raw = Truncate(body, maxBodyLength);
        var errors = ParseErrors(body);
        SalesforceException ex;
        if (errors.Count > 0)
        {
            ex = Create(status, errors, retryAfter);
        }
        else
        {
            var message = string.IsNullOrWhiteSpace(raw) ? DefaultMessage(status) : raw!;
            ex = Create(status, new[] { new SalesforceError { Message = message, ErrorCode = string.Empty } }, retryAfter);
            errors = Array.Empty<SalesforceError>();
        }
        return ex.WithResponse(status, errors, raw, requestId);
    }

    /// <summary>
    /// Builds the typed exception for a list of Salesforce errors (e.g. a failed record inside a 200 composite/collection response).
    /// </summary>
    public static SalesforceException FromErrors(IEnumerable<SalesforceError> errors, int? statusCode = null)
    {
        var list = (errors ?? Enumerable.Empty<SalesforceError>()).ToList();
        if (list.Count == 0)
        {
            return new SalesforceException("Unknown Salesforce error", httpStatusCode: statusCode);
        }
        return Create(statusCode ?? 0, list, null).WithResponse(statusCode, list, null, null);
    }

    /// <summary>
    /// Parses Salesforce error bodies: <c>[{"message","errorCode","fields"}]</c>, a single error object, or an OAuth
    /// error (<c>{"error","error_description"}</c>). Returns an empty list for non-JSON bodies.
    /// </summary>
    public static IReadOnlyList<SalesforceError> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<SalesforceError>();
        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonArray arr)
            {
                return arr.OfType<JsonObject>().Select(FromNode).Where(e => e != null).Select(e => e!).ToList();
            }
            if (node is JsonObject obj)
            {
                if (obj["errors"] is JsonArray nested && nested.Count > 0)
                    return nested.OfType<JsonObject>().Select(FromNode).Where(e => e != null).Select(e => e!).ToList();
                var single = FromNode(obj);
                return single == null ? Array.Empty<SalesforceError>() : new[] { single };
            }
        }
        catch (JsonException)
        {
        }
        return Array.Empty<SalesforceError>();
    }

    private static SalesforceError? FromNode(JsonObject o)
    {
        var message = Str(o, "message") ?? Str(o, "error_description") ?? Str(o, "error");
        var code = Str(o, "errorCode") ?? Str(o, "statusCode") ?? Str(o, "error");
        if (message == null && code == null) return null;
        List<string>? fields = null;
        if (o["fields"] is JsonArray f)
            fields = f.Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        return new SalesforceError { Message = message ?? code ?? string.Empty, ErrorCode = code ?? string.Empty, Fields = fields };
    }

    private static string? Str(JsonObject o, string name)
        => o[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s) ? s : null;

    private static SalesforceException Create(int status, IReadOnlyList<SalesforceError> errors, TimeSpan? retryAfter)
    {
        var first = errors[0];
        var code = first.ErrorCode ?? string.Empty;
        var message = errors.Count == 1
            ? first.Message
            : string.Join("; ", errors.Select(e => string.IsNullOrEmpty(e.ErrorCode) ? e.Message : $"[{e.ErrorCode}] {e.Message}"));

        if (Has(DuplicateCodes, code))
            return new SalesforceDuplicateRuleException(message);
        if (Has(LockCodes, code))
            return new SalesforceRecordLockedException(message);
        if (status == 429 || Has(RateLimitCodes, code))
            return new SalesforceRateLimitException(message, retryAfter);
        if (Has(AuthCodes, code) || status == 401)
            return new SalesforceAuthException(message, tokenExpired: code == "INVALID_SESSION_ID");
        if (Has(PermissionCodes, code) || (status == 403 && code.Length == 0))
            return new SalesforcePermissionException(message);
        if (Has(FieldSecurityCodes, code))
            return new SalesforceFieldSecurityException(message, fieldName: first.Fields?.FirstOrDefault());
        if (Has(NotFoundCodes, code) || (status == 404 && code.Length == 0))
            return new SalesforceNotFoundException(message);
        if (Has(QueryCodes, code))
            return new SalesforceQueryException(message, errorCode: code);
        if (Has(ValidationCodes, code))
        {
            var byField = errors
                .SelectMany(e => (e.Fields is { Count: > 0 } ? e.Fields : new List<string> { string.Empty }).Select(f => (f, e.Message)))
                .GroupBy(x => x.f)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray());
            return new SalesforceValidationException(message, byField);
        }
        if (status >= 500)
            return new SalesforceServerException(message, code.Length == 0 ? null : code, status);
        if (status == 403)
            return new SalesforcePermissionException(message);
        if (status == 404)
            return new SalesforceNotFoundException(message);
        return new SalesforceException(message, code.Length == 0 ? null : code, status == 0 ? null : status);
    }

    private static string DefaultMessage(int status) => status switch
    {
        400 => "Invalid request",
        401 => "Session has expired or is invalid",
        403 => "Access denied by Salesforce",
        404 => "Resource not found",
        429 => "Salesforce request limit exceeded",
        >= 500 => "Salesforce server error",
        _ => $"HTTP {status}"
    };

    private static string? Truncate(string? s, int max)
        => s == null || max <= 0 || s.Length <= max ? s : s[..max] + "…";

    private static bool Has(IReadOnlySet<string> set, string code) => code.Length > 0 && set.Contains(code);

    private static IReadOnlySet<string> Set(params string[] codes) => new HashSet<string>(codes, StringComparer.Ordinal);
}
