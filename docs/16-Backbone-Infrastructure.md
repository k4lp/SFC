# Backbone Infrastructure

Supporting services that power validation, change tracking, schema diffing, and FLS enforcement.

## Components
- **Validation Engine (`IValidationRuleEngine`)**: Run business rules before hitting the API.
- **Change Tracker (`IChangeTracker`)**: Tracks entity state to send only modified fields.
- **Record Type Manager (`IRecordTypeManager`)**: Resolves record types and picklist contexts.
- **Schema Diff (`ISchemaDiffService`)**: Compare object schemas across environments.
- **Field-Level Security (`IFieldLevelSecurityService`)**: Enforce FLS on read/write.
- **Field History (`IFieldHistoryService`)**: Query field history objects.

## Usage Patterns
- **Pre-save validation**: Decorate actions with `[SalesforceValidate]` or call the rule engine directly.
- **Optimized updates**: Use `IChangeTracker` to capture before/after and send minimal payloads to `IDataService`/`ITypedDataService`.
- **Deployments**: Use `ISchemaDiffService` to detect metadata drift between orgs.
- **UI picklists**: Combine `IRecordTypeManager` with tag helpers to render context-aware picklists.

## Recommendations
- Keep `EnforceFieldLevelSecurity` enabled; rely on FLS service for server-side checks.
- Persist change-tracking state per request scope; avoid static/global usage.
- Cache schema metadata to speed up validation and diff operations.
- Register `IUserContextProvider` in non-ASP.NET hosts so permission/visibility-aware services evaluate correctly; ASP.NET hosts include this via `AddSalesforceCoreMvc`.
- Record-type-aware picklists are resolved via `IRecordTypeManager`/`ISchemaService` using Salesforce UI API; results are cached per user and record type.

## Next Steps
- Validation in MVC: [12-Custom-MVC-Guide.md](12-Custom-MVC-Guide.md).
- Security posture: [09-Security.md](09-Security.md).
- Bulk/Composite workflows: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md).
