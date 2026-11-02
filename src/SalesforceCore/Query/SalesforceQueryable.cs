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
/// Provides LINQ support for querying Salesforce objects.
/// Translates LINQ expressions to SOQL queries.
/// Implements IOrderedQueryable&lt;T&gt; to support OrderBy/ThenBy operations without casting errors.
/// </summary>
/// <typeparam name="T">The entity type representing the Salesforce object.</typeparam>
public class SalesforceQueryable<T> : IOrderedQueryable<T> where T : class, new()
{
    private readonly SalesforceQueryProvider _provider;
    private readonly Expression _expression;

    /// <summary>
    /// Creates a new Salesforce queryable.
    /// </summary>
    public SalesforceQueryable(ISoqlQueryExecutor dataService)
    {
        _provider = new SalesforceQueryProvider(dataService);
        _expression = Expression.Constant(this);
    }

    public SalesforceQueryable(ISoqlQueryExecutor dataService, ILogger<SalesforceQueryProvider> logger)
    {
        _provider = new SalesforceQueryProvider(dataService, logger);
        _expression = Expression.Constant(this);
    }

    internal SalesforceQueryable(SalesforceQueryProvider provider, Expression expression)
    {
        _provider = provider;
        _expression = expression;
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator()
    {
        return _provider.Execute<IEnumerable<T>>(_expression).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Executes the query asynchronously and returns the results.
    /// Note: This returns only the first batch of results (up to 2000 records).
    /// Use ToListAllAsync() to automatically fetch all pages.
    /// </summary>
    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.ExecuteAsync<T>(_expression, cancellationToken);
    }

    /// <summary>
    /// Executes the query asynchronously and returns ALL results by automatically
    /// following pagination (nextRecordsUrl) until all records are retrieved.
    /// Use with caution for large datasets - consider ToAsyncEnumerable() instead.
    /// </summary>
    public async Task<List<T>> ToListAllAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.ExecuteAllAsync<T>(_expression, cancellationToken);
    }

    /// <summary>
    /// Executes the query asynchronously and returns the first result, or null if no results.
    /// </summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var results = await _provider.ExecuteAsync<T>(_expression, cancellationToken, limit: 1);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Executes the query asynchronously and returns the count of results.
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.ExecuteCountAsync<T>(_expression, cancellationToken);
    }

    /// <summary>
    /// Returns the SOQL query that would be executed (useful for debugging).
    /// </summary>
    public string ToSoql()
    {
        return _provider.GetSoql<T>(_expression);
    }

    /// <summary>
    /// Internal method for ToAsyncEnumerable extension.
    /// </summary>
    internal IAsyncEnumerable<T> ToAsyncEnumerableInternal(CancellationToken cancellationToken)
    {
        return _provider.ExecuteStreamingAsync<T>(_expression, cancellationToken);
    }
}

/// <summary>
/// Queryable for projection results that do not require entity mapping constraints.
/// </summary>
internal class SalesforceProjectionQueryable<T> : IOrderedQueryable<T>
{
    private readonly SalesforceQueryProvider _provider;
    private readonly Expression _expression;

    internal SalesforceProjectionQueryable(SalesforceQueryProvider provider, Expression expression)
    {
        _provider = provider;
        _expression = expression;
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator()
    {
        return _provider.Execute<IEnumerable<T>>(_expression).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
