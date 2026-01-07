# Data Services Explanation

## 1. Overview
The `src/SalesforceCore/Services/Data` directory contains the primary services for interacting with Salesforce records. This includes Create, Read, Update, and Delete (CRUD) operations, SOQL querying, file management, and handling complex scenarios like polymorphic lookups and batch processing.

## 2. Key Components

### `IDataService` & `DataService`
**Purpose**: The foundational, "close-to-the-metal" service for data operations. It operates primarily on `JsonNode` or `IDictionary<string, object?>`, giving developers full control over the raw data sent to and received from Salesforce.
**Key Features**:
- **Automatic FLS Checks**: Before sending data, it consults `ISchemaService` to filter out fields that the user doesn't have permission to create or update (Field Level Security).
- **Smart Batching**: Methods like `BatchCreateAsync` automatically decide whether to use the synchronous Composite API (sObject Collections) or the asynchronous Bulk API V2 based on the number of records (default threshold is 200).
- **Resilience**: Integrated with the underlying `ISalesforceClient` which handles retries and authentication.

### `ITypedDataService` & `TypedDataService`
**Purpose**: A high-level, strongly-typed wrapper around `IDataService`. It allows developers to work with C# POCOs (Plain Old CLR Objects) instead of loose dictionaries.
**Key Features**:
- **Type Safety**: Uses Generics (`<T>`) to ensure compile-time safety for database operations.
- **Mapping**: Leverages `SalesforceMapper` to convert C# objects (decorated with `[SalesforceObject]` attributes) to Salesforce-compatible JSON.
- **LINQ Provider Integration**: Exposes `SalesforceQueryable<T>`, enabling developers to write standard C# LINQ queries that are translated into SOQL strings at runtime.

### `LookupService` (Implied)
**Purpose**: Handles the resolution of Lookup fields. For example, turning a `OwnerId` "005..." string into a readable name like "John Doe". It likely supports batching to minimize API round-trips.

## 3. Design Decisions

### Dual-Layer Abstraction
The decision to separate `IDataService` (loose typing) and `ITypedDataService` (strong typing) allows for maximum flexibility.
- **Use `ITypedDataService`** for standard business logic where models are known and stable.
- **Use `IDataService`** for dynamic scenarios, such as generic integration tools or when working with objects not known at compile time.

### Smart Batching Strategy
Salesforce has different APIs for different scales. The `Batch*Async` methods abstract this complexity.
- **Composite API**: Fast, synchronous, transactional (all-or-none possible). Limit 200 records.
- **Bulk API V2**: Asynchronous, high-scale, capable of handling millions of records.
The service dynamically chooses the best tool for the job.

### Polymorphic Lookup Handling
Salesforce allows some fields (like `Task.WhoId`) to point to multiple object types (Lead or Contact). `ResolvePolymorphicTypeAsync` uses a combination of static prefix maps (fast) and `EntityDefinition` queries (robust) to correctly identify the target object type.

## 4. Key C# Terminology

### Generics (`<T>`)
Used extensively in `TypedDataService`.
```csharp
// T must be a class and have a parameterless constructor
public Task<T?> GetByIdAsync<T>(string id) where T : class, new()
```
This allows the same logic to work for `Account`, `Contact`, or any custom object.

### Asynchronous Streams (`IAsyncEnumerable`)
Used in `QueryAllAsyncEnumerable` to memory-efficiently process large datasets.
```csharp
public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(...)
{
    // yields records one by one as pages are fetched
    yield return record;
}
```
This prevents loading millions of records into memory at once.

### Extension Methods
`TypedDataServiceExtensions` adds functionality to `IDataService` without modifying the original class.
```csharp
public static async Task<T?> GetByIdAsync<T>(this IDataService dataService, ...)
```
This enables the "fluent" syntax where you can call `.GetByIdAsync<Account>(...)` on an `IDataService` instance.

### Expression Trees (`Expression<Func<T, bool>>`)
Used to allow passing lambda expressions for filtering, which are then inspected (reflected) to build SOQL WHERE clauses.
```csharp
// predicate is not executed as code, but analyzed as data
public Task<T?> GetAsync<T>(Expression<Func<T, bool>> predicate, ...)
```
