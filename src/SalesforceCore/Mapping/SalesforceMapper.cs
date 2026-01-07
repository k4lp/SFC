using System.Collections.Concurrent;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SalesforceCore.Attributes;

namespace SalesforceCore.Mapping;

/// <summary>
/// Provides mapping between C# objects and Salesforce API representations.
/// Uses attributes for field mapping and supports both strongly-typed and dynamic objects.
/// </summary>
public static class SalesforceMapper
{
    /// <summary>
    /// Thread-safe metadata cache. Note: GetOrAdd factory may execute multiple times 
    /// concurrently for the same key, but only one result is stored. This is safe
    /// because CreateMetadata produces independent, identical objects.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ObjectMetadata> _metadataCache = new();
    private static readonly ConcurrentDictionary<Type, EnumMap> _enumMapCache = new();

    /// <summary>
    /// When true, only properties with [SalesforceField] attribute are included in SOQL queries.
    /// Set via Configure() method during startup.
    /// </summary>
    private static bool _requireSalesforceFieldAttribute = false;

    /// <summary>
    /// Configures the SalesforceMapper behavior.
    /// Call this during application startup to change default mapping behavior.
    /// </summary>
    /// <param name="requireSalesforceFieldAttribute">
    /// When true, properties without [SalesforceField] attribute are automatically ignored in SOQL queries.
    /// This prevents "No such column" errors when models have computed or non-Salesforce properties.
    /// </param>
    public static void Configure(bool requireSalesforceFieldAttribute)
    {
        _requireSalesforceFieldAttribute = requireSalesforceFieldAttribute;
        ClearCache(); // Clear cached metadata so new settings take effect
    }

    /// <summary>
    /// Gets the Salesforce object name for a type.
    /// Uses SalesforceObject attribute if present, otherwise uses the type name.
    /// </summary>
    public static string GetObjectName<T>() => GetObjectName(typeof(T));

    /// <summary>
    /// Gets the Salesforce object name for a type.
    /// </summary>
    public static string GetObjectName(Type type)
    {
        var metadata = GetOrCreateMetadata(type);
        return metadata.ObjectName;
    }

    /// <summary>
    /// Converts a C# object to a Salesforce-compatible dictionary.
    /// </summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="obj">The object to convert.</param>
    /// <param name="includeReadOnly">Whether to include read-only fields.</param>
    /// <param name="forCreate">Whether this is for a create operation (excludes non-createable fields).</param>
    /// <param name="forUpdate">Whether this is for an update operation (excludes non-updateable fields).</param>
    public static Dictionary<string, object?> ToSalesforceDictionary<T>(
        T obj,
        bool includeReadOnly = false,
        bool forCreate = false,
        bool forUpdate = false) where T : class
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        var metadata = GetOrCreateMetadata(typeof(T));
        var result = new Dictionary<string, object?>();

