using System.Globalization;
using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Utilities;

/// <summary>
/// Utilities for converting field values between form input and Salesforce API formats.
/// </summary>
public static class FieldTypeConverter
{
    /// <summary>
    /// Converts a form value to the appropriate type for Salesforce API.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <param name="value">Raw form value.</param>
    /// <returns>Converted value for API submission.</returns>
    public static object? ConvertToApiValue(SObjectField field, string? value)
    {
        // Handle special case for encrypted strings - don't overwrite with null or empty
        if (field.Type.Equals("encryptedstring", StringComparison.OrdinalIgnoreCase))
        {
            return DBNull.Value; // Signal to exclude from payload
        }

        // Treat whitespace as null for most fields
        if (string.IsNullOrWhiteSpace(value))
        {
            // For boolean, empty usually means false (unchecked checkbox)
            if (field.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // For reference fields, return null to clear
            if (field.IsLookup)
            {
                return null;
            }

            // For nullable fields, return null
            if (field.Nillable)
            {
                return null;
            }

            // If not nillable but empty, we return null and let Salesforce throw the validation error,
            // or we return string.Empty if it's a string type.
            return field.Type.ToLowerInvariant() switch
            {
                "string" or "textarea" or "phone" or "email" or "url" or "picklist" => string.Empty,
                _ => null
            };
        }

        var fieldType = field.Type.ToLowerInvariant();

        return fieldType switch
        {
            "boolean" => ConvertToBoolean(value),
            "date" => ConvertToDate(value),
            "datetime" => ConvertToDateTime(value),
            "time" => ConvertToTime(value),
            "int" or "integer" => ConvertToInteger(value),
            "long" => ConvertToLong(value),
            "double" => ConvertToDouble(value),
            "currency" => ConvertToDecimal(value),
            "percent" => ConvertToDouble(value), // Salesforce expects 50.0 for 50%, not 0.5 usually, unless strictly configured. Keeping as double.
            "multipicklist" => ConvertToMultiPicklist(value),
            "reference" or "id" => ConvertToId(value),
            _ => value
        };
    }

    /// <summary>
    /// Converts a Salesforce API value to display format.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <param name="value">API value.</param>
    /// <param name="format">Optional format string.</param>
    /// <returns>Display-formatted value.</returns>
    public static string FormatForDisplay(SObjectField field, object? value, string? format = null)
    {
        if (value == null)
        {
            return string.Empty;
        }

        var fieldType = field.Type.ToLowerInvariant();
        var stringValue = value.ToString() ?? string.Empty;

        return fieldType switch
        {
            "boolean" => FormatBoolean(stringValue),
            "date" => FormatDate(stringValue, format ?? "yyyy-MM-dd"),
            "datetime" => FormatDateTime(stringValue, format ?? "yyyy-MM-dd HH:mm"),
            "time" => FormatTime(stringValue),
            "currency" => FormatCurrency(stringValue, field.Scale),
            "percent" => FormatPercent(stringValue, field.Scale),
            "double" or "decimal" => FormatNumber(stringValue, field.Scale),
            "multipicklist" => FormatMultiPicklist(stringValue),
            "url" => FormatUrl(stringValue),
            "email" => FormatEmail(stringValue),
            "phone" => FormatPhone(stringValue),
            "textarea" => FormatTextArea(stringValue),
            _ => stringValue
        };
    }

    /// <summary>
    /// Converts a form value to input value for edit forms.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <param name="value">API value.</param>
    /// <returns>Value for form input.</returns>
    public static string ConvertToInputValue(SObjectField field, object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        var fieldType = field.Type.ToLowerInvariant();
        return fieldType switch
        {
            "boolean" => value is bool b ? (b ? "true" : "false") : (value.ToString() ?? string.Empty).ToLowerInvariant(),
            "date" => value switch
            {
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => ParseDateForInput(value.ToString() ?? string.Empty)
            },
            "datetime" => value switch
            {
                DateTimeOffset dto => FormatDateTimeForInput(dto.UtcDateTime),
                DateTime dt => FormatDateTimeForInput(dt),
                _ => ParseDateTimeForInput(value.ToString() ?? string.Empty)
            },
            "time" => value switch
            {
                TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
                TimeSpan ts => ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                _ => ParseTimeForInput(value.ToString() ?? string.Empty)
            },
            "currency" or "double" or "decimal" or "percent" => value is IFormattable f
                ? f.ToString(null, CultureInfo.InvariantCulture)
                : ParseNumberForInput(value.ToString() ?? string.Empty),
            _ => value.ToString() ?? string.Empty
        };
    }

    #region Conversion Methods

