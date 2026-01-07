# Custom MVC Guide

How to build bespoke ASP.NET Core MVC experiences with SalesforceCore.AspNetCore.

## Requirements
- **Required**: SalesforceCore + SalesforceCore.AspNetCore packages; PKCE auth configured.
- **Recommended**: Server-side auth tickets (`useServerSideSessions: true`) backed by `IDistributedCache`; HTMX enabled for partials.
- **Optional**: Custom layout/static assets via MVC options.

## Register Services
```csharp
using SalesforceCore.AspNetCore.Extensions;

builder.Services.AddSalesforceCoreMvc(builder.Configuration);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);
```

## Use Built-in Controllers (optional)
```csharp
app.MapControllers();
```
- Routes (attribute-routed):
  - CRUD: `/Salesforce/{sObject}` (list), `/Salesforce/{sObject}/Details/{id}`, `Create`, `Edit`, `Delete`, `Upload`
  - Lookup: `/Lookup/Search`, `/Lookup/Recent`, `/Lookup/Resolve`
  - Files: `/File/Download/{versionId}/{filename?}`, `/File/GetImage/{versionId}`, `/File/Preview/{versionId}`
- Good for admin-style CRUD; replace with custom controllers for bespoke flows.

## Tag Helpers Highlights
- `sf-lookup` – AJAX-powered lookup fields.
- `sf-picklist` – Dynamic picklists with record type support.
- `sf-model-form` – Quick form scaffolding from metadata.
- See [15-Tag-Helpers.md](15-Tag-Helpers.md) for details.

## Current Behavior Notes
- Dynamic UI/visibility services require `IUserContextProvider`; ASP.NET hosts register this automatically via `AddSalesforceCoreMvc`.
- Picklists respect record type when `recordTypeId` is supplied; dependent picklists are filtered using record-type-specific values.
- `DynamicUi:WatchConfigFile` enables hot reload of `dynamic_ui.json`; caches are user-scoped, so forms/lists reflect current user permissions.

## Custom Controllers
Inherit from your own controllers, inject services you need:
```csharp
public class OpportunitiesController : Controller
{
    private readonly ITypedDataService _data;
    public OpportunitiesController(ITypedDataService data) => _data = data;

    public async Task<IActionResult> Index()
    {
        var items = await _data.Query<Opportunity>()
            .OrderByDescending(o => o.CreatedDate)
            .Take(50)
            .ToListAsync();
        return View(items);
    }
}
```

## Custom Views
- Combine tag helpers with your layout; if you need custom CSS/JS, mount your own static files or bundle assets into your app.
- Use partials for HTMX responses (`HX-Request` header).

## File Uploads
- Ensure `EnableFileUploads` is true; it controls both the upload UI and the upload endpoint. Respect `MaxFileUploadSize` and `AllowedFileExtensions`.
- Use `/Salesforce/{sObject}/Upload/{id}` for uploads, `/File/*` for downloads/previews, or call `IDataService.UploadFileAsync`.

## Validation
- Use `[SalesforceValidate]` attribute to apply validation engine before actions.
- Keep `EnforceFieldLevelSecurity` enabled to avoid leaking unauthorized fields.

## Layout & Assets
- Embedded assets are served at `StaticFilesPath` (default: `/_salesforce`) by `app.UseSalesforceCore()` when `UseEmbeddedStaticFiles` is enabled.
- Set `UseEmbeddedViews` to false to remove compiled Razor views, and `UseEmbeddedStaticFiles` to false to supply your own assets.

## Next Steps
- Tag helpers reference: [15-Tag-Helpers.md](15-Tag-Helpers.md).
- Tutorial walk-through: [12-Tutorial-MVC-CRUD-App.md](12-Tutorial-MVC-CRUD-App.md).
