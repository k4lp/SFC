# SalesforceCore

<p align="center">
  <strong>The Ultimate Enterprise .NET Integration Library for Salesforce</strong>
</p>

<p align="center">
  Build enterprise-grade, high-performance Salesforce applications with the familiar power of .NET 10 and ASP.NET Core. From LINQ-to-SOQL to Bulk API 2.0 to Dynamic UI Systems, SalesforceCore provides a complete, strongly-typed toolkit for the modern developer.
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net" alt=".NET 10"/></a>
  <img src="https://img.shields.io/badge/tests-454%20passing-brightgreen" alt="Tests"/>
</p>

---

## Table of Contents

1. [Introduction](#introduction)
2. [Why SalesforceCore?](#why-salesforcecore)
3. [Architecture Overview](#architecture-overview)
4. [Features](#features)
   - [Data Access & Querying](#data-access--querying)
   - [High-Volume Operations](#high-volume-operations)
   - [Developer Experience](#developer-experience)
   - [ASP.NET Core Integration](#aspnet-core-integration)
   - [Dynamic UI System](#dynamic-ui-system)
   - [Security & Enterprise Features](#security--enterprise-features)
5. [Installation](#installation)
6. [Quick Start Guide](#quick-start-guide)
   - [1. Define Your Models](#1-define-your-models)
   - [2. Configure Services](#2-configure-services)
   - [3. Query & Manipulate Data](#3-query--manipulate-data)
7. [Authentication](#authentication)
   - [OAuth 2.0 PKCE Flow](#oauth-20-pkce-flow)
   - [JWT Bearer Flow](#jwt-bearer-flow)
   - [Client Credentials Flow](#client-credentials-flow)
8. [Configuration Reference](#configuration-reference)
   - [Core Options](#core-options)
   - [MVC Options](#mvc-options)
   - [Dynamic UI Options](#dynamic-ui-options)
   - [Caching Options](#caching-options)
9. [Services Reference](#services-reference)
   - [IDataService](#idataservice)
   - [ITypedDataService](#itypeddataservice)
   - [ISchemaService](#ischemaservice)
   - [IBulkService](#ibulkservice)
   - [ICompositeService](#icompositeservice)
   - [IPermissionService](#ipermissionservice)
   - [ILayoutDescriptorService](#ilayoutdescriptorservice)
10. [Tag Helpers](#tag-helpers)
11. [Dynamic UI System](#dynamic-ui-system-1)
12. [API Endpoints](#api-endpoints)
13. [Model Generator CLI](#model-generator-cli)
14. [Best Practices](#best-practices)
15. [Performance Optimization](#performance-optimization)
16. [Troubleshooting](#troubleshooting)
17. [Migration Guide](#migration-guide)
18. [Contributing](#contributing)
19. [License](#license)

---

## Introduction

SalesforceCore is a comprehensive .NET library designed to provide seamless integration with Salesforce CRM. Built from the ground up for .NET 10 and leveraging the latest ASP.NET Core features, it offers a modern, type-safe approach to Salesforce development that eliminates the pain points of traditional integration approaches.

### What Problems Does SalesforceCore Solve?

1. **Magic Strings Everywhere**: Traditional Salesforce integrations rely heavily on string-based field and object names, leading to runtime errors and maintenance nightmares. SalesforceCore uses strongly-typed models with compile-time checking.

2. **Complex Query Building**: Writing SOQL queries as strings is error-prone and lacks IntelliSense support. SalesforceCore provides a full LINQ provider that translates C# expressions to optimized SOQL.

3. **Authentication Complexity**: Managing OAuth tokens, refresh flows, and session state is complex. SalesforceCore handles all authentication flows automatically with built-in token management.

4. **Performance at Scale**: Processing large datasets through the standard REST API is slow. SalesforceCore integrates Bulk API 2.0 and Composite APIs for massive throughput.

5. **Field-Level Security**: Enforcing Salesforce FLS in custom applications is often overlooked. SalesforceCore automatically enforces FLS on all read and write operations.

6. **Dynamic UI Generation**: Building permission-aware UIs that respect Salesforce metadata is tedious. SalesforceCore's Dynamic UI system generates forms, lists, and navigation automatically.

---

## Why SalesforceCore?

SalesforceCore isn't just another Salesforce wrapper library—it's a **complete productivity platform** designed for enterprise .NET development.

### Type-Safety First

Every interaction with Salesforce is strongly-typed. No more magic strings, no more runtime surprises:

```csharp
// Traditional approach - error-prone
var query = "SELECT Id, Name FROM Account WHERE Industry = 'Technology'";

// SalesforceCore approach - compile-time checked
var accounts = await _data.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .ToListAsync();
```

### LINQ Support

Write queries using familiar C# LINQ syntax that translates to optimized SOQL:

```csharp
var highValueAccounts = await _data.Query<Account>()
    .Where(a => a.AnnualRevenue > 1000000)
    .Where(a => a.Industry != "Government")
    .OrderByDescending(a => a.AnnualRevenue)
    .Select(a => new { a.Id, a.Name, a.AnnualRevenue })
    .Take(50)
    .ToListAsync();

// Generates: SELECT Id, Name, AnnualRevenue FROM Account
// WHERE AnnualRevenue > 1000000 AND Industry != 'Government'
// ORDER BY AnnualRevenue DESC LIMIT 50
```

### Enterprise Ready

Built-in resilience, caching, distributed locking, and rate-limit handling:

```csharp
// Automatic retry with exponential backoff
builder.Services.AddSalesforceCore(options =>
{
    options.MaxRetries = 3;
    options.RetryBaseDelay = TimeSpan.FromSeconds(1);
    options.CacheProvider = CacheProviderType.Distributed; // SalesforceCore.Services.Caching
    options.CacheKeyPrefix = "SF_PROD_";
});

// Distributed caching for web farms
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
```

### High Performance

Bulk API 2.0 and Composite Graph API support for massive throughput:

```csharp
// Process millions of records with Bulk API 2.0
var records = await GetMillionRecords();
var result = await _bulkService.InsertAsync("Account", records);

Console.WriteLine($"Processed: {result.Job.NumberRecordsProcessed}");
Console.WriteLine($"Success: {result.Job.NumberRecordsFailed == 0}");

// Chain 500 dependent operations in a single transaction
var graph = _compositeService.CreateGraphBuilder()
    .StartGraph("customer-onboarding")
    .Create("Account", new Dictionary<string, object?> { ["Name"] = "Acme Corp" }, "account")
    .CreateWithReference("Contact", new Dictionary<string, object?>
    {
        ["FirstName"] = "Ada",
        ["LastName"] = "Lovelace",
        ["AccountId"] = "@{account.id}"
    }, "contact")
    .CreateWithReference("Opportunity", new Dictionary<string, object?>
    {
        ["Name"] = "New Deal",
        ["StageName"] = "Prospecting",
        ["CloseDate"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
        ["AccountId"] = "@{account.id}"
    }, "opportunity")
    .Build();

await _compositeService.ExecuteGraphAsync(graph);
```

### ASP.NET Core Native

Tag Helpers, Middleware, Dependency Injection, and API Controllers out of the box:

```html
<!-- Razor Tag Helpers for rapid development -->
<sf-lookup asp-for="AccountId" sf-target-object="Account" sf-placeholder="Search Accounts..." />
<sf-picklist asp-for="Industry" sf-object="Account" sf-record-type-id="@recordTypeId" />
<sf-model-form asp-model="Model" sf-object="Account" sf-mode="Create" sf-columns="2" />
```

### Dynamic UI System

Permission-aware UI generation from configuration:

```json
{
  "DynamicUi": {
    "Navigation": {
      "AppName": "My CRM",
        "Items": [
        { "Id": "accounts", "Label": "Accounts", "SObject": "Account", "Route": "/Salesforce/Account" }
      ]
    },
    "Objects": {
      "Account": {
        "EnableCreate": true,
        "List": { "Columns": ["Name", "Industry", "AnnualRevenue"] },
        "Form": { "Sections": [{ "Heading": "Basic Info", "Fields": ["Name", "Industry"] }] }
      }
    }
  }
}
```

---

## Architecture Overview

SalesforceCore follows a layered architecture designed for extensibility and testability:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Your Application                              │
├─────────────────────────────────────────────────────────────────────┤
│                    ASP.NET Core Integration                          │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌────────────┐ │
│  │ Tag Helpers  │ │ Controllers  │ │  Middleware  │ │   Views    │ │
│  └──────────────┘ └──────────────┘ └──────────────┘ └────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│                      Dynamic UI Services                             │
│  ┌──────────────────────┐ ┌──────────────────────────────────────┐ │
│  │  PermissionService   │ │      LayoutDescriptorService         │ │
│  │  - CRUD checks       │ │      - Navigation                    │ │
│  │  - FLS evaluation    │ │      - Forms                         │ │
│  │  - Batch permissions │ │      - Lists                         │ │
│  └──────────────────────┘ │      - Details                       │ │
│                           └──────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│                        Core Services                                 │
│  ┌────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ │
│  │DataService │ │SchemaService │ │ BulkService  │ │CompositeServ │ │
│  │ - Query    │ │ - Describe   │ │ - Insert     │ │ - Subrequests│ │
│  │ - CRUD     │ │ - Fields     │ │ - Update     │ │ - Graph API  │ │
│  │ - Search   │ │ - Relations  │ │ - Delete     │ │ - Trees      │ │
│  └────────────┘ └──────────────┘ └──────────────┘ └──────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│                    Infrastructure Services                           │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌────────────┐ │
│  │TokenProvider │ │CacheProvider │ │ HttpClient   │ │  Resilience│ │
│  │ - OAuth     │ │ - Memory     │ │  Factory     │ │  - Retry   │ │
│  │ - JWT       │ │ - Distributed│ │              │ │  - Circuit │ │
│  └──────────────┘ └──────────────┘ └──────────────┘ └────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│                        Salesforce REST API                           │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌────────────┐ │
│  │  REST API   │ │  Bulk API    │ │ Composite API│ │ Tooling API│ │
│  └──────────────┘ └──────────────┘ └──────────────┘ └────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Components

1. **Core Services Layer**: Fundamental data operations, schema introspection, and bulk operations.
2. **Dynamic UI Layer**: Permission-aware UI descriptor generation from metadata and configuration.
3. **ASP.NET Core Layer**: Web integration with Tag Helpers, Controllers, and Middleware.
4. **Infrastructure Layer**: Authentication, caching, HTTP client management, and resilience.

### Project Structure

```
SalesforceCore/
├── src/
│   ├── SalesforceCore/                    # Core library
│   │   ├── Attributes/                    # Model mapping attributes
│   │   ├── Extensions/                    # Extension methods
│   │   ├── Mapping/                       # Object mapping
│   │   ├── Models/
│   │   │   ├── Authorization/             # Permission models
│   │   │   ├── Configuration/             # Options and config
│   │   │   ├── Data/                      # Data models
│   │   │   ├── Layout/                    # UI descriptors
│   │   │   └── Metadata/                  # Schema models
│   │   ├── Query/                         # LINQ provider
│   │   ├── Services/
│   │   │   ├── Apex/                      # Apex execution
│   │   │   ├── Authorization/             # Permission service
│   │   │   ├── Caching/                   # Cache providers
│   │   │   ├── Configuration/             # Config service
│   │   │   ├── Core/                      # Core services
│   │   │   ├── Data/                      # Data services
│   │   │   ├── Files/                     # File service
│   │   │   ├── Layout/                    # Layout service
│   │   │   ├── Metadata/                  # Schema service
│   │   │   ├── Reports/                   # Report service
│   │   │   └── Tooling/                   # Tooling service
│   │   └── Utilities/                     # Helper classes
│   │
│   ├── SalesforceCore.AspNetCore/         # ASP.NET Core integration
│   │   ├── Authentication/                # Auth handlers
│   │   ├── Controllers/                   # API controllers
│   │   ├── Extensions/                    # Service extensions
│   │   ├── Middleware/                    # HTTP middleware
│   │   ├── TagHelpers/                    # Razor tag helpers
│   │   └── ViewModels/                    # View models
│   │
│   └── SalesforceCore.ModelGenerator/     # CLI tool
│       └── Templates/                     # Code templates
│
├── tests/
│   └── SalesforceCore.Tests/              # Unit tests
│
├── samples/
│   └── SalesforceCore.SampleApp/          # Sample application
│
└── docs/                                  # Documentation
```

---

## Features

### Data Access & Querying

| Feature | Description | Example |
|---------|-------------|---------|
| **LINQ-to-SOQL** | Write standard C# LINQ queries that translate to optimized SOQL | `_data.Query<Account>().Where(a => a.Name.Contains("Acme"))` |
| **Dynamic SOQL** | Build queries dynamically when types aren't known at compile time | `_data.QueryAsync("SELECT Id FROM Account")` |
| **SOSL Search** | Full-text search across multiple objects | `_search.FindAsync("Acme", new[] { "Account", "Contact" })` |
| **Smart Pagination** | Automatic handling of `queryMore` for large result sets | Transparent cursor management |
| **Relationship Queries** | Query parent and child relationships | `Query<Contact>().Include(c => c.Account)` |
| **Aggregate Queries** | COUNT, SUM, AVG, MIN, MAX operations | `Query<Account>().CountAsync()` |
| **Recent Items** | Access user's recently viewed records | `_data.GetRecentItemsAsync("Account", 10)` |

#### Extended LINQ Operators

These operators provide workarounds for SOQL limitations:

| Operator | Description | Example |
|----------|-------------|---------|
| **DistinctAsync** | Get unique values using GROUP BY | `Query<Account>().DistinctAsync(a => a.Industry)` |
| **AllAsync** | Check if all records match condition | `Query<Account>().AllAsync(a => a.IsActive)` |
| **LastAsync** | Get last record by ordering | `Query<Account>().LastAsync(a => a.CreatedDate)` |
| **LastOrDefaultAsync** | Get last record or null | `Query<Account>().LastOrDefaultAsync(a => a.CreatedDate)` |
| **ElementAtAsync** | Get record at specific index | `Query<Account>().ElementAtAsync(4)` |
| **ElementAtOrDefaultAsync** | Get record at index or null | `Query<Account>().ElementAtOrDefaultAsync(4)` |
| **UnionAsync** | Combine distinct records from two queries | `query1.UnionAsync(query2)` |
| **ConcatAsync** | Concatenate all records from two queries | `query1.ConcatAsync(query2)` |
| **ExceptAsync** | Records in first but not second | `query1.ExceptAsync(query2)` |
| **IntersectAsync** | Records in both queries | `query1.IntersectAsync(query2)` |

#### LINQ-to-SOQL Examples

```csharp
// Basic query with filtering
var techAccounts = await _data.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .ToListAsync();

// Complex filtering with multiple conditions
var qualifiedLeads = await _data.Query<Lead>()
    .Where(l => l.Status == "Open" || l.Status == "Working")
    .Where(l => l.AnnualRevenue > 500000)
    .Where(l => l.LeadSource != "Web")
    .ToListAsync();

// Ordering and limiting
var topOpportunities = await _data.Query<Opportunity>()
    .Where(o => o.StageName != "Closed Won")
    .OrderByDescending(o => o.Amount)
    .Take(20)
    .ToListAsync();

// Selecting specific fields
var accountNames = await _data.Query<Account>()
    .Select(a => new { a.Id, a.Name, a.Industry })
    .ToListAsync();

// String operations
var acmeAccounts = await _data.Query<Account>()
    .Where(a => a.Name.StartsWith("Acme"))
    .Where(a => a.Description.Contains("enterprise"))
    .ToListAsync();

// Date filtering
var recentOpps = await _data.Query<Opportunity>()
    .Where(o => o.CreatedDate > DateTime.UtcNow.AddDays(-30))
    .ToListAsync();

// Null checks
var accountsWithWebsite = await _data.Query<Account>()
    .Where(a => a.Website != null)
    .ToListAsync();

// Relationship queries
var contactsWithAccounts = await _data.Query<Contact>()
    .Where(c => c.Account.Industry == "Technology")
    .Select(c => new { c.Name, AccountName = c.Account.Name })
    .ToListAsync();
```

#### Extended LINQ Operator Examples

```csharp
// Get unique industries (DISTINCT workaround using GROUP BY)
var industries = await _data.Query<Account>()
    .Where(a => a.AnnualRevenue > 1000000)
    .DistinctAsync(a => a.Industry);
// Generates: SELECT Industry FROM Account WHERE AnnualRevenue > 1000000 GROUP BY Industry

// Check if all accounts are active
var allActive = await _data.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .AllAsync(a => a.IsActive == true);

// Get the last created account
var lastAccount = await _data.Query<Account>()
    .Where(a => a.Industry == "Technology")
    .LastAsync(a => a.CreatedDate);

// Get element at specific position
var fifthAccount = await _data.Query<Account>()
    .OrderBy(a => a.Name)
    .ElementAtOrDefaultAsync(4); // 0-indexed

// Union two queries (distinct by Id)
var techAccounts = _data.Query<Account>().Where(a => a.Industry == "Technology");
var financeAccounts = _data.Query<Account>().Where(a => a.Industry == "Finance");
var combined = await techAccounts.UnionAsync(financeAccounts);

// Get accounts in first query but not second (set difference)
var activeAccounts = _data.Query<Account>().Where(a => a.IsActive == true);
var recentAccounts = _data.Query<Account>().Where(a => a.CreatedDate > DateTime.UtcNow.AddDays(-30));
var oldActiveAccounts = await activeAccounts.ExceptAsync(recentAccounts);

// Get accounts in both queries (intersection)
var commonAccounts = await techAccounts.IntersectAsync(activeAccounts);
```

### High-Volume Operations

| Feature | Description | Capacity |
|---------|-------------|----------|
| **Bulk API 2.0 Insert** | Mass insert with automatic CSV serialization or raw CSV input | 150M records/day |
| **Bulk API 2.0 Update** | Mass update with field mapping | 150M records/day |
| **Bulk API 2.0 Delete** | Mass delete by ID | 150M records/day |
| **Bulk API 2.0 Upsert** | Insert or update by external ID | 150M records/day |
| **Composite API** | Chain up to 25 requests in a single round-trip | 25 subrequests |
| **Composite Graph** | Orchestrate complex transactions with dependencies | 500 operations |
| **sObject Collections** | Batch create/update/delete | 200 records |

#### Bulk API 2.0 Examples

```csharp
// Bulk insert
var accounts = Enumerable.Range(1, 10000).Select(i => new Dictionary<string, object>
{
    ["Name"] = $"Account {i}",
    ["Industry"] = "Technology",
    ["Website"] = $"https://account{i}.com"
}).ToList();

var insertResult = await _bulkService.InsertAsync("Account", accounts);
Console.WriteLine($"Job ID: {insertResult.Job.Id}");
Console.WriteLine($"Records Processed: {insertResult.Job.NumberRecordsProcessed}");
Console.WriteLine($"Records Failed: {insertResult.Job.NumberRecordsFailed}");

// Bulk update
var updates = existingAccounts.Select(a => new Dictionary<string, object>
{
    ["Id"] = a.Id,
    ["Industry"] = "Updated Industry"
}).ToList();

var updateResult = await _bulkService.UpdateAsync("Account", updates);

// Bulk upsert by external ID
var upserts = records.Select(r => new Dictionary<string, object>
{
    ["External_Id__c"] = r.ExternalId,
    ["Name"] = r.Name,
    ["Industry"] = r.Industry
}).ToList();

var upsertResult = await _bulkService.UpsertAsync("Account", "External_Id__c", upserts);

// Bulk delete
var idsToDelete = accounts.Select(a => a.Id).ToList();
var deleteResult = await _bulkService.DeleteAsync("Account", idsToDelete);

// Query with Bulk API (for large exports)
var queryResult = await _bulkService.QueryAsync("Account",
    "SELECT Id, Name, Industry FROM Account WHERE CreatedDate > LAST_YEAR");
```

#### Composite API Examples

```csharp
// Chain multiple operations
var results = await _compositeService.CreateBatch()
    .Add(CompositeSubRequestBuilder.Get("/services/data/v60.0/sobjects/Account/001xxx", "account").Build())
    .Add(CompositeSubRequestBuilder.Get("/services/data/v60.0/sobjects/Account/@{account.Id}/Contacts", "contacts").Build())
    .ExecuteAsync();

// Composite Graph for complex transactions
var graph = _compositeService.CreateGraphBuilder()
    .StartGraph("new-customer")
    .Create("Account", new Dictionary<string, object?>
    {
        ["Name"] = "Acme Corporation",
        ["Industry"] = "Technology"
    }, "account")
    .CreateWithReference("Contact", new Dictionary<string, object?>
    {
        ["FirstName"] = "John",
        ["LastName"] = "Doe",
        ["Email"] = "john.doe@acme.com",
        ["AccountId"] = "@{account.id}"
    }, "primaryContact")
    .CreateWithReference("Opportunity", new Dictionary<string, object?>
    {
        ["Name"] = "Acme - New Business",
        ["StageName"] = "Prospecting",
        ["CloseDate"] = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd"),
        ["Amount"] = 100000,
        ["AccountId"] = "@{account.id}"
    }, "opportunity")
    .CreateWithReference("Task", new Dictionary<string, object?>
    {
        ["Subject"] = "Follow up with primary contact",
        ["WhoId"] = "@{primaryContact.id}",
        ["WhatId"] = "@{opportunity.id}",
        ["ActivityDate"] = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd")
    }, "task")
    .Build();

var graphResult = await _compositeService.ExecuteGraphAsync(graph);
```

### Developer Experience

| Feature | Description |
|---------|-------------|
| **CLI Model Generator** | Scaffold your entire org's schema into C# classes |
| **Tooling API** | Deploy Apex, run tests, manage metadata |
| **Execute Anonymous** | Run Apex snippets directly from .NET |
| **Custom Apex REST** | Call custom `@RestResource` endpoints |
| **Analytics API** | Run reports and retrieve dashboard data |
| **Schema Services** | Introspect metadata on the fly |
| **Limits Service** | Monitor API usage limits |
| **Validation Rule Engine** | Define and execute client-side validation rules |
| **Change Tracking** | ORM-style state management and dirty checking |

#### Model Generator CLI

```bash
# Install globally
dotnet tool install -g SalesforceCore.ModelGenerator

# Generate models for specific objects
sf-gen --objects Account,Contact,Opportunity --output ./Models

# Generate all accessible objects
sf-gen --all --output ./Models --namespace MyApp.Models

# Generate with custom options
sf-gen --objects Account,Contact \
       --output ./Models \
       --namespace MyApp.Salesforce.Models \
       --include-relationships \
       --include-picklist-enums \
       --use-nullable-reference-types
```

Generated model example:

```csharp
// Auto-generated by SalesforceCore.ModelGenerator
using SalesforceCore.Attributes;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Models;

/// <summary>
/// Represents a Salesforce Account object.
/// API Name: Account
/// Label: Account
/// </summary>
[SalesforceObject("Account")]
public class Account
{
    /// <summary>
    /// Account ID (Id)
    /// Type: id
    /// </summary>
    [SalesforceId]
    public string? Id { get; set; }

    /// <summary>
    /// Account Name (Name)
    /// Type: string
    /// Required: Yes
    /// Max Length: 255
    /// </summary>
    [SalesforceField("Name", Required = true)]
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Industry (Industry)
    /// Type: picklist
    /// </summary>
    [SalesforceField("Industry")]
    public string? Industry { get; set; }

    /// <summary>
    /// Annual Revenue (AnnualRevenue)
    /// Type: currency
    /// </summary>
    [SalesforceField("AnnualRevenue")]
    public decimal? AnnualRevenue { get; set; }

    /// <summary>
    /// Parent Account (ParentId)
    /// Type: reference
    /// References: Account
    /// </summary>
    [SalesforceField("ParentId", ReferenceTo = "Account")]
    public string? ParentId { get; set; }

    /// <summary>
    /// Navigation property for Parent Account
    /// </summary>
    [SalesforceRelationship("Parent")]
    public Account? Parent { get; set; }

    /// <summary>
    /// Child Contacts relationship
    /// </summary>
    [SalesforceChildRelationship("Contacts", "Contact", "AccountId")]
    public List<Contact>? Contacts { get; set; }
}
```

### ASP.NET Core Integration

| Feature | Description |
|---------|-------------|
| **Tag Helpers** | `<sf-lookup>`, `<sf-picklist>`, `<sf-model-form>` |
| **Authentication** | OAuth 2.0 PKCE, JWT Bearer, Client Credentials |
| **MVC Controllers** | Ready-to-use controllers for common operations |
| **Middleware** | Exception handling, request logging |
| **Distributed Cache** | Redis-backed token and schema caching |

### Dynamic UI System

| Feature | Description |
|---------|-------------|
| **Permission Service** | CRUD and FLS permission checks |
| **Navigation Generator** | Permission-aware navigation menus |
| **Form Descriptors** | Dynamic form generation from metadata |
| **List Descriptors** | Dynamic list/table generation |
| **Detail Descriptors** | Dynamic detail views with related lists |
| **REST API** | JSON endpoints for SPAs |

### Security & Enterprise Features

| Feature | Description |
|---------|-------------|
| **OAuth 2.0 PKCE** | Secure web authentication flow |
| **JWT Bearer** | Server-to-server authentication |
| **Field-Level Security** | Automatic FLS enforcement |
| **SOQL Injection Prevention** | Input sanitization |
| **Rate Limit Handling** | Automatic retry with backoff |
| **Distributed Locking** | Prevent concurrent operations |
| **Audit Logging** | Track all API operations |

---

## Installation

### Prerequisites

- **.NET 10.0 SDK** or later
- **Salesforce Connected App** with appropriate OAuth scopes
- **API Access** enabled in your Salesforce org

### NuGet Packages

```bash
# Core library (required)

# ASP.NET Core integration (recommended for web apps)

# CLI tool (optional, global installation)
```
NOT YET PUBLISHED!
### Package Dependencies

The core library has minimal dependencies:
- `Microsoft.Extensions.Caching.Abstractions`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Http`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`
- `System.Text.Json`

The ASP.NET Core package adds:
- `Microsoft.AspNetCore.Authentication`
- `Microsoft.AspNetCore.Mvc.TagHelpers`

### Connected App Setup

1. In Salesforce Setup, navigate to **App Manager**
2. Click **New Connected App**
3. Configure the following:
   - **Enable OAuth Settings**: Yes
   - **Callback URL**: `https://your-domain.com/salesforce/callback`
   - **Selected OAuth Scopes**:
     - `Access and manage your data (api)`
     - `Access your basic information (id, profile, email, address, phone)`
     - `Perform requests on your behalf at any time (refresh_token, offline_access)`
     - `Full access (full)`

---

## Quick Start Guide

### 1. Define Your Models

Create strongly-typed models that map to Salesforce objects:

```csharp
using SalesforceCore.Attributes;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Models;

[SalesforceObject("Account")]
public class Account
{
    [SalesforceId]
    public string? Id { get; set; }

    [SalesforceField("Name", Required = true)]
    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [SalesforceField("Industry")]
    public string? Industry { get; set; }

    [SalesforceField("AnnualRevenue")]
    public decimal? AnnualRevenue { get; set; }

    [SalesforceField("Phone")]
    [Phone]
    public string? Phone { get; set; }

    [SalesforceField("Website")]
    [Url]
    public string? Website { get; set; }

    [SalesforceField("Description")]
    public string? Description { get; set; }

    [SalesforceField("BillingStreet")]
    public string? BillingStreet { get; set; }

    [SalesforceField("BillingCity")]
    public string? BillingCity { get; set; }

    [SalesforceField("BillingState")]
    public string? BillingState { get; set; }

    [SalesforceField("BillingPostalCode")]
    public string? BillingPostalCode { get; set; }

    [SalesforceField("BillingCountry")]
    public string? BillingCountry { get; set; }

    [SalesforceField("CreatedDate")]
    public DateTime? CreatedDate { get; set; }

    [SalesforceField("LastModifiedDate")]
    public DateTime? LastModifiedDate { get; set; }
}

[SalesforceObject("Contact")]
public class Contact
{
    [SalesforceId]
    public string? Id { get; set; }

    [SalesforceField("FirstName")]
    public string? FirstName { get; set; }

    [SalesforceField("LastName", Required = true)]
    [Required]
    public string LastName { get; set; } = string.Empty;

    [SalesforceField("Email")]
    [EmailAddress]
    public string? Email { get; set; }

    [SalesforceField("Phone")]
    [Phone]
    public string? Phone { get; set; }

    [SalesforceField("AccountId", ReferenceTo = "Account")]
    public string? AccountId { get; set; }

    [SalesforceRelationship("Account")]
    public Account? Account { get; set; }
}
```

### 2. Configure Services

In your `Program.cs`:

```csharp
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Salesforce Core with MVC components
builder.Services.AddSalesforceCoreMvc(builder.Configuration);

// Add Salesforce Authentication (PKCE flow)
builder.Services.AddSalesforceAuthentication(builder.Configuration);

// Add Dynamic UI services
builder.Services.AddSalesforceDynamicUi(options =>
{
    options.DefaultFormColumns = 2;
    options.HideInaccessibleNavItems = true;
    options.Navigation.AppName = "My CRM";
});

// Add distributed caching (recommended for production)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Add MVC
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Enable Salesforce middleware and routes
app.UseSalesforceCore();
app.MapSalesforceRoutes();
app.MapDynamicUiRoutes();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

Add configuration to `appsettings.json`:

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0",
    "CallbackPath": "/salesforce/callback",
    "MaxRetries": 3,
    "RetryBaseDelay": "00:00:01",
    "HttpTimeout": "00:00:30",
    "TotalRequestTimeout": "00:01:00",
    "CacheProvider": "Distributed",
    "CacheKeyPrefix": "SF_",
    "SchemaCacheDuration": "01:00:00",
    "EnforceFieldLevelSecurity": true,
    "ValidateSoqlInputs": true
  },
  "DynamicUi": {
    "ConfigFilePath": "dynamic_ui.json",
    "WatchConfigFile": true,
    "PermissionCacheDuration": "00:05:00",
    "LayoutCacheDuration": "00:10:00",
    "HideInaccessibleNavItems": true,
    "HideInaccessibleFields": true,
    "DefaultFormColumns": 2,
    "DefaultPageSize": 25
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

Note: `ClientSecret` is only required for Client Credentials flow. PKCE does not use it.

### 3. Query & Manipulate Data

Create a controller to interact with Salesforce data:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Services.Data;
using MyApp.Models;

namespace MyApp.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly ITypedDataService _data;
    private readonly ILogger<AccountController> _logger;

    public AccountController(ITypedDataService data, ILogger<AccountController> logger)
    {
        _data = data;
        _logger = logger;
    }

    // GET: /Account
    public async Task<IActionResult> Index(string? search, string? industry, int page = 1)
    {
        var query = _data.Query<Account>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a => a.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(industry))
        {
            query = query.Where(a => a.Industry == industry);
        }

        var accounts = await query
            .OrderByDescending(a => a.LastModifiedDate)
            .Skip((page - 1) * 25)
            .Take(25)
            .ToListAsync();

        return View(accounts);
    }

    // GET: /Account/Details/001xxx
    public async Task<IActionResult> Details(string id)
    {
        var account = await _data.GetByIdAsync<Account>(id);

        if (account == null)
        {
            return NotFound();
        }

        // Get related contacts
        var contacts = await _data.Query<Contact>()
            .Where(c => c.AccountId == id)
            .OrderBy(c => c.LastName)
            .ToListAsync();

        ViewBag.Contacts = contacts;
        return View(account);
    }

    // GET: /Account/Create
    public IActionResult Create()
    {
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
            var id = await _data.CreateAsync(account);
            _logger.LogInformation("Created Account {Id}", id);

            TempData["Success"] = "Account created successfully!";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Account");
            ModelState.AddModelError("", $"Error creating account: {ex.Message}");
            return View(account);
        }
    }

    // GET: /Account/Edit/001xxx
    public async Task<IActionResult> Edit(string id)
    {
        var account = await _data.GetByIdAsync<Account>(id);

        if (account == null)
        {
            return NotFound();
        }

        return View(account);
    }

    // POST: /Account/Edit/001xxx
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Account account)
    {
        if (id != account.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(account);
        }

        try
        {
            await _data.UpdateAsync(account);
            _logger.LogInformation("Updated Account {Id}", id);

            TempData["Success"] = "Account updated successfully!";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Account {Id}", id);
            ModelState.AddModelError("", $"Error updating account: {ex.Message}");
            return View(account);
        }
    }

    // POST: /Account/Delete/001xxx
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            await _data.DeleteAsync<Account>(id);
            _logger.LogInformation("Deleted Account {Id}", id);

            TempData["Success"] = "Account deleted successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Account {Id}", id);
            TempData["Error"] = $"Error deleting account: {ex.Message}";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
```

---

## Authentication

SalesforceCore supports multiple authentication flows to suit different application types.

### OAuth 2.0 PKCE Flow

The recommended flow for web applications. Uses Proof Key for Code Exchange for enhanced security.

```csharp
// Program.cs
builder.Services.AddSalesforceAuthentication(builder.Configuration);

// appsettings.json
{
  "Salesforce": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "CallbackPath": "/salesforce/callback",
    "Scopes": ["api", "refresh_token", "openid", "profile"]
  }
}
```

### JWT Bearer Flow

For server-to-server integration without user interaction. If the `SalesforceJwt` configuration section is present, the provider is automatically registered.

```csharp
// Program.cs
builder.Services.AddSalesforceCore(builder.Configuration);
// JwtTokenProvider is automatically registered if SalesforceJwt section exists

// appsettings.json
{
  "Salesforce": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceJwt": {
    "Username": "integration-user@company.com",
    "PrivateKeyPath": "/path/to/private.key",
    "TokenExpiration": "00:05:00"
  }
}
```

### Client Credentials Flow

For trusted server-to-server apps. Automatically registered if `SalesforceClientCredentials` section exists.

```csharp
// Program.cs
builder.Services.AddSalesforceCore(builder.Configuration);
// ClientCredentialsTokenProvider is automatically registered if SalesforceClientCredentials section exists

// appsettings.json
{
  "Salesforce": {
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceClientCredentials": {
    "ClientId": "YOUR_CONSUMER_KEY",
    "ClientSecret": "YOUR_CONSUMER_SECRET"
  }
}
```

---

## Configuration Reference

### Core Options (`Salesforce` Section)

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ClientId` | string | Required | Connected App Consumer Key |
| `ClientSecret` | string | Optional | Connected App Consumer Secret (required for Client Credentials flow) |
| `Domain` | string | `https://login.salesforce.com` | Salesforce auth/API base URL |
| `ApiVersion` | string | `v60.0` | REST API version |
| `CallbackPath` | string | `/salesforce/callback` | OAuth callback path |
| `HttpTimeout` | TimeSpan | `00:00:30` | Per-request timeout |
| `MaxResponseContentBufferSize` | long | `10485760` | Max response buffer in bytes (10 MB) |
| `MaxRetries` | int | `3` | Maximum retry attempts |
| `RetryBaseDelay` | TimeSpan | `00:00:01` | Base delay for exponential backoff |
| `TotalRequestTimeout` | TimeSpan | `00:01:00` | End-to-end timeout |
| `BulkPollInterval` | TimeSpan | `00:00:05` | Bulk job polling interval |
| `BulkJobTimeout` | TimeSpan | `00:30:00` | Maximum wait for bulk jobs |
| `CacheProvider` | string | `Memory` | Cache provider selection (Memory, Distributed, SqlServer) |
| `CacheKeyPrefix` | string | `SF_` | Cache key namespace |
| `SchemaCacheDuration` | TimeSpan | `01:00:00` | Metadata cache TTL |
| `LookupCacheDuration` | TimeSpan | `00:15:00` | Lookup cache TTL |
| `DefaultPageSize` | int | `25` | Default pagination size for list views and QueryPaged |
| `MaxPageSize` | int | `100` | Maximum pagination size |
| `EnableDebugLogging` | bool | `false` | Verbose HTTP logging |
| `EnforceFieldLevelSecurity` | bool | `true` | Filter read fields and write payloads using Salesforce FLS |
| `ValidateSoqlInputs` | bool | `true` | Validate raw SOQL (SELECT-only, no comment tokens) |
| `MaxFileUploadSize` | long | `26214400` | Max file size in bytes (default: 25 MB). |

### MVC Options (`SalesforceMvc` Section)

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RoutePrefix` | string | `sf` | Conventional route prefix used by `MapSalesforceRoutes` when no override is passed. |
| `LayoutPath` | string | `null` | Optional layout view override for embedded views. |
| `UseEmbeddedViews` | bool | `true` | Use embedded Razor views |
| `UseEmbeddedStaticFiles` | bool | `true` | Use embedded assets |
| `StaticFilesPath` | string | `/_salesforce` | Static files URL prefix (used by `UseSalesforceCore` and MVC views). |
| `EnableHtmx` | bool | `true` | Enable HTMX attributes in list and form views |
| `ShowRecordIds` | bool | `false` | Show IDs in list views |
| `ConfirmDeletes` | bool | `true` | Delete confirmation in Details view |
| `EnableFileUploads` | bool | `true` | Enable upload UI and endpoint |
| `AllowedFileExtensions` | string[] | Common docs | Upload allowlist |
| `CssFramework` | string | `Bootstrap5` | CSS framework |
| `EnableDependentPicklists` | bool | `true` | Include dependent picklist behavior |
| `EnableLookupAutocomplete` | bool | `true` | Include lookup autocomplete behavior |
| `CustomScriptPath` | string | `null` | Optional script override path |
| `CustomStylePath` | string | `null` | Optional stylesheet override path |
| `ToastPosition` | string | `TopRight` | Toast notification position |
| `ToastAutoDismissSeconds` | int | `5` | Toast auto-dismiss TTL |
| `ToastClosable` | bool | `true` | Allow manual toast dismissal |
| `DefaultFormColumns` | int | `1` | Default form columns |
| `EnableDynamicSupport` | bool | `true` | Enable mutation observer for dynamic content |
| `LookupMinChars` | int | `2` | Min characters before lookup search triggers |
| `LookupDebounceMs` | int | `300` | Debounce delay in milliseconds for lookups |
| `EnableSchemaValidation` | bool | `true` | Enable schema-based validation rules |
| `EnableCustomValidation` | bool | `true` | Enable custom validation rules |
| `ShowValidationSummary` | bool | `true` | Show validation summary at top of forms |

### Dynamic UI Options (`DynamicUi` Section)

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConfigFilePath` | string | `dynamic_ui.json` | External config file |
| `WatchConfigFile` | bool | `true` | Hot reload config |
| `PermissionCacheDuration` | TimeSpan | `00:05:00` | Permission cache TTL |
| `LayoutCacheDuration` | TimeSpan | `00:10:00` | Layout cache TTL |
| `BypassCache` | bool | `false` | Skip caching |
| `HideInaccessibleNavItems` | bool | `true` | Hide unauthorized nav |
| `HideInaccessibleFields` | bool | `true` | Hide unauthorized fields |
| `HideUnauthorizedActions` | bool | `true` | Hide unauthorized actions |
| `DefaultFormColumns` | int | `1` | Default form columns |
| `DefaultPageSize` | int | `25` | Default list page size |
| `MaxPageSize` | int | `100` | Maximum page size |

---

## Services Reference

### IDataService

Low-level data operations with dynamic types:

```csharp
public interface IDataService
{
    // Query
    Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default);
    Task<QueryResult> QueryNextAsync(string nextRecordsUrl, CancellationToken cancellationToken = default);

    // CRUD
    Task<JsonNode> GetRecordAsync(string sObject, string id, IEnumerable<string>? fields = null, CancellationToken cancellationToken = default);
    Task<string> CreateRecordAsync(string sObject, IDictionary<string, object?> data, CancellationToken cancellationToken = default);
    Task UpdateRecordAsync(string sObject, string id, IDictionary<string, object?> data, CancellationToken cancellationToken = default);
    Task DeleteRecordAsync(string sObject, string id, CancellationToken cancellationToken = default);

    // Upsert
    Task<UpsertResult> UpsertRecordAsync(
        string sObject,
        string externalIdField,
        string externalIdValue,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);
}
```

### ITypedDataService

Strongly-typed data operations with LINQ support:

```csharp
public interface ITypedDataService
{
    // LINQ Query
    SalesforceQueryable<T> Query<T>() where T : class, new();

    // CRUD
    Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken = default) where T : class, new();
    Task<T?> GetAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, new();
    Task<List<T>> GetAllAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new();
    Task<string> CreateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class;
    Task UpdateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class;
    Task DeleteAsync<T>(string id, CancellationToken cancellationToken = default) where T : class;
    Task<string> UpsertAsync<T>(T record, string? externalIdField = null, CancellationToken cancellationToken = default) where T : class;
}
```

### ISchemaService

Metadata introspection:

```csharp
public interface ISchemaService
{
    Task<SObjectDescribe?> GetDescribeAsync(string sObject, CancellationToken cancellationToken = default);
    Task<List<SObjectField>> GetFieldsAsync(string sObject, CancellationToken cancellationToken = default);
    Task<List<RecordTypeInfo>> GetRecordTypesAsync(string sObject, CancellationToken cancellationToken = default);
    Task<PicklistValuesResult> GetPicklistValuesAsync(string sObject, string fieldName, string? recordTypeId = null, CancellationToken cancellationToken = default);
    Task<List<ChildRelationship>> GetChildRelationshipsAsync(string sObject, CancellationToken cancellationToken = default);
    Task<string> GetNameFieldAsync(string sObject, CancellationToken cancellationToken = default);
    Task<List<SObjectInfo>> GetAllObjectsAsync(CancellationToken cancellationToken = default);
}
```

### IPermissionService

Permission checking:

```csharp
public interface IPermissionService
{
    Task<ObjectPermissionSnapshot> GetPermissionsAsync(string objectName, CancellationToken ct = default);
    Task<PermissionResult> GetPermissionsAsync(PermissionRequestContext context, CancellationToken ct = default);
    Task<bool> CanPerformActionAsync(string objectName, PermissionAction action, CancellationToken ct = default);
    Task<bool> CanAccessFieldAsync(string objectName, string fieldName, PermissionAction action, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetReadableFieldsAsync(string objectName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCreateableFieldsAsync(string objectName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUpdateableFieldsAsync(string objectName, CancellationToken ct = default);
}
```

### ILayoutDescriptorService

Dynamic UI generation:

```csharp
public interface ILayoutDescriptorService
{
    Task<NavigationDescriptor> GetNavigationAsync(string? currentPath = null, CancellationToken ct = default);
    Task<FormDescriptor> GetFormAsync(string objectName, FormMode mode, string? recordTypeId = null, CancellationToken ct = default);
    Task<ListDescriptor> GetListAsync(string objectName, CancellationToken ct = default);
    Task<DetailDescriptor> GetDetailAsync(string objectName, CancellationToken ct = default);
    Task<IReadOnlyList<FormAction>> GetAvailableActionsAsync(string objectName, UiActionContext context, CancellationToken ct = default);
    Task<IReadOnlyList<PicklistOption>> GetPicklistOptionsAsync(string objectName, string fieldName, string? controllingValue = null, string? recordTypeId = null, CancellationToken ct = default);
    Task<RecordTypeSelector?> GetRecordTypeSelectorAsync(string objectName, CancellationToken ct = default);
    Task<IReadOnlyList<RelatedListDescriptor>> GetRelatedListsAsync(string objectName, CancellationToken ct = default);
    Task RefreshAsync(string? objectName = null, CancellationToken ct = default);
}
```

---

## Tag Helpers

### sf-lookup

Renders an AJAX-powered lookup field for related records:

```html
<sf-lookup asp-for="AccountId"
           sf-target-object="Account"
           sf-display-template="{Name}"
           sf-placeholder="Search for an account..."
           sf-min-chars="2"
           sf-debounce="300"
           sf-limit="10"
           class="form-control" />
```

### sf-picklist

Renders a dropdown that respects Record Types and dependent picklists:

```html
<sf-picklist asp-for="Industry"
             sf-object="Account"
             sf-record-type-id="@recordTypeId"
             sf-controlling-field="Type"
             sf-include-blank="true"
             sf-blank-text="-- Select Industry --"
             class="form-select" />
```

### sf-model-form

Scaffolds a complete form based on model metadata:

```html
<sf-model-form asp-model="Model"
               sf-object="Account"
               sf-mode="Edit"
               sf-columns="2"
               sf-show-validation-summary="true"
               sf-exclude-fields="CreatedDate,LastModifiedDate" />
```

---

## API Endpoints

### Dynamic UI Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/dynamic-ui/navigation` | GET | Navigation descriptor |
| `/api/dynamic-ui/forms/{sObject}` | GET | Form descriptor |
| `/api/dynamic-ui/lists/{sObject}` | GET | List descriptor |
| `/api/dynamic-ui/details/{sObject}` | GET | Detail descriptor |
| `/api/dynamic-ui/permissions/{sObject}` | GET | Permission snapshot |
| `/api/dynamic-ui/permissions?objects=...` | GET | Batch permissions |
| `/api/dynamic-ui/actions/{sObject}` | GET | Available actions |
| `/api/dynamic-ui/picklist/{sObject}/{field}` | GET | Picklist options |
| `/api/dynamic-ui/record-types/{sObject}` | GET | Record type selector |
| `/api/dynamic-ui/related-lists/{sObject}` | GET | Related lists |
| `/api/dynamic-ui/fields/{sObject}/{fieldName}` | GET | Field descriptor |
| `/api/dynamic-ui/refresh` | POST | Refresh cache |

### Lookup Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/Lookup/Search` | GET | Search records for lookup (returns an HTML partial) |
| `/Lookup/Recent` | GET | Recent items (returns an HTML partial) |
| `/Lookup/Resolve` | GET | Resolve a record ID to display name (JSON) |

### File Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/Salesforce/{sObject}/Upload/{id}` | POST | Upload file to a record |
| `/File/Download/{versionId}/{filename?}` | GET | Download a file |
| `/File/GetImage/{versionId}` | GET | Get image bytes for inline display |
| `/File/Preview/{versionId}` | GET | Preview a file (inline) |

---

## Best Practices

1. **Use Strongly-Typed Models**: Always define models for your Salesforce objects rather than using dynamic dictionaries.

2. **Leverage Caching**: Enable distributed caching in production to reduce API calls and improve performance.

3. **Enforce FLS**: Keep `EnforceFieldLevelSecurity` enabled to respect Salesforce security model.

4. **Use Bulk API for Large Operations**: For operations involving more than 200 records, use the Bulk API instead of individual DML.

5. **Handle Rate Limits**: Configure appropriate retry policies to handle 429 responses gracefully.

6. **Monitor Limits**: Use ILimitsService to monitor API usage and avoid hitting daily limits.

7. **Use Connection Pooling**: The built-in HttpClientFactory handles connection pooling automatically.

8. **Validate Inputs**: Keep `ValidateSoqlInputs` enabled for basic raw SOQL validation; prefer `SoqlBuilder`/`SoqlCondition` for safe queries.

---

## Documentation Index

| Document | Description |
|----------|-------------|
| [00-Setup-Guide.md](docs/00-Setup-Guide.md) | Initial setup and prerequisites |
| [01-Getting-Started.md](docs/01-Getting-Started.md) | Quick start guide |
| [02-Authentication.md](docs/02-Authentication.md) | Authentication flows |
| [03-Configuration.md](docs/03-Configuration.md) | Complete configuration reference |
| [04-Data-Service.md](docs/04-Data-Service.md) | Low-level data operations |
| [05-Typed-Data-Service.md](docs/05-Typed-Data-Service.md) | LINQ and typed operations |
| [06-Schema-Service.md](docs/06-Schema-Service.md) | Metadata introspection |
| [07-Bulk-Composite-Services.md](docs/07-Bulk-Composite-Services.md) | High-volume operations |
| [08-Model-Generator-CLI.md](docs/08-Model-Generator-CLI.md) | CLI tool reference |
| [09-Security.md](docs/09-Security.md) | Security and FLS |
| [10-API-Reference.md](docs/10-API-Reference.md) | Complete API reference |
| [11-Additional-Services.md](docs/11-Additional-Services.md) | Files, Reports, Tooling |
| [12-Tutorial-MVC-CRUD-App.md](docs/12-Tutorial-MVC-CRUD-App.md) | Step-by-step MVC tutorial |
| [13-Complex-Scenarios-Guide.md](docs/13-Complex-Scenarios-Guide.md) | Advanced patterns |
| [14-Enterprise-Integration-Guide.md](docs/14-Enterprise-Integration-Guide.md) | Enterprise architecture |
| [15-Tag-Helpers.md](docs/15-Tag-Helpers.md) | Tag helper reference |
| [16-Backbone-Infrastructure.md](docs/16-Backbone-Infrastructure.md) | Infrastructure details |
| [17-Dynamic-UI-System.md](docs/17-Dynamic-UI-System.md) | Dynamic UI complete guide |

---

## Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Setup

```bash
git clone https://github.com/your-org/SalesforceCore.git
cd SalesforceCore
dotnet restore
dotnet build
dotnet test
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/SalesforceCore.Tests
```

---

## License

Licensed under the [License](LICENSE).

---

<p align="center">
  <strong>Built with ❤️ for the .NET Community</strong>
</p>

<p align="center">
  <a href="https://github.com/k4lp/SalesforceCore/issues">Report Bug</a> •
  <a href="https://github.com/k4lp/SalesforceCore/discussions">Request Feature</a> •
  <a href="docs/00-Setup-Guide.md">Documentation</a>
</p>
