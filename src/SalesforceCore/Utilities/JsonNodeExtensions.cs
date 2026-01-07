using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SalesforceCore.Utilities;

/// <summary>
/// Extension methods for safe value extraction from <see cref="JsonNode"/>.
/// Provides robust parsing for Salesforce API response values, handling
/// edge cases that <see cref="JsonNode.GetValue{T}"/> cannot handle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Background:</b> <c>JsonNode.GetValue&lt;DateTime&gt;()</c> throws an
/// <see cref="InvalidOperationException"/> when the underlying JSON value is
/// a string (such as ISO 8601 date strings returned by Salesforce).
/// </para>
/// <para>
/// <b>Solution:</b> This class provides extension methods that first convert
/// the JsonNode to a string and then parse with proper culture and format handling.
/// </para>
/// </remarks>
public static class JsonNodeExtensions
{
    /// <summary>
    /// Safely parses a <see cref="DateTime"/> from a <see cref="JsonNode"/>.
    /// Handles ISO 8601 strings with timezone offsets as returned by Salesforce API.
    /// </summary>
    /// <param name="node">The JSON node to parse. Can be null.</param>
    /// <returns>
    /// A <see cref="DateTime"/> in UTC if parsing succeeds; <c>null</c> otherwise.
    /// </returns>
    /// <example>
    /// <code>
    /// // Salesforce returns dates like: "2025-12-20T10:30:00.000+0000"
    /// var createdDate = record["CreatedDate"].ParseDateTime();
    /// </code>
    /// </example>
    public static DateTime? ParseDateTime(this JsonNode? node)
    {
        if (node == null)
            return null;

        var str = node.ToString();
        return ParseDateTimeFromString(str);
    }

    /// <summary>
    /// Safely parses a <see cref="DateTime"/> from a <see cref="JsonNode"/>,
    /// returning a default value if parsing fails or the node is null.
    /// </summary>
    /// <param name="node">The JSON node to parse. Can be null.</param>
    /// <param name="defaultValue">The value to return if parsing fails. Defaults to <see cref="DateTime.MinValue"/>.</param>
    /// <returns>
    /// A <see cref="DateTime"/> in UTC if parsing succeeds; the <paramref name="defaultValue"/> otherwise.
    /// </returns>
    /// <example>
    /// <code>
    /// // Use DateTime.MinValue as default
    /// var createdDate = doc["CreatedDate"].ParseDateTimeOrDefault(DateTime.MinValue);
    /// 
    /// // Use a custom default
    /// var modifiedDate = doc["LastModifiedDate"].ParseDateTimeOrDefault(DateTime.UtcNow);
    /// </code>
    /// </example>
    public static DateTime ParseDateTimeOrDefault(this JsonNode? node, DateTime defaultValue = default)
    {
        return node.ParseDateTime() ?? defaultValue;
    }

    /// <summary>
    /// Safely parses a <see cref="DateTimeOffset"/> from a <see cref="JsonNode"/>.
    /// Handles ISO 8601 strings with timezone offsets as returned by Salesforce API.
    /// </summary>
    /// <param name="node">The JSON node to parse. Can be null.</param>
    /// <returns>
    /// A <see cref="DateTimeOffset"/> if parsing succeeds; <c>null</c> otherwise.
    /// </returns>
    public static DateTimeOffset? ParseDateTimeOffset(this JsonNode? node)
    {
        if (node == null)
            return null;

        var str = node.ToString();
        return ParseDateTimeOffsetFromString(str);
    }

    /// <summary>
    /// Safely parses a <see cref="DateTimeOffset"/> from a <see cref="JsonNode"/>,
    /// returning a default value if parsing fails or the node is null.
    /// </summary>
    /// <param name="node">The JSON node to parse. Can be null.</param>
    /// <param name="defaultValue">The value to return if parsing fails.</param>
    /// <returns>
    /// A <see cref="DateTimeOffset"/> if parsing succeeds; the <paramref name="defaultValue"/> otherwise.
    /// </returns>
    public static DateTimeOffset ParseDateTimeOffsetOrDefault(this JsonNode? node, DateTimeOffset defaultValue = default)
    {
        return node.ParseDateTimeOffset() ?? defaultValue;
    }

    /// <summary>
    /// Safely gets a value from a <see cref="JsonNode"/> with proper type handling.
    /// This method handles cases where <c>GetValue&lt;T&gt;()</c> would fail.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="node">The JSON node to extract value from.</param>
    /// <param name="defaultValue">Default value if extraction fails.</param>
    /// <returns>The extracted value or default.</returns>
    public static T GetValueSafe<T>(this JsonNode? node, T defaultValue = default!)
    {
        if (node == null)
            return defaultValue;

        try
        {
            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Handle DateTime specially - GetValue<DateTime>() doesn't parse strings
            if (underlyingType == typeof(DateTime))
            {
                var dt = ParseDateTimeFromString(node.ToString());
                return dt.HasValue ? (T)(object)dt.Value : defaultValue;
            }

            // Handle DateTimeOffset specially
            if (underlyingType == typeof(DateTimeOffset))
            {
                var dto = ParseDateTimeOffsetFromString(node.ToString());
                return dto.HasValue ? (T)(object)dto.Value : defaultValue;
            }

            // Handle DateOnly
            if (underlyingType == typeof(DateOnly))
            {
                var str = node.ToString();
                if (DateOnly.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
                    return (T)(object)dateOnly;
                // Fallback: try parsing as DateTime and extract date
                var dt = ParseDateTimeFromString(str);
                if (dt.HasValue)
                    return (T)(object)DateOnly.FromDateTime(dt.Value);
                return defaultValue;
            }

            // Handle TimeOnly
            if (underlyingType == typeof(TimeOnly))
            {
                var str = node.ToString();
                if (TimeOnly.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
                    return (T)(object)timeOnly;
                // Fallback: try parsing as DateTime and extract time
                var dt = ParseDateTimeFromString(str);
                if (dt.HasValue)
                    return (T)(object)TimeOnly.FromDateTime(dt.Value);
                return defaultValue;
            }

            // For string type, just use ToString()
            if (underlyingType == typeof(string))
            {
                return (T)(object)node.ToString();
            }

            // For other types, try the standard GetValue<T>()
            return node.GetValue<T>();
        }
        catch
        {
            return defaultValue;
        }
    }

    #region Private Helpers

    /// <summary>
    /// Parses a DateTime from a string value, handling Salesforce date formats.
    /// </summary>
    private static DateTime? ParseDateTimeFromString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (SalesforceDateTimeParser.TryParseDateTime(str, out var dt))
        {
            return dt;
        }

        return null;
    }

    /// <summary>
    /// Parses a DateTimeOffset from a string value, handling Salesforce date formats.
    /// </summary>
    private static DateTimeOffset? ParseDateTimeOffsetFromString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (SalesforceDateTimeParser.TryParseDateTimeOffset(str, out var dto))
        {
            return dto;
        }

        return null;
    }

    #endregion
}
