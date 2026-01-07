# Infrastructure Directory Explanation

## 1. Overview
The `src/SalesforceCore/Infrastructure` directory contains low-level technical components that support the business logic. These components handle cross-cutting concerns like concurrency control and high-throughput data processing.

## 2. Sub-Directories

### `Locking/`
**Purpose**: Provides distributed locking mechanisms.
**Why**: Critical for ensuring operations like "Refresh Token" happen exactly once across multiple server instances (e.g., in a web farm).
**Key Interfaces**: `IDistributedLockProvider`.
**Implementations**: Likely includes providers for Redis or SQL-based locking.

### `Processing/`
**Purpose**: Implements high-performance batch processing pipelines.
**Key Components**:
- **`ChannelBatchProcessor`**: Uses `System.Threading.Channels` (Producer-Consumer pattern) to buffer incoming items and process them in batches. This is used for "fire-and-forget" style operations where high throughput is needed without blocking the caller.

## 3. C# Concepts
- **`System.Threading.Channels`**: A modern, high-performance API for asynchronous producer/consumer scenarios. It allows for backpressure and efficient batching.
- **Distributed Locking**: Ensuring data integrity in distributed systems.
