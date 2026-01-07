# Authentication

Comprehensive guide to authentication flows in SalesforceCore - covering web applications, background services, and enterprise deployments requiring secure, scalable token management.

## Table of Contents

1. [Overview](#overview)
2. [Authentication Flows](#authentication-flows)
3. [PKCE OAuth Flow (Web Applications)](#pkce-oauth-flow-web-applications)
4. [JWT Bearer Flow (Server-to-Server)](#jwt-bearer-flow-server-to-server)
5. [Client Credentials Flow](#client-credentials-flow)
6. [Token Storage Strategies](#token-storage-strategies)
7. [Token Refresh Architecture](#token-refresh-architecture)
8. [Multi-Tenant Authentication](#multi-tenant-authentication)
9. [Security Best Practices](#security-best-practices)
10. [Troubleshooting](#troubleshooting)

---

## Overview

SalesforceCore implements three OAuth 2.0 authentication flows, each optimized for specific scenarios:

| Flow | Use Case | User Interaction | Token Refresh |
|------|----------|------------------|---------------|
| **PKCE** | Web applications, SPAs | Required (login page) | Automatic via refresh_token |
| **JWT Bearer** | Background workers, integrations | None | New assertion on expiry |
| **Client Credentials** | Server-to-server, headless | None | New token on expiry |

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Token Provider Architecture                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐  │
│  │ AspNetCore      │    │ JWT Token       │    │ Client Creds    │  │
│  │ TokenProvider   │    │ Provider        │    │ TokenProvider   │  │
│  │ (PKCE Flow)     │    │ (Server Flow)   │    │ (Server Flow)   │  │
│  └────────┬────────┘    └────────┬────────┘    └────────┬────────┘  │
│           │                      │                      │            │
│           └──────────────────────┼──────────────────────┘            │
│                                  ▼                                   │
│                    ┌─────────────────────────┐                       │
│                    │  ITokenProvider          │                       │
│                    │  Interface               │                       │
│                    └────────────┬────────────┘                       │
│                                 │                                    │
│           ┌─────────────────────┼─────────────────────┐             │
│           ▼                     ▼                     ▼             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐      │
│  │ ICacheProvider  │  │ ISynchronization│  │ Token Refresh   │      │
│  │ (Token Storage) │  │ Service (Locks) │  │ Background Svc  │      │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘      │
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

### Requirements

- **Connected App** with appropriate OAuth scopes enabled
- **Callback URL** configured for web applications
- **.NET 10.0** or later
- **Redis** (recommended for server-side auth tickets and shared caching in multi-node deployments)

---

## Authentication Flows

### Choosing the Right Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    Which flow should I use?                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  Is there a human user logging in?                               │
│       │                                                           │
│       ├── YES → Use PKCE OAuth Flow                              │
│       │         - Web apps, MVC, Blazor Server                   │
│       │         - Most secure for interactive apps               │
│       │                                                           │
│       └── NO → Is this a background service or worker?          │
│                │                                                  │
│                ├── YES → Do you have a certificate?              │
│                │         │                                        │
│                │         ├── YES → Use JWT Bearer Flow           │
│                │         │         - Most secure server-to-server │
│                │         │         - Pre-authorized users        │
│                │         │                                        │
│                │         └── NO → Use Client Credentials Flow    │
│                │                   - Simpler setup                │
│                │                   - Requires client secret       │
│                │                                                  │
│                └── NO → Evaluate your architecture               │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## PKCE OAuth Flow (Web Applications)

The **Proof Key for Code Exchange (PKCE)** flow is the recommended authentication method for web applications with user interaction. It provides enhanced security by eliminating the need to expose the client secret to the browser.

### How PKCE Works

```
┌─────────────┐                                              ┌─────────────┐
│   Browser   │                                              │  Salesforce │
└──────┬──────┘                                              └──────┬──────┘
       │                                                            │
       │  1. User clicks "Login with Salesforce"                   │
       │  ─────────────────────────────────►                       │
       │                                                            │
       │  2. Generate code_verifier + code_challenge               │
       │  (code_challenge = SHA256(code_verifier))                 │
       │                                                            │
       │  3. Redirect to Salesforce with code_challenge            │
       │  ─────────────────────────────────────────────────────────►
       │                                                            │
       │  4. User authenticates + authorizes                       │
       │  ◄─────────────────────────────────────────────────────────
       │                                                            │
       │  5. Redirect back with authorization_code                 │
       │  ◄─────────────────────────────────────────────────────────
       │                                                            │
       │  6. Exchange code + code_verifier for tokens              │
       │  ─────────────────────────────────────────────────────────►
       │                                                            │
       │  7. Receive access_token + refresh_token                  │
       │  ◄─────────────────────────────────────────────────────────
       │                                                            │
```

### Configuration

**Program.cs:**

```csharp
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register Salesforce Core with MVC integration
builder.Services.AddSalesforceCoreMvc(builder.Configuration);

// Add PKCE OAuth authentication.
// Recommended for production/multi-node deployments: store the auth ticket server-side
// to avoid cookie size limits (requires IDistributedCache: Redis recommended).
builder.Services.AddDistributedMemoryCache(); // dev only; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Enable Salesforce middleware
app.UseSalesforceCore();
app.MapSalesforceRoutes();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

**appsettings.json:**

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "CallbackPath": "/salesforce/callback",
    "ApiVersion": "v60.0",

    "ForceSecureCookie": true,
    "SessionCookieName": "__Host-SalesforceSession"
  }
}
```

### Production Configuration (Multi-Node)

For load-balanced deployments, use distributed auth ticket storage (server-side sessions):

```csharp
// Use Redis for distributed auth ticket storage (recommended for production)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SalesforceCore_";
});

// Enable server-side auth tickets (avoids cookie size limits)
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);
```

### Login/Logout Implementation

```csharp
[Controller]
public class AuthController : Controller
{
    // GET: /Auth/Login
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(returnUrl ?? "/");
        }

        // Store return URL for post-login redirect
        TempData["ReturnUrl"] = returnUrl;

        // Challenge triggers OAuth flow
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.Action("LoginCallback", "Auth")
        }, "Salesforce");
    }

    // GET: /Auth/LoginCallback
    [HttpGet]
    public IActionResult LoginCallback()
    {
        var returnUrl = TempData["ReturnUrl"]?.ToString() ?? "/";
        return Redirect(returnUrl);
    }

    // POST: /Auth/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout([FromServices] ITokenProvider tokenProvider)
    {
        // Revoke Salesforce token
        try
        {
            await tokenProvider.RevokeTokenAsync();
        }
        catch
        {
            // Best-effort revocation
        }

        // Sign out of ASP.NET Core
        await HttpContext.SignOutAsync();

        return RedirectToAction("Index", "Home");
    }
}
```

---

## JWT Bearer Flow (Server-to-Server)

The **JWT Bearer** flow is designed for server-to-server integrations where no user interaction is possible. It uses a digitally signed JWT assertion to authenticate.

### How JWT Bearer Works

```
┌─────────────────┐                                    ┌─────────────────┐
│ Your Service    │                                    │   Salesforce    │
└────────┬────────┘                                    └────────┬────────┘
         │                                                      │
         │  1. Create JWT with:                                │
         │     - iss: Client ID                                │
         │     - sub: Username                                 │
         │     - aud: login.salesforce.com                     │
         │     - exp: Current time + 5 minutes                 │
         │                                                      │
         │  2. Sign JWT with private key                       │
         │                                                      │
         │  3. POST to /services/oauth2/token                  │
         │     grant_type=urn:ietf:params:oauth:               │
         │                grant-type:jwt-bearer                │
         │     assertion={signed_jwt}                          │
         │  ────────────────────────────────────────────────────►
         │                                                      │
         │  4. Salesforce validates JWT signature               │
         │     using uploaded certificate                       │
         │                                                      │
         │  5. Returns access_token + instance_url              │
         │  ◄────────────────────────────────────────────────────
         │                                                      │
```

### Prerequisites

1. **Generate RSA Key Pair** (minimum 2048-bit):

```bash
# Generate private key
openssl genrsa -out salesforce_private.key 2048

# Generate self-signed certificate (valid for 1 year)
openssl req -new -x509 -sha256 \
    -key salesforce_private.key \
    -out salesforce_certificate.crt \
    -days 365 \
    -subj "/CN=SalesforceIntegration/O=YourCompany"
```

2. **Upload Certificate to Connected App**:
   - Edit your Connected App
   - Check "Use digital signatures"
   - Upload `salesforce_certificate.crt`

3. **Pre-authorize Users**:
   - Set "Permitted Users" to "Admin approved users are pre-authorized"
   - Add specific users or profiles

### Configuration

**Program.cs:**

```csharp
using SalesforceCore.Extensions;
using SalesforceCore.Services.Core;

var builder = Host.CreateApplicationBuilder(args);

// Add Salesforce Core services
builder.Services.AddSalesforceCore(builder.Configuration);

// JWT Token Provider is automatically registered when SalesforceJwt config exists
// Add distributed cache for shared caching across instances
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Add your worker service
builder.Services.AddHostedService<SalesforceIntegrationWorker>();

var host = builder.Build();
host.Run();
```

**appsettings.json:**

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0"
  },

  "SalesforceJwt": {
    "Username": "integration-user@company.com",
    "PrivateKeyPath": "/etc/secrets/salesforce_private.key",
    "Audience": "https://login.salesforce.com",
    "TokenExpiration": "00:05:00"
  },

  "ConnectionStrings": {
    "Redis": "localhost:6379,abortConnect=false"
  }
}
```

### Private Key from Azure Key Vault

For production, store the private key securely:

```csharp
// Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-vault.vault.azure.net/"),
    new DefaultAzureCredential());

// The key will be available as:
// SalesforceJwt:PrivateKey (as a string value from Key Vault secret)
```

**appsettings.json:**

```json
{
  "SalesforceJwt": {
    "Username": "integration-user@company.com",
    "PrivateKey": "", // Will be overridden by Key Vault
    "Audience": "https://login.salesforce.com"
  }
}
```

### Worker Service Example

```csharp
public class SalesforceIntegrationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SalesforceIntegrationWorker> _logger;

    public SalesforceIntegrationWorker(
        IServiceProvider services,
        ILogger<SalesforceIntegrationWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Salesforce Integration Worker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dataService = scope.ServiceProvider
                    .GetRequiredService<ITypedDataService>();

                // Query Salesforce data
                var accounts = await dataService.Query<Account>()
                    .Where(a => a.LastModifiedDate > DateTime.UtcNow.AddHours(-1))
                    .Take(100)
                    .ToListAsync(stoppingToken);

                _logger.LogInformation(
                    "Synced {Count} accounts modified in last hour",
                    accounts.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Salesforce sync");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## Client Credentials Flow

The **Client Credentials** flow is the simplest server-to-server authentication method, using only the client ID and secret.

### Prerequisites

1. **Enable Client Credentials in Connected App**:
   - Edit your Connected App
   - Check "Enable Client Credentials Flow"
   - Assign a "Run As" user with appropriate permissions

2. **Configure OAuth Policies**:
   - Set refresh token policy as needed
   - Configure IP restrictions for security

### Configuration

**appsettings.json:**

```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "ClientSecret": "YOUR_CONNECTED_APP_CONSUMER_SECRET",
    "Domain": "https://login.salesforce.com",
    "ApiVersion": "v60.0"
  },
  "SalesforceClientCredentials": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "ClientSecret": "YOUR_CONNECTED_APP_CONSUMER_SECRET"
  }
}
```

**User Secrets (Development):**

```bash
dotnet user-secrets set "Salesforce:ClientSecret" "YOUR_CONSUMER_SECRET"
```

**Environment Variables (Production):**

```bash
export Salesforce__ClientSecret=YOUR_CONSUMER_SECRET
```

**Program.cs:**

```csharp
builder.Services.AddSalesforceCore(builder.Configuration);

// If SalesforceClientCredentials section exists and no ITokenProvider is registered,
// ClientCredentialsTokenProvider is selected. It falls back to Salesforce:ClientId/ClientSecret
// when SalesforceClientCredentials values are not provided.
```

---

## Token Storage Strategies

### Storage Options Comparison

| Strategy | Primary Use Case | What Is Stored | Multi-Node |
|----------|------------------|----------------|-----------|
| **Cookie auth ticket (default)** | Dev/simple web apps | Auth ticket containing tokens/claims | Yes, but can hit cookie size limits |
| **Server-side auth tickets (`useServerSideSessions`)** | Production web apps | Auth ticket in `IDistributedCache` + small reference cookie | Yes (recommended; share Data Protection keys) |
| **JWT/Client Credentials providers** | Headless services | `CachedToken` via `ICacheProvider` | Yes if cache provider is distributed/SQL |
| **SessionTokenProvider / DistributedCacheTokenProvider** | Custom login flows | Tokens stored in Session or `IDistributedCache` | Yes for distributed cache |

Session timestamp: 2025-12-25T23:00:00Z

### Cookie-Based Storage (Default for Web)

```csharp
// This is the default when using AddSalesforceAuthentication
builder.Services.AddSalesforceAuthentication(builder.Configuration);
```

Tokens are stored in the ASP.NET Core authentication ticket because `SaveTokens = true` is enabled in `AddSalesforceAuthentication`.
The instance URL is persisted into auth properties by `OnTokenResponseReceived`.

```json
{
  "Salesforce": {
    "SessionCookieName": "__Host-SalesforceSession",
    "ForceSecureCookie": true
  }
}
```

### Server-Side Auth Tickets (Recommended for Production)

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379,abortConnect=false";
    options.InstanceName = "SalesforceCore_";
});

builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);
```

Notes:
- This stores the authentication ticket in `IDistributedCache` to avoid cookie size limits.
- The ticket is stored by `DistributedCacheTicketStore` with key prefix `SalesforceAuth:` when enabled.
- In multi-node deployments, share ASP.NET Core Data Protection keys across nodes.
- Session timestamp: 2025-12-25T23:00:00Z

### Custom Token Stores (Advanced)

`AddSalesforceSessionTokenStorage` and `AddSalesforceDistributedCacheTokenStorage` register token providers that store tokens in ASP.NET Session or `IDistributedCache` respectively. These are intended for custom login flows where your code explicitly calls `SetTokensAsync` or `SetTokens`.

For complete, code-backed storage details and cache keys, see `Tokens.MD` (Session: 2025-12-22T18:45:00Z).

---

## Token Refresh Architecture

SalesforceCore implements a robust token refresh mechanism with the following features:

### Proactive Background Refresh

The `TokenRefreshBackgroundService` proactively refreshes tokens **for server-side flows** (JWT Bearer, Client Credentials) before they expire.
It is not used for the PKCE cookie/OIDC flow because that token state is request/user-specific and is refreshed reactively.

```
┌─────────────────────────────────────────────────────────────────┐
│                Token Refresh Timeline                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  Token Issued        Buffer (5 min)      Token Expires           │
│      │                    │                    │                 │
│      ├────────────────────┼────────────────────┤                 │
│      │                    │                    │                 │
│      │    Token Valid     │  Refresh Window    │   Expired       │
│      │                    │                    │                 │
│                           ▲                                       │
│                           │                                       │
│               Background service checks every 1 min               │
│               Refreshes when entering buffer window               │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

### Race Condition Prevention

For server-side flows (JWT Bearer / Client Credentials), `ISynchronizationService` uses striped in-process locks to prevent multiple threads from refreshing simultaneously:

```csharp
// 32 lock stripes for fine-grained concurrency control
private readonly SemaphoreSlim[] _lockStripes = new SemaphoreSlim[32];

// Get lock for specific key using hash
var lockIndex = (uint)key.GetHashCode() & 0x1F; // Mask for 32 stripes
var lockObj = _lockStripes[lockIndex];

await lockObj.WaitAsync(cancellationToken);
try
{
    // Refresh token
}
finally
{
    lockObj.Release();
}
```

### Web Apps (PKCE + Cookies) Refresh (Multi-Node Safe)

For the PKCE cookie/OIDC flow (`AddSalesforceAuthentication`), refresh is handled reactively by the web token provider:

- An in-process lock prevents duplicate refresh within a single node.
- If an `IDistributedLockProvider` is available, a distributed lock prevents cross-node refresh storms (critical when refresh token rotation is enabled).
- If `IDistributedCache` is available, a short-lived “refresh snapshot” can be published so other nodes handling concurrent in-flight requests can pick up the refreshed token without attempting a second refresh.

Notes for multi-node:
- Use server-side auth tickets (`useServerSideSessions: true`) so the authentication ticket is the shared token source-of-truth.
- Share ASP.NET Core Data Protection keys across nodes.

### Distributed Lock for Multi-Node

In distributed environments, additional locking prevents multiple nodes from refreshing the same token concurrently.

SalesforceCore uses an `IDistributedLockProvider` abstraction for cross-node coordination (for example, SQL Server application locks via `sp_getapplock`).

```csharp
// Attempt to acquire a distributed lock for this session/user.
// sessionKey should be stable across refreshes (e.g., auth property "sf_session_id").
await using var refreshLock = await _distributedLockProvider.TryAcquireAsync(
    resourceName: $"sf_token_refresh:{sessionKey}",
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken);

if (refreshLock == null)
{
    // Another node is already refreshing; re-check current token state (or read a coordinator snapshot if configured).
    return;
}

// We own the lock - safe to refresh exactly once across the cluster.
```

---

## Multi-Tenant Authentication

For applications serving multiple Salesforce orgs:

### Tenant Resolution

```csharp
public interface ITenantResolver
{
    Task<TenantContext?> ResolveTenantAsync(HttpContext context);
}

public class HostBasedTenantResolver : ITenantResolver
{
    public async Task<TenantContext?> ResolveTenantAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;

        // tenant1.app.com → tenant1
        var tenantId = host.Split('.').First();

        return await LoadTenantConfig(tenantId);
    }
}
```

### Per-Tenant Token Storage

```csharp
public class TenantAwareTokenStorage : ITokenStorage
{
    private readonly ITenantResolver _tenantResolver;
    private readonly IDistributedCache _cache;

    public async Task<TokenInfo?> GetTokenAsync(CancellationToken ct = default)
    {
        var tenant = await _tenantResolver.GetCurrentTenantAsync();
        var key = $"tokens:{tenant.Id}:{tenant.UserId}";

        return await _cache.GetAsync<TokenInfo>(key, ct);
    }
}
```

---

## Security Best Practices

### 1. Never Store Secrets in Code or Config Files

```csharp
// BAD - secrets in appsettings.json
"ClientSecret": "actual-secret-here"

// GOOD - use User Secrets for development
dotnet user-secrets set "Salesforce:ClientSecret" "your-secret"

// GOOD - use environment variables for production
export Salesforce__ClientSecret=your-secret

// BEST - use secure vault
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

### 2. Use Secure Cookie Settings

```json
{
  "Salesforce": {
    "SessionCookieName": "__Host-SalesforceSession",
    "ForceSecureCookie": true
  }
}
```

The `__Host-` prefix enforces:
- `Secure` flag (HTTPS only)
- No `Domain` attribute (same-origin only)
- `Path` must be `/`

### 3. Implement Token Revocation on Logout

```csharp
public async Task LogoutAsync(ITokenProvider tokenProvider)
{
    try
    {
        await tokenProvider.RevokeTokenAsync();
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Token revocation failed");
        // Continue with local logout
    }

    // Clear local session/cookies
    await HttpContext.SignOutAsync();
}
```

### 4. Rotate Keys Regularly

- Rotate JWT signing keys at least annually
- Rotate Connected App client secrets quarterly
- Use automated key rotation where possible

### 5. Monitor for Suspicious Activity

```csharp
// Log authentication events
_logger.LogInformation(
    "User {UserId} authenticated from {IpAddress} at {Timestamp}",
    userId,
    HttpContext.Connection.RemoteIpAddress,
    DateTime.UtcNow);

// Alert on unusual patterns
if (failedAttempts > 5)
{
    _alertService.SendSecurityAlert($"Multiple failed login attempts for {userId}");
}
```

---

## Troubleshooting

### Common Errors

| Error | Cause | Solution |
|-------|-------|----------|
| `invalid_client` | Wrong Client ID | Verify Consumer Key in Connected App |
| `invalid_client_id` | Client ID not found | Check Connected App is active |
| `invalid_grant` | Token expired/revoked | Re-authenticate user |
| `invalid_grant: user hasn't approved this consumer` | User not pre-authorized | Add user to Connected App |
| `invalid_grant: jwt audience invalid` | Wrong audience URL | Use correct domain (login vs test) |
| `INVALID_SESSION_ID` | Session expired | Implement token refresh |
| `expired access/refresh token` | Refresh token expired | User must re-authenticate |

### Enable Debug Logging

```json
{
  "Logging": {
    "LogLevel": {
      "SalesforceCore": "Debug",
      "SalesforceCore.Services.Core": "Trace"
    }
  },
  "Salesforce": {
    "EnableDebugLogging": true
  }
}
```

### Verify Token Provider is Registered

```csharp
// In a controller or service, inject and check
public class DiagnosticsController : Controller
{
    private readonly ITokenProvider _tokenProvider;

    public DiagnosticsController(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    [HttpGet("auth/status")]
    public async Task<IActionResult> GetAuthStatus()
    {
        try
        {
            var token = await _tokenProvider.GetTokenAsync();
            return Ok(new
            {
                authenticated = token != null,
                tokenType = _tokenProvider.GetType().Name,
                instanceUrl = token?.InstanceUrl
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                authenticated = false,
                error = ex.Message
            });
        }
    }
}
```

### JWT Debugging

```csharp
// Decode JWT to inspect claims (for debugging only)
var parts = jwt.Split('.');
var header = Base64UrlDecode(parts[0]);
var payload = Base64UrlDecode(parts[1]);

Console.WriteLine($"Header: {header}");
Console.WriteLine($"Payload: {payload}");
```

---

## Next Steps

- **Configuration Reference**: [03-Configuration.md](03-Configuration.md) - All configuration options
- **Security Guide**: [09-Security.md](09-Security.md) - Security best practices
- **Data Service**: [04-Data-Service.md](04-Data-Service.md) - CRUD operations
- **Enterprise Guide**: [14-Enterprise-Integration-Guide.md](14-Enterprise-Integration-Guide.md) - Production deployments
