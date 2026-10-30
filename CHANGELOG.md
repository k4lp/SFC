# Changelog

## 2.1.0 — 2026-10-30

Robustness release. Source-compatible for almost all apps; see [MIGRATION.md](MIGRATION.md#21) for the few signature changes.

### Architecture
- **Query no longer depends on Data.** New `SalesforceCore.Query.ISoqlQueryExecutor` (`QueryAsync`, `QueryAllAsync`,
  `QueryAllAsyncEnumerable`) is all the LINQ provider needs; `IDataService` now extends it. `SalesforceQueryable<T>`,
  `SalesforceQueryProvider` and the `Query<T>()` extension accept any executor (cache, replay log, test fake).
- **Mapper mode is no longer only process-global.** `SalesforceMapper.UseFieldAttributeMode(bool)` overrides the
  `[SalesforceField]`-required mode for the current async flow (per client/request/test). Metadata is cached per (type, mode).
- `ModelGenerator` reuses the core `SObjectDescribe`/`SObjectField`/`GlobalDescribeResult` models (duplicate classes removed)
  and has a new `ModelGenerator(ISalesforceClient)` constructor.
- New shared `UserCachePartition` helper; `SalesforceOptions.PartitionLookupCacheByUser` (default `true`).
- `sf-search-url` attribute on `<sf-lookup>` (default `/Lookup/Search`) for apps under a path base.

### Fixed
- Lookup search results were cached **across users** although Salesforce sharing makes them user-specific.
- Lookup cache key ignored `SearchFields`/`ContextFields` and used `SoqlCondition.GetHashCode()` (per instance), so
  different searches shared entries and equal filters never hit; keys are now hashed and bounded.
- Lookup search treated `%`/`_` typed by the user as LIKE wildcards; `HasMore` used the unclamped limit; cancellation was
  swallowed and returned an empty result.
- Pagination: a `nextRecordsUrl` pointing back at itself looped forever; a page with `records: null` threw.
- `TypedDataService.UpsertAsync` did query-then-create (duplicate records under concurrency); now the atomic
  external-id `PATCH`.
- `FileService`: ids were interpolated into REST paths unvalidated; a non-seekable upload stream was buffered without
  bound; `success:false` with no errors returned an empty id; `VersionNumber` (a string in Salesforce) threw on parse;
  dot-files got an empty title.
- Schema cache keys were case-sensitive (`Account` vs `account` cached twice; invalidating one left the other stale).
- `DistributedCacheTicketStore` ignored the ticket's `ExpiresUtc`, threw on corrupt bytes, and lacked the
  `CancellationToken` overloads of `ITicketStore`.
- `EncryptedSqlServerCache` swallowed `OperationCanceledException` (returned `null`); sub-second sliding expirations were
  truncated to 0 so entries expired on write; sync methods could deadlock under a `SynchronizationContext`.
- Sync LINQ (`ToList()`, `First()`, …) could deadlock under a `SynchronizationContext`; it now offloads in that case.
- Model generator emitted Newtonsoft `[JsonProperty]` into files that import System.Text.Json (did not compile),
  `RelationshipName = ""` on lookups, accepted an unvalidated namespace and object name (code/path injection).
- `ApexService`: percent-encoded `..`/`/`/`\` were not rejected; `null`/empty query parameters threw or appended `?`.
- Tag helpers: `<sf-field-group>`/`<sf-model-form>` interpolated names, classes and record-type ids into attributes
  unencoded; numbers/dates were rendered in the current culture (e.g. `1,5`, Buddhist years) which inputs reject;
  `sf-cancel-url` accepted `javascript:`; the antiforgery field was always empty; multi-select picklists bound to a
  collection pre-selected nothing.

### Added
- `CancellationToken` on `ISchemaService.InvalidateCacheAsync`, `ModelGenerator.GenerateAsync/ListObjectsAsync/DescribeObjectAsync`,
  and the `ITicketStore` overloads.
- 116 tests (781 → 897): LINQ over a custom executor and under a single-threaded context, pagination edge cases, files,
  lookups, typed data, schema cache, mapper scopes, SQL cache crypto/expiry/cancellation, ticket store, all form tag helpers,
  model generator codegen. The old Apex test with no assertions now asserts the endpoint. Line coverage 38.4% → 46.4%.

## 2.0.0 — 2026-10-25

Breaking architecture release. See [MIGRATION.md](MIGRATION.md).

### Removed
- All access gating: FLS filtering (`EnforceFieldLevelSecurity`, `FlsEnforcementMode`, `SanitizeFieldListWithFlsAsync`, `FieldLevelSecurity`),
  visibility engine (`IVisibilityService`, handlers, `VisibilityOptions`, `VisibilityPolicy`), permission guards, `[SalesforceAuthorize]`,
  `VisibilityTagHelper`, and controller-side 403 pre-checks. Salesforce enforces access; errors are typed.
- EF Core SqlServer and Pkcs dependencies from the core package.
- Dead/duplicate types: `BatchResult`/`BatchItemResult` in `QueryResult.cs`, `Services.Data.RecordAttributes`, AspNetCore `FormMode` duplicates.

### Added
- `SalesforceCore.Abstractions` package (contracts, `SalesforceRequest`/`SalesforceResponse`, exceptions, `SalesforceErrorMapper`, `SalesforceApiVersion`).
- `SalesforceCore.SqlServer` package (SQL Server caches, `SqlAppLockProvider`).
- `ISalesforceClient.SendAsync`, `SalesforceLoggingHandler`, `ISalesforceApiUsage` (`Sforce-Limit-Info`).
- `AddSalesforceHttpHandler<T>()`, `ConfigureSalesforceHttpClient()`, `AddSalesforceBackgroundTokenRefresh()`, options validator and post-configure.
- `SalesforceConnectionException`, `SalesforceServerException`; `Errors`, `RequestId`, `IsTransient` on all exceptions.
- Test kit (`FakeSalesforceServer`, `SalesforceTestHost`, `DescribeBuilder`) and 83 new tests (698 → 781).

### Fixed
- Default resilience pipeline failed Polly validation (30s sampling vs 30s attempt timeout) — no client could be created with defaults.
- HTTP settings in the `AddSalesforceCore(Action)` overload were ignored; `HttpTimeout` was overwritten by the resilience handler.
- `MaxRetries = 0` still retried.
- Bulk 2.0 close/abort used `POST` instead of `PATCH`; unvalidated job ids; unescaped `locator`.
- Apex/`nextRecordsUrl` path traversal hardening; composite graph builder hard-coded API version.
- 401 refresh now replays the original body exactly once; network failures/timeouts are typed.
- Duplicate pipeline registration when `AddSalesforceCore` was called twice.

## 1.3.0
Security scan fixes — see `docs/security/SFC_Security_Scan_Report.md`.
