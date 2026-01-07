using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalesforceCore.Mapping;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
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
    public SalesforceQueryable(IDataService dataService)
    {
        _provider = new SalesforceQueryProvider(dataService);
        _expression = Expression.Constant(this);
    }

    public SalesforceQueryable(IDataService dataService, ILogger<SalesforceQueryProvider> logger)
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

/// <summary>
/// LINQ query provider that translates expressions to SOQL.
/// </summary>
public class SalesforceQueryProvider : IQueryProvider
{
    private readonly IDataService _dataService;
    private readonly ILogger<SalesforceQueryProvider> _logger;

    public SalesforceQueryProvider(IDataService dataService, ILogger<SalesforceQueryProvider>? logger = null)
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
        var detector = new SelectExpressionDetector();
        detector.Visit(expression);
        return detector.Found;
    }

    private sealed class SelectExpressionDetector : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable) &&
                string.Equals(node.Method.Name, "Select", StringComparison.Ordinal))
            {
                Found = true;
                return node;
            }

            return base.VisitMethodCall(node);
        }
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
            return ExecuteListAsync(
                    expression,
                    sequenceElementType,
                    sourceEntityType,
                    CancellationToken.None,
                    limit: null)
                .GetAwaiter()
                .GetResult();
        }

        return ExecuteInternalAsync<object>(expression, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return ExecuteInternalAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();
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
        var soql = BuildSoql(expression, GetRootElementType(expression));
        var allRecords = await _dataService.QueryAllAsync(soql, cancellationToken);
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
        var soql = BuildSoql(expression, GetRootElementType(expression));

        await foreach (var record in _dataService.QueryAllAsyncEnumerable(soql, cancellationToken))
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
    public async Task<AggregateResult> ExecuteAggregateAsync(string soql, CancellationToken cancellationToken)
    {
        var result = await _dataService.QueryAsync(soql, cancellationToken);
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
    public async Task<List<AggregateResult>> ExecuteAggregateListAsync(string soql, CancellationToken cancellationToken)
    {
        var results = new List<AggregateResult>();
        var result = await _dataService.QueryAsync(soql, cancellationToken);

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

        return node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<decimal>(),
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => node
        };
    }

    public string GetSoql<T>(Expression expression)
    {
        return BuildSoql(expression, GetRootElementType(expression));
    }

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
        var soql = BuildSoql(expression, sourceEntityType ?? typeof(T), limit);
        var result = await _dataService.QueryAsync(soql, cancellationToken);
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
        var soql = BuildSoql(expression, typeof(TSource));
        var projector = projection.Compile();

        await foreach (var record in _dataService.QueryAllAsyncEnumerable(soql, cancellationToken))
        {
            var entity = SalesforceMapper.FromSalesforce<TSource>(record);
            yield return projector(entity);
        }
    }

    private async Task<int> ExecuteCountAsyncInternal(Expression expression, Type sourceEntityType, CancellationToken cancellationToken)
    {
        var queryData = ParseExpression(expression, sourceEntityType);
        if (queryData.Limit.HasValue || queryData.Offset.HasValue)
        {
            var soql = BuildSoql(expression, sourceEntityType);
            var allRecords = await _dataService.QueryAllAsync(soql, cancellationToken);
            return allRecords.Count;
        }

        var countSoql = BuildCountSoql(expression, sourceEntityType);
        var result = await _dataService.QueryAsync(countSoql, cancellationToken);
        return result.TotalSize;
    }

    private async Task<bool> ExecuteAnyAsync(Type sourceEntityType, Expression expression, CancellationToken cancellationToken)
    {
        // Use efficient COUNT query with LIMIT 1 to check existence
        // This avoids fetching all records just to check if any exist
        var queryData = ParseExpression(expression, sourceEntityType);
        var objectName = SalesforceMapper.GetObjectName(sourceEntityType);

        var countSoql = $"SELECT COUNT() FROM {SecurityUtils.SanitizeObjectName(objectName)}";

        if (!string.IsNullOrEmpty(queryData.WhereClause))
        {
            countSoql += $" WHERE {queryData.WhereClause}";
        }

        countSoql += " LIMIT 1";

        var result = await _dataService.QueryAsync(countSoql, cancellationToken);
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

    private string BuildSoql(Expression expression, Type entityType, int? limit = null)
    {
        var queryData = ParseExpression(expression, entityType);
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

        // Add ORDER BY
        foreach (var (field, descending) in queryData.OrderByFields)
        {
            if (descending)
                soql.OrderByDescending(field);
            else
                soql.OrderBy(field);
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
            soql.Offset(queryData.Offset.Value);
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

        return soql;
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
                var orderLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                var orderField = GetFieldNameFromExpression(orderLambda.Body, entityType);
                data.OrderByFields.Add((orderField, false));
                break;

            case "OrderByDescending":
                data.OrderByFields.Clear();
                goto case "ThenByDescending";
            case "ThenByDescending":
                var orderDescLambda = (LambdaExpression)StripQuotes(methodCall.Arguments[1]);
                var orderDescField = GetFieldNameFromExpression(orderDescLambda.Body, entityType);
                data.OrderByFields.Add((orderDescField, true));
                break;

            case "Take":
                var takeCount = (int)GetConstantValue(methodCall.Arguments[1])!;
                data.Limit = takeCount;
                break;

            case "Skip":
                var skipCount = (int)GetConstantValue(methodCall.Arguments[1])!;
                data.Offset = skipCount;
                break;

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

            default:
                _logger.LogWarning("Queryable method '{MethodName}' is not supported by the Salesforce LINQ provider.", methodName);
                throw new NotSupportedException($"Queryable method '{methodName}' is not supported by the Salesforce LINQ provider.");
        }
    }

    private static bool IsAllowedAfterProjection(string methodName)
    {
        return methodName is "Skip" or "Take" or "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or
               "Count" or "Any" or "AsQueryable" or "AsEnumerable";
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
                if (TryGetMemberExpression(arg, out var memberExpr))
                {
                    fields.Add(GetFieldPathFromMember(memberExpr, entityType));
                }
                else
                {
                    _logger.LogWarning("Select expression contains unsupported argument: {Expression}", arg);
                    throw new NotSupportedException("Select expressions must be simple member accesses.");
                }
            }
            return fields;
        }

        // Single member: x => x.Name
        if (TryGetMemberExpression(body, out var singleMember))
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
                    TryGetMemberExpression(assignment.Expression, out var memberExpr))
                {
                    fields.Add(GetFieldPathFromMember(memberExpr, entityType));
                }
                else
                {
                    _logger.LogWarning("Select expression contains unsupported member binding: {Expression}", binding);
                    throw new NotSupportedException("Select expressions must be simple member accesses.");
                }
            }
            return fields;
        }

        _logger.LogWarning("Select expression is not a supported member access form: {Expression}", body);
        throw new NotSupportedException("Select expressions must be simple member accesses.");
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
        public List<(string Field, bool Descending)> OrderByFields { get; } = new();
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public List<string> GroupByFields { get; } = new();
        public string? HavingClause { get; set; }
        public List<AggregateExpression> Aggregates { get; } = new();
        public List<IncludeExpression> Includes { get; } = new();
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

