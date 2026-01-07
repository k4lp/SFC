using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Utilities;

internal static class SalesforceDateTimeParser
{
    internal static bool TryParseDateTime(string? value, out DateTime dateTime)
    {
        dateTime = default;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = NormalizeOffset(value);

        if (DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var dto))
        {
            dateTime = dto.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            dateTime = dt;
            return true;
        }

        return false;
    }

    internal static bool TryParseDateTimeOffset(string? value, out DateTimeOffset dateTimeOffset)
    {
        dateTimeOffset = default;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = NormalizeOffset(value);

        if (DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var dto))
        {
            dateTimeOffset = dto;
            return true;
        }

        if (DateTime.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            dateTimeOffset = new DateTimeOffset(dt, TimeSpan.Zero);
            return true;
        }

        return false;
    }

    private static string NormalizeOffset(string value)
    {
        if (value.Length < 5)
        {
            return value;
        }

        var offset = value.AsSpan(value.Length - 5);
        if ((offset[0] == '+' || offset[0] == '-')
            && char.IsDigit(offset[1])
            && char.IsDigit(offset[2])
            && char.IsDigit(offset[3])
            && char.IsDigit(offset[4]))
        {
            return value[..^2] + ":" + value[^2..];
        }

        return value;
    }
}

/// <summary>
/// Parses Salesforce DateTime strings with timezone offsets like +0000.
/// </summary>
public sealed class SalesforceDateTimeConverter : JsonConverter<DateTime>
{
    /// <inheritdoc />
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (SalesforceDateTimeParser.TryParseDateTime(value, out var dateTime))
            {
                return dateTime;
            }

            throw new JsonException($"Invalid Salesforce DateTime value: '{value}'.");
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("Unexpected null for non-nullable DateTime.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing DateTime.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Parses nullable Salesforce DateTime strings with timezone offsets like +0000.
/// </summary>
public sealed class SalesforceNullableDateTimeConverter : JsonConverter<DateTime?>
{
    /// <inheritdoc />
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (SalesforceDateTimeParser.TryParseDateTime(value, out var dateTime))
            {
                return dateTime;
            }

            throw new JsonException($"Invalid Salesforce DateTime value: '{value}'.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing DateTime.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteNullValue();
    }
}
