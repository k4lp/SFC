# Visibility System: Atomic Handlers Reference

This document details the built-in "Atomic Handlers" available in the SalesforceCore Visibility System. These handlers are the building blocks of your visibility policies.

## 1. Role Handler

Checks if the current authenticated user belongs to a specific .NET Identity Role.

*   **Type**: `Role`
*   **Dependency**: `System.Security.Claims.ClaimsPrincipal` (Standard ASP.NET User)

### Configuration Settings
| Property | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `Role` | string | Yes | The name of the role to check (case-sensitive depending on Identity setup). |

### Example
```json
{
  "Type": "Role",
  "Settings": {
    "Role": "SystemAdministrator"
  }
}
```

---

## 2. SalesforcePermission Handler

Checks granular permissions against the Salesforce Metadata API. This handler uses the `IPermissionService` to ensure rules respect the user's Profile and Permission Sets in Salesforce.

*   **Type**: `SalesforcePermission`
*   **Dependency**: `IPermissionService`, `ISchemaService`

### Configuration Settings
| Property | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `Object` | string | Yes | The API name of the Salesforce SObject (e.g., `Account`, `CustomObj__c`). |
| `Action` | string | Yes | The action to check: `Read`, `Create`, `Update`, `Delete`. |
| `Field` | string | No | Optional. If provided, checks Field-Level Security (FLS) for that specific field. If omitted, checks Object-Level Security (CRUD). |

### Examples

#### Object-Level Check (CRUD)
*Checks if the user can CREATE a new Lead.*
```json
{
  "Type": "SalesforcePermission",
  "Settings": {
    "Object": "Lead",
    "Action": "Create"
  }
}
```

#### Field-Level Check (FLS)
*Checks if the user has READ access to the 'AnnualRevenue' field on Account.*
```json
{
  "Type": "SalesforcePermission",
  "Settings": {
    "Object": "Account",
    "Field": "AnnualRevenue",
    "Action": "Read"
  }
}
```

---

## Handler Evaluation Logic

The `VisibilityService` processes these handlers based on the Policy's `Strategy`.

### "All" Strategy (AND)
The evaluator iterates through the requirements.
1.  If a handler returns `false`, evaluation stops immediately, and the Policy returns `false`.
2.  If all handlers return `true`, the Policy returns `true`.

### "Any" Strategy (OR)
The evaluator iterates through the requirements.
1.  If a handler returns `true`, evaluation stops immediately, and the Policy returns `true`.
2.  If all handlers return `false`, the Policy returns `false`.

### Error Handling
*   **Missing Handler**: If a Policy references a `Type` that is not registered (e.g., "FeatureFlag"), the requirement is considered **FAILED** (returns false), and an error is logged.
*   **Missing User**: If there is no authenticated user context, all checks generally return `false`.

### Current Behavior Notes

- Handler resolution is case-insensitive; register handler types that match configuration `Type` values.
- Exceptions inside handlers are caught and logged; the requirement returns false to stay fail-safe.
