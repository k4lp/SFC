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
public static partial class SalesforceQueryExtensions
{
    #region Aggregate Extensions

    /// <summary>
    /// Calculates the sum of a numeric field.
    /// </summary>
    /// <example>
    /// var totalRevenue = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Industry == "Technology")
    ///     .SumAsync(a => a.AnnualRevenue);
    /// </example>
    public static async Task<decimal> SumAsync<T, TResult>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var fieldName = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { $"SUM({fieldName}) sum" });
        var result = await provider.ExecuteAggregateAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);

        return result.Get<decimal?>("sum") ?? 0;
    }

    /// <summary>
    /// Calculates the average of a numeric field.
    /// </summary>
    /// <example>
    /// var avgRevenue = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Industry == "Technology")
    ///     .AverageAsync(a => a.AnnualRevenue);
    /// </example>
    public static async Task<decimal> AverageAsync<T, TResult>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var fieldName = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { $"AVG({fieldName}) avg" });
        var result = await provider.ExecuteAggregateAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);

        return result.Get<decimal?>("avg") ?? 0;
    }

    /// <summary>
    /// Gets the minimum value of a field.
    /// </summary>
    /// <example>
    /// var minDate = await _dataService.Query&lt;Opportunity&gt;()
    ///     .Where(o => o.StageName == "Closed Won")
    ///     .MinAsync(o => o.CloseDate);
    /// </example>
    public static async Task<TResult?> MinAsync<T, TResult>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var fieldName = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { $"MIN({fieldName}) min" });
        var result = await provider.ExecuteAggregateAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);

        return result.Get<TResult>("min");
    }

    /// <summary>
    /// Gets the maximum value of a field.
    /// </summary>
    /// <example>
    /// var maxAmount = await _dataService.Query&lt;Opportunity&gt;()
    ///     .Where(o => o.StageName == "Prospecting")
    ///     .MaxAsync(o => o.Amount);
    /// </example>
    public static async Task<TResult?> MaxAsync<T, TResult>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var fieldName = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { $"MAX({fieldName}) max" });
        var result = await provider.ExecuteAggregateAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);

        return result.Get<TResult>("max");
    }

    /// <summary>
    /// Groups records by the specified field and returns aggregate results.
    /// </summary>
    /// <example>
    /// var byIndustry = await _dataService.Query&lt;Account&gt;()
    ///     .GroupByAsync(
    ///         a => a.Industry,
    ///         g => new {
    ///             Industry = g.Key,
    ///             Count = g.Count(),
    ///             TotalRevenue = g.Sum(a => a.AnnualRevenue)
    ///         });
    /// </example>
    public static async Task<List<AggregateResult>> GroupByFieldAsync<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var groupField = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(keySelector));
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { groupField, "COUNT(Id) cnt" }, new[] { groupField });
        return await provider.ExecuteAggregateListAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);
    }

    /// <summary>
    /// Groups records and performs multiple aggregate operations.
    /// </summary>
    /// <example>
    /// var results = await _dataService.Query&lt;Opportunity&gt;()
    ///     .Where(o => o.IsClosed)
    ///     .GroupByWithAggregatesAsync(
    ///         o => o.StageName,
    ///         new AggregateSpec("Amount", AggregateFunction.Sum, "TotalAmount"),
    ///         new AggregateSpec("Amount", AggregateFunction.Avg, "AvgAmount"),
    ///         new AggregateSpec("Id", AggregateFunction.Count, "Count"));
    /// </example>
    public static async Task<List<AggregateResult>> GroupByWithAggregatesAsync<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector,
        CancellationToken cancellationToken = default,
        params AggregateSpec[] aggregates)
        where T : class, new()
    {
        var groupField = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(keySelector));
        var selectParts = new List<string> { groupField };
        foreach (var agg in aggregates)
        {
            var func = NormalizeAggregateFunction(agg.Function);
            var field = SecurityUtils.SanitizeFieldName(agg.Field);
            var aggExpr = $"{func}({field})";
            if (!string.IsNullOrWhiteSpace(agg.Alias))
            {
                aggExpr += $" {SecurityUtils.SanitizeFieldName(agg.Alias)}";
            }
            selectParts.Add(aggExpr);
        }

        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, selectParts, new[] { groupField });
        return await provider.ExecuteAggregateListAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);
    }

    private static string GetFieldNameFromSelector<T, TResult>(Expression<Func<T, TResult>> selector)
    {
        if (TryGetMemberExpression(selector.Body, out var memberExpr))
        {
            return GetFieldPathFromMember(memberExpr, typeof(T));
        }

        throw new NotSupportedException("Cannot extract field name from selector expression");
    }

    private static bool TryGetMemberExpression(Expression expression, out MemberExpression memberExpression)
    {
        if (expression is MemberExpression member)
        {
            memberExpression = member;
            return true;
        }

        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert &&
            unary.Operand is MemberExpression unaryMember)
        {
            memberExpression = unaryMember;
            return true;
        }

        memberExpression = null!;
        return false;
    }

    private static string GetFieldPathFromMember(MemberExpression memberExpr, Type rootType)
    {
        var parts = new List<string>();
        Expression? current = memberExpr;

        while (current is MemberExpression member)
        {
            if (member.Member.Name == "Value" && member.Expression != null && Nullable.GetUnderlyingType(member.Expression.Type) != null)
            {
                current = member.Expression; // x.Date.Value is the field x.Date
                continue;
            }

            if (member.Member.DeclaringType?.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                throw new NotSupportedException(
                    $"'{member}' is a .NET member, not a Salesforce field; use the field itself or a SoqlFunctions date function.");
            }

            parts.Insert(0, GetFieldNameFromMember(member.Member, member.Member.DeclaringType ?? rootType));
            current = member.Expression;

            if (current is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                current = unary.Operand;
            }
        }

        if (current is ParameterExpression)
        {
            return string.Join(".", parts);
        }

        throw new NotSupportedException("Member access must be rooted on the query parameter.");
    }

    private static string GetFieldNameFromMember(MemberInfo member, Type entityType)
    {
        var fieldAttr = member.GetCustomAttribute(typeof(Attributes.SalesforceFieldAttribute), true)
            as Attributes.SalesforceFieldAttribute;

        return fieldAttr?.FieldName ?? SalesforceMapper.GetFieldName(entityType, member.Name);
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

    #endregion

}
