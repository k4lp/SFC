# Tag Helpers (SalesforceCore.AspNetCore)

UI building blocks for MVC views.

## Requirements
- Add `SalesforceCore.AspNetCore` and call `AddSalesforceCoreMvc` + `AddSalesforceAuthentication`.
- Ensure `_ViewImports.cshtml` includes `@addTagHelper *, SalesforceCore.AspNetCore`.

## Lookup (`sf-lookup`)
- Purpose: AJAX search + select for lookup fields.
- Attributes:
  - `asp-for`: bound property.
  - `sf-target-object` (optional): target object API name (recommended when you know it).
  - `sf-object` (optional): source object API name (used to infer the lookup target from field metadata when `sf-target-object` is not provided).
  - `sf-field` (optional): lookup field API name (defaults to `asp-for` name).
  - `sf-display-template` (optional): display template for results (e.g., `{Name} ({AccountNumber})`).
  - `sf-search-fields` (optional): comma-separated search fields (defaults to Name).
  - `sf-min-chars` (optional): minimum characters before search triggers.
  - `sf-debounce` (optional): debounce delay (ms) for input.
  - `sf-limit` (optional): maximum results.
  - `sf-placeholder` (optional): placeholder text.

Example:
```html
<sf-lookup asp-for="AccountId" sf-target-object="Account" sf-placeholder="Search accounts..." />
```

## Picklist (`sf-picklist`)
- Purpose: Render picklists with record type awareness.
- Attributes:
  - `asp-for`: bound property.
  - `sf-object`: target object.
  - `sf-record-type-id` (optional): record type context.

Example:
```html
<sf-picklist asp-for="Industry" sf-object="Account" class="form-select"></sf-picklist>
```

## Model Form (`sf-model-form`)
- Purpose: Rapid form scaffolding from metadata.
- Attributes:
  - `asp-model`: model instance.
  - `sf-object`: object API name.
  - `sf-columns` (optional): column count.

Example:
```html
<sf-model-form asp-model="Model" sf-object="Contact" sf-columns="2"></sf-model-form>
```

## Notes
- Honors FLS and createable/updateable flags.
- Works with HTMX partials; detects `HX-Request`.
- Embedded assets are served by `app.UseSalesforceCore()` at `/_salesforce` (current default). If you need a different asset strategy, mount your own static files or bundle assets into your app.
- Record-type-aware picklists and dependent picklists use the Salesforce UI API when `sf-record-type-id` is set; values are filtered per record type.
- Visibility policies can be applied with `sfc-policy` (fail closed when policy is missing/false); ensure `IUserContextProvider` is registered.

## Next Steps
- Custom MVC usage: [12-Custom-MVC-Guide.md](12-Custom-MVC-Guide.md).
- MVC tutorial: [12-Tutorial-MVC-CRUD-App.md](12-Tutorial-MVC-CRUD-App.md).