/// <summary>
/// Represents the result of a GroupBy query with aggregate values.
/// </summary>
public class AggregateResult
{
    private readonly Dictionary<string, object?> _values;

    /// <summary>
    /// Creates a new aggregate result from a dictionary.
    /// </summary>
    public AggregateResult(Dictionary<string, object?> values)
    {
        _values = values ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// Gets a value by key.
    /// </summary>
    public object? this[string key] => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a typed value by key.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_values.TryGetValue(key, out var value) || value == null)
            return default;

        if (value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Gets the count value (from COUNT()).
    /// </summary>
    public int Count => Get<int?>("expr0") ?? Get<int?>("Count") ?? 0;

    /// <summary>
    /// Gets all keys.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>
    /// Gets all values.
    /// </summary>
    public IEnumerable<object?> Values => _values.Values;
}

/// <summary>
/// Extension methods for creating Salesforce queryables.
/// </summary>
public static class SalesforceQueryExtensions
{
    /// <summary>
    /// Creates a queryable for a Salesforce object type.
    /// </summary>
    public static SalesforceQueryable<T> Query<T>(this IDataService dataService) where T : class, new()
    {
        return new SalesforceQueryable<T>(dataService);
    }

    public static SalesforceQueryable<T> Query<T>(this IDataService dataService, ILogger<SalesforceQueryProvider> logger)
        where T : class, new()
    {
        return new SalesforceQueryable<T>(dataService, logger);
    }

