# Apex Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Apex` directory contains the `IApexService` and its implementation. This service provides a bridge to interact with **Custom Apex REST Endpoints** exposed in Salesforce.

## 2. Key Components

### `IApexService`
**Purpose**: Executes HTTP methods (GET, POST, PUT, PATCH, DELETE) against endpoints defined in Salesforce Apex classes using `@RestResource`.
**Key Features**:
- **Serialization**: Automatically serializes request bodies to JSON and deserializes responses to strong C# types (or `JsonObject`).
- **Resilience**: Configured with the same Polly policies as the main client, ensuring retries on transient failures.
- **Flexibility**: Can return raw `JsonObject` for dynamic scenarios or typed objects for strict contracts.

## 3. Usage Example
If you have an Apex class:
```java
@RestResource(urlMapping='/myapi/calculate/*')
global class MyApi {
    @HttpPost
    global static Decimal calculate(Decimal amount) { ... }
}
```
You can call it from C#:
```csharp
var result = await _apexService.PostAsync<decimal>("/myapi/calculate", new { amount = 100 });
```

## 4. Design Decisions
- **Separation from DataService**: While `DataService` handles standard SObject CRUD, `ApexService` is dedicated to custom logic. This distinction helps keep the API surface clean.
