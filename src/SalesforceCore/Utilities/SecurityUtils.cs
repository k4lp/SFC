using System.Text.RegularExpressions;
using System.Web;

namespace SalesforceCore.Utilities;

/// <summary>
/// Security utilities for input sanitization and validation.
/// </summary>
public static class SecurityUtils
{
    /// <summary>
    /// Sanitizes input for use in SOQL queries.
    /// Escapes single quotes by doubling them.
    /// Also removes null bytes and control characters that could be used for injection bypass.
    /// </summary>
    /// <param name="input">Raw input string.</param>
    /// <returns>Sanitized string safe for SOQL.</returns>
    public static string SanitizeSoql(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Step 1: Remove null bytes (can be used to truncate strings or bypass WAF)
        var sanitized = input.Replace("\0", string.Empty);

        // Step 2: Normalize Unicode to prevent homograph attacks (e.g., using ʼ instead of ')
        // Use FormC to compose characters and catch decomposed sequences
        sanitized = sanitized.Normalize(System.Text.NormalizationForm.FormC);

        // Step 3: Replace any Unicode quote-like characters with standard single quote
        // These are sometimes used to bypass naive quote filtering
        sanitized = NormalizeQuoteLikeCharacters(sanitized);

        // Step 4: Remove control characters (except newline/tab which SOQL handles)
        // Control characters can sometimes cause parsing issues
        sanitized = RemoveControlCharacters(sanitized);

        // Step 5: Escape single quotes by doubling them (core SOQL injection prevention)
        return sanitized.Replace("'", "''");
    }

    /// <summary>
    /// Normalizes Unicode quote-like characters to standard single quotes.
    /// Prevents bypass attacks using homoglyphs.
    /// </summary>
    private static string NormalizeQuoteLikeCharacters(string input)
    {
        // Unicode quote-like characters that could bypass naive filtering:
        // U+02BC ʼ Modifier Letter Apostrophe
        // U+02B9 ʹ Modifier Letter Prime  
        // U+0027 ' Standard Apostrophe (keep as-is, will be escaped)
        // U+2019 ' Right Single Quotation Mark
        // U+2018 ' Left Single Quotation Mark
        // U+201B ‛ Single High-Reversed-9 Quotation Mark
        // U+FF07 ' Fullwidth Apostrophe
        // U+02CA ˊ Modifier Letter Acute Accent
        // U+0060 ` Grave Accent
        
        return input
            .Replace('\u02BC', '\'')  // Modifier Letter Apostrophe
            .Replace('\u02B9', '\'')  // Modifier Letter Prime
            .Replace('\u2019', '\'')  // Right Single Quotation Mark
            .Replace('\u2018', '\'')  // Left Single Quotation Mark
            .Replace('\u201B', '\'')  // Single High-Reversed-9 Quotation Mark
            .Replace('\uFF07', '\'')  // Fullwidth Apostrophe
            .Replace('\u02CA', '\''); // Modifier Letter Acute Accent
    }

