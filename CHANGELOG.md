# Changelog

## 2.5.0 — 2026-11-04

Interaction audit of the 2.1–2.4 fixes: each fix was exercised together with the features it touches (retries x
tokens x idempotency, bulk x paging x size x gzip x cancellation, batching x chunk limits x failure indexes x culture,
caches x layout x concurrency, LINQ x enums). New scenario suites: `tests/SalesforceCore.E2E/Round4InteractionE2ETests.cs`
(12) and `tests/SalesforceCore.Tests/V25/InteractionScenarioTests.cs` (10); all 12 E2E scenarios fail on 2.4.0. See
[MIGRATION.md](MIGRATION.md#250).

### Fixed
- **Layout/permission descriptors could hang forever.** Cache stampede locks were 32 stripes picked by
  `key.GetHashCode()`; a layout or permission factory loads describes through the same cache, and when the inner key
  hashed to the outer key's stripe the second wait never completed. String hashes are randomised per process, so it
  hung on some starts only (reproduced: 40 forms, hang). Locks are now per key, so nested `GetOrCreateAsync` is safe.
- **Cache single-flight did not work across requests.** The cache provider is scoped, so each request scope had its own
  locks and N concurrent requests described the same object N times. Locks are now process-wide (1 describe for 10 scopes).
- **Bulk results over 10 MB failed** (query pages, success/failure files): they were buffered under
  `MaxResponseContentBufferSize`. Because an ingest job had already committed, a caller that retried on the error
  duplicated inserts. CSV results are now streamed (`SalesforceRequest.UnboundedResponse`); works with gzip and a
  mid-flight token refresh.
- **Cancelling a bulk job left it running.** The cleanup abort was sent with the caller's (already cancelled) token, so
  it never left the process; bulk query had no cleanup at all. Aborts now use their own 30 s token. A job that already
  completed is no longer "aborted" when reading its results fails.
- **`WaitForCompletionAsync` returned the still-running job on cancellation**, which Insert/Update/Upsert/Delete then
  reported as an empty successful result. It now throws `OperationCanceledException`.
- **Enums in record dictionaries were written as ordinals** (`["StageName"] = Stage.ClosedWon` sent `1`, which an
  unrestricted picklist stores as "1") on REST/composite, and as the member name (`ClosedWon`) in Bulk CSV, while LINQ
  filters send `'Closed Won'`. Both now send the `[SalesforceValue]` (or member name). Typed responses read picklist
  strings into enums.
- **LINQ enum aggregates returned the wrong value.** `DistinctAsync`/`MinAsync`/`MaxAsync`/`GroupBy` keys used
  `Enum.Parse`, which failed on "Closed Won" and silently returned the first member. They now map `[SalesforceValue]`;
  an unknown picklist value throws `InvalidCastException`.
- **sObject Collections over 200 records failed.** `bulkThreshold` is caller-chosen; above 200, `BatchCreate/Update/DeleteAsync`
  sent 201+ records in one call and Salesforce rejected all of them. Calls are chunked by 200; failure indexes stay
  relative to the input.
- **Composite upsert swallowed cancellation**: it was recorded as per-record failures and the remaining chunks were
  still attempted. It now propagates.
- **Composite upsert external ids used the thread culture** (`1234.5` sent as `1234,5` under de-DE).
- **A per-attempt timeout on a GET was not retried**: the custom retry predicate dropped the standard handler's
  `TimeoutRejectedException` case. Idempotent requests retry it again; POST/PATCH still never replay.
- Bulk CSV: `NaN`, infinity and doubles beyond decimal range throw `ArgumentException` instead of `OverflowException`.

### Added
- `SalesforceRequest.UnboundedResponse` (read the body from the stream, bypassing `MaxResponseContentBufferSize`).
- `SalesforceEnumJsonConverterFactory` (picklist-value enum JSON), used by `SalesforceClient` request/response options.

## 2.4.0 — 2026-11-02

Second end-to-end round against the fake Salesforce (`tests/SalesforceCore.E2E`, now 30 tests). See
[MIGRATION.md](MIGRATION.md#240).

### Fixed
- **JWT bearer flow failed on every token refresh after the first** with `ObjectDisposedException`: the RSA key was
  disposed after signing while IdentityModel cached a signature provider bound to it. Refreshes now sign correctly
  (verified: cold start and three 401 storms of 20 parallel calls, one token request each).
- **Files over 10 MB could not be downloaded** (`DownloadAsync`/`DownloadToStreamAsync` hit HttpClient's 10 MB buffer
  limit although uploads up to 25 MB are allowed). Downloads now stream; `DownloadToStreamAsync` no longer buffers.
- **`GetDescribeAsync` returned `null` on any error** (503, auth failure, open circuit), indistinguishable from an
  unknown object; `GetAllObjectsAsync` returned an empty list. Only not-found returns `null` now; everything else throws.
- **Typed responses bound case-sensitively**: Apex REST returning `{"accountName":..}` left a `AccountName` DTO property
  null. Typed deserialisation is now case-insensitive.
- **Bulk update/upsert silently ignored `null`** (empty cell = unchanged in Bulk 2.0) while REST/composite cleared the
  field. Nulls are now sent as `#N/A`; opt out with `BulkNullsAsNA = false`.

### Added
- `SalesforceOptions.EnableResponseCompression` (default off): gzip responses via `SalesforceCompressionHandler`.
- `SalesforceOptions.BulkNullsAsNA` (default on).
- E2E coverage for the distributed ticket store (Data Protection ciphertext, foreign key ring rejected) and
  `DistributedCacheProvider` (round trip, single-flight `GetOrCreateAsync`), global-describe caching and host disposal.

## 2.3.0 — 2026-11-02

End-to-end audit of the whole library against a fake Salesforce served by ASP.NET Core TestServer and the real DI
pipeline (new `tests/SalesforceCore.E2E` project). See [MIGRATION.md](MIGRATION.md#230).

### Fixed
- **POST/PATCH were retried on 5xx, 408 and connection errors**, so a create whose response was lost at a gateway
  created a duplicate record (reproduced: 1 call, 2 records). POST/PATCH now retry only on 429.
- **Bulk query returned only the first page**: `IBulkService.QueryAsync` ignored `Sforce-Locator`. All pages are now
  fetched and merged under one header.
- **Bulk CSV used the thread culture**: under de-DE `1234.5` was sent as `"1234,5"` and dates as `02.01.2024 03:04:05`.
  Values are now invariant (ISO-8601 UTC, `true`/`false`).
- **Bulk jobs that ended `Failed`/`Aborted` returned an empty success** (0 ok, 0 failed, no error). They now throw with
  Salesforce's `errorMessage`.
- **`BatchCreate/Update/Upsert/DeleteAsync` on the Bulk path reported the wrong failure `Index`** (row number in the
  failure file, 1-based). It is now the index into the caller's input, like the composite path.
- **Bulk result values were trimmed**, altering data with leading/trailing spaces.
- **Composite `allOrNone: true` over 25 records was split into several calls**, so earlier calls committed while a later
  one rolled back. It now throws `ArgumentException`.
- **Late 401s triggered extra token refreshes**: a request whose 401 arrived after another request had refreshed now
  reuses the new token instead of authenticating again.
- **`Retry-After` from `SalesforceExceptionMiddleware`** was `TotalSeconds.ToString()` (`1.5`, or `1,5` under de-DE). It
  is now whole seconds, rounded up.
- **`FileService` ignored `SalesforceOptions.MaxFileUploadSize`** (hard-coded 25 MB).
- **Model generator**: a field named like its object (`Invoice__c.Invoice__c`) produced CS0542 and duplicate sanitised
  names collided; `Id` produced CS8618; picklist attributes stopped at 20 values. Generated code now compiles warning-free
  (verified with Roslyn in the E2E suite).

### Tests
- New `tests/SalesforceCore.E2E` (18 tests): fake REST/OAuth/Bulk 2.0/Composite server, real `AddSalesforceCore`
  client-credentials pipeline, Roslyn compilation of generated models. 963 unit tests unchanged in count; one test that
  asserted the split-allOrNone behaviour was corrected.

## 2.2.1 — 2026-11-02

Revalidation of the 2.2.0 LINQ release. No API changes. Queries that silently returned wrong data now either return
the right data or throw a clear `NotSupportedException` before any request. See [MIGRATION.md](MIGRATION.md#221).

### Fixed
- **Non-nullable enum comparisons sent the ordinal** (`StageName = 1`) instead of the picklist value
  (`StageName = 'Closed Won'`). Nullable enums and IN lists were already correct.
- **`enumArray.Contains(x.Field)` threw**: C# 14 binds it to `MemoryExtensions.Contains(span, value, comparer = null)`
  (three arguments), which the IN-list translator didn't recognise.
- **Inline semi-joins crashed** with `InvalidCastException`: `Where(a => data.Query<Contact>().Select(c => c.AccountId).Contains(a.Id))`
  was mistaken for a projection of the outer query, and the inner lambda's parameter blocked client-side evaluation.
- **`double`/`float` literals used exponent form** (`1E-07`, `1E+20`), which SOQL rejects.
- **Field-to-field comparisons** (`a.Name == a.Id`) produced invalid SOQL (`Name = Id`) although the docs said they throw.
- **`Where`/`OrderBy`/predicate terminals after `Take`/`Skip`** were merged into one SOQL statement, changing which rows
  came back (`Take(5).Where(p)` returned 5 matching rows; `Skip(3).Count(p)` returned `count(p) - 3`). They now throw.
- **Aggregates after `Take`/`Skip`** (`OrderByDescending(x).Take(10).SumAsync(...)`) silently aggregated every row.
  They now throw.
- **`Skip(k)` with `k > 2000`** now throws before sending (Salesforce's OFFSET maximum).
- Tests that asserted the old aggregate-ignores-Take behaviour were corrected; 9 regression tests added (963 total).

## 2.2.0 — 2026-10-30

LINQ release. Source-compatible; a few results change because they were wrong before. See
[MIGRATION.md](MIGRATION.md#22). Full guide: [docs/19-LINQ-to-SOQL.md](docs/19-LINQ-to-SOQL.md).

### Fixed
- `Take(n).Skip(k)` sent `LIMIT n OFFSET k` (n rows after k), LINQ means `LIMIT n-k`; `Take(a).Take(b)` used the last
  value instead of the minimum; `Skip(a).Skip(b)` used the last value instead of the sum.
- `Count()`/`CountAsync()` after `Skip`/`Take` downloaded every row to count them; it now sends one `SELECT COUNT()`
  and clamps (`clamp(total - k, 0, n)`). `Any()` after `Skip(k)` ignored the skip.
- `SumAsync`/`AverageAsync`/`MinAsync`/`MaxAsync` cut the SOQL text at `" WHERE "` and kept `ORDER BY`/`LIMIT` (invalid
  in aggregate queries); `GroupBy*Async` broke on string literals containing `" WHERE "`/`" ORDER BY "`; `DistinctAsync`
  used only the last `Where` and ignored `WhereCondition`. All now read the filter from the expression tree.
- `AggregateResult` values were re-wrapped as `JsonNode` (a switch-expression implicit conversion), so `Get<decimal>()`,
  `Get<int?>()` and `Count` returned 0/default for every aggregate. `Get<T>` also failed for nullable targets.
- `5 < x.Field` (constant on the left) produced invalid SOQL; it is now flipped to `Field > 5`.
- Parameter-free calls such as `DateTime.UtcNow.AddDays(-7)`, `new DateOnly(...)`, `a * b`, ternaries threw
  "not supported"; they are now evaluated client-side and sent as literals.
- `x.Nullable == null` with a `Convert` node and `x.Nullable.Value.Year` / `OrderBy(x => x.Date.Value)` produced
  `Field.Value` paths.
- `Select` with computed expressions (`x.First + " " + x.Last`, `new { Big = x.N > 5 }`) threw; they now select every
  field read and project client-side.
- `ToListAsync`/`ToListAllAsync`/`ToAsyncEnumerable` required `class, new()`, so projections to strings, numbers or
  anonymous types could not run asynchronously.
- `ElementAtAsync` after `Take` could return a row past the `Take`.

### Added
- `SoqlFunctions`: date literals (`Literal`, `LiteralDate`, incl. `LAST_N_DAYS:n`), date functions (`CalendarYear`,
  `CalendarMonth`, `CalendarQuarter`, `FiscalYear/Quarter/Month`, `WeekInYear/Month`, `DayOnly`, `DatePart`,
  `ConvertTimezone`), `Includes`/`Excludes`, `ToLabel`, raw `Like`, `Distance` (Where and OrderBy), `In`/`NotIn`.
- Semi-joins and anti-joins: `sub.Contains(x.Id)`, `!sub.Contains(x.Id)`, `SoqlFunctions.In/NotIn` → `IN (SELECT …)`.
- `!list.Contains(x.F)` → `NOT IN (…)`; `string.IsNullOrEmpty/IsNullOrWhiteSpace(x.F)` → `F = NULL`; `HashSet`/any
  `IEnumerable` and C# 14 span-bound `array.Contains`.
- `NullsFirst()` / `NullsLast()`; `WithSecurityEnforced()`, `WithUserMode()`, `WithSystemMode()` (also applied to
  Count/Any/aggregates) and the same on `SoqlBuilder`.
- `IncludeDeleted()` → REST `queryAll`; new optional `ISoqlDeletedRowsQueryExecutor` (implemented by `DataService`:
  `QueryIncludingDeletedAsync`, `QueryAllIncludingDeletedAsyncEnumerable`).
- `AggregateAsync(g => g.GroupBy(...).Rollup()/Cube().Count().Sum().Average().Min().Max().CountDistinct()
  .Having(...).HavingCount(...).OrderBy(...).OrderByAggregate(...).Limit(...))`, `ToAggregateSoql`, `CountDistinctAsync`.
- Async terminals on any Salesforce queryable (entities and projections): `AnyAsync`, `CountAsync(predicate)`,
  `LongCountAsync`, `FirstAsync`, `FirstOrDefaultAsync(predicate)`, `SingleAsync`, `SingleOrDefaultAsync`; sync `LongCount`.
- `ToSoql()` for projections (`IQueryable<T>` extension).
- Clearer `NotSupportedException` messages that name the expression and point to the LINQ guide.
- `docs/19-LINQ-to-SOQL.md`: operator reference and LINQ→SOQL translation table.

### Changed
- `SalesforceQueryable.cs` (2,650 lines) split into `SalesforceQueryable.cs`, `SalesforceQueryProvider.cs`,
  `QueryModels.cs` and partial `SalesforceQueryExtensions.*.cs` files (no API change).
- `Take(0)` returns an empty result without a request.
- 57 tests (897 → 954). LINQ layer (`src/SalesforceCore/Query`) line coverage 54.4% → 82.9%
  (`SalesforceQueryProvider` 65.3% → 88.2%); overall 46.4% → 49.8%.

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
