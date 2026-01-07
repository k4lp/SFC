# Filters Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Filters` directory contains ASP.NET Core Action Filters.

## 2. Key Components

### `SalesforceValidateAttribute`
**Purpose**: Server-side validation enforcement.
**Logic**:
- Intercepts requests to Controller Actions.
- If a model is present (e.g., a form submission), it may trigger the `IValidationRuleEngine` to run client-side validation rules again on the server.
- Ensures that invalid data is caught before reaching the `DataService`.

## 3. Usage
```csharp
[HttpPost]
[SalesforceValidate]
public async Task<IActionResult> Save(AccountModel model) { ... }
```
