# Migrating to SalesforceCore 2.2 – 2.8

<a id="280"></a>
## 2.8.0

- `IApexService` gains `GetNodeAsync`; it has a default interface implementation, so custom
  implementations still compile (the default throws `NotSupportedException` until you implement it).
- External ID values containing an encoded `://` are accepted again (they were rejected in 2.7.0).
- **Behaviour change:** Salesforce HTTP clients no longer follow redirects. A redirect to another host throws
  `SalesforceConnectionException`; a same-host 3xx is returned as an error response. If you route through a proxy that
  redirects on purpose, configure the proxy instead. A primary handler that is neither `SocketsHttpHandler` nor
  `HttpClientHandler` is left as-is.
- **Behaviour change:** `sf-picklist` and `sf-field-group` take `name`/`id` from `asp-for` plus the template prefix
  (`asp-for="X" sf-field="Y"` renders `name="X"`, or `name="Deal.X"` under prefix `Deal`; it rendered `name="Y"`).
  If your controller binds the posted value by the Salesforce field name, bind the model property instead.
- `sf-field-group` emits a real `<select>` for picklist fields and the lookup widget for lookup fields. CSS that targeted
  the literal `sf-picklist` element must target `select.sf-picklist`.
- Custom `IApexService` implementations or decorators should implement/forward `GetNodeAsync`; the interface default
  throws `NotSupportedException`.

<a id="270"></a>
## 2.7.0

No API removals. Stricter input checks that can reject calls that used to be sent:
- **Report and report instance IDs** must be 15/18-character Salesforce IDs (`ArgumentException`).
- **Composite** update/delete record IDs and `ICompositeBatchBuilder.Update/Delete` IDs must be Salesforce IDs or a
  composite reference such as `@{newAccount.id}`; object names are validated.
- **Tooling** `CreateAsync/UpdateAsync/DeleteAsync/GetDebugLogContentAsync` validate object names and IDs.
- **Any request path** containing `.`/`..` segments (also `%2e%2e` or `%2F..%2F` after decoding), `\` or control
  characters throws `ArgumentException` before sending. An external ID value such as `a/../b` is therefore rejected.
- **HTTP**: `Domain` must be `https://`, and tokens are not sent over `http://`. For a local test proxy set
  `Salesforce:AllowInsecureHttp=true`. A response redirected to a different host throws `SalesforceConnectionException`.
- **Gzip** responses larger than `MaxDecompressedResponseBytes` (1 GiB) after decompression fail with
  `InvalidDataException`; raise it or set `0` for unlimited.
- **Exception messages** from non-JSON error bodies are redacted (`[REDACTED]` in place of tokens).
- **`SecurityUtils.SanitizeFileName`** also removes `\ : * ? " < > |`, control characters and leading dots on Linux, and
  prefixes Windows device names (`CON.txt` → `_CON.txt`). `FileController.Download` sends non-image/PDF types as
  `application/octet-stream`.
- Identifier validation no longer accepts a trailing newline.

<a id="260"></a>
## 2.6.0

No API removals. Behaviour changes to check:
- **Multipicklist values** from `string[]`, `List<string>` or `[Flags]` enums are now sent as `A;B`. If you worked
  around the old behaviour by joining yourself, keep passing a `string` — it is sent unchanged.
- **`SecurityUtils.SanitizeForSoql`** no longer replaces Unicode quote characters (’ ʼ) with `'` or normalises to NFC.
  Filters now match the stored text exactly. Injection safety is unchanged (backslash escaping).
- **Bulk CSV** uses LF line endings; if you post-process `BulkService` CSV, expect `\n`.
- **Typed JSON** (`GetAsync<T>`, Apex typed calls) now reads Salesforce dateTimes into `DateTime` with `Kind=Utc`, and
  writes `DateTime`/`DateOnly`/`TimeOnly` in Salesforce format (`...fffZ`, `yyyy-MM-dd`, `HH:mm:ss.fffZ`).
- **Compound fields** (`SalesforceAddress`, `SalesforceLocation`) are never written.
- New optional settings: `QueryBatchSize` (200–2000) and `ClientName`. Both off by default.

## 2.5.0

No API removals; two additive APIs (`SalesforceRequest.UnboundedResponse`, `SalesforceEnumJsonConverterFactory`).
Behaviour changes:

