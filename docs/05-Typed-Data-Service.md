# Typed Data Service (`ITypedDataService`)

LINQ-to-SOQL with strongly-typed models. Use when you have C# classes mapped to Salesforce objects.

## Requirements
- **Required**: Model classes with `[SalesforceObject]`, `[SalesforceField]`, etc.; configured `Salesforce` options; working `ITokenProvider`.
- **Recommended**: Keep models in a shared library; validate SOQL inputs enabled.
- **Optional**: Generate models via `sf-gen` (see [08-Model-Generator-CLI.md](08-Model-Generator-CLI.md)).

## Basics
Resolve from DI:
```csharp
public class AccountService
{
    private readonly ITypedDataService _data;
    public AccountService(ITypedDataService data) => _data = data;

    public Task<List<Account>> TopAsync() =>
        _data.Query<Account>()
             .OrderByDescending(a => a.CreatedDate)
             .Take(10)
             .ToListAsync();
}
```

## CRUD
```csharp
var id = await _data.CreateAsync(account);
account.Id = id;

await _data.UpdateAsync(account);
await _data.DeleteAsync<Account>(id);

var upsertId = await _data.UpsertAsync(account, externalIdField: "External_Id__c");
```

## Query Examples
```csharp
var contacts = await _data.Query<Contact>()
    .Where(c => c.Email.Contains("@example.com"))
    .OrderBy(c => c.LastName)
    .Skip(20)
    .Take(10)
    .ToListAsync();
```

## Extended LINQ Operators

These operators provide workarounds for SOQL limitations:

| Operator | Description | SOQL Strategy |
|----------|-------------|---------------|
| `DistinctAsync` | Get unique field values | Uses GROUP BY |
| `AllAsync` | Check if all match condition | Negated COUNT |
| `LastAsync` / `LastOrDefaultAsync` | Get last by ordering | ORDER BY DESC + LIMIT 1 |
| `ElementAtAsync` / `ElementAtOrDefaultAsync` | Get at index | OFFSET + LIMIT 1 |
| `UnionAsync` / `ConcatAsync` | Combine queries | Client-side merge |
| `ExceptAsync` / `IntersectAsync` | Set operations | Client-side compare |

```csharp
// Get unique industries
var industries = await _data.Query<Account>()
    .DistinctAsync(a => a.Industry);

// Check if all accounts are active
var allActive = await _data.Query<Account>()
    .AllAsync(a => a.IsActive == true);

// Get last created account
var last = await _data.Query<Account>()
    .LastAsync(a => a.CreatedDate);

// Union two queries
var combined = await techQuery.UnionAsync(financeQuery);
```

### DateTime Member Translation

The LINQ provider translates DateTime property access to SOQL date functions:

| C# Member | SOQL Function |
|-----------|---------------|
| `.Year` | `CALENDAR_YEAR()` |
| `.Month` | `CALENDAR_MONTH()` |
| `.Day` | `DAY_IN_MONTH()` |
| `.Hour` | `HOUR_IN_DAY()` |
| `.DayOfYear` | `DAY_IN_YEAR()` |

```csharp
// Filter by year and month - translated to SOQL date functions
var q4Opportunities = await _data.Query<Opportunity>()
    .Where(o => o.CloseDate.Year == 2024 && o.CloseDate.Month > 9)
    .ToListAsync();
// SOQL: WHERE CALENDAR_YEAR(CloseDate) = 2024 AND CALENDAR_MONTH(CloseDate) > 9

// Filter by hour (for time-based analysis)
var morningActivities = await _data.Query<Task>()
    .Where(t => t.CreatedDate.Hour >= 9 && t.CreatedDate.Hour < 12)
    .ToListAsync();
```

### Locking Clauses

Use locking clauses for record-level locking or tracking:

| Method | SOQL Clause | Purpose |
|--------|-------------|---------|
| `ForUpdate()` | `FOR UPDATE` | Lock records to prevent concurrent modifications |
| `ForView()` | `FOR VIEW` | Update `LastViewedDate` for queried records |
| `ForReference()` | `FOR REFERENCE` | Update `LastReferencedDate` for queried records |

```csharp
// Lock records for update (pessimistic locking)
var accounts = await _data.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .ForUpdate()
    .ToListAsync();

// Track as recently viewed
var viewed = await _data.Query<Account>()
    .Where(a => a.Id == accountId)
    .ForView()
    .FirstOrDefaultAsync();

## Mapping Attributes (common)
- `[SalesforceObject("Account")]` – maps class to object.
- `[SalesforceField("Name", Required = true, MaxLength = 255)]` – field mapping + constraints.
- `[SalesforceId]` – marks Id property.
- `[SalesforceExternalId]` – marks external ID.
- `[SalesforceLookup("Account", RelationshipName = "Parent")]` – lookup relationships.

> [!NOTE]
> **`[SalesforceLookup]` is metadata-only** – it does NOT automatically hydrate related objects. Use relationship queries (below) or manual lookups to fetch related data.

## Relationship Queries

### Parent Relationships (Single Record)

Query parent object fields using dot notation in `Select()`:

```csharp
// Query Contact with Account.Name included
var contacts = await _data.Query<Contact>()
    .Select(c => new { c.Id, c.Name, c.Account.Name })
    .Where(c => c.AccountId != null)
    .ToListAsync();

// SOQL generated: SELECT Id, Name, Account.Name FROM Contact WHERE AccountId != null
```

### Child Relationships (Related Lists)

Use `SelectSubQuery()` for child relationships:

```csharp
// Query Account with related Contacts
var accounts = await _data.Query<Account>()
    .SelectSubQuery<Contact>("Contacts", c => new { c.Id, c.Name, c.Email })
    .Take(10)
    .ToListAsync();

// SOQL: SELECT Id, Name, (SELECT Id, Name, Email FROM Contacts) FROM Account LIMIT 10
```

### Manual Lookup Hydration

For complex scenarios, fetch related data manually:

```csharp
var contact = await _data.GetByIdAsync<Contact>(id);
if (!string.IsNullOrEmpty(contact.AccountId))
{
    var account = await _data.GetByIdAsync<Account>(contact.AccountId);
    contact.Account = account; // Manually assign
}
```

## Validation & Safety
- SOQL generated from expressions respects property mappings; avoid string-based conditions.
- Paging operators (`Skip/Take`) translate to OFFSET/LIMIT.
- Null handling: fields with null values are omitted on create/update.

## When to Drop to `IDataService`
- Dynamic fields at runtime, polymorphic object names, or ad-hoc SOQL: use `IDataService` ([04-Data-Service.md](04-Data-Service.md)).

## Next Steps
- Schema helpers: [06-Schema-Service.md](06-Schema-Service.md).
- Bulk/Composite: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md).
