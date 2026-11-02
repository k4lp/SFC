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
    #region Relationship Sub-Query Extensions

    /// <summary>
    /// Includes a child relationship in the query result (similar to EF Core Include).
    /// Generates a SOQL sub-query.
    /// </summary>
    /// <example>
    /// var accountsWithContacts = await _dataService.Query&lt;Account&gt;()
    ///     .Include("Contacts", "Id", "FirstName", "LastName", "Email")
    ///     .ToListAsync();
    /// </example>
    public static SalesforceQueryable<T> Include<T>(
        this SalesforceQueryable<T> query,
        string relationshipName,
        params string[] fields)
        where T : class, new()
    {
        // This creates a modified query expression that includes the sub-query
        var includeExpr = new IncludeExpressionHolder(relationshipName, fields.ToList());

        // Store in a custom expression that the provider will understand
        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(IncludeInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(
            method,
            query.Expression,
            Expression.Constant(includeExpr));

        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    /// <summary>
    /// Includes a child relationship with a type-safe filter condition.
    /// </summary>
    /// <example>
    /// var accountsWithActiveContacts = await _dataService.Query&lt;Account&gt;()
    ///     .IncludeWhere("Contacts",
    ///         fields: new[] { "Id", "Name", "Email" },
    ///         condition: SoqlCondition.IsNotNull("Email"),
    ///         limit: 5)
    ///     .ToListAsync();
    /// </example>
    public static SalesforceQueryable<T> IncludeWhere<T>(
        this SalesforceQueryable<T> query,
        string relationshipName,
        string[] fields,
        SoqlCondition? condition = null,
        int? limit = null)
        where T : class, new()
    {
        var includeExpr = new IncludeExpressionHolder(relationshipName, fields.ToList())
        {
            Condition = condition,
            Limit = limit
        };

        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(IncludeInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(
            method,
            query.Expression,
            Expression.Constant(includeExpr));

        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    private static IQueryable<T> IncludeInternal<T>(IQueryable<T> source, IncludeExpressionHolder include)
    {
        // This is a marker method - the actual work is done in the query provider
        return source;
    }

    #endregion

    #region Multi-Select Picklist Extensions

    /// <summary>
    /// Filters records where a multi-select picklist includes any of the specified values.
    /// Generates SOQL INCLUDES clause.
    /// </summary>
    /// <example>
    /// var contacts = await _dataService.Query&lt;Contact&gt;()
    ///     .WhereIncludes(c => c.Languages, "English", "Spanish")
    ///     .ToListAsync();
    /// // Generates: WHERE Languages INCLUDES ('English;Spanish')
    /// </example>
    public static SalesforceQueryable<T> WhereIncludes<T>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, string?>> fieldSelector,
        params string[] values)
        where T : class, new()
    {
        var fieldName = GetFieldNameFromSelector(fieldSelector);
        var condition = SoqlCondition.Includes(fieldName, values);
        return query.WhereCondition(condition);
    }

    /// <summary>
    /// Filters records where a multi-select picklist excludes all of the specified values.
    /// Generates SOQL EXCLUDES clause.
    /// </summary>
    /// <example>
    /// var contacts = await _dataService.Query&lt;Contact&gt;()
    ///     .WhereExcludes(c => c.Languages, "French", "German")
    ///     .ToListAsync();
    /// // Generates: WHERE Languages EXCLUDES ('French;German')
    /// </example>
    public static SalesforceQueryable<T> WhereExcludes<T>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, string?>> fieldSelector,
        params string[] values)
        where T : class, new()
    {
        var fieldName = GetFieldNameFromSelector(fieldSelector);
        var condition = SoqlCondition.Excludes(fieldName, values);
        return query.WhereCondition(condition);
    }

    /// <summary>
    /// Adds a type-safe WHERE condition to the query.
    /// Use for complex conditions that cannot be expressed using LINQ.
    /// </summary>
    /// <example>
    /// var results = await _dataService.Query&lt;Account&gt;()
    ///     .WhereCondition(SoqlCondition.DateFunction(DateFunction.CALENDAR_YEAR, "CreatedDate", 2024))
    ///     .ToListAsync();
    /// </example>
    public static SalesforceQueryable<T> WhereCondition<T>(
        this SalesforceQueryable<T> query,
        SoqlCondition condition)
        where T : class, new()
    {
        if (condition == null)
            throw new ArgumentNullException(nameof(condition));

        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(WhereConditionInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(
            method,
            query.Expression,
            Expression.Constant(condition));

        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    private static IQueryable<T> WhereConditionInternal<T>(IQueryable<T> source, SoqlCondition condition)
    {
        return source;
    }

    #endregion

}
