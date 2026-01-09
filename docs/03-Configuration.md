# Complete Configuration Reference

This document provides an exhaustive reference for all configuration options in SalesforceCore. Every setting, its type, default value, and purpose is documented here with examples and best practices.

## Table of Contents

1. [Configuration Overview](#configuration-overview)
2. [Configuration Sources](#configuration-sources)
3. [Core Salesforce Options](#core-salesforce-options)
4. [Authentication Options](#authentication-options)
5. [MVC and Web Options](#mvc-and-web-options)
6. [Dynamic UI Options](#dynamic-ui-options)
7. [Caching Options](#caching-options)
8. [Security Options](#security-options)
9. [Resilience Options](#resilience-options)
10. [Bulk API Options](#bulk-api-options)
11. [Programmatic Configuration](#programmatic-configuration)
12. [Environment-Specific Configuration](#environment-specific-configuration)
13. [Secrets Management](#secrets-management)
14. [Configuration Validation](#configuration-validation)
15. [Dynamic Configuration Reload](#dynamic-configuration-reload)
16. [Complete Configuration Example](#complete-configuration-example)
17. [Best Practices](#best-practices)
18. [Troubleshooting Configuration Issues](#troubleshooting-configuration-issues)

---

## Configuration Overview

SalesforceCore uses the standard .NET configuration system (`Microsoft.Extensions.Configuration`) and supports:

- **appsettings.json** - Primary configuration file
- **appsettings.{Environment}.json** - Environment-specific overrides
- **Environment variables** - For containerized deployments
- **User secrets** - For development secrets
- **Azure Key Vault / AWS Secrets Manager** - For production secrets
- **Programmatic configuration** - Via fluent APIs

### Configuration Sections

```
┌──────────────────────────────────────────────────────────────┐
│                    Configuration Hierarchy                     │
├──────────────────────────────────────────────────────────────┤
│  appsettings.json                                             │
│  ├── Salesforce (Core settings)                               │
│  │   ├── ClientId, Domain, ApiVersion                        │
│  │   ├── HTTP settings                                        │
│  │   ├── Cache settings                                       │
│  │   └── Security settings                                    │
│  ├── SalesforceJwt (JWT auth)                                │
│  ├── SalesforceMvc (Web/MVC)                                 │
│  ├── DynamicUi (Dynamic UI system)                           │
│  └── ConnectionStrings (Cache, etc.)                         │
├──────────────────────────────────────────────────────────────┤
│  appsettings.Development.json (overrides for dev)             │
│  appsettings.Production.json (overrides for prod)             │
├──────────────────────────────────────────────────────────────┤
│  Environment Variables (highest priority)                     │
│  User Secrets (development only)                              │
└──────────────────────────────────────────────────────────────┘
```

---

## Configuration Sources

### JSON Configuration Files

The primary method of configuration:

```json
{
  "Salesforce": {
    "ClientId": "your_client_id",
    "Domain": "https://login.salesforce.com"
  }
}
```

### Environment Variables

Use double underscores (`__`) for nested properties:

```bash
# Linux/macOS
export Salesforce__ClientId="your_client_id"
export Salesforce__Domain="https://login.salesforce.com"

# Windows PowerShell
$env:Salesforce__ClientId = "your_client_id"
$env:Salesforce__Domain = "https://login.salesforce.com"

# Docker
docker run -e Salesforce__ClientId="your_client_id" myapp
```

### User Secrets (Development)

For local development, use the Secret Manager:

```bash
# Initialize (once per project)
dotnet user-secrets init

# Set individual secrets
dotnet user-secrets set "Salesforce:ClientId" "your_client_id"
dotnet user-secrets set "Salesforce:ClientSecret" "your_client_secret"

# List all secrets
dotnet user-secrets list

# Clear all secrets
dotnet user-secrets clear
```

User secrets are stored in:
- Windows: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
- Linux/macOS: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

### Current Behavior Addenda

- `dynamic_ui.json` is auto-loaded (path `DynamicUi:ConfigFilePath`) and merged over appsettings; partial JSON overlays do not remove existing values.
- If `DynamicUi:WatchConfigFile` is true, configuration hot-reloads without restart; use `DynamicUi:BypassCache` to disable caches during debugging.
- Permission/layout caches are scoped per user identity; form caches also include `recordTypeId` to prevent cross-user/record-type leakage.

### Azure Key Vault

For production secrets in Azure:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

Key Vault secret names use `--` instead of `:`:
- `Salesforce--ClientId` → `Salesforce:ClientId`
- `Salesforce--ClientSecret` → `Salesforce:ClientSecret`

---

## Core Salesforce Options

The `Salesforce` section contains all core library settings.

### Required Options

| Option | Type | Description |
|--------|------|-------------|
| `ClientId` | `string` | Your Connected App's Consumer Key. **Required.** |
| `Domain` | `string` | Salesforce OAuth/API domain. Defaults to `https://login.salesforce.com`. |
| `ApiVersion` | `string` | REST API version. Defaults to `v60.0`. |

```json
{
  "Salesforce": {
    "ClientId": "3MVG9d8...",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0"
  }
}
```

### Authentication Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ClientSecret` | `string` | `null` | Connected App's Consumer Secret. Required for Client Credentials flow. |
| `CallbackPath` | `string` | `/salesforce/callback` | OAuth redirect callback path. |
| `SessionCookieName` | `string` | `__Host-SalesforceSession` | Authentication cookie name (web apps). |
| `SessionTimeout` | `TimeSpan` | `08:00:00` | Cookie/session lifetime (web apps). |
| `ForceSecureCookie` | `bool` | `true` | Forces HTTPS-only cookies (recommended for production/reverse proxies). |
| `SlidingExpiration` | `bool` | `true` | Sliding expiration for auth cookie/session (web apps). |
| `PromptLogin` | `bool` | `false` | Forces re-auth on login (`prompt=login`) for PKCE web apps. |
| `Scopes` | `string[]` | `["openid","profile","email","api","refresh_token","offline_access"]` | Currently not applied by `AddSalesforceAuthentication` (scopes are fixed in code); use `OpenIdConnectOptions` customization if needed. |

```json
{
  "Salesforce": {
    "ClientId": "3MVG9d8...",
    // "ClientSecret": NO! Load from Environment/Vault
    "Domain": "https://login.salesforce.com",
    "CallbackPath": "/salesforce/callback",
    "SessionCookieName": "__Host-SalesforceSession",
    "SessionTimeout": "08:00:00",
    "ForceSecureCookie": true,
    "SlidingExpiration": true,
    "PromptLogin": false
  }
}
```

### Domain Options by Environment

| Environment | Domain |
|-------------|--------|
| Production | `https://login.salesforce.com` |
| Sandbox | `https://test.salesforce.com` |
| My Domain | `https://[your-domain].my.salesforce.com` |
| My Domain (Sandbox) | `https://[your-domain]--[sandbox-name].sandbox.my.salesforce.com` |

### HTTP Client Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HttpTimeout` | `TimeSpan` | `00:00:30` | Individual HTTP request timeout. |
| `TotalRequestTimeout` | `TimeSpan` | `00:01:00` | End-to-end request timeout including retries. |
| `MaxResponseContentBufferSize` | `long` | `10485760` | Max response buffer (10 MB). |
| `EnableDebugLogging` | `bool` | `false` | Log all HTTP requests/responses. |

```json
{
  "Salesforce": {
    "HttpTimeout": "00:00:30",
    "TotalRequestTimeout": "00:01:00",
    "MaxResponseContentBufferSize": 10485760,
    "EnableDebugLogging": false
  }
}
```

### API Version Information

| API Version | Salesforce Release | Notes |
|-------------|-------------------|-------|
| `v60.0` | Spring '24 | Latest stable, recommended |
| `v59.0` | Winter '24 | |
| `v58.0` | Summer '23 | |
| `v57.0` | Spring '23 | Minimum for some features |
| `v56.0` | Winter '23 | |

---

### Resilience Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts for failed requests. |
| `RetryBaseDelay` | `TimeSpan` | `00:00:01` | Base delay for exponential backoff (delays: 1s, 2s, 4s...). |
| `CircuitBreakerSamplingDuration` | `TimeSpan` | `00:00:30` | Circuit breaker sampling window. |
| `CircuitBreakerBreakDuration` | `TimeSpan` | `00:00:30` | Duration circuit stays open after tripping. |

### Security & Validation Options

> [!IMPORTANT]
> `EnforceFieldLevelSecurity` defaults to `true`. This changes which `ISchemaService` methods are called:
> - Uses `GetAccessibleFieldsAsync` instead of `GetQueryableFieldsAsync`
> - Uses `SanitizeFieldListWithFlsAsync` instead of `SanitizeFieldListAsync`
> - **Unit tests** must mock both FLS methods to avoid null reference exceptions.

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnforceFieldLevelSecurity` | `bool` | `true` | Enforce Salesforce Field Level Security. When false, `FlsEnforcementMode` is ignored. |
| `FlsEnforcementMode` | `enum` | `Silent` | How to handle FLS violations: `Silent` (drop fields quietly), `Strict` (throw exception), `None` (bypass checks). |
| `ValidateSoqlInputs` | `bool` | `true` | Validate and sanitize SOQL inputs. |
| `LookupSearchLimit` | `int` | `15` | Maximum lookup search results. |

### Deprecated Options

> [!WARNING]
> `UseDistributedCache` is deprecated. Use `CacheProvider = Distributed` instead.

---

## Authentication Options

### OAuth PKCE Options (Web Apps)

For web applications using browser-based authentication:

```json
{
  "Salesforce": {
    "ClientId": "3MVG9d8...",
    "Domain": "https://login.salesforce.com",
    "CallbackPath": "/salesforce/callback",
    "SessionCookieName": "__Host-SalesforceSession",
    "SessionTimeout": "08:00:00",
    "ForceSecureCookie": true,
    "SlidingExpiration": true,
    "PromptLogin": false
  }
}
```

### JWT Bearer Options (Server-to-Server)

For background services and workers:

```json
{
  "Salesforce": {
    "ClientId": "3MVG9d8...",
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceJwt": {
    "Username": "integration-user@yourcompany.com",
    "PrivateKeyPath": "/secrets/salesforce.key",
    "TokenExpiration": "00:05:00",
    "Audience": "https://login.salesforce.com"
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Username` | `string` | **Required** | Salesforce username for the integration user. |
| `PrivateKeyPath` | `string` | `null` | Path to PEM-encoded private key file. |
| `PrivateKey` | `string` | `null` | PEM-encoded private key string (alternative to path). |
| `TokenExpiration` | `TimeSpan` | `00:05:00` | JWT token validity period. |
| `Audience` | `string` | Uses `Domain` | JWT audience claim. |

**Private Key Generation:**

```bash
# Generate RSA private key
openssl genrsa -out salesforce.key 2048

# Generate self-signed certificate
openssl req -new -x509 -sha256 -key salesforce.key -out salesforce.crt -days 365 \
  -subj "/CN=Salesforce Integration/O=Your Company"

# View certificate details
openssl x509 -in salesforce.crt -text -noout
```

### Client Credentials Options

For trusted server applications:

```json
{
  "Salesforce": {
    "ClientId": "3MVG9d8...",
    // Secrets loaded from env vars
    "Domain": "https://login.salesforce.com"
  },
  "SalesforceClientCredentials": {
    "ClientId": "3MVG9d8..."
    // Secrets loaded from env vars
  }
}
```

---

## MVC and Web Options

The `SalesforceMvc` section configures ASP.NET Core MVC integration.

### Complete MVC Options

```json
{
  "SalesforceMvc": {
    "RoutePrefix": "sf",
    "LayoutPath": null,
    "UseEmbeddedViews": true,
    "UseEmbeddedStaticFiles": true,
    "StaticFilesPath": "/_salesforce",
    "EnableHtmx": true,
    "ShowRecordIds": false,
    "ConfirmDeletes": true,
    "EnableFileUploads": true,
    "AllowedFileExtensions": [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".jpg", ".jpeg", ".png", ".gif"],
    "CssFramework": "Bootstrap5",
    "ToastPosition": "TopRight",
    "ToastAutoDismissSeconds": 5,
    "ToastClosable": true,
    "EnableDependentPicklists": true,
    "EnableLookupAutocomplete": true,
    "CustomScriptPath": null,
    "CustomStylePath": null,
    "DefaultFormColumns": 1,
    "EnableDynamicSupport": true,
    "LookupMinChars": 2,
    "LookupDebounceMs": 300,
    "EnableSchemaValidation": true,
    "EnableCustomValidation": true,
    "ShowValidationSummary": true
  }
}
```

### MVC Option Details

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RoutePrefix` | `string` | `sf` | Conventional route prefix used by `MapSalesforceRoutes` when no override is passed. |
| `LayoutPath` | `string` | `null` | Optional layout view path override for embedded views. |
| `UseEmbeddedViews` | `bool` | `true` | Use embedded Razor views from the package. |
| `UseEmbeddedStaticFiles` | `bool` | `true` | Use embedded CSS/JS from the package. |
| `StaticFilesPath` | `string` | `/_salesforce` | URL path for static files (used by `UseSalesforceCore` and MVC views). |
| `EnableHtmx` | `bool` | `true` | Enable HTMX attributes in list and form views. |
| `ShowRecordIds` | `bool` | `false` | Display Salesforce IDs in list views. |
| `ConfirmDeletes` | `bool` | `true` | Show confirmation dialog before deletions in Details view. |
| `CssFramework` | `string` | `Bootstrap5` | CSS framework class set used by tag helpers. |
| `EnableDependentPicklists` | `bool` | `true` | Include dependent picklist script behavior. |
| `EnableLookupAutocomplete` | `bool` | `true` | Include lookup autocomplete script behavior. |
| `CustomScriptPath` | `string` | `null` | Optional script override path. |
| `CustomStylePath` | `string` | `null` | Optional stylesheet override path. |
| `ToastPosition` | `string` | `TopRight` | Toast position. |
| `ToastAutoDismissSeconds` | `int` | `5` | Auto-dismiss after seconds (0 to disable). |
| `ToastClosable` | `bool` | `true` | Allow manual toast dismissal. |
| `DefaultFormColumns` | `int` | `1` | Default columns for form layouts. |
| `EnableDynamicSupport` | `bool` | `true` | Enable mutation observer for dynamic content. |
| `LookupMinChars` | `int` | `2` | Min characters before lookup search triggers. |
| `LookupDebounceMs` | `int` | `300` | Debounce delay in milliseconds for lookups. |
| `EnableSchemaValidation` | `bool` | `true` | Enable schema-based validation rules. |
| `EnableCustomValidation` | `bool` | `true` | Enable custom validation rules. |
| `ShowValidationSummary` | `bool` | `true` | Show validation summary at top of forms. |

### File Upload Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableFileUploads` | `bool` | `true` | Enable upload UI and upload endpoint. |
| `AllowedFileExtensions` | `string[]` | Images + docs + data | `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.bmp`, `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt`, `.pptx`, `.txt`, `.csv`, `.xml`, `.json` |
| `Salesforce:MaxFileUploadSize` | `long` | `26214400` | Max file size in bytes (default: 25 MB). |

### CSS Framework Options

| Value | Description |
|-------|-------------|
| `Bootstrap5` | Bootstrap 5.x (default) |
| `SLDS` | Salesforce Lightning Design System classes |
| `Custom` | No framework-specific classes (custom CSS) |
| `None` | No framework-specific classes |

### Toast Notification Options

| Option | Values | Description |
|--------|--------|-------------|
| `ToastPosition` | `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft` | Toast position. |
| `ToastAutoDismissSeconds` | `int` | Auto-dismiss after seconds. 0 = manual dismiss. |

---

## Dynamic UI Options

The `DynamicUi` section configures the Dynamic UI system for permission-aware UI generation.

### Complete Dynamic UI Options

```json
{
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
      "EnableInlineEdit": false
    }
  }
}
```

### Dynamic UI Core Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConfigFilePath` | `string` | `dynamic_ui.json` | External configuration file path. |
| `WatchConfigFile` | `bool` | `true` | Hot-reload config file changes. |
| `PermissionCacheDuration` | `TimeSpan` | `00:05:00` | Cache duration for permission snapshots. |
| `LayoutCacheDuration` | `TimeSpan` | `00:10:00` | Cache duration for layout descriptors. |
| `BypassCache` | `bool` | `false` | Skip all caching (for debugging). |

### Visibility Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HideInaccessibleNavItems` | `bool` | `true` | Hide navigation items user can't access. |
| `HideInaccessibleFields` | `bool` | `true` | Hide fields user can't read. |
| `HideUnauthorizedActions` | `bool` | `true` | Hide actions user can't perform. |

### Layout Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `DefaultFormColumns` | `int` | `1` | Default columns in forms. |
| `DefaultPageSize` | `int` | `25` | Default records per page. |
| `MaxPageSize` | `int` | `100` | Maximum records per page. |

### Navigation Configuration

```json
{
  "DynamicUi": {
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
          "Order": 1,
          "Children": [
            {
              "Id": "new-account",
              "Label": "New Account",
              "Route": "/Salesforce/Account/Create",
              "RequiredPermission": "Create"
            }
          ]
        },
        {
          "Id": "contacts",
          "Label": "Contacts",
          "Icon": "bi-people",
          "Route": "/Salesforce/Contact",
          "SObject": "Contact",
          "Order": 2
        }
      ],
      "UtilityItems": [
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
}
```

### Object Configuration

Configure specific objects:

```json
{
  "DynamicUi": {
    "Objects": {
      "Account": {
        "DisplayLabel": "Customer Account",
        "EnableCreate": true,
        "EnableEdit": true,
        "EnableDelete": false,
        "IncludeFields": null,
        "ExcludeFields": ["Jigsaw", "CleanStatus", "DandbCompanyId"],

        "List": {
          "EnableSearch": true,
          "EnableFilters": true,
          "EnableSelection": false,
          "EnableExport": true,
          "PageSize": 25,
          "DefaultSortField": "Name",
          "DefaultSortDirection": "asc",
          "Columns": [
            { "FieldName": "Name", "IsLink": true, "IsSortable": true, "Order": 0 },
            { "FieldName": "Industry", "IsFilterable": true, "Order": 1 },
            { "FieldName": "AnnualRevenue", "Format": "currency", "Order": 2 },
            { "FieldName": "Phone", "Order": 3 },
            { "FieldName": "LastModifiedDate", "Format": "datetime", "Order": 4 }
          ],
          "RowActions": [
            { "Id": "edit", "Label": "Edit", "Type": "edit", "RequiredPermission": "Update" },
            { "Id": "delete", "Label": "Delete", "Type": "delete", "RequiredPermission": "Delete" }
          ]
        },

        "Form": {
          "Columns": 2,
          "ShowValidationSummary": true,
          "Sections": [
            {
              "Id": "basic",
              "Heading": "Account Information",
              "Fields": ["Name", "Type", "Industry", "AnnualRevenue"],
              "Columns": 2,
              "IsCollapsible": false
            },
            {
              "Id": "contact",
              "Heading": "Contact Information",
              "Fields": ["Phone", "Fax", "Website"],
              "Columns": 2
            },
            {
              "Id": "address",
              "Heading": "Billing Address",
              "Fields": ["BillingStreet", "BillingCity", "BillingState", "BillingPostalCode", "BillingCountry"],
              "Columns": 2,
              "IsCollapsible": true,
              "IsCollapsed": true
            },
            {
              "Id": "description",
              "Heading": "Description",
              "Fields": ["Description"],
              "Columns": 1
            }
          ],
          "Fields": [
            { "FieldName": "Name", "IsRequired": true },
            { "FieldName": "Description", "ControlType": "textarea", "ColumnSpan": 2 },
            { "FieldName": "Website", "Placeholder": "https://example.com" }
          ]
        },

        "Detail": {
          "Columns": 2,
          "RelatedLists": [
            {
              "RelationshipName": "Contacts",
              "Title": "Related Contacts",
              "MaxRecords": 10,
              "Columns": [
                { "FieldName": "Name", "IsLink": true },
                { "FieldName": "Email" },
                { "FieldName": "Phone" },
                { "FieldName": "Title" }
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
            }
          ]
        },

        "CustomActions": [
          {
            "Id": "send-email",
            "Label": "Send Email",
            "Type": "custom",
            "Icon": "bi-envelope",
            "Url": "/Salesforce/Account/{Id}/email",
            "RequiredPermission": "Read",
            "Order": 10
          }
        ]
      }
    }
  }
}
```

### Feature Flags

```json
{
  "DynamicUi": {
    "FeatureFlags": {
      "EnableExport": true,
      "EnableBulkActions": false,
      "EnableInlineEdit": false,
      "EnableAdvancedSearch": true,
      "EnableDashboards": false,
      "EnableReports": true
    }
  }
}
```

---

## Caching Options

SalesforceCore supports three cache providers, explicitly configured via the `CacheProvider` setting:

| Provider | Best For | Features |
|----------|----------|----------|
| `Memory` | Single-instance apps, development | Fast, no external dependencies |
| `Distributed` | Multi-instance with Redis/NCache | Shared cache, survives restarts |
| `SqlServer` | Government/regulated environments | **AES-256-GCM encryption**, full audit logging |

> [!IMPORTANT]
> **CacheProvider vs Authentication Ticket Storage**
>
> The `CacheProvider` setting **only affects schema metadata and lookup caching**. It does NOT control where ASP.NET Core authentication tickets are stored.
>
> - **Schema/Lookup caching**: Controlled by `Salesforce:CacheProvider`
> - **Auth ticket storage**: Controlled by ASP.NET Core's session/data protection system
>
> For SQL Server auth ticket storage in web farms, use `AddDistributedSqlServerCache()`:
> ```csharp
> // For auth ticket storage in SQL Server (separate from CacheProvider)
> builder.Services.AddDistributedSqlServerCache(options =>
> {
>     options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
>     options.SchemaName = "dbo";
>     options.TableName = "AuthTickets";
> });
> ```
> Then run: `dotnet sql-cache create "YOUR_CONNECTION_STRING" dbo AuthTickets`

### Memory Cache (Default)

Best for single-instance deployments and development:

```json
{
  "Salesforce": {
    "CacheProvider": "Memory",
    "CacheKeyPrefix": "SF_",
    "SchemaCacheDuration": "01:00:00",
    "LookupCacheDuration": "00:15:00"
  }
}
```

### Distributed Cache (Redis)

For multi-instance deployments with external Redis:

```json
{
  "Salesforce": {
    "CacheProvider": "Distributed",
    "CacheKeyPrefix": "SF_PROD_"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379,abortConnect=false,ssl=false,password=yourpassword"
  }
}
```

```csharp
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SalesforceCore_";
});
builder.Services.AddSalesforceCore(builder.Configuration);
```

### SQL Server Encrypted Cache (Government-Grade)

For environments without Redis or requiring encryption at rest. **All cached data is encrypted with AES-256-GCM** - encryption cannot be disabled.

**Features:**
- Mandatory AES-256-GCM encryption for all cached values
- Full audit logging of all cache operations
- Access tracking for compliance reporting
- Write-behind buffering for access/expiry metadata (prevents read-path write amplification; enabled by default)
- Background cleanup of expired entries
- Works in Console Apps (no ASP.NET Core required)

**Configuration:**

```json
{
  "Salesforce": {
    "CacheProvider": "SqlServer",
    "CacheKeyPrefix": "SF_",
    "SqlCacheEncryptionKey": "base64-encoded-32-byte-key-from-vault",
    "AllowInsecureSqlCacheKeyDerivation": false,
    "SqlCacheWriteBehind": {
      "Enabled": true,
      "Capacity": 50000,
      "MaxBatchSize": 500,
      "FlushInterval": "00:00:05",
      "SlidingExpirationRefreshThresholdSeconds": 60,
      "CleanupGracePeriod": "00:00:10"
    },
    "CacheCleanupInterval": "00:30:00"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

**Setup (ASP.NET Core) - Just 2 lines, same as Redis:**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Just two lines - table is auto-created on startup by default (AutoCreateTable=true).
builder.Services.AddSalesforceEncryptedSqlServerCache(builder.Configuration);
builder.Services.AddSalesforceCore(builder.Configuration);

var app = builder.Build();
app.Run();
```

**Setup (Console App) - Just 2 lines, same as Redis:**

```csharp
// Program.cs
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Just two lines - table is auto-created on startup by default (AutoCreateTable=true).
        services.AddSalesforceEncryptedSqlServerCache(context.Configuration);
        services.AddSalesforceCore(context.Configuration);
    })
    .Build();

await host.RunAsync();
```

**Generate Encryption Key:**

```csharp
using System.Security.Cryptography;

// Generate a secure 256-bit (32-byte) key
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
Console.WriteLine($"Encryption Key: {key}");

// Store this key in Azure Key Vault, AWS Secrets Manager, or similar
```

**Important: Encryption Key Is Required**

- If `Salesforce:SqlCacheEncryptionKey` is missing or invalid, the app will fail fast on startup when `CacheProvider=SqlServer`.
- Development-only escape hatch: set `Salesforce:AllowInsecureSqlCacheKeyDerivation=true` to derive a deterministic key (not secure for production).

**Write-Behind Notes (Read-Path Hardening)**

- Write-behind is enabled by default (`Salesforce:SqlCacheWriteBehind:Enabled=true`) to prevent a “read → write” amplification loop under load.
- Set `CleanupGracePeriod >= FlushInterval` to avoid cleanup deleting entries before buffered sliding-expiration refresh is flushed.
- Disabling write-behind makes reads perform awaited metadata writes (safe, but higher DB write load).

**Government Deployment Checklist:**

- [ ] Store encryption key in FIPS 140-2 compliant key management system
- [ ] Enable SQL Server Transparent Data Encryption (TDE) for defense in depth
- [ ] Configure Row-Level Security if multi-tenant
- [ ] Set up monitoring for cache statistics
- [ ] Review audit logs regularly

### Backward Compatibility

The old `UseDistributedCache` boolean is still supported but deprecated:

```json
// Old (deprecated, still works)
{
  "Salesforce": {
    "UseDistributedCache": true
  }
}

// New (preferred)
{
  "Salesforce": {
    "CacheProvider": "Distributed"
  }
}
```

### Cache Duration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `SchemaCacheDuration` | `TimeSpan` | `01:00:00` | Object metadata cache duration. |
| `LookupCacheDuration` | `TimeSpan` | `00:15:00` | Lookup results cache duration. |
| `PermissionCacheDuration` | `TimeSpan` | `00:05:00` | Permission snapshot cache. |
| `LayoutCacheDuration` | `TimeSpan` | `00:10:00` | Layout descriptor cache. |
| `CacheCleanupInterval` | `TimeSpan` | `00:30:00` | SQL Server cache cleanup interval. |

### SQL Server Cache Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `SqlCacheConnectionStringName` | `string` | `DefaultConnection` | Connection string name. |
| `SqlCacheEncryptionKey` | `string` | `null` | Base64-encoded 32-byte AES key. **Required** when `CacheProvider=SqlServer` (unless insecure derivation is explicitly enabled). |
| `AllowInsecureSqlCacheKeyDerivation` | `bool` | `false` | Development-only: derive a deterministic key when an explicit key is not provided (never enable in production). |
| `CacheCleanupInterval` | `TimeSpan` | `00:30:00` | Interval for removing expired entries. |
| `SqlCacheWriteBehind.Enabled` | `bool` | `true` | Enables write-behind buffering for access/expiry metadata updates. |
| `SqlCacheWriteBehind.Capacity` | `int` | `50000` | Max queued access events before overflow behavior applies. |
| `SqlCacheWriteBehind.MaxBatchSize` | `int` | `500` | Max distinct keys updated per SQL flush batch. |
| `SqlCacheWriteBehind.FlushInterval` | `TimeSpan` | `00:00:05` | Max time between flushes when there is buffered work. |
| `SqlCacheWriteBehind.SlidingExpirationRefreshThresholdSeconds` | `int` | `60` | Minimum expiry extension before persisting a sliding refresh (reduces write amplification). |
| `SqlCacheWriteBehind.CleanupGracePeriod` | `TimeSpan` | `00:00:10` | Cleanup safety window to avoid deleting entries before buffered refresh is flushed (must be >= FlushInterval). |

---

## Security Options

### Field-Level Security

```json
{
  "Salesforce": {
    "EnforceFieldLevelSecurity": true
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnforceFieldLevelSecurity` | `bool` | `true` | Filter read fields and write payloads using Salesforce FLS. Disable to pass fields through and let Salesforce enforce. |

### SOQL Injection Prevention

```json
{
  "Salesforce": {
    "ValidateSoqlInputs": true
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ValidateSoqlInputs` | `bool` | `true` | Validate raw SOQL (SELECT-only, no comment tokens). Prefer `SoqlBuilder` for safe query construction. |

---

## Resilience Options

### Retry Configuration

```json
{
  "Salesforce": {
    "MaxRetries": 3,
    "RetryBaseDelay": "00:00:01"
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts. |
| `RetryBaseDelay` | `TimeSpan` | `00:00:01` | Initial retry delay. |

### Circuit Breaker

```json
{
  "Salesforce": {
    "CircuitBreakerSamplingDuration": "00:00:30",
    "CircuitBreakerBreakDuration": "00:00:30",
    "TotalRequestTimeout": "00:01:00"
  }
}
```

---

## Bulk API Options

```json
{
  "Salesforce": {
    "BulkPollInterval": "00:00:05",
    "BulkJobTimeout": "00:30:00"
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `BulkPollInterval` | `TimeSpan` | `00:00:05` | Job status polling interval. |
| `BulkJobTimeout` | `TimeSpan` | `00:30:00` | Maximum wait for job completion. |

---

## Programmatic Configuration

### Configure via Options Pattern

```csharp
builder.Services.AddSalesforceCore(options =>
{
    options.Domain = "https://login.salesforce.com";
    options.ClientId = Configuration["Salesforce:ClientId"]!;
    options.ClientSecret = Configuration["Salesforce:ClientSecret"];
    options.ApiVersion = "v60.0";
    options.MaxRetries = 3;
    options.RetryBaseDelay = TimeSpan.FromSeconds(1);
    options.CacheProvider = CacheProviderType.Distributed; // SalesforceCore.Services.Caching
    options.EnforceFieldLevelSecurity = true;
});
```

### Configure Dynamic UI Programmatically

```csharp
builder.Services.AddSalesforceDynamicUi(options =>
{
    options.DefaultFormColumns = 2;
    options.DefaultPageSize = 25;
    options.HideInaccessibleNavItems = true;

    options.Navigation.AppName = "My CRM";
    options.Navigation.LogoUrl = "/images/logo.png";
    options.Navigation.Items.Add(new NavigationItemConfig
    {
        Id = "accounts",
        Label = "Accounts",
        Icon = "bi-building",
        Route = "/Salesforce/Account",
        SObject = "Account",
        Order = 1
    });

    options.Objects["Account"] = new ObjectUiConfig
    {
        DisplayLabel = "Customer Accounts",
        EnableCreate = true,
        EnableEdit = true,
        EnableDelete = false
    };

    options.FeatureFlags["EnableExport"] = true;
    options.FeatureFlags["EnableBulkActions"] = false;
});
```

### Post-Configuration

```csharp
builder.Services.PostConfigure<SalesforceOptions>(options =>
{
    // Apply transformations after all configuration is loaded
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDebugLogging = true;
    }
});
```

---

## Environment-Specific Configuration

### appsettings.Development.json

```json
{
  "Salesforce": {
    "Domain": "https://test.salesforce.com",
    "EnableDebugLogging": true,
    "CacheProvider": "Memory"
  },
  "DynamicUi": {
    "BypassCache": true
  }
}
```

### appsettings.Production.json

```json
{
  "Salesforce": {
    "Domain": "https://login.salesforce.com",
    "EnableDebugLogging": false,
    "CacheProvider": "Distributed",
    "MaxRetries": 5,
    "EnforceFieldLevelSecurity": true,
    "ValidateSoqlInputs": true
  },
  "DynamicUi": {
    "BypassCache": false,
    "PermissionCacheDuration": "00:10:00",
    "LayoutCacheDuration": "00:30:00"
  }
}
```

### appsettings.Staging.json

```json
{
  "Salesforce": {
    "Domain": "https://yourcompany--staging.sandbox.my.salesforce.com",
    "EnableDebugLogging": true,
    "CacheProvider": "Distributed"
  }
}
```

---

## Configuration Validation

### Validate on Startup

```csharp
builder.Services.AddOptions<SalesforceOptions>()
    .Bind(builder.Configuration.GetSection("Salesforce"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Custom Validation

```csharp
builder.Services.AddOptions<SalesforceOptions>()
    .Bind(builder.Configuration.GetSection("Salesforce"))
    .Validate(options =>
    {
        if (string.IsNullOrEmpty(options.ClientId))
            return false;
        if (string.IsNullOrEmpty(options.Domain))
            return false;
        if (!options.Domain.StartsWith("https://"))
            return false;
        return true;
    }, "Invalid Salesforce configuration");
```

---

## Complete Configuration Example

Here's a complete, production-ready configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SalesforceCore": "Information",
      "SalesforceCore.Services": "Debug"
    }
  },

	  "Salesforce": {
	    "ClientId": "3MVG9d8...",
	    // "ClientSecret": REMOVED - Use Environment Variables
	    "Domain": "https://login.salesforce.com",
	    "ApiVersion": "v60.0",
	    "CallbackPath": "/salesforce/callback",
	    // Scopes are fixed by AddSalesforceAuthentication (openid/profile/email/api/refresh_token).

	    "HttpTimeout": "00:00:30",
	    "TotalRequestTimeout": "00:01:00",
	    "MaxRetries": 3,
    "RetryBaseDelay": "00:00:01",

    "CacheProvider": "Distributed",
    "CacheKeyPrefix": "SF_PROD_",
    "SchemaCacheDuration": "01:00:00",
    "LookupCacheDuration": "00:15:00",

    "DefaultPageSize": 25,
    "MaxPageSize": 100,

	    "EnforceFieldLevelSecurity": true,
	    "ValidateSoqlInputs": true,
	    "MaxFileUploadSize": 26214400,
	    "EnableDebugLogging": false,

    "BulkPollInterval": "00:00:05",
    "BulkJobTimeout": "00:30:00"
  },

  "SalesforceJwt": {
    "Username": "integration@yourcompany.com",
    "PrivateKeyPath": "/secrets/salesforce.key",
    "TokenExpiration": "00:05:00"
  },

	  "SalesforceMvc": {
	    "RoutePrefix": "sf",
	    "UseEmbeddedViews": true,
	    "UseEmbeddedStaticFiles": true,
	    "StaticFilesPath": "/_salesforce",
	    "EnableHtmx": true,
	    "ConfirmDeletes": true,
	    "EnableFileUploads": true,
	    "CssFramework": "Bootstrap5"
	  },

  "DynamicUi": {
    "ConfigFilePath": "dynamic_ui.json",
    "WatchConfigFile": true,
    "PermissionCacheDuration": "00:05:00",
    "LayoutCacheDuration": "00:10:00",
    "HideInaccessibleNavItems": true,
    "HideInaccessibleFields": true,
    "DefaultFormColumns": 1,
    "DefaultPageSize": 25,
    "Navigation": {
      "AppName": "Sales Cloud",
      "DefaultObjects": ["Account", "Contact", "Lead", "Opportunity", "Case"]
    },
    "FeatureFlags": {
      "EnableExport": true,
      "EnableBulkActions": true
    }
  },

  "ConnectionStrings": {
    "Redis": "your-redis-server:6379,password=yourpassword,ssl=true"
  }
}
```

---

## Best Practices

### 1. Never Hardcode Secrets

```csharp
// BAD
options.ClientSecret = "YOUR_CLIENT_SECRET";

// GOOD
// Load from KeyVault or Environment Variables via Configuration provider
options.ClientSecret = Configuration["Salesforce:ClientSecret"];
```

### 2. Use Environment-Specific Configuration

Create separate files for each environment and use the right domain.

### 3. Enable Caching in Production

```json
{
  "Salesforce": {
    "CacheProvider": "Distributed"
  }
}
```

### 4. Configure Appropriate Timeouts

Set timeouts based on your SLAs and Salesforce response times.

### 5. Enable FLS and Input Validation

Always keep security features enabled:

```json
{
  "Salesforce": {
    "EnforceFieldLevelSecurity": true,
    "ValidateSoqlInputs": true
  }
}
```

---

## Troubleshooting Configuration Issues

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| `ClientId is required` | Missing configuration | Add `Salesforce:ClientId` to config |
| `Invalid domain` | Wrong Salesforce URL | Use correct domain for your org type |
| `Configuration binding failed` | Typo in JSON | Validate JSON syntax |
| `Secrets not loading` | User secrets not initialized | Run `dotnet user-secrets init` |

### Diagnostic Logging

Enable configuration diagnostics:

```csharp
builder.Services.AddSalesforceCore(builder.Configuration);

// Log loaded configuration (development only!)
if (builder.Environment.IsDevelopment())
{
    var config = builder.Configuration.GetSection("Salesforce");
    logger.LogInformation("Salesforce Domain: {Domain}", config["Domain"]);
    logger.LogInformation("Salesforce ApiVersion: {Version}", config["ApiVersion"]);
}
```

Session timestamp: 2025-12-25T23:00:00Z

---

## Next Steps

- [02-Authentication.md](02-Authentication.md) - Authentication deep dive
- [04-Data-Service.md](04-Data-Service.md) - Data operations
- [17-Dynamic-UI-System.md](17-Dynamic-UI-System.md) - Dynamic UI configuration
- [09-Security.md](09-Security.md) - Security best practices
