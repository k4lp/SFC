# Security Directory Explanation

## 1. Overview
The `src/SalesforceCore/Security` directory implements security controls, primarily **Field Level Security (FLS)**. In Salesforce, just because a user has access to a record doesn't mean they can see or edit all fields on it. This module ensures the application respects those fine-grained permissions.

## 2. Key Components

### `FieldLevelSecurityService`
**Purpose**: The enforcement engine for FLS.
**Key Capabilities**:
- **`GetReadableFieldsAsync` / `GetCreateableFieldsAsync`**: Returns lists of fields the user is allowed to interact with.
- **`CanReadFieldAsync` / `CanUpdateFieldAsync`**: specific checks for a single field.
- **`Filter*FieldsAsync`**: Takes a dictionary of data (e.g., from a form submission) and silently strips out any fields the user isn't allowed to touch.
- **`ValidateFor*Async`**: Similar to filtering, but returns a failure result/exception instead of silently removing fields. This is useful for API endpoints where you want to tell the caller "Forbidden".

### `FlsValidationResult` & `FlsViolation`
**Purpose**: Structured error reporting for security failures, detailing exactly which fields failed and why.

### `[EnforceFls]` Attribute
**Purpose**: A declarative way to secure service methods.
**Usage**: Can be placed on a method to automatically trigger FLS checks before execution.

## 3. Design Decisions
- **Safety First**: The library defaults to checking permissions. `DataService` uses this service internally to prevent accidental privilege escalation (e.g., a user blindly updating a "readonly" field via a mass-assignment vulnerability).

## 4. FLS Enforcement Modes

The `FlsEnforcementMode` setting in `SalesforceOptions` controls how violations are handled:

| Mode   | Write Operations                                | Use Case                        |
|--------|------------------------------------------------|----------------------------------|
| Silent | Inaccessible fields quietly dropped (default)  | Production - graceful degradation |
| Strict | `FlsException` thrown with violation details   | Development - catch issues early |
| None   | No FLS filtering performed                     | Testing or legacy compatibility  |

**Configuration Example**:
```csharp
services.AddSalesforceCore(options => {
    options.EnforceFieldLevelSecurity = true;
    options.FlsEnforcementMode = FlsEnforcementMode.Strict;
});
```