    private static bool ConvertToBoolean(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ConvertToDate(string value)
    {
        // ISO 8601 strict parsing for dates
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string? ConvertToDateTime(string value)
    {
        // ISO 8601 strict parsing for datetimes, avoid AssumeLocal which uses server timezone
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToString("yyyy-MM-ddTHH:mm:ss.000'Z'", CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string? ConvertToTime(string value)
    {
        if (TimeSpan.TryParse(value, out var time))
        {
            return time.ToString(@"hh\:mm\:ss\.000\Z", CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static int? ConvertToInteger(string value)
    {
        if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static long? ConvertToLong(string value)
    {
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static double? ConvertToDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static decimal? ConvertToDecimal(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static string ConvertToMultiPicklist(string value)
    {
        // Multi-picklist values are separated by semicolons
        // Form may send as comma-separated or semicolon-separated
        return value.Replace(",", ";").Trim(';');
    }

    private static string? ConvertToId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return SecurityUtils.IsValidSalesforceId(value) ? value : null;
    }

    #endregion

    #region Formatting Methods

    private static string FormatBoolean(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
    }

    private static string FormatDate(string value, string format)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date.ToString(format, CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string FormatDateTime(string value, string format)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            // Do NOT convert to local time here as it uses server timezone.
            // Keep as UTC or allow client to handle timezone.
            // Returning in UTC for now to ensure consistency.
            return date.ToString(format, CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string FormatTime(string value)
    {
        if (TimeSpan.TryParse(value, out var time))
        {
            return time.ToString(@"hh\:mm");
        }
        return value;
    }

    private static string FormatCurrency(string value, int scale)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return amount.ToString($"C{scale}");
        }
        return value;
    }

    private static string FormatPercent(string value, int scale)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
        {
            return (percent / 100).ToString($"P{scale}");
        }
        return value;
    }

    private static string FormatNumber(string value, int scale)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString($"N{scale}");
        }
        return value;
    }

    private static string FormatMultiPicklist(string value)
    {
        return value.Replace(";", ", ");
    }

    private static string FormatUrl(string value)
    {
        return value;
    }

    private static string FormatEmail(string value)
    {
        return value;
    }

    private static string FormatPhone(string value)
    {
        return value;
    }

    private static string FormatTextArea(string value)
    {
        // Preserve line breaks
        return value;
    }

    #endregion

    #region Input Parsing Methods

    private static string ParseDateForInput(string value)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
        {
            return dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var currentCultureDate))
        {
            return currentCultureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static string FormatDateTimeForInput(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => dateTime
        };

        // Keep strictly ISO 8601 / UTC to avoid server timezone dependency
        return utc.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }

    private static string ParseDateTimeForInput(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            return FormatDateTimeForInput(date);
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var currentCultureDate))
        {
            return FormatDateTimeForInput(currentCultureDate);
        }

        return value;
    }

    private static string ParseTimeForInput(string value)
    {
        if (TimeSpan.TryParse(value, out var time))
        {
            return time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
        return value;
    }

    private static string ParseNumberForInput(string value)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return value;
    }

    #endregion

    /// <summary>
    /// Gets the HTML input type for a Salesforce field type.
    /// </summary>
    /// <param name="fieldType">Salesforce field type.</param>
    /// <returns>HTML input type.</returns>
    public static string GetHtmlInputType(string fieldType)
    {
        return fieldType.ToLowerInvariant() switch
        {
            "boolean" => "checkbox",
            "date" => "date",
            "datetime" => "datetime-local",
            "time" => "time",
            "email" => "email",
            "phone" => "tel",
            "url" => "url",
            "int" or "integer" or "long" => "number",
            "double" or "currency" or "percent" or "decimal" => "number",
            "textarea" or "encryptedstring" => "textarea",
            "picklist" or "multipicklist" => "select",
            "reference" or "id" => "hidden",
            _ => "text"
        };
    }

    /// <summary>
    /// Gets additional HTML attributes for a field input.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <returns>Dictionary of HTML attributes.</returns>
    public static Dictionary<string, string> GetHtmlAttributes(SObjectField field)
    {
        var attrs = new Dictionary<string, string>();
        var fieldType = field.Type.ToLowerInvariant();

        // Required
        if (field.IsRequired)
        {
            attrs["required"] = "required";
        }

        // Max length
        if (field.Length > 0 && (fieldType == "string" || fieldType == "textarea"))
        {
            attrs["maxlength"] = field.Length.ToString();
        }

        // Number attributes
        if (fieldType is "int" or "integer" or "long")
        {
            attrs["step"] = "1";
        }
        else if (fieldType is "double" or "decimal" or "currency" or "percent")
        {
            var step = field.Scale > 0 ? Math.Pow(10, -field.Scale).ToString(CultureInfo.InvariantCulture) : "any";
            attrs["step"] = step;
        }

        // Read-only
        if (field.IsReadOnly)
        {
            attrs["readonly"] = "readonly";
        }

        return attrs;
    }
}
