# Tutorial: MVC CRUD App

Build a simple ASP.NET Core MVC app that lists, creates, edits, and deletes Salesforce records.

## Prerequisites
- .NET 10 SDK; SalesforceCore + SalesforceCore.AspNetCore installed.
- Connected App with PKCE; appsettings configured as in [01-Getting-Started.md](01-Getting-Started.md).

## Step 1: Register Services
```csharp
using SalesforceCore.AspNetCore.Extensions;

builder.Services.AddSalesforceCoreMvc(builder.Configuration);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);
```

## Step 2: Enable Middleware & Routes
```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseSalesforceCore();
app.MapSalesforceRoutes("sf"); // optional built-in CRUD
```

## Step 3: Create a Model
```csharp
[SalesforceObject("Contact")]
public class ContactModel
{
    [SalesforceId] public string? Id { get; set; }
    [SalesforceField("FirstName", Required = true)] public string? FirstName { get; set; }
    [SalesforceField("LastName", Required = true)] public string? LastName { get; set; }
    [SalesforceField("Email")] public string? Email { get; set; }
}
```

## Step 4: Controller
```csharp
public class ContactsController : Controller
{
    private readonly ITypedDataService _data;
    public ContactsController(ITypedDataService data) => _data = data;

    public async Task<IActionResult> Index()
    {
        var items = await _data.Query<ContactModel>().Take(50).ToListAsync();
        return View(items);
    }

    public IActionResult Create() => View(new ContactModel());

    [HttpPost]
    public async Task<IActionResult> Create(ContactModel model)
    {
        if (!ModelState.IsValid) return View(model);
        await _data.CreateAsync(model);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var model = await _data.GetByIdAsync<ContactModel>(id);
        if (model == null) return NotFound();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ContactModel model)
    {
        if (!ModelState.IsValid) return View(model);
        await _data.UpdateAsync(model);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(string id)
    {
        await _data.DeleteAsync<ContactModel>(id);
        return RedirectToAction(nameof(Index));
    }
}
```

## Step 5: Views
- **Index.cshtml**: table of contacts with Edit/Delete links.
- **Create/Edit.cshtml**: use tag helpers for inputs:
```html
<form asp-action="Create" method="post">
  <input asp-for="FirstName" class="form-control" />
  <input asp-for="LastName" class="form-control" />
  <input asp-for="Email" class="form-control" />
  <button type="submit" class="btn btn-primary">Save</button>
</form>
```

## Step 6: Run & Test
- `dotnet run`
- Sign in via PKCE, navigate to `/Contacts`, create/edit/delete records.

## Next Steps
- Add lookups/picklists with tag helpers (see [15-Tag-Helpers.md](15-Tag-Helpers.md)).
- Add validation and change tracking (see [16-Backbone-Infrastructure.md](16-Backbone-Infrastructure.md)).
