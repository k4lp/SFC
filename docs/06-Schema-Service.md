# Schema Service (`ISchemaService`)

Metadata discovery, field validation, and lookup helpers. Use this to drive dynamic UIs and enforce object rules.

## Requirements
- **Required**: Authenticated client; `Salesforce` configuration; .NET 10.
- **Recommended**: Distributed cache enabled (`CacheProvider = Distributed`) to cache describes and picklists.
- **Optional**: Prefix map caching for polymorphic lookups.

## Common Calls
```csharp
var describe = await schemaService.GetDescribeAsync("Account");
var fields = await schemaService.GetQueryableFieldsAsync("Contact");
var nameField = await schemaService.GetNameFieldAsync("Case");
var sanitized = await schemaService.SanitizeFieldListAsync("Lead", requestedFields);
```

## Helpers
- **Describe**: `GetDescribeAsync(string sObject)` returns object metadata (fields, permissions).
- **Field Lists**: `GetCreateableFieldsAsync`, `GetUpdateableFieldsAsync`, `GetQueryableFieldsAsync`.
- **Name Field**: `GetNameFieldAsync` resolves the display/name field.
- **Field Map**: `GetFieldMapAsync` dictionary by field name.
- **Sanitize Fields**: Validate/trim requested fields against schema (prevents invalid SOQL).
- **Polymorphic IDs**: `IDataService.ResolvePolymorphicTypeAsync` and `IDataService.BatchResolvePolymorphicTypesAsync`.

## Usage Patterns
- **Dynamic Forms**: Use createable/updateable lists to render inputs; Salesforce rejects writes the user is not allowed to make.
- **List Views**: Sanitize user-selected fields via `SanitizeFieldListAsync` or `SanitizeFieldListWithFlsAsync`.
- **Lookup Resolution**: Combine `GetNameFieldAsync` with `IDataService.BatchResolveLookupAsync`.

## Caching
- Controlled by `Salesforce:SchemaCacheDuration` and cache provider selection (`CacheProvider`).
- Entity prefix map is cached to resolve polymorphic IDs for custom objects.

## Safety
- Always sanitize client-provided field lists before executing SOQL.
- Respect createable/updateable flags to avoid `INSUFFICIENT_ACCESS` errors.

## Next Steps
- Data operations: [04-Data-Service.md](04-Data-Service.md).
- Typed operations: [05-Typed-Data-Service.md](05-Typed-Data-Service.md).
- Bulk/Composite: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md).

Session timestamp: 2025-12-25T23:00:00Z
