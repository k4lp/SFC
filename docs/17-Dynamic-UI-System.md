# Dynamic UI System - Complete Guide

The Dynamic UI system is a powerful, permission-aware UI generation engine that creates runtime UI configurations based on Salesforce metadata, field-level security, and centralized configuration. This comprehensive guide covers everything you need to know about implementing and customizing dynamic user interfaces.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Quick Start](#quick-start)
4. [Permission Service](#permission-service)
5. [Layout Descriptor Service](#layout-descriptor-service)
6. [REST API Endpoints](#rest-api-endpoints)
7. [Configuration System](#configuration-system)
8. [Navigation Configuration](#navigation-configuration)
9. [Object Configuration](#object-configuration)
10. [Form Configuration](#form-configuration)
11. [List Configuration](#list-configuration)
12. [Detail View Configuration](#detail-view-configuration)
13. [Field Configuration](#field-configuration)
14. [Actions Configuration](#actions-configuration)
15. [Theming Configuration](#theming-configuration)
16. [Caching Strategy](#caching-strategy)
17. [Security Integration](#security-integration)
18. [SPA Integration](#spa-integration)
19. [Server-Side Rendering](#server-side-rendering)
20. [Advanced Customization](#advanced-customization)
21. [Best Practices](#best-practices)
22. [Troubleshooting](#troubleshooting)
23. [API Reference](#api-reference)

---

## Implementation Notes

- **dynamic_ui.json loading**: The library now auto-loads `dynamic_ui.json` (path via `DynamicUi:ConfigFilePath`) and merges it over appsettings. If `WatchConfigFile` is enabled, changes are hot-reloaded without restarting.
- **User-scoped caching**: Permission and layout caches include the current user identity to avoid cross-user leakage. Use `DynamicUi:BypassCache` during debugging or call `/api/dynamic-ui/refresh` to flush.
- **Record types**: Form descriptors and cached layouts are keyed by `recordTypeId` so variants are isolated per record type selection. Picklist options come from Salesforce UI API when `recordTypeId` is provided, and dependent picklists are filtered using record-type-specific values.

## Current Behavior Addenda

- **Config merge semantics**: `dynamic_ui.json` values override appsettings for matching properties; partial JSON overlays do not remove existing in-memory values. Set fields explicitly in the file when overriding defaults.
- **Cache scope**: Layout cache keys include both user identity and record type (for forms). Permission caches are user-scoped; navigation/list/detail caches are user-scoped.
- **Record-type picklists**: When `recordTypeId` is supplied, picklist values are fetched from the Salesforce UI API and filtered accordingly; dependent picklists use the record-type-specific dependency map.
- **Hide/disable semantics**: `HideUnauthorizedActions=false` leaves unauthorized standard actions present but disabled; set to true to remove them from descriptors.

---

## Overview

The Dynamic UI system provides a complete solution for building permission-aware user interfaces that automatically adapt to user permissions and Salesforce metadata. Instead of hardcoding UI layouts, the system generates UI descriptors at runtime that respect:

- **Object-Level Security (OLS)** - CRUD permissions on Salesforce objects
- **Field-Level Security (FLS)** - Read/write access to individual fields
- **Record Types** - Different layouts for different record types
- **Picklist Dependencies** - Controlling/dependent field relationships
- **Relationship Metadata** - Parent/child relationships and lookups

### Key Benefits

1. **Security by Default**: UI elements automatically hide based on user permissions
2. **Metadata-Driven**: Changes in Salesforce reflect immediately in your UI
3. **Centralized Configuration**: Single source of truth for all UI layouts
4. **Hot Reload**: Configuration changes apply without restart
5. **SPA-Ready**: Full REST API for frontend frameworks
6. **Server-Side Ready**: Direct service injection for Razor views

### System Components

```
┌──────────────────────────────────────────────────────────────────┐
│                      Dynamic UI System                            │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────┐      ┌─────────────────────────────┐   │
│  │  Configuration      │      │    Salesforce Metadata       │   │
│  │  - dynamic_ui.json  │      │    - SObjectDescribe         │   │
│  │  - appsettings.json │      │    - Field Metadata          │   │
│  │  - Programmatic     │      │    - Record Types            │   │
│  └─────────┬───────────┘      └─────────────┬───────────────┘   │
│            │                                 │                    │
│            ▼                                 ▼                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │                   Permission Service                       │   │
│  │  - CRUD Permission Checks                                  │   │
│  │  - Field-Level Security Evaluation                         │   │
│  │  - Batch Permission Resolution                             │   │
│  │  - Permission Caching                                      │   │
│  └───────────────────────────┬───────────────────────────────┘   │
│                              │                                    │
│                              ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │              Layout Descriptor Service                     │   │
│  │  - Navigation Descriptors                                  │   │
│  │  - Form Descriptors                                        │   │
│  │  - List Descriptors                                        │   │
│  │  - Detail Descriptors                                      │   │
│  │  - Field Descriptors                                       │   │
│  │  - Action Descriptors                                      │   │
│  └───────────────────────────┬───────────────────────────────┘   │
│                              │                                    │
│                              ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │               REST API / Service Interface                 │   │
│  │  - /api/dynamic-ui/* endpoints                            │   │
│  │  - ILayoutDescriptorService injection                     │   │
│  │  - IPermissionService injection                           │   │
│  └───────────────────────────────────────────────────────────┘   │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Architecture

### Service Layer

The Dynamic UI system is built on two core services:

#### IPermissionService

Evaluates and caches permission information from Salesforce metadata:

```csharp
public interface IPermissionService
{
    // Get complete permission snapshot for an object
    Task<ObjectPermissionSnapshot> GetPermissionsAsync(
        string objectName,
        CancellationToken ct = default);

    // Batch permission request for multiple objects
    Task<PermissionResult> GetPermissionsAsync(
        PermissionRequestContext context,
        CancellationToken ct = default);

    // Check if user can perform a specific action
    Task<bool> CanPerformActionAsync(
        string objectName,
        PermissionAction action,
        CancellationToken ct = default);

    // Check field-level access
    Task<bool> CanAccessFieldAsync(
        string objectName,
        string fieldName,
        PermissionAction action,
        CancellationToken ct = default);

    // Get lists of accessible fields by permission type
    Task<IReadOnlyList<string>> GetReadableFieldsAsync(
        string objectName,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetCreateableFieldsAsync(
        string objectName,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetUpdateableFieldsAsync(
        string objectName,
        CancellationToken ct = default);

    // Get all allowed actions for an object
    Task<IReadOnlyList<PermissionAction>> GetAllowedActionsAsync(
        string objectName,
        CancellationToken ct = default);

    // Batch permission checks
    Task<IReadOnlyList<PermissionCheckResult>> CheckPermissionsAsync(
        IEnumerable<(string ObjectName, PermissionAction Action, string? FieldName)> checks,
        CancellationToken ct = default);
}
```

#### ILayoutDescriptorService

Generates UI descriptors by combining metadata, permissions, and configuration:

```csharp
public interface ILayoutDescriptorService
{
    // Navigation
    Task<NavigationDescriptor> GetNavigationAsync(
        string? currentPath = null,
        CancellationToken ct = default);

    // Forms
    Task<FormDescriptor> GetFormAsync(
        string objectName,
        FormMode mode,
        string? recordTypeId = null,
        CancellationToken ct = default);

    // Lists
    Task<ListDescriptor> GetListAsync(
        string objectName,
        CancellationToken ct = default);

    // Detail views
    Task<DetailDescriptor> GetDetailAsync(
        string objectName,
        CancellationToken ct = default);

    // Actions
    Task<IReadOnlyList<FormAction>> GetAvailableActionsAsync(
        string objectName,
        UiActionContext context,
        CancellationToken ct = default);

    // Picklists
    Task<IReadOnlyList<PicklistOption>> GetPicklistOptionsAsync(
        string objectName,
        string fieldName,
        string? controllingValue = null,
        string? recordTypeId = null,
        CancellationToken ct = default);

    // Record types
    Task<RecordTypeSelector?> GetRecordTypeSelectorAsync(
        string objectName,
        CancellationToken ct = default);

    // Related lists
    Task<IReadOnlyList<RelatedListDescriptor>> GetRelatedListsAsync(
        string objectName,
        CancellationToken ct = default);

    // Single field descriptor
    Task<FieldDescriptor?> GetFieldDescriptorAsync(
        string objectName,
        string fieldName,
        FormMode mode = FormMode.Edit,
        string? recordTypeId = null,
        CancellationToken ct = default);

    // Cache management
    Task RefreshAsync(
        string? objectName = null,
        CancellationToken ct = default);
}
```

### Data Flow

```
User Request → Controller/View
                    │
                    ▼
           LayoutDescriptorService
                    │
          ┌─────────┴─────────┐
          │                   │
          ▼                   ▼
   PermissionService    SchemaService
          │                   │
          ▼                   ▼
    Cache Check          Cache Check
          │                   │
          ▼                   ▼
   Salesforce API      Salesforce API
   (if cache miss)     (if cache miss)
          │                   │
          └─────────┬─────────┘
                    │
                    ▼
           Merge with Configuration
                    │
                    ▼
           Generate Descriptor
                    │
                    ▼
           Return to Client
```

---

## Quick Start

### 1. Install Packages

```bash
dotnet add package SalesforceCore
dotnet add package SalesforceCore.AspNetCore
```

### 2. Configure Services

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Salesforce Core with MVC integration
builder.Services.AddSalesforceCoreMvc(builder.Configuration);

// Add Salesforce Authentication
builder.Services.AddDistributedMemoryCache(); // dev; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

// Add Dynamic UI services with configuration
builder.Services.AddSalesforceDynamicUi(options =>
{
    options.DefaultFormColumns = 1;
    options.DefaultPageSize = 25;
    options.MaxPageSize = 100;
    options.HideInaccessibleNavItems = true;
    options.HideInaccessibleFields = true;
    options.HideUnauthorizedActions = true;
    options.PermissionCacheDuration = TimeSpan.FromMinutes(5);
    options.LayoutCacheDuration = TimeSpan.FromMinutes(10);

    // Configure navigation
    options.Navigation.AppName = "My CRM Application";
    options.Navigation.LogoUrl = "/images/logo.png";
});

var app = builder.Build();

// Configure middleware
app.UseAuthentication();
app.UseAuthorization();

// Enable Salesforce middleware and routes
app.UseSalesforceCore();
app.MapSalesforceRoutes();
app.MapDynamicUiRoutes();  // Maps /api/dynamic-ui/* endpoints

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

### 3. Add Configuration

**appsettings.json:**

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0"
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
  }
}
```

Note: `ClientSecret` is only required for Client Credentials flow. PKCE does not use it.

### 4. Create dynamic_ui.json

```json
{
  "Navigation": {
    "AppName": "Sales Cloud",
    "LogoUrl": "/images/logo.svg",
    "Items": [
      {
        "Id": "home",
        "Label": "Home",
        "Icon": "bi-house",
        "Route": "/",
        "Order": 0
      },
      {
        "Id": "accounts",
        "Label": "Accounts",
        "Icon": "bi-building",
        "Route": "/Salesforce/Account",
        "SObject": "Account",
        "RequiredPermission": "Read",
        "Order": 1
      },
      {
        "Id": "contacts",
        "Label": "Contacts",
        "Icon": "bi-people",
        "Route": "/Salesforce/Contact",
        "SObject": "Contact",
        "RequiredPermission": "Read",
        "Order": 2
      }
    ]
  },
  "Objects": {
    "Account": {
      "DisplayLabel": "Customer Account",
      "EnableCreate": true,
      "EnableEdit": true,
      "EnableDelete": true,
      "ExcludeFields": ["Jigsaw", "CleanStatus", "DandbCompanyId"],
      "List": {
        "Columns": [
          { "FieldName": "Name", "IsLink": true },
          { "FieldName": "Industry" },
          { "FieldName": "Phone" }
        ]
      },
      "Form": {
        "Sections": [
          {
            "Id": "basic",
            "Heading": "Account Information",
            "Fields": ["Name", "Industry", "Phone", "Website"]
          }
        ]
      }
    }
  }
}
```

### 5. Use in Controller

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Layout;
using SalesforceCore.Models.Layout;

[Authorize]
public class AccountController : Controller
{
    private readonly ILayoutDescriptorService _layoutService;
    private readonly IPermissionService _permissionService;

    public AccountController(
        ILayoutDescriptorService layoutService,
        IPermissionService permissionService)
    {
        _layoutService = layoutService;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index()
    {
        // Get list descriptor with permission-filtered columns
        var listDescriptor = await _layoutService.GetListAsync("Account");

        // Get permissions for UI controls
        var permissions = await _permissionService.GetPermissionsAsync("Account");

        ViewBag.CanCreate = permissions.CanCreate;
        ViewBag.ListDescriptor = listDescriptor;

        return View();
    }

    public async Task<IActionResult> Create()
    {
        // Check create permission
        if (!await _permissionService.CanPerformActionAsync("Account", PermissionAction.Create))
        {
            return Forbid();
        }

        // Get form descriptor for create mode
        var formDescriptor = await _layoutService.GetFormAsync("Account", FormMode.Create);

        return View(formDescriptor);
    }

    public async Task<IActionResult> Edit(string id)
    {
        // Check update permission
        if (!await _permissionService.CanPerformActionAsync("Account", PermissionAction.Update))
        {
            return Forbid();
        }

        // Get form descriptor for edit mode
        var formDescriptor = await _layoutService.GetFormAsync("Account", FormMode.Edit);

        ViewBag.RecordId = id;
        return View(formDescriptor);
    }

    public async Task<IActionResult> Details(string id)
    {
        // Get detail descriptor with related lists
        var detailDescriptor = await _layoutService.GetDetailAsync("Account");

        // Get permissions for action buttons
        var permissions = await _permissionService.GetPermissionsAsync("Account");

        ViewBag.RecordId = id;
        ViewBag.CanEdit = permissions.CanUpdate;
        ViewBag.CanDelete = permissions.CanDelete;
        ViewBag.DetailDescriptor = detailDescriptor;

        return View();
    }
}
```

---

## Permission Service

The Permission Service aggregates permission information from Salesforce metadata and provides efficient caching.

### ObjectPermissionSnapshot

The complete permission state for a Salesforce object:

```csharp
public class ObjectPermissionSnapshot
{
    public string ObjectName { get; set; }
    public string ObjectLabel { get; set; }
    public bool CanCreate { get; set; }
    public bool CanRead { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool IsQueryable { get; set; }
    public bool IsSearchable { get; set; }
    public Dictionary<string, FieldPermission> FieldPermissions { get; set; }
    public List<RecordTypeInfo> AvailableRecordTypes { get; set; }
    public DateTime CachedAt { get; set; }
    public TimeSpan CacheDuration { get; set; }
}

public class FieldPermission
{
    public string FieldName { get; set; }
    public string Label { get; set; }
    public string FieldLabel { get; set; } // alias for Label
    public bool CanRead { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool IsRequired { get; set; }
    public string FieldType { get; set; }
    public int? Length { get; set; }
}
```

### Usage Examples

#### Single Object Permission Check

```csharp
public class MyController : Controller
{
    private readonly IPermissionService _permissions;

    public async Task<IActionResult> Index()
    {
        // Get complete permission snapshot
        var snapshot = await _permissions.GetPermissionsAsync("Account");

        // Use in view
        ViewBag.CanCreate = snapshot.CanCreate;
        ViewBag.CanEdit = snapshot.CanUpdate;
        ViewBag.CanDelete = snapshot.CanDelete;

        // Check field permissions
        if (snapshot.FieldPermissions.TryGetValue("AnnualRevenue", out var fieldPerm))
        {
            ViewBag.CanSeeRevenue = fieldPerm.CanRead;
            ViewBag.CanEditRevenue = fieldPerm.CanUpdate;
        }

        return View();
    }
}
```

#### Batch Permission Checks

```csharp
// Check permissions for multiple objects at once
var context = PermissionRequestContext.ForObjects("Account", "Contact", "Lead", "Opportunity");
var result = await _permissions.GetPermissionsAsync(context);

foreach (var (objectName, snapshot) in result.Snapshots)
{
    Console.WriteLine($"{objectName}: Create={snapshot.CanCreate}, Read={snapshot.CanRead}");
}
```

#### Field-Level Security Checks

```csharp
// Check if user can read a specific field
var canReadRevenue = await _permissions.CanAccessFieldAsync(
    "Account",
    "AnnualRevenue",
    PermissionAction.Read);

// Get all readable fields
var readableFields = await _permissions.GetReadableFieldsAsync("Account");

// Get all fields user can create
var createableFields = await _permissions.GetCreateableFieldsAsync("Account");

// Get all fields user can update
var updateableFields = await _permissions.GetUpdateableFieldsAsync("Account");
```

#### Batch Field Permission Checks

```csharp
var checks = new[]
{
    ("Account", PermissionAction.Create, (string?)null),
    ("Account", PermissionAction.Read, "Name"),
    ("Account", PermissionAction.Update, "Industry"),
    ("Contact", PermissionAction.Delete, (string?)null)
};

var results = await _permissions.CheckPermissionsAsync(checks);

foreach (var result in results)
{
    Console.WriteLine($"{result.ObjectName}.{result.FieldName ?? "OBJECT"} " +
                      $"{result.Action}: {result.IsAllowed}");
}
```

---

## Layout Descriptor Service

The Layout Descriptor Service generates UI descriptors by combining Salesforce metadata, user permissions, and configuration.

### Navigation Descriptor

```csharp
public class NavigationDescriptor
{
    public string AppName { get; set; }
    public string LogoUrl { get; set; }
    public string UserDisplayName { get; set; }
    public List<NavigationItem> MainItems { get; set; }
    public List<NavigationItem> UtilityItems { get; set; }
}

public class NavigationItem
{
    public string Id { get; set; }
    public string Label { get; set; }
    public string Icon { get; set; }
    public string Route { get; set; }
    public string SObject { get; set; }
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsVisible { get; set; }
    public List<NavigationItem> Children { get; set; }
}
```

#### Usage

```csharp
// Get navigation with current path highlighting
var navigation = await _layoutService.GetNavigationAsync("/Salesforce/Account");

// Render in view
@foreach (var item in navigation.MainItems.Where(i => i.IsVisible))
{
    <li class="nav-item @(item.IsActive ? "active" : "")">
        <a href="@item.Route" class="nav-link @(!item.IsEnabled ? "disabled" : "")">
            <i class="@item.Icon"></i>
            @item.Label
        </a>
        @if (item.Children?.Any() == true)
        {
            <ul class="dropdown-menu">
                @foreach (var child in item.Children.Where(c => c.IsVisible))
                {
                    <li><a href="@child.Route">@child.Label</a></li>
                }
            </ul>
        }
    </li>
}
```

### Form Descriptor

```csharp
public class FormDescriptor
{
    public string ObjectName { get; set; }
    public string ObjectLabel { get; set; }
    public FormMode Mode { get; set; }  // Create, Edit, View
    public string Title { get; set; }
    public int Columns { get; set; }
    public bool ShowValidationSummary { get; set; }
    public List<FormSection> Sections { get; set; }
    public List<FieldDescriptor> Fields { get; set; }
    public List<FormAction> Actions { get; set; }
    public RecordTypeSelector RecordTypeSelector { get; set; }
    public bool IsReadOnly => Mode == FormMode.View;
}

public class FormSection
{
    public string Id { get; set; }
    public string Heading { get; set; }
    public string Description { get; set; }
    public int Columns { get; set; }
    public bool IsCollapsible { get; set; }
    public bool IsCollapsed { get; set; }
    public int Order { get; set; }
    public List<string> FieldNames { get; set; }
}
```

#### Usage

```csharp
// Get form for creating a new record
var createForm = await _layoutService.GetFormAsync("Account", FormMode.Create);

// Get form for editing with specific record type
var editForm = await _layoutService.GetFormAsync(
    "Account",
    FormMode.Edit,
    recordTypeId: "012000000000001AAA");

// Get read-only form for viewing
var viewForm = await _layoutService.GetFormAsync("Account", FormMode.View);
```

### List Descriptor

```csharp
public class ListDescriptor
{
    public string ObjectName { get; set; }
    public string ObjectLabel { get; set; }
    public string ObjectLabelPlural { get; set; }
    public List<ColumnDescriptor> Columns { get; set; }
    public List<FormAction> RowActions { get; set; }
    public List<FormAction> BulkActions { get; set; }
    public List<FormAction> HeaderActions { get; set; }
    public string DefaultSortField { get; set; }
    public SortDirection DefaultSortDirection { get; set; }
    public int PageSize { get; set; }
    public bool EnableSearch { get; set; }
    public bool EnableFilters { get; set; }
    public bool EnableSelection { get; set; }
    public bool EnableExport { get; set; }
}

public class ColumnDescriptor
{
    public string FieldName { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
    public bool IsVisible { get; set; }
    public bool IsSortable { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsLink { get; set; }
    public string Format { get; set; }
    public int Width { get; set; }
    public int Order { get; set; }
}
```

### Detail Descriptor

```csharp
public class DetailDescriptor
{
    public string ObjectName { get; set; }
    public string ObjectLabel { get; set; }
    public int Columns { get; set; }
    public List<FormSection> Sections { get; set; }
    public List<FieldDescriptor> Fields { get; set; }
    public List<FormAction> Actions { get; set; }
    public List<RelatedListDescriptor> RelatedLists { get; set; }
}

public class RelatedListDescriptor
{
    public string RelationshipName { get; set; }
    public string ChildObject { get; set; }
    public string ChildObjectLabel { get; set; }
    public string Title { get; set; }
    public string ParentField { get; set; }
    public List<ColumnDescriptor> Columns { get; set; }
    public List<FormAction> RowActions { get; set; }
    public int MaxRecords { get; set; }
    public bool CanCreate { get; set; }
}
```

### Field Descriptor

```csharp
public class FieldDescriptor
{
    public string Name { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }           // string, picklist, reference, etc.
    public string InputType { get; set; }       // text, email, number, tel, url, etc.
    public string ControlType { get; set; }     // input, select, lookup, textarea, checkbox
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsVisible { get; set; }
    public bool IsUnique { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public string Placeholder { get; set; }
    public string HelpText { get; set; }
    public object DefaultValue { get; set; }
    public string ValidationPattern { get; set; }
    public string ValidationMessage { get; set; }
    public int ColumnSpan { get; set; }
    public int Order { get; set; }

    // Picklist fields
    public List<PicklistOption> PicklistOptions { get; set; }
    public string ControllingField { get; set; }
    public bool IsRestrictedPicklist { get; set; }

    // Reference/Lookup fields
    public LookupConfig LookupConfig { get; set; }
}

public class PicklistOption
{
    public string Value { get; set; }
    public string Label { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string ControllingValue { get; set; }
}

public class LookupConfig
{
    public string ReferenceTo { get; set; }
    public string ReferenceToLabel { get; set; }
    public string DisplayField { get; set; }
    public string SearchFields { get; set; }
    public int MinSearchChars { get; set; }
    public int MaxResults { get; set; }
    public string Filter { get; set; }
}
```

---

## REST API Endpoints

The `DynamicUiController` provides a complete REST API at `/api/dynamic-ui/`:

### Endpoint Reference

| Endpoint | Method | Description | Parameters |
|----------|--------|-------------|------------|
| `/navigation` | GET | Get navigation descriptor | `currentPath` (optional) |
| `/forms/{sObject}` | GET | Get form descriptor | `mode` (Create/Edit/View), `recordTypeId` |
| `/lists/{sObject}` | GET | Get list descriptor | - |
| `/details/{sObject}` | GET | Get detail descriptor | - |
| `/permissions/{sObject}` | GET | Get permission snapshot | - |
| `/permissions` | GET | Batch permission check | `objects` (comma-separated) |
| `/actions/{sObject}` | GET | Get available actions | `context` (List/Detail/Form) |
| `/picklist/{sObject}/{field}` | GET | Get picklist options | `controllingValue`, `recordTypeId` |
| `/record-types/{sObject}` | GET | Get record type selector | - |
| `/fields/{sObject}/{field}` | GET | Get single field descriptor | `mode`, `recordTypeId` |
| `/related-lists/{sObject}` | GET | Get related list descriptors | - |
| `/refresh` | POST | Invalidate cache | `sObject` (optional) |

### API Examples

#### JavaScript/Fetch

```javascript
// Get navigation
const nav = await fetch('/api/dynamic-ui/navigation?currentPath=/Salesforce/Account')
    .then(r => r.json());

// Get form descriptor
const form = await fetch('/api/dynamic-ui/forms/Account?mode=Edit&recordTypeId=012xxx')
    .then(r => r.json());

// Get list descriptor
const list = await fetch('/api/dynamic-ui/lists/Account')
    .then(r => r.json());

// Get detail descriptor
const detail = await fetch('/api/dynamic-ui/details/Account')
    .then(r => r.json());

// Check permissions for multiple objects
const perms = await fetch('/api/dynamic-ui/permissions?objects=Account,Contact,Lead')
    .then(r => r.json());

// Get dependent picklist values
const states = await fetch('/api/dynamic-ui/picklist/Account/BillingState?controllingValue=USA')
    .then(r => r.json());

// Refresh cache for specific object
await fetch('/api/dynamic-ui/refresh?sObject=Account', { method: 'POST' });
```

#### React Example

```jsx
import { useState, useEffect } from 'react';

function DynamicForm({ objectName, mode, recordTypeId }) {
    const [formDescriptor, setFormDescriptor] = useState(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const fetchForm = async () => {
            const url = `/api/dynamic-ui/forms/${objectName}?mode=${mode}` +
                        (recordTypeId ? `&recordTypeId=${recordTypeId}` : '');
            const response = await fetch(url);
            const data = await response.json();
            setFormDescriptor(data);
            setLoading(false);
        };
        fetchForm();
    }, [objectName, mode, recordTypeId]);

    if (loading) return <div>Loading...</div>;

    return (
        <form>
            <h2>{formDescriptor.title}</h2>
            {formDescriptor.sections.map(section => (
                <fieldset key={section.id}>
                    <legend>{section.heading}</legend>
                    <div className={`grid grid-cols-${section.columns}`}>
                        {section.fieldNames.map(fieldName => {
                            const field = formDescriptor.fields.find(f => f.name === fieldName);
                            if (!field?.isVisible) return null;
                            return <DynamicField key={field.name} field={field} />;
                        })}
                    </div>
                </fieldset>
            ))}
            <div className="actions">
                {formDescriptor.actions
                    .filter(a => a.isEnabled)
                    .map(action => (
                        <button key={action.id} type={action.type === 'submit' ? 'submit' : 'button'}>
                            {action.label}
                        </button>
                    ))}
            </div>
        </form>
    );
}

function DynamicField({ field }) {
    switch (field.controlType) {
        case 'select':
            return (
                <div className="form-group">
                    <label>{field.label}{field.isRequired && '*'}</label>
                    <select name={field.name} required={field.isRequired} disabled={field.isReadOnly}>
                        <option value="">-- Select --</option>
                        {field.picklistOptions.map(opt => (
                            <option key={opt.value} value={opt.value}>{opt.label}</option>
                        ))}
                    </select>
                </div>
            );
        case 'lookup':
            return <LookupField field={field} />;
        case 'textarea':
            return (
                <div className="form-group">
                    <label>{field.label}{field.isRequired && '*'}</label>
                    <textarea
                        name={field.name}
                        required={field.isRequired}
                        readOnly={field.isReadOnly}
                        maxLength={field.maxLength}
                        placeholder={field.placeholder}
                    />
                </div>
            );
        default:
            return (
                <div className="form-group">
                    <label>{field.label}{field.isRequired && '*'}</label>
                    <input
                        type={field.inputType}
                        name={field.name}
                        required={field.isRequired}
                        readOnly={field.isReadOnly}
                        maxLength={field.maxLength}
                        placeholder={field.placeholder}
                    />
                </div>
            );
    }
}
```

---

## Configuration System

### Configuration Hierarchy

Configuration is loaded from multiple sources in order of priority:

1. **Programmatic configuration** (highest priority)
2. **dynamic_ui.json** file
3. **appsettings.json** DynamicUi section
4. **Default values** (lowest priority)

### Complete Configuration Reference

```json
{
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
  "MaxPageSize": 100,

  "Navigation": {
    "AppName": "My CRM",
    "LogoUrl": "/images/logo.png",
    "AutoGenerateFromObjects": false,
    "DefaultObjects": ["Account", "Contact", "Lead", "Opportunity"],
    "ExcludedObjects": ["User", "Profile", "PermissionSet"],
    "Items": [],
    "UtilityItems": []
  },

  "Objects": {},

  "Theming": {
    "UseDefaultCss": true,
    "UseDefaultJs": true,
    "CssFramework": "Bootstrap5",
    "CssFiles": [],
    "JsFiles": [],
    "ColorScheme": {}
  },

  "FeatureFlags": {
    "EnableExport": true,
    "EnableBulkActions": false,
    "EnableInlineEdit": false,
    "EnableAdvancedSearch": true,
    "EnableDashboards": false,
    "EnableReports": true
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConfigFilePath` | string | `dynamic_ui.json` | Path to external configuration file |
| `WatchConfigFile` | bool | `true` | Hot-reload configuration changes |
| `PermissionCacheDuration` | TimeSpan | `00:05:00` | Permission cache TTL |
| `LayoutCacheDuration` | TimeSpan | `00:10:00` | Layout descriptor cache TTL |
| `BypassCache` | bool | `false` | Skip all caching (for debugging) |
| `HideInaccessibleNavItems` | bool | `true` | Hide navigation items user can't access |
| `HideInaccessibleFields` | bool | `true` | Hide fields user can't read |
| `HideUnauthorizedActions` | bool | `true` | Hide actions user can't perform |
| `DefaultFormColumns` | int | `1` | Default number of columns in forms |
| `DefaultPageSize` | int | `25` | Default records per page |
| `MaxPageSize` | int | `100` | Maximum records per page |

---

## Navigation Configuration

### Complete Navigation Structure

```json
{
  "Navigation": {
    "AppName": "Sales Cloud CRM",
    "LogoUrl": "/images/logo.svg",
    "AutoGenerateFromObjects": false,
    "DefaultObjects": ["Account", "Contact", "Lead", "Opportunity", "Case"],
    "ExcludedObjects": ["User", "Profile", "PermissionSet", "RecordType"],
    "Items": [
      {
        "Id": "home",
        "Label": "Home",
        "Icon": "bi-house-fill",
        "Route": "/",
        "Order": 0
      },
      {
        "Id": "sales",
        "Label": "Sales",
        "Icon": "bi-graph-up",
        "Order": 1,
        "Children": [
          {
            "Id": "accounts",
            "Label": "Accounts",
            "Icon": "bi-building",
            "Route": "/Salesforce/Account",
            "SObject": "Account",
            "RequiredPermission": "Read"
          },
          {
            "Id": "contacts",
            "Label": "Contacts",
            "Icon": "bi-people",
            "Route": "/Salesforce/Contact",
            "SObject": "Contact",
            "RequiredPermission": "Read"
          },
          {
            "Id": "opportunities",
            "Label": "Opportunities",
            "Icon": "bi-trophy",
            "Route": "/Salesforce/Opportunity",
            "SObject": "Opportunity",
            "RequiredPermission": "Read"
          }
        ]
      },
      {
        "Id": "marketing",
        "Label": "Marketing",
        "Icon": "bi-megaphone",
        "Order": 2,
        "Children": [
          {
            "Id": "leads",
            "Label": "Leads",
            "Icon": "bi-person-plus",
            "Route": "/Salesforce/Lead",
            "SObject": "Lead",
            "RequiredPermission": "Read"
          },
          {
            "Id": "campaigns",
            "Label": "Campaigns",
            "Icon": "bi-bullseye",
            "Route": "/Salesforce/Campaign",
            "SObject": "Campaign",
            "RequiredPermission": "Read"
          }
        ]
      },
      {
        "Id": "service",
        "Label": "Service",
        "Icon": "bi-headset",
        "Order": 3,
        "Children": [
          {
            "Id": "cases",
            "Label": "Cases",
            "Icon": "bi-inbox",
            "Route": "/Salesforce/Case",
            "SObject": "Case",
            "RequiredPermission": "Read"
          }
        ]
      },
      {
        "Id": "reports",
        "Label": "Reports",
        "Icon": "bi-file-earmark-bar-graph",
        "Route": "/reports",
        "Order": 4
      }
    ],
    "UtilityItems": [
      {
        "Id": "search",
        "Label": "Global Search",
        "Icon": "bi-search",
        "Route": "/search"
      },
      {
        "Id": "notifications",
        "Label": "Notifications",
        "Icon": "bi-bell",
        "Route": "/notifications"
      },
      {
        "Id": "settings",
        "Label": "Settings",
        "Icon": "bi-gear",
        "Route": "/settings"
      },
      {
        "Id": "logout",
        "Label": "Logout",
        "Icon": "bi-box-arrow-right",
        "Route": "/salesforce/signout"
      }
    ]
  }
}
```

### Navigation Item Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Id` | string | Yes | Unique identifier |
| `Label` | string | Yes | Display text |
| `Icon` | string | No | Icon class (Bootstrap Icons, Font Awesome, etc.) |
| `Route` | string | No | Navigation URL |
| `SObject` | string | No | Associated Salesforce object for permission checks |
| `RequiredPermission` | string | No | Permission required (Read, Create, Update, Delete) |
| `Order` | int | No | Sort order (lower = first) |
| `Children` | array | No | Nested navigation items |

---

## Object Configuration

### Complete Object Configuration

```json
{
  "Objects": {
    "Account": {
      "DisplayLabel": "Customer Account",
      "DisplayLabelPlural": "Customer Accounts",
      "EnableCreate": true,
      "EnableEdit": true,
      "EnableDelete": true,
      "EnableClone": false,
      "IncludeFields": null,
      "ExcludeFields": ["Jigsaw", "CleanStatus", "DandbCompanyId", "NaicsCode", "NaicsDesc"],

      "List": {
        "EnableSearch": true,
        "EnableFilters": true,
        "EnableSelection": true,
        "EnableExport": true,
        "EnableInlineEdit": false,
        "PageSize": 25,
        "DefaultSortField": "Name",
        "DefaultSortDirection": "asc",
        "SearchFields": ["Name", "Phone", "Website"],
        "Columns": [
          {
            "FieldName": "Name",
            "Label": "Account Name",
            "IsLink": true,
            "IsSortable": true,
            "IsFilterable": true,
            "Width": 250,
            "Order": 0
          },
          {
            "FieldName": "Industry",
            "IsFilterable": true,
            "Order": 1
          },
          {
            "FieldName": "Type",
            "IsFilterable": true,
            "Order": 2
          },
          {
            "FieldName": "AnnualRevenue",
            "Format": "currency",
            "IsSortable": true,
            "Order": 3
          },
          {
            "FieldName": "Phone",
            "Order": 4
          },
          {
            "FieldName": "LastModifiedDate",
            "Label": "Last Modified",
            "Format": "datetime",
            "IsSortable": true,
            "Order": 5
          }
        ],
        "RowActions": [
          {
            "Id": "view",
            "Label": "View",
            "Icon": "bi-eye",
            "Type": "view",
            "RequiredPermission": "Read"
          },
          {
            "Id": "edit",
            "Label": "Edit",
            "Icon": "bi-pencil",
            "Type": "edit",
            "RequiredPermission": "Update"
          },
          {
            "Id": "delete",
            "Label": "Delete",
            "Icon": "bi-trash",
            "Type": "delete",
            "RequiredPermission": "Delete",
            "ConfirmMessage": "Are you sure you want to delete this account?"
          }
        ],
        "BulkActions": [
          {
            "Id": "bulk-delete",
            "Label": "Delete Selected",
            "Icon": "bi-trash",
            "Type": "bulk-delete",
            "RequiredPermission": "Delete",
            "ConfirmMessage": "Delete {count} selected accounts?"
          },
          {
            "Id": "bulk-export",
            "Label": "Export Selected",
            "Icon": "bi-download",
            "Type": "export"
          }
        ],
        "HeaderActions": [
          {
            "Id": "new",
            "Label": "New Account",
            "Icon": "bi-plus-lg",
            "Type": "create",
            "RequiredPermission": "Create",
            "IsPrimary": true
          },
          {
            "Id": "import",
            "Label": "Import",
            "Icon": "bi-upload",
            "Type": "import",
            "RequiredPermission": "Create"
          }
        ]
      },

      "Form": {
        "Columns": 2,
        "ShowValidationSummary": true,
        "ShowRequiredIndicator": true,
        "Sections": [
          {
            "Id": "basic",
            "Heading": "Account Information",
            "Description": "Basic account details",
            "Fields": ["Name", "Type", "Industry", "AnnualRevenue", "NumberOfEmployees"],
            "Columns": 2,
            "IsCollapsible": false,
            "Order": 0
          },
          {
            "Id": "contact",
            "Heading": "Contact Information",
            "Fields": ["Phone", "Fax", "Website"],
            "Columns": 2,
            "IsCollapsible": false,
            "Order": 1
          },
          {
            "Id": "billing",
            "Heading": "Billing Address",
            "Fields": ["BillingStreet", "BillingCity", "BillingState", "BillingPostalCode", "BillingCountry"],
            "Columns": 2,
            "IsCollapsible": true,
            "IsCollapsed": false,
            "Order": 2
          },
          {
            "Id": "shipping",
            "Heading": "Shipping Address",
            "Fields": ["ShippingStreet", "ShippingCity", "ShippingState", "ShippingPostalCode", "ShippingCountry"],
            "Columns": 2,
            "IsCollapsible": true,
            "IsCollapsed": true,
            "Order": 3
          },
          {
            "Id": "description",
            "Heading": "Additional Information",
            "Fields": ["Description"],
            "Columns": 1,
            "IsCollapsible": true,
            "IsCollapsed": true,
            "Order": 4
          }
        ],
        "Fields": [
          {
            "FieldName": "Name",
            "IsRequired": true,
            "HelpText": "The official name of the company"
          },
          {
            "FieldName": "Description",
            "ControlType": "textarea",
            "ColumnSpan": 2,
            "Rows": 4
          },
          {
            "FieldName": "Website",
            "Placeholder": "https://example.com",
            "HelpText": "Company website URL"
          },
          {
            "FieldName": "AnnualRevenue",
            "HelpText": "Estimated annual revenue in USD"
          }
        ]
      },

      "Detail": {
        "Columns": 2,
        "Sections": [
          {
            "Id": "basic",
            "Heading": "Account Information",
            "Fields": ["Name", "Type", "Industry", "AnnualRevenue", "NumberOfEmployees", "Phone", "Website"],
            "Columns": 2,
            "Order": 0
          },
          {
            "Id": "address",
            "Heading": "Address Information",
            "Fields": ["BillingStreet", "BillingCity", "BillingState", "BillingPostalCode", "BillingCountry"],
            "Columns": 2,
            "Order": 1
          },
          {
            "Id": "system",
            "Heading": "System Information",
            "Fields": ["CreatedDate", "CreatedById", "LastModifiedDate", "LastModifiedById"],
            "Columns": 2,
            "IsCollapsible": true,
            "Order": 2
          }
        ],
        "RelatedLists": [
          {
            "RelationshipName": "Contacts",
            "Title": "Related Contacts",
            "MaxRecords": 10,
            "Columns": [
              { "FieldName": "Name", "IsLink": true },
              { "FieldName": "Title" },
              { "FieldName": "Email" },
              { "FieldName": "Phone" }
            ],
            "RowActions": [
              { "Id": "edit", "Label": "Edit", "Type": "edit" }
            ]
          },
          {
            "RelationshipName": "Opportunities",
            "Title": "Related Opportunities",
            "MaxRecords": 5,
            "Columns": [
              { "FieldName": "Name", "IsLink": true },
              { "FieldName": "Amount", "Format": "currency" },
              { "FieldName": "StageName" },
              { "FieldName": "CloseDate", "Format": "date" }
            ]
          },
          {
            "RelationshipName": "Cases",
            "Title": "Related Cases",
            "MaxRecords": 5,
            "Columns": [
              { "FieldName": "CaseNumber", "IsLink": true },
              { "FieldName": "Subject" },
              { "FieldName": "Status" },
              { "FieldName": "Priority" }
            ]
          }
        ]
      },

      "CustomActions": [
        {
          "Id": "send-email",
          "Label": "Send Email",
          "Icon": "bi-envelope",
          "Type": "custom",
          "Url": "/Salesforce/Account/{Id}/email",
          "RequiredPermission": "Read",
          "Contexts": ["Detail"],
          "Order": 10
        },
        {
          "Id": "merge",
          "Label": "Merge Accounts",
          "Icon": "bi-layers",
          "Type": "custom",
          "Url": "/Salesforce/Account/merge?ids={SelectedIds}",
          "RequiredPermission": "Delete",
          "Contexts": ["List"],
          "MinSelection": 2,
          "MaxSelection": 3,
          "Order": 20
        }
      ]
    }
  }
}
```

---

## Caching Strategy

### Cache Levels

| Cache | Default TTL | Configurable | Purpose |
|-------|------------|--------------|---------|
| Permission snapshots | 5 minutes | Yes | Object and field permissions |
| Layout descriptors | 10 minutes | Yes | Forms, lists, details, navigation |
| Schema metadata | 1 hour | Via core options | Object structure from Salesforce |
| Picklist values | 15 minutes | Via core options | Picklist option values |

### Cache Configuration

```json
{
  "DynamicUi": {
    "PermissionCacheDuration": "00:05:00",
    "LayoutCacheDuration": "00:10:00",
    "BypassCache": false
  },
  "Salesforce": {
    "SchemaCacheDuration": "01:00:00",
    "LookupCacheDuration": "00:15:00"
  }
}
```

### Cache Invalidation

```csharp
// Programmatically refresh cache
await _layoutService.RefreshAsync("Account");  // Single object
await _layoutService.RefreshAsync();           // All objects

// Via API
POST /api/dynamic-ui/refresh?sObject=Account
POST /api/dynamic-ui/refresh  // All
```

### Distributed Caching

For web farms, configure Redis:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "SalesforceCore_";
});
```

---

## Security Integration

### Permission Enforcement Flow

```
User Request
     │
     ▼
┌────────────────────┐
│  Check Object      │
│  Permissions       │
│  (OLS)            │
└─────────┬──────────┘
          │
     Can Access?
     ┌────┴────┐
     │ No      │ Yes
     ▼         ▼
  Return   ┌────────────────────┐
  403      │  Filter Fields by  │
           │  FLS Permissions   │
           └─────────┬──────────┘
                     │
                     ▼
           ┌────────────────────┐
           │  Apply Record Type │
           │  Restrictions      │
           └─────────┬──────────┘
                     │
                     ▼
           ┌────────────────────┐
           │  Generate          │
           │  Descriptor        │
           └─────────┬──────────┘
                     │
                     ▼
              Return to Client
```

### Security Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `HideInaccessibleNavItems` | `true` | Hide navigation items user can't access |
| `HideInaccessibleFields` | `true` | Hide fields user can't read |
| `HideUnauthorizedActions` | `true` | Hide actions user can't perform |

### Permission Enforcement Example

```csharp
// In controller - check before action
[Authorize]
public async Task<IActionResult> Delete(string id)
{
    // Check delete permission
    var canDelete = await _permissions.CanPerformActionAsync("Account", PermissionAction.Delete);
    if (!canDelete)
    {
        return Forbid();
    }

    // Proceed with delete
    await _dataService.DeleteAsync<Account>(id);
    return RedirectToAction("Index");
}

// In view - conditional rendering
@{
    var permissions = await PermissionService.GetPermissionsAsync("Account");
}

@if (permissions.CanCreate)
{
    <a href="/Salesforce/Account/Create" class="btn btn-primary">New Account</a>
}

@if (permissions.CanUpdate)
{
    <a href="/Salesforce/Account/Edit/@Model.Id" class="btn btn-secondary">Edit</a>
}

@if (permissions.CanDelete)
{
    <form asp-action="Delete" asp-route-id="@Model.Id" method="post">
        <button type="submit" class="btn btn-danger">Delete</button>
    </form>
}
```

---

## Best Practices

### 1. Use Configuration Files

Define UI layouts in `dynamic_ui.json` rather than hardcoding in views:

```json
// Good - centralized configuration
{
  "Objects": {
    "Account": {
      "Form": {
        "Sections": [...]
      }
    }
  }
}
```

### 2. Leverage Batch Operations

Fetch multiple permissions in one call:

```csharp
// Good - single request
var context = PermissionRequestContext.ForObjects("Account", "Contact", "Lead");
var result = await _permissions.GetPermissionsAsync(context);

// Avoid - multiple requests
var accountPerms = await _permissions.GetPermissionsAsync("Account");
var contactPerms = await _permissions.GetPermissionsAsync("Contact");
var leadPerms = await _permissions.GetPermissionsAsync("Lead");
```

### 3. Always Check Visibility Flags

```csharp
// Always check before rendering
foreach (var field in formDescriptor.Fields.Where(f => f.IsVisible))
{
    // Render field
}

foreach (var action in listDescriptor.RowActions.Where(a => a.IsEnabled))
{
    // Render action button
}
```

### 4. Handle Permission Denial Gracefully

```csharp
public async Task<IActionResult> Edit(string id)
{
    if (!await _permissions.CanPerformActionAsync("Account", PermissionAction.Update))
    {
        TempData["Error"] = "You don't have permission to edit accounts.";
        return RedirectToAction("Details", new { id });
    }
    // Continue with edit
}
```

### 5. Preload Permissions at Startup

```csharp
// In a hosted service
public class PermissionPreloader : IHostedService
{
    private readonly IServiceProvider _services;

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        // Preload common objects
        var objects = new[] { "Account", "Contact", "Lead", "Opportunity", "Case" };
        var context = PermissionRequestContext.ForObjects(objects);
        await permissions.GetPermissionsAsync(context, ct);
    }
}
```

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Fields not showing | FLS restriction | Check field permissions in Salesforce |
| Navigation item disabled | Missing object permission | Verify user has Read access |
| Actions hidden | Insufficient CRUD permission | Check object-level permissions |
| Stale data | Cache not invalidated | Call RefreshAsync() or POST /refresh |
| Form sections empty | Fields excluded by config | Check ExcludeFields in configuration |

### Debug Mode

Enable debug mode to bypass caching:

```json
{
  "DynamicUi": {
    "BypassCache": true
  }
}
```

### Logging

Enable verbose logging:

```json
{
  "Logging": {
    "LogLevel": {
      "SalesforceCore.Services.Authorization": "Debug",
      "SalesforceCore.Services.Layout": "Debug"
    }
  }
}
```

### Verify Permissions

Use the REST API to check current permissions:

```bash
# Get permission snapshot
curl -H "Authorization: Bearer TOKEN" \
     https://yourapp.com/api/dynamic-ui/permissions/Account

# Batch check
curl -H "Authorization: Bearer TOKEN" \
     "https://yourapp.com/api/dynamic-ui/permissions?objects=Account,Contact"
```

---

## Next Steps

- [03-Configuration.md](03-Configuration.md) - Complete configuration reference
- [09-Security.md](09-Security.md) - Security and FLS details
- [15-Tag-Helpers.md](15-Tag-Helpers.md) - Razor Tag Helpers for forms
- [12-Tutorial-MVC-CRUD-App.md](12-Tutorial-MVC-CRUD-App.md) - Step-by-step MVC tutorial

Session timestamp: 2025-12-25T23:00:00Z
