using System.Text.Json.Nodes;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Query;

/// <summary>
/// The minimal SOQL execution surface the LINQ provider needs. <see cref="Services.Data.IDataService"/>
/// extends it, but the query layer depends only on this contract, so Query no longer depends on Data
/// and any executor (a cache, a test fake, a replay log, a different transport) can back a <see cref="SalesforceQueryable{T}"/>.
/// </summary>
public interface ISoqlQueryExecutor
{
    /// <summary>
    /// Executes a raw SOQL query.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with records.</returns>
    Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and automatically fetches all pages, returning all records.
    /// Use with caution for large datasets - consider using QueryAllAsyncEnumerable instead.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All records from all pages.</returns>
    Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and streams records from all pages without loading everything into memory.
    /// Ideal for processing large datasets efficiently.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of records.</returns>
    IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability for executors that can read deleted and archived rows through the REST
/// <c>queryAll</c> resource. <see cref="Services.Data.DataService"/> implements it; the LINQ
/// <c>IncludeDeleted()</c> operator requires it. Kept separate from <see cref="ISoqlQueryExecutor"/>
/// so existing custom executors keep compiling.
/// </summary>
public interface ISoqlDeletedRowsQueryExecutor
{
    /// <summary>
    /// Executes a SOQL query against <c>/queryAll/</c> (includes deleted and archived records). First page only.
    /// </summary>
    Task<QueryResult> QueryIncludingDeletedAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every page of a <c>/queryAll/</c> query, following <c>nextRecordsUrl</c>.
    /// </summary>
    IAsyncEnumerable<JsonObject> QueryAllIncludingDeletedAsyncEnumerable(string soql, CancellationToken cancellationToken = default);
}
