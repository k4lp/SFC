using SalesforceCore.Models.Data;

namespace SalesforceCore.Models.Errors;

/// <summary>
/// Base exception for all Salesforce-related errors.
/// </summary>
public class SalesforceException : Exception
{
    /// <summary>
    /// Salesforce error code.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// HTTP status code if from API response.
    /// </summary>
    public int? HttpStatusCode { get; }

    /// <summary>
    /// Fields related to the error.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; }

    /// <summary>
    /// Raw error response from Salesforce.
    /// </summary>
    public string? RawResponse { get; }

    /// <summary>
    /// Creates a new SalesforceException.
    /// </summary>
    public SalesforceException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new SalesforceException with error code.
    /// </summary>
    public SalesforceException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new SalesforceException with full details.
    /// </summary>
    public SalesforceException(
        string message,
        string? errorCode = null,
        int? httpStatusCode = null,
        IEnumerable<string>? fields = null,
        string? rawResponse = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
        Fields = fields?.ToList();
        RawResponse = rawResponse;
    }

    /// <summary>
    /// Creates a SalesforceException from a SalesforceError.
    /// </summary>
    public static SalesforceException FromError(SalesforceError error, int? statusCode = null)
    {
        return new SalesforceException(
            error.Message,
            error.ErrorCode,
            statusCode,
            error.Fields);
    }

    /// <summary>
    /// Creates a SalesforceException from multiple errors.
    /// </summary>
    public static SalesforceException FromErrors(IEnumerable<SalesforceError> errors, int? statusCode = null)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
        {
            return new SalesforceException("Unknown Salesforce error", httpStatusCode: statusCode);
        }

        if (errorList.Count == 1)
        {
            return FromError(errorList[0], statusCode);
        }

        var message = string.Join("; ", errorList.Select(e => $"[{e.ErrorCode}] {e.Message}"));
        var allFields = errorList.SelectMany(e => e.Fields ?? Enumerable.Empty<string>()).Distinct();

        return new SalesforceException(
            message,
            errorList[0].ErrorCode,
            statusCode,
            allFields);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var result = base.ToString();
        if (!string.IsNullOrEmpty(ErrorCode))
        {
            result = $"[{ErrorCode}] {result}";
        }
        return result;
    }
}

/// <summary>
/// Exception thrown when authentication fails or token is invalid.
/// </summary>
public class SalesforceAuthException : SalesforceException
{
    /// <summary>
    /// Whether the token has expired.
    /// </summary>
    public bool TokenExpired { get; }

    /// <summary>
    /// Whether re-authentication is required.
    /// </summary>
    public bool RequiresReauth { get; }

    /// <summary>
    /// Creates a new SalesforceAuthException.
    /// </summary>
    public SalesforceAuthException(string message, bool tokenExpired = false, bool requiresReauth = true)
        : base(message, "INVALID_SESSION_ID", 401)
    {
        TokenExpired = tokenExpired;
        RequiresReauth = requiresReauth;
    }

    /// <summary>
    /// Creates an exception for expired token.
    /// </summary>
    public static SalesforceAuthException TokenExpiredException()
    {
        return new SalesforceAuthException("Session has expired. Please log in again.", tokenExpired: true);
    }

    /// <summary>
    /// Creates an exception for missing token.
    /// </summary>
    public static SalesforceAuthException MissingTokenException()
    {
        return new SalesforceAuthException("No authentication token found. Please log in.");
    }

    /// <summary>
    /// Creates an exception for invalid token.
    /// </summary>
    public static SalesforceAuthException InvalidTokenException()
    {
        return new SalesforceAuthException("Authentication token is invalid. Please log in again.");
    }
}

/// <summary>
/// Exception thrown when validation fails.
/// </summary>
public class SalesforceValidationException : SalesforceException
{
    /// <summary>
    /// Validation errors by field.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    /// <summary>
    /// Creates a new SalesforceValidationException.
    /// </summary>
    public SalesforceValidationException(string message, Dictionary<string, string[]> validationErrors)
        : base(message, "VALIDATION_ERROR", 400)
    {
        ValidationErrors = validationErrors;
    }

