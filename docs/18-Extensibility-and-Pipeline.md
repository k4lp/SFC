# Extensibility and the HTTP pipeline (2.0)

## Composition

```csharp
builder.Services.AddSalesforceCore(builder.Configuration);           // or AddSalesforceCore(o => { ... })
builder.Services.AddSalesforceHttpHandler<MyCorrelationHandler>();    // DelegatingHandler in the Salesforce pipeline
builder.Services.ConfigureSalesforceHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }));
builder.Services.AddSalesforceTokenProvider<MyTokenProvider>();       // replaces the configured provider
```

Every service is registered with `TryAdd*`: register your own `IDataService`, `ISchemaService`, cache provider, etc.
**before** `AddSalesforceCore` to replace the default.

Pipeline order (outer → inner): resilience (Polly standard handler: total timeout, retry with `Retry-After`, circuit breaker,
attempt timeout) → `SalesforceLoggingHandler` → `SalesforceApiUsageHandler` → your handlers → primary handler.

## Raw requests

```csharp
var response = await client.SendAsync(
    SalesforceRequest.Post("sobjects/Account", new { Name = "Acme" }).WithHeader("Sforce-Auto-Assign", "FALSE"));
response.Status;      // 201
response.RequestId;   // Sforce-Request-Id
response.AsJson();    // JsonNode
```

Relative paths resolve against `/services/data/{ApiVersion}/`; paths starting with `/services/` are used as-is.
`Authorization`/`Host` cannot be overridden and absolute URLs are rejected, so the bearer token only reaches the validated instance.
Set `ThrowOnError = false` to receive error responses instead of exceptions.

## Errors

`SalesforceErrorMapper.FromResponse(...)` maps status + Salesforce error codes to:
`SalesforceAuthException` (401), `SalesforcePermissionException` (403), `SalesforceNotFoundException` (404),
`SalesforceFieldSecurityException` (`INVALID_FIELD_FOR_INSERT_UPDATE`, ...), `SalesforceValidationException`, `SalesforceDuplicateRuleException`, `SalesforceQueryException`,
`SalesforceRecordLockedException` (transient), `SalesforceRateLimitException` (transient, `RetryAfter`),
`SalesforceServerException` (5xx), `SalesforceConnectionException` (network/timeout). All carry `ErrorCode`, `Errors`, `RequestId`, `IsTransient`.

## API usage

`ISalesforceApiUsage` (singleton) exposes the last `Sforce-Limit-Info` `api-usage=used/max` seen by any request.

## Testing

`tests/SalesforceCore.Tests/TestKit` contains `FakeSalesforceServer` (route table + request journal), `SalesforceTestHost`
(the real composition root wired to the fake server) and `DescribeBuilder`. Copy it into your own test project to test
code that uses SalesforceCore without an org.

## 2.1: plug-in query executors and per-flow mapping mode

The LINQ provider depends only on `SalesforceCore.Query.ISoqlQueryExecutor`, so you can back
`SalesforceQueryable<T>` with anything that runs SOQL: a caching decorator, a replay log, a fake in tests.

```csharp
public sealed class CachingExecutor(IDataService inner, IMemoryCache cache) : ISoqlQueryExecutor
{
    public Task<QueryResult> QueryAsync(string soql, CancellationToken ct = default)
        => cache.GetOrCreateAsync(soql, _ => inner.QueryAsync(soql, ct))!;
    public Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken ct = default) => inner.QueryAllAsync(soql, ct);
    public IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, CancellationToken ct = default) => inner.QueryAllAsyncEnumerable(soql, ct);
}

var accounts = await new CachingExecutor(data, cache).Query<Account>().Where(a => a.Industry == "Tech").ToListAsync(ct);
```

`SalesforceOptions.RequireSalesforceFieldAttribute` sets the process-wide default. To use the other mode for one
client, request or test without affecting anything else:

```csharp
using (SalesforceMapper.UseFieldAttributeMode(requireSalesforceFieldAttribute: true))
{
    var rows = await typed.GetAllAsync<Contact>(ct); // only [SalesforceField] properties are selected
}
```