        foreach (var mapping in metadata.PropertyMappings.Values)
        {
            // Skip ignored properties
            if (mapping.IsIgnored) continue;

            // Child relationship properties are not writable Salesforce fields and must never be sent in payloads.
            if (mapping.IsChildRelationship) continue;

            // Skip read-only unless explicitly requested
            if (mapping.IsReadOnly && !includeReadOnly) continue;

            // Skip non-createable for create operations
            if (forCreate && !mapping.IsCreateable) continue;

            // Skip non-updateable for update operations
            if (forUpdate && !mapping.IsUpdateable) continue;

            // Skip Id for create operations
            if (forCreate && mapping.IsId) continue;

            var value = mapping.Property.GetValue(obj);

            // Convert value to Salesforce format
            var salesforceValue = ConvertToSalesforceValue(value, mapping);

            // Only include non-null values (or null for explicit clearing)
            if (salesforceValue != null || forUpdate)
            {
                result[mapping.SalesforceFieldName] = salesforceValue;
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a Salesforce JsonNode to a strongly-typed C# object.
    /// Supports dot notation for nested relationship fields (e.g., "Lead.FirstName" maps to property LeadFirstName).
    /// </summary>
    public static T FromSalesforce<T>(JsonNode salesforceRecord) where T : class, new()
    {
        if (salesforceRecord == null) throw new ArgumentNullException(nameof(salesforceRecord));

        var metadata = GetOrCreateMetadata(typeof(T));
        var result = new T();

        foreach (var mapping in metadata.PropertyMappings.Values)
        {
            if (mapping.IsIgnored) continue;

            JsonNode? token;

            // Support dot notation for nested/relationship fields
            // e.g., "Lead.FirstName" will traverse salesforceRecord["Lead"]["FirstName"]
            if (mapping.SalesforceFieldName.Contains('.'))
            {
                token = GetNestedToken(salesforceRecord, mapping.SalesforceFieldName);
            }
            else
            {
                token = salesforceRecord[mapping.SalesforceFieldName];
            }

            if (token == null) continue;

            object? value;
            if (mapping.IsChildRelationship)
            {
                value = ConvertFromChildRelationshipValue(token, mapping.Property.PropertyType);
            }
            else
            {
                value = ConvertFromSalesforceValue(token, mapping.Property.PropertyType);
            }
            if (value != null)
            {
                mapping.Property.SetValue(result, value);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets a nested token from a JsonNode using dot notation path.
    /// For example, "Lead.FirstName" will return salesforceRecord["Lead"]["FirstName"].
    /// </summary>
    private static JsonNode? GetNestedToken(JsonNode root, string path)
    {
        var parts = path.Split('.');
        JsonNode? current = root;

        foreach (var part in parts)
        {
            if (current == null)
                return null;

            if (current is JsonObject obj)
            {
                current = obj[part];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Converts a list of Salesforce JsonNodes to strongly-typed C# objects.
    /// </summary>
    public static List<T> FromSalesforce<T>(IEnumerable<JsonNode> salesforceRecords) where T : class, new()
    {
        return salesforceRecords.Select(FromSalesforce<T>).ToList();
    }

    /// <summary>
    /// Gets the Salesforce field name for a property.
    /// </summary>
    public static string GetFieldName<T>(string propertyName)
    {
        return GetFieldName(typeof(T), propertyName);
    }

    /// <summary>
    /// Gets the Salesforce field name for a property on a specific type.
    /// </summary>
    public static string GetFieldName(Type entityType, string propertyName)
    {
        var metadata = GetOrCreateMetadata(entityType);
        return metadata.PropertyMappings.TryGetValue(propertyName, out var mapping)
            ? mapping.SalesforceFieldName
            : propertyName;
    }

    /// <summary>
    /// Gets all queryable field names for a type.
    /// </summary>
    public static IEnumerable<string> GetQueryableFields<T>()
    {
        var metadata = GetOrCreateMetadata(typeof(T));
        return metadata.PropertyMappings.Values
            .Where(m => !m.IsIgnored && !m.IsChildRelationship)
            .Select(m => m.SalesforceFieldName);
    }

    /// <summary>
    /// Gets all queryable field names for a type (non-generic overload).
    /// </summary>
    public static IEnumerable<string> GetQueryableFields(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        var metadata = GetOrCreateMetadata(type);
        return metadata.PropertyMappings.Values
            .Where(m => !m.IsIgnored && !m.IsChildRelationship)
            .Select(m => m.SalesforceFieldName);
    }

    /// <summary>
    /// Gets the Id field name for a type.
    /// </summary>
    public static string? GetIdFieldName<T>()
    {
        var metadata = GetOrCreateMetadata(typeof(T));
        var idMapping = metadata.PropertyMappings.Values.FirstOrDefault(m => m.IsId);
        return idMapping?.SalesforceFieldName;
    }

    /// <summary>
    /// Gets the external Id field name for a type.
    /// </summary>
    public static string? GetExternalIdFieldName<T>()
    {
        var metadata = GetOrCreateMetadata(typeof(T));
        var externalIdMapping = metadata.PropertyMappings.Values.FirstOrDefault(m => m.IsExternalId);
        return externalIdMapping?.SalesforceFieldName;
    }

    /// <summary>
    /// Clears the metadata cache (useful for testing).
    /// Also resets configuration to defaults.
    /// </summary>
    /// <param name="resetConfiguration">If true, also resets RequireSalesforceFieldAttribute to false.</param>
    public static void ClearCache(bool resetConfiguration = false)
    {
        _metadataCache.Clear();
        _enumMapCache.Clear();
        if (resetConfiguration)
        {
            _requireSalesforceFieldAttribute = false;
        }
    }

    #region Private Methods

    private static ObjectMetadata GetOrCreateMetadata(Type type)
    {
        return _metadataCache.GetOrAdd(type, CreateMetadata);
    }

    private static ObjectMetadata CreateMetadata(Type type)
    {
        var objectAttr = type.GetCustomAttribute<SalesforceObjectAttribute>();
        var objectName = objectAttr?.ObjectName ?? type.Name;

        var metadata = new ObjectMetadata
        {
            Type = type,
            ObjectName = objectName,
            IsQueryable = objectAttr?.Queryable ?? true,
            IsCreateable = objectAttr?.Createable ?? true,
            IsUpdateable = objectAttr?.Updateable ?? true,
            IsDeletable = objectAttr?.Deletable ?? true
        };

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Skip properties without public getter
            if (property.GetMethod == null || !property.GetMethod.IsPublic) continue;

            var mapping = CreatePropertyMapping(property);
            metadata.PropertyMappings[property.Name] = mapping;
        }

        // Note: Computed properties (getter-only without setter) are auto-ignored
        // via CreatePropertyMapping's IsComputedProperty check

        return metadata;
    }

    private static PropertyMapping CreatePropertyMapping(PropertyInfo property)
    {
        var fieldAttr = property.GetCustomAttribute<SalesforceFieldAttribute>();
        var ignoreAttr = property.GetCustomAttribute<SalesforceIgnoreAttribute>();
        var idAttr = property.GetCustomAttribute<SalesforceIdAttribute>();
        var externalIdAttr = property.GetCustomAttribute<SalesforceExternalIdAttribute>();
        var lookupAttr = property.GetCustomAttribute<SalesforceLookupAttribute>();
        var childRelAttr = property.GetCustomAttribute<SalesforceChildRelationshipAttribute>();

        // Determine Salesforce field name
        var salesforceFieldName = childRelAttr?.RelationshipName ?? fieldAttr?.FieldName ?? property.Name;

        // Check if this is the Id field (by name or attribute)
        var isId = idAttr != null ||
                   property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                   salesforceFieldName.Equals("Id", StringComparison.OrdinalIgnoreCase);

        // Check if this is a computed property (getter-only without setter)
        // Computed properties should be automatically ignored for SOQL queries
        // as they don't correspond to Salesforce fields
        var isComputedProperty = property.SetMethod == null || !property.SetMethod.IsPublic;

        // Check if property has explicit Salesforce mapping
        var hasExplicitMapping = fieldAttr != null || childRelAttr != null || lookupAttr != null;
        
        // When RequireSalesforceFieldAttribute is enabled, unmapped properties (except Id) are ignored
        var isUnmappedInStrictMode = _requireSalesforceFieldAttribute 
            && !hasExplicitMapping 
            && !isId 
            && externalIdAttr == null;

        // Auto-ignore computed properties unless they have an explicit SalesforceField attribute
        // Also ignore unmapped properties when strict mode is enabled
        var shouldIgnore = ignoreAttr != null ||
                          (isComputedProperty && fieldAttr == null) ||
                          isUnmappedInStrictMode;

        return new PropertyMapping
        {
            Property = property,
            SalesforceFieldName = salesforceFieldName,
            IsIgnored = shouldIgnore,
            IsComputed = isComputedProperty,
            IsReadOnly = fieldAttr?.ReadOnly ?? false,
            IsCreateable = fieldAttr?.Createable ?? true,
            IsUpdateable = fieldAttr?.Updateable ?? true,
            IsRequired = fieldAttr?.Required ?? false,
            IsId = isId,
            IsExternalId = externalIdAttr != null,
            IsLookup = lookupAttr != null,
            IsChildRelationship = childRelAttr != null,
            ChildObject = childRelAttr?.ChildObject,
            ChildRelationshipName = childRelAttr?.RelationshipName,
            ForeignKeyField = childRelAttr?.ForeignKeyField,
            ReferenceTo = lookupAttr?.ReferenceTo ?? fieldAttr?.ReferenceTo,
            RelationshipName = lookupAttr?.RelationshipName ?? fieldAttr?.RelationshipName,
            MaxLength = fieldAttr?.MaxLength ?? 0,
            Precision = fieldAttr?.Precision ?? 0,
            Scale = fieldAttr?.Scale ?? 0
        };
    }

    private static object? ConvertToSalesforceValue(object? value, PropertyMapping mapping)
    {
        if (value == null) return null;

        return value switch
        {
            DateTime dt => FormatDateTimeUtc(dt),
            DateTimeOffset dto => FormatDateTimeOffsetUtc(dto),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            bool b => b,
            Enum e => GetEnumSalesforceValue(e),
            decimal d => d,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s => s,
            _ => value.ToString()
        };
    }

    private static string FormatDateTimeUtc(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => dateTime
        };

        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatDateTimeOffsetUtc(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static object? ConvertFromChildRelationshipValue(JsonNode token, Type targetType)
    {
        if (token is not JsonObject relationshipResult)
        {
            return null;
        }

        // Salesforce subquery JSON shape:
        // { "totalSize": n, "done": true/false, "records": [ { ... }, { ... } ] }
        if (relationshipResult["records"] is not JsonArray recordsArray)
        {
            return null;
        }

        var elementType = GetCollectionElementType(targetType);
        if (elementType == null)
        {
            return null;
        }

        // Build List<TElement> via reflection; map each record using the existing generic mapper.
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;

        var fromSalesforce = typeof(SalesforceMapper)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == nameof(FromSalesforce) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(JsonNode));
        if (fromSalesforce == null)
        {
            return null;
        }

        MethodInfo closedFromSalesforce;
        try
        {
            closedFromSalesforce = fromSalesforce.MakeGenericMethod(elementType);
        }
        catch
        {
            // Element type does not satisfy mapper constraints (class, new()).
            return null;
        }

        foreach (var recordNode in recordsArray)
        {
            if (recordNode == null) continue;

            var child = closedFromSalesforce.Invoke(null, new object[] { recordNode });
            if (child != null)
            {
                list.Add(child);
            }
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        if (targetType.IsAssignableFrom(listType))
        {
            return list;
        }

        // Best-effort conversion for collection types with a ctor(IEnumerable<T>).
        try
        {
            var enumerableOfElement = typeof(IEnumerable<>).MakeGenericType(elementType);
            var ctor = targetType.GetConstructor(new[] { enumerableOfElement });
            if (ctor != null)
            {
                return ctor.Invoke(new object[] { list });
            }
        }
        catch
        {
            // Ignore and fall back to returning List<T>.
        }

        return list;
    }

    private static Type? GetCollectionElementType(Type targetType)
    {
        if (targetType == typeof(string))
        {
            return null;
        }

        if (targetType.IsArray)
        {
            return targetType.GetElementType();
        }

        if (targetType.IsGenericType)
        {
            var args = targetType.GetGenericArguments();
            if (args.Length == 1)
            {
                return args[0];
            }
        }

        var enumerableInterface = targetType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0];
    }

    private static object? ConvertFromSalesforceValue(JsonNode token, Type targetType)
    {
        if (token == null)
            return null;

        // Handle nullable types
        var nullableUnderlyingType = Nullable.GetUnderlyingType(targetType);
        var isNullable = nullableUnderlyingType != null;
        var underlyingType = nullableUnderlyingType ?? targetType;

        try
        {
            if (underlyingType == typeof(string))
                return token.ToString();

            if (underlyingType == typeof(bool))
                return token.GetValue<bool>();

            if (underlyingType == typeof(int))
                return token.GetValue<int>();

            if (underlyingType == typeof(long))
                return token.GetValue<long>();

            if (underlyingType == typeof(decimal))
            {
                if (token is JsonValue val)
                {
                    if (val.TryGetValue<decimal>(out var d)) return d;
                    if (val.TryGetValue<double>(out var db)) return (decimal)db;
                    if (val.TryGetValue<long>(out var l)) return (decimal)l;
                    if (val.TryGetValue<int>(out var i)) return (decimal)i;
                }

                return decimal.Parse(token.ToString(), CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(double))
                return token.GetValue<double>();

            if (underlyingType == typeof(float))
                return token.GetValue<float>();

            if (underlyingType == typeof(DateTime))
            {
                var str = token.ToString();
                if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return isNullable ? null : throw new InvalidOperationException("Salesforce returned an empty DateTime value for a non-nullable target type.");
                }

                // Prefer DateTimeOffset parsing to correctly handle offsets, then normalize to UTC DateTime.
                if (DateTimeOffset.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                        out var dto))
                {
                    return dto.UtcDateTime;
                }

                if (DateTime.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return dt;
                }

                return isNullable ? null : throw new InvalidOperationException($"Unable to parse Salesforce DateTime value '{str}'.");
            }

            if (underlyingType == typeof(DateTimeOffset))
            {
                var str = token.ToString();
                if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return isNullable ? null : throw new InvalidOperationException("Salesforce returned an empty DateTimeOffset value for a non-nullable target type.");
                }

                if (DateTimeOffset.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                        out var dto))
                {
                    return dto;
                }

                // Fallback: parse as DateTime and treat as UTC.
                if (DateTime.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return new DateTimeOffset(dt, TimeSpan.Zero);
                }

                return isNullable ? null : throw new InvalidOperationException($"Unable to parse Salesforce DateTimeOffset value '{str}'.");
            }

            if (underlyingType == typeof(DateOnly))
            {
                var str = token.ToString();
                if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return isNullable ? null : throw new InvalidOperationException("Salesforce returned an empty DateOnly value for a non-nullable target type.");
                }

                if (DateOnly.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
                {
                    return dateOnly;
                }

                if (DateTimeOffset.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                        out var dto))
                {
                    return DateOnly.FromDateTime(dto.UtcDateTime);
                }

                if (DateTime.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return DateOnly.FromDateTime(dt);
                }

                return isNullable ? null : throw new InvalidOperationException($"Unable to parse Salesforce DateOnly value '{str}'.");
            }

            if (underlyingType == typeof(TimeOnly))
            {
                var str = token.ToString();
                if (string.IsNullOrWhiteSpace(str) || str.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return isNullable ? null : throw new InvalidOperationException("Salesforce returned an empty TimeOnly value for a non-nullable target type.");
                }

                if (TimeOnly.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timeOnly))
                {
                    return timeOnly;
                }

                // Some orgs may return a full datetime-like representation; fall back to DateTimeOffset/DateTime parsing.
                if (DateTimeOffset.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                        out var dto))
                {
                    return TimeOnly.FromDateTime(dto.UtcDateTime);
                }

                if (DateTime.TryParse(
                        str,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return TimeOnly.FromDateTime(dt);
                }

                return isNullable ? null : throw new InvalidOperationException($"Unable to parse Salesforce TimeOnly value '{str}'.");
            }

            if (underlyingType.IsEnum)
            {
                var str = token.ToString();
                return ParseEnumValue(str, underlyingType);
            }

            // Default: try to convert via JsonNode deserialization
            return JsonSerializer.Deserialize(token, targetType);
        }
        catch
        {
            // Re-throw exception to make data integrity errors noisy
            throw;
        }
    }

    #endregion

    #region Nested Types

    private sealed class EnumMap
    {
        public EnumMap(
            Dictionary<Enum, string> enumToValue,
            Dictionary<string, Enum> valueToEnum)
        {
            EnumToValue = enumToValue;
            ValueToEnum = valueToEnum;
        }

        public Dictionary<Enum, string> EnumToValue { get; }
        public Dictionary<string, Enum> ValueToEnum { get; }
    }

    private class ObjectMetadata
    {
        public Type Type { get; set; } = null!;
        public string ObjectName { get; set; } = null!;
        public bool IsQueryable { get; set; }
        public bool IsCreateable { get; set; }
        public bool IsUpdateable { get; set; }
        public bool IsDeletable { get; set; }
        public Dictionary<string, PropertyMapping> PropertyMappings { get; } = new();
    }

    private class PropertyMapping
    {
        public PropertyInfo Property { get; set; } = null!;
        public string SalesforceFieldName { get; set; } = null!;
        public bool IsIgnored { get; set; }
        public bool IsComputed { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsCreateable { get; set; }
        public bool IsUpdateable { get; set; }
        public bool IsRequired { get; set; }
        public bool IsId { get; set; }
        public bool IsExternalId { get; set; }
        public bool IsLookup { get; set; }
        public bool IsChildRelationship { get; set; }
        public string? ChildObject { get; set; }
        public string? ChildRelationshipName { get; set; }
        public string? ForeignKeyField { get; set; }
        public string? ReferenceTo { get; set; }
        public string? RelationshipName { get; set; }
        public int MaxLength { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
    }

    #endregion

    #region Enum Mapping Helpers

    private static string GetEnumSalesforceValue(Enum value)
    {
        var map = GetEnumMap(value.GetType());
        if (map.EnumToValue.TryGetValue(value, out var mapped))
        {
            return mapped;
        }

        return value.ToString();
    }

    private static object ParseEnumValue(string rawValue, Type enumType)
    {
        var map = GetEnumMap(enumType);
        if (map.ValueToEnum.TryGetValue(rawValue, out var mapped))
        {
            return mapped;
        }

        return Enum.Parse(enumType, rawValue, ignoreCase: true);
    }

    private static EnumMap GetEnumMap(Type enumType)
    {
        return _enumMapCache.GetOrAdd(enumType, BuildEnumMap);
    }

    private static EnumMap BuildEnumMap(Type enumType)
    {
        var enumToValue = new Dictionary<Enum, string>();
        var valueToEnum = new Dictionary<string, Enum>(StringComparer.OrdinalIgnoreCase);

        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (var field in fields)
        {
            var enumValue = (Enum)field.GetValue(null)!;
            var attr = field.GetCustomAttribute<SalesforceValueAttribute>();
            var mappedValue = attr?.Value ?? field.Name;

            enumToValue[enumValue] = mappedValue;
            valueToEnum.TryAdd(mappedValue, enumValue);
            valueToEnum.TryAdd(field.Name, enumValue);
        }

        return new EnumMap(enumToValue, valueToEnum);
    }

    #endregion
}
