# TagHelpers Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/TagHelpers` directory implements server-side rendering helpers for Razor views. These helpers make it easy to build HTML forms that respect Salesforce metadata and permissions.

## 2. Key Components

### `SalesforceFieldTagHelper` (`sf-field`)
**Usage**: `<input sf-object="Account" sf-field="Name" />`
**Logic**:
1.  Looks up the field metadata for `Account.Name`.
2.  Checks **Field Level Security (FLS)** via `IPermissionService`.
3.  **No Access**: Renders nothing (or a hidden input).
4.  **Read Only**: Adds `readonly="readonly"` attribute.
5.  **Edit**: Renders a standard input.
6.  **Validation**: Automatically adds `data-val-*` attributes based on max length, required status, etc.

### `SalesforcePermissionTagHelper` (`sf-permission`)
**Usage**:
```html
<div sf-permission="Delete" sf-object="Account">
    <button>Delete Account</button>
</div>
```
**Logic**: Conditionally renders the content only if the user has the specified permission.

### `SalesforcePicklistTagHelper`
**Purpose**: Automatically renders a `<select>` dropdown populated with the active picklist values for a field.

## 3. Design Decisions
- **Security by Default**: Developers don't need to manually write `if (canEdit) { <input> } else { <span> }`. The tag helper handles this, reducing the risk of accidentally exposing editable fields to unauthorized users.
