using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;

namespace SalesforceCore.Tracking;

/// <summary>
/// Interface for tracking changes to Salesforce records.
/// </summary>
public interface IChangeTracker
{
    /// <summary>
    /// Begins tracking an entity.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="entity">Entity to track.</param>
    /// <param name="state">Initial state.</param>
    void Track<T>(T entity, EntityState state = EntityState.Unchanged) where T : class;

    /// <summary>
    /// Stops tracking an entity.
    /// </summary>
    void Untrack<T>(T entity) where T : class;

    /// <summary>
    /// Gets the current state of an entity.
    /// </summary>
    EntityState GetState<T>(T entity) where T : class;

    /// <summary>
    /// Gets all changes for an entity.
    /// </summary>
    IReadOnlyList<FieldChange> GetChanges<T>(T entity) where T : class;

    /// <summary>
    /// Gets only the modified fields for update operations.
    /// </summary>
    IDictionary<string, object?> GetModifiedFields<T>(T entity) where T : class;

    /// <summary>
    /// Marks an entity as modified.
    /// </summary>
    void MarkModified<T>(T entity) where T : class;

    /// <summary>
    /// Marks an entity for deletion.
    /// </summary>
    void MarkDeleted<T>(T entity) where T : class;

    /// <summary>
    /// Accepts all changes (resets tracking to current values).
    /// </summary>
    void AcceptChanges<T>(T entity) where T : class;

    /// <summary>
    /// Reverts all changes to original values.
    /// </summary>
    void RevertChanges<T>(T entity) where T : class;

    /// <summary>
    /// Gets all tracked entities with pending changes.
    /// </summary>
    IEnumerable<TrackedEntity> GetPendingChanges();

    /// <summary>
    /// Clears all tracked entities.
    /// </summary>
    void Clear();

    /// <summary>
    /// Checks if an entity has any changes.
    /// </summary>
    bool HasChanges<T>(T entity) where T : class;

    /// <summary>
    /// Gets entries for all tracked entities.
    /// </summary>
    IEnumerable<TrackedEntity> Entries();
}

/// <summary>
/// State of a tracked entity.
/// </summary>
public enum EntityState
{
    /// <summary>Entity is not being tracked.</summary>
    Detached,
    /// <summary>Entity exists in Salesforce and hasn't been modified.</summary>
    Unchanged,
    /// <summary>Entity is new and will be inserted.</summary>
    Added,
    /// <summary>Entity exists and has been modified.</summary>
    Modified,
    /// <summary>Entity exists and will be deleted.</summary>
    Deleted
}

/// <summary>
/// Represents a change to a single field.
/// </summary>
public record FieldChange(
    string FieldName,
    string SalesforceFieldName,
    object? OriginalValue,
    object? CurrentValue,
    Type FieldType);

/// <summary>
/// Represents a tracked entity.
/// </summary>
public class TrackedEntity
{
    /// <summary>
    /// The tracked entity object.
    /// </summary>
    public required object Entity { get; init; }

    /// <summary>
    /// The entity type.
    /// </summary>
    public required Type EntityType { get; init; }

    /// <summary>
    /// Salesforce object name.
    /// </summary>
    public required string ObjectName { get; init; }

    /// <summary>
    /// Current entity state.
    /// </summary>
    public EntityState State { get; set; }

    /// <summary>
    /// Original values when tracking started.
    /// </summary>
    public required IDictionary<string, object?> OriginalValues { get; init; }

    /// <summary>
    /// List of field changes.
    /// </summary>
    public List<FieldChange> Changes { get; } = new();

    /// <summary>
    /// Record ID if known.
    /// </summary>
    public string? RecordId { get; set; }
}

/// <summary>
/// Implementation of change tracking for Salesforce entities.
/// </summary>
public class ChangeTracker : IChangeTracker
{
    private readonly ConcurrentDictionary<object, TrackedEntity> _trackedEntities = new();

