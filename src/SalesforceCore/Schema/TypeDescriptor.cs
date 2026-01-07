using System.Collections.Concurrent;
using System.Reflection;
using SalesforceCore.Attributes;
using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Schema;

/// <summary>
/// Runtime type information for Salesforce entities.
/// Provides cached reflection data and schema mapping.
/// </summary>
public interface ITypeDescriptor
{
    /// <summary>
    /// Gets the Salesforce object name.
    /// </summary>
    string ObjectName { get; }

    /// <summary>
    /// Gets the .NET type.
    /// </summary>
    Type ClrType { get; }

    /// <summary>
    /// Gets all property descriptors.
    /// </summary>
    IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>
    /// Gets the ID property descriptor.
    /// </summary>
    PropertyDescriptor? IdProperty { get; }

    /// <summary>
    /// Gets the external ID property descriptor.
    /// </summary>
    PropertyDescriptor? ExternalIdProperty { get; }

    /// <summary>
    /// Gets lookup/reference properties.
    /// </summary>
    IReadOnlyList<PropertyDescriptor> LookupProperties { get; }

    /// <summary>
    /// Gets child relationship properties.
    /// </summary>
    IReadOnlyList<PropertyDescriptor> ChildRelationshipProperties { get; }

    /// <summary>
    /// Gets a property by CLR name.
    /// </summary>
    PropertyDescriptor? GetProperty(string propertyName);

    /// <summary>
    /// Gets a property by Salesforce field name.
    /// </summary>
    PropertyDescriptor? GetPropertyByFieldName(string fieldName);

    /// <summary>
    /// Gets field names for a SOQL SELECT clause.
    /// </summary>
    IEnumerable<string> GetSelectFields(bool includeRelationships = false);

    /// <summary>
    /// Gets field names for create operations.
    /// </summary>
    IEnumerable<string> GetCreateableFields();

    /// <summary>
    /// Gets field names for update operations.
    /// </summary>
    IEnumerable<string> GetUpdateableFields();

    /// <summary>
    /// Checks if the type has a specific attribute.
    /// </summary>
    bool HasAttribute<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// Gets a specific attribute from the type.
    /// </summary>
    TAttribute? GetAttribute<TAttribute>() where TAttribute : Attribute;
}

/// <summary>
/// Descriptor for a single property.
/// </summary>
public class PropertyDescriptor
{
    /// <summary>
    /// CLR property name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Salesforce field API name.
    /// </summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>
    /// CLR property type.
    /// </summary>
    public Type PropertyType { get; init; } = typeof(object);

    /// <summary>
    /// Whether this is the ID field.
    /// </summary>
    public bool IsId { get; init; }

    /// <summary>
    /// Whether this is an external ID field.
    /// </summary>
    public bool IsExternalId { get; init; }

    /// <summary>
    /// Whether this is a lookup/reference field.
    /// </summary>
    public bool IsLookup { get; init; }

    /// <summary>
    /// Whether this is a child relationship.
    /// </summary>
    public bool IsChildRelationship { get; init; }

    /// <summary>
    /// Whether this field should be ignored.
    /// </summary>
    public bool IsIgnored { get; init; }

    /// <summary>
    /// Whether this field is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Whether this field is createable.
    /// </summary>
    public bool IsCreateable { get; init; } = true;

    /// <summary>
    /// Whether this field is updateable.
    /// </summary>
    public bool IsUpdateable { get; init; } = true;

    /// <summary>
    /// Relationship name for lookup fields.
    /// </summary>
    public string? RelationshipName { get; init; }

    /// <summary>
    /// Referenced object name for lookups.
    /// </summary>
    public string? ReferenceTo { get; init; }

    /// <summary>
    /// Maximum length for string fields.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Field type from attribute.
    /// </summary>
    public string? FieldType { get; init; }

    /// <summary>
    /// Whether this is a required field.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// The underlying PropertyInfo.
    /// </summary>
    public PropertyInfo PropertyInfo { get; init; } = null!;

    /// <summary>
    /// Gets the value from an entity.
    /// </summary>
    public object? GetValue(object entity)
    {
        return PropertyInfo.GetValue(entity);
    }

