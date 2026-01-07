# Visibility System: Overview & Configuration

The **SalesforceCore Visibility System** is an enterprise-grade, policy-based authorization engine designed to decouple UI rendering from hardcoded logic. It allows developers to define complex, atomic visibility rules (Policies) in configuration and apply them to any UI element—Razor views, Dynamic Forms, Navigation menus, or Actions.

## Core Concepts

### 1. Policies
A **Policy** is a named collection of rules that determines if something should be visible. Policies are defined centrally in your application configuration (e.g., `appsettings.json`).

*   **Name**: A unique string ID (e.g., `CanApproveOrders`, `IsSuperUser`).
*   **Strategy**: Determines how requirements are combined.
    *   `All` (AND): All requirements must pass for the policy to be true.
    *   `Any` (OR): At least one requirement must pass for the policy to be true.
*   **Requirements**: A list of atomic rules to evaluate.

### 2. Atomic Requirements
A **Requirement** is a single unit of logic handled by a specific *Handler*.
*   **Type**: Identifies which handler to use (e.g., "Role", "SalesforcePermission").
*   **Settings**: Arbitrary JSON configuration specific to that handler.

## Configuration Structure

The system is configured under the `Salesforce:Visibility` section in `appsettings.json`.

```json
{
  "Salesforce": {
    "Visibility": {
      "Policies": {
        "PolicyName": {
          "Strategy": "All",
          "Requirements": [
            {
              "Type": "HandlerName",
              "Settings": { ... }
            }
          ]
        }
      }
    }
  }
}
```

## Complete Example

Here is a comprehensive example demonstrating various policy strategies and requirement combinations.

```json
{
  "Salesforce": {
    "Visibility": {
      "Policies": {
        // Policy 1: Simple Role Check
        // Visible if user is in 'Admin' role
        "IsAdmin": {
          "Requirements": [
            { "Type": "Role", "Settings": { "Role": "Admin" } }
          ]
        },

        // Policy 2: Complex Permission Check (AND Strategy)
        // Visible ONLY if user is a 'Manager' AND has 'Edit' permission on 'Account'
        "CanManageAccounts": {
          "Strategy": "All",
          "Requirements": [
            { "Type": "Role", "Settings": { "Role": "Manager" } },
            { 
              "Type": "SalesforcePermission", 
              "Settings": { "Object": "Account", "Action": "Edit" } 
            }
          ]
        },

        // Policy 3: Alternative Access (Any Strategy)
        // Visible if user is 'Admin' OR has 'ViewAllData' permission
        "CanViewSensitiveData": {
          "Strategy": "Any",
          "Requirements": [
            { "Type": "Role", "Settings": { "Role": "Admin" } },
            { 
              "Type": "SalesforcePermission", 
              "Settings": { "Object": "Account", "Field": "SSN__c", "Action": "Read" } 
            }
          ]
        }
      }
    }
  }
}
```

## Key Benefits

1.  **Decoupling**: Your Razor views and UI configurations no longer contain logic like `if (User.IsInRole("Admin") && perm.CanCreate)`. They just say `sfc-policy="CanManageAccounts"`.
2.  **Centralization**: All visibility logic is in one file. Changing a business rule (e.g., "Managers can no longer delete") happens in JSON, not C#.
3.  **Granularity**: You can target specific Fields, Objects, or Actions.
4.  **Extensibility**: You can add your own handlers (e.g., for Feature Flags or IP restrictions) without changing the core library.

## Implementation Updates

- Policies are evaluated against the current user via `IUserContextProvider` (ASP.NET hosts wire the HTTP context; non-HTTP hosts can register their own provider).
- Missing or unknown policy/handler returns false (fail closed); ensure configuration keys match registered handlers.
