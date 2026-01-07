# Additional Services

Supporting services beyond core CRUD and bulk operations.

## Search (`ISearchService`)
- SOSL search via raw string or fluent builder.
- Example:
```csharp
using SalesforceCore.Services.Data;
using SalesforceCore.Models.Data;

var builder = searchService.CreateBuilder()
    .Find("Acme*")
    .In(SearchScope.AllFields)
    .Returning("Account", "Id", "Name")
    .WithLimit(25);

var results = await searchService.SearchAsync(builder);

// Access search record fields
foreach (var record in results.SearchRecords)
{
    var id = record.Id;
    var type = record.Attributes?.Type;
    var name = record.GetValue<string>("Name");
}
```

## JSON Node Utilities (`JsonNodeExtensions`)

Extension methods for safe value extraction from `JsonNode`. Handles ISO 8601 date strings correctly.

```csharp
using SalesforceCore.Utilities;
using System.Text.Json.Nodes;

// Parse DateTime from JSON (handles Salesforce ISO 8601 format)
DateTime? createdDate = jsonNode["CreatedDate"].ParseDateTime();

// With default value
DateTime created = jsonNode["CreatedDate"].ParseDateTimeOrDefault(DateTime.MinValue);

// DateTimeOffset support
DateTimeOffset? dto = jsonNode["SystemModstamp"].ParseDateTimeOffset();
```

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ParseDateTime()` | `DateTime?` | Parses ISO 8601 string to UTC DateTime |
| `ParseDateTimeOrDefault(defaultValue)` | `DateTime` | Returns default if null/invalid |
| `ParseDateTimeOffset()` | `DateTimeOffset?` | Parses with timezone info |
| `GetValueSafe<T>(defaultValue)` | `T` | Generic safe value extraction |


## Limits (`ILimitsService`)
- Read API limits and warnings.
- Example:
```csharp
using SalesforceCore.Services.Core;

var warnings = await limitsService.CheckLimitsAsync(80);
```

## Replication (`IReplicationService`)
- Change tracking via `GetUpdatedAsync` and `GetDeletedAsync`.
- Example:
```csharp
var windowStart = DateTime.UtcNow.AddHours(-1);
var updated = await replicationService.GetUpdatedAsync("Account", windowStart, DateTime.UtcNow);
```

## Tooling (`IToolingService`)
- Execute anonymous Apex, query tooling objects, fetch debug logs, manage Apex classes.
- Example:
```csharp
var result = await toolingService.ExecuteAnonymousAsync("System.debug('hi');");
```
```csharp
var logs = await toolingService.GetDebugLogsAsync();
var logBody = await toolingService.GetDebugLogAsync(logs[0].Id);
```

## Reports/Analytics (`IReportService`)
- Run reports and retrieve results programmatically.
- Example:
```csharp
var report = await reportService.RunAsync("00OXXXXXXXXXXXX");
```

## Apex REST (`IApexService`)
- Call custom Apex REST endpoints with strongly-typed helpers.
- Example:
```csharp
var response = await apexService.PostAsync<MyResponse>("MyNamespace/MyEndpoint", payload);
```

## Next Steps
- Core data: [04-Data-Service.md](04-Data-Service.md).
- Typed LINQ: [05-Typed-Data-Service.md](05-Typed-Data-Service.md).
- Security: [09-Security.md](09-Security.md).
