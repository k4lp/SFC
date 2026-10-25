# Changelog

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