    /// <summary>
    /// Removes control characters that could cause parsing issues.
    /// Preserves newlines, tabs, and carriage returns as these are valid in SOQL strings.
    /// </summary>
    private static string RemoveControlCharacters(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            // Allow standard whitespace: newline, carriage return, tab, space
            if (c == '\n' || c == '\r' || c == '\t' || c >= ' ')
            {
                // Also filter out DEL (0x7F) and C1 control characters (0x80-0x9F)
                if (c != '\u007F' && (c < '\u0080' || c > '\u009F'))
                {
                    sb.Append(c);
                }
            }
            // Skip C0 control characters (0x00-0x1F) except tab, newline, carriage return
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes input for use in SOQL LIKE clauses.
    /// Escapes special LIKE characters in addition to standard SOQL escaping.
    /// </summary>
    /// <param name="input">Raw input string.</param>
    /// <returns>Sanitized string safe for SOQL LIKE.</returns>
    public static string SanitizeSoqlLike(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sanitized = SanitizeSoql(input);

        // Escape the LIKE escape character itself first, then wildcards.
        return sanitized
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    /// <summary>
    /// Validates a SOQL query string for basic safety checks.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="error">Validation error when invalid.</param>
    /// <returns>True if the query passes basic validation.</returns>
    public static bool TryValidateSoqlQuery(string? soql, out string? error)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            error = "SOQL query is required.";
            return false;
        }

        var trimmed = soql.TrimStart();
        if (!trimmed.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            error = "SOQL query must start with SELECT.";
            return false;
        }

        var inString = false;

        for (var i = 0; i < soql.Length; i++)
        {
            var current = soql[i];

            if (current == '\'')
            {
                if (inString && i + 1 < soql.Length && soql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current == ';')
            {
                error = "SOQL query must not contain statement delimiters.";
                return false;
            }

            if (current == '-' && i + 1 < soql.Length && soql[i + 1] == '-')
            {
                error = "SOQL query must not contain line comments.";
                return false;
            }

            if (current == '/' && i + 1 < soql.Length && soql[i + 1] == '*')
            {
                error = "SOQL query must not contain block comments.";
                return false;
            }

            if (current == '*' && i + 1 < soql.Length && soql[i + 1] == '/')
            {
                error = "SOQL query must not contain block comments.";
                return false;
            }
        }

        if (inString)
        {
            error = "SOQL query contains an unterminated string literal.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// HTML encodes a string for safe display in HTML.
    /// </summary>
    /// <param name="input">Raw input string.</param>
    /// <returns>HTML encoded string.</returns>
    public static string HtmlEncode(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return HttpUtility.HtmlEncode(input);
    }

    /// <summary>
    /// Removes all HTML tags from a string.
    /// </summary>
    /// <param name="input">String with potential HTML.</param>
    /// <returns>String with HTML tags removed.</returns>
    public static string StripHtml(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return Regex.Replace(input, "<[^>]*>", string.Empty);
    }

    /// <summary>
    /// Validates that a URL is local (relative or same origin).
    /// Prevents open redirect vulnerabilities.
    /// </summary>
    /// <param name="url">URL to validate.</param>
    /// <returns>True if the URL is local.</returns>
    public static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // Local URLs:
        // - Start with / but not //
        // - Start with ~/ (ASP.NET virtual path)
        // - Do not start with a scheme (http://, https://, etc.)

        if (url.StartsWith("//"))
        {
            return false;
        }

        if (url.StartsWith("/") || url.StartsWith("~/"))
        {
            return true;
        }

        // Check for scheme
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Only allow http and https, and only if it's truly local
            return false;
        }

        // Relative URL without leading /
        return !url.Contains(":");
    }

    /// <summary>
    /// Validates a Salesforce ID format.
    /// </summary>
    /// <param name="id">Salesforce ID.</param>
    /// <returns>True if the ID is valid format.</returns>
    public static bool IsValidSalesforceId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        // Salesforce IDs are 15 or 18 characters, alphanumeric
        if (id.Length != 15 && id.Length != 18)
        {
            return false;
        }

        return Regex.IsMatch(id, "^[a-zA-Z0-9]+$");
    }

    /// <summary>
    /// Validates a Salesforce object API name.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <returns>True if the name is valid format.</returns>
    public static bool IsValidObjectName(string? objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return false;
        }

        if (objectName.Contains('.'))
        {
            return false;
        }

