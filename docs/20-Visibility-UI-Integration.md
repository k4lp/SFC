# Visibility System: UI Integration Guide

This guide explains how to apply your configured Visibility Policies to different parts of your application's UI.

## 1. Razor Views (Tag Helper)

For standard MVC views (`.cshtml`), use the `sfc-policy` Tag Helper. This helper can be added to **any** HTML element. If the policy fails, the element and all its children are removed from the rendered output.

### Usage
1.  Ensure `SalesforceCore.AspNetCore.TagHelpers` is imported in `_ViewImports.cshtml`.
    ```cshtml
    @addTagHelper *, SalesforceCore.AspNetCore
    ```

2.  Apply the attribute to an element.

### Examples

**Basic Button Visibility**
```html
<a href="/admin/settings" class="btn btn-primary" sfc-policy="IsAdmin">
    Admin Settings
</a>
```

**Complex Section Visibility**
```html
<div class="card" sfc-policy="CanViewSensitiveData">
    <div class="card-header">Sensitive Financials</div>
    <div class="card-body">
        <!-- This entire block is suppressed if the policy fails -->
        <p>Revenue: @Model.Revenue</p>
    </div>
</div>
```

---

## 2. Dynamic UI System

The Visibility System is deeply integrated into the Dynamic UI framework. You can assign policies to almost any component in your `dynamic_ui.json` configuration file. The `LayoutDescriptorService` evaluates these policies on the server before sending the UI description to the client.

### Supported Components
You can add the `"VisibilityPolicy"` property to:
*   **Navigation Items**
*   **Objects**
*   **Fields** (in Forms)
*   **Actions** (Buttons)

### Configuration Example (`dynamic_ui.json`)

```json
{
  "Navigation": {
    "Items": [
      {
        "Label": "Admin Console",
        "Route": "/admin",
        "VisibilityPolicy": "IsAdmin" 
      }
    ]
  },
  "Objects": {
    "Account": {
      "Form": {
        "Fields": [
          {
             "FieldName": "AccountNumber",
             "VisibilityPolicy": "CanViewSensitiveData"
          }
        ]
      },
      "CustomActions": [
        {
          "Id": "approve_account",
          "Label": "Approve",
          "Type": "custom",
          "VisibilityPolicy": "CanApproveOrders"
        }
      ]
    }
  }
}
```

### How It Works
1.  **Request**: Browser requests a form descriptor (e.g., `GET /api/dynamic-ui/forms/Account?mode=Edit`).
2.  **Processing**: `LayoutDescriptorService` builds the descriptor.
3.  **Evaluation**: It iterates through fields and actions.
    *   It checks `VisibilityPolicy` against the `IVisibilityService`.
4.  **Filtering**: If a policy returns `false`:
    *   **Fields**: Marked `IsVisible = false`.
    *   **Actions/Nav Items**: Completely removed from the list.
5.  **Response**: The JSON sent to the client only contains authorized elements.

### Additional Behavior
- Object-level `VisibilityPolicy` now gates entire form/list/detail descriptors (`IsVisible = false` when denied).
- Row/Bulk/List/Detail actions defined in `dynamic_ui.json` honor `VisibilityPolicy` and `RequiredPermission`, with unauthorized actions removed when `HideUnauthorizedActions = true`.
- Related lists respect `VisibilityPolicy` and `ShowCreateButton` to suppress child creation when configured.
- Standard actions (create/edit/delete/view/bulk) respect `HideUnauthorizedActions`; when false, unauthorized actions are present but disabled.

### Integration Notes

- Dynamic UI endpoints (`/api/dynamic-ui/*`) evaluate visibility server-side; filtered descriptors never include elements the user cannot see.
- Tag helper `sfc-policy` uses the same policy engine; unknown/missing policies result in suppressed output.
- Ensure `IUserContextProvider` is registered in non-ASP.NET hosts so policies evaluate correctly.

---

## 3. Programmatic Usage

You can also inject `IVisibilityService` into your own Controllers or Services to check policies manually.

```csharp
public class MyController : Controller
{
    private readonly IVisibilityService _visibility;

    public MyController(IVisibilityService visibility)
    {
        _visibility = visibility;
    }

    public async Task<IActionResult> SensitiveAction()
    {
        if (!await _visibility.EvaluatePolicyAsync("CanViewSensitiveData"))
        {
            return Forbid();
        }

        return View();
    }
}
```