| Change | What to do |
|---|---|
| Enums in request bodies (record dictionaries, Apex DTOs) are written as the `[SalesforceValue]`/member name string, not the ordinal | If an Apex endpoint expects an integer, put `[JsonConverter(typeof(JsonNumberEnumConverter<T>))]` on that property. Enum types with their own `[JsonConverter]` are unchanged. |
| Bulk CSV writes enums as the `[SalesforceValue]` instead of the member name | Nothing, unless you relied on the member name. |
| `AggregateResult.Get<TEnum>` throws `InvalidCastException` for a value that matches no enum member (it returned the first member) | Add the missing member or `[SalesforceValue]`, or read the key as `string`. |
| `IBulkService.WaitForCompletionAsync` throws `OperationCanceledException` when cancelled (it returned the unfinished job) | Catch the exception instead of checking `IsComplete`. |
| Cancelled bulk ingest/query jobs are aborted server-side | Nothing. |
| `BatchCreate/Update/DeleteAsync` below `bulkThreshold` but above 200 records make several 200-record calls | Nothing; previously the call failed. |
| Cache stampede locks are process-wide and per key | Nothing. A factory must still not call `GetOrCreateAsync` for its own key. |

<a id="240"></a>
## 2.4.0

No API removals. Behaviour changes:

| Change | What to do |
|---|---|
| Bulk update/upsert sends `null` as `#N/A` (clears the field) | If you passed `null` meaning "leave unchanged", remove the key or set `Salesforce:BulkNullsAsNA` to `false`. |
| `GetDescribeAsync` throws on non-404 errors; `GetAllObjectsAsync` throws instead of returning `[]` | Catch `SalesforceException` where you treated `null`/empty as "service unavailable". |
| Typed responses bind property names case-insensitively | Nothing, unless a DTO has two properties differing only by case. |

<a id="230"></a>
## 2.3.0

No API removals. Behaviour changes:

| Change | What to do |
|---|---|
| POST/PATCH no longer retried on 5xx/408/connection errors (still on 429) | If you relied on automatic replay of creates, add your own idempotency (external-ID upsert) and retry. |
| Composite `*RecordsAsync(allOrNone: true)` with more than 25 records throws | Split into ≤25-record calls yourself, or pass `allOrNone: false`. |
| Bulk jobs ending `Failed`/`Aborted` throw `SalesforceException` | Catch it where you previously checked for an empty result. |
| Bulk-path `BatchRecordError.Index` is the input index (or `-1`) | Code that subtracted 1 from it should stop. |
| `FileService` enforces `MaxFileUploadSize` | Raise the option if you upload larger files. |
| Generated models: colliding property names get a `_1` suffix; `Id` gets `= default!` | Regenerate; the API name stays in `[SalesforceField]`. |

<a id="221"></a>
## 2.2.1

No API changes. These now throw `NotSupportedException` instead of returning wrong rows or sending invalid SOQL:
`Where`/`OrderBy`/`First(p)`/`Count(p)`/`Any(p)`/`Single(p)` after `Take`/`Skip` (move them before), aggregate helpers
after `Take`/`Skip` (remove the paging or aggregate in memory), `Skip(k > 2000)`, and field-to-field comparisons.
Non-nullable enum filters now send the picklist value instead of the ordinal, so they start matching records.

<a id="22"></a>
2.2.0 is source- and binary-compatible for typical apps. These results change because they were wrong before:

| Change | Impact |
|---|---|
| `Take(n).Skip(k)` now sends `LIMIT n-k OFFSET k`; `Take(a).Take(b)` uses `min(a, b)`; `Skip(a).Skip(b)` uses `a + b` | Matches LINQ. Code that relied on the old (wrong) row counts returns fewer/other rows. |
| `Count()` with `Skip`/`Take` sends `SELECT COUNT()` and clamps locally | Same number, no row download. |
| `AggregateResult` values are CLR values (`decimal`, `string`, `bool`, `null`) instead of `JsonNode` | `Get<T>()` and `Count` now return real values. Code that cast `result["x"]` to `JsonNode` must use `Get<T>`/the CLR value. |
| Aggregate helpers (`SumAsync`, `GroupBy*Async`, `DistinctAsync`) ignore the query's `OrderBy`/`Take`/`Skip` and keep every `Where`/`WhereCondition` | Previously the SOQL was invalid or filters were dropped. |
| Parameter-free sub-expressions are evaluated client-side | `Where(a => a.Date > DateTime.UtcNow.AddDays(-7))` now works instead of throwing. |
| `Select` with computed expressions now works (selects every field read) | Previously `NotSupportedException`. |
| `ToListAsync`/`ToListAllAsync`/`ToAsyncEnumerable` (`IQueryable<T>` extensions) no longer require `T : class, new()` | Source-compatible; projections to scalars/anonymous types can run async. |
| `Take(0)` returns empty without a request | Previously sent `LIMIT 0`. |
| `ISoqlDeletedRowsQueryExecutor` is new and optional | Only needed by custom executors that want `IncludeDeleted()`. `ISoqlQueryExecutor` is unchanged. |
| `SalesforceQueryable.cs` split into several files | Source layout only; types, namespaces and members are unchanged. |