    /// <summary>
    /// Executes the query and returns results as a list.
    /// Note: Returns only the first page (up to 2000 records). Use ToListAllAsync for all records.
    /// </summary>
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        if (query is SalesforceQueryable<T> sfQuery)
        {
            return sfQuery.ToListAsync(cancellationToken);
        }
        if (query.Provider is SalesforceQueryProvider provider)
        {
            return provider.ExecuteProjectionAsync<T>(query.Expression, cancellationToken);
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
        where T : class, new()
    {
        if (query is SalesforceQueryable<T> sfQuery)
        {
            return sfQuery.ToListAllAsync(cancellationToken);
        }
        if (query.Provider is SalesforceQueryProvider provider)
        {
            return provider.ExecuteProjectionAllAsync<T>(query.Expression, cancellationToken);
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
        where T : class, new()
    {
        if (query is SalesforceQueryable<T> sfQuery)
        {
            return sfQuery.ToAsyncEnumerableInternal(cancellationToken);
        }
        if (query.Provider is SalesforceQueryProvider provider)
        {
            return provider.ExecuteProjectionStreamingAsync<T>(query.Expression, cancellationToken);
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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        // Extract WHERE clause from the generated SOQL
        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        var soql = $"SELECT SUM({fieldName}) sum FROM {objectName}{wherePart}";
        var provider = (SalesforceQueryProvider)query.Provider;
        var result = await provider.ExecuteAggregateAsync(soql, cancellationToken);

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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        var soql = $"SELECT AVG({fieldName}) avg FROM {objectName}{wherePart}";
        var provider = (SalesforceQueryProvider)query.Provider;
        var result = await provider.ExecuteAggregateAsync(soql, cancellationToken);

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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        var soql = $"SELECT MIN({fieldName}) min FROM {objectName}{wherePart}";
        var provider = (SalesforceQueryProvider)query.Provider;
        var result = await provider.ExecuteAggregateAsync(soql, cancellationToken);

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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        var soql = $"SELECT MAX({fieldName}) max FROM {objectName}{wherePart}";
        var provider = (SalesforceQueryProvider)query.Provider;
        var result = await provider.ExecuteAggregateAsync(soql, cancellationToken);

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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        // Remove ORDER BY / LIMIT / OFFSET from WHERE part if present
        var orderByIndex = wherePart.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
        var limitIndex = wherePart.IndexOf(" LIMIT ", StringComparison.OrdinalIgnoreCase);
        var offsetIndex = wherePart.IndexOf(" OFFSET ", StringComparison.OrdinalIgnoreCase);
        var cutoff = new[] { orderByIndex, limitIndex, offsetIndex }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (cutoff >= 0)
            wherePart = wherePart.Substring(0, cutoff);

        var soql = $"SELECT {groupField}, COUNT(Id) cnt FROM {objectName}{wherePart} GROUP BY {groupField}";
        var provider = (SalesforceQueryProvider)query.Provider;

        return await provider.ExecuteAggregateListAsync(soql, cancellationToken);
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
        var objectName = SecurityUtils.SanitizeObjectName(SalesforceMapper.GetObjectName<T>());
        var whereClause = query.ToSoql();

        var whereIndex = whereClause.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        var wherePart = whereIndex >= 0
            ? whereClause.Substring(whereIndex)
            : "";

        // Remove ORDER BY / LIMIT / OFFSET
        var orderByIndex = wherePart.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
        var limitIndex = wherePart.IndexOf(" LIMIT ", StringComparison.OrdinalIgnoreCase);
        var offsetIndex = wherePart.IndexOf(" OFFSET ", StringComparison.OrdinalIgnoreCase);
        var cutoff = new[] { orderByIndex, limitIndex, offsetIndex }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (cutoff >= 0)
            wherePart = wherePart.Substring(0, cutoff);

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

        var soql = $"SELECT {string.Join(", ", selectParts)} FROM {objectName}{wherePart} GROUP BY {groupField}";
        var provider = (SalesforceQueryProvider)query.Provider;

        return await provider.ExecuteAggregateListAsync(soql, cancellationToken);
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

    #region Extended LINQ Operators (SOQL Workarounds)

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
        var fieldName = GetFieldNameFromSelector(selector);
        var objectName = SalesforceMapper.GetObjectName<T>();
        
        // Build WHERE clause from the query if any
        var queryable = query as IQueryable<T>;
        var whereClause = "";
        if (queryable.Expression is MethodCallExpression methodCall)
        {
            var visitor = new SoqlExpressionVisitor(typeof(T));
            whereClause = ExtractWhereClause(methodCall, typeof(T));
        }
        
        var soql = $"SELECT {fieldName} FROM {objectName}";
        if (!string.IsNullOrEmpty(whereClause))
        {
            soql += $" WHERE {whereClause}";
        }
        soql += $" GROUP BY {fieldName}";
        
        var provider = (SalesforceQueryProvider)query.Provider;
        var results = await provider.ExecuteAggregateListAsync(soql, cancellationToken);
        
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

    private static string ExtractWhereClause(MethodCallExpression expression, Type entityType)
    {
        if (expression.Method.Name == "Where" && expression.Arguments.Count > 1)
        {
            var lambda = (LambdaExpression)StripQuotesStatic(expression.Arguments[1]);
            return SoqlExpressionVisitor.Translate(lambda.Body, entityType);
        }
        
        if (expression.Arguments.Count > 0 && expression.Arguments[0] is MethodCallExpression inner)
        {
            return ExtractWhereClause(inner, entityType);
        }
        
        return "";
    }

    private static Expression StripQuotesStatic(Expression e)
    {
        while (e is UnaryExpression { NodeType: ExpressionType.Quote } unary)
        {
            e = unary.Operand;
        }
        return e;
    }

    #endregion

    #endregion
}

/// <summary>
/// Specifies an aggregate function for GroupBy operations.
/// </summary>
public class AggregateSpec
{
    /// <summary>
    /// The field to aggregate.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// The aggregate function to apply.
    /// </summary>
    public string Function { get; }

    /// <summary>
    /// Optional alias for the result.
    /// </summary>
    public string? Alias { get; }

    /// <summary>
    /// Creates a new aggregate specification.
    /// </summary>
    public AggregateSpec(string field, AggregateFunction function, string? alias = null)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Function = NormalizeAggregateFunction(function);
        Alias = alias;
    }

    /// <summary>
    /// Creates a new aggregate specification with a string function name.
    /// </summary>
    public AggregateSpec(string field, string function, string? alias = null)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Function = NormalizeAggregateFunction(function ?? throw new ArgumentNullException(nameof(function)));
        Alias = alias;
    }

    private static string NormalizeAggregateFunction(AggregateFunction function)
    {
        return function switch
        {
            AggregateFunction.Count => "COUNT",
            AggregateFunction.Sum => "SUM",
            AggregateFunction.Avg => "AVG",
            AggregateFunction.Min => "MIN",
            AggregateFunction.Max => "MAX",
            AggregateFunction.CountDistinct => "COUNT_DISTINCT",
            _ => throw new ArgumentOutOfRangeException(nameof(function))
        };
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
}

/// <summary>
/// Aggregate functions supported in SOQL.
/// </summary>
public enum AggregateFunction
{
    /// <summary>Count records.</summary>
    Count,
    /// <summary>Sum numeric values.</summary>
    Sum,
    /// <summary>Average numeric values.</summary>
    Avg,
    /// <summary>Minimum value.</summary>
    Min,
    /// <summary>Maximum value.</summary>
    Max,
    /// <summary>Count distinct values.</summary>
    CountDistinct
}

/// <summary>
/// Internal holder for Include expression data.
/// </summary>
internal class IncludeExpressionHolder
{
    public string RelationshipName { get; }
    public List<string> Fields { get; }
    public SoqlCondition? Condition { get; set; }
    public int? Limit { get; set; }

    public IncludeExpressionHolder(string relationshipName, List<string> fields)
    {
        RelationshipName = relationshipName;
        Fields = fields;
    }
}

/// <summary>
/// Equality comparer that uses the Id property of Salesforce objects.
/// Used by Union/Except/Intersect operations for set-based comparison.
/// </summary>
internal class IdBasedEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    private static readonly PropertyInfo? IdProperty = typeof(T).GetProperty("Id");

    public bool Equals(T? x, T? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        
        if (IdProperty == null)
        {
            // Fall back to reference equality if no Id property
            return ReferenceEquals(x, y);
        }

        var xId = IdProperty.GetValue(x);
        var yId = IdProperty.GetValue(y);
        
        if (xId == null && yId == null) return true;
        if (xId == null || yId == null) return false;
        
        return xId.Equals(yId);
    }

    public int GetHashCode(T obj)
    {
        if (obj == null) return 0;
        
        if (IdProperty == null)
        {
            return obj.GetHashCode();
        }

        var id = IdProperty.GetValue(obj);
        return id?.GetHashCode() ?? 0;
    }
}