    /// <summary>
    /// Creates a validation exception for a single field.
    /// </summary>
    public static SalesforceValidationException ForField(string field, string error)
    {
        return new SalesforceValidationException(
            error,
            new Dictionary<string, string[]> { { field, new[] { error } } });
    }

    /// <summary>
    /// Creates a validation exception for required fields.
    /// </summary>
    public static SalesforceValidationException RequiredFieldMissing(params string[] fields)
    {
        var errors = fields.ToDictionary(f => f, f => new[] { $"{f} is required" });
        return new SalesforceValidationException("Required fields are missing", errors);
    }
}

/// <summary>
/// Exception thrown when an object or record is not found.
/// </summary>
public class SalesforceNotFoundException : SalesforceException
{
    /// <summary>
    /// The object type that was not found.
    /// </summary>
    public string? ObjectType { get; }

    /// <summary>
    /// The record ID that was not found.
    /// </summary>
    public string? RecordId { get; }

    /// <summary>
    /// Creates a new SalesforceNotFoundException.
    /// </summary>
    public SalesforceNotFoundException(string message, string? objectType = null, string? recordId = null)
        : base(message, "NOT_FOUND", 404)
    {
        ObjectType = objectType;
        RecordId = recordId;
    }

    /// <summary>
    /// Creates an exception for a missing record.
    /// </summary>
    public static SalesforceNotFoundException RecordNotFound(string objectType, string recordId)
    {
        return new SalesforceNotFoundException(
            $"{objectType} record with ID '{recordId}' was not found.",
            objectType,
            recordId);
    }

    /// <summary>
    /// Creates an exception for a missing object type.
    /// </summary>
    public static SalesforceNotFoundException ObjectNotFound(string objectType)
    {
        return new SalesforceNotFoundException(
            $"Object type '{objectType}' does not exist or is not accessible.",
            objectType);
    }
}

/// <summary>
/// Exception thrown when the user lacks permission.
/// </summary>
public class SalesforcePermissionException : SalesforceException
{
    /// <summary>
    /// The operation that was denied.
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    /// The object or field that access was denied to.
    /// </summary>
    public string? Target { get; }

    /// <summary>
    /// Creates a new SalesforcePermissionException.
    /// </summary>
    public SalesforcePermissionException(string message, string? operation = null, string? target = null)
        : base(message, "INSUFFICIENT_ACCESS", 403)
    {
        Operation = operation;
        Target = target;
    }

    /// <summary>
    /// Creates an exception for object access denial.
    /// </summary>
    public static SalesforcePermissionException ObjectAccessDenied(string objectType, string operation)
    {
        return new SalesforcePermissionException(
            $"You do not have permission to {operation} {objectType} records.",
            operation,
            objectType);
    }

    /// <summary>
    /// Creates an exception for field access denial.
    /// </summary>
    public static SalesforcePermissionException FieldAccessDenied(string objectType, string fieldName)
    {
        return new SalesforcePermissionException(
            $"You do not have permission to access the {fieldName} field on {objectType}.",
            "read",
            $"{objectType}.{fieldName}");
    }
}

/// <summary>
/// Exception thrown when rate limits are exceeded.
/// </summary>
public class SalesforceRateLimitException : SalesforceException
{
    /// <summary>
    /// Time until rate limit resets.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates a new SalesforceRateLimitException.
    /// </summary>
    public SalesforceRateLimitException(string message, TimeSpan? retryAfter = null)
        : base(message, "REQUEST_LIMIT_EXCEEDED", 429)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Exception thrown when a duplicate rule blocks record creation/update.
/// </summary>
public class SalesforceDuplicateRuleException : SalesforceException
{
    /// <summary>
    /// The duplicate rule that blocked the operation.
    /// </summary>
    public string? DuplicateRuleName { get; }

