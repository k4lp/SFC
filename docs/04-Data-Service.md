# Data Service (`IDataService`)

Comprehensive guide to the Data Service API - providing dynamic CRUD operations, SOQL queries, file handling, and batch operations for Salesforce integration.

## Table of Contents

1. [Overview](#overview)
2. [Service Architecture](#service-architecture)
3. [CRUD Operations](#crud-operations)
4. [Querying Data](#querying-data)
5. [Pagination](#pagination)
6. [File Operations](#file-operations)
7. [Lookup Hydration](#lookup-hydration)
8. [Security & Validation](#security--validation)
9. [Error Handling](#error-handling)
10. [Performance Optimization](#performance-optimization)
11. [When to Use ITypedDataService Instead](#when-to-use-itypeddataservice-instead)

---

## Overview

The `IDataService` interface provides low-level, dynamic access to Salesforce data. It's ideal for:

- **Metadata-driven applications** where object types are determined at runtime
- **Admin tools** that work with any sObject type
- **Dynamic UI generation** based on Salesforce schema
- **Migration scripts** that process multiple object types

### Key Features

| Feature | Description |
|---------|-------------|
| **Dynamic CRUD** | Create, read, update, delete any sObject |
| **SOQL Execution** | Execute raw or builder-based SOQL queries |
| **Pagination** | Built-in paged result handling |
| **File Operations** | Upload, download, delete attachments |
| **Lookup Hydration** | Resolve lookup field references |
| **Schema Integration** | Automatic field validation via schema |

### When to Use IDataService vs ITypedDataService

| Use Case | IDataService | ITypedDataService |
|----------|--------------|-------------------|
| Dynamic sObject types | ✅ | ❌ |
| Strong typing with models | ❌ | ✅ |
| LINQ-style queries | ❌ | ✅ |
| Runtime-determined schemas | ✅ | ❌ |
| Admin/config tools | ✅ | ❌ |
| Business logic with models | ❌ | ✅ |

---

## Service Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       IDataService                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │ CRUD Ops     │  │ Query Ops    │  │ File Ops     │           │
│  │              │  │              │  │              │           │
│  │ • Create     │  │ • QueryAsync │  │ • Upload     │           │
│  │ • Read       │  │ • QueryPaged │  │ • Download   │           │
│  │ • Update     │  │ • QueryNext  │  │ • Delete     │           │
│  │ • Delete     │  │              │  │              │           │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘           │
│         │                 │                 │                    │
│         └─────────────────┼─────────────────┘                    │
│                           ▼                                      │
│              ┌─────────────────────────┐                         │
│              │   ISalesforceClient     │                         │
│              │   (HTTP Operations)     │                         │
│              └────────────┬────────────┘                         │
│                           │                                      │
├───────────────────────────┼──────────────────────────────────────┤
│                           ▼                                      │
│              ┌─────────────────────────┐                         │
│              │   ISchemaService        │                         │
│              │   (Field Validation)    │                         │
│              └─────────────────────────┘                         │
│                                                                   │
│              ┌─────────────────────────┐                         │
│              │   ICacheProvider        │                         │
│              │   (Query Results)       │                         │
│              └─────────────────────────┘                         │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### Dependency Injection

```csharp
public class AccountService
{
    private readonly IDataService _dataService;
    private readonly ISchemaService _schemaService;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IDataService dataService,
        ISchemaService schemaService,
        ILogger<AccountService> logger)
    {
        _dataService = dataService;
        _schemaService = schemaService;
        _logger = logger;
    }
}
```

---

## CRUD Operations

### Create Record

```csharp
// Create a new record
var data = new Dictionary<string, object?>
{
    ["Name"] = "Acme Corporation",
    ["Industry"] = "Technology",
    ["AnnualRevenue"] = 5000000.00m,
    ["Phone"] = "(555) 123-4567",
    ["Website"] = "https://acme.example.com"
};

string recordId = await _dataService.CreateRecordAsync("Account", data);
_logger.LogInformation("Created Account: {Id}", recordId);
```

### Create with External ID (Upsert)

```csharp
// Upsert using external ID field
var contactData = new Dictionary<string, object?>
{
    ["External_Id__c"] = "EXT-001",
    ["FirstName"] = "John",
    ["LastName"] = "Doe",
    ["Email"] = "john.doe@example.com"
};

// UpsertRecordAsync returns UpsertResult with Id, Created, and Success properties
UpsertResult result = await _dataService.UpsertRecordAsync(
    "Contact",
    "External_Id__c",
    "EXT-001",
    contactData);

if (result.Success)
{
    _logger.LogInformation(
        result.Created ? "Created: {Id}" : "Updated: {Id}",
        result.Id);
}
```

### Read Record

```csharp
// Get single record by ID
var record = await _dataService.GetRecordAsync(
    "Account",
    "001XXXXXXXXXXXX",
    fields: new[] { "Id", "Name", "Industry", "AnnualRevenue" });

if (record != null)
{
    var name = record["Name"]?.ToString();
    var industry = record["Industry"]?.ToString();
}
```

### Read with Related Records

```csharp
using SalesforceCore.Services.Query;

// Use SOQL when you need relationship fields or child relationship subqueries.
var soql = SoqlBuilder.From("Account")
    .Select("Id", "Name", "Industry", "Owner.Name", "Owner.Email") // Parent relationship
    .SelectSubQuery("Contacts", sub => sub.Select("Id", "FirstName", "LastName", "Email")) // Child relationship
    .Where("Id", accountId)
    .Limit(1)
    .Build();

var result = await _dataService.QueryAsync(soql);
var accountWithContacts = result.Records.Count > 0 ? result.Records[0] : null;
```

### Update Record

```csharp
// Update specific fields
var updates = new Dictionary<string, object?>
{
    ["Industry"] = "Healthcare",
    ["Rating"] = "Hot",
    ["Description"] = "Updated via API"
};

await _dataService.UpdateRecordAsync("Account", accountId, updates);
```

### Partial Update with Null Handling

```csharp
// Clear a field by setting to null
var updates = new Dictionary<string, object?>
{
    ["Phone"] = null,  // Clears the phone field
    ["Industry"] = "Finance"
};

await _dataService.UpdateRecordAsync("Account", accountId, updates);
```

### Delete Record

```csharp
// Delete a single record
await _dataService.DeleteRecordAsync("Account", accountId);

// Delete is permanent - implement soft delete in your app if needed
```

### Batch Delete

```csharp
// Delete multiple records
var idsToDelete = new[] { "001XXX1", "001XXX2", "001XXX3" };

foreach (var id in idsToDelete)
{
    try
    {
        await _dataService.DeleteRecordAsync("Account", id);
    }
    catch (SalesforceException ex) when (ex.ErrorCode == "ENTITY_IS_DELETED")
    {
        // Already deleted, continue
        _logger.LogWarning("Record {Id} was already deleted", id);
    }
}
```

---

## Querying Data

### Basic Query

```csharp
// Execute raw SOQL
var result = await _dataService.QueryAsync(
    "SELECT Id, Name, Industry FROM Account WHERE Industry = 'Technology' LIMIT 10");

foreach (var record in result.Records)
{
    Console.WriteLine($"{record["Id"]}: {record["Name"]}");
}
```

### Using SoqlBuilder (Recommended)

SoqlBuilder provides **type-safe query construction with built-in SOQL injection prevention**:

```csharp
using SalesforceCore.Services.Query;

// Build query safely - all values are automatically sanitized
var query = SoqlBuilder.From("Account")
    .Select("Id", "Name", "Industry", "AnnualRevenue")
    .WhereCondition(SoqlCondition.And(
        SoqlCondition.Equals("Industry", userInputIndustry),  // Safe!
        SoqlCondition.GreaterThan("AnnualRevenue", 1000000),
        SoqlCondition.IsNotNull("Phone")
    ))
    .OrderBy("Name")
    .Limit(100)
    .Build();

var result = await _dataService.QueryAsync(query);
```

### Complex Conditions

```csharp
// OR conditions
var filter = SoqlCondition.Or(
    SoqlCondition.Equals("Industry", "Technology"),
    SoqlCondition.Equals("Industry", "Finance"),
    SoqlCondition.Equals("Industry", "Healthcare")
);

// Nested AND/OR
var complexFilter = SoqlCondition.And(
    SoqlCondition.GreaterThan("AnnualRevenue", 1000000),
    SoqlCondition.Or(
        SoqlCondition.Equals("Rating", "Hot"),
        SoqlCondition.Equals("Rating", "Warm")
    ),
    SoqlCondition.IsNotNull("Phone")
);

var query = SoqlBuilder.From("Account")
    .Select("Id", "Name", "Industry", "Rating")
    .WhereCondition(complexFilter)
    .Build();
```

### LIKE Queries (Pattern Matching)

```csharp
// Search names starting with "Acme"
var condition = SoqlCondition.Like("Name", "Acme%");

// Search names containing user input (safely escaped)
var searchCondition = SoqlCondition.Like("Name", $"%{userSearch}%");

// Case-insensitive search (SOQL is case-insensitive by default)
var query = SoqlBuilder.From("Contact")
    .Select("Id", "FirstName", "LastName", "Email")
    .WhereCondition(SoqlCondition.Like("Email", $"%{emailDomain}"))
    .Build();
```

### IN Queries

```csharp
// Filter by list of values
var industries = new[] { "Technology", "Finance", "Healthcare" };
var condition = SoqlCondition.In("Industry", industries);

// Filter by list of IDs
var accountIds = new[] { "001XXX1", "001XXX2", "001XXX3" };
var idCondition = SoqlCondition.In("Id", accountIds);
```

### IN Subqueries

Use `WhereInSubquery` / `WhereNotInSubquery` to safely express `IN (SELECT ...)` patterns without raw string concatenation:

```csharp
// ParentId IN (SELECT PermissionSetId FROM PermissionSetAssignment WHERE AssigneeId = '...')
var permissionSetIds = SoqlBuilder.From("PermissionSetAssignment")
    .Select("PermissionSetId")
    .WhereEquals("AssigneeId", userId);

var query = SoqlBuilder.From("SetupEntityAccess")
    .Select("Id")
    .WhereInSubquery("ParentId", permissionSetIds)
    .Build();
```

### Date/DateTime Queries

```csharp
// Records modified in last 7 days
var recentCondition = SoqlCondition.GreaterThan(
    "LastModifiedDate",
    DateTime.UtcNow.AddDays(-7));

// Records created in specific date range
var dateRangeCondition = SoqlCondition.And(
    SoqlCondition.GreaterThanOrEqual("CreatedDate", startDate),
    SoqlCondition.LessThanOrEqual("CreatedDate", endDate)
);

// Using date literals
var query = SoqlBuilder.From("Opportunity")
    .Select("Id", "Name", "CloseDate")
    .WhereDateLiteral("CloseDate", DateLiteral.THIS_MONTH)
    .Build();
```

### Relationship Queries

```csharp
// Parent-to-child (subquery)
var query = SoqlBuilder.From("Account")
    .Select("Id", "Name")
    .SelectSubQuery("Contacts", sub => sub.Select("Id", "FirstName", "LastName"))
    .WhereCondition(SoqlCondition.Equals("Industry", "Technology"))
    .Build();

// Child-to-parent (dot notation)
var contactQuery = SoqlBuilder.From("Contact")
    .Select("Id", "FirstName", "LastName", "Account.Name", "Account.Industry")
    .WhereCondition(SoqlCondition.Equals("Account.Industry", "Technology"))
    .Build();
```

### Aggregate Queries

```csharp
// COUNT query
var countQuery = "SELECT COUNT() FROM Account WHERE Industry = 'Technology'";
var countResult = await _dataService.QueryAsync(countQuery);
var total = countResult.TotalSize;

// GROUP BY query
var groupQuery = @"
    SELECT Industry, COUNT(Id) RecordCount
    FROM Account
    GROUP BY Industry
    ORDER BY COUNT(Id) DESC";
var groupResult = await _dataService.QueryAsync(groupQuery);
```

---

## Pagination

### Using QueryPagedAsync

```csharp
// Get first page
var page = await _dataService.QueryPagedAsync(
    sObject: "Account",
    fields: new[] { "Id", "Name", "Industry", "CreatedDate" },
    filter: SoqlCondition.GreaterThan("AnnualRevenue", 100000),
    orderBy: "CreatedDate",
    descending: true,
    page: 1,
    pageSize: 25);

// Access results
Console.WriteLine($"Total records: {page.TotalCount}");
Console.WriteLine($"Current page: {page.PageNumber}");
Console.WriteLine($"Total pages: {page.TotalPages}");
Console.WriteLine($"Has more: {page.HasNextPage}");

foreach (var record in page.Records)
{
    Console.WriteLine($"{record["Id"]}: {record["Name"]}");
}
```

### Iterate All Pages

```csharp
// Process all pages
var allRecords = new List<JsonObject>();
var currentPage = 1;
PagedResult<JsonObject> page;

do
{
    page = await _dataService.QueryPagedAsync(
        "Contact",
        fields: new[] { "Id", "FirstName", "LastName", "Email" },
        orderBy: "LastName",
        page: currentPage,
        pageSize: 100);

    allRecords.AddRange(page.Records);
    currentPage++;

} while (page.HasNextPage);

Console.WriteLine($"Total processed: {allRecords.Count}");
```

### Using NextRecordsUrl (Large Result Sets)

For very large result sets, use the Salesforce cursor-based pagination:

```csharp
var allRecords = new List<JsonObject>();

// Initial query
var result = await _dataService.QueryAsync(
    "SELECT Id, Name FROM Account ORDER BY Name");
allRecords.AddRange(result.Records);

// Follow NextRecordsUrl for additional batches
while (!result.Done && !string.IsNullOrEmpty(result.NextRecordsUrl))
{
    result = await _dataService.QueryNextAsync(result.NextRecordsUrl);
    allRecords.AddRange(result.Records);

    _logger.LogDebug("Fetched batch, total: {Count}", allRecords.Count);
}
```

### Using QueryAllAsyncEnumerable (Streaming)

For memory-efficient processing of large datasets without loading everything into memory:

```csharp
// Stream records one at a time - ideal for large datasets
await foreach (var record in _dataService.QueryAllAsyncEnumerable(
    "SELECT Id, Name FROM Account ORDER BY Name"))
{
    await ProcessRecordAsync(record);
    // Records are processed as they arrive, not buffered in memory
}
```

---

## User Operations

### Get Current User

```csharp
// Get the authenticated user's profile
var userProfile = await _dataService.GetCurrentUserAsync();

var userId = userProfile["Id"]?.ToString();
var userName = userProfile["Name"]?.ToString();
var email = userProfile["Email"]?.ToString();
```

### Get Recent Items

```csharp
// Get user's recently viewed items (across all objects)
var recentItems = await _dataService.GetRecentItemsAsync(limit: 10);

foreach (var item in recentItems)
{
    Console.WriteLine($"{item.Type}: {item.Name} ({item.Id})");
}
```

---

## Polymorphic Lookup Resolution

Resolve object types for polymorphic lookup IDs:

```csharp
// Resolve a single polymorphic ID
string? objectType = await _dataService.ResolvePolymorphicTypeAsync(recordId);
if (objectType != null)
{
    Console.WriteLine($"Record {recordId} is a {objectType}");
}

// Batch resolve multiple IDs
var recordIds = new[] { "001xxx", "003xxx", "005xxx" };
Dictionary<string, string> types = await _dataService.BatchResolvePolymorphicTypesAsync(recordIds);

foreach (var (id, type) in types)
{
    Console.WriteLine($"{id}: {type}");
}
```

---

## File Operations

### Upload File

```csharp
// Upload file (creates ContentVersion and links it to the record)
byte[] fileBytes = await File.ReadAllBytesAsync("document.pdf");

string contentVersionId = await _dataService.UploadFileAsync(
    linkedEntityId: recordId,     // Account, Contact, etc.
    fileName: "document.pdf",
    content: fileBytes);

_logger.LogInformation("Uploaded ContentVersion: {ContentVersionId}", contentVersionId);
```

### Upload with Stream

```csharp
// Upload from stream (requires a known contentLength)
using var fileStream = File.OpenRead("large-document.pdf");

string contentVersionId = await _dataService.UploadFileAsync(
    linkedEntityId: recordId,
    fileName: "large-document.pdf",
    contentStream: fileStream,
    contentLength: fileStream.Length);
```

### Get Attached Files

```csharp
// List all files attached to a record
var files = await _dataService.GetAttachedFilesAsync(recordId);

foreach (var file in files)
{
    Console.WriteLine($"File: {file.Title}");
    Console.WriteLine($"  Size: {file.ContentSize} bytes");
    Console.WriteLine($"  Type: {file.FileExtension}");
    Console.WriteLine($"  Created: {file.CreatedDate:O}");
    Console.WriteLine($"  ContentDocumentId: {file.ContentDocumentId}");
    Console.WriteLine($"  ContentVersionId: {file.ContentVersionId}");
}
```

### Download File Content

```csharp
// Download file content by ContentVersionId (e.g., from GetAttachedFilesAsync)
var files = await _dataService.GetAttachedFilesAsync(recordId);
var file = files.First();
byte[] content = await _dataService.GetFileContentAsync(file.ContentVersionId);

// Save to disk
await File.WriteAllBytesAsync("downloaded-file.pdf", content);
```

### Delete File

```csharp
// Delete a file (removes ContentDocument)
var files = await _dataService.GetAttachedFilesAsync(recordId);
var file = files.First();
await _dataService.DeleteFileAsync(file.ContentDocumentId);
```

### File Upload Validation

```csharp
using SalesforceCore.Utilities;

// Validate file extension before upload
if (!SecurityUtils.IsAllowedExtension(fileName, allowedExtensions))
{
    throw new InvalidOperationException($"File type not allowed: {fileName}");
}

// Check file size
if (fileBytes.Length > options.MaxFileUploadSize)
{
    throw new InvalidOperationException(
        $"File exceeds maximum size of {options.MaxFileUploadSize} bytes");
}
```

---

## Lookup Hydration

Lookup hydration resolves reference fields to their display values.

> [!NOTE]
> `HydrateLookupsAsync` returns `Dictionary<string, string>` mapping field names to resolved display names.

```csharp
// Get schema for lookup fields
var describe = await _schemaService.GetDescribeAsync("Contact");
var lookupFields = describe.Fields
    .Where(f => f.Type == "reference")
    .ToList();

// Hydrate record with lookup values - returns Dictionary<string, string>
var contact = await _dataService.GetRecordAsync("Contact", contactId);
Dictionary<string, string> hydratedLookups = await _dataService.HydrateLookupsAsync(contact, lookupFields);

// Access resolved display names by field name:
// hydratedLookups["AccountId"] = "Acme Corp"
// hydratedLookups["OwnerId"] = "John Smith"
foreach (var (fieldName, displayName) in hydratedLookups)
{
    Console.WriteLine($"{fieldName}: {displayName}");
}
```

### Bulk Hydration

```csharp
// Hydrate multiple records efficiently
var contacts = await _dataService.QueryAsync(
    "SELECT Id, FirstName, LastName, AccountId, OwnerId FROM Contact LIMIT 100");

var allHydratedLookups = new List<Dictionary<string, string>>();

foreach (var contact in contacts.Records)
{
    var hydrated = await _dataService.HydrateLookupsAsync(contact, lookupFields);
    allHydratedLookups.Add(hydrated);
}
```

---

## Security & Validation

### SOQL Injection Prevention

**SoqlBuilder and SoqlCondition automatically sanitize all values:**

```csharp
// User input is automatically escaped
var userInput = "Acme'; DELETE FROM Account; --";

var condition = SoqlCondition.Equals("Name", userInput);
// Renders as: Name = 'Acme\'; DELETE FROM Account; --'
// The injection attempt is harmless - treated as literal text

var query = SoqlBuilder.From("Account")
    .Select("Id", "Name")
    .WhereCondition(condition)
    .Build();
```

### How Sanitization Works

```csharp
// SecurityUtils.SanitizeForSoql escapes dangerous characters
public static string SanitizeForSoql(string? input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;

    // SOQL uses doubled single quotes for escaping (like SQL)
    return input.Replace("'", "''");
}

// Examples:
// Input: "O'Brien"          → "O''Brien"
// Input: "'; DROP TABLE--"  → "''; DROP TABLE--"
```

### Field Name Validation

```csharp
// Field names are validated to prevent injection via column names
SecurityUtils.IsValidFieldName("Name");           // true
SecurityUtils.IsValidFieldName("Custom__c");      // true
SecurityUtils.IsValidFieldName("Account.Name");   // true

SecurityUtils.IsValidFieldName("Name; DROP");     // false
SecurityUtils.IsValidFieldName("../etc/passwd");  // false
```

### Salesforce ID Validation

```csharp
// Always validate IDs before using them
if (!SecurityUtils.IsValidSalesforceId(userProvidedId))
{
    throw new ArgumentException("Invalid Salesforce ID format");
}

var record = await _dataService.GetRecordAsync("Account", userProvidedId);
```

### Field-Level Security

When `EnforceFieldLevelSecurity` is enabled (default), the service respects Salesforce FLS:

```csharp
// Fields the user can't read are automatically filtered
var record = await _dataService.GetRecordAsync("Account", id);
// SensitiveField__c won't appear if user lacks read access

// Fields the user can't write are filtered on create/update
var data = new Dictionary<string, object?>
{
    ["Name"] = "Test",
    ["ReadOnlyField__c"] = "value"  // Filtered out if not createable
};
await _dataService.CreateRecordAsync("Account", data);
```

If you disable FLS enforcement, the data service will pass fields through and let Salesforce reject unauthorized access.

---

## Error Handling

### Common Salesforce Errors

```csharp
try
{
    await _dataService.CreateRecordAsync("Account", data);
}
catch (SalesforceException ex) when (ex.ErrorCode == "REQUIRED_FIELD_MISSING")
{
    // Handle missing required fields
    _logger.LogWarning("Missing required fields: {Fields}", ex.Fields);
    throw new ValidationException($"Please provide: {string.Join(", ", ex.Fields)}");
}
catch (SalesforceException ex) when (ex.ErrorCode == "DUPLICATE_VALUE")
{
    // Handle duplicate external ID or unique field
    _logger.LogWarning("Duplicate value detected");
    throw new ConflictException("A record with this value already exists");
}
catch (SalesforceException ex) when (ex.ErrorCode == "INSUFFICIENT_ACCESS_OR_READONLY")
{
    // Handle permission issues
    _logger.LogError("Access denied to object or field");
    throw new ForbiddenException("You don't have permission for this operation");
}
catch (SalesforceException ex)
{
    // Handle other Salesforce errors
    _logger.LogError(ex, "Salesforce error: {Code} - {Message}",
        ex.ErrorCode, ex.Message);
    throw;
}
```

### Error Code Reference

| Error Code | Description | Common Cause |
|------------|-------------|--------------|
| `REQUIRED_FIELD_MISSING` | Required field not provided | Missing required data |
| `DUPLICATE_VALUE` | Unique constraint violated | Duplicate external ID |
| `INVALID_CROSS_REFERENCE_KEY` | Invalid lookup reference | Bad foreign key |
| `ENTITY_IS_DELETED` | Record was deleted | Stale reference |
| `UNABLE_TO_LOCK_ROW` | Record locked by another process | Concurrent update |
| `FIELD_CUSTOM_VALIDATION_EXCEPTION` | Validation rule failed | Business rule violation |
| `STRING_TOO_LONG` | Text exceeds field length | Data too large |
| `INSUFFICIENT_ACCESS_OR_READONLY` | Permission denied | FLS or sharing issue |

### Retry Logic

```csharp
// Retry on transient errors
var retryPolicy = Policy
    .Handle<SalesforceException>(ex =>
        ex.ErrorCode == "UNABLE_TO_LOCK_ROW" ||
        ex.StatusCode == 503)
    .WaitAndRetryAsync(3, attempt =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt)));

await retryPolicy.ExecuteAsync(async () =>
{
    await _dataService.UpdateRecordAsync("Account", id, updates);
});
```

---

## Performance Optimization

### Use Field Lists

```csharp
// BAD - fetches all fields
var record = await _dataService.GetRecordAsync("Account", id);

// GOOD - fetch only needed fields
var record = await _dataService.GetRecordAsync("Account", id,
    fields: new[] { "Id", "Name", "Industry" });
```

### Batch Operations

```csharp
// For multiple records, use Bulk or Composite services
var bulkService = serviceProvider.GetRequiredService<IBulkService>();

// Insert 10,000 records efficiently
await bulkService.InsertAsync("Account", records);
```

### Cache Schema

Schema is cached automatically, but you can warm the cache:

```csharp
// Pre-warm schema cache on startup
var objects = new[] { "Account", "Contact", "Opportunity", "Lead" };
foreach (var obj in objects)
{
    await _schemaService.GetDescribeAsync(obj);
}
```

### Limit Query Results

```csharp
// Always use LIMIT for unbounded queries
var query = SoqlBuilder.From("Account")
    .Select("Id", "Name")
    .WhereCondition(SoqlCondition.Equals("Industry", "Technology"))
    .Limit(1000)  // Always set a reasonable limit
    .Build();
```

---

## When to Use ITypedDataService Instead

Consider switching to `ITypedDataService` when you need:

### Strong Typing

```csharp
// ITypedDataService - compile-time type safety
var accounts = await _typedDataService.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .OrderBy(a => a.Name)
    .ToListAsync();

// IDataService - runtime dictionary access
var accounts = await _dataService.QueryPagedAsync("Account",
    fields: new[] { "Id", "Name", "Industry" },
    filter: SoqlCondition.Equals("Industry", "Technology"));
```

### LINQ Queries

```csharp
// LINQ is only available with ITypedDataService
var result = await _typedDataService.Query<Contact>()
    .Where(c => c.Account.Industry == "Technology")
    .Where(c => c.Email.EndsWith("@company.com"))
    .OrderByDescending(c => c.CreatedDate)
    .Take(50)
    .ToListAsync();
```

### Model Mapping

```csharp
// ITypedDataService automatically maps to your model
Account account = await _typedDataService.GetByIdAsync<Account>(id);

// IDataService returns raw dictionary
JsonObject record = await _dataService.GetRecordAsync("Account", id);
```

---

## Next Steps

- **Typed Data Service**: [05-Typed-Data-Service.md](05-Typed-Data-Service.md) - LINQ-based queries
- **Schema Service**: [06-Schema-Service.md](06-Schema-Service.md) - Metadata operations
- **Bulk/Composite**: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md) - High-volume operations
- **Security Guide**: [09-Security.md](09-Security.md) - Security best practices
