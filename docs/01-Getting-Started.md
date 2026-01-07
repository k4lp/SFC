# Getting Started with SalesforceCore

This comprehensive guide will take you from zero to a fully functional Salesforce-integrated .NET application. Whether you're building a web application, API service, or background worker, this guide covers everything you need to know.

## Table of Contents

1. [Introduction](#introduction)
2. [Prerequisites](#prerequisites)
3. [Installation](#installation)
4. [Salesforce Connected App Setup](#salesforce-connected-app-setup)
5. [Project Configuration](#project-configuration)
6. [Authentication Flows](#authentication-flows)
7. [Web Application Quick Start](#web-application-quick-start)
8. [Worker Service Quick Start](#worker-service-quick-start)
9. [API-Only Application Quick Start](#api-only-application-quick-start)
10. [Creating Your First Model](#creating-your-first-model)
11. [Querying Data](#querying-data)
12. [Creating Records](#creating-records)
13. [Updating Records](#updating-records)
14. [Deleting Records](#deleting-records)
15. [Using Tag Helpers](#using-tag-helpers)
16. [Dynamic UI Integration](#dynamic-ui-integration)
17. [Testing Your Integration](#testing-your-integration)
18. [Troubleshooting](#troubleshooting)
19. [Next Steps](#next-steps)

---

## Introduction

SalesforceCore is a comprehensive .NET library for Salesforce integration. It provides:

- **Strongly-typed data access** with compile-time checking
- **LINQ-to-SOQL** query provider for intuitive querying
- **Multiple authentication flows** (OAuth PKCE, JWT, Client Credentials)
- **High-volume operations** via Bulk API 2.0 and Composite APIs
- **Dynamic UI generation** based on Salesforce metadata and permissions
- **ASP.NET Core integration** with Tag Helpers and middleware

### Supported .NET Versions

| Version | Support Level |
|---------|---------------|
| .NET 10.0 | Full Support (Primary Target) |

### Package Architecture

```
┌─────────────────────────────────────────────┐
│           Your Application                   │
├─────────────────────────────────────────────┤
│  SalesforceCore.AspNetCore (Web Apps)       │
│  - Tag Helpers                               │
│  - Controllers                               │
│  - Authentication                            │
│  - Middleware                                │
├─────────────────────────────────────────────┤
│  SalesforceCore (Core Library)              │
│  - Data Services                             │
│  - Schema Services                           │
│  - Permission Services                       │
│  - Layout Services                           │
│  - Bulk/Composite Services                   │
├─────────────────────────────────────────────┤
│        Salesforce REST APIs                  │
└─────────────────────────────────────────────┘
```

---

## Prerequisites

### Required

1. **.NET SDK** - Version 10.0 or later
   ```bash
   # Check your installed version
   dotnet --version

   # Download from: https://dotnet.microsoft.com/download
   ```

2. **Salesforce Org** - Developer Edition, Sandbox, or Production
   - API access must be enabled
   - User must have "API Enabled" permission

3. **Salesforce Connected App** - For OAuth authentication
   - Will be created in the next section

### Recommended

1. **Redis** - For distributed caching in production
   ```bash
   # Docker
   docker run -d -p 6379:6379 redis:latest

   # Or use Azure Cache for Redis, AWS ElastiCache, etc.
   ```

2. **IDE** - Visual Studio 2022, VS Code with C# Dev Kit, or JetBrains Rider

3. **Salesforce CLI** (sf) - For development and testing
   ```bash
   npm install -g @salesforce/cli
   ```

### Optional

1. **SalesforceCore Model Generator** - CLI tool for scaffolding models
   ```bash
   dotnet tool install -g SalesforceCore.ModelGenerator
   ```

---

## Installation

### NuGet Package Installation

```bash
# Create a new project (if starting fresh)
dotnet new webapi -n MySalesforceApp
cd MySalesforceApp

# Install the core library (required)
dotnet add package SalesforceCore

# Install ASP.NET Core integration (recommended for web apps)
dotnet add package SalesforceCore.AspNetCore

# Install model generator CLI (optional, global tool)
dotnet tool install -g SalesforceCore.ModelGenerator
```

### Package Reference in .csproj

For more control, add packages directly to your `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core library - required -->
    <PackageReference Include="SalesforceCore" Version="1.0.0" />

    <!-- ASP.NET Core integration - for web apps -->
    <PackageReference Include="SalesforceCore.AspNetCore" Version="1.0.0" />

    <!-- Optional: Redis caching -->
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.0" />
  </ItemGroup>
</Project>
```

### Verifying Installation

After installation, verify the packages are correctly referenced:

```bash
dotnet list package
```

Expected output:
```
Project 'MySalesforceApp' has the following package references:
   [net10.0]:
   Top-level Package                                        Requested   Resolved
   > SalesforceCore                                         1.0.0       1.0.0
   > SalesforceCore.AspNetCore                              1.0.0       1.0.0
```

---

## Salesforce Connected App Setup

A Connected App is required for OAuth authentication. Follow these steps carefully:

### Step 1: Navigate to App Manager

1. Log in to your Salesforce org
2. Go to **Setup** (gear icon → Setup)
3. In Quick Find, search for **App Manager**
4. Click **App Manager**

### Step 2: Create New Connected App

1. Click **New Connected App** button
2. Fill in the **Basic Information**:
   - **Connected App Name**: `My .NET Application`
   - **API Name**: `My_NET_Application` (auto-generated)
   - **Contact Email**: your.email@company.com

### Step 3: Configure OAuth Settings

1. Check **Enable OAuth Settings**
2. **Callback URL**:
   - For development: `https://localhost:5001/salesforce/callback`
   - For production: `https://your-domain.com/salesforce/callback`
   - Add multiple URLs for different environments

3. **Selected OAuth Scopes** - Add these scopes:
   | Scope | Purpose |
   |-------|---------|
   | `Access and manage your data (api)` | REST API access |
   | `Access your basic information (id, profile, email, address, phone)` | User identity |
   | `Perform requests on your behalf at any time (refresh_token, offline_access)` | Token refresh |
   | `Full access (full)` | Complete access (use cautiously) |

4. **Additional Settings**:
   - Check **Require Secret for Web Server Flow** (for server-side apps)
   - Check **Require Secret for Refresh Token Flow**
   - Check **Enable Client Credentials Flow** (for server-to-server)

### Step 4: Configure OAuth Policies

After saving, click **Manage** then **Edit Policies**:

1. **Permitted Users**: Choose based on your needs:
   - `Admin approved users are pre-authorized` - Most secure
   - `All users may self-authorize` - For user-facing apps

2. **IP Relaxation**:
   - `Relax IP restrictions` for development
   - `Enforce IP restrictions` for production

3. **Refresh Token Policy**:
   - `Refresh token is valid until revoked` - Recommended

### Step 5: Retrieve Credentials

1. Go back to **App Manager**
2. Find your app and click the dropdown → **View**
3. Click **Manage Consumer Details**
4. Verify your identity (may require verification code)
5. Copy:
   - **Consumer Key** (Client ID)
   - **Consumer Secret** (Client Secret)

### Step 6: Configure for JWT (Optional)

For server-to-server authentication without user interaction:

1. Generate a certificate:
   ```bash
   # Generate private key
   openssl genrsa -out server.key 2048

   # Generate certificate
   openssl req -new -x509 -sha256 -key server.key -out server.crt -days 365
   ```

2. In Connected App settings:
   - Check **Use digital signatures**
   - Upload `server.crt`

3. Create a dedicated integration user and pre-authorize the Connected App

---

## Project Configuration

### Basic Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SalesforceCore": "Debug"
    }
  },

  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0",
    "CallbackPath": "/salesforce/callback"
  }
}
```

Note: `ClientSecret` is only required for Client Credentials flow. PKCE does not use it.

### Complete Configuration Reference

```json
{
  "Salesforce": {
    // === Required Settings ===
    "ClientId": "YOUR_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0",

    // === Authentication ===
    "ClientSecret": "YOUR_CONSUMER_SECRET", // Optional; required for Client Credentials flow
    "CallbackPath": "/salesforce/callback",
    // Scopes are fixed by AddSalesforceAuthentication (openid/profile/email/api/refresh_token).

    // === HTTP Client Settings ===
    "HttpTimeout": "00:00:30",
    "MaxRetries": 3,
    "RetryBaseDelay": "00:00:01",
    "TotalRequestTimeout": "00:01:00",

    // === Bulk API Settings ===
    "BulkPollInterval": "00:00:05",
    "BulkJobTimeout": "00:30:00",

    // === Caching ===
    "CacheProvider": "Memory",
    "CacheKeyPrefix": "SF_",
    "SchemaCacheDuration": "01:00:00",
    "LookupCacheDuration": "00:15:00",

    // === Pagination ===
    "DefaultPageSize": 25,
    "MaxPageSize": 100,

    // === Security ===
    "EnforceFieldLevelSecurity": true,
    "ValidateSoqlInputs": true,
    "MaxFileUploadSize": 26214400,

    // === Debugging ===
    "EnableDebugLogging": false
  },

  "SalesforceMvc": {
    "RoutePrefix": "sf",
    "UseEmbeddedViews": true,
    "UseEmbeddedStaticFiles": true,
    "StaticFilesPath": "/_salesforce",
    "EnableHtmx": true,
    "ShowRecordIds": false,
    "ConfirmDeletes": true,
    "EnableFileUploads": true,
    "AllowedFileExtensions": [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".png"],
    "CssFramework": "Bootstrap5",
    "ToastPosition": "TopRight",
    "ToastAutoDismissSeconds": 5
  },

  "DynamicUi": {
    "ConfigFilePath": "dynamic_ui.json",
    "WatchConfigFile": true,
    "PermissionCacheDuration": "00:05:00",
    "LayoutCacheDuration": "00:10:00",
    "BypassCache": false,
    "HideInaccessibleNavItems": true,
    "HideInaccessibleFields": true,
    "HideUnauthorizedActions": true,
    "DefaultFormColumns": 1,
    "DefaultPageSize": 25,
    "MaxPageSize": 100
  },

  "SalesforceJwt": {
    "Username": "integration-user@company.com",
    "PrivateKeyPath": "/path/to/private.key",
    "TokenExpiration": "00:05:00"
  },

  "ConnectionStrings": {
    "Redis": "localhost:6379,abortConnect=false"
  }
}
```

### Environment-Specific Configuration

Create environment-specific files:

**appsettings.Development.json**:
```json
{
  "Salesforce": {
    "Domain": "https://test.salesforce.com",
    "EnableDebugLogging": true
  }
}
```

**appsettings.Production.json**:
```json
{
  "Salesforce": {
    "Domain": "https://login.salesforce.com",
    "CacheProvider": "Distributed",
    "EnableDebugLogging": false
  }
}
```

### User Secrets for Sensitive Data

Never commit secrets to source control. Use User Secrets for development:

```bash
# Initialize user secrets
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "Salesforce:ClientId" "YOUR_CONSUMER_KEY"
dotnet user-secrets set "Salesforce:ClientSecret" "YOUR_CONSUMER_SECRET"
```

For production, use:
- Azure Key Vault
- AWS Secrets Manager
- HashiCorp Vault
- Environment variables

---

## Authentication Flows

SalesforceCore supports three OAuth 2.0 flows:

### 1. OAuth 2.0 PKCE (Web Applications)

The recommended flow for web applications with user interaction.

**How it works:**
1. User clicks "Login with Salesforce"
2. Browser redirects to Salesforce login page
3. User authenticates and grants permissions
4. Salesforce redirects back with authorization code
5. Server exchanges code for tokens
6. Tokens stored in the authentication ticket (cookie) or in `IDistributedCache` when `useServerSideSessions: true`

**Configuration:**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Salesforce services
builder.Services.AddSalesforceCoreMvc(builder.Configuration);

// Add OAuth authentication.
// Recommended: store auth tickets server-side to avoid cookie size limits (requires IDistributedCache).
builder.Services.AddDistributedMemoryCache(); // dev only; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseSalesforceCore();
app.MapSalesforceRoutes();
app.MapControllers();

app.Run();
```

### 2. JWT Bearer (Server-to-Server)

For background services, workers, and server-to-server integration without user interaction.

**How it works:**
1. Service generates JWT signed with private key
2. JWT sent to Salesforce token endpoint
3. Salesforce validates signature against uploaded certificate
4. Access token returned

**Configuration:**

```csharp
// Program.cs for Worker Service
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSalesforceCore(builder.Configuration);
builder.Services.AddScoped<ITokenProvider, JwtTokenProvider>();
builder.Services.AddHostedService<SalesforceWorker>();

var host = builder.Build();
host.Run();
```

**appsettings.json:**
```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceJwt": {
    "Username": "integration@company.com",
    "PrivateKeyPath": "/secrets/salesforce.key"
  }
}
```

### 3. Client Credentials (Trusted Server Apps)

For highly trusted server applications with direct client secret exchange.

If the `SalesforceClientCredentials` section exists and no `ITokenProvider` is already registered, `AddSalesforceCore` selects `ClientCredentialsTokenProvider` automatically.

**Configuration:**

```csharp
builder.Services.AddSalesforceCore(builder.Configuration);
builder.Services.AddScoped<ITokenProvider, ClientCredentialsTokenProvider>();
```

**Configuration (required for auto-selection):**

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "ClientSecret": "YOUR_CONSUMER_SECRET",
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceClientCredentials": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "ClientSecret": "YOUR_CONSUMER_SECRET"
  }
}
```

**Note:** Client Credentials flow requires additional Salesforce configuration and is typically used for system integrations.

---

## Web Application Quick Start

Here's a complete, working web application:

### Project Structure

```
MySalesforceApp/
├── Controllers/
│   ├── HomeController.cs
│   └── AccountController.cs
├── Models/
│   ├── Account.cs
│   └── Contact.cs
├── Views/
│   ├── Home/
│   │   └── Index.cshtml
│   ├── Account/
│   │   ├── Index.cshtml
│   │   ├── Details.cshtml
│   │   ├── Create.cshtml
│   │   └── Edit.cshtml
│   └── Shared/
│       └── _Layout.cshtml
├── wwwroot/
├── appsettings.json
├── appsettings.Development.json
└── Program.cs
```

### Program.cs

```csharp
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Salesforce services with MVC integration
builder.Services.AddSalesforceCoreMvc(builder.Configuration);
builder.Services.AddDistributedMemoryCache(); // dev only; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

// Add Dynamic UI services
builder.Services.AddSalesforceDynamicUi(options =>
{
    options.Navigation.AppName = "My Salesforce App";
    options.DefaultFormColumns = 1;
});

// Add MVC
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Enable Salesforce middleware
app.UseSalesforceCore();
app.MapSalesforceRoutes();
app.MapDynamicUiRoutes();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

### Models/Account.cs

```csharp
using SalesforceCore.Attributes;
using System.ComponentModel.DataAnnotations;

namespace MySalesforceApp.Models;

[SalesforceObject("Account")]
public class Account
{
    [SalesforceId]
    public string? Id { get; set; }

    [SalesforceField("Name", Required = true)]
    [Required(ErrorMessage = "Account name is required")]
    [StringLength(255, ErrorMessage = "Name cannot exceed 255 characters")]
    [Display(Name = "Account Name")]
    public string Name { get; set; } = string.Empty;

    [SalesforceField("Industry")]
    [Display(Name = "Industry")]
    public string? Industry { get; set; }

    [SalesforceField("AnnualRevenue")]
    [Display(Name = "Annual Revenue")]
    [DataType(DataType.Currency)]
    public decimal? AnnualRevenue { get; set; }

    [SalesforceField("Phone")]
    [Display(Name = "Phone")]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }

    [SalesforceField("Website")]
    [Display(Name = "Website")]
    [Url(ErrorMessage = "Invalid URL")]
    public string? Website { get; set; }

    [SalesforceField("Description")]
    [Display(Name = "Description")]
    [DataType(DataType.MultilineText)]
    public string? Description { get; set; }

    [SalesforceField("BillingStreet")]
    [Display(Name = "Billing Street")]
    public string? BillingStreet { get; set; }

    [SalesforceField("BillingCity")]
    [Display(Name = "Billing City")]
    public string? BillingCity { get; set; }

    [SalesforceField("BillingState")]
    [Display(Name = "Billing State/Province")]
    public string? BillingState { get; set; }

    [SalesforceField("BillingPostalCode")]
    [Display(Name = "Billing Zip/Postal Code")]
    public string? BillingPostalCode { get; set; }

    [SalesforceField("BillingCountry")]
    [Display(Name = "Billing Country")]
    public string? BillingCountry { get; set; }

    [SalesforceField("CreatedDate")]
    [Display(Name = "Created Date")]
    public DateTime? CreatedDate { get; set; }

    [SalesforceField("LastModifiedDate")]
    [Display(Name = "Last Modified Date")]
    public DateTime? LastModifiedDate { get; set; }

    // Relationship - Contacts under this Account
    [SalesforceChildRelationship("Contacts", "Contact", "AccountId")]
    public List<Contact>? Contacts { get; set; }
}
```

### Controllers/AccountController.cs

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Authorization;
using MySalesforceApp.Models;

namespace MySalesforceApp.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly ITypedDataService _dataService;
    private readonly IPermissionService _permissionService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        ITypedDataService dataService,
        IPermissionService permissionService,
        ILogger<AccountController> logger)
    {
        _dataService = dataService;
        _permissionService = permissionService;
        _logger = logger;
    }

    // GET: /Account
    public async Task<IActionResult> Index(
        string? search,
        string? industry,
        string? sortField,
        string? sortDir,
        int page = 1,
        int pageSize = 25)
    {
        // Get permissions for UI
        var permissions = await _permissionService.GetPermissionsAsync("Account");
        ViewBag.CanCreate = permissions.CanCreate;
        ViewBag.CanEdit = permissions.CanUpdate;
        ViewBag.CanDelete = permissions.CanDelete;

        // Build query
        var query = _dataService.Query<Account>();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a => a.Name.Contains(search));
            ViewBag.Search = search;
        }

        // Apply industry filter
        if (!string.IsNullOrWhiteSpace(industry))
        {
            query = query.Where(a => a.Industry == industry);
            ViewBag.Industry = industry;
        }

        // Apply sorting
        query = (sortField, sortDir) switch
        {
            ("Name", "desc") => query.OrderByDescending(a => a.Name),
            ("Name", _) => query.OrderBy(a => a.Name),
            ("Industry", "desc") => query.OrderByDescending(a => a.Industry),
            ("Industry", _) => query.OrderBy(a => a.Industry),
            ("AnnualRevenue", "desc") => query.OrderByDescending(a => a.AnnualRevenue),
            ("AnnualRevenue", _) => query.OrderBy(a => a.AnnualRevenue),
            (_, "desc") => query.OrderByDescending(a => a.LastModifiedDate),
            _ => query.OrderBy(a => a.Name)
        };

        ViewBag.SortField = sortField ?? "Name";
        ViewBag.SortDir = sortDir ?? "asc";

        // Apply pagination
        var accounts = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;

        return View(accounts);
    }

    // GET: /Account/Details/{id}
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest("Account ID is required");
        }

        var account = await _dataService.GetByIdAsync<Account>(id);

        if (account == null)
        {
            _logger.LogWarning("Account not found: {Id}", id);
            return NotFound();
        }

        // Get related contacts
        var contacts = await _dataService.Query<Contact>()
            .Where(c => c.AccountId == id)
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync();

        ViewBag.Contacts = contacts;

        // Get permissions
        var permissions = await _permissionService.GetPermissionsAsync("Account");
        ViewBag.CanEdit = permissions.CanUpdate;
        ViewBag.CanDelete = permissions.CanDelete;

        return View(account);
    }

    // GET: /Account/Create
    public async Task<IActionResult> Create()
    {
        var permissions = await _permissionService.GetPermissionsAsync("Account");

        if (!permissions.CanCreate)
        {
            TempData["Error"] = "You do not have permission to create accounts.";
            return RedirectToAction(nameof(Index));
        }

        return View(new Account());
    }

    // POST: /Account/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Account account)
    {
        if (!ModelState.IsValid)
        {
            return View(account);
        }

        try
        {
            var id = await _dataService.CreateAsync(account);

            _logger.LogInformation("Created Account: {Id} - {Name}", id, account.Name);
            TempData["Success"] = $"Account '{account.Name}' created successfully!";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create account: {Name}", account.Name);
            ModelState.AddModelError("", $"Error creating account: {ex.Message}");
            return View(account);
        }
    }

    // GET: /Account/Edit/{id}
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest("Account ID is required");
        }

        var permissions = await _permissionService.GetPermissionsAsync("Account");

        if (!permissions.CanUpdate)
        {
            TempData["Error"] = "You do not have permission to edit accounts.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var account = await _dataService.GetByIdAsync<Account>(id);

        if (account == null)
        {
            return NotFound();
        }

        return View(account);
    }

    // POST: /Account/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Account account)
    {
        if (id != account.Id)
        {
            return BadRequest("ID mismatch");
        }

        if (!ModelState.IsValid)
        {
            return View(account);
        }

        try
        {
            await _dataService.UpdateAsync(account);

            _logger.LogInformation("Updated Account: {Id} - {Name}", id, account.Name);
            TempData["Success"] = $"Account '{account.Name}' updated successfully!";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update account: {Id}", id);
            ModelState.AddModelError("", $"Error updating account: {ex.Message}");
            return View(account);
        }
    }

    // POST: /Account/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest("Account ID is required");
        }

        try
        {
            await _dataService.DeleteAsync<Account>(id);

            _logger.LogInformation("Deleted Account: {Id}", id);
            TempData["Success"] = "Account deleted successfully!";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete account: {Id}", id);
            TempData["Error"] = $"Error deleting account: {ex.Message}";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
```

---

## Worker Service Quick Start

For background processing without user interaction:

### Program.cs

```csharp
using SalesforceCore.Extensions;
using SalesforceCore.Services.Core;

var builder = Host.CreateApplicationBuilder(args);

// Add Salesforce services
builder.Services.AddSalesforceCore(builder.Configuration);

// Use JWT authentication for server-to-server
builder.Services.AddScoped<ITokenProvider, JwtTokenProvider>();

// Add your worker
builder.Services.AddHostedService<SalesforceDataSyncWorker>();

var host = builder.Build();
host.Run();
```

### SalesforceDataSyncWorker.cs

```csharp
using SalesforceCore.Services.Data;

public class SalesforceDataSyncWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SalesforceDataSyncWorker> _logger;

    public SalesforceDataSyncWorker(
        IServiceProvider services,
        ILogger<SalesforceDataSyncWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dataService = scope.ServiceProvider.GetRequiredService<ITypedDataService>();

                // Query recent accounts
                var accounts = await dataService.Query<Account>()
                    .Where(a => a.LastModifiedDate > DateTime.UtcNow.AddHours(-1))
                    .ToListAsync();

                _logger.LogInformation("Synced {Count} accounts", accounts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## Testing Your Integration

### 1. Verify Authentication

```csharp
[Fact]
public async Task CanAuthenticate()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSalesforceCore(Configuration);

    var provider = services.BuildServiceProvider();
    var client = provider.GetRequiredService<ISalesforceClient>();

    // Act
    var limits = await client.GetLimitsAsync();

    // Assert
    Assert.NotNull(limits);
    Assert.True(limits.DailyApiRequests.Remaining > 0);
}
```

### 2. Verify Query

```csharp
[Fact]
public async Task CanQueryAccounts()
{
    // Arrange
    var dataService = _provider.GetRequiredService<ITypedDataService>();

    // Act
    var accounts = await dataService.Query<Account>()
        .Take(5)
        .ToListAsync();

    // Assert
    Assert.NotNull(accounts);
}
```

---

## Troubleshooting

### Common Issues

| Error | Cause | Solution |
|-------|-------|----------|
| `invalid_client` | Wrong Client ID or Secret | Verify credentials in Connected App |
| `invalid_grant` | Token expired or revoked | Re-authenticate, check refresh token |
| `INVALID_SESSION_ID` | Session expired | Implement token refresh |
| `REQUEST_LIMIT_EXCEEDED` | API limit hit | Wait for reset, implement retry |
| `UNABLE_TO_LOCK_ROW` | Record locked | Implement retry with backoff |

### Enable Debug Logging

```json
{
  "Logging": {
    "LogLevel": {
      "SalesforceCore": "Debug"
    }
  },
  "Salesforce": {
    "EnableDebugLogging": true
  }
}
```

---

## Next Steps

Now that you have a working Salesforce integration:

1. **[02-Authentication.md](02-Authentication.md)** - Deep dive into authentication flows
2. **[03-Configuration.md](03-Configuration.md)** - Complete configuration reference
3. **[04-Data-Service.md](04-Data-Service.md)** - Data operations
4. **[05-Typed-Data-Service.md](05-Typed-Data-Service.md)** - LINQ queries
5. **[07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md)** - High-volume operations
6. **[17-Dynamic-UI-System.md](17-Dynamic-UI-System.md)** - Dynamic UI generation

---

## Summary

In this guide, you learned:

- How to install and configure SalesforceCore packages
- How to set up a Salesforce Connected App
- How to configure authentication (PKCE, JWT, Client Credentials)
- How to create models mapped to Salesforce objects
- How to perform CRUD operations
- How to build a complete web application
- How to create a background worker service

You're now ready to build powerful Salesforce-integrated applications with .NET!

Session timestamp: 2025-12-25T23:00:00Z
