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
/// Extension methods for creating Salesforce queryables.
/// </summary>
public static partial class SalesforceQueryExtensions
{
    /// <summary>
    /// Creates a queryable for a Salesforce object type.
    /// </summary>
    public static SalesforceQueryable<T> Query<T>(this ISoqlQueryExecutor dataService) where T : class, new()
    {
        return new SalesforceQueryable<T>(dataService);
    }

    public static SalesforceQueryable<T> Query<T>(this ISoqlQueryExecutor dataService, ILogger<SalesforceQueryProvider> logger)
        where T : class, new()
    {
        return new SalesforceQueryable<T>(dataService, logger);
    }

    /// <summary>
    /// Executes the query and returns results as a list.
    /// Note: Returns only the first page (up to 2000 records). Use ToListAllAsync for all records.
    /// </summary>
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        // No class/new() constraint (v2.2): projections to strings, numbers and anonymous types work too.
        if (query?.Provider is SalesforceQueryProvider provider)
        {
            return provider.ListAny<T>(query.Expression, all: false, cancellationToken);
        }
        throw new InvalidOperationException("Query is not a Salesforce queryable");
    }


    /// <summary>
    /// Executes the query and returns ALL results by automatically following pagination.
    /// Automatically fetches all pages using nextRecordsUrl until all records are retrieved.
    /// Use with caution for large datasets - consider ToAsyncEnumerable() instead.
    /// </summary>
    /// <example>
    /// // Fetch all accounts (handles pagination automatically)
    /// var allAccounts = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Industry == "Technology")
    ///     .ToListAllAsync();
    /// </example>
    public static Task<List<T>> ToListAllAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        // No class/new() constraint (v2.2): projections to strings, numbers and anonymous types work too.
        if (query?.Provider is SalesforceQueryProvider provider)
        {
            return provider.ListAny<T>(query.Expression, all: true, cancellationToken);
        }
        throw new InvalidOperationException("Query is not a Salesforce queryable");
    }


    /// <summary>
    /// Returns an async enumerable that streams ALL records from ALL pages.
    /// Memory-efficient for large datasets as records are yielded one at a time.
    /// </summary>
    /// <example>
    /// // Stream millions of records without loading all into memory
    /// await foreach (var account in _dataService.Query&lt;Account&gt;().ToAsyncEnumerable())
    /// {
    ///     await ProcessAccountAsync(account);
    /// }
    /// </example>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        // No class/new() constraint (v2.2): projections to strings, numbers and anonymous types work too.
        if (query?.Provider is SalesforceQueryProvider provider)
        {
            return provider.StreamAny<T>(query.Expression, cancellationToken);
        }
        throw new InvalidOperationException("Query is not a Salesforce queryable");
    }


    /// <summary>
    /// Applies a WHERE clause and preserves the SalesforceQueryable fluent type.
    /// This avoids fluent chain breaks caused by System.Linq.Queryable.Where returning IQueryable&lt;T&gt;.
    /// </summary>
    public static SalesforceQueryable<T> Where<T>(this SalesforceQueryable<T> query, Expression<Func<T, bool>> predicate)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(predicate);
        return (SalesforceQueryable<T>)System.Linq.Queryable.Where(query, predicate);
    }

    /// <summary>
    /// Applies a SKIP/OFFSET clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> Skip<T>(this SalesforceQueryable<T> query, int count)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        return (SalesforceQueryable<T>)System.Linq.Queryable.Skip(query, count);
    }

    /// <summary>
    /// Applies a TAKE/LIMIT clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> Take<T>(this SalesforceQueryable<T> query, int count)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        return (SalesforceQueryable<T>)System.Linq.Queryable.Take(query, count);
    }

    /// <summary>
    /// Applies an ORDER BY clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> OrderBy<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keySelector);
        return (SalesforceQueryable<T>)System.Linq.Queryable.OrderBy(query, keySelector);
    }

    /// <summary>
    /// Applies an ORDER BY DESC clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> OrderByDescending<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keySelector);
        return (SalesforceQueryable<T>)System.Linq.Queryable.OrderByDescending(query, keySelector);
    }

    /// <summary>
    /// Applies a THEN BY clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> ThenBy<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keySelector);
        return (SalesforceQueryable<T>)System.Linq.Queryable.ThenBy((IOrderedQueryable<T>)query, keySelector);
    }

    /// <summary>
    /// Applies a THEN BY DESC clause and preserves the SalesforceQueryable fluent type.
    /// </summary>
    public static SalesforceQueryable<T> ThenByDescending<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keySelector);
        return (SalesforceQueryable<T>)System.Linq.Queryable.ThenByDescending((IOrderedQueryable<T>)query, keySelector);
    }

    /// <summary>
    /// Sorts the query results by the specified field in ascending order.
    /// This is an alternative to OrderBy() that doesn't require IOrderedQueryable casting.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="query">The queryable.</param>
    /// <param name="keySelector">The field to sort by.</param>
    /// <returns>A sorted queryable.</returns>
    public static SalesforceQueryable<T> SortBy<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        return (SalesforceQueryable<T>)query.OrderBy(keySelector);
    }

    /// <summary>
    /// Sorts the query results by the specified field in descending order.
    /// This is an alternative to OrderByDescending() that doesn't require IOrderedQueryable casting.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="query">The queryable.</param>
    /// <param name="keySelector">The field to sort by.</param>
    /// <returns>A sorted queryable.</returns>
    public static SalesforceQueryable<T> SortByDescending<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        return (SalesforceQueryable<T>)query.OrderByDescending(keySelector);
    }

    /// <summary>
    /// Adds a secondary sort by the specified field in ascending order.
    /// This is an alternative to ThenBy() that doesn't require IOrderedQueryable casting.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="query">The queryable.</param>
    /// <param name="keySelector">The field to sort by.</param>
    /// <returns>A sorted queryable.</returns>
    public static SalesforceQueryable<T> ThenSortBy<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        return (SalesforceQueryable<T>)((IOrderedQueryable<T>)query).ThenBy(keySelector);
    }

    /// <summary>
    /// Adds a secondary sort by the specified field in descending order.
    /// This is an alternative to ThenByDescending() that doesn't require IOrderedQueryable casting.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="query">The queryable.</param>
    /// <param name="keySelector">The field to sort by.</param>
    /// <returns>A sorted queryable.</returns>
    public static SalesforceQueryable<T> ThenSortByDescending<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> keySelector)
        where T : class, new()
    {
        return (SalesforceQueryable<T>)((IOrderedQueryable<T>)query).ThenByDescending(keySelector);
    }

}
