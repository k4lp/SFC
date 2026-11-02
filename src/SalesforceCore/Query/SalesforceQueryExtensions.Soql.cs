using System.Linq.Expressions;
using System.Reflection;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Query;

/// <summary>
/// v2.2 LINQ operators: NULLS FIRST/LAST, WITH security modes, queryAll (deleted rows),
/// async terminal operators for any Salesforce queryable (including projections), and ToSoql for projections.
/// </summary>
public static partial class SalesforceQueryExtensions
{
    private static IOrderedQueryable<T> NullsFirstInternal<T>(IOrderedQueryable<T> source) => source;
    private static IOrderedQueryable<T> NullsLastInternal<T>(IOrderedQueryable<T> source) => source;
    private static IQueryable<T> WithSecurityEnforcedInternal<T>(IQueryable<T> source) => source;
    private static IQueryable<T> WithUserModeInternal<T>(IQueryable<T> source) => source;
    private static IQueryable<T> WithSystemModeInternal<T>(IQueryable<T> source) => source;
    private static IQueryable<T> IncludeDeletedInternal<T>(IQueryable<T> source) => source;

    private static SalesforceQueryable<T> ApplyMarker<T>(SalesforceQueryable<T> query, string markerName)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(query);
        var method = typeof(SalesforceQueryExtensions)
            .GetMethod(markerName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));
        var source = query.Expression;
        var expected = method.GetParameters()[0].ParameterType;
        if (!expected.IsAssignableFrom(source.Type))
        {
            if (expected == typeof(IOrderedQueryable<T>))
                throw new NotSupportedException("NullsFirst()/NullsLast() must follow OrderBy/ThenBy.");
            source = Expression.Convert(source, expected);
        }
        return (SalesforceQueryable<T>)query.Provider.CreateQuery<T>(Expression.Call(method, source));
    }

    /// <summary>
    /// Sorts nulls first for the preceding OrderBy/ThenBy key: <c>ORDER BY Field ASC NULLS FIRST</c>.
    /// </summary>
    public static SalesforceQueryable<T> NullsFirst<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(NullsFirstInternal));

    /// <summary>
    /// Sorts nulls last for the preceding OrderBy/ThenBy key: <c>ORDER BY Field DESC NULLS LAST</c>.
    /// </summary>
    public static SalesforceQueryable<T> NullsLast<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(NullsLastInternal));

    /// <summary>
    /// Adds <c>WITH SECURITY_ENFORCED</c> (field- and object-level security checked by Salesforce).
    /// Applies to the entity query and to Count/Any/aggregate queries built from it.
    /// </summary>
    public static SalesforceQueryable<T> WithSecurityEnforced<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(WithSecurityEnforcedInternal));

    /// <summary>
    /// Adds <c>WITH USER_MODE</c> (sharing, FLS and CRUD of the running user enforced).
    /// </summary>
    public static SalesforceQueryable<T> WithUserMode<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(WithUserModeInternal));

    /// <summary>
    /// Adds <c>WITH SYSTEM_MODE</c>.
    /// </summary>
    public static SalesforceQueryable<T> WithSystemMode<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(WithSystemModeInternal));

    /// <summary>
    /// Runs the query through the REST <c>queryAll</c> resource so deleted (recycle bin) and archived rows are returned.
    /// Filter with <c>Where(x =&gt; x.IsDeleted)</c> to get only deleted rows. Requires an executor implementing
    /// <see cref="ISoqlDeletedRowsQueryExecutor"/> (the built-in DataService does).
    /// </summary>
    public static SalesforceQueryable<T> IncludeDeleted<T>(this SalesforceQueryable<T> query) where T : class, new()
        => ApplyMarker(query, nameof(IncludeDeletedInternal));

    // ---- SOQL inspection ---------------------------------------------------------------------

    /// <summary>
    /// Returns the SOQL a Salesforce LINQ query (entity or projection) would send. Nothing is executed.
    /// </summary>
    public static string ToSoql<T>(this IQueryable<T> query) => RequireProvider(query).GetSoql(query.Expression);

    // ---- Async terminal operators ------------------------------------------------------------

    /// <summary>Asynchronously returns whether any row matches (<c>SELECT COUNT() ... LIMIT 1</c>).</summary>
    public static Task<bool> AnyAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, bool>(query, nameof(Queryable.Any), null, cancellationToken);

    /// <summary>Asynchronously returns whether any row matches the predicate.</summary>
    public static Task<bool> AnyAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, bool>(query, nameof(Queryable.Any), predicate, cancellationToken);

    /// <summary>Asynchronously counts rows matching the predicate (<c>SELECT COUNT()</c>; no rows downloaded).</summary>
    public static Task<int> CountAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, int>(query, nameof(Queryable.Count), predicate, cancellationToken);

    /// <summary>Asynchronously counts rows as a <see cref="long"/>.</summary>
    public static Task<long> LongCountAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, long>(query, nameof(Queryable.LongCount), null, cancellationToken);

    /// <summary>Asynchronously counts rows matching the predicate as a <see cref="long"/>.</summary>
    public static Task<long> LongCountAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, long>(query, nameof(Queryable.LongCount), predicate, cancellationToken);

    /// <summary>Asynchronously returns the first row (<c>LIMIT 1</c>); throws when there is none.</summary>
    public static Task<T> FirstAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T>(query, nameof(Queryable.First), null, cancellationToken);

    /// <summary>Asynchronously returns the first row matching the predicate; throws when there is none.</summary>
    public static Task<T> FirstAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T>(query, nameof(Queryable.First), predicate, cancellationToken);

    /// <summary>Asynchronously returns the first row matching the predicate, or default.</summary>
    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T?>(query, nameof(Queryable.FirstOrDefault), predicate, cancellationToken);

    /// <summary>Asynchronously returns the only row (<c>LIMIT 2</c>); throws when there are zero or several.</summary>
    public static Task<T> SingleAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T>(query, nameof(Queryable.Single), null, cancellationToken);

    /// <summary>Asynchronously returns the only row matching the predicate.</summary>
    public static Task<T> SingleAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T>(query, nameof(Queryable.Single), predicate, cancellationToken);

    /// <summary>Asynchronously returns the only row or default; throws when there are several.</summary>
    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T?>(query, nameof(Queryable.SingleOrDefault), null, cancellationToken);

    /// <summary>Asynchronously returns the only row matching the predicate, or default.</summary>
    public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => ExecuteTerminal<T, T?>(query, nameof(Queryable.SingleOrDefault), predicate, cancellationToken);

    private static SalesforceQueryProvider RequireProvider<T>(IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Provider as SalesforceQueryProvider
            ?? throw new NotSupportedException("This operator works only on queries created with Query<T>() (Salesforce LINQ provider).");
    }

    private static bool IsPredicateParameter(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>) &&
        type.GetGenericArguments()[0] is { IsGenericType: true } func &&
        func.GetGenericTypeDefinition() == typeof(Func<,>);

    private static Task<TResult> ExecuteTerminal<T, TResult>(
        IQueryable<T> query,
        string operatorName,
        Expression<Func<T, bool>>? predicate,
        CancellationToken cancellationToken)
    {
        var provider = RequireProvider(query);
        var parameterCount = predicate == null ? 1 : 2;
        var method = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == operatorName && m.IsGenericMethodDefinition && m.GetParameters().Length == parameterCount &&
                        (predicate == null || IsPredicateParameter(m.GetParameters()[1].ParameterType)))
            .MakeGenericMethod(typeof(T));
        var call = predicate == null
            ? Expression.Call(method, query.Expression)
            : Expression.Call(method, query.Expression, Expression.Quote(predicate));
        return provider.ExecuteScalarAsync<TResult>(call, cancellationToken);
    }
}
