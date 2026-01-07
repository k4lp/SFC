# Models Directory Explanation

## 1. Overview
The `src/SalesforceCore/Models` directory contains the **Data Transfer Objects (DTOs)** and **Plain Old CLR Objects (POCOs)** used throughout the application. These classes represent the "shape" of data flowing between the Salesforce API and the C# application. They typically contain no business logic, only properties.

## 2. Directory Structure

### `Data/`
**Purpose**: Models related to actual business data and CRUD operations.
**Key Classes (Likely)**:
- `QueryResult<T>`: Wraps the standard Salesforce query response (`totalSize`, `done`, `records`).
- `CompositeRequest`/`CompositeResponse`: Structures for the Composite API.
- `UpsertResult`: Return value for upsert operations.

### `Metadata/`
**Purpose**: Models describing the *structure* of Salesforce objects (Schema).
**Key Classes**:
- `SObjectDescription`: Result of a "Describe" call (fields, relationships, record types).
- `SObjectField`: Details about a specific field (type, length, isNillable).

### `Configuration/`
**Purpose**: Classes that bind to `appsettings.json` or other configuration sources.
**Key Classes**:
- `SalesforceOptions`: The primary configuration object (Client ID, Secret, Domain).
- `CacheOptions`: Settings for caching strategies.

### `Authorization/`
**Purpose**: Models for OAuth flows.
**Key Classes**:
- `AuthToken`: Represents the JSON response from the OAuth `/token` endpoint (access_token, instance_url).

### `Errors/`
**Purpose**: Custom exception types and error response models.
**Key Classes**:
- `SalesforceException`: The base exception thrown by the library.
- `ErrorResponse`: The standard JSON error array returned by Salesforce APIs (`[{"message":"...", "errorCode":"..."}]`).

### `Security/`
**Purpose**: Models related to Field Level Security (FLS) and permission checks.

## 3. Design Decisions & C# Concepts

### System.Text.Json Serialization
These models are designed to be serialized/deserialized by `System.Text.Json`.
- **`[JsonPropertyName("...")]`**: You will likely see these attributes mapping C# PascalCase properties (`AccessToken`) to Salesforce snake_case JSON fields (`access_token`).

### Immutable vs Mutable
Configuration models are often immutable (record types or init-only properties) to prevent runtime changes, while Data models might be mutable to allow for easy manipulation before sending updates.

### Generics
Classes like `QueryResult<T>` use generics to allow the same wrapper to be used for `QueryResult<Account>`, `QueryResult<Contact>`, or `QueryResult<JsonObject>` (dynamic).
