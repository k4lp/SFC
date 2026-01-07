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

## Field-Level Security (FLS) Methods

> [!IMPORTANT]
> When `EnforceFieldLevelSecurity` is enabled (default: `true`), use these FLS-aware methods instead of the standard field list methods.

### GetAccessibleFieldsAsync

Returns only fields the current user has **read access** to:

```csharp
// Get FLS-enforced readable fields
var accessibleFields = await schemaService.GetAccessibleFieldsAsync("Account");

// Use instead of GetQueryableFieldsAsync when FLS is enabled
var queryableFields = _options.EnforceFieldLevelSecurity
    ? await schemaService.GetAccessibleFieldsAsync(sObject)
    : await schemaService.GetQueryableFieldsAsync(sObject);
```

### SanitizeFieldListWithFlsAsync

Sanitizes a field list ensuring only **accessible** fields are included:

```csharp
// More restrictive than SanitizeFieldListAsync - also checks read access
var safeFields = await schemaService.SanitizeFieldListWithFlsAsync("Contact", requestedFields);

// Use in controllers/services
var validFields = _options.EnforceFieldLevelSecurity
    ? await schemaService.SanitizeFieldListWithFlsAsync(sObject, requestedFields)
    : await schemaService.SanitizeFieldListAsync(sObject, requestedFields);
```

> [!TIP]
> When writing unit tests, mock both `SanitizeFieldListAsync` **and** `SanitizeFieldListWithFlsAsync` to avoid `ArgumentNullException` when FLS is enabled.

## Usage Patterns
- **Dynamic Forms**: Use createable/updateable lists to render inputs; respect `EnforceFieldLevelSecurity`.
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
