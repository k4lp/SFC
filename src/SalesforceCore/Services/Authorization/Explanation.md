# Authorization Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Authorization` directory handles the logic for determining **what a user is allowed to do**. This goes beyond simple authentication (who you are) and deals with permissions (what you can do).

## 2. Key Components

### `IPermissionService`
**Purpose**: The main entry point for permission checks.
**Capabilities**:
- **Snapshotting**: Retrieves a comprehensive "Snapshot" of permissions for an object (Create, Read, Edit, Delete flags + Field Level Security).
- **Caching**: Permissions are cached to avoid expensive metadata calls on every request.
- **Action Checks**: `CanPerformActionAsync` (e.g., can I delete this Account?).
- **Field Access**: `CanAccessFieldAsync` (e.g., can I edit the 'Revenue' field?).

### `IVisibilityService` & Handlers
**Purpose**: Controls which UI elements (modules, menus, buttons) should be visible to the user.
**Mechanism**:
- Uses `IVisibilityRequirementHandler` implementations to evaluate rules.
- **`RoleHandler`**: Checks if the user has a specific Salesforce Role.
- **`SalesforcePermissionHandler`**: Checks for specific Permission Set assignments or Custom Permissions.

### `IUserContextProvider`
**Purpose**: Abstraction for retrieving the current user's context (ID, Organization ID, Timezone).
**Implementation**:
- **`DefaultUserContextProvider`**: Likely pulls from a static configuration or the active token for console apps.
- **ASP.NET Core**: In a web context, this is typically overridden to pull from `HttpContext.User`.

## 3. Relationship to Security
While `FieldLevelSecurityService` (in `src/SalesforceCore/Security`) enforces rules at the data layer, `PermissionService` provides the high-level queries used by the UI to hide/disable buttons *before* the user even tries an action.