---

# Migrating to SalesforceCore 2.1

<a id="21"></a>
2.1.0 is source-compatible for typical apps. Recompile; only these edge cases need attention:

| Change | Impact |
|---|---|
| `IDataService.QueryAsync/QueryAllAsync/QueryAllAsyncEnumerable` moved to the base interface `ISoqlQueryExecutor` | Callers and Moq setups are unchanged. Binary break only: recompile assemblies that implement or call `IDataService`. |
| `SalesforceQueryable<T>`/`SalesforceQueryProvider` constructors and `Query<T>()` now take `ISoqlQueryExecutor` | `IDataService` converts implicitly — source compatible; recompile. |
| `ISchemaService.InvalidateCacheAsync(string?, CancellationToken = default)` | Custom `ISchemaService` implementations must add the parameter. |
| `ModelGenerator` describe types are now `SalesforceCore.Models.Metadata.*` (`FieldDescribe`→`SObjectField`, `PicklistValue`→`PicklistEntry`) | Only affects code that referenced the generator's own copies. |
| Lookup cache is per user by default | Set `PartitionLookupCacheByUser = false` for single-integration-user apps that want one shared cache. |
| Lookup search no longer treats `%`/`_` in the user's query as wildcards | Intended; pass a `Filter` with `SoqlCondition.Like` for explicit patterns. |
| `TypedDataService.UpsertAsync` uses the external-id `PATCH` | One request instead of two; Salesforce returns 300 if the external id is not unique. |
| `FileService` validates ids (15/18-char) | Invalid ids now throw `ArgumentException` before any request. |
| Schema cache keys are lower-cased | Old entries simply age out. |
| `SalesforceMapper.Configure` no longer clears the cache | Not needed: metadata is cached per mode. |

---

# Migrating from SalesforceCore 1.x to 2.0

2.0.0 is a breaking release. Most apps only need the steps in **1** and **2**; the rest is reference.

## 1. Access gating is gone — Salesforce enforces access

The library no longer decides what a user may see or write. Salesforce already enforces CRUD, FLS and sharing for the
authenticated user, so SalesforceCore now sends what you ask for and maps Salesforce's answer to a typed exception.

| Removed (1.x) | What to do in 2.0 |
|---|---|
| `SalesforceOptions.EnforceFieldLevelSecurity`, `FlsEnforcementMode` (+ enum) | Delete from config. Writes are not filtered; a field the user cannot edit fails with `SalesforceFieldSecurityException` (`INVALID_FIELD_FOR_INSERT_UPDATE`). Reads request the fields you list (names are still validated syntactically, `ArgumentException` on invalid names). |
| `ISchemaService.SanitizeFieldListWithFlsAsync` | Use `SanitizeFieldListAsync` / `GetQueryableFieldsAsync`. |
| `IFieldLevelSecurityService` / `FieldLevelSecurity` | Remove. Catch `SalesforceFieldSecurityException` if you need to react. |
| `IVisibilityService`, `IVisibilityRequirementHandler` + handlers, `VisibilityOptions`, `VisibilityPolicy` on `ModuleConfig`/`DynamicUiOptions` | Remove. Hide UI with your own app logic if desired; it is not a security boundary. |
| `IPermissionGuard`, `IPermissionGuardBuilder`, `PermissionGuardResult`, `PermissionServiceExtensions` | Remove. Use ASP.NET Core authorization for app-level rules. |
| `[SalesforceAuthorize]`, `VisibilityTagHelper` (`sf-visible`, `sfc-policy`) | Remove. Use `[Authorize]`/policies for app rules. |
| `SalesforceController` returning `403 Forbid` from client-side checks | It now forwards to Salesforce and returns Salesforce's error (403/400) with the typed message. |

`IPermissionService` (reading permission metadata) and the per-user describe cache remain — they are correctness, not gating.

## 2. Packages