    /// <summary>
    /// Sets the value on an entity.
    /// </summary>
    public void SetValue(object entity, object? value)
    {
        if (PropertyInfo.CanWrite)
        {
            // Handle type conversion if needed
            var targetValue = ConvertValue(value, PropertyType);
            PropertyInfo.SetValue(entity, targetValue);
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.GetType() == underlyingType)
            return value;

        // Handle common conversions
        if (underlyingType == typeof(string))
            return value.ToString();

        if (underlyingType == typeof(DateTime) && value is string dateStr)
            return DateTime.TryParse(dateStr, out var dt) ? dt : null;

        if (underlyingType == typeof(DateOnly) && value is string dateOnlyStr)
            return DateOnly.TryParse(dateOnlyStr, out var d) ? d : null;

        if (underlyingType == typeof(bool) && value is string boolStr)
            return bool.TryParse(boolStr, out var b) && b;

        if (underlyingType.IsEnum && value is string enumStr)
            return Enum.TryParse(underlyingType, enumStr, true, out var e) ? e : null;

        try
        {
            return Convert.ChangeType(value, underlyingType);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Implementation of type descriptor.
/// </summary>
public class TypeDescriptor : ITypeDescriptor
{
    private readonly List<PropertyDescriptor> _properties = new();
    private readonly Dictionary<string, PropertyDescriptor> _propertyByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PropertyDescriptor> _propertyByFieldName = new(StringComparer.OrdinalIgnoreCase);

    public string ObjectName { get; private init; } = string.Empty;
    public Type ClrType { get; private init; } = typeof(object);
    public IReadOnlyList<PropertyDescriptor> Properties => _properties;
    public PropertyDescriptor? IdProperty { get; private set; }
    public PropertyDescriptor? ExternalIdProperty { get; private set; }
    public IReadOnlyList<PropertyDescriptor> LookupProperties => _properties.Where(p => p.IsLookup).ToList();
    public IReadOnlyList<PropertyDescriptor> ChildRelationshipProperties => _properties.Where(p => p.IsChildRelationship).ToList();

    private TypeDescriptor() { }

    /// <summary>
    /// Creates a type descriptor from a .NET type.
    /// </summary>
    public static TypeDescriptor FromType(Type type)
    {
        var descriptor = new TypeDescriptor
        {
            ClrType = type,
            ObjectName = GetObjectName(type)
        };

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propDesc = CreatePropertyDescriptor(prop);
            descriptor._properties.Add(propDesc);
            descriptor._propertyByName[propDesc.Name] = propDesc;
            descriptor._propertyByFieldName[propDesc.FieldName] = propDesc;

            if (propDesc.IsId)
                descriptor.IdProperty = propDesc;
            if (propDesc.IsExternalId)
                descriptor.ExternalIdProperty = propDesc;
        }

        return descriptor;
    }

    /// <inheritdoc/>
    public PropertyDescriptor? GetProperty(string propertyName)
    {
        return _propertyByName.TryGetValue(propertyName, out var prop) ? prop : null;
    }

    /// <inheritdoc/>
    public PropertyDescriptor? GetPropertyByFieldName(string fieldName)
    {
        return _propertyByFieldName.TryGetValue(fieldName, out var prop) ? prop : null;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetSelectFields(bool includeRelationships = false)
    {
        foreach (var prop in _properties)
        {
            if (prop.IsIgnored)
                continue;
            if (prop.IsChildRelationship && !includeRelationships)
                continue;

            yield return prop.FieldName;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetCreateableFields()
    {
        return _properties
            .Where(p => !p.IsIgnored && p.IsCreateable && !p.IsReadOnly && !p.IsId)
            .Select(p => p.FieldName);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetUpdateableFields()
    {
        return _properties
            .Where(p => !p.IsIgnored && p.IsUpdateable && !p.IsReadOnly)
            .Select(p => p.FieldName);
    }

    /// <inheritdoc/>
    public bool HasAttribute<TAttribute>() where TAttribute : Attribute
    {
        return ClrType.GetCustomAttribute<TAttribute>() != null;
    }

    /// <inheritdoc/>
    public TAttribute? GetAttribute<TAttribute>() where TAttribute : Attribute
    {
        return ClrType.GetCustomAttribute<TAttribute>();
    }

    private static string GetObjectName(Type type)
    {
        var attr = type.GetCustomAttribute<SalesforceObjectAttribute>();
        return attr?.ObjectName ?? type.Name;
    }

    private static PropertyDescriptor CreatePropertyDescriptor(PropertyInfo prop)
    {
        var fieldAttr = prop.GetCustomAttribute<SalesforceFieldAttribute>();
        var idAttr = prop.GetCustomAttribute<SalesforceIdAttribute>();
        var extIdAttr = prop.GetCustomAttribute<SalesforceExternalIdAttribute>();
        var lookupAttr = prop.GetCustomAttribute<SalesforceLookupAttribute>();
        var childRelAttr = prop.GetCustomAttribute<SalesforceChildRelationshipAttribute>();
        var ignoreAttr = prop.GetCustomAttribute<SalesforceIgnoreAttribute>();

        return new PropertyDescriptor
        {
            Name = prop.Name,
            FieldName = fieldAttr?.FieldName ?? prop.Name,
            PropertyType = prop.PropertyType,
            PropertyInfo = prop,
            IsId = idAttr != null || prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase),
            IsExternalId = extIdAttr != null,
            IsLookup = lookupAttr != null || (fieldAttr?.FieldName?.EndsWith("Id") == true && prop.PropertyType == typeof(string)),
            IsChildRelationship = childRelAttr != null,
            IsIgnored = ignoreAttr != null,
            IsReadOnly = fieldAttr?.ReadOnly ?? false,
            IsCreateable = fieldAttr?.Createable ?? true,
            IsUpdateable = fieldAttr?.Updateable ?? true,
            RelationshipName = lookupAttr?.RelationshipName ?? fieldAttr?.RelationshipName,
            ReferenceTo = lookupAttr?.ReferenceTo ?? fieldAttr?.ReferenceTo,
            MaxLength = fieldAttr?.MaxLength,
            FieldType = fieldAttr?.FieldType,
            IsRequired = fieldAttr?.Required ?? false
        };
    }
}

/// <summary>
/// Cache for type descriptors.
/// </summary>
public static class TypeDescriptorCache
{
    private static readonly ConcurrentDictionary<Type, ITypeDescriptor> _cache = new();

    /// <summary>
    /// Gets the type descriptor for a type.
    /// </summary>
    public static ITypeDescriptor Get<T>() where T : class
    {
        return Get(typeof(T));
    }

    /// <summary>
    /// Gets the type descriptor for a type.
    /// </summary>
    public static ITypeDescriptor Get(Type type)
    {
        return _cache.GetOrAdd(type, t => TypeDescriptor.FromType(t));
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public static void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Removes a type from the cache.
    /// </summary>
    public static void Remove<T>() where T : class
    {
        _cache.TryRemove(typeof(T), out _);
    }
}

/// <summary>
/// Extension methods for type descriptors.
/// </summary>
public static class TypeDescriptorExtensions
{
    /// <summary>
    /// Converts an entity to a Salesforce dictionary using type descriptor.
    /// </summary>
    public static IDictionary<string, object?> ToDictionary<T>(this T entity, bool forCreate = false) where T : class
    {
        var descriptor = TypeDescriptorCache.Get<T>();
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in descriptor.Properties)
        {
            if (prop.IsIgnored)
                continue;
            if (prop.IsChildRelationship)
                continue;
            if (forCreate && !prop.IsCreateable)
                continue;
            if (forCreate && prop.IsId)
                continue;

            var value = prop.GetValue(entity);
            if (value != null)
            {
                result[prop.FieldName] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Populates an entity from a dictionary using type descriptor.
    /// </summary>
    public static T FromDictionary<T>(this IDictionary<string, object?> data) where T : class, new()
    {
        var entity = new T();
        var descriptor = TypeDescriptorCache.Get<T>();

        foreach (var (key, value) in data)
        {
            var prop = descriptor.GetPropertyByFieldName(key);
            if (prop != null && !prop.IsIgnored)
            {
                prop.SetValue(entity, value);
            }
        }

        return entity;
    }

    /// <summary>
    /// Gets the ID value from an entity.
    /// </summary>
    public static string? GetId<T>(this T entity) where T : class
    {
        var descriptor = TypeDescriptorCache.Get<T>();
        return descriptor.IdProperty?.GetValue(entity)?.ToString();
    }

    /// <summary>
    /// Sets the ID value on an entity.
    /// </summary>
    public static void SetId<T>(this T entity, string id) where T : class
    {
        var descriptor = TypeDescriptorCache.Get<T>();
        descriptor.IdProperty?.SetValue(entity, id);
    }
}
