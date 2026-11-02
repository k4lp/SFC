using System.Linq.Expressions;
using SalesforceCore.Utilities;

namespace SalesforceCore.Query;

public static partial class SalesforceQueryExtensions
{
    /// <summary>
    /// Runs a SOQL aggregate query (GROUP BY / ROLLUP / CUBE, COUNT/SUM/AVG/MIN/MAX/COUNT_DISTINCT, HAVING,
    /// ORDER BY, LIMIT) over this query's Where filters and WITH mode. ORDER BY/Take/Skip of the entity query
    /// are ignored; use the builder's OrderBy/Limit instead.
    /// </summary>
    public static async Task<List<AggregateResult>> AggregateAsync<T>(
        this SalesforceQueryable<T> query,
        Action<SoqlAggregateQuery<T>> configure,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(configure);

        var spec = new SoqlAggregateQuery<T>();
        configure(spec);
        if (spec.SelectItems.Count == 0)
        {
            throw new InvalidOperationException("AggregateAsync needs at least one GroupBy key or aggregate.");
        }

        if (spec.Mode != null && spec.GroupFields.Count == 0)
        {
            throw new InvalidOperationException("Rollup()/Cube() need at least one GroupBy key.");
        }

        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = ToAggregateSoql(query, spec);
        return await provider.ExecuteAggregateListAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);
    }

    /// <summary>
    /// Returns the SOQL that <see cref="AggregateAsync{T}"/> would send, without executing it.
    /// </summary>
    public static string ToAggregateSoql<T>(this SalesforceQueryable<T> query, Action<SoqlAggregateQuery<T>> configure)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(configure);
        var spec = new SoqlAggregateQuery<T>();
        configure(spec);
        return ToAggregateSoql(query, spec);
    }

    private static string ToAggregateSoql<T>(SalesforceQueryable<T> query, SoqlAggregateQuery<T> spec) where T : class, new()
    {
        var provider = (SalesforceQueryProvider)query.Provider;
        return provider.BuildAggregateSoql(
            query.Expression,
            spec.SelectItems,
            spec.GroupFields,
            spec.Mode,
            spec.HavingClause,
            spec.OrderItems,
            spec.LimitValue);
    }

    /// <summary>
    /// Counts distinct non-null values of a field: <c>SELECT COUNT_DISTINCT(Field) cnt FROM ...</c>.
    /// </summary>
    public static async Task<int> CountDistinctAsync<T, TField>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TField>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { $"COUNT_DISTINCT({RenderSelector(selector)}) cnt" });
        var result = await provider.ExecuteAggregateAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);
        return result.Get<int?>("cnt") ?? 0;
    }

    /// <summary>
    /// Renders a selector as a sanitized SOQL field path, or as a SoqlFunctions call (e.g. CALENDAR_YEAR(CreatedDate)).
    /// </summary>
    internal static string RenderSelector<T, TKey>(Expression<Func<T, TKey>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            body = convert.Operand;
        }

        if (body is MethodCallExpression call && call.Method.DeclaringType == typeof(SoqlFunctions))
        {
            return SoqlExpressionVisitor.Translate(call, typeof(T));
        }

        return SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));
    }
}
