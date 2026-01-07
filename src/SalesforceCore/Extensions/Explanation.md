# Extensions Directory Explanation

## 1. Overview
The `src/SalesforceCore/Extensions` directory contains extension methods primarily used for **Dependency Injection (DI)** configuration. This is the entry point for integrating the library into an application.

## 2. Key Components

### `ServiceCollectionExtensions.cs`
**Purpose**: Registers all SalesforceCore services into the `IServiceCollection` container.
**Key Method**: `AddSalesforceCore(Action<SalesforceOptions> configure)`
**Responsibilities**:
- **Service Registration**: Adds `DataService`, `SchemaService`, `AuthService`, etc., as Scoped or Singleton services.
- **Resilience**: Configures `HttpClient` instances with **Polly** policies for retries, circuit breakers, and timeouts via `AddStandardResilienceHandler`.
- **Strategy Selection**: Determines which implementation of `ICacheProvider` to register (Memory, Distributed, or SQL) based on configuration.
- **Token Provider**: Auto-detects and registers the correct `ITokenProvider` (JWT vs. Client Credentials) based on available config sections.

## 3. C# Concepts
- **Extension Methods (`this IServiceCollection services`)**: Allows the "fluent" syntax `services.AddSalesforceCore(...)` in `Program.cs`.
- **Dependency Injection**: The core design pattern. The library is built to be injected, not instantiated manually.
- **Options Pattern (`IOptions<T>`)**: Used to bind `SalesforceOptions` from `appsettings.json` to strongly-typed classes.
