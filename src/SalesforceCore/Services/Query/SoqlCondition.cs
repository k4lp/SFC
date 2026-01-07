using System.Globalization;
using System.Reflection;
using SalesforceCore.Attributes;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Query;

/// <summary>
/// Represents a type-safe SOQL condition that can be combined with other conditions.
/// All conditions are validated and sanitized - no raw strings allowed.
/// </summary>
/// <remarks>
/// This class replaces the unsafe WhereRaw method with a structured, type-safe approach.
/// All field names are validated against Salesforce naming conventions.
/// All values are properly escaped to prevent SOQL injection.
/// </remarks>
public abstract class SoqlCondition
{
    /// <summary>
    /// Renders the condition as a SOQL string.
    /// </summary>
    internal abstract string Render();

    /// <summary>
    /// Creates an equality condition: field = value
    /// </summary>
    public static SoqlCondition Equals(string field, object? value)
        => new ComparisonCondition(field, "=", value);

    /// <summary>
    /// Creates a not-equals condition: field != value
    /// </summary>
    public static SoqlCondition NotEquals(string field, object? value)
        => new ComparisonCondition(field, "!=", value);

    /// <summary>
    /// Creates a greater-than condition: field > value
    /// </summary>
    public static SoqlCondition GreaterThan(string field, object value)
        => new ComparisonCondition(field, ">", value);

    /// <summary>
    /// Creates a greater-than-or-equal condition: field >= value
    /// </summary>
    public static SoqlCondition GreaterThanOrEqual(string field, object value)
        => new ComparisonCondition(field, ">=", value);

    /// <summary>
    /// Creates a less-than condition: field &lt; value
    /// </summary>
    public static SoqlCondition LessThan(string field, object value)
        => new ComparisonCondition(field, "<", value);

    /// <summary>
    /// Creates a less-than-or-equal condition: field &lt;= value
    /// </summary>
    public static SoqlCondition LessThanOrEqual(string field, object value)
        => new ComparisonCondition(field, "<=", value);

    /// <summary>
    /// Creates a null check condition: field = NULL
    /// </summary>
    public static SoqlCondition IsNull(string field)
        => new NullCondition(field, isNull: true);

    /// <summary>
    /// Creates a not-null check condition: field != NULL
    /// </summary>
    public static SoqlCondition IsNotNull(string field)
        => new NullCondition(field, isNull: false);

    /// <summary>
    /// Creates a LIKE condition: field LIKE pattern
    /// </summary>
    public static SoqlCondition Like(string field, string pattern)
        => new LikeCondition(field, pattern);

    /// <summary>
    /// Creates an IN condition: field IN (values)
    /// </summary>
    public static SoqlCondition In(string field, IEnumerable<object?> values)
        => new InCondition(field, values, negated: false);

    /// <summary>
    /// Creates a NOT IN condition: field NOT IN (values)
    /// </summary>
    public static SoqlCondition NotIn(string field, IEnumerable<object?> values)
        => new InCondition(field, values, negated: true);

    /// <summary>
    /// Creates an INCLUDES condition for multi-select picklists.
    /// </summary>
    public static SoqlCondition Includes(string field, IEnumerable<string> values)
        => new MultiSelectCondition(field, values, includes: true);

    /// <summary>
    /// Creates an EXCLUDES condition for multi-select picklists.
    /// </summary>
    public static SoqlCondition Excludes(string field, IEnumerable<string> values)
        => new MultiSelectCondition(field, values, includes: false);

    /// <summary>
    /// Creates a date literal condition: field = DATE_LITERAL
    /// </summary>
    public static SoqlCondition DateLiteral(string field, DateLiteral literal)
        => new DateLiteralCondition(field, "=", literal);

    /// <summary>
    /// Creates a date literal condition with comparison operator.
    /// </summary>
    public static SoqlCondition DateLiteralCompare(string field, ComparisonOperator op, DateLiteral literal)
        => new DateLiteralCondition(field, GetOperatorString(op), literal);

    /// <summary>
    /// Creates a parameterized date literal condition (e.g., LAST_N_DAYS:30).
    /// </summary>
    public static SoqlCondition DateLiteralN(string field, string literalExpression)
        => new DateLiteralNCondition(field, literalExpression);

    /// <summary>
    /// Creates a date function condition (e.g., CALENDAR_YEAR(field) = value).
    /// </summary>
    public static SoqlCondition DateFunction(DateFunction function, string field, int value, bool convertTimezone = false)
        => new DateFunctionCondition(function, field, "=", value, convertTimezone);

    /// <summary>
    /// Creates a date function condition with comparison operator.
    /// </summary>
    public static SoqlCondition DateFunctionCompare(DateFunction function, string field, ComparisonOperator op, int value, bool convertTimezone = false)
        => new DateFunctionCondition(function, field, GetOperatorString(op), value, convertTimezone);

    /// <summary>
    /// Creates a toLabel condition for translated picklist comparison.
    /// </summary>
    public static SoqlCondition ToLabelEquals(string field, string translatedValue)
        => new ToLabelCondition(field, translatedValue);

