using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalesforceCore.Mapping;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Query;
/// <summary>
/// Represents the result of a GroupBy query with aggregate values.
/// </summary>
public class AggregateResult
{
    private readonly Dictionary<string, object?> _values;

    /// <summary>
    /// Creates a new aggregate result from a dictionary.
    /// </summary>
    public AggregateResult(Dictionary<string, object?> values)
    {
        _values = values ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// Gets a value by key.
    /// </summary>
    public object? this[string key] => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a typed value by key.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_values.TryGetValue(key, out var value) || value == null)
            return default;

        if (value is T typedValue)
            return typedValue;

        try
        {
            // Convert.ChangeType cannot target Nullable<T>; convert to the underlying type (int? <- decimal).
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target.IsEnum)
                return (T)Enum.Parse(target, value.ToString()!, ignoreCase: true);
            if (target == typeof(DateTime) && value is string dts)
                return (T)(object)DateTime.Parse(dts, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
            if (target == typeof(DateOnly) && value is string ds)
                return (T)(object)DateOnly.Parse(ds, System.Globalization.CultureInfo.InvariantCulture);
            return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Gets the count value (from COUNT()).
    /// </summary>
    public int Count => Get<int?>("expr0") ?? Get<int?>("Count") ?? 0;

    /// <summary>
    /// Gets all keys.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>
    /// Gets all values.
    /// </summary>
    public IEnumerable<object?> Values => _values.Values;
}


/// <summary>
/// Specifies an aggregate function for GroupBy operations.
/// </summary>
public class AggregateSpec
{
    /// <summary>
    /// The field to aggregate.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// The aggregate function to apply.
    /// </summary>
    public string Function { get; }

    /// <summary>
    /// Optional alias for the result.
    /// </summary>
    public string? Alias { get; }

    /// <summary>
    /// Creates a new aggregate specification.
    /// </summary>
    public AggregateSpec(string field, AggregateFunction function, string? alias = null)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Function = NormalizeAggregateFunction(function);
        Alias = alias;
    }

    /// <summary>
    /// Creates a new aggregate specification with a string function name.
    /// </summary>
    public AggregateSpec(string field, string function, string? alias = null)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Function = NormalizeAggregateFunction(function ?? throw new ArgumentNullException(nameof(function)));
        Alias = alias;
    }

    private static string NormalizeAggregateFunction(AggregateFunction function)
    {
        return function switch
        {
            AggregateFunction.Count => "COUNT",
            AggregateFunction.Sum => "SUM",
            AggregateFunction.Avg => "AVG",
            AggregateFunction.Min => "MIN",
            AggregateFunction.Max => "MAX",
            AggregateFunction.CountDistinct => "COUNT_DISTINCT",
            _ => throw new ArgumentOutOfRangeException(nameof(function))
        };
    }

    private static string NormalizeAggregateFunction(string function)
    {
        if (string.IsNullOrWhiteSpace(function))
        {
            throw new ArgumentException("Aggregate function is required.", nameof(function));
        }

        var normalized = function.Trim().ToUpperInvariant();

        if (normalized == "COUNTDISTINCT")
        {
            normalized = "COUNT_DISTINCT";
        }

        return normalized switch
        {
            "COUNT" => "COUNT",
            "COUNT_DISTINCT" => "COUNT_DISTINCT",
            "SUM" => "SUM",
            "AVG" => "AVG",
            "MIN" => "MIN",
            "MAX" => "MAX",
            _ => throw new ArgumentException($"Unsupported aggregate function: {function}", nameof(function))
        };
    }
}

/// <summary>
/// Aggregate functions supported in SOQL.
/// </summary>
public enum AggregateFunction
{
    /// <summary>Count records.</summary>
    Count,
    /// <summary>Sum numeric values.</summary>
    Sum,
    /// <summary>Average numeric values.</summary>
    Avg,
    /// <summary>Minimum value.</summary>
    Min,
    /// <summary>Maximum value.</summary>
    Max,
    /// <summary>Count distinct values.</summary>
    CountDistinct
}

/// <summary>
/// Internal holder for Include expression data.
/// </summary>
internal class IncludeExpressionHolder
{
    public string RelationshipName { get; }
    public List<string> Fields { get; }
    public SoqlCondition? Condition { get; set; }
    public int? Limit { get; set; }

    public IncludeExpressionHolder(string relationshipName, List<string> fields)
    {
        RelationshipName = relationshipName;
        Fields = fields;
    }
}

/// <summary>
/// Equality comparer that uses the Id property of Salesforce objects.
/// Used by Union/Except/Intersect operations for set-based comparison.
/// </summary>
internal class IdBasedEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    private static readonly PropertyInfo? IdProperty = typeof(T).GetProperty("Id");

    public bool Equals(T? x, T? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        
        if (IdProperty == null)
        {
            // Fall back to reference equality if no Id property
            return ReferenceEquals(x, y);
        }

        var xId = IdProperty.GetValue(x);
        var yId = IdProperty.GetValue(y);
        
        if (xId == null && yId == null) return true;
        if (xId == null || yId == null) return false;
        
        return xId.Equals(yId);
    }

    public int GetHashCode(T obj)
    {
        if (obj == null) return 0;
        
        if (IdProperty == null)
        {
            return obj.GetHashCode();
        }

        var id = IdProperty.GetValue(obj);
        return id?.GetHashCode() ?? 0;
    }
}
