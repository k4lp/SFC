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
    #region Locking Clause Extensions

    /// <summary>
    /// Adds FOR UPDATE to the query for record locking.
    /// Use when you need to lock records to prevent concurrent modifications.
    /// </summary>
    /// <example>
    /// var accounts = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Name == "Acme")
    ///     .ForUpdate()
    ///     .ToListAsync();
    /// // Generates: SELECT Id, Name FROM Account WHERE Name = 'Acme' FOR UPDATE
    /// </example>
    public static SalesforceQueryable<T> ForUpdate<T>(this SalesforceQueryable<T> query)
        where T : class, new()
    {
        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(ForUpdateInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(method, query.Expression);
        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    /// <summary>
    /// Adds FOR VIEW to the query to track recently viewed records.
    /// Updates the LastViewedDate field for queried records.
    /// </summary>
    /// <example>
    /// var accounts = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Industry == "Technology")
    ///     .ForView()
    ///     .ToListAsync();
    /// // Generates: SELECT Id, Name FROM Account WHERE Industry = 'Technology' FOR VIEW
    /// </example>
    public static SalesforceQueryable<T> ForView<T>(this SalesforceQueryable<T> query)
        where T : class, new()
    {
        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(ForViewInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(method, query.Expression);
        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    /// <summary>
    /// Adds FOR REFERENCE to the query to track recently referenced records.
    /// Updates the LastReferencedDate field for queried records.
    /// </summary>
    /// <example>
    /// var accounts = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a => a.Type == "Customer")
    ///     .ForReference()
    ///     .ToListAsync();
    /// // Generates: SELECT Id, Name FROM Account WHERE Type = 'Customer' FOR REFERENCE
    /// </example>
    public static SalesforceQueryable<T> ForReference<T>(this SalesforceQueryable<T> query)
        where T : class, new()
    {
        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(nameof(ForReferenceInternal), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        var call = Expression.Call(method, query.Expression);
        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    private static IQueryable<T> ForUpdateInternal<T>(IQueryable<T> source) => source;
    private static IQueryable<T> ForViewInternal<T>(IQueryable<T> source) => source;
    private static IQueryable<T> ForReferenceInternal<T>(IQueryable<T> source) => source;

    /// <summary>
    /// Returns distinct values for a field using GROUP BY.
    /// SOQL does not support SELECT DISTINCT, so this uses GROUP BY as a workaround.
    /// </summary>
    /// <example>
    /// var industries = await _dataService.Query&lt;Account&gt;()
    ///     .DistinctAsync(a =&gt; a.Industry);
    /// // Generates: SELECT Industry FROM Account GROUP BY Industry
    /// </example>
    public static async Task<List<TKey>> DistinctAsync<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> selector,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var fieldName = SecurityUtils.SanitizeFieldName(GetFieldNameFromSelector(selector));

        // WHERE comes from the parsed expression tree (all Where/WhereCondition calls, not just the last one).
        var provider = (SalesforceQueryProvider)query.Provider;
        var soql = provider.BuildAggregateSoql(query.Expression, new[] { fieldName }, new[] { fieldName });
        var results = await provider.ExecuteAggregateListAsync(soql, provider.UsesQueryAll(query.Expression), cancellationToken);

        return results
            .Select(r => r.Get<TKey>(fieldName))
            .Where(v => v != null)
            .ToList()!;
    }

    /// <summary>
    /// Determines whether all elements satisfy a condition.
    /// Implemented as !Any(negated predicate).
    /// </summary>
    /// <example>
    /// var allActive = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .AllAsync(a =&gt; a.IsActive);
    /// </example>
    public static async Task<bool> AllAsync<T>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        // All(predicate) == !Any(!predicate)
        // We check if any record does NOT satisfy the predicate
        var parameter = predicate.Parameters[0];
        var negatedBody = Expression.Not(predicate.Body);
        var negatedPredicate = Expression.Lambda<Func<T, bool>>(negatedBody, parameter);
        
        // Use Where extension that returns SalesforceQueryable<T>
        var filtered = SalesforceQueryExtensions.Where(query, negatedPredicate);
        var count = await filtered.CountAsync(cancellationToken);
        return count == 0;
    }

    /// <summary>
    /// Returns the last element that satisfies a condition.
    /// Requires an OrderBy clause to define "last".
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the sequence is empty.</exception>
    /// <example>
    /// var lastAccount = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .LastAsync(a =&gt; a.CreatedDate);
    /// </example>
    public static async Task<T> LastAsync<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> orderBy,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var result = await query.OrderByDescending(orderBy).Take(1).FirstOrDefaultAsync(cancellationToken);
        if (result == null)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }
        return result;
    }

    /// <summary>
    /// Returns the last element that satisfies a condition, or default if empty.
    /// Requires an OrderBy clause to define "last".
    /// </summary>
    /// <example>
    /// var lastAccount = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .LastOrDefaultAsync(a =&gt; a.CreatedDate);
    /// </example>
    public static async Task<T?> LastOrDefaultAsync<T, TKey>(
        this SalesforceQueryable<T> query,
        Expression<Func<T, TKey>> orderBy,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        return await query.OrderByDescending(orderBy).Take(1).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the element at a specified index.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is out of range.</exception>
    /// <example>
    /// var fifthAccount = await _dataService.Query&lt;Account&gt;()
    ///     .OrderBy(a =&gt; a.Name)
    ///     .ElementAtAsync(4);
    /// </example>
    public static async Task<T> ElementAtAsync<T>(
        this SalesforceQueryable<T> query,
        int index,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }
        
        var result = await query.Skip(index).Take(1).FirstOrDefaultAsync(cancellationToken);
        if (result == null)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index was out of range.");
        }
        return result;
    }

    /// <summary>
    /// Returns the element at a specified index, or default if the index is out of range.
    /// </summary>
    /// <example>
    /// var fifthAccount = await _dataService.Query&lt;Account&gt;()
    ///     .OrderBy(a =&gt; a.Name)
    ///     .ElementAtOrDefaultAsync(4);
    /// </example>
    public static async Task<T?> ElementAtOrDefaultAsync<T>(
        this SalesforceQueryable<T> query,
        int index,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        if (index < 0)
        {
            return default;
        }
        
        return await query.Skip(index).Take(1).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Produces the set union of two queries (distinct elements from both).
    /// Executes both queries and merges results client-side.
    /// </summary>
    /// <example>
    /// var techOrFinance = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .UnionAsync(_dataService.Query&lt;Account&gt;().Where(a =&gt; a.Industry == "Finance"));
    /// </example>
    public static async Task<List<T>> UnionAsync<T>(
        this SalesforceQueryable<T> first,
        SalesforceQueryable<T> second,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var results1 = await first.ToListAsync(cancellationToken);
        var results2 = await second.ToListAsync(cancellationToken);
        
        return results1.Union(results2, new IdBasedEqualityComparer<T>()).ToList();
    }

    /// <summary>
    /// Concatenates two queries (all elements from both, including duplicates).
    /// Executes both queries and merges results client-side.
    /// </summary>
    /// <example>
    /// var allAccounts = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .ConcatAsync(_dataService.Query&lt;Account&gt;().Where(a =&gt; a.Industry == "Finance"));
    /// </example>
    public static async Task<List<T>> ConcatAsync<T>(
        this SalesforceQueryable<T> first,
        SalesforceQueryable<T> second,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var results1 = await first.ToListAsync(cancellationToken);
        var results2 = await second.ToListAsync(cancellationToken);
        
        return results1.Concat(results2).ToList();
    }

    /// <summary>
    /// Produces the set difference (elements in first but not in second).
    /// Executes both queries and computes difference client-side.
    /// </summary>
    /// <example>
    /// var techNotActive = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .ExceptAsync(_dataService.Query&lt;Account&gt;().Where(a =&gt; a.IsActive == true));
    /// </example>
    public static async Task<List<T>> ExceptAsync<T>(
        this SalesforceQueryable<T> first,
        SalesforceQueryable<T> second,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var results1 = await first.ToListAsync(cancellationToken);
        var results2 = await second.ToListAsync(cancellationToken);
        
        return results1.Except(results2, new IdBasedEqualityComparer<T>()).ToList();
    }

    /// <summary>
    /// Produces the set intersection (elements in both queries).
    /// Executes both queries and computes intersection client-side.
    /// </summary>
    /// <example>
    /// var activeAndTech = await _dataService.Query&lt;Account&gt;()
    ///     .Where(a =&gt; a.Industry == "Technology")
    ///     .IntersectAsync(_dataService.Query&lt;Account&gt;().Where(a =&gt; a.IsActive == true));
    /// </example>
    public static async Task<List<T>> IntersectAsync<T>(
        this SalesforceQueryable<T> first,
        SalesforceQueryable<T> second,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var results1 = await first.ToListAsync(cancellationToken);
        var results2 = await second.ToListAsync(cancellationToken);
        
        return results1.Intersect(results2, new IdBasedEqualityComparer<T>()).ToList();
    }

    #endregion
}