    /// <summary>
    /// Creates a date range condition: field >= start AND field &lt;= end
    /// </summary>
    public static SoqlCondition DateBetween(string field, DateTime startDate, DateTime endDate)
        => new DateBetweenCondition(field, startDate, endDate);

    /// <summary>
    /// Creates a date range condition for date-only fields.
    /// </summary>
    public static SoqlCondition DateBetween(string field, DateOnly startDate, DateOnly endDate)
        => new DateOnlyBetweenCondition(field, startDate, endDate);

    /// <summary>
    /// Combines conditions with AND.
    /// </summary>
    public static SoqlCondition And(params SoqlCondition[] conditions)
        => new CompoundCondition(conditions, "AND");

    /// <summary>
    /// Combines conditions with OR.
    /// </summary>
    public static SoqlCondition Or(params SoqlCondition[] conditions)
        => new CompoundCondition(conditions, "OR");

    /// <summary>
    /// Negates a condition with NOT.
    /// </summary>
    public static SoqlCondition Not(SoqlCondition condition)
        => new NotCondition(condition);

    protected static string FormatDateTimeUtc(DateTime dateTime)
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

    protected static string FormatDateTimeOffsetUtc(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the Salesforce API value for an enum member.
    /// Uses SalesforceValueAttribute if present, otherwise falls back to the enum member name.
    /// </summary>
    protected static string GetEnumSalesforceValue(Enum e)
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
/// Comparison operators for SOQL conditions.
/// </summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

#region Condition Implementations

internal sealed class ComparisonCondition : SoqlCondition
{
    private readonly string _field;
    private readonly string _operator;
    private readonly object? _value;

    public ComparisonCondition(string field, string op, object? value)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _operator = op;
        _value = value;
    }

    internal override string Render()
    {
        return $"{_field} {_operator} {FormatValue(_value)}";
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
}

internal sealed class NullCondition : SoqlCondition
{
    private readonly string _field;
    private readonly bool _isNull;

    public NullCondition(string field, bool isNull)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _isNull = isNull;
    }

    internal override string Render()
    {
        return _isNull ? $"{_field} = NULL" : $"{_field} != NULL";
    }
}

internal sealed class LikeCondition : SoqlCondition
{
    private readonly string _field;
    private readonly string _pattern;

    public LikeCondition(string field, string pattern)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _pattern = SecurityUtils.SanitizeSoql(pattern).Replace("\\", "\\\\");
    }

    internal override string Render()
    {
        return $"{_field} LIKE '{_pattern}'";
    }
}

internal sealed class InCondition : SoqlCondition
{
    private readonly string _field;
    private readonly IEnumerable<object?> _values;
    private readonly bool _negated;

    public InCondition(string field, IEnumerable<object?> values, bool negated)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _negated = negated;
    }

    internal override string Render()
    {
        var valueList = _values.ToList();
        if (valueList.Count == 0)
        {
            return _negated ? "Id != NULL" : "Id = NULL";
        }

        var formattedValues = string.Join(", ", valueList.Select(FormatValue));
        var op = _negated ? "NOT IN" : "IN";
        return $"{_field} {op} ({formattedValues})";
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
}

internal sealed class MultiSelectCondition : SoqlCondition
{
    private readonly string _field;
    private readonly IEnumerable<string> _values;
    private readonly bool _includes;

    public MultiSelectCondition(string field, IEnumerable<string> values, bool includes)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _includes = includes;
    }

    internal override string Render()
    {
        var valueList = _values.ToList();
        if (valueList.Count == 0)
        {
            return _includes ? "Id = NULL" : "Id != NULL";
        }

        var formattedValues = string.Join(";", valueList.Select(SecurityUtils.SanitizeForSoql));
        var op = _includes ? "INCLUDES" : "EXCLUDES";
        return $"{_field} {op} ('{formattedValues}')";
    }
}

internal sealed class DateLiteralCondition : SoqlCondition
{
    private readonly string _field;
    private readonly string _operator;
    private readonly DateLiteral _literal;

    public DateLiteralCondition(string field, string op, DateLiteral literal)
    {
        if (DateLiteralHelper.RequiresParameter(literal))
        {
            throw new ArgumentException($"Date literal {literal} requires a parameter. Use DateLiteralN instead.", nameof(literal));
        }

        _field = SecurityUtils.SanitizeFieldName(field);
        _operator = op;
        _literal = literal;
    }

    internal override string Render()
    {
        return $"{_field} {_operator} {_literal}";
    }
}

internal sealed class DateLiteralNCondition : SoqlCondition
{
    private readonly string _field;
    private readonly string _literalExpression;