| Package | Notes |
|---|---|
| `SalesforceCore.Abstractions` **(new)** | Dependency-free contracts: `ISalesforceClient`, `SalesforceRequest`, `SalesforceResponse`, `SalesforceException` hierarchy, `SalesforceErrorMapper`, `SalesforceApiVersion`, `QueryResult`. Namespaces are unchanged (`SalesforceCore.Services.Core`, `SalesforceCore.Models.Errors`, `SalesforceCore.Http`), so most code recompiles as-is. |
| `SalesforceCore` | Pulls Abstractions transitively. **No longer references EF Core SqlServer or `System.Security.Cryptography.Pkcs`.** |
| `SalesforceCore.SqlServer` **(new)** | SQL Server token/schema cache and `SqlAppLockProvider`. Add it if you used `Salesforce:Cache:Provider=SqlServer` or the SQL app lock. |
| `SalesforceCore.AspNetCore`, `SalesforceCore.ModelGenerator` | Version 2.0.0. |

## 3. Composition root

* `AddSalesforceCore(o => ...)`: HTTP settings set in the Action (`HttpTimeout`, `MaxRetries`, timeouts, circuit breaker)
  are now honoured — 1.x read them from configuration only.
* Calling `AddSalesforceCore` twice no longer registers a second HTTP pipeline; services use `TryAdd*`, so register your own
  implementation **before** `AddSalesforceCore` to replace one, or call `AddSalesforceTokenProvider<T>()` (it replaces the existing provider).
* `AddSalesforceCoreWithoutAuth` now registers the full service set.
* New: `AddSalesforceHttpHandler<THandler>()`, `ConfigureSalesforceHttpClient(builder => ...)`,
  `AddSalesforceBackgroundTokenRefresh()` (added automatically for JWT/ClientCredentials configs).
* Options are validated at startup (`ApiVersion`, `MaxRetries` 0–10). `ApiVersion` accepts `"v62.0"`, `"62.0"` or `"62"`
  and is normalised; the default is now `v62.0`.
* Named HttpClient: `SalesforceHttpClientNames.Api` (`"SalesforceCore.Api"`).

## 4. Resilience defaults

1.x defaulted to `CircuitBreakerSamplingDuration = 30s` and `PerAttemptTimeout = 30s`, which **fails Polly validation**
("sampling duration ... needs to be at least double of an attempt timeout") the first time a client is created. 2.0 defaults
the sampling duration to 60s and clamps it to `2 × PerAttemptTimeout`. `MaxRetries = 0` now really disables retries.
`HttpTimeout` is applied after the resilience handler as an outer limit.

## 5. Errors

* Every failure is a `SalesforceException` subtype with `ErrorCode`, `StatusCode`, `Errors`, `RequestId`, `IsTransient`.
  New: `SalesforceConnectionException` (network/timeout), `SalesforceServerException` (5xx).
  The real Salesforce error code (e.g. `APEX_ERROR`) is preserved even when the status picks the subtype.
* `BatchResult`/`BatchItemResult` in `QueryResult.cs` (dead duplicates) were removed; use `SalesforceCore.Models.Data.BatchResult`.

## 6. Transport

* `ISalesforceClient.SendAsync(SalesforceRequest)` returns a `SalesforceResponse` (status, headers, `RequestId`, body)
  for anything the typed helpers do not cover. Set `ThrowOnError = false` to inspect error responses.
* `Authorization` and `Host` headers cannot be overridden; absolute URLs are rejected (tokens only go to the validated instance).
* `SalesforceClient` no longer mutates the shared `HttpClient`. Request logging moved to `SalesforceLoggingHandler`;
  `Sforce-Limit-Info` is tracked by `ISalesforceApiUsage`.

## 7. Behaviour fixes you may notice

* Bulk 2.0 `CloseJobAsync`/`AbortJobAsync` (and query-job abort) use `PATCH` as Salesforce requires (1.x sent `POST`).
  Job ids are validated; the results `locator` is URL-escaped.
* `QueryNextAsync` keeps the API version Salesforce returned in `nextRecordsUrl`; it rejects `..`, `\`, `//` and
  non-absolute host prefixes.
* Apex REST paths reject `..`, `\`, `//` and absolute URLs.
* `CompositeGraphBuilder` uses the configured API version (`CompositeService.CreateGraphBuilder()`), validates object names and ids/`@{ref.field}`.
* Duplicate `FormMode` enums in AspNetCore and `Services.Data.RecordAttributes` were removed (use `SalesforceCore.Models.Layout.FormMode`, `SalesforceCore.Models.Data.RecordAttributes`).
* `IApexService` is registered scoped (it depends on scoped services).
* `SalesforceMapper.IsSalesforceFieldAttributeRequired` exposes the process-wide mapping switch.
