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
