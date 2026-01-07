# Layout Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Layout` directory implements the **Dynamic UI System**. This is a powerful feature that allows the application to render forms, lists, and navigation menus entirely from metadata, rather than hardcoding HTML/Razor views for every object.

## 2. Key Components

### `ILayoutDescriptorService`
**Purpose**: The central factory for UI descriptors.
**Key Methods**:
- **`GetFormAsync`**: Returns a `FormDescriptor` (List of Sections -> Rows -> Fields) for a given Object and Mode (Create/Edit/View). It automatically applies Field Level Security (hiding fields the user can't see) and Layout metadata (ordering fields correctly).
- **`GetListAsync`**: Returns a `ListDescriptor` (Columns, Sort Order) for rendering data grids.
- **`GetNavigationAsync`**: Returns a hierarchical menu structure based on available modules and user permissions.

### Descriptors (Implied Models)
- **`FormDescriptor`**: A JSON-serializable structure representing a form.
- **`FieldDescriptor`**: Metadata for a single input (Label, Type, Required, ReadOnly, PicklistOptions).

## 3. Design Decisions
- **Metadata Abstraction**: The UI layer (e.g., React or Razor) should be "dumb". It simply iterates over the descriptors provided by this service. This ensures that if a field is made "Read Only" in Salesforce, the UI updates automatically without code changes.
- **Permission Aware**: This service tightly integrates with `IPermissionService`. If a user loses access to the "Account" object, the "Account" menu item disappears automatically.
