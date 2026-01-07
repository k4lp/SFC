# Infrastructure Locking Directory Explanation

## 1. Overview
The `src/SalesforceCore/Infrastructure/Locking` directory defines the abstractions and implementations for distributed locking. Distributed locks are essential when multiple instances of the application (e.g., scaled-out web servers) need to coordinate access to a shared resource, such as refreshing a single OAuth token.

## 2. Key Components

### `IDistributedLockProvider`
**Purpose**: Defines the contract for acquiring a lock.
**Method**: `TryAcquireAsync(string key, TimeSpan timeout)`.

### `IDistributedLockHandle`
**Purpose**: Represents an acquired lock. It implements `IDisposable` (or `IAsyncDisposable`) to release the lock when disposed.

## 3. Implementations (Subfolders)
- **`SqlServer/`**: Likely contains an implementation using `sp_getapplock` to manage locks using the database, which is a common strategy when a SQL database is already part of the infrastructure.

## 4. Design Pattern
- **Resource Acquisition Is Initialization (RAII)**: The returned handle allows using the `using` statement to automatically release locks, preventing deadlocks if exceptions occur.
