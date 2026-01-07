# Bulk & Composite Services

High-volume and batched operations: Bulk API 2.0 (`IBulkService`) and Composite API (`ICompositeService`).

## Requirements
- **Required**: Authenticated client; `Salesforce` config; .NET 10.
- **Recommended**: Tune `BulkPollInterval` and `BulkJobTimeout`; ensure retry/backoff aligns with org limits.
- **Optional**: Distributed cache for schema validation before bulk uploads.

## Quick Start: IDataService Batch Methods

For most use cases, use `IDataService` batch methods which automatically switch to Bulk API for large datasets:

```csharp
// Batch create (auto-switches to Bulk API for > 200 records)
var records = new List<Dictionary<string, object?>>
{
    new() { ["Name"] = "Account 1" },
    new() { ["Name"] = "Account 2" }
};
var result = await dataService.BatchCreateAsync("Account", records);

Console.WriteLine($"Created: {result.SuccessCount}, Failed: {result.FailureCount}");
foreach (var id in result.SuccessfulIds) Console.WriteLine($"  ID: {id}");
foreach (var err in result.FailedRecords) Console.WriteLine($"  Error: {err.Message}");

// Batch update (records must include Id field)
await dataService.BatchUpdateAsync("Account", recordsWithIds);

// Batch upsert with external ID
await dataService.BatchUpsertAsync("Account", "External_Id__c", records);

// Batch delete
await dataService.BatchDeleteAsync("Account", idsToDelete);
```

> [!TIP]
> `IDataService.BatchCreateAsync()` uses sObject Collections for < 200 records (fast), automatically switching to Bulk API 2.0 for larger datasets. This is the recommended approach for most scenarios.

## Bulk API 2.0 (`IBulkService`)
Use for large datasets with record dictionaries or CSV payloads.

### Typical Insert
```csharp
using SalesforceCore.Services.Core;

// Dictionary-based records (library converts to CSV)
var job = await bulkService.InsertAsync("Account", records);

// Raw CSV payload
var jobFromCsv = await bulkService.InsertAsync("Account", csvData);
```

### Full Control
```csharp
var create = await bulkService.CreateJobAsync(new CreateBulkJobRequest
{
    ObjectName = "Lead",
    Operation = BulkOperation.insert,
    ContentType = BulkContentType.CSV
});

await bulkService.UploadJobDataAsync(create.Id, csvData);
await bulkService.CloseJobAsync(create.Id);
var result = await bulkService.WaitForCompletionAsync(create.Id);
var successCsv = await bulkService.GetSuccessfulResultsAsync(create.Id);
```

### Query Jobs
```csharp
var job = await bulkService.CreateQueryJobAsync(new CreateBulkQueryRequest
{
    Query = "SELECT Id, Name FROM Account",
    Operation = BulkOperation.query
});
var resultsCsv = await bulkService.GetQueryResultsAsync(job.Id);
```

### Notes
- CSV must be RFC 4180 compliant.
- `InsertAsync` and `UpsertAsync` accept either record dictionaries or raw CSV.
- Respect `MaxFileUploadSize` and org bulk limits.
- Job timeouts use `BulkJobTimeout`; polling uses `BulkPollInterval`.

## Composite API (`ICompositeService`)
Use for small/medium batches or when you need partial success handling without bulk CSV.

### Example: Upsert batch
```csharp
using SalesforceCore.Services.Core;

var responses = await compositeService.UpsertRecordsAsync(
    "Contact",
    "External_Id__c",
    records,
    allOrNone: false);
```

### Composite Graph
```csharp
var graph = compositeService.CreateGraphBuilder()
    .StartGraph("createRecords")
    .Create("Account", new Dictionary<string, object?> { ["Name"] = "Acme" }, "newAccount")
    .CreateWithReference("Contact", new Dictionary<string, object?>
    {
        ["FirstName"] = "Ada",
        ["LastName"] = "Lovelace",
        ["AccountId"] = "@{newAccount.id}"
    }, "newContact")
    .Build();

var response = await compositeService.ExecuteGraphAsync(graph);
```

### When to Choose Composite
- Batching up to 25 sub-requests where sequencing or mixed verbs matter.
- Graph operations with dependencies up to 500 nodes per graph.
- Avoid CSV and want JSON payloads with immediate responses.

## Safety & Limits
- Bulk: watch API limits, file sizes, and job timeouts; handle 429 via configured retries.
- Composite: cap at 25 sub-requests per batch; handle partial failures via response parsing.
- Validate createable/updateable fields before sending to either API.

## Next Steps
- Data service for smaller workloads: [04-Data-Service.md](04-Data-Service.md).
- Typed LINQ usage: [05-Typed-Data-Service.md](05-Typed-Data-Service.md).
- Security considerations: [09-Security.md](09-Security.md).
