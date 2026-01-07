# Security

Comprehensive security guidance for SalesforceCore - designed for government-grade deployments requiring the highest levels of data protection and compliance.

## Table of Contents

1. [Security Architecture Overview](#security-architecture-overview)
2. [Core Security Principles](#core-security-principles)
3. [SOQL Injection Prevention](#soql-injection-prevention)
4. [Authentication Security](#authentication-security)
5. [Field-Level Security (FLS)](#field-level-security-fls)
6. [Input Validation](#input-validation)
7. [Caching Security](#caching-security)
8. [Token Management](#token-management)
9. [Network Security](#network-security)
10. [Logging & Audit](#logging--audit)
11. [File Upload Security](#file-upload-security)
12. [Configuration Security](#configuration-security)
13. [Compliance Checklist](#compliance-checklist)

---

## Security Architecture Overview

SalesforceCore implements a defense-in-depth approach with multiple security layers:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Application Layer                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Visibility     │  │  FLS Service    │  │  Permission     │  │
│  │  Service        │  │  (Field-Level)  │  │  Service        │  │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘  │
├───────────┼────────────────────┼────────────────────┼───────────┤
│           │     Data Service Layer                   │           │
│  ┌────────┴────────────────────┴────────────────────┴────────┐  │
│  │              DataService / TypedDataService                │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │           SoqlBuilder + SoqlCondition               │  │  │
│  │  │         (Type-Safe Query Construction)              │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                    Security Utilities Layer                      │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                    SecurityUtils                             ││
│  │  • SanitizeForSoql()     • IsValidSalesforceId()            ││
│  │  • IsValidObjectName()   • IsValidFieldName()               ││
│  │  • IsLocalUrl()          • IsAllowedExtension()             ││
│  └─────────────────────────────────────────────────────────────┘│
├─────────────────────────────────────────────────────────────────┤
│                    Infrastructure Layer                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Token Provider │  │  Cache Provider │  │  HTTP Client    │  │
│  │  (JWT/OAuth)    │  │  (Memory/Redis) │  │  (Polly)        │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Security Principles

### 1. Defense in Depth
Multiple layers of security controls ensure that if one layer fails, others continue to protect the system.

### 2. Least Privilege
- Configure Salesforce Connected Apps with minimal required scopes
- Use Profile/Permission Set restrictions in Salesforce
- Grant only necessary CRUD permissions per object

### 3. Secure by Default
All security features are **enabled by default**:

```csharp
public class SalesforceOptions
{
    public bool EnforceFieldLevelSecurity { get; set; } = true;  // Default: ON
    public bool ValidateSoqlInputs { get; set; } = true;         // Default: ON (basic raw SOQL validation)
    public bool ForceSecureCookie { get; set; } = true;          // Default: ON
    public string SessionCookieName { get; set; } = "__Host-SalesforceSession"; // Secure prefix
}
```

### 4. Fail Secure
When security checks fail, the system denies access rather than allowing it:

```csharp
// VisibilityService fails securely - unknown policy = hidden
if (!config.Policies.TryGetValue(policyName, out var policy))
{
    _logger.LogWarning("Policy '{PolicyName}' not found. Defaulting to Hidden.", policyName);
    return false; // DENY access
}
```

---

## SOQL Injection Prevention

### The Threat

SOQL injection is similar to SQL injection - attackers can manipulate queries to access unauthorized data:

```sql
-- Intended query
SELECT Id, Name FROM Account WHERE Name = 'Acme'

-- Injected query (if unsanitized)
SELECT Id, Name FROM Account WHERE Name = '' OR Name LIKE '%' --'
```

### Type-Safe Query Building with SoqlCondition

**SalesforceCore provides complete SOQL injection protection through the `SoqlCondition` API:**

```csharp
// SAFE: All values are automatically sanitized
var condition = SoqlCondition.And(
    SoqlCondition.Equals("Industry", userInput),          // Sanitized
    SoqlCondition.Like("Name", $"%{searchTerm}%"),        // Sanitized
    SoqlCondition.In("Status", new[] { "Active", "Pending" })  // Sanitized
);

var results = await dataService.QueryPagedAsync("Account",
    fields: new[] { "Id", "Name", "Industry" },
    filter: condition);
```

### How Sanitization Works

The `SecurityUtils.SanitizeForSoql()` method escapes dangerous characters:

```csharp
public static string SanitizeForSoql(string? input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;

    // SOQL uses doubled single quotes for escaping (like SQL)
    return input.Replace("'", "''");
}
```

**Before sanitization:** `Smith' OR Name LIKE '%' --`
**After sanitization:** `Smith'' OR Name LIKE ''%'' --`

The escaped version is treated as a literal string, not as SOQL syntax.


### Field Name Validation

Field names are validated against a strict pattern to prevent injection via column names:

```csharp
public static bool IsValidFieldName(string? fieldName)
{
    if (string.IsNullOrEmpty(fieldName)) return false;

    // Must match: Letter followed by alphanumeric/underscore,
    // optionally with __c or __r suffix, supporting relationship paths
    return Regex.IsMatch(fieldName, @"^[A-Za-z][A-Za-z0-9_]*(__[cr])?(\.[A-Za-z][A-Za-z0-9_]*(__[cr])?)*$");
}
```

**Valid field names:**
- `Name`, `Account.Name`, `Owner.Profile.Name`
- `Custom_Field__c`, `Lookup__r.Name`

**Invalid field names (rejected):**
- `Name; DROP TABLE`
- `Field\nName`
- `../../../etc/passwd`

### SoqlBuilder Validation

`SoqlBuilder` validates all inputs before query construction:

```csharp
var query = SoqlBuilder.From("Account")    // Validated object name
    .Select("Id", "Name", "Industry")       // Each field validated
    .WhereCondition(SoqlCondition.Equals("Status", userInput))  // Value sanitized
    .OrderBy("Name")                        // Field validated
    .Limit(100)
    .Build();
```

**If invalid input is provided, an `ArgumentException` is thrown immediately** - preventing any malformed query from reaching Salesforce.

### Raw Query Safety

When using raw SOQL queries, **you are responsible for sanitization**. With `ValidateSoqlInputs = true`, `IDataService.QueryAsync` performs basic validation (SELECT-only, no comment tokens), but it does not sanitize values for you:

```csharp
// DANGEROUS - Direct string interpolation
var query = $"SELECT Id FROM Account WHERE Name = '{userInput}'"; // DON'T DO THIS

// SAFE - Use SecurityUtils
var safeName = SecurityUtils.SanitizeForSoql(userInput);
var query = $"SELECT Id FROM Account WHERE Name = '{safeName}'";

// BEST - Use SoqlBuilder instead
var query = SoqlBuilder.From("Account")
    .Select("Id")
    .WhereCondition(SoqlCondition.Equals("Name", userInput))
    .Build();
```

### Subquery Safety (IN / NOT IN)

When you need `IN (SELECT ...)` patterns, use `SoqlBuilder.WhereInSubquery` / `SoqlBuilder.WhereNotInSubquery` and build the subquery with `SoqlBuilder` so both the outer query and the subquery remain validated and sanitized:

```csharp
var permissionSetIds = SoqlBuilder.From("PermissionSetAssignment")
    .Select("PermissionSetId")
    .WhereEquals("AssigneeId", userId);

var query = SoqlBuilder.From("SetupEntityAccess")
    .Select("Id")
    .WhereInSubquery("ParentId", permissionSetIds)
    .Build();
```

### MVC Authorization (`[SalesforceAuthorize]`)

`SalesforceCore.AspNetCore` provides `[SalesforceAuthorize]` for server-side authorization checks (permission sets, profiles, custom permissions, and object-level CRUD). Internally it:
- Uses `IDataService` for access checks and constructs all SOQL via `SoqlBuilder`/`SoqlCondition` (no direct SOQL interpolation).
- Logs exceptions via `ILogger<SalesforceAuthorizeAttribute>` and avoids leaking raw exception messages in non-development environments.

---

## Authentication Security

### JWT Bearer Token Flow (Recommended for Server-to-Server)

```csharp
// appsettings.json:
// "Salesforce": { "ClientId": "...", "Domain": "https://login.salesforce.com" },
// "SalesforceJwt": { "Username": "integration@company.com", "PrivateKeyPath": "/secrets/salesforce.key" }
//
// Program.cs:
builder.Services.AddSalesforceCore(builder.Configuration);
// JwtTokenProvider is automatically selected when the SalesforceJwt section exists.
```

**JWT Security Best Practices:**
- Use RSA-2048 or higher for signing keys
- Store private keys in HSM or secure vault (Azure KeyVault, AWS Secrets Manager)
- Rotate keys at least annually
- Set short token expiration (1 hour recommended)

### PKCE OAuth Flow (Recommended for Web Apps)

```csharp
builder.Services.AddSalesforceCoreMvc(builder.Configuration);

// Optional but recommended for large auth tickets / multi-node deployments:
// store the authentication ticket server-side (IDistributedCache) to avoid cookie size limits.
builder.Services.AddDistributedMemoryCache(); // dev only; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);
```

**PKCE Security Features:**
- No client secret exposure in browser
- Code verifier prevents authorization code interception
- SHA256 code challenge method (S256)

### Token Refresh Security

Token refresh uses locking to prevent race conditions:
- Server-side flows (JWT / Client Credentials) use in-process striped locks to prevent duplicate refresh within a node.
- Web PKCE (cookie/OIDC) uses an optional distributed lock provider to prevent refresh storms across nodes.

```csharp
// SynchronizationService provides in-process striped locks
private readonly SemaphoreSlim[] _lockStripes = new SemaphoreSlim[32];

// Get lock for specific key
var lockObj = _syncService.GetLock($"token_refresh_{userId}");
await lockObj.WaitAsync(cancellationToken);
try
{
    // Refresh token safely
}
finally
{
    lockObj.Release();
}
```

For web-farm deployments using PKCE refresh tokens with rotation enabled, ensure refresh is serialized across servers:

```csharp
await using var refreshLock = await _distributedLockProvider.TryAcquireAsync(
    resourceName: $"sf_token_refresh:{sessionKey}",
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken);
```

### Proactive Token Refresh

`TokenRefreshBackgroundService` refreshes tokens before expiry for server-side flows (JWT / Client Credentials):

```csharp
// Refresh when token has less than 5 minutes remaining
if (token.ExpiresAt < DateTime.UtcNow.AddMinutes(TokenExpiryBufferMinutes))
{
    await RefreshTokenAsync(token);
}
```

---

## Field-Level Security (FLS)

### Automatic FLS Enforcement

When `EnforceFieldLevelSecurity = true` (default), all operations respect Salesforce FLS:

```csharp
// DataService filters read fields and write payloads using schema-based FLS
var readableFields = await _schemaService.SanitizeFieldListWithFlsAsync(
    "Account",
    new[] { "Id", "Name", "Owner.Name" },
    cancellationToken);

// Only readable fields are sent in SELECT, and only createable/updateable fields in writes.
```

### FLS in Practice

```csharp
// User tries to set a non-createable field
var data = new Dictionary<string, object?>
{
    ["Name"] = "Acme Corp",        // Createable: YES
    ["Industry"] = "Technology",    // Createable: YES
    ["SystemModstamp"] = DateTime.Now, // Createable: NO - filtered out
    ["CreatedById"] = "005xxx"      // Createable: NO - filtered out
};

// Only Name and Industry are sent to Salesforce
await dataService.CreateRecordAsync("Account", data);
```

### Query Field Filtering

Fields are also filtered on read operations:

```csharp
var record = await dataService.GetRecordAsync("Account", accountId);
// Only returns fields the user has read access to
```

If you disable FLS enforcement, SalesforceCore will pass fields through and rely on Salesforce to reject unauthorized access.

---

## Input Validation

### Salesforce ID Validation

```csharp
public static bool IsValidSalesforceId(string? id)
{
    if (string.IsNullOrEmpty(id)) return false;
    if (id.Length != 15 && id.Length != 18) return false;
    return Regex.IsMatch(id, "^[a-zA-Z0-9]+$");
}
```

**Valid IDs:** `001ABCDEFGHIJKL`, `001ABCDEFGHIJKLMNO`
**Invalid IDs:** `invalid`, `001' OR '1'='1`, `<script>alert(1)</script>`

### Object Name Validation

```csharp
public static bool IsValidObjectName(string? objectName)
{
    if (string.IsNullOrEmpty(objectName)) return false;

    // Standard or custom object: Account, Account__c, ns__Object__c
    return Regex.IsMatch(objectName, @"^[A-Za-z][A-Za-z0-9_]*(__c)?$") ||
           Regex.IsMatch(objectName, @"^[A-Za-z][A-Za-z0-9_]*__[A-Za-z][A-Za-z0-9_]*(__c)?$");
}
```

### URL Validation (Prevent Open Redirect)

```csharp
public static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return false;

    // Reject absolute URLs to external hosts
    if (url.StartsWith("//")) return false;  // Protocol-relative
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return false; // External absolute URL
    }

    // Reject dangerous schemes
    var lower = url.ToLowerInvariant();
    if (lower.StartsWith("javascript:")) return false;
    if (lower.StartsWith("data:")) return false;

    return true;
}
```

---

## Caching Security

### Cache Key Isolation

Different environments use prefixed keys to prevent cache poisoning:

```csharp
public class SalesforceOptions
{
    public string CacheKeyPrefix { get; set; } = "SF_";
}

// Production: PROD_SF_schema:Account
// Staging:    STAGING_SF_schema:Account
```

### Cache Stampede Prevention

Striped locking prevents multiple threads from calling the factory simultaneously:

```csharp
// 32 lock stripes for fine-grained locking
private readonly SemaphoreSlim[] _lockStripes = new SemaphoreSlim[32];

private SemaphoreSlim GetLockForKey(string key)
{
    var hash = (uint)key.GetHashCode();
    var index = (int)(hash & 0x1F);  // Mask for 32 stripes
    return _lockStripes[index];
}
```

### Distributed Cache Security

For multi-node deployments, use a real distributed lock provider for cross-node coordination (not `IDistributedCache` get/set “optimistic locks”, which are not atomic):

```csharp
await using var distributedLock = await _distributedLockProvider.TryAcquireAsync(
    resourceName: $"sf_cache:{cacheKey}",
    timeout: TimeSpan.FromSeconds(10),
    cancellationToken);

if (distributedLock == null)
{
    // Another node is already doing the work; re-check cache or degrade gracefully.
    return;
}

// We own the lock - safe to run the factory exactly once across the cluster.
```

---

## Token Management

### Secure Cookie Configuration

```csharp
public class SalesforceOptions
{
    // __Host- prefix enforces:
    // - Secure flag (HTTPS only)
    // - No Domain attribute (same-origin only)
    // - Path must be "/"
    public string SessionCookieName { get; set; } = "__Host-SalesforceSession";
    public bool ForceSecureCookie { get; set; } = true;
}
```

### Token Storage Best Practices

| Scenario | Recommended Storage | Rationale |
|----------|---------------------|-----------|
| Web Apps (Single node) | Cookie auth ticket | Simple; may hit cookie size limits if many tokens/claims |
| Web Apps (Multi-node) | Server-side auth tickets (`useServerSideSessions: true` + Redis) | Avoids cookie size limits; consistent across nodes (share Data Protection keys) |
| Background Workers (JWT/ClientCreds) | `ICacheProvider` (memory/distributed/SQL) | Cached tokens via `CachedToken` with proactive refresh |
| Custom login flows | SessionTokenProvider or DistributedCacheTokenProvider | Explicit token storage under your control |
| Mobile/SPA | Never store Salesforce tokens in the client | Use backend-for-frontend; keep tokens on the server |

Session timestamp: 2025-12-25T23:00:00Z

### Token Revocation

Always revoke tokens on logout:

```csharp
public async Task LogoutAsync()
{
    await _tokenProvider.RevokeTokenAsync();
}
```

---

## Network Security

### HTTP Client Security

```csharp
services.AddHttpClient("SalesforceClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // DO NOT disable certificate validation in production
        // ServerCertificateCustomValidationCallback = ... // NEVER do this
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

### Resilience Policies

```csharp
// Retry with exponential backoff
private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

// Circuit breaker
private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

---

## Logging & Audit

### What to Log

```csharp
// DO log: Operations, errors, access patterns
_logger.LogInformation("User {UserId} queried {Object} with {RecordCount} results",
    userId, objectName, results.Count);

// DON'T log: Tokens, secrets, PII
// NEVER: _logger.LogDebug("Token: {Token}", accessToken);
```

### Structured Logging Configuration

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "SalesforceCore": "Warning"  // Reduce verbosity in production
      }
    },
    "Enrich": ["FromLogContext", "WithMachineName"],
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/salesforce-.log",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

---

## File Upload Security

### Configuration

`MaxFileUploadSize` is a core option (`Salesforce:MaxFileUploadSize`). The upload allowlist is an MVC option (`SalesforceMvc:AllowedFileExtensions`).

```json
{
  "Salesforce": {
    "MaxFileUploadSize": 26214400
  },
  "SalesforceMvc": {
    "EnableFileUploads": true,
    "AllowedFileExtensions": [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg"]
  }
}
```

### Validation

```csharp
public static bool IsAllowedExtension(string filename, string[] allowedExtensions)
{
    if (string.IsNullOrEmpty(filename)) return false;

    var extension = Path.GetExtension(filename);
    return allowedExtensions.Any(ext =>
        ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
}
```

### Content Type Validation

```csharp
// Validate content type matches extension
var expectedContentType = GetExpectedContentType(extension);
if (file.ContentType != expectedContentType)
{
    throw new SecurityException("Content type mismatch");
}
```

---

## Configuration Security

### Secrets Management

**NEVER store secrets in configuration files:**

```csharp
// BAD - secrets in appsettings.json
{
    "Salesforce": {
        "ClientSecret": "actual-secret-here"  // DANGEROUS
    }
}

// GOOD - use environment variables or vault
services.AddSalesforceCore(options =>
{
    options.ClientId = Configuration["Salesforce:ClientId"];
    options.ClientSecret = await keyVault.GetSecretAsync("sf-client-secret");
});
```

### Configuration Validation

```csharp
services.AddOptions<SalesforceOptions>()
    .Bind(Configuration.GetSection("Salesforce"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Compliance Checklist

### Pre-Production Checklist

- [ ] **Secrets Management**
  - [ ] All secrets stored in secure vault (not config files)
  - [ ] Private keys secured with appropriate access controls
  - [ ] Key rotation schedule established

- [ ] **Network Security**
  - [ ] TLS 1.2+ enforced
  - [ ] HSTS enabled
  - [ ] Certificate validation enabled (no bypass)

- [ ] **Authentication**
  - [ ] Appropriate OAuth flow selected (PKCE for web, JWT for server)
  - [ ] Token expiration configured appropriately
  - [ ] Token revocation implemented

- [ ] **Authorization**
  - [ ] FLS enforcement enabled
  - [ ] Visibility policies configured
  - [ ] Least privilege Salesforce profiles/permission sets

- [ ] **Input Validation**
  - [ ] All user inputs validated
  - [ ] SOQL queries use SoqlBuilder/SoqlCondition
  - [ ] File uploads validated and scanned

- [ ] **Logging & Monitoring**
  - [ ] Audit logging enabled
  - [ ] No sensitive data in logs
  - [ ] Alerting configured for security events

- [ ] **Salesforce Configuration**
  - [ ] Connected App scopes minimized
  - [ ] IP restrictions configured
  - [ ] Refresh token policy reviewed

### Ongoing Security Tasks

- [ ] Regular security audits
- [ ] Dependency vulnerability scanning
- [ ] Penetration testing
- [ ] Key rotation
- [ ] Salesforce event log monitoring
- [ ] API limit monitoring

---

## Next Steps

- [Authentication Setup](02-Authentication.md)
- [Configuration Reference](03-Configuration.md)
- [Enterprise Integration Guide](14-Enterprise-Integration-Guide.md)
