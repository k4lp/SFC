# Core Services Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Core` directory contains the foundational services that handle low-level communication with the Salesforce API. These services abstract the complexities of HTTP requests, authentication, and API limits.

## 2. Key Components

### `ISalesforceClient`
**Purpose**: The raw HTTP client used by all other services.
**Features**:
- **Authentication**: Uses an injected `ITokenProvider` to attach the OAuth Bearer token to every request.
- **Resilience**: Integrated with Polly for retries and circuit breaking.
- **Convenience**: Helper methods (`GetAsync`, `PostAsync`, etc.) that handle JSON serialization/deserialization.

### `ITokenProvider`
**Purpose**: Strategy interface for obtaining Access Tokens.
**Implementations**:
- **`JwtTokenProvider`**: Implements the OAuth 2.0 JWT Bearer Flow (server-to-server).
- **`ClientCredentialsTokenProvider`**: Implements the OAuth 2.0 Client Credentials Flow.
- **`MissingConfigurationTokenProvider`**: A safe placeholder that throws descriptive errors if auth is missing.

### `IBulkService`
**Purpose**: Handles high-volume data operations (thousands/millions of records).
**Logic**: Wraps the **Bulk API 2.0**.
- Creates jobs (`ingest` or `query`).
- Uploads CSV data (via string or Stream).
- Polls for completion.
- Retrieves success/failure results.

### `ICompositeService`
**Purpose**: Handles transactional batch operations.
**Logic**: Wraps the **Composite API** and **Composite Graph API**.
- **Composite API**: Executes up to 25 dependent sub-requests in a single call.
- **Composite Graph API**: Executes up to 500 nodes, allowing for complex object trees (e.g., insert Account + Contacts + Opportunities in one go).

### `ILimitsService`
**Purpose**: Tracks API usage limits (e.g., "DailyRequests").

### `SynchronizationService`
**Purpose**: Provides logic for keeping local data in sync with Salesforce, possibly using the `GetUpdated` / `GetDeleted` replication APIs.

## 3. Design Decisions
- **Unified Client**: Having a single `ISalesforceClient` ensures that cross-cutting concerns (logging, metrics, global error handling) are applied consistently across REST, Tooling, and Bulk APIs.
- **Streaming Support**: `IBulkService` and `IFileService` support `Stream` inputs to minimize memory usage when handling large files or datasets.
