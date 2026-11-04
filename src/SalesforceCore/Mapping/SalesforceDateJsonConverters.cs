using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Mapping;

/// <summary>
/// dateTime/date/time JSON converters matching the Salesforce REST wire format (see <see cref="SalesforceWireFormat"/>).
/// Writing: <c>2024-01-02T03:04:05.000Z</c>, <c>2024-01-02</c>, <c>03:04:05.000Z</c>. Reading additionally accepts
/// <c>+0000</c> offsets as REST returns them (<c>2024-01-02T03:04:05.000+0000</c>), which System.Text.Json's built-in
/// DateTime parser rejects; 2.5.0 and earlier failed to bind such values on typed responses.
/// </summary>
public static class SalesforceDateJsonConverters
{
    /// <summary>All converters, for <see cref="JsonSerializerOptions.Converters"/>.</summary>
    public static IEnumerable<JsonConverter> All => new JsonConverter[]
    {
        new DateTimeConverter(), new DateTimeOffsetConverter(), new DateOnlyConverter(), new TimeOnlyConverter()
    };

    private sealed class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => SalesforceWireFormat.TryParseDateTime(reader.GetString(), out var v) ? v.UtcDateTime
               : throw new JsonException($"'{reader.GetString()}' is not a Salesforce dateTime.");
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(SalesforceWireFormat.FormatDateTime(value));
    }

    private sealed class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => SalesforceWireFormat.TryParseDateTime(reader.GetString(), out var v) ? v
               : throw new JsonException($"'{reader.GetString()}' is not a Salesforce dateTime.");
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(SalesforceWireFormat.FormatDateTime(value));
    }

    private sealed class DateOnlyConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return d;
            return SalesforceWireFormat.TryParseDateTime(s, out var dt) ? DateOnly.FromDateTime(dt.UtcDateTime)
                : throw new JsonException($"'{s}' is not a Salesforce date.");
        }
        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
            => writer.WriteStringValue(SalesforceWireFormat.FormatDate(value));
    }

    private sealed class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => SalesforceWireFormat.TryParseTime(reader.GetString(), out var t) ? t
               : throw new JsonException($"'{reader.GetString()}' is not a Salesforce time.");
        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
            => writer.WriteStringValue(SalesforceWireFormat.FormatTime(value));
    }
}