        return IsValidApiName(objectName, ObjectSuffixes);
    }

    /// <summary>
    /// Validates a field API name.
    /// </summary>
    /// <param name="fieldName">Field API name.</param>
    /// <returns>True if the name is valid format.</returns>
    public static bool IsValidFieldName(string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return false;
        }

        // Reject leading/trailing dots and consecutive dots
        if (fieldName.StartsWith('.') || fieldName.EndsWith('.') || fieldName.Contains(".."))
        {
            return false;
        }

        // Allow relationship notation (e.g., Account.Name, Account.Owner.Name)
        var parts = fieldName.Split('.');

        // Must have at least one part
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsValidApiName(part, FieldSuffixes))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] ObjectSuffixes =
    {
        "__c",
        "__mdt",
        "__x",
        "__e",
        "__b",
        "__ChangeEvent",
        "__History",
        "__Share",
        "__Feed",
        "__pc",
        "__pr"
    };

    private static readonly string[] FieldSuffixes =
    {
        "__c",
        "__r",
        "__mdt",
        "__x",
        "__e",
        "__b",
        "__ChangeEvent",
        "__History",
        "__Share",
        "__Feed",
        "__pc",
        "__pr"
    };

    private static bool IsValidApiName(string name, IReadOnlyCollection<string> allowedSuffixes)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var suffix = allowedSuffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal));
        var baseName = suffix != null ? name.Substring(0, name.Length - suffix.Length) : name;

        if (string.IsNullOrEmpty(baseName))
        {
            return false;
        }

        var parts = baseName.Split(new[] { "__" }, StringSplitOptions.None);
        if (parts.Length == 0 || parts.Length > 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || !Regex.IsMatch(part, "^[A-Za-z][A-Za-z0-9_]*$"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sanitizes a file name for safe storage.
    /// </summary>
    /// <param name="fileName">Original file name.</param>
    /// <returns>Sanitized file name.</returns>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "file";
        }

        // Remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());

        // Ensure not empty
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "file";
        }

        // Limit length
        if (sanitized.Length > 255)
        {
            var ext = Path.GetExtension(sanitized);
            var name = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = name.Substring(0, 255 - ext.Length) + ext;
        }

        return sanitized;
    }

    /// <summary>
    /// Validates content type against allowed types.
    /// </summary>
    /// <param name="contentType">Content type to validate.</param>
    /// <param name="allowedTypes">Allowed content types or patterns.</param>
    /// <returns>True if content type is allowed.</returns>
    public static bool IsAllowedContentType(string? contentType, IEnumerable<string> allowedTypes)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        var normalizedType = contentType.ToLowerInvariant().Split(';')[0].Trim();

        foreach (var allowed in allowedTypes)
        {
            var normalizedAllowed = allowed.ToLowerInvariant().Trim();

            // Exact match
            if (normalizedType == normalizedAllowed)
            {
                return true;
            }

            // Wildcard match (e.g., "image/*")
            if (normalizedAllowed.EndsWith("/*"))
            {
                var prefix = normalizedAllowed.Substring(0, normalizedAllowed.Length - 1);
                if (normalizedType.StartsWith(prefix))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Validates file extension against allowed extensions.
    /// </summary>
    /// <param name="fileName">File name with extension.</param>
    /// <param name="allowedExtensions">Allowed extensions (with or without dot).</param>
    /// <returns>True if extension is allowed.</returns>
    public static bool IsAllowedExtension(string? fileName, IEnumerable<string> allowedExtensions)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        // Normalize extensions to include dot
        var normalizedAllowed = allowedExtensions
            .Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : "." + e.ToLowerInvariant());

        return normalizedAllowed.Contains(ext);
    }

    /// <summary>
    /// Sanitizes input for use in SOQL queries.
    /// Alias for SanitizeSoql for API consistency.
    /// </summary>
    /// <param name="input">Raw input string.</param>
    /// <returns>Sanitized string safe for SOQL.</returns>
    public static string SanitizeForSoql(string? input)
    {
        return SanitizeSoql(input);
    }

    /// <summary>
    /// Sanitizes and validates an object name for use in queries.
    /// Throws if the name is invalid.
    /// </summary>
    /// <param name="name">The object name to sanitize.</param>
    /// <returns>The validated object name.</returns>
    /// <exception cref="ArgumentException">Thrown if the name is invalid.</exception>
    public static string SanitizeObjectName(string? name)
    {
        if (!IsValidObjectName(name))
        {
            throw new ArgumentException($"Invalid object name: {name}", nameof(name));
        }
        return name!;
    }

    /// <summary>
    /// Sanitizes and validates a field name for use in queries.
    /// Throws if the name is invalid.
    /// </summary>
    /// <param name="name">The field name to sanitize.</param>
    /// <returns>The validated field name.</returns>
    /// <exception cref="ArgumentException">Thrown if the name is invalid.</exception>
    public static string SanitizeFieldName(string? name)
    {
        if (!IsValidFieldName(name))
        {
            throw new ArgumentException($"Invalid field name: {name}", nameof(name));
        }
        return name!;
    }

    /// <summary>
    /// Validates and returns a Salesforce ID.
    /// </summary>
    /// <param name="id">The ID to validate.</param>
    /// <returns>The validated ID.</returns>
    /// <exception cref="ArgumentException">Thrown if the ID is invalid.</exception>
    public static string ValidateId(string? id)
    {
        if (!IsValidSalesforceId(id))
        {
            throw new ArgumentException($"Invalid Salesforce ID: {id}", nameof(id));
        }
        return id!;
    }

    /// <summary>
    /// Sanitizes a list of field names, returning only valid ones.
    /// </summary>
    /// <param name="fields">The fields to sanitize.</param>
    /// <returns>Enumerable of valid field names.</returns>
    public static IEnumerable<string> SanitizeFieldList(IEnumerable<string>? fields)
    {
        if (fields == null)
            yield break;

        foreach (var field in fields)
        {
            if (IsValidFieldName(field))
                yield return field;
        }
    }

    #region Advanced Injection Protection

    /// <summary>
    /// Detects potential SOQL injection patterns in user input.
    /// Use this for logging/alerting, not as primary defense (use sanitization instead).
    /// </summary>
    /// <param name="input">User input to check.</param>
    /// <returns>True if suspicious patterns are detected.</returns>
    public static bool ContainsSuspiciousPatterns(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        var lower = input.ToLowerInvariant();

        // SQL/SOQL keywords that shouldn't appear in typical user input
        var suspiciousPatterns = new[]
        {
            "' or ",
            "' and ",
            "'; --",
            "' --",
            "1=1",
            "1'='1",
            "select ",
            " from ",
            " where ",
            " union ",
            " delete ",
            " update ",
            " insert ",
            " drop ",
            "/*",
            "*/",
            "\\x00",  // Null byte in hex
            "%00",    // URL-encoded null byte
        };

        foreach (var pattern in suspiciousPatterns)
        {
            if (lower.Contains(pattern))
                return true;
        }

        // Check for excessive quote usage (potential injection attempt)
        var quoteCount = input.Count(c => c == '\'');
        if (quoteCount > 5)
            return true;

        // Check for Unicode quote homoglyphs
        var unicodeQuotes = new[] { '\u02BC', '\u02B9', '\u2019', '\u2018', '\u201B', '\uFF07' };
        if (input.Any(c => unicodeQuotes.Contains(c)))
            return true;

        return false;
    }

    /// <summary>
    /// Validates that a value is safe for use as a numeric parameter.
    /// Prevents injection via non-string parameters.
    /// </summary>
    /// <param name="value">Value to validate.</param>
    /// <param name="sanitized">Sanitized numeric string if valid.</param>
    /// <returns>True if the value is a valid number.</returns>
    public static bool TryValidateNumeric(object? value, out string sanitized)
    {
        sanitized = string.Empty;

        if (value == null)
            return false;

        // Handle various numeric types
        if (value is int i) { sanitized = i.ToString(); return true; }
        if (value is long l) { sanitized = l.ToString(); return true; }
        if (value is decimal d) { sanitized = d.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
        if (value is double dbl) { sanitized = dbl.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
        if (value is float f) { sanitized = f.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }

        // String that should be numeric
        if (value is string str)
        {
            // Remove any whitespace
            str = str.Trim();

            // Try parsing as decimal (most flexible numeric type)
            if (decimal.TryParse(str, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out var result))
            {
                sanitized = result.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates that a value is safe for use as a boolean parameter.
    /// Prevents injection via boolean coercion attacks.
    /// </summary>
    /// <param name="value">Value to validate.</param>
    /// <param name="sanitized">Sanitized boolean string (TRUE or FALSE).</param>
    /// <returns>True if the value is a valid boolean.</returns>
    public static bool TryValidateBoolean(object? value, out string sanitized)
    {
        sanitized = string.Empty;

        if (value == null)
            return false;

        if (value is bool b)
        {
            sanitized = b ? "TRUE" : "FALSE";
            return true;
        }

        if (value is string str)
        {
            str = str.Trim().ToLowerInvariant();
            if (str == "true" || str == "1" || str == "yes")
            {
                sanitized = "TRUE";
                return true;
            }
            if (str == "false" || str == "0" || str == "no")
            {
                sanitized = "FALSE";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates and sanitizes a date/datetime value for SOQL.
    /// </summary>
    /// <param name="value">Date value to validate.</param>
    /// <param name="sanitized">Sanitized date string in SOQL format.</param>
    /// <returns>True if the value is a valid date.</returns>
    public static bool TryValidateDateTime(object? value, out string sanitized)
    {
        sanitized = string.Empty;

        if (value == null)
            return false;

        DateTimeOffset dto;

        if (value is DateTime dt)
            dto = new DateTimeOffset(dt);
        else if (value is DateTimeOffset d)
            dto = d;
        else if (value is DateOnly dateOnly)
        {
            // DateOnly -> Date format for SOQL
            sanitized = dateOnly.ToString("yyyy-MM-dd");
            return true;
        }
        else if (value is string str)
        {
            if (DateTimeOffset.TryParse(str, out dto))
            {
                // Continue to format below
            }
            else if (DateOnly.TryParse(str, out var parsedDate))
            {
                sanitized = parsedDate.ToString("yyyy-MM-dd");
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        // Format as ISO 8601 for SOQL
        sanitized = dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return true;
    }

    /// <summary>
    /// Maximum allowed length for string values in SOQL queries.
    /// Prevents buffer overflow attacks and DoS via extremely long strings.
    /// </summary>
    public const int MaxStringValueLength = 4000;

    /// <summary>
    /// Validates string length to prevent DoS attacks via extremely long input.
    /// </summary>
    /// <param name="input">Input string to validate.</param>
    /// <param name="maxLength">Maximum allowed length (default: 4000).</param>
    /// <returns>True if the string length is acceptable.</returns>
    public static bool ValidateStringLength(string? input, int maxLength = MaxStringValueLength)
    {
        if (string.IsNullOrEmpty(input))
            return true;

        return input.Length <= maxLength;
    }

    /// <summary>
    /// Sanitizes a string value with length validation.
    /// Throws if the input exceeds maximum length.
    /// </summary>
    /// <param name="input">Input string to sanitize.</param>
    /// <param name="maxLength">Maximum allowed length.</param>
    /// <returns>Sanitized string.</returns>
    /// <exception cref="ArgumentException">Thrown if input exceeds max length.</exception>
    public static string SanitizeSoqlWithLengthCheck(string? input, int maxLength = MaxStringValueLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (input.Length > maxLength)
        {
            throw new ArgumentException(
                $"Input exceeds maximum allowed length of {maxLength} characters.", 
                nameof(input));
        }

        return SanitizeSoql(input);
    }

    /// <summary>
    /// Creates a safe, parameterized-style value for SOQL.
    /// This is the recommended method for building dynamic SOQL values.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A safely formatted SOQL value string (including quotes if needed).</returns>
    public static string FormatSoqlValue(object? value)
    {
        if (value == null)
            return "NULL";

        // Boolean
        if (TryValidateBoolean(value, out var boolVal))
            return boolVal;

        // Numeric (no quotes needed)
        if (TryValidateNumeric(value, out var numVal))
            return numVal;

        // Date/DateTime
        if (TryValidateDateTime(value, out var dateVal))
            return dateVal;

        // Salesforce ID (validate format, no quotes needed for binding but may need for SOQL)
        if (value is string strVal && IsValidSalesforceId(strVal))
            return $"'{strVal}'";

        // Default: treat as string
        var stringValue = value.ToString() ?? string.Empty;
        var sanitized = SanitizeSoqlWithLengthCheck(stringValue);
        return $"'{sanitized}'";
    }

    #endregion
}
