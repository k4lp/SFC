# Tests Project Explanation

## 1. Overview
The `tests/SalesforceCore.Tests` project contains a comprehensive suite of Unit and Integration tests to ensure the reliability of the core library.

## 2. Testing Stack
- **xUnit**: The test runner.
- **FluentAssertions**: For readable assertions (`result.Should().BeTrue()`).
- **Moq**: For mocking dependencies (`ISalesforceClient`, `ICacheProvider`) to isolate the unit under test.

## 3. Key Test Categories

### Service Logic Tests (Unit)
Tests files like `DataServiceTests.cs` verify business logic without making network calls.
- **Mocking**: We mock `ISalesforceClient` to return predefined JSON responses.
- **Verification**: We assert that `DataService` calls the correct endpoint (e.g., `/query/?q=...`) and handles parameters correctly.

### Client Resilience Tests
Tests like `SalesforceClientRobustnessTests.cs` verify the HTTP client's behavior under failure conditions.
- **Token Rotation**: Simulates a 401 Unauthorized response to ensure the client automatically calls `RefreshTokenAsync` and retries the original request with the new token.
- **Edge Cases**: Verifies handling of 204 No Content responses for DELETE/PUT operations.

### Mapping Tests
Tests like `SalesforceMapperTests.cs` ensure that:
- Attributes (`[SalesforceField]`) are read correctly.
- Types are converted properly (e.g., Salesforce ISO8601 dates to C# `DateTimeOffset`).
- `[SalesforceIgnore]` fields are excluded from payloads.

### Query Generation Tests
Tests like `SalesforceQueryableInterfaceTests.cs` and `SoqlExpressionVisitorTests.cs` verify that C# LINQ expressions are correctly translated into valid SOQL strings, covering `Where`, `Select`, `OrderBy`, and sub-queries.
