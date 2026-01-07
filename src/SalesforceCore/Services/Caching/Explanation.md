# Caching Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Caching` directory implements the **Strategy Pattern** for caching. This ensures the library can scale from a simple console app (using memory) to a load-balanced web farm (using Redis or SQL).

## 2. Key Components

### `ICacheProvider`
**Purpose**: The unified interface used by all other services (`DataService`, `SchemaService`, etc.) to store and retrieve data.
**Methods**:
- `GetAsync<T>`
- `SetAsync<T>`
- `GetOrCreateAsync<T>`: A helper that checks the cache and, if missing, executes a factory method to fetch the data and store it, handling concurrency race conditions.

### Implementations

#### `MemoryCacheProvider`
- **Backing Store**: `Microsoft.Extensions.Caching.Memory.IMemoryCache`.
- **Use Case**: Single-instance applications or development. Fast, but data is lost on restart and not shared across servers.

#### `DistributedCacheProvider`
- **Backing Store**: `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.
- **Use Case**: Production environments. Connects to Redis, NCache, or SQL Server.
- **Serialization**: Handles serializing complex objects to byte arrays (likely JSON) for storage in the distributed store.

### `SqlServer` (Subfolder)
- **Purpose**: Specific implementation details for using SQL Server as a distributed cache, likely adding **Encryption** capabilities as hinted by the `.csproj` reference to `Microsoft.EntityFrameworkCore.SqlServer` and comments about "Government-grade caching".

## 3. Design Decisions
- **Abstraction**: Services like `SchemaService` never know *where* the data is stored. They just ask `ICacheProvider`.
- **Async-First**: All operations are asynchronous to prevent blocking threads while waiting for Redis/SQL.