    /// <inheritdoc/>
    public void Track<T>(T entity, EntityState state = EntityState.Unchanged) where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var objectName = SalesforceMapper.GetObjectName<T>();
        var originalValues = CaptureValues(entity);

        var entry = new TrackedEntity
        {
            Entity = entity,
            EntityType = typeof(T),
            ObjectName = objectName,
            State = state,
            OriginalValues = originalValues,
            RecordId = GetRecordId(entity)
        };

        _trackedEntities[entity] = entry;
    }

    /// <inheritdoc/>
    public void Untrack<T>(T entity) where T : class
    {
        _trackedEntities.TryRemove(entity, out _);
    }

    /// <inheritdoc/>
    public EntityState GetState<T>(T entity) where T : class
    {
        if (_trackedEntities.TryGetValue(entity, out var entry))
        {
            // Auto-detect modifications
            if (entry.State == EntityState.Unchanged)
            {
                DetectChanges(entry);
                if (entry.Changes.Count > 0)
                    entry.State = EntityState.Modified;
            }
            return entry.State;
        }
        return EntityState.Detached;
    }

    /// <inheritdoc/>
    public IReadOnlyList<FieldChange> GetChanges<T>(T entity) where T : class
    {
        if (!_trackedEntities.TryGetValue(entity, out var entry))
            return Array.Empty<FieldChange>();

        DetectChanges(entry);
        return entry.Changes.AsReadOnly();
    }

    /// <inheritdoc/>
    public IDictionary<string, object?> GetModifiedFields<T>(T entity) where T : class
    {
        var changes = GetChanges(entity);
        return changes.ToDictionary(
            c => c.SalesforceFieldName,
            c => c.CurrentValue);
    }

    /// <inheritdoc/>
    public void MarkModified<T>(T entity) where T : class
    {
        if (_trackedEntities.TryGetValue(entity, out var entry))
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Deleted)
                entry.State = EntityState.Modified;
        }
        else
        {
            Track(entity, EntityState.Modified);
        }
    }

    /// <inheritdoc/>
    public void MarkDeleted<T>(T entity) where T : class
    {
        if (_trackedEntities.TryGetValue(entity, out var entry))
        {
            entry.State = EntityState.Deleted;
        }
        else
        {
            Track(entity, EntityState.Deleted);
        }
    }

    /// <inheritdoc/>
    public void AcceptChanges<T>(T entity) where T : class
    {
        if (_trackedEntities.TryGetValue(entity, out var entry))
        {
            entry.OriginalValues.Clear();
            foreach (var kvp in CaptureValues(entity))
            {
                entry.OriginalValues[kvp.Key] = kvp.Value;
            }
            entry.Changes.Clear();
            entry.State = EntityState.Unchanged;
            entry.RecordId = GetRecordId(entity);
        }
    }

    /// <inheritdoc/>
    public void RevertChanges<T>(T entity) where T : class
    {
        if (!_trackedEntities.TryGetValue(entity, out var entry))
            return;

        var type = typeof(T);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite) continue;

            var fieldName = GetSalesforceFieldName(prop);
            if (entry.OriginalValues.TryGetValue(fieldName, out var originalValue))
            {
                try
                {
                    prop.SetValue(entity, originalValue);
                }
                catch
                {
                    // Ignore properties that can't be set
                }
            }
        }

        entry.Changes.Clear();
        entry.State = EntityState.Unchanged;
    }

    /// <inheritdoc/>
    public IEnumerable<TrackedEntity> GetPendingChanges()
    {
        foreach (var entry in _trackedEntities.Values)
        {
            if (entry.State == EntityState.Unchanged)
                DetectChanges(entry);

            if (entry.State != EntityState.Unchanged && entry.State != EntityState.Detached)
                yield return entry;
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _trackedEntities.Clear();
    }

    /// <inheritdoc/>
    public bool HasChanges<T>(T entity) where T : class
    {
        return GetChanges(entity).Count > 0;
    }

    /// <inheritdoc/>
    public IEnumerable<TrackedEntity> Entries()
    {
        return _trackedEntities.Values;
    }

    private void DetectChanges(TrackedEntity entry)
    {
        entry.Changes.Clear();
        var currentValues = CaptureValues(entry.Entity);

        foreach (var (fieldName, currentValue) in currentValues)
        {
            entry.OriginalValues.TryGetValue(fieldName, out var originalValue);

            if (!ValuesEqual(originalValue, currentValue))
            {
                var prop = GetPropertyForField(entry.EntityType, fieldName);
                entry.Changes.Add(new FieldChange(
                    prop?.Name ?? fieldName,
                    fieldName,
                    originalValue,
                    currentValue,
                    prop?.PropertyType ?? typeof(object)));
            }
        }

        // Update state if changes detected
        if (entry.State == EntityState.Unchanged && entry.Changes.Count > 0)
            entry.State = EntityState.Modified;
    }

    private static IDictionary<string, object?> CaptureValues(object entity)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var type = entity.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            if (prop.GetCustomAttribute<SalesforceIgnoreAttribute>() != null) continue;

            var fieldName = GetSalesforceFieldName(prop);
            try
            {
                var value = prop.GetValue(entity);
                values[fieldName] = CloneValue(value);
            }
            catch
            {
                // Ignore properties that can't be read
            }
        }

        return values;
    }

    private static string GetSalesforceFieldName(PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<SalesforceFieldAttribute>();
        return attr?.FieldName ?? prop.Name;
    }

    private static PropertyInfo? GetPropertyForField(Type type, string fieldName)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p =>
            {
                var attr = p.GetCustomAttribute<SalesforceFieldAttribute>();
                var name = attr?.FieldName ?? p.Name;
                return name.Equals(fieldName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string? GetRecordId(object entity)
    {
        var type = entity.GetType();

        // Look for property with SalesforceId attribute
        var idProp = type.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<SalesforceIdAttribute>() != null);

        // Fall back to property named "Id"
        idProp ??= type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (idProp != null)
        {
            var value = idProp.GetValue(entity);
            return value?.ToString();
        }

        return null;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Handle JsonNode comparisons
        if (a is JsonNode ja && b is JsonNode jb)
            return JsonNode.DeepEquals(ja, jb);

        var typeA = a.GetType();
        var typeB = b.GetType();

        // Value types and strings
        if ((typeA.IsValueType || typeA == typeof(string)) &&
            (typeB.IsValueType || typeB == typeof(string)))
        {
            return a.Equals(b);
        }

        // Handle collections
        if (a is System.Collections.IEnumerable ea && b is System.Collections.IEnumerable eb &&
            a is not string && b is not string)
        {
            var listA = ea.Cast<object?>().ToList();
            var listB = eb.Cast<object?>().ToList();

            if (listA.Count != listB.Count) return false;

            for (int i = 0; i < listA.Count; i++)
            {
                if (!ValuesEqual(listA[i], listB[i]))
                    return false;
            }
            return true;
        }

        // Deep comparison for complex types using JSON serialization
        // This is robust but heavier. Ensure circular references are handled by settings if needed,
        // but Salesforce models are typically tree-like.
        try
        {
            var jsonA = JsonSerializer.Serialize(a);
            var jsonB = JsonSerializer.Serialize(b);
            return jsonA == jsonB;
        }
        catch
        {
            // Fallback to Equals if serialization fails
            return a.Equals(b);
        }
    }

    private static object? CloneValue(object? value)
    {
        if (value == null) return null;

        // Value types are already cloned
        if (value.GetType().IsValueType) return value;

        // Strings are immutable
        if (value is string) return value;

        // Clone JsonNodes
        if (value is JsonNode jt) return jt.DeepClone();

        // Clone simple lists
        if (value is System.Collections.IList list)
        {
            var cloned = new List<object?>();
            foreach (var item in list)
            {
                cloned.Add(CloneValue(item));
            }
            return cloned;
        }

        // For complex objects, perform deep clone via JSON serialization
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize(json, value.GetType());
        }
        catch
        {
            // Fallback if serialization fails
            return value;
        }
    }
}