    public DateLiteralNCondition(string field, string literalExpression)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        // Validate the literal expression format (e.g., LAST_N_DAYS:30)
        if (!IsValidDateLiteralNExpression(literalExpression))
        {
            throw new ArgumentException($"Invalid date literal expression: {literalExpression}", nameof(literalExpression));
        }
        _literalExpression = literalExpression;
    }

    internal override string Render()
    {
        return $"{_field} = {_literalExpression}";
    }

    internal static bool IsValidDateLiteralNExpression(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        // Valid patterns: LAST_N_DAYS:n, NEXT_N_DAYS:n, etc.
        var validPrefixes = new[]
        {
            "LAST_N_DAYS:", "NEXT_N_DAYS:", "LAST_N_WEEKS:", "NEXT_N_WEEKS:",
            "LAST_N_MONTHS:", "NEXT_N_MONTHS:", "LAST_N_QUARTERS:", "NEXT_N_QUARTERS:",
            "LAST_N_YEARS:", "NEXT_N_YEARS:", "LAST_N_FISCAL_QUARTERS:", "NEXT_N_FISCAL_QUARTERS:",
            "LAST_N_FISCAL_YEARS:", "NEXT_N_FISCAL_YEARS:", "N_DAYS_AGO:", "N_WEEKS_AGO:",
            "N_MONTHS_AGO:", "N_QUARTERS_AGO:", "N_YEARS_AGO:", "N_FISCAL_QUARTERS_AGO:",
            "N_FISCAL_YEARS_AGO:"
        };

        foreach (var prefix in validPrefixes)
        {
            if (expr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var numberPart = expr.Substring(prefix.Length);
                return int.TryParse(numberPart, out var n) && n > 0;
            }
        }

        return false;
    }
}

internal sealed class DateFunctionCondition : SoqlCondition
{
    private readonly DateFunction _function;
    private readonly string _field;
    private readonly string _operator;
    private readonly int _value;
    private readonly bool _convertTimezone;

    public DateFunctionCondition(DateFunction function, string field, string op, int value, bool convertTimezone)
    {
        _function = function;
        _field = SecurityUtils.SanitizeFieldName(field);
        _operator = op;
        _value = value;
        _convertTimezone = convertTimezone;
    }

    internal override string Render()
    {
        var fieldExpr = _convertTimezone ? $"convertTimezone({_field})" : _field;
        return $"{_function}({fieldExpr}) {_operator} {_value}";
    }
}

internal sealed class ToLabelCondition : SoqlCondition
{
    private readonly string _field;
    private readonly string _translatedValue;

    public ToLabelCondition(string field, string translatedValue)
    {
        _field = SecurityUtils.SanitizeFieldName(field);
        _translatedValue = SecurityUtils.SanitizeForSoql(translatedValue);
    }

    internal override string Render()
    {
        return $"toLabel({_field}) = '{_translatedValue}'";
    }
}

internal sealed class DateBetweenCondition : SoqlCondition
{
    private readonly string _field;
    private readonly DateTime _startDate;
    private readonly DateTime _endDate;

    public DateBetweenCondition(string field, DateTime startDate, DateTime endDate)
    {
        _field = SecurityUtils.SanitizeFieldName(field);

        // Auto-swap dates if inverted to prevent always-empty results (defensive)
        if (startDate > endDate)
        {
            _startDate = endDate;
            _endDate = startDate;
        }
        else
        {
            _startDate = startDate;
            _endDate = endDate;
        }
    }

    internal override string Render()
    {
        var start = FormatDateTimeUtc(_startDate);
        var end = FormatDateTimeUtc(_endDate);
        // Wrap in parentheses to ensure correct precedence in compound conditions
        return $"({_field} >= {start} AND {_field} <= {end})";
    }
}

internal sealed class DateOnlyBetweenCondition : SoqlCondition
{
    private readonly string _field;
    private readonly DateOnly _startDate;
    private readonly DateOnly _endDate;

    public DateOnlyBetweenCondition(string field, DateOnly startDate, DateOnly endDate)
    {
        _field = SecurityUtils.SanitizeFieldName(field);

        // Auto-swap dates if inverted to prevent always-empty results (defensive)
        if (startDate > endDate)
        {
            _startDate = endDate;
            _endDate = startDate;
        }
        else
        {
            _startDate = startDate;
            _endDate = endDate;
        }
    }

    internal override string Render()
    {
        var start = _startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = _endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Wrap in parentheses to ensure correct precedence in compound conditions
        return $"({_field} >= {start} AND {_field} <= {end})";
    }
}

internal sealed class CompoundCondition : SoqlCondition
{
    private readonly SoqlCondition[] _conditions;
    private readonly string _operator;

    public CompoundCondition(SoqlCondition[] conditions, string op)
    {
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _operator = op;
    }

    internal override string Render()
    {
        var validConditions = _conditions.Where(c => c != null).ToList();
        if (validConditions.Count == 0)
            return string.Empty;

        if (validConditions.Count == 1)
            return validConditions[0].Render();

        var parts = validConditions.Select(c => $"({c.Render()})");
        return $"({string.Join($" {_operator} ", parts)})";
    }
}

internal sealed class NotCondition : SoqlCondition
{
    private readonly SoqlCondition _condition;

    public NotCondition(SoqlCondition condition)
    {
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    internal override string Render()
    {
        return $"NOT ({_condition.Render()})";
    }
}

#endregion
