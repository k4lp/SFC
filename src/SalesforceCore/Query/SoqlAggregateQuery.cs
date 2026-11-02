using System.Globalization;
using System.Linq.Expressions;

namespace SalesforceCore.Query;

/// <summary>
/// Fluent description of a SOQL aggregate query (GROUP BY [ROLLUP|CUBE], aggregates, HAVING, ORDER BY, LIMIT)
/// built on top of a LINQ query's filters. Used with
/// <see cref="SalesforceQueryExtensions.AggregateAsync{T}(SalesforceQueryable{T}, Action{SoqlAggregateQuery{T}}, CancellationToken)"/>.
/// Every field is resolved from an expression and sanitized; no free-form SOQL is accepted.
/// </summary>
/// <example>
/// <code>
/// var rows = await data.Query&lt;Opportunity&gt;()
///     .Where(o =&gt; o.IsClosed)
///     .AggregateAsync(g =&gt; g
///         .GroupBy(o =&gt; o.StageName)
///         .Count("cnt")
///         .Sum(o =&gt; o.Amount, "total")
///         .Having(AggregateFunction.Sum, o =&gt; o.Amount, "&gt;", 10000)
///         .OrderByAggregate(AggregateFunction.Sum, o =&gt; o.Amount, descending: true)
///         .Limit(10));
/// // SELECT StageName, COUNT(Id) cnt, SUM(Amount) total FROM Opportunity WHERE (IsClosed = TRUE)
/// //   GROUP BY StageName HAVING SUM(Amount) &gt; 10000 ORDER BY SUM(Amount) DESC LIMIT 10
/// </code>
/// </example>
public sealed class SoqlAggregateQuery<T> where T : class, new()
{
    private static readonly HashSet<string> AllowedOperators = new(StringComparer.Ordinal) { "=", "!=", "<", "<=", ">", ">=" };

    private readonly List<string> _groupBy = new();
    private readonly List<string> _select = new();
    private readonly List<string> _having = new();
    private readonly List<string> _orderBy = new();
    private string? _mode;
    private int? _limit;

    internal IReadOnlyList<string> GroupFields => _groupBy;
    internal IReadOnlyList<string> SelectItems => _select;
    internal string? Mode => _mode;
    internal string? HavingClause => _having.Count == 0 ? null : string.Join(" AND ", _having);
    internal IReadOnlyList<string> OrderItems => _orderBy;
    internal int? LimitValue => _limit;

    /// <summary>Adds a GROUP BY key (field, relationship path or SoqlFunctions date function) and selects it.</summary>
    public SoqlAggregateQuery<T> GroupBy<TKey>(Expression<Func<T, TKey>> key, string? alias = null)
    {
        var rendered = SalesforceQueryExtensions.RenderSelector(key);
        _groupBy.Add(rendered);
        _select.Add(alias == null ? rendered : $"{rendered} {Utilities.SecurityUtils.SanitizeFieldName(alias)}");
        return this;
    }

    /// <summary>Uses <c>GROUP BY ROLLUP(...)</c> for subtotals. Use <c>GROUPING(field)</c> semantics via null keys in results.</summary>
    public SoqlAggregateQuery<T> Rollup() { _mode = "ROLLUP"; return this; }

    /// <summary>Uses <c>GROUP BY CUBE(...)</c> for subtotals across every key combination.</summary>
    public SoqlAggregateQuery<T> Cube() { _mode = "CUBE"; return this; }

    /// <summary>Adds <c>COUNT(Id) alias</c>.</summary>
    public SoqlAggregateQuery<T> Count(string alias = "cnt") => AddAggregate("COUNT", "Id", alias);

    /// <summary>Adds <c>COUNT(field) alias</c> (non-null values).</summary>
    public SoqlAggregateQuery<T> Count<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("COUNT", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>COUNT_DISTINCT(field) alias</c>.</summary>
    public SoqlAggregateQuery<T> CountDistinct<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("COUNT_DISTINCT", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>SUM(field) alias</c>.</summary>
    public SoqlAggregateQuery<T> Sum<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("SUM", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>AVG(field) alias</c>.</summary>
    public SoqlAggregateQuery<T> Average<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("AVG", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>MIN(field) alias</c>.</summary>
    public SoqlAggregateQuery<T> Min<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("MIN", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>MAX(field) alias</c>.</summary>
    public SoqlAggregateQuery<T> Max<TField>(Expression<Func<T, TField>> field, string alias) => AddAggregate("MAX", SalesforceQueryExtensions.RenderSelector(field), alias);

    /// <summary>Adds <c>HAVING COUNT(Id) op value</c>. Several Having calls are ANDed.</summary>
    public SoqlAggregateQuery<T> HavingCount(string op, long value)
    {
        _having.Add($"COUNT(Id) {ValidateOperator(op)} {value.ToString(CultureInfo.InvariantCulture)}");
        return this;
    }

    /// <summary>Adds <c>HAVING FN(field) op value</c>. Several Having calls are ANDed.</summary>
    public SoqlAggregateQuery<T> Having<TField>(AggregateFunction function, Expression<Func<T, TField>> field, string op, decimal value)
    {
        _having.Add($"{FunctionName(function)}({SalesforceQueryExtensions.RenderSelector(field)}) {ValidateOperator(op)} {value.ToString(CultureInfo.InvariantCulture)}");
        return this;
    }

    /// <summary>Orders by a group key: <c>ORDER BY Field [DESC]</c>.</summary>
    public SoqlAggregateQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> key, bool descending = false)
    {
        _orderBy.Add($"{SalesforceQueryExtensions.RenderSelector(key)} {(descending ? "DESC" : "ASC")}");
        return this;
    }

    /// <summary>Orders by an aggregate: <c>ORDER BY SUM(Amount) DESC</c> (SOQL does not accept aliases here).</summary>
    public SoqlAggregateQuery<T> OrderByAggregate<TField>(AggregateFunction function, Expression<Func<T, TField>> field, bool descending = false)
    {
        _orderBy.Add($"{FunctionName(function)}({SalesforceQueryExtensions.RenderSelector(field)}) {(descending ? "DESC" : "ASC")}");
        return this;
    }

    /// <summary>Orders by <c>COUNT(Id)</c>.</summary>
    public SoqlAggregateQuery<T> OrderByCount(bool descending = false)
    {
        _orderBy.Add($"COUNT(Id) {(descending ? "DESC" : "ASC")}");
        return this;
    }

    /// <summary>Adds <c>LIMIT n</c>.</summary>
    public SoqlAggregateQuery<T> Limit(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "LIMIT must be at least 1.");
        _limit = count;
        return this;
    }

    private SoqlAggregateQuery<T> AddAggregate(string function, string field, string alias)
    {
        _select.Add($"{function}({field}) {Utilities.SecurityUtils.SanitizeFieldName(alias)}");
        return this;
    }

    private static string ValidateOperator(string op)
    {
        var trimmed = op?.Trim() ?? string.Empty;
        if (!AllowedOperators.Contains(trimmed))
            throw new ArgumentException($"HAVING operator '{op}' is not allowed. Use one of: = != < <= > >=.", nameof(op));
        return trimmed;
    }

    private static string FunctionName(AggregateFunction function) => function switch
    {
        AggregateFunction.Count => "COUNT",
        AggregateFunction.CountDistinct => "COUNT_DISTINCT",
        AggregateFunction.Sum => "SUM",
        AggregateFunction.Avg => "AVG",
        AggregateFunction.Min => "MIN",
        AggregateFunction.Max => "MAX",
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, null)
    };
}
