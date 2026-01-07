# SalesforceCore - Project Explanation

## 1. Overview
The `src/SalesforceCore` directory contains the core class library for the **SalesforceCore** project. This project serves as a comprehensive, metadata-driven, service-oriented REST API client for Salesforce. It is designed to be framework-agnostic (though heavily integrated with .NET standards) and provides the fundamental building blocks for interacting with Salesforce APIs, including authentication, data management (CRUD), SOQL querying, and metadata handling.

## 2. Design Decisions & Architecture

### Metadata-Driven Architecture
The library is designed to be "metadata-driven," meaning it relies heavily on Salesforce's `describe` and `metadata` APIs to understand the shape of data at runtime. This allows the client to adapt to custom objects and fields without requiring hardcoded C# classes for every single Salesforce object.

### Service-Oriented Design
Logic is segregated into distinct "Services" (found in the `Services/` directory). Each service is responsible for a specific domain of the Salesforce API (e.g., `DataService` for records, `MetadataService` for schema, `AuthService` for tokens). This separation of concerns ensures maintainability and testability.

### Dependency Injection (DI) First
The project assumes usage of the Microsoft.Extensions.DependencyInjection container. All services are designed to be injected via interfaces. The `Extensions/` directory contains helper methods to register these services, promoting loose coupling and making the library easy to integrate into ASP.NET Core applications or background workers.

### Resilience & Robustness
The presence of `Microsoft.Extensions.Http.Resilience` in the `.csproj` file indicates a design choice to build a robust network client. The library likely employs policies for retries, circuit breakers, and timeouts to handle the transient nature of external HTTP APIs.

### Strategy Pattern
The architecture uses the Strategy pattern for key components like Caching and Authentication. This allows the library to switch between different implementations (e.g., In-Memory Cache vs. Distributed Cache vs. SQL Encrypted Cache) without changing the core business logic.

## 3. File Analysis

### `SalesforceConstants.cs`
**Purpose**: This static class acts as a central repository for all constant values used throughout the library. It eliminates "magic strings" from the code, making refactoring and updates easier.
**Key Sections**:
- `Default Values`: API versions, domains (`login.salesforce.com`).
- `GrantTypes`: OAuth 2.0 grant types (Authorization Code, Refresh Token, JWT).
- `Claims`: Constants for JWT claims (e.g., `urn:salesforce:user_id`).
- `Paths`: API endpoint paths (e.g., `/services/data/`, `/sobjects`).
- `Defaults`: Configuration defaults like timeouts (30s), retry attempts (3), and cache durations.
- `Headers`: HTTP header keys and values.
- `ValidationPatterns`: Regex patterns for validating Salesforce IDs and API names.

**C# Concepts**:
- **`static class`**: A class that cannot be instantiated and contains only static members. Used here to group related constants.
- **`const string`**: Compile-time constant strings.
- **`public static class` nested types**: Used to group constants hierarchically (e.g., `SalesforceConstants.Paths.SObjects`).

### `SalesforceCore.csproj`
**Purpose**: The project file defining the build configuration, target framework, and dependencies.
**Key Attributes**:
- `TargetFramework`: `net10.0` - Targets the latest .NET 10.
- `Nullable`: `enable` - Enforces null safety at the compiler level.
- `ImplicitUsings`: `enable` - Reduces boilerplate `using` statements.
**Key Dependencies**:
- `Microsoft.Extensions.Http.Resilience`: For resilient HTTP requests (Polly integration).
- `Microsoft.IdentityModel.JsonWebTokens`: For handling JWT authentication flows.
- `CsvHelper`: For handling Bulk API CSV data.
- `Microsoft.EntityFrameworkCore.SqlServer`: Indicates support for SQL-backed caching (likely for "Government-grade" encryption requirements mentioned in comments).

## 4. Directory Summaries

- **`Attributes/`**: Custom C# attributes used to decorate classes and properties (e.g., mapping properties to Salesforce fields).
- **`Extensions/`**: Contains extension methods, primarily for `IServiceCollection` to facilitate Dependency Injection registration.
- **`Infrastructure/`**: Low-level technical implementations, such as distributed locking, channel-based batch processing, and other cross-cutting concerns.
- **`Mapping/`**: Logic for mapping between C# objects and Salesforce JSON/DTOs.
- **`Models/`**: Contains Data Transfer Objects (DTOs) representing Salesforce resources (Errors, Metadata, Query results).
- **`Query/`**: Implements the LINQ provider, translating C# LINQ expressions into Salesforce SOQL strings.
- **`Schema/`**: Logic for managing and discovering Salesforce schema (SObjects, Fields) and handling differences/migrations.
- **`Security/`**: Components for handling Field Level Security (FLS) and other security-related logic.
- **`Services/`**: The core business logic layer. Contains implementations for interacting with various Salesforce APIs (Data, Tooling, Bulk, etc.).
- **`Tracking/`**: Logic for tracking changes to entities, useful for optimizing updates (only sending changed fields).
- **`Utilities/`**: Helper classes for common tasks like string manipulation, URL formatting, and bitwise operations.
- **`Validation/`**: Engines and rules for validating data before sending it to Salesforce.

## 5. Key C# Terminology Used
- **Reflection**: Extensively used in `Mapping/` and `Query/` to inspect object properties and attributes at runtime to generate JSON or SOQL.
- **LINQ (Language Integrated Query)**: Used in `Query/` to allow developers to write C# queries that are translated to SOQL.
- **Asynchronous Programming (`async`/`await`)**: heavily used in `Services/` to perform non-blocking I/O operations with Salesforce APIs.
