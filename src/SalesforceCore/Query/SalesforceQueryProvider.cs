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
/// LINQ query provider that translates expressions to SOQL.
/// </summary>
public class SalesforceQueryProvider : IQueryProvider
{
    private readonly ISoqlQueryExecutor _dataService;
    private readonly ILogger<SalesforceQueryProvider> _logger;

    public SalesforceQueryProvider(ISoqlQueryExecutor dataService, ILogger<SalesforceQueryProvider>? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? NullLogger<SalesforceQueryProvider>.Instance;
    }

    private static Type GetSequenceElementType(Type type)
    {
        var elementType = TryGetSequenceElementType(type);
        if (elementType == null)
        {
            throw new NotSupportedException($"Cannot determine element type for expression type '{type}'.");
        }
        return elementType;
    }

    private static Type? TryGetSequenceElementType(Type type)
    {
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IQueryable<>) || genericDef == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        var interfaceMatch = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 (i.GetGenericTypeDefinition() == typeof(IQueryable<>) ||
                                  i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));

        return interfaceMatch?.GetGenericArguments()[0];
    }

    private static Type GetRootElementType(Expression expression)
    {
        var current = expression;
        while (current is MethodCallExpression methodCall && methodCall.Arguments.Count > 0)
        {
            current = methodCall.Arguments[0];
        }

        var elementType = TryGetSequenceElementType(current.Type) ?? TryGetSequenceElementType(expression.Type);
        if (elementType == null)
        {
            throw new NotSupportedException($"Cannot determine root element type for expression '{expression}'.");
        }

        return elementType;
    }

    private static bool IsMappableEntityType(Type type)
    {
        return type.IsClass && type.GetConstructor(Type.EmptyTypes) != null;
    }

    private static bool ContainsQueryableSelect(Expression expression)
    {
        // Walk only the source chain (argument 0 of each operator). A Select nested inside a predicate lambda
        // (an inline semi-join: Where(a => sub.Select(c => c.AccountId).Contains(a.Id))) is not a projection
        // of this query.
        var current = expression;
        while (current is MethodCallExpression call)
        {
            if (call.Method.DeclaringType == typeof(Queryable) &&
                string.Equals(call.Method.Name, "Select", StringComparison.Ordinal))
            {
                return true;
            }

            if (call.Arguments.Count == 0) break;
            current = call.Arguments[0];
        }

        return false;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = GetSequenceElementType(expression.Type);
        return CreateQueryable(elementType, expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return (IQueryable<TElement>)CreateQueryable(typeof(TElement), expression);
    }

    private IQueryable CreateQueryable(Type elementType, Expression expression)
    {
        if (ContainsQueryableSelect(expression))
        {
            var projectionType = typeof(SalesforceProjectionQueryable<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(projectionType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { this, expression },
                null)!;
        }

        if (!IsMappableEntityType(elementType))
        {
            _logger.LogWarning("Cannot create Salesforce queryable for non-entity type {Type}", elementType);
            throw new NotSupportedException(
                $"Cannot create queryable for type {elementType.Name}. Type must be a class with a parameterless constructor.");
        }

        var queryableType = typeof(SalesforceQueryable<>).MakeGenericType(elementType);
        return (IQueryable)Activator.CreateInstance(queryableType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new object[] { this, expression },
            null)!;
    }

    public object? Execute(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var sequenceElementType = TryGetSequenceElementType(expression.Type);
        if (sequenceElementType != null)
        {
            var sourceEntityType = GetRootElementType(expression);
            return RunSync(() => ExecuteListAsync(
                expression,
                sequenceElementType,
                sourceEntityType,
                CancellationToken.None,
                limit: null));
        }

        return RunSync(() => ExecuteInternalAsync<object>(expression, CancellationToken.None));
    }

    public TResult Execute<TResult>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return RunSync(() => ExecuteInternalAsync<TResult>(expression, CancellationToken.None));
    }

    /// <summary>
    /// <see cref="IQueryProvider"/> is synchronous by contract. Prefer the *Async operators; when the sync path is used
    /// under a SynchronizationContext (WinForms/WPF/legacy ASP.NET) the work is offloaded to the thread pool so the
    /// blocking wait cannot deadlock on the captured context. Exceptions are rethrown unwrapped.
    /// </summary>
    internal static T RunSync<T>(Func<Task<T>> work)
    {
        if (SynchronizationContext.Current is null && TaskScheduler.Current == TaskScheduler.Default)
        {
            return work().GetAwaiter().GetResult();
        }

        return Task.Run(work).GetAwaiter().GetResult();
    }

    public async Task<List<T>> ExecuteAsync<T>(Expression expression, CancellationToken cancellationToken, int? limit = null)
        where T : class, new()
    {
        var sourceEntityType = GetRootElementType(expression);
        return await ExecuteAsyncInternal<T>(expression, cancellationToken, limit, sourceEntityType);
    }

    /// <summary>
    /// Executes the query and returns ALL records by automatically following pagination.
    /// </summary>
    public async Task<List<T>> ExecuteAllAsync<T>(Expression expression, CancellationToken cancellationToken)
        where T : class, new()
    {
        var entityType = GetRootElementType(expression);
        var data = ParseExpression(expression, entityType);
        if (data.Limit == 0)
        {
            return new List<T>();
        }

        var allRecords = await RunQueryAllAsync(BuildSoql(data, entityType), data.IncludeDeleted, cancellationToken);
        return SalesforceMapper.FromSalesforce<T>(allRecords);
    }

    /// <summary>
    /// Executes the query and streams ALL records from ALL pages.
    /// </summary>
    public async IAsyncEnumerable<T> ExecuteStreamingAsync<T>(
        Expression expression,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class, new()
    {
        var entityType = GetRootElementType(expression);
        var data = ParseExpression(expression, entityType);
        if (data.Limit == 0)
        {
            yield break;
        }

        var soql = BuildSoql(data, entityType);

        await foreach (var record in RunQueryStreamAsync(soql, data.IncludeDeleted, cancellationToken))
        {
            yield return SalesforceMapper.FromSalesforce<T>(record);
        }
    }

    public async Task<int> ExecuteCountAsync<T>(Expression expression, CancellationToken cancellationToken)
        where T : class, new()
    {
        return await ExecuteCountAsyncInternal(expression, GetRootElementType(expression), cancellationToken);
    }

    /// <summary>
    /// Executes an aggregate query and returns a single result.
    /// </summary>
    public Task<AggregateResult> ExecuteAggregateAsync(string soql, CancellationToken cancellationToken)
        => ExecuteAggregateAsync(soql, includeDeleted: false, cancellationToken);

    internal async Task<AggregateResult> ExecuteAggregateAsync(string soql, bool includeDeleted, CancellationToken cancellationToken)
    {
        var result = await RunQueryAsync(soql, includeDeleted, cancellationToken);
        if (result.Records.Count == 0)
            return new AggregateResult(new Dictionary<string, object?>());

        var record = result.Records[0];
        var dict = new Dictionary<string, object?>();

        foreach (var kvp in record)
        {
            if (kvp.Key != "attributes")
            {
                dict[kvp.Key] = UnwrapNode(kvp.Value);
            }
        }

        return new AggregateResult(dict);
    }

    /// <summary>
    /// Executes an aggregate query and returns multiple results (for GROUP BY).
    /// </summary>
    public Task<List<AggregateResult>> ExecuteAggregateListAsync(string soql, CancellationToken cancellationToken)
        => ExecuteAggregateListAsync(soql, includeDeleted: false, cancellationToken);

    internal async Task<List<AggregateResult>> ExecuteAggregateListAsync(string soql, bool includeDeleted, CancellationToken cancellationToken)
    {
        var results = new List<AggregateResult>();
        var result = await RunQueryAsync(soql, includeDeleted, cancellationToken);

        foreach (var record in result.Records)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in record)
            {
                if (kvp.Key != "attributes")
                {
                    dict[kvp.Key] = UnwrapNode(kvp.Value);
                }
            }
            results.Add(new AggregateResult(dict));
        }

        return results;
    }

    private static object? UnwrapNode(JsonNode? node)
    {
        if (node == null) return null;

        // Every arm is typed object on purpose: JsonNode has implicit conversions from decimal/string/bool, so a
        // switch whose last arm is "node" would silently re-wrap every value as a JsonNode (the pre-2.2 bug that made
        // AggregateResult.Get<decimal>() return default).
        switch (node.GetValueKind())
        {
            case JsonValueKind.Number:
                // Parsed responses hold a JsonElement; executors that build JsonObjects in code hold int/long/double.
                return node.AsValue().TryGetValue<decimal>(out var dec)
                    ? dec
                    : decimal.Parse(node.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            case JsonValueKind.String:
                return node.GetValue<string>();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return node;
        }
    }

    public string GetSoql<T>(Expression expression)
    {
        return BuildSoql(expression, GetRootElementType(expression));
    }

    /// <summary>
    /// Returns the SOQL for any expression built on this provider (entity or projection queries).
    /// </summary>
    internal string GetSoql(Expression expression) => BuildSoql(expression, GetRootElementType(expression));

    /// <summary>True when the expression asks for deleted/archived rows (IncludeDeleted()).</summary>
    internal bool UsesQueryAll(Expression expression) =>
        ParseExpression(expression, GetRootElementType(expression)).IncludeDeleted;

    private ISoqlDeletedRowsQueryExecutor RequireDeletedRowsExecutor()
    {
        return _dataService as ISoqlDeletedRowsQueryExecutor
            ?? throw new NotSupportedException(
                $"IncludeDeleted() needs an executor that implements {nameof(ISoqlDeletedRowsQueryExecutor)} " +
                $"(DataService does); '{_dataService.GetType().Name}' does not.");
    }

    private Task<Models.Data.QueryResult> RunQueryAsync(string soql, bool includeDeleted, CancellationToken cancellationToken)
        => includeDeleted
            ? RequireDeletedRowsExecutor().QueryIncludingDeletedAsync(soql, cancellationToken)
            : _dataService.QueryAsync(soql, cancellationToken);

    private async Task<List<JsonObject>> RunQueryAllAsync(string soql, bool includeDeleted, CancellationToken cancellationToken)
    {
        if (!includeDeleted)
        {
            return await _dataService.QueryAllAsync(soql, cancellationToken);
        }

        var all = new List<JsonObject>();
        await foreach (var record in RequireDeletedRowsExecutor().QueryAllIncludingDeletedAsyncEnumerable(soql, cancellationToken))
        {
            all.Add(record);
        }
        return all;
    }

    private IAsyncEnumerable<JsonObject> RunQueryStreamAsync(string soql, bool includeDeleted, CancellationToken cancellationToken)
        => includeDeleted
            ? RequireDeletedRowsExecutor().QueryAllIncludingDeletedAsyncEnumerable(soql, cancellationToken)
            : _dataService.QueryAllAsyncEnumerable(soql, cancellationToken);

    /// <summary>
    /// Builds the inner SELECT of a semi-join/anti-join: exactly one projected field, WHERE allowed,
    /// no ORDER BY / LIMIT / OFFSET (Salesforce rejects them in semi-joins).
    /// </summary>
    internal string BuildSemiJoinSoql(Expression expression)
    {
        var entityType = GetRootElementType(expression);
        var data = ParseExpression(expression, entityType);
        if (data.Projection == null || data.SelectFields.Count != 1 || !TryGetFieldChain(data.Projection.Body, out _))
        {
            throw new NotSupportedException(
                "A semi-join subquery must project exactly one field, e.g. data.Query<Contact>().Select(c => c.AccountId).");
        }

        if (data.OrderByFields.Count > 0 || data.Limit.HasValue || data.Offset.HasValue || data.Includes.Count > 0 ||
            data.ForUpdate || data.ForView || data.ForReference || data.SecurityMode != null || data.IncludeDeleted)
        {
            throw new NotSupportedException(
                "Semi-join subqueries support only Where and a single-field Select (no OrderBy, Take, Skip, Include, FOR or WITH clauses).");
        }

        var soql = $"SELECT {SecurityUtils.SanitizeFieldName(data.SelectFields[0])} FROM {SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName(entityType))}";
        return string.IsNullOrEmpty(data.WhereClause) ? soql : $"{soql} WHERE {data.WhereClause}";
    }

    /// <summary>
    /// Builds an aggregate SOQL statement over the query's filters (WHERE + WITH), ignoring ORDER BY / LIMIT /
    /// OFFSET of the entity query. Parsed from the expression tree, never by cutting SOQL text.
    /// </summary>
    internal string BuildAggregateSoql(
        Expression expression,
        IReadOnlyList<string> selectItems,
        IReadOnlyList<string>? groupBy = null,
        string? groupMode = null,
        string? having = null,
        IReadOnlyList<string>? orderBy = null,
        int? limit = null)
    {
        var entityType = GetRootElementType(expression);
        var data = ParseExpression(expression, entityType);
        if (data.Limit.HasValue || data.Offset.HasValue)
        {
            // SOQL applies LIMIT/OFFSET to the aggregate rows, not to the rows being aggregated, so honouring
            // Take/Skip here is impossible and ignoring them would silently aggregate every matching row.
            throw new NotSupportedException(
                "Aggregates (Sum/Average/Min/Max/Count distinct/GroupBy/Distinct/AggregateAsync) cannot be applied after Take/Skip: " +
                "SOQL cannot aggregate a limited subset. Remove Take/Skip, or load the rows and aggregate in memory.");
        }

        var sb = new System.Text.StringBuilder("SELECT ");
        sb.Append(string.Join(", ", selectItems));
        sb.Append(" FROM ").Append(SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName(entityType)));
        if (!string.IsNullOrEmpty(data.WhereClause)) sb.Append(" WHERE ").Append(data.WhereClause);
        sb.Append(SecuritySuffix(data.SecurityMode));
        if (groupBy is { Count: > 0 })
        {
            sb.Append(" GROUP BY ");
            sb.Append(groupMode == null ? string.Join(", ", groupBy) : $"{groupMode}({string.Join(", ", groupBy)})");
        }
        if (!string.IsNullOrWhiteSpace(having)) sb.Append(" HAVING ").Append(having);
        if (orderBy is { Count: > 0 }) sb.Append(" ORDER BY ").Append(string.Join(", ", orderBy));
        if (limit.HasValue) sb.Append(" LIMIT ").Append(limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// Lists results for any element type: projections go through the projection path, entity queries through the
    /// mapper. Lets ToListAsync/ToListAllAsync work without a class/new() constraint.
    /// </summary>
    internal Task<List<T>> ListAny<T>(Expression expression, bool all, CancellationToken cancellationToken)
    {
        var entityType = GetRootElementType(expression);
        if (ParseExpression(expression, entityType).Projection != null)
        {
            return all ? ExecuteProjectionAllAsync<T>(expression, cancellationToken) : ExecuteProjectionAsync<T>(expression, cancellationToken);
        }

        var method = typeof(SalesforceQueryProvider)
            .GetMethod(all ? nameof(ExecuteAllAsync) : nameof(ExecuteAsync))!
            .MakeGenericMethod(typeof(T));
        var args = all ? new object?[] { expression, cancellationToken } : new object?[] { expression, cancellationToken, null };
        return (Task<List<T>>)method.Invoke(this, args)!;
    }

    internal IAsyncEnumerable<T> StreamAny<T>(Expression expression, CancellationToken cancellationToken)
    {
        var entityType = GetRootElementType(expression);
        if (ParseExpression(expression, entityType).Projection != null)
        {
            return ExecuteProjectionStreamingAsync<T>(expression, cancellationToken);
        }

        var method = typeof(SalesforceQueryProvider).GetMethod(nameof(ExecuteStreamingAsync))!.MakeGenericMethod(typeof(T));
        return (IAsyncEnumerable<T>)method.Invoke(this, new object?[] { expression, cancellationToken })!;
    }

    internal Task<TResult> ExecuteScalarAsync<TResult>(Expression expression, CancellationToken cancellationToken)
        => ExecuteInternalAsync<TResult>(expression, cancellationToken);

    private async Task<TResult> ExecuteInternalAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        if (expression is MethodCallExpression methodCall &&
            methodCall.Method.DeclaringType == typeof(Queryable))
        {
            var methodName = methodCall.Method.Name;
            var sourceExpression = methodCall.Arguments.Count > 0 ? methodCall.Arguments[0] : expression;
            var sourceEntityType = GetRootElementType(sourceExpression);

            switch (methodName)
            {
                case "Count":
                    var count = await ExecuteCountAsyncInternal(expression, sourceEntityType, cancellationToken);
                    return (TResult)(object)count;

                case "LongCount":
                    var longCount = await ExecuteCountAsyncInternal(expression, sourceEntityType, cancellationToken);
                    return (TResult)(object)(long)longCount;

                case "Any":
                    var any = await ExecuteAnyAsync(sourceEntityType, expression, cancellationToken);
                    return (TResult)(object)any;

                case "First":
                case "FirstOrDefault":
                case "Single":
                case "SingleOrDefault":
                    return await ExecuteSingleResultAsync<TResult>(expression, sourceEntityType, methodName, cancellationToken);
            }
        }

        var resultElementType = TryGetSequenceElementType(typeof(TResult));
        if (resultElementType != null)
        {
            var sourceEntityType = GetRootElementType(expression);
            var listResult = await ExecuteListAsync(expression, resultElementType, sourceEntityType, cancellationToken, limit: null);
            return (TResult)listResult!;
        }

        _logger.LogWarning("Unsupported LINQ expression execution for type {Type}", typeof(TResult));
        throw new NotSupportedException($"The expression '{expression}' is not supported.");
    }

    private async Task<List<T>> ExecuteAsyncInternal<T>(Expression expression, CancellationToken cancellationToken, int? limit, Type? sourceEntityType)
        where T : class, new()
    {
        var entityType = sourceEntityType ?? typeof(T);
        var data = ParseExpression(expression, entityType);
        if (data.Limit == 0 || limit == 0)
        {
            return new List<T>();
        }

        var result = await RunQueryAsync(BuildSoql(data, entityType, limit), data.IncludeDeleted, cancellationToken);
        return SalesforceMapper.FromSalesforce<T>(result.Records);
    }

    private async Task<object?> ExecuteListAsync(
        Expression expression,
        Type resultType,
        Type sourceEntityType,
        CancellationToken cancellationToken,
        int? limit,
        bool applyProjection = true)
    {
        var queryData = ParseExpression(expression, sourceEntityType);
        if (applyProjection && queryData.Projection != null)
        {
            var typedProjection = CreateTypedProjectionLambda(
                sourceEntityType,
                resultType,
                queryData.Projection);

            return await ExecuteProjectionListAsync(
                expression,
                sourceEntityType,
                resultType,
                typedProjection,
                cancellationToken,
                limit,
                queryAll: false);
        }

        if (!IsMappableEntityType(resultType))
        {
            _logger.LogWarning("Cannot execute list result for non-entity type {Type} without a projection.", resultType);
            throw new NotSupportedException(
                $"Cannot execute list result for type {resultType.Name} without a projection.");
        }

        var method = typeof(SalesforceQueryProvider)
            .GetMethod(nameof(ExecuteAsyncInternal), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(resultType);

        var task = (Task)method.Invoke(this, new object?[] { expression, cancellationToken, limit, sourceEntityType })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    internal async Task<List<TResult>> ExecuteProjectionAsync<TResult>(
        Expression expression,
        CancellationToken cancellationToken,
        int? limit = null)
    {
        var sourceEntityType = GetRootElementType(expression);
        var queryData = ParseExpression(expression, sourceEntityType);

        if (queryData.Projection == null)
        {
            _logger.LogWarning("Projection execution requested but no Select expression was found.");
            throw new NotSupportedException("Projection execution requires a Select expression.");
        }

        var typedProjection = CreateTypedProjectionLambda(
            sourceEntityType,
            typeof(TResult),
            queryData.Projection);

        var result = await ExecuteProjectionListAsync(
            expression,
            sourceEntityType,
            typeof(TResult),
            typedProjection,
            cancellationToken,
            limit,
            queryAll: false);

        return (List<TResult>)result!;
    }

    internal async Task<List<TResult>> ExecuteProjectionAllAsync<TResult>(
        Expression expression,
        CancellationToken cancellationToken)
    {
        var sourceEntityType = GetRootElementType(expression);
        var queryData = ParseExpression(expression, sourceEntityType);

        if (queryData.Projection == null)
        {
            _logger.LogWarning("Projection execution requested but no Select expression was found.");
            throw new NotSupportedException("Projection execution requires a Select expression.");
        }

        var typedProjection = CreateTypedProjectionLambda(
            sourceEntityType,
            typeof(TResult),
            queryData.Projection);

        var result = await ExecuteProjectionListAsync(
            expression,
            sourceEntityType,
            typeof(TResult),
            typedProjection,
            cancellationToken,
            limit: null,
            queryAll: true);

        return (List<TResult>)result!;
    }

    internal IAsyncEnumerable<TResult> ExecuteProjectionStreamingAsync<TResult>(
        Expression expression,
        CancellationToken cancellationToken)
    {
        var sourceEntityType = GetRootElementType(expression);
        var queryData = ParseExpression(expression, sourceEntityType);

        if (queryData.Projection == null)
        {
            _logger.LogWarning("Projection streaming requested but no Select expression was found.");
            throw new NotSupportedException("Projection streaming requires a Select expression.");
        }

        var typedProjection = CreateTypedProjectionLambda(
            sourceEntityType,
            typeof(TResult),
            queryData.Projection);

        var method = typeof(SalesforceQueryProvider)
            .GetMethod(nameof(ExecuteProjectionStreamingAsyncInternal), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(sourceEntityType, typeof(TResult));

        return (IAsyncEnumerable<TResult>)method.Invoke(this, new object?[]
        {
            expression,
            typedProjection,
            cancellationToken
        })!;
    }

    private LambdaExpression CreateTypedProjectionLambda(
        Type sourceEntityType,
        Type resultType,
        LambdaExpression projection)
    {
        if (projection.Parameters.Count != 1 || projection.Parameters[0].Type != sourceEntityType)
        {
            _logger.LogWarning(
                "Projection parameter type mismatch. Expected {Expected}, got {Actual}.",
                sourceEntityType,
                projection.Parameters.FirstOrDefault()?.Type);
            throw new NotSupportedException("Projection parameter type does not match the source entity type.");
        }

        Expression body = projection.Body;
        if (projection.ReturnType != resultType)
        {
            if (resultType.IsAssignableFrom(projection.ReturnType))
            {
                body = Expression.Convert(body, resultType);
            }
            else
            {
                _logger.LogWarning(
                    "Projection return type mismatch. Expected {Expected}, got {Actual}.",
                    resultType,
                    projection.ReturnType);
                throw new NotSupportedException("Projection return type does not match the query result type.");
            }
        }

        var delegateType = typeof(Func<,>).MakeGenericType(sourceEntityType, resultType);
        return Expression.Lambda(delegateType, body, projection.Parameters);
    }

    private async Task<object?> ExecuteProjectionListAsync(
        Expression expression,
        Type sourceEntityType,
        Type resultType,
        LambdaExpression projection,
        CancellationToken cancellationToken,
        int? limit,
        bool queryAll)
    {
        var method = typeof(SalesforceQueryProvider)
            .GetMethod(nameof(ExecuteProjectionListAsyncInternal), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(sourceEntityType, resultType);

        var task = (Task)method.Invoke(this, new object?[]
        {
            expression,
            projection,
            cancellationToken,
            limit,
            queryAll
        })!;

        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    private async Task<List<TResult>> ExecuteProjectionListAsyncInternal<TSource, TResult>(
        Expression expression,
        Expression<Func<TSource, TResult>> projection,
        CancellationToken cancellationToken,
        int? limit,
        bool queryAll)
        where TSource : class, new()
    {
        List<TSource> sourceRecords = queryAll
            ? await ExecuteAllAsync<TSource>(expression, cancellationToken)
            : await ExecuteAsyncInternal<TSource>(expression, cancellationToken, limit, typeof(TSource));

        var projector = projection.Compile();
        return sourceRecords.Select(projector).ToList();
    }

    private async IAsyncEnumerable<TResult> ExecuteProjectionStreamingAsyncInternal<TSource, TResult>(
        Expression expression,
        Expression<Func<TSource, TResult>> projection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TSource : class, new()
    {
        var data = ParseExpression(expression, typeof(TSource));
        if (data.Limit == 0)
        {
            yield break;
        }

        var soql = BuildSoql(data, typeof(TSource));
        var projector = projection.Compile();

        await foreach (var record in RunQueryStreamAsync(soql, data.IncludeDeleted, cancellationToken))
        {
            var entity = SalesforceMapper.FromSalesforce<TSource>(record);
            yield return projector(entity);
        }
    }

    private async Task<int> ExecuteCountAsyncInternal(Expression expression, Type sourceEntityType, CancellationToken cancellationToken)
    {
        var queryData = ParseExpression(expression, sourceEntityType);
        if (queryData.Limit == 0)
        {
            return 0;
        }

        // COUNT() ignores LIMIT/OFFSET in the server's totalSize, so apply Skip/Take arithmetically:
        // Count(Skip(k).Take(n)) == clamp(total - k, 0, n). No rows are downloaded.
        var countSoql = BuildCountSoql(expression, sourceEntityType);
        var result = await RunQueryAsync(countSoql, queryData.IncludeDeleted, cancellationToken);
        var total = Math.Max(0, result.TotalSize - (queryData.Offset ?? 0));
        return queryData.Limit.HasValue ? Math.Min(total, queryData.Limit.Value) : total;
    }

    private async Task<bool> ExecuteAnyAsync(Type sourceEntityType, Expression expression, CancellationToken cancellationToken)
    {
        if (ParseExpression(expression, sourceEntityType).Offset is > 0)
        {
            // Any() after Skip(k) means "more than k rows match".
            return await ExecuteCountAsyncInternal(expression, sourceEntityType, cancellationToken) > 0;
        }

        // Use efficient COUNT query with LIMIT 1 to check existence
        // This avoids fetching all records just to check if any exist
        var queryData = ParseExpression(expression, sourceEntityType);
        var objectName = SalesforceMapper.GetObjectName(sourceEntityType);

        var countSoql = $"SELECT COUNT() FROM {SecurityUtils.SanitizeObjectName(objectName)}";

        if (!string.IsNullOrEmpty(queryData.WhereClause))
        {
            countSoql += $" WHERE {queryData.WhereClause}";
        }

        countSoql += SecuritySuffix(queryData.SecurityMode);
        if (queryData.Limit == 0)
        {
            return false;
        }

        countSoql += " LIMIT 1";

        var result = await RunQueryAsync(countSoql, queryData.IncludeDeleted, cancellationToken);
        return result.TotalSize > 0;
    }

    private async Task<TResult> ExecuteSingleResultAsync<TResult>(
        Expression expression,
        Type sourceEntityType,
        string methodName,
        CancellationToken cancellationToken)
    {
        var listResult = await ExecuteListAsync(expression, typeof(TResult), sourceEntityType, cancellationToken, limit: null);
        var list = listResult as IList;
        if (list == null)
        {
            return default!;
        }

        return methodName switch
        {
            "First" => list.Count == 0
                ? throw new InvalidOperationException("Sequence contains no elements.")
                : (TResult)list[0]!,
            "FirstOrDefault" => list.Count == 0 ? default! : (TResult)list[0]!,
            "Single" => list.Count switch
            {
                0 => throw new InvalidOperationException("Sequence contains no elements."),
                1 => (TResult)list[0]!,
                _ => throw new InvalidOperationException("Sequence contains more than one element.")
            },
            "SingleOrDefault" => list.Count switch
            {
                0 => default!,
                1 => (TResult)list[0]!,
                _ => throw new InvalidOperationException("Sequence contains more than one element.")
            },
            _ => throw new NotSupportedException($"Queryable method '{methodName}' is not supported for scalar results.")
        };
    }

    private static void ApplySecurityMode(SoqlBuilder soql, string? mode)
    {
        switch (mode)
        {
            case "SECURITY_ENFORCED": soql.WithSecurityEnforced(); break;
            case "USER_MODE": soql.WithUserMode(); break;
            case "SYSTEM_MODE": soql.WithSystemMode(); break;
        }
    }

    private static string SecuritySuffix(string? mode) => mode == null ? string.Empty : $" WITH {mode}";

    private string BuildSoql(Expression expression, Type entityType, int? limit = null)
        => BuildSoql(ParseExpression(expression, entityType), entityType, limit);

    private string BuildSoql(QueryData queryData, Type entityType, int? limit = null)
    {
        var objectName = SalesforceMapper.GetObjectName(entityType);

        // Build SELECT clause
        var fields = queryData.SelectFields.Count > 0
            ? queryData.SelectFields
            : SalesforceMapper.GetQueryableFields(entityType).ToList();

        // Ensure Id is always included
        if (!fields.Contains("Id", StringComparer.OrdinalIgnoreCase))
        {
            fields.Insert(0, "Id");
        }

        var soql = new SoqlBuilder(objectName)
            .Select(fields);

        // Add relationship sub-queries (Includes)
        foreach (var include in queryData.Includes)
        {
            var includeFields = include.Fields.Count > 0
                ? include.Fields
                : ResolveDefaultIncludeFields(entityType, include.RelationshipName);

            soql.SelectSubQuery(include.RelationshipName, sub =>
            {
                if (includeFields.Count > 0)
                {
                    sub.Select(includeFields);
                }
                else
                {
                    sub.Select("Id", "Name"); // Default fields
                }

                // Use type-safe condition if available
                if (include.Condition != null)
                {
                    sub.WhereCondition(include.Condition);
                }

                if (include.Limit.HasValue)
                {
                    sub.Limit(include.Limit.Value);
                }
            });
        }

        // Add WHERE clause (generated by SoqlExpressionVisitor - type-safe)
        if (!string.IsNullOrEmpty(queryData.WhereClause))
        {
            soql.WhereExpressionVisitorClause(queryData.WhereClause);
        }

        ApplySecurityMode(soql, queryData.SecurityMode);

        // Add ORDER BY
        foreach (var order in queryData.OrderByFields)
        {
            soql.OrderByTrusted(order.Render());
        }

        // Add LIMIT
        int? effectiveLimit = limit ?? queryData.Limit;
        if (limit.HasValue && queryData.Limit.HasValue)
        {
            effectiveLimit = Math.Min(limit.Value, queryData.Limit.Value);
        }
        if (effectiveLimit.HasValue)
        {
            soql.Limit(effectiveLimit.Value);
        }

        // Add OFFSET
        if (queryData.Offset.HasValue)
        {
            if (queryData.Offset.Value > 2000)
            {
                throw new NotSupportedException(
                    $"Skip({queryData.Offset.Value}) exceeds Salesforce's OFFSET maximum of 2000. " +
                    "Use ToAsyncEnumerable() or keyset paging (Where(a => a.Id > lastId)) for deep paging.");
            }

            soql.Offset(queryData.Offset.Value);
        }

        // Add locking clauses
        if (queryData.ForUpdate)
        {
            soql.ForUpdate();
        }
        if (queryData.ForView)
        {
            soql.ForView();
        }
        if (queryData.ForReference)
        {
            soql.ForReference();
        }

        return soql.Build();
    }

    private static List<string> ResolveDefaultIncludeFields(Type parentType, string relationshipName)
    {
        if (parentType == null) throw new ArgumentNullException(nameof(parentType));
        if (string.IsNullOrWhiteSpace(relationshipName)) return new List<string>();

        var relationshipProp = parentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new
            {
                Property = p,
                Attribute = p.GetCustomAttribute<SalesforceCore.Attributes.SalesforceChildRelationshipAttribute>()
            })
            .FirstOrDefault(x =>
                x.Attribute != null &&
                string.Equals(x.Attribute.RelationshipName, relationshipName, StringComparison.OrdinalIgnoreCase));

        if (relationshipProp?.Attribute != null)
        {
            var attr = relationshipProp.Attribute;

            if (attr.Fields is { Length: > 0 })
            {
                return EnsureIdFirst(attr.Fields.ToList());
            }

            var childClrType = TryGetCollectionElementType(relationshipProp.Property.PropertyType);
            if (childClrType != null &&
                childClrType.IsClass &&
                childClrType.GetConstructor(Type.EmptyTypes) != null)
            {
                var childFields = SalesforceMapper.GetQueryableFields(childClrType).ToList();
                return EnsureIdFirst(childFields);
            }
        }

        // Safe minimal default.
        return new List<string> { "Id", "Name" };
    }

    private static List<string> EnsureIdFirst(List<string> fields)
    {
        if (fields == null) throw new ArgumentNullException(nameof(fields));

        var idIndex = fields.FindIndex(f => f.Equals("Id", StringComparison.OrdinalIgnoreCase));
        if (idIndex < 0)
        {
            fields.Insert(0, "Id");
            return fields;
        }

        if (idIndex > 0)
        {
            var id = fields[idIndex];
            fields.RemoveAt(idIndex);
            fields.Insert(0, id);
        }

        return fields;
    }

    private static Type? TryGetCollectionElementType(Type type)
    {
        if (type == typeof(string)) return null;

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 1)
            {
                return args[0];
            }
        }

        var enumerableInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0];
    }

    private string BuildCountSoql(Expression expression, Type entityType)
    {
        var queryData = ParseExpression(expression, entityType);
        var objectName = SalesforceMapper.GetObjectName(entityType);

        var soql = $"SELECT COUNT() FROM {SecurityUtils.SanitizeObjectName(objectName)}";

        if (!string.IsNullOrEmpty(queryData.WhereClause))
        {
            soql += $" WHERE {queryData.WhereClause}";
        }

        return soql + SecuritySuffix(queryData.SecurityMode);
    }

    private QueryData ParseExpression(Expression expression, Type entityType)
    {
        var data = new QueryData();
        ParseExpressionRecursive(expression, entityType, data);
        return data;
    }

    private void ParseExpressionRecursive(Expression expression, Type entityType, QueryData data)
    {
        switch (expression)
        {
            case MethodCallExpression methodCall:
                ParseMethodCall(methodCall, entityType, data);
                break;

            case ConstantExpression:
                // Root queryable, nothing to parse
                break;
        }
    }

    private void ParseMethodCall(MethodCallExpression methodCall, Type entityType, QueryData data)
    {
        // First parse the source (for chained calls)
        if (methodCall.Arguments.Count > 0)
        {
            ParseExpressionRecursive(methodCall.Arguments[0], entityType, data);
        }

        var methodName = methodCall.Method.Name;

        if (data.Projection != null && methodName != "Select" && !IsAllowedAfterProjection(methodName))
        {
            _logger.LogWarning("Queryable method '{MethodName}' is not supported after a projection.", methodName);
            throw new NotSupportedException(
                $"Queryable method '{methodName}' is not supported after a projection.");
        }

        if ((data.Limit.HasValue || data.Offset.HasValue) && ChangesRowSetAfterPaging(methodName, methodCall))
        {
            throw new NotSupportedException(
                $"'{methodName}' after Take/Skip is not supported: SOQL always filters and sorts before LIMIT/OFFSET, " +
                "so the query would return different rows than LINQ promises. Move Where/OrderBy before Take/Skip.");
        }

        switch (methodName)
        {
            case "Where":
                var whereLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                var whereClause = SoqlExpressionVisitor.Translate(whereLambda.Body, entityType);

                if (string.IsNullOrEmpty(data.WhereClause))
                {
                    data.WhereClause = whereClause;
                }
                else
                {
                    data.WhereClause = $"({data.WhereClause}) AND ({whereClause})";
                }
                break;

            case "Select":
                // Parse select expression to get field names
                var selectLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                if (data.Projection != null)
                {
                    _logger.LogWarning("Multiple Select projections are not supported in a single query.");
                    throw new NotSupportedException("Only a single Select projection is supported.");
                }

                data.Projection = selectLambda;
                data.SelectFields.Clear();
                var selectFields = ParseSelectExpression(selectLambda, entityType);
                if (selectFields.Count > 0)
                {
                    data.SelectFields.AddRange(selectFields);
                }
                break;

            case "OrderBy":
                data.OrderByFields.Clear();
                goto case "ThenBy";
            case "ThenBy":
                data.OrderByFields.Add(ParseOrderItem(methodCall, entityType, descending: false));
                break;

            case "OrderByDescending":
                data.OrderByFields.Clear();
                goto case "ThenByDescending";
            case "ThenByDescending":
                data.OrderByFields.Add(ParseOrderItem(methodCall, entityType, descending: true));
                break;

            case "Take":
            {
                // Take(a).Take(b) == Take(min(a, b)).
                var takeCount = Math.Max(0, Convert.ToInt32(GetConstantValue(methodCall.Arguments[1]), System.Globalization.CultureInfo.InvariantCulture));
                data.Limit = data.Limit.HasValue ? Math.Min(data.Limit.Value, takeCount) : takeCount;
                break;
            }

            case "Skip":
            {
                // Skip(a).Skip(b) == Skip(a + b); Take(n).Skip(k) == Skip(k).Take(max(n - k, 0)).
                var skipCount = Math.Max(0, Convert.ToInt32(GetConstantValue(methodCall.Arguments[1]), System.Globalization.CultureInfo.InvariantCulture));
                data.Offset = (data.Offset ?? 0) + skipCount;
                if (data.Limit.HasValue)
                {
                    data.Limit = Math.Max(0, data.Limit.Value - skipCount);
                }
                break;
            }

            case "First":
            case "FirstOrDefault":
                data.Limit = data.Limit.HasValue ? Math.Min(data.Limit.Value, 1) : 1;
                if (methodCall.Arguments.Count > 1)
                {
                    var firstLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                    var firstWhere = SoqlExpressionVisitor.Translate(firstLambda.Body, entityType);
                    data.WhereClause = string.IsNullOrEmpty(data.WhereClause)
                        ? firstWhere
                        : $"({data.WhereClause}) AND ({firstWhere})";
                }
                break;

            case "Single":
            case "SingleOrDefault":
                data.Limit = data.Limit.HasValue ? Math.Min(data.Limit.Value, 2) : 2; // Get 2 to detect if there's more than one
                if (methodCall.Arguments.Count > 1)
                {
                    var singleLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                    var singleWhere = SoqlExpressionVisitor.Translate(singleLambda.Body, entityType);
                    data.WhereClause = string.IsNullOrEmpty(data.WhereClause)
                        ? singleWhere
                        : $"({data.WhereClause}) AND ({singleWhere})";
                }
                break;

            case "Count":
            case "LongCount":
                if (methodCall.Arguments.Count > 1)
                {
                    var countLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                    var countWhere = SoqlExpressionVisitor.Translate(countLambda.Body, entityType);
                    data.WhereClause = string.IsNullOrEmpty(data.WhereClause)
                        ? countWhere
                        : $"({data.WhereClause}) AND ({countWhere})";
                }
                break;

            case "Any":
                data.Limit = data.Limit.HasValue ? Math.Min(data.Limit.Value, 1) : 1;
                if (methodCall.Arguments.Count > 1)
                {
                    var anyLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                    var anyWhere = SoqlExpressionVisitor.Translate(anyLambda.Body, entityType);
                    data.WhereClause = string.IsNullOrEmpty(data.WhereClause)
                        ? anyWhere
                        : $"({data.WhereClause}) AND ({anyWhere})";
                }
                break;

            case "IncludeInternal":
                // Handle Include expression
                if (methodCall.Arguments.Count >= 2 && methodCall.Arguments[1] is ConstantExpression includeConst)
                {
                    if (includeConst.Value is IncludeExpressionHolder includeHolder)
                    {
                        data.Includes.Add(new IncludeExpression
                        {
                            RelationshipName = includeHolder.RelationshipName,
                            Condition = includeHolder.Condition,
                            Limit = includeHolder.Limit
                        });
                        data.Includes.Last().Fields.AddRange(includeHolder.Fields);
                    }
                }
                break;

            case "WhereConditionInternal":
                // Handle type-safe WhereCondition expression
                if (methodCall.Arguments.Count >= 2 && methodCall.Arguments[1] is ConstantExpression condConst)
                {
                    if (condConst.Value is SoqlCondition condition)
                    {
                        var rendered = condition.Render();
                        if (!string.IsNullOrEmpty(rendered))
                        {
                            data.WhereClause = string.IsNullOrEmpty(data.WhereClause)
                                ? rendered
                                : $"({data.WhereClause}) AND ({rendered})";
                        }
                    }
                }
                break;

            case "AsQueryable":
            case "AsEnumerable":
                // No-op for query translation
                break;

            case "ForUpdateInternal":
                data.ForUpdate = true;
                break;

            case "NullsFirstInternal":
            case "NullsLastInternal":
                if (data.OrderByFields.Count == 0)
                {
                    throw new NotSupportedException("NullsFirst()/NullsLast() must follow OrderBy/ThenBy.");
                }
                data.OrderByFields[^1].Nulls = methodName == "NullsFirstInternal" ? "FIRST" : "LAST";
                break;

            case "WithSecurityEnforcedInternal":
                data.SecurityMode = "SECURITY_ENFORCED";
                break;

            case "WithUserModeInternal":
                data.SecurityMode = "USER_MODE";
                break;

            case "WithSystemModeInternal":
                data.SecurityMode = "SYSTEM_MODE";
                break;

            case "IncludeDeletedInternal":
                data.IncludeDeleted = true;
                break;

            case "ForViewInternal":
                data.ForView = true;
                break;

            case "ForReferenceInternal":
                data.ForReference = true;
                break;

            default:
                _logger.LogWarning("Queryable method '{MethodName}' is not supported by the Salesforce LINQ provider.", methodName);
                throw new NotSupportedException($"Queryable method '{methodName}' is not supported by the Salesforce LINQ provider.");
        }
    }

    private static bool ChangesRowSetAfterPaging(string methodName, MethodCallExpression methodCall) => methodName switch
    {
        "Where" or "WhereConditionInternal" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" => true,
        "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Count" or "LongCount" or "Any" => methodCall.Arguments.Count > 1,
        _ => false
    };

    private static bool IsAllowedAfterProjection(string methodName)
    {
        return methodName is "Skip" or "Take" or "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or
               "Count" or "LongCount" or "Any" or "AsQueryable" or "AsEnumerable" or
               "WithSecurityEnforcedInternal" or "WithUserModeInternal" or "WithSystemModeInternal" or
               "IncludeDeletedInternal" or "ForUpdateInternal" or "ForViewInternal" or "ForReferenceInternal";
    }

    private OrderItem ParseOrderItem(MethodCallExpression methodCall, Type entityType, bool descending)
    {
        var lambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
        var body = lambda.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            body = convert.Operand;
        }

        // OrderBy(x => SoqlFunctions.Distance(...)) and other SOQL functions are rendered by the visitor.
        if (body is MethodCallExpression call && call.Method.DeclaringType == typeof(SoqlFunctions))
        {
            return new OrderItem(SoqlExpressionVisitor.Translate(call, entityType), descending, trusted: true);
        }

        return new OrderItem(GetFieldNameFromExpression(lambda.Body, entityType), descending);
    }

    private List<string> ParseSelectExpression(LambdaExpression lambda, Type entityType)
    {
        var body = lambda.Body;
        var fields = new List<string>();

        if (body is ParameterExpression)
        {
            return fields;
        }

        // Anonymous type: new { x.Name, x.Id }
        if (body is NewExpression newExpr)
        {
            foreach (var arg in newExpr.Arguments)
            {
                if (TryGetFieldChain(arg, out var memberExpr))
                {
                    fields.Add(GetFieldPathFromMember(memberExpr, entityType));
                }
                else
                {
                    return CollectProjectedFields(lambda, entityType);
                }
            }
            return fields;
        }

        // Single member: x => x.Name
        if (TryGetFieldChain(body, out var singleMember))
        {
            fields.Add(GetFieldPathFromMember(singleMember, entityType));
            return fields;
        }

        // Member init: new SomeClass { Name = x.Name }
        if (body is MemberInitExpression memberInit)
        {
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is MemberAssignment assignment &&
                    TryGetFieldChain(assignment.Expression, out var memberExpr))
                {
                    fields.Add(GetFieldPathFromMember(memberExpr, entityType));
                }
                else
                {
                    return CollectProjectedFields(lambda, entityType);
                }
            }
            return fields;
        }

        return CollectProjectedFields(lambda, entityType);
    }

    /// <summary>
    /// Computed projections (x =&gt; x.First + " " + x.Last, x =&gt; x.Count ?? 0, new { Big = x.Emp &gt; 5 }) run client-side
    /// after the fetch; SELECT just needs every field the projection reads.
    /// </summary>
    private static bool TryGetFieldChain(Expression expression, out MemberExpression memberExpression)
    {
        if (!TryGetMemberExpression(expression, out memberExpression)) return false;
        Expression? current = memberExpression;
        while (current is MemberExpression m)
        {
            var isNullableValue = m.Member.Name == "Value" && m.Expression != null && Nullable.GetUnderlyingType(m.Expression.Type) != null;
            if (!isNullableValue && m.Member.DeclaringType?.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) return false;
            current = m.Expression is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : m.Expression;
        }
        return current is ParameterExpression;
    }

    private List<string> CollectProjectedFields(LambdaExpression lambda, Type entityType)
    {
        var collector = new ProjectionFieldCollector(lambda.Parameters[0]);
        collector.Visit(lambda.Body);
        if (collector.UsesWholeEntity)
        {
            return new List<string>(); // x => Helper(x): select every mapped field
        }

        var fields = collector.Members
            .Select(m => GetFieldPathFromMember(m, entityType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fields.Count == 0)
        {
            _logger.LogWarning("Select expression reads no fields: {Expression}", lambda.Body);
            throw new NotSupportedException($"Select expression '{lambda.Body}' does not read any field.");
        }

        return fields;
    }

    private sealed class ProjectionFieldCollector : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        public ProjectionFieldCollector(ParameterExpression parameter) => _parameter = parameter;

        public List<MemberExpression> Members { get; } = new();
        public bool UsesWholeEntity { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (IsEntityMemberChain(node))
            {
                Members.Add(node);
                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            // Reached only when the parameter is used other than through a member chain.
            if (node == _parameter) UsesWholeEntity = true;
            return node;
        }

        private bool IsEntityMemberChain(MemberExpression node)
        {
            Expression? current = node;
            while (current is MemberExpression member)
            {
                var declaring = member.Member.DeclaringType;
                if (declaring == null || declaring.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                {
                    return false; // x.Date.Value / x.Name.Length: descend to the entity member
                }
                current = member.Expression;
                if (current is UnaryExpression { NodeType: ExpressionType.Convert } u) current = u.Operand;
            }

            return current == _parameter;
        }
    }

    private string GetFieldNameFromExpression(Expression expression, Type entityType)
    {
        if (TryGetMemberExpression(expression, out var memberExpr))
        {
            return GetFieldPathFromMember(memberExpr, entityType);
        }

        throw new NotSupportedException($"Cannot extract field name from expression type: {expression.GetType().Name}");
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

    private string GetFieldPathFromMember(MemberExpression memberExpr, Type rootType)
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

    private string GetFieldNameFromMember(MemberInfo member, Type entityType)
    {
        // Check for SalesforceField attribute
        var fieldAttr = member.GetCustomAttribute(typeof(Attributes.SalesforceFieldAttribute), true)
            as Attributes.SalesforceFieldAttribute;

        return fieldAttr?.FieldName ?? SalesforceMapper.GetFieldName(entityType, member.Name);
    }

    private static Expression StripQuotes(Expression expression)
    {
        while (expression.NodeType == ExpressionType.Quote)
        {
            expression = ((UnaryExpression)expression).Operand;
        }
        return expression;
    }

    private static object? GetConstantValue(Expression expression)
    {
        expression = StripQuotes(expression);

        if (ContainsParameterExpression(expression))
        {
            throw new NotSupportedException("Expressions that reference the query parameter are not supported for constant evaluation.");
        }

        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        var lambda = Expression.Lambda(expression);
        var compiled = lambda.Compile();
        return compiled.DynamicInvoke();
    }

    private static bool ContainsParameterExpression(Expression expression)
    {
        var detector = new ParameterExpressionDetector();
        detector.Visit(expression);
        return detector.Found;
    }

    private sealed class ParameterExpressionDetector : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node == null || Found)
            {
                return node;
            }

            if (node.NodeType == ExpressionType.Parameter)
            {
                Found = true;
                return node;
            }

            return base.Visit(node);
        }
    }

    private class QueryData
    {
        public List<string> SelectFields { get; } = new();
        public LambdaExpression? Projection { get; set; }
        public string? WhereClause { get; set; }
        public List<OrderItem> OrderByFields { get; } = new();
        /// <summary>WITH clause: SECURITY_ENFORCED, USER_MODE or SYSTEM_MODE.</summary>
        public string? SecurityMode { get; set; }
        /// <summary>Use the queryAll resource (deleted and archived rows).</summary>
        public bool IncludeDeleted { get; set; }
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public List<string> GroupByFields { get; } = new();
        public string? HavingClause { get; set; }
        public List<AggregateExpression> Aggregates { get; } = new();
        public List<IncludeExpression> Includes { get; } = new();
        
        // Locking clause flags
        public bool ForUpdate { get; set; }
        public bool ForView { get; set; }
        public bool ForReference { get; set; }
    }

    internal sealed class OrderItem
    {
        public OrderItem(string field, bool descending, bool trusted = false)
        {
            Field = field;
            Descending = descending;
            Trusted = trusted;
        }

        public string Field { get; }
        public bool Descending { get; }
        /// <summary>True when <see cref="Field"/> is a pre-rendered SOQL expression (e.g. DISTANCE(...)).</summary>
        public bool Trusted { get; }
        public string? Nulls { get; set; }

        public string Render()
        {
            var field = Trusted ? Field : SecurityUtils.SanitizeFieldName(Field);
            var text = $"{field} {(Descending ? "DESC" : "ASC")}";
            return Nulls == null ? text : $"{text} NULLS {Nulls}";
        }
    }

    internal class AggregateExpression
    {
        public string Function { get; set; } = string.Empty;
        public string? Field { get; set; }
        public string? Alias { get; set; }
    }

    internal class IncludeExpression
    {
        public string RelationshipName { get; set; } = string.Empty;
        public List<string> Fields { get; } = new();
        public SoqlCondition? Condition { get; set; }
        public int? Limit { get; set; }
    }
}
