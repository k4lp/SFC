using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Mapping;

/// <summary>
/// Serialises enums the way Salesforce expects picklist values: the <see cref="Attributes.SalesforceValueAttribute"/>
/// value, or the member name. Reads strings (value or name, case-insensitive) and numbers.
/// </summary>
/// <remarks>
/// Used for request bodies and typed responses of <c>SalesforceClient</c>. Before 2.5 an enum inside a record
/// dictionary (<c>["StageName"] = Stage.ClosedWon</c>) was written as its ordinal (<c>1</c>), which an unrestricted
/// picklist silently stored as "1", while LINQ filters (2.2.1) already sent <c>'Closed Won'</c>. Enum types with
/// their own <see cref="JsonConverterAttribute"/> keep it; to send a number for one property, put
/// <c>[JsonConverter(typeof(JsonNumberEnumConverter&lt;T&gt;))]</c> on that property.
/// </remarks>
public sealed class SalesforceEnumJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsEnum && typeToConvert.GetCustomAttribute<JsonConverterAttribute>() == null;

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(EnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class EnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return (T)Enum.ToObject(typeof(T), reader.GetInt64());
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Cannot convert {reader.TokenType} to enum {typeof(T).Name}.");
            }

            var raw = reader.GetString()!;
            try
            {
                return (T)SalesforceMapper.ParseEnumValue(raw, typeof(T));
            }
            catch (ArgumentException ex)
            {
                throw new JsonException($"'{raw}' is not a value of enum {typeof(T).Name}.", ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(SalesforceMapper.GetEnumSalesforceValue(value));
    }
}
