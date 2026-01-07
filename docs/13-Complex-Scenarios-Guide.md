# Complex Scenarios Guide

Patterns for more advanced SalesforceCore usage.

## Multi-Object Workflows
- Use Composite Graph for dependent operations (e.g., create Account then Contacts).
- Prefer Bulk API for >200 records; Composite for smaller, mixed operations.

## Large Data Moves
- Stream results with `QueryAllAsyncEnumerable` to avoid high memory usage.
- Use Bulk Query for exports; process CSV incrementally.
- Tune `BulkJobTimeout` and `BulkPollInterval` per workload.

## Polymorphic Relationships
- Resolve object types with `IDataService.ResolvePolymorphicTypeAsync`; cache prefixes.
- Hydrate lookups using `BatchResolveLookupAsync` grouped by target type.

## Offline/Sync
- Use `IReplicationService.GetUpdatedAsync` and `GetDeletedAsync` with time windows.
- Store watermarks per object; handle overlapping windows to avoid gaps.

## Validation Pipelines
- Keep `EnforceFieldLevelSecurity` and `ValidateSoqlInputs` enabled.
- Use `[SalesforceValidate]` on controllers to run validation engine before save.

## Performance
- Cache describes (`CacheProvider = Distributed`) and tune durations.
- Reuse HttpClient via DI; avoid per-request client creation.
- Adjust retries/backoff to respect Salesforce rate limits; monitor 429 headers.

## Security/Compliance
- Avoid logging PII or tokens; scrub payloads.
- Use Redis with TLS for distributed caches; set `CacheKeyPrefix` by environment.
- Rotate JWT private keys and client secrets regularly.

## Testing Strategies
- Mock `ISalesforceClient`/service interfaces for unit tests.
- For integration, target a sandbox; use dedicated test users and data seeds.

## Next Steps
- Bulk/Composite specifics: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md).
- Security: [09-Security.md](09-Security.md).
- Infrastructure: [16-Backbone-Infrastructure.md](16-Backbone-Infrastructure.md).

Session timestamp: 2025-12-25T23:00:00Z