    /// <summary>
    /// IDs of the duplicate records found.
    /// </summary>
    public IReadOnlyList<string> DuplicateRecordIds { get; }

    /// <summary>
    /// Information about matched duplicate records.
    /// </summary>
    public IReadOnlyList<DuplicateMatch> DuplicateMatches { get; }

    /// <summary>
    /// Creates a new SalesforceDuplicateRuleException.
    /// </summary>
    public SalesforceDuplicateRuleException(
        string message,
        string? duplicateRuleName = null,
        IEnumerable<string>? duplicateRecordIds = null,
        IEnumerable<DuplicateMatch>? duplicateMatches = null)
        : base(message, "DUPLICATES_DETECTED", 400)
    {
        DuplicateRuleName = duplicateRuleName;
        DuplicateRecordIds = duplicateRecordIds?.ToList() ?? new List<string>();
        DuplicateMatches = duplicateMatches?.ToList() ?? new List<DuplicateMatch>();
    }

    /// <summary>
    /// Creates a duplicate rule exception from Salesforce error details.
    /// </summary>
    public static SalesforceDuplicateRuleException FromDuplicateResult(
        string message,
        string? ruleName,
        IEnumerable<DuplicateMatch>? matches)
    {
        var matchList = matches?.ToList() ?? new List<DuplicateMatch>();
        var recordIds = matchList.SelectMany(m => m.MatchRecordIds).Distinct().ToList();

        return new SalesforceDuplicateRuleException(message, ruleName, recordIds, matchList);
    }
}

/// <summary>
/// Represents a duplicate match from Salesforce duplicate rules.
/// </summary>
public class DuplicateMatch
{
    /// <summary>
    /// The matching rule that detected the duplicate.
    /// </summary>
    public string? MatchRuleName { get; set; }

    /// <summary>
    /// The object type of the matched records.
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// IDs of records that matched as duplicates.
    /// </summary>
    public List<string> MatchRecordIds { get; set; } = new();

    /// <summary>
    /// Confidence score of the match (0-100).
    /// </summary>
    public int? MatchConfidence { get; set; }
}

/// <summary>
/// Exception thrown when a SOQL query is malformed or invalid.
/// </summary>
public class SalesforceQueryException : SalesforceException
{
    /// <summary>
    /// The SOQL query that caused the error.
    /// </summary>
    public string? Soql { get; }

    /// <summary>
    /// Creates a new SalesforceQueryException.
    /// </summary>
    public SalesforceQueryException(string message, string? soql = null, string? errorCode = null)
        : base(message, errorCode ?? "MALFORMED_QUERY", 400)
    {
        Soql = soql;
    }
}

/// <summary>
/// Exception thrown when field-level security prevents access.
/// </summary>
public class SalesforceFieldSecurityException : SalesforceException
{
    /// <summary>
    /// The field that access was denied to.
    /// </summary>
    public string? FieldName { get; }

    /// <summary>
    /// The object containing the field.
    /// </summary>
    public string? ObjectName { get; }

    /// <summary>
    /// Creates a new SalesforceFieldSecurityException.
    /// </summary>
    public SalesforceFieldSecurityException(string message, string? objectName = null, string? fieldName = null)
        : base(message, "FIELD_INTEGRITY_EXCEPTION", 400)
    {
        ObjectName = objectName;
        FieldName = fieldName;
    }
}

/// <summary>
/// Exception thrown when a record is locked (e.g., by approval process or another user).
/// </summary>
public class SalesforceRecordLockedException : SalesforceException
{
    /// <summary>
    /// The ID of the locked record.
    /// </summary>
    public string? RecordId { get; }

    /// <summary>
    /// Creates a new SalesforceRecordLockedException.
    /// </summary>
    public SalesforceRecordLockedException(string message, string? recordId = null)
        : base(message, "UNABLE_TO_LOCK_ROW", 400)
    {
        RecordId = recordId;
    }
}
