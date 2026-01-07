using System.Globalization;
using System.Reflection;
using SalesforceCore.Attributes;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Query;

/// <summary>
/// Factory helpers for safe aggregate expressions used in HAVING clauses.
/// </summary>
public static class SoqlAggregate
{
    public static SoqlAggregateExpression Count() => new("COUNT", null, fieldRequired: false);
    public static SoqlAggregateExpression Count(string field) => new("COUNT", field, fieldRequired: false);
    public static SoqlAggregateExpression CountDistinct(string field) => new("COUNT_DISTINCT", field, fieldRequired: true);
    public static SoqlAggregateExpression Sum(string field) => new("SUM", field, fieldRequired: true);
    public static SoqlAggregateExpression Avg(string field) => new("AVG", field, fieldRequired: true);
    public static SoqlAggregateExpression Min(string field) => new("MIN", field, fieldRequired: true);
    public static SoqlAggregateExpression Max(string field) => new("MAX", field, fieldRequired: true);
}

/// <summary>
/// Represents an aggregate function expression (e.g., COUNT(), SUM(Amount)).
/// </summary>
public sealed class SoqlAggregateExpression
{
    private readonly string _function;
    private readonly string? _field;

    internal SoqlAggregateExpression(string function, string? field, bool fieldRequired)
    {
        if (string.IsNullOrWhiteSpace(function))
        {
            throw new ArgumentException("Aggregate function is required.", nameof(function));
        }

        _function = NormalizeAggregateFunction(function);

        if (fieldRequired && string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException($"Aggregate function {_function} requires a field name.", nameof(field));
        }

        if (!string.IsNullOrWhiteSpace(field))
        {
            _field = SecurityUtils.SanitizeFieldName(field);
        }
    }

    public SoqlAggregateCondition Compare(ComparisonOperator op, object value)
        => new AggregateComparisonCondition(this, GetOperatorString(op), value);

    public SoqlAggregateCondition EqualsTo(object value)
        => Compare(ComparisonOperator.Equals, value);

    public SoqlAggregateCondition NotEquals(object value)
        => Compare(ComparisonOperator.NotEquals, value);

    public SoqlAggregateCondition GreaterThan(object value)
        => Compare(ComparisonOperator.GreaterThan, value);

    public SoqlAggregateCondition GreaterThanOrEqual(object value)
        => Compare(ComparisonOperator.GreaterThanOrEqual, value);

    public SoqlAggregateCondition LessThan(object value)
        => Compare(ComparisonOperator.LessThan, value);

    public SoqlAggregateCondition LessThanOrEqual(object value)
        => Compare(ComparisonOperator.LessThanOrEqual, value);

    internal string Render()
    {
        return _field == null ? $"{_function}()" : $"{_function}({_field})";
    }

    private static string NormalizeAggregateFunction(string aggregateFunc)
    {
        var normalized = aggregateFunc.Trim().ToUpperInvariant();

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
            _ => throw new ArgumentException($"Unsupported aggregate function: {aggregateFunc}", nameof(aggregateFunc))
        };
    }

    private static string GetOperatorString(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equals => "=",
        ComparisonOperator.NotEquals => "!=",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };
}

/// <summary>
/// Represents a typed HAVING condition built from aggregate expressions.
/// </summary>
public abstract class SoqlAggregateCondition
{
    internal abstract string Render();

    public static SoqlAggregateCondition And(params SoqlAggregateCondition[] conditions)
        => new AggregateCompoundCondition("AND", conditions ?? throw new ArgumentNullException(nameof(conditions)));

    public static SoqlAggregateCondition Or(params SoqlAggregateCondition[] conditions)
        => new AggregateCompoundCondition("OR", conditions ?? throw new ArgumentNullException(nameof(conditions)));

    public static SoqlAggregateCondition Not(SoqlAggregateCondition condition)
        => condition == null ? throw new ArgumentNullException(nameof(condition)) : new AggregateNotCondition(condition);
}

internal sealed class AggregateComparisonCondition : SoqlAggregateCondition
{
    private readonly SoqlAggregateExpression _expression;
    private readonly string _operator;
    private readonly object _value;

    public AggregateComparisonCondition(SoqlAggregateExpression expression, string op, object value)
    {
        _expression = expression ?? throw new ArgumentNullException(nameof(expression));
        _operator = op ?? throw new ArgumentNullException(nameof(op));
        _value = value;
    }

    internal override string Render()
    {
        return $"{_expression.Render()} {_operator} {FormatValue(_value)}";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{SecurityUtils.SanitizeForSoql(s)}'",
        bool b => b.ToString().ToUpperInvariant(),
        DateTime dt => FormatDateTimeUtc(dt),
        DateTimeOffset dto => FormatDateTimeOffsetUtc(dto),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        double dbl => dbl.ToString(CultureInfo.InvariantCulture),
        float fl => fl.ToString(CultureInfo.InvariantCulture),
        Enum e => $"'{GetEnumSalesforceValue(e)}'",
        _ => $"'{SecurityUtils.SanitizeForSoql(value.ToString() ?? "")}'"
    };

    private static string FormatDateTimeUtc(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => dateTime
        };

        return utc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatDateTimeOffsetUtc(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string GetEnumSalesforceValue(Enum e)
    {
        var type = e.GetType();
        var memberName = e.ToString();
        var memberInfo = type.GetField(memberName);

        if (memberInfo != null)
        {
            var attr = memberInfo.GetCustomAttribute<SalesforceValueAttribute>();
            if (attr != null)
            {
                return SecurityUtils.SanitizeForSoql(attr.Value);
            }
        }

        return SecurityUtils.SanitizeForSoql(memberName);
    }
}

internal sealed class AggregateCompoundCondition : SoqlAggregateCondition
{
    private readonly string _join;
    private readonly IReadOnlyList<SoqlAggregateCondition> _conditions;

    public AggregateCompoundCondition(string join, SoqlAggregateCondition[] conditions)
    {
        _join = join ?? throw new ArgumentNullException(nameof(join));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));

        if (_conditions.Count == 0)
        {
            throw new ArgumentException("At least one condition is required.", nameof(conditions));
        }

        if (_conditions.Any(c => c == null))
        {
            throw new ArgumentException("Aggregate conditions cannot contain null entries.", nameof(conditions));
        }
    }

    internal override string Render()
    {
        if (_conditions.Count == 1)
        {
            return _conditions[0].Render();
        }

        return string.Join($" {_join} ", _conditions.Select(c => $"({c.Render()})"));
    }
}

internal sealed class AggregateNotCondition : SoqlAggregateCondition
{
    private readonly SoqlAggregateCondition _condition;

    public AggregateNotCondition(SoqlAggregateCondition condition)
    {
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    internal override string Render()
    {
        return $"NOT ({_condition.Render()})";
    }
}
